#!/usr/bin/env python3
"""MOSS nas duas faixas separadas, em vez de no mix — T1.6

**A ideia é do dono do produto**, e ela ataca um desperdício: hoje o MOSS roda no
``mix.wav`` e tem de separar **você** dos outros, quando o app já sabe quem é
você de graça — o microfone é uma faixa própria (``Nucleo/VozDoDono.cs``). É dar
ao modelo um falante a mais para resolver, de graça e sem precisar.

**A alternativa:** rodar o MOSS uma vez em cada faixa e juntar por tempo.

* no ``mic.wav`` há **uma pessoa só**, e sabe-se qual — a atribuição do dono
  passa a ser certa por construção, não estimada;
* no ``system.wav`` sobra um falante a menos para separar;
* e **fala sobreposta deixa de se perder**: quando você fala junto com alguém,
  hoje um dos dois some no mix. Em faixas separadas, os dois são transcritos.

**O custo é duas passadas** em vez de uma. Mas o ``mic.wav`` é quase todo
silêncio — o dono fala 17% do tempo na mediana do acervo — e silêncio é barato
neste modelo.

**O que se mede**, contra o Gemini: texto (divergência e cobertura) e falante,
do mix contra as faixas separadas. E o tempo das duas estratégias.

Uso::

    ~/.cache/pulsemeet-medicoes/venv-moss/bin/python tools/medir_faixas_separadas.py \\
        --varredura ~/.cache/pulsemeet-medicoes/varredura-faixas
"""

from __future__ import annotations

import argparse
import json
import sys
import time
import wave
from pathlib import Path

ACERVO_PADRAO = "/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings"
REPO_GGUF = "handy-computer/moss-transcribe-diarize-gguf"
ARQUIVO_GGUF = "MOSS-Transcribe-Diarize-Q5_K_M.gguf"

COM_GEMINI = ["2026-08-21_11-00-33", "2026-08-25_08-59-22",
              "2026-08-20_15-59-20", "2026-08-27_15-28-37"]

#: O rótulo do dono, o mesmo que o app sempre gravou (VozDoDono.Rotulo).
DONO = "You"

#: Abaixo disto o bloco é silêncio, e **não pode ir ao modelo**.
#:
#: Medido em 03/09/2026: sem este portão, 17,5% dos segmentos da faixa do
#: microfone saíram em **chinês** — o modelo, sem sinal para transcrever,
#: completa o prompt padrão que está embutido no GGUF ("你好，我是你的助手").
#: No mix isso nunca acontece, porque quase não há bloco mudo; na faixa do
#: microfone a maior parte é silêncio, e o dono fala 17% do tempo na mediana
#: do acervo.
#:
#: O valor é o ``VozDoDono.LimiarDeFala``, medido em duas reuniões com fone:
#: o RMS do microfone é ~0,03–0,10 enquanto o dono fala e ~0,0002 enquanto
#: outra pessoa fala — 27 dB de folga.
LIMIAR_DE_FALA = 1e-2


def carregar(backend: str):
    import transcribe_cpp as t
    from huggingface_hub import hf_hub_download

    caminho = hf_hub_download(REPO_GGUF, ARQUIVO_GGUF)
    modelo = t.Model(caminho, backend=backend)
    print(f"{ARQUIVO_GGUF} em {backend}", flush=True)
    return modelo


def ler(caminho: Path):
    import numpy as np

    with wave.open(str(caminho), "rb") as w:
        if w.getsampwidth() != 2 or w.getnchannels() != 1:
            raise SystemExit(f"{caminho.name}: esperava WAV mono de 16 bits")
        taxa = w.getframerate()
        bruto = w.readframes(w.getnframes())
    return np.frombuffer(bruto, dtype=np.int16).astype(np.float32) / 32768.0, taxa


def blocos(modelo, pcm, taxa: int, bloco: float, rotulo: str | None):
    """Transcreve em blocos. ``rotulo`` fixo ignora o falante que o MOSS achar.

    Na faixa do microfone há uma pessoa só e sabe-se qual — aceitar a
    diarização do modelo ali seria trocar uma certeza por uma estimativa.
    """
    import transcribe_cpp as t

    import numpy as np

    passo = int(bloco * taxa)
    n = (len(pcm) + passo - 1) // passo
    segs, gasto, mudos = [], 0.0, 0
    for i in range(n):
        pedaco = pcm[i * passo:(i + 1) * passo]
        if len(pedaco) < taxa:
            continue
        if float(np.sqrt(np.mean(pedaco ** 2))) < LIMIAR_DE_FALA:
            mudos += 1
            continue
        t0 = time.perf_counter()
        r = t.transcribe(modelo, pedaco, diarize="on", timestamps="segment")
        gasto += time.perf_counter() - t0
        desloc = i * bloco
        for s in r.segments:
            if not s.text.strip():
                continue
            segs.append({
                "start": s.t0_ms / 1000 + desloc,
                "end": s.t1_ms / 1000 + desloc,
                "text": " " + s.text.strip(),
                "speaker": rotulo if rotulo else f"b{i}_S{s.speaker_id}",
            })
    return segs, gasto, n, mudos


def despejar(destino: Path, segs: list[dict], duracao: float) -> None:
    destino.mkdir(parents=True, exist_ok=True)
    (destino / "transcricao.json").write_text(json.dumps(
        {"language": "pt", "duration": duracao,
         "segments": sorted(segs, key=lambda s: s["start"])},
        ensure_ascii=False), encoding="utf-8")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--acervo", default=ACERVO_PADRAO)
    ap.add_argument("--gravacoes")
    ap.add_argument("--bloco", type=float, default=180.0)
    ap.add_argument("--backend", default="cuda")
    ap.add_argument("--varredura", required=True)
    ap.add_argument("--json")
    args = ap.parse_args()

    acervo = Path(args.acervo)
    nomes = ([n.strip() for n in args.gravacoes.split(",")] if args.gravacoes
             else list(COM_GEMINI))
    modelo = carregar(args.backend)
    resultados = []

    for nome in nomes:
        pasta = acervo / nome
        sistema, mic = pasta / "system.wav", pasta / "mic.wav"
        if not (sistema.is_file() and mic.is_file()):
            print(f"  [pulei] {nome}", file=sys.stderr)
            continue

        pcm_s, taxa = ler(sistema)
        pcm_m, _ = ler(mic)
        dur = len(pcm_s) / taxa
        print(f"\n{nome}  ({dur/60:.1f} min)", flush=True)

        segs_s, t_s, n_s, mudos_s = blocos(modelo, pcm_s, taxa, args.bloco, None)
        print(f"    system.wav  {t_s:6.1f}s  {len(segs_s):4d} seg  "
              f"{len({s['speaker'] for s in segs_s})} rótulos"
              f"{f'  ({mudos_s} blocos mudos pulados)' if mudos_s else ''}", flush=True)

        segs_m, t_m, _, mudos_m = blocos(modelo, pcm_m, taxa, args.bloco, DONO)
        print(f"    mic.wav     {t_m:6.1f}s  {len(segs_m):4d} seg  "
              f"(rótulo fixo '{DONO}')"
              f"{f'  ({mudos_m} blocos mudos pulados)' if mudos_m else ''}", flush=True)

        juntos = segs_s + segs_m
        despejar(Path(args.varredura) / f"{nome}__faixas", juntos, dur)
        print(f"    juntos      {t_s+t_m:6.1f}s  {len(juntos):4d} seg  "
              f"({dur/(t_s+t_m):.2f}x tempo real)", flush=True)

        resultados.append({
            "gravacao": nome, "minutos": round(dur / 60, 1),
            "system": {"segundos": round(t_s, 1), "segmentos": len(segs_s),
                       "rotulos": len({s["speaker"] for s in segs_s}),
                       "blocos_mudos": mudos_s},
            "mic": {"segundos": round(t_m, 1), "segmentos": len(segs_m),
                    "blocos_mudos": mudos_m},
            "total_s": round(t_s + t_m, 1),
            "rtf": round(dur / (t_s + t_m), 2),
        })

    if args.json and resultados:
        Path(args.json).write_text(json.dumps(resultados, ensure_ascii=False,
                                              indent=2), encoding="utf-8")
        print(f"\nbruto em {args.json}")
    print(f"\nvariantes em {args.varredura} — rode as réguas no venv do projeto.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
