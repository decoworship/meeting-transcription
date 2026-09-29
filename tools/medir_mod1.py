#!/usr/bin/env python3
"""O Nemotron-3 em streaming no ritmo do relógio, para rodar ao lado da legenda (MOD-1).

**O que este teste decide.** Se a diarização do Nemotron-3 cabe na 2060 junto
da legenda ao vivo — o critério de saída do ``MOD-1`` do docs/BACKLOG.md: um modo
de streaming em que a legenda não perde tempo real, ou a conclusão escrita de
que só a passada final cabe.

**Como ele roda.** Dois processos, como os dois sidecars do app rodariam: este,
e o ``tools/medir_legenda.py`` no venv do ``transcribe_cpp``. O ``medir.sh``
do fim deste cabeçalho sobe os dois e um vigia do ``nvidia-smi``.

**O que muda em relação ao ``medir_nemotron3.py``:** lá o ``embed.onnx`` recebe o
mel da gravação inteira de uma vez, o que nenhum streaming pode fazer e o que
faz a VRAM crescer com a duração (docs/NEMOTRON-DIARIZACAO.md §4). Aqui o mel e
o embed são calculados **por bloco**, com um pouco de contexto à esquerda, e
cada passo espera o relógio chegar ao áudio de que precisa. Para que o número
de custo não esconda uma troca de resultado, o fim do teste compara as decisões
por quadro com as do embed da gravação inteira (``concordancia``).

Medidas:

* ``atraso``: quanto depois do áudio necessário (bloco + contexto à direita) o
  passo terminou. Se ele cresce ao longo do teste, o modo não acompanha;
* ``passo_ms``: o custo de cada passo (mel + embed + step);
* ``ciclo``: fração do relógio gasta calculando.

Uso::

    uv run --with onnxruntime-gpu==1.23.2 --with soundfile python tools/medir_mod1.py \\
        --gravacao 2026-08-20_15-59-20 --modo low_latency --minutos 10 --json saida.json
"""
from __future__ import annotations

import argparse
import json
import statistics
import sys
import time
from pathlib import Path

import numpy as np
import soundfile as sf

AQUI = Path(__file__).resolve().parent
sys.path.insert(0, str(AQUI))
import nemotron3_onnx as n3  # noqa: E402

ACERVO = Path("/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings")
TAXA = 16000
QUADRO_S = 0.08          # um embed = 8 quadros de mel de 10 ms
CONTEXTO_ESQ = 8         # embeds de contexto à esquerda do bloco, descartados


def onda(gravacao: str, minutos: float) -> np.ndarray:
    a, _ = sf.read(ACERVO / gravacao / "mix.wav", dtype="float32")
    a = a.mean(1) if a.ndim > 1 else a
    return a[: int(minutos * 60 * TAXA)]


def embed_do_trecho(eng: n3.Nemotron3, audio: np.ndarray, ini: int, fim: int) -> np.ndarray:
    """Os embeds [ini, fim) calculados só do áudio em volta, como um sidecar faria."""
    e0 = max(0, ini - CONTEXTO_ESQ)
    a0 = int(e0 * QUADRO_S * TAXA)
    a1 = min(len(audio), int(fim * QUADRO_S * TAXA))
    feats = n3.mel(audio[a0:a1], eng.filtros)
    emb = eng.embed.run(None, {"features": feats[None]})[0][0]
    return emb[ini - e0: ini - e0 + (fim - ini)]


def streaming(eng: n3.Nemotron3, audio: np.ndarray, modo: str, relogio: bool):
    bl, cd, fifo, per = n3.MODOS[modo]
    ne = int(len(audio) / TAXA / QUADRO_S)
    cache = n3.CacheDeFalantes(fifo, per, eng.sil)
    saida, atrasos, passos = [], [], []
    calculando = 0.0
    t_ini = time.perf_counter()
    for ini in range(0, ne, bl):
        fim = min(ini + bl, ne)
        ate = min(fim + cd, ne)
        precisa_s = ate * QUADRO_S
        if relogio:
            espera = precisa_s - (time.perf_counter() - t_ini)
            if espera > 0:
                time.sleep(espera)
        t0 = time.perf_counter()
        pedaco = embed_do_trecho(eng, audio, ini, ate)
        ant = cache.anteriores()
        entrada = np.concatenate([ant, pedaco])
        lg = eng.step.run(None, {"embeds": entrada[None]})[0][0]
        cache.atualizar(entrada, lg, fim - ini)
        saida.append(lg[len(ant) * n3.SUB: (len(ant) + fim - ini) * n3.SUB])
        t1 = time.perf_counter()
        calculando += t1 - t0
        passos.append((t1 - t0) * 1000)
        atrasos.append((t1 - t_ini) - precisa_s)
    parede = time.perf_counter() - t_ini
    return np.concatenate(saida), atrasos, passos, calculando, parede


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--gravacao", required=True)
    p.add_argument("--modo", default="low_latency", choices=list(n3.MODOS))
    p.add_argument("--minutos", type=float, default=10.0)
    p.add_argument("--ritmo", choices=("relogio", "cheio"), default="relogio")
    p.add_argument("--sem-conferir", action="store_true",
                   help="não roda o embed inteiro no fim para comparar decisões")
    p.add_argument("--json", type=Path)
    a = p.parse_args()

    import onnxruntime as ort
    ort.preload_dlls()
    eng = n3.Nemotron3(AQUI / "_nemotron3", gpu=True)
    audio = onda(a.gravacao, a.minutos)
    streaming(eng, np.zeros(TAXA * 20, np.float32), a.modo, relogio=False)  # aquece

    lg, atrasos, passos, calc, parede = streaming(eng, audio, a.modo, a.ritmo == "relogio")
    terco = max(1, len(atrasos) // 3)
    r = {
        "gravacao": a.gravacao, "modo": a.modo, "ritmo": a.ritmo,
        "audio_s": len(audio) / TAXA, "parede_s": parede,
        "ciclo": calc / max(parede, 1e-9),
        "xrt": (len(audio) / TAXA) / max(calc, 1e-9),
        "passo_ms_p50": statistics.median(passos),
        "passo_ms_p90": float(np.percentile(passos, 90)),
        "atraso_s_p50": statistics.median(atrasos),
        "atraso_s_p90": float(np.percentile(atrasos, 90)),
        "atraso_s_max": max(atrasos),
        "atraso_s_ultimo_terco_p50": statistics.median(atrasos[-terco:]),
    }
    if not a.sem_conferir:
        inteiro = eng.logits(audio, a.modo)
        n = min(len(inteiro), len(lg))
        r["concordancia"] = float(((n3.sigmoid(inteiro[:n]) > 0.5)
                                   == (n3.sigmoid(lg[:n]) > 0.5)).mean())
    print(json.dumps(r, indent=2, ensure_ascii=False))
    if a.json:
        a.json.write_text(json.dumps(r, indent=2, ensure_ascii=False), encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
