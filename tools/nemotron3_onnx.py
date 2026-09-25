"""Nemotron-3-Diarization em numpy + onnxruntime, sem torch (25/09/2026).

Porta de ``Nemotron3DiarizationForAudioFrameClassification.forward`` e de
``Nemotron3DiarizationSpeakerCache`` do ``transformers``, em volta dos dois
grafos que ``tools/exportar_nemotron3_onnx.py`` gera em ``tools/_nemotron3/``.
É o formato do sidecar de diarização: onnxruntime-gpu e numpy, nada mais.

**Offline e streaming são o MESMO laço.** O "offline" do modelo também anda em
blocos (340 quadros de 80 ms, 40 de contexto) com o cache de falantes; o
streaming só encolhe o bloco e aumenta o FIFO. Por isso ``MODOS`` é uma tabela,
e não dois caminhos.

**O mel sai igual por bloco e de uma vez só** — o processor do ``transformers``
garante isso com ``center=False`` depois do primeiro bloco —, então medir o
streaming sobre o arquivo inteiro é exato, e não uma aproximação.

A régua do porte é ``tools/medir_nemotron3.py validar``: 100% das decisões por
quadro iguais ao ``transformers``, offline e streaming. Ver
docs/NEMOTRON-DIARIZACAO.md.
"""
from __future__ import annotations

import math
from pathlib import Path

import numpy as np
import onnxruntime as ort

HOP, NFFT, WIN, SUB, NSPK = 160, 512, 400, 8, 8

# (bloco, contexto à direita, fifo, período de atualização), em quadros de 80 ms
MODOS = {
    "offline": (340, 40, 40, 300),
    "low_latency": (9, 4, 264, 222),
    "very_low_latency": (6, 2, 264, 222),
    "ultra_low_latency": (3, 1, 264, 222),
}
CACHE, SIL = 264, 1
LIMIAR, BOOST_RECENTE = 0.25, 0.05
_ORC = CACHE // NSPK - SIL
MIN_POS = math.floor(_ORC * 0.5)
FORTES, FRACOS = math.floor(_ORC * 0.75), math.floor(_ORC * 1.5)


def sigmoid(x):
    return 1.0 / (1.0 + np.exp(-x))


def mel(audio: np.ndarray, filtros: np.ndarray) -> np.ndarray:
    """(N, 128) — pré-ênfase, STFT centrada, potência, mel slaney, log(x + 2^-24)."""
    x = np.concatenate([audio[:1], audio[1:] - 0.97 * audio[:-1]]).astype(np.float32)
    n = len(x) // HOP
    x = np.pad(x, NFFT // 2)
    janela = np.zeros(NFFT, np.float32)
    off = (NFFT - WIN) // 2
    janela[off:off + WIN] = np.hanning(WIN)  # simétrica, como hann_window(periodic=False)
    quadros = np.lib.stride_tricks.sliding_window_view(x, NFFT)[::HOP][:n] * janela
    pot = np.abs(np.fft.rfft(quadros, axis=1)) ** 2
    return np.log(pot.astype(np.float32) @ filtros.T + 2.0 ** -24).astype(np.float32)


class CacheDeFalantes:
    def __init__(self, fifo: int, periodo: int, silencio: np.ndarray):
        self.fifo_max, self.periodo, self.sil = fifo, periodo, silencio
        d = silencio.shape[0]
        self.embeds = np.zeros((0, d), np.float32)
        self.probs = np.zeros((0, NSPK), np.float32)
        self.fifo = np.zeros((0, d), np.float32)
        self.comprimido = False

    def anteriores(self) -> np.ndarray:
        return np.concatenate([self.embeds, self.fifo])

    def atualizar(self, entrada: np.ndarray, logits: np.ndarray, n_bloco: int):
        nc, nf = len(self.embeds), len(self.fifo)
        t = len(entrada)
        probs = sigmoid(logits[: t * SUB]).reshape(t, SUB, NSPK).mean(1)
        bloco = entrada[nc + nf: nc + nf + n_bloco]
        fifo = np.concatenate([self.fifo, bloco])
        k = len(fifo)
        pop = 0 if k <= self.fifo_max else min(max(self.periodo, k - self.fifo_max), k)
        if pop:
            fifo_p = probs[nc: nc + k]
            guardadas = self.probs if self.comprimido else probs[:nc]
            ce = np.concatenate([self.embeds, fifo[:pop]])
            cp = np.concatenate([guardadas, fifo_p[:pop]])
            fifo = fifo[pop:]
            if len(ce) > CACHE:
                ce, cp = self._comprimir(ce, cp)
                self.comprimido = True
            self.embeds, self.probs = ce, cp.astype(np.float32)
        self.fifo = fifo

    @staticmethod
    def _pontuar(p):
        lp = np.log(np.maximum(p, LIMIAR))
        lc = np.log(np.maximum(1.0 - p, LIMIAR))
        s = lp - lc + lc.sum(-1, keepdims=True) - math.log(0.5)
        fala = p > 0.5
        s = np.where(fala, s, -np.inf)
        pos = s > 0
        basta = pos.sum(0, keepdims=True) >= MIN_POS
        return np.where(~pos & fala & basta, -np.inf, s)

    @staticmethod
    def _reforcar(s, n, b):
        idx = np.argpartition(-s, n - 1, axis=0)[:n]  # top-n quadros por falante
        np.add.at(s, (idx, np.arange(s.shape[1])[None, :].repeat(n, 0)), b)
        return s

    def _comprimir(self, e, p):
        f = len(p)
        s = self._pontuar(p)
        s[CACHE:] += BOOST_RECENTE
        s = self._reforcar(s, FORTES, -2.0 * math.log(0.5))
        s = self._reforcar(s, FRACOS, -math.log(0.5))
        s = np.concatenate([s, np.full((SIL, NSPK), np.inf)])
        e = np.concatenate([e, self.sil[None]])
        p = np.concatenate([p, np.zeros((1, NSPK), p.dtype)])
        nsc = f + SIL
        plano = s.T.reshape(-1)
        idx = np.argpartition(-plano, CACHE - 1)[:CACHE]
        sentinela = nsc * NSPK
        idx = np.where(plano[idx] == -np.inf, sentinela, idx)
        idx = np.sort(idx)
        q = np.where(idx == sentinela, f, np.minimum(idx % nsc, f))
        return e[q], p[q]


class Nemotron3:
    def __init__(self, pasta: Path, gpu: bool = True):
        pr = (["CUDAExecutionProvider"] if gpu else []) + ["CPUExecutionProvider"]
        self.embed = ort.InferenceSession(str(pasta / "embed.onnx"), providers=pr)
        self.step = ort.InferenceSession(str(pasta / "step.onnx"), providers=pr)
        self.filtros = np.load(pasta / "mel.npy")
        self.sil = np.load(pasta / "silencio.npy")

    def logits(self, audio: np.ndarray, modo: str = "offline") -> np.ndarray:
        """(N, 8) logits a 10 ms, falantes em ordem de chegada."""
        bl, cd, fifo, per = MODOS[modo]
        feats = mel(audio, self.filtros)
        n = len(feats)
        emb = self.embed.run(None, {"features": feats[None]})[0][0]
        ne = len(emb)
        cache = CacheDeFalantes(fifo, per, self.sil)
        saida = []
        for ini in range(0, ne, bl):
            fim = min(ini + bl, ne)
            pedaco = emb[ini: min(fim + cd, ne)]
            ant = cache.anteriores()
            entrada = np.concatenate([ant, pedaco])
            lg = self.step.run(None, {"embeds": entrada[None]})[0][0]
            cache.atualizar(entrada, lg, fim - ini)
            saida.append(lg[len(ant) * SUB: (len(ant) + fim - ini) * SUB])
        return np.concatenate(saida)[:n]


def segmentos(logits: np.ndarray, limiar: float = 0.5) -> list[dict]:
    ativo = (sigmoid(logits) > limiar).astype(np.int8)
    borda = np.zeros((1, ativo.shape[1]), np.int8)
    mud = np.diff(np.concatenate([borda, ativo, borda]), axis=0)
    segs = []
    for f in range(ativo.shape[1]):
        for a, b in zip(np.nonzero(mud[:, f] == 1)[0], np.nonzero(mud[:, f] == -1)[0]):
            segs.append({"Start": round(a * 0.01, 2), "End": round(b * 0.01, 2), "Speaker": f})
    return sorted(segs, key=lambda s: (s["Start"], s["Speaker"]))
