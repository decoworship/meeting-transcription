#!/usr/bin/env python3
"""O vetor de voz fora do torch — o teste que decide 4,8 GB (S1).

**A conta que motiva isto.** O modelo que produz o vetor de voz —
``pyannote/wespeaker-voxceleb-resnet34-LM`` — tem **26,6 MB**. O torch que o
executa, com os wheels de CUDA e as dependências científicas, tem **~4,8 GB**:
uma razão de ~180×, e 28% da instalação inteira
(``docs/CONVERGENCIA.md`` §1 e §2).

E o vetor não é dispensável. Ele é o que costura o ``S1`` do bloco 3 com o do
bloco 7 (``Nucleo/CosturaDeFalantes.cs``) e o que faz a pessoa **ter nome na
próxima reunião** (``Nucleo/AprendizadoDeVozes.cs``). O MOSS não o substitui: a
ABI do ``transcribe.cpp`` expõe do falante apenas
``{t0_ms, t1_ms, speaker_id, p}``, sem campo de vetor — o rótulo dele é
relacional dentro da janela de atenção, não uma impressão digital absoluta.

**Por isso a pergunta não é qual modelo, e sim qual runtime.** Este teste mede o
caminho de menor risco: o **mesmo** modelo, exportado para ONNX, com o fbank do
Kaldi reimplementado em numpy. Mesmos pesos, mesma arquitetura — e se os vetores
saírem equivalentes, **o banco de vozes não precisa ser re-extraído**.

**O que se descobriu montando isto**, e vale para quem for repetir:

* o modelo inteiro **não exporta**. O ``compute_fbank`` do pyannote embrulha o
  fbank num ``torch.vmap``, e nem o exportador antigo nem o dynamo o atravessam;
* **sem o vmap, ele exporta** — e o wrapper de batch 1 é *bit-exato* contra o
  modelo oficial (``maxabs = 0.0``), o que valida a decomposição;
* o que sobra sem exportar é só o ``aten::fft_rfft``, que o opset 17 não tem.
  Daí o fbank em numpy — que é exatamente o que o ``infer_onnx.py`` do WeSpeaker
  já fazia, e que o próprio ``compute_fbank`` do pyannote cita como fonte.

**A régua não é a similaridade dos vetores, e sim a decisão.** Dois vetores
podem diferir na sexta casa e mesmo assim mandar o app tratar duas gravações
como pessoas diferentes. O que se conta aqui é **quantas decisões de
reconhecimento mudam** nos limiares que o app usa de verdade — 0,55 e 0,60
dentro da reunião (``docs/FASE7-RESULTADOS.md`` §11.3) e 0,70 entre reuniões
(``Vozes.LimiarDeReconhecimento``).

**O torch só é usado aqui como referência e para exportar.** O caminho medido —
o que ficaria no app — é numpy + onnxruntime, e nada mais.

Uso::

    uv run --with onnxscript python tools/medir_vetor_onnx.py
    uv run --with onnxscript python tools/medir_vetor_onnx.py --gravacoes 12 --json saida.json
"""

from __future__ import annotations

import argparse
import json
import sys
import time
import warnings
from pathlib import Path

import numpy as np

warnings.filterwarnings("ignore")

ACERVO = Path("/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings")
MODELO = Path(
    "/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores/diarizacao"
    "/modelos/wespeaker-voxceleb-resnet34-LM"
)

# Os limiares que o app usa de verdade. 0,55 é o da costura entre blocos, 0,60 o
# de nomear cedo, e 0,70 o do banco de vozes entre reuniões.
LIMIARES = (0.55, 0.60, 0.70)

# ── o fbank do Kaldi, com os parâmetros que o pyannote passa ────────────────
TAXA, N_MEL = 16000, 80
JANELA, PASSO, PAD = 400, 160, 512          # 25 ms, 10 ms, próxima potência de 2
PREENFASE = 0.97
EPS = np.float32(1.1920929e-07)             # torch.finfo(torch.float).eps


def _hamming(n: int) -> np.ndarray:
    """A janela do Kaldi: ``periodic=False``, alpha 0,54."""
    return (0.54 - 0.46 * np.cos(2 * np.pi * np.arange(n) / (n - 1))).astype(np.float32)


def fbank(onda: np.ndarray, mel: np.ndarray) -> np.ndarray:
    """O log-mel do Kaldi. ``onda`` já vem na escala do pyannote (×2¹⁵)."""
    m = 1 + (len(onda) - JANELA) // PASSO    # snip_edges=True
    if m <= 0:
        return np.zeros((0, N_MEL), np.float32)

    idx = np.arange(m)[:, None] * PASSO + np.arange(JANELA)[None, :]
    q = onda[idx].astype(np.float32)
    q = q - q.mean(axis=1, keepdims=True)                      # remove_dc_offset
    ant = np.concatenate([q[:, :1], q[:, :-1]], axis=1)        # pad "replicate"
    q = (q - PREENFASE * ant) * _hamming(JANELA)[None, :]
    q = np.pad(q, ((0, 0), (0, PAD - JANELA)))

    pot = np.abs(np.fft.rfft(q, axis=1).astype(np.complex64)) ** 2
    return np.log(np.maximum(pot.astype(np.float32) @ mel.T, EPS))


def fbank_centrado(onda: np.ndarray, mel: np.ndarray) -> np.ndarray:
    """O que o ``compute_fbank`` entrega: escala, fbank e a média global fora."""
    f = fbank(onda.astype(np.float32) * np.float32(1 << 15), mel)
    return (f - f.mean(axis=0, keepdims=True))[None, :, :]


# ── o modelo, dos dois jeitos ───────────────────────────────────────────────
def preparar(destino: Path):
    """Exporta a ResNet para ONNX e devolve ``(modelo_torch, sessao_onnx, mel)``."""
    import torch
    from pyannote.audio import Model
    from torchaudio.compliance.kaldi import get_mel_banks

    banco = get_mel_banks(N_MEL, PAD, TAXA, 20.0, 0.0, 100.0, -500.0, 1.0)
    banco = banco[0] if isinstance(banco, tuple) else banco
    mel = torch.nn.functional.pad(banco, (0, 1)).numpy().astype(np.float32)

    modelo = Model.from_pretrained(MODELO)
    modelo.eval()

    if not destino.exists():
        # **Só a ResNet.** O compute_fbank não exporta por causa do vmap, e o
        # que sobra dele sem exportar é o fft_rfft — daí o fbank em numpy.
        class SoResNet(torch.nn.Module):
            def __init__(self, r):
                super().__init__()
                self.r = r

            def forward(self, f):
                return self.r(f)[1]

        torch.onnx.export(
            SoResNet(modelo.resnet).eval(),
            (torch.randn(1, 498, N_MEL),),
            str(destino),
            input_names=["fbank"],
            output_names=["embedding"],
            dynamic_axes={"fbank": {1: "frames"}},
            opset_version=17,
            dynamo=False,
        )

    import onnxruntime as ort

    sessao = ort.InferenceSession(str(destino), providers=["CPUExecutionProvider"])
    return modelo, sessao, mel


def vetor_torch(modelo, onda: np.ndarray) -> np.ndarray:
    import torch

    with torch.no_grad():
        v = modelo(torch.from_numpy(onda)[None, None, :])
    return v.numpy()[0].astype(np.float64)


def vetor_onnx(sessao, onda: np.ndarray, mel: np.ndarray) -> np.ndarray:
    f = fbank_centrado(onda, mel)
    return sessao.run(None, {"fbank": f})[0][0].astype(np.float64)


def cosseno(a: np.ndarray, b: np.ndarray) -> float:
    return float(np.dot(a, b) / (np.linalg.norm(a) * np.linalg.norm(b)))


# ── o acervo ────────────────────────────────────────────────────────────────
def vozes_da_gravacao(pasta: Path, min_s: float, max_s: float):
    """Uma faixa de áudio concatenada por falante, como o app monta."""
    import soundfile as sf

    arquivo = pasta / "transcricao.json"
    audio = pasta / "system.wav"
    if not arquivo.exists() or not audio.exists():
        return {}

    dados = json.loads(arquivo.read_text(encoding="utf-8"))
    porfalante: dict[str, list[tuple[float, float]]] = {}
    for s in dados.get("segments") or []:
        quem = s.get("speaker")
        ini, fim = s.get("start"), s.get("end")
        if not quem or ini is None or fim is None or fim - ini < 1.0:
            continue
        porfalante.setdefault(quem, []).append((float(ini), float(fim)))

    if not porfalante:
        return {}

    onda, taxa = sf.read(str(audio), dtype="float32")
    if onda.ndim > 1:
        onda = onda[:, 0]
    if taxa != TAXA:
        return {}

    saida = {}
    for quem, trechos in porfalante.items():
        pedacos, total = [], 0.0
        for ini, fim in trechos:
            if total >= max_s:
                break
            a, b = int(ini * TAXA), int(fim * TAXA)
            if b > a and b <= len(onda):
                pedacos.append(onda[a:b])
                total += (b - a) / TAXA
        if total >= min_s and pedacos:
            saida[quem] = np.concatenate(pedacos)
    return saida


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--gravacoes", type=int, default=10)
    p.add_argument("--min-s", type=float, default=8.0)
    p.add_argument("--max-s", type=float, default=60.0)
    p.add_argument("--json", type=Path)
    a = p.parse_args()

    if not ACERVO.exists():
        print(f"acervo não encontrado: {ACERVO}", file=sys.stderr)
        return 2

    onnx = Path(__file__).parent / "_wespeaker_resnet34.onnx"
    print("preparando (exporta o ONNX na primeira vez)…", flush=True)
    modelo, sessao, mel = preparar(onnx)
    print(f"ONNX: {onnx.stat().st_size / 1e6:.1f} MB\n")

    itens = []          # (gravação, falante, vetor_torch, vetor_onnx)
    tempo = {"torch": 0.0, "onnx": 0.0, "audio_s": 0.0}
    pastas = sorted(d for d in ACERVO.iterdir() if d.is_dir())
    for pasta in pastas:
        if len({i[0] for i in itens}) >= a.gravacoes:
            break
        vozes = vozes_da_gravacao(pasta, a.min_s, a.max_s)
        for quem, onda in vozes.items():
            t0 = time.perf_counter()
            vt = vetor_torch(modelo, onda)
            t1 = time.perf_counter()
            vo = vetor_onnx(sessao, onda, mel)
            t2 = time.perf_counter()
            tempo["torch"] += t1 - t0
            tempo["onnx"] += t2 - t1
            tempo["audio_s"] += len(onda) / TAXA
            itens.append((pasta.name, quem, vt, vo))
            print(f"  {pasta.name}  {quem:<28} {len(onda)/TAXA:6.1f}s", flush=True)

    if not itens:
        print("nenhuma voz utilizável no acervo", file=sys.stderr)
        return 2

    # ── 1. equivalência: o mesmo áudio pelos dois caminhos ──────────────────
    prop = [cosseno(t, o) for _, _, t, o in itens]
    print(f"\n{'='*66}\n1. EQUIVALÊNCIA — o mesmo áudio, torch vs ONNX ({len(itens)} vozes)\n")
    print(f"   cosseno mínimo   {min(prop):.9f}")
    print(f"   cosseno mediano  {float(np.median(prop)):.9f}")

    # ── 2. o que decide: as decisões de reconhecimento mudam? ───────────────
    n = len(itens)
    pares = [(i, j) for i in range(n) for j in range(i + 1, n)]
    ct = [cosseno(itens[i][2], itens[j][2]) for i, j in pares]
    co = [cosseno(itens[i][3], itens[j][3]) for i, j in pares]

    print(f"\n2. DECISÃO — {len(pares)} pares de vozes\n")
    print(f"   {'limiar':>8}  {'discordâncias':>14}  {'taxa':>8}")
    discordancias = {}
    for lim in LIMIARES:
        muda = sum((x >= lim) != (y >= lim) for x, y in zip(ct, co))
        discordancias[lim] = muda
        print(f"   {lim:>8.2f}  {muda:>14d}  {muda/len(pares):>7.3%}")

    dmax = max(abs(x - y) for x, y in zip(ct, co))
    print(f"\n   maior diferença de cosseno entre os dois caminhos: {dmax:.2e}")

    print(f"\n3. VELOCIDADE — {tempo['audio_s']:.0f}s de áudio, tudo em CPU\n")
    print(f"   torch (pyannote)   {tempo['torch']:7.2f}s   "
          f"{tempo['audio_s']/tempo['torch']:6.1f}x o tempo real")
    print(f"   numpy + onnx       {tempo['onnx']:7.2f}s   "
          f"{tempo['audio_s']/tempo['onnx']:6.1f}x o tempo real")

    ok = all(v == 0 for v in discordancias.values())
    print(f"\n{'='*66}")
    print("VEREDITO:", "equivalente — nenhuma decisão muda" if ok
          else "ATENÇÃO — há decisões divergentes, ver acima")

    if a.json:
        a.json.write_text(json.dumps({
            "vozes": len(itens), "pares": len(pares),
            "cos_proprio_min": min(prop),
            "cos_proprio_mediano": float(np.median(prop)),
            "maior_dif_cosseno": dmax,
            "discordancias": {str(k): v for k, v in discordancias.items()},
            "equivalente": ok,
            "seg_torch": tempo["torch"], "seg_onnx": tempo["onnx"],
            "audio_s": tempo["audio_s"],
        }, indent=2, ensure_ascii=False), encoding="utf-8")
        print(f"\nescrito em {a.json}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
