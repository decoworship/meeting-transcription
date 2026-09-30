"""Nemotron-3-Diarization em numpy + onnxruntime, sem torch (25/09/2026).

**O segundo diarizador do sidecar desde 29/09/2026** (MOD-2 do docs/BACKLOG.md),
ao lado do pyannote e escolhido como um modelo a mais em ``modelos/`` — a pasta
``nemotron-3``. Medido do jeito que o app monta em
docs/NEMOTRON-DIARIZACAO.md §3b: separa melhor, 18x mais rápido, ~0,55 GB.

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

from sessao import abrir

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
    #: Embeds de contexto à esquerda de cada bloco, descartados. Com 8, o embed
    #: por bloco decide 100% igual ao da gravação inteira (tools/medir_mod1.py).
    CONTEXTO_ESQ = 8

    def __init__(self, pasta: Path, gpu: bool = True):
        pasta = Path(pasta)
        # sessao.abrir e não ort.InferenceSession: é ele que monta o PATH das
        # DLLs de CUDA no Windows e cai para CPU quando o provedor morre no meio
        # (o SUP-2). O mesmo caminho do pyannote.
        self.embed, self.provedor = abrir(pasta / "embed.onnx", gpu)
        self.step, _ = abrir(pasta / "step.onnx", gpu)
        self.filtros = np.load(pasta / "mel.npy")
        self.sil = np.load(pasta / "silencio.npy")

    def logits(self, audio: np.ndarray, modo: str = "offline") -> np.ndarray:
        """(N, 8) logits a 10 ms, falantes em ordem de chegada.

        O embed da gravação inteira de uma vez — a referência das ferramentas
        de medição. A VRAM dele cresce com a duração; o sidecar usa
        :meth:`logits_por_bloco`.
        """
        bl, cd, fifo, per = MODOS[modo]
        feats = mel(audio, self.filtros)
        n = len(feats)
        emb = self.embed.run(None, {"features": feats[None]})[0][0]
        return self._laco(lambda ini, fim: emb[ini:fim], len(emb), modo)[:n]

    def logits_por_bloco(self, audio: np.ndarray, modo: str = "offline") -> np.ndarray:
        """Os mesmos logits, com o mel e o embed calculados bloco a bloco.

        É o que um sidecar pode fazer — e o que faz o pico de VRAM ficar no dos
        pesos (~0,55 GB), e não crescer com a reunião.
        """
        n = len(audio) // HOP
        ne = int(len(audio) / 16000 / 0.08)

        def trecho(ini, fim):
            e0 = max(0, ini - self.CONTEXTO_ESQ)
            a0, a1 = e0 * SUB * HOP, min(len(audio), fim * SUB * HOP)
            emb = self.embed.run(None, {"features": mel(audio[a0:a1], self.filtros)[None]})[0][0]
            return emb[ini - e0: ini - e0 + (fim - ini)]

        return self._laco(trecho, ne, modo)[:n]

    def _laco(self, trecho, ne: int, modo: str) -> np.ndarray:
        bl, cd, fifo, per = MODOS[modo]
        cache = CacheDeFalantes(fifo, per, self.sil)
        saida = []
        for ini in range(0, ne, bl):
            fim = min(ini + bl, ne)
            pedaco = trecho(ini, min(fim + cd, ne))
            ant = cache.anteriores()
            entrada = np.concatenate([ant, pedaco])
            lg = self.step.run(None, {"embeds": entrada[None]})[0][0]
            cache.atualizar(entrada, lg, fim - ini)
            saida.append(lg[len(ant) * SUB: (len(ant) + fim - ini) * SUB])
        if not saida:
            return np.zeros((0, NSPK), np.float32)
        return np.concatenate(saida)

    def __call__(self, onda: np.ndarray, taxa: int = 16000) -> list[dict]:
        """A forma que o núcleo espera — a mesma do ``Diarizador`` do pyannote.

        ``[{"inicio", "fim", "falante"}]``, com rótulos ``SPEAKER_0k`` como os
        dele: nomear é apresentação e vive no núcleo.
        """
        if taxa != 16000:
            raise RuntimeError(f"o Nemotron-3 espera 16000 Hz, o áudio veio a {taxa} Hz")
        if len(onda) < 16000:
            return []
        return [{"inicio": s["Start"], "fim": s["End"], "falante": f"SPEAKER_{s['Speaker']:02d}"}
                for s in segmentos(self.logits_por_bloco(onda, "offline"))]


class FluxoAoVivo:
    """O Nemotron-3 alimentado aos poucos, para a legenda ao vivo (30/09/2026).

    Recebe o ``system.wav`` em pedaços de qualquer tamanho e processa um bloco
    sempre que o áudio dele e o contexto à direita já chegaram. É o mesmo laço
    do :meth:`Nemotron3.logits_por_bloco` — mesmo mel por trecho, mesmo cache —,
    só que dirigido pelo áudio que chega em vez de pelo arquivo inteiro.

    **Junta a fala limpa de cada vaga**, porque é dela que sai o nome: o núcleo
    reconhece a voz com o mesmo ``Vozes.Reconhecer`` da passada final. Uma vaga
    manda o áudio limpo quando cruza cada marca de :attr:`MARCAS_S` — a primeira
    cedo, para o nome aparecer logo, e a segunda com fala bastante para corrigir
    um primeiro palpite ruim.
    """

    AMOSTRAS_POR_EMBED = SUB * HOP          # 1280: um embed = 80 ms
    MARCAS_S = (6.0, 20.0)

    def __init__(self, eng: "Nemotron3", modo: str = "low_latency"):
        self.eng = eng
        self.bl, self.cd, fifo, per = MODOS[modo]
        self.cache = CacheDeFalantes(fifo, per, eng.sil)
        self._audio = np.zeros(0, np.float32)
        self._base = 0          # índice do embed que _audio[0] representa
        self._prox = 0          # próximo embed a decidir
        self._limpo: dict[int, list[np.ndarray]] = {}
        self._marcas: dict[int, int] = {}

    def alimentar(self, pcm: np.ndarray) -> tuple[list[tuple[int, int, int]], list[tuple[int, np.ndarray]]]:
        """``(ativos, prontos)``: trechos ``(inicio_ms, fim_ms, vaga)`` recém
        decididos, e ``(vaga, audio)`` das vagas que cruzaram uma marca."""
        self._audio = np.concatenate([self._audio, pcm.astype(np.float32)])
        total = self._base + len(self._audio) // self.AMOSTRAS_POR_EMBED
        ativos, prontos = [], []
        while self._prox + self.bl + self.cd <= total:
            ini, fim = self._prox, self._prox + self.bl
            e0 = max(0, ini - Nemotron3.CONTEXTO_ESQ)
            a0 = (e0 - self._base) * self.AMOSTRAS_POR_EMBED
            a1 = (fim + self.cd - self._base) * self.AMOSTRAS_POR_EMBED
            feats = mel(self._audio[a0:a1], self.eng.filtros)
            emb = self.eng.embed.run(None, {"features": feats[None]})[0][0]
            pedaco = emb[ini - e0: ini - e0 + self.bl + self.cd]
            ant = self.cache.anteriores()
            entrada = np.concatenate([ant, pedaco])
            lg = self.eng.step.run(None, {"embeds": entrada[None]})[0][0]
            self.cache.atualizar(entrada, lg, self.bl)
            ativo = sigmoid(lg[len(ant) * SUB: (len(ant) + self.bl) * SUB]) > 0.5
            ativos += self._trechos(ativo, ini * 80)
            prontos += self._juntar_limpo(ativo, (ini - self._base) * self.AMOSTRAS_POR_EMBED)
            self._prox = fim
            # Guarda só o contexto à esquerda do próximo bloco.
            corte = max(0, self._prox - Nemotron3.CONTEXTO_ESQ) - self._base
            if corte > 0:
                self._audio = self._audio[corte * self.AMOSTRAS_POR_EMBED:]
                self._base += corte
        return ativos, prontos

    @staticmethod
    def _trechos(ativo: np.ndarray, inicio_ms: int) -> list[tuple[int, int, int]]:
        borda = np.zeros((1, ativo.shape[1]), np.int8)
        mud = np.diff(np.concatenate([borda, ativo.astype(np.int8), borda]), axis=0)
        out = []
        for f in range(ativo.shape[1]):
            for a, b in zip(np.nonzero(mud[:, f] == 1)[0], np.nonzero(mud[:, f] == -1)[0]):
                out.append((inicio_ms + int(a) * 10, inicio_ms + int(b) * 10, f))
        return out

    def _juntar_limpo(self, ativo: np.ndarray, a0: int) -> list[tuple[int, np.ndarray]]:
        prontos = []
        so_um = ativo.sum(1) == 1
        for q in np.nonzero(so_um)[0]:
            f = int(ativo[q].argmax())
            if self._marcas.get(f, 0) >= len(self.MARCAS_S):
                continue  # já disse o que tinha a dizer
            pedaco = self._audio[a0 + q * HOP: a0 + (q + 1) * HOP]
            if len(pedaco) == HOP:
                self._limpo.setdefault(f, []).append(pedaco)
        for f, pedacos in self._limpo.items():
            k = self._marcas.get(f, 0)
            if k < len(self.MARCAS_S) and len(pedacos) * HOP / 16000 >= self.MARCAS_S[k]:
                self._marcas[f] = k + 1
                prontos.append((f, np.concatenate(pedacos)))
                if k + 1 == len(self.MARCAS_S):
                    self._limpo[f] = []
        return prontos


def segmentos(logits: np.ndarray, limiar: float = 0.5) -> list[dict]:
    ativo = (sigmoid(logits) > limiar).astype(np.int8)
    borda = np.zeros((1, ativo.shape[1]), np.int8)
    mud = np.diff(np.concatenate([borda, ativo, borda]), axis=0)
    segs = []
    for f in range(ativo.shape[1]):
        for a, b in zip(np.nonzero(mud[:, f] == 1)[0], np.nonzero(mud[:, f] == -1)[0]):
            segs.append({"Start": round(a * 0.01, 2), "End": round(b * 0.01, 2), "Speaker": f})
    return sorted(segs, key=lambda s: (s["Start"], s["Speaker"]))
