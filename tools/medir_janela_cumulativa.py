#!/usr/bin/env python3
"""T0.4: rediarizar a reunião inteira a cada bloco, em vez do bloco isolado.

**De onde veio.** A §3 do ``docs/FASE7-RESULTADOS.md`` mediu que o bloco isolado
funde falantes — 4,0% de erro no bloco de 3 min, e o erro é de identidade, não
de segmentação. A §1.3 mostrou por que existe alternativa: **a diarização custa
28× o tempo real, cinco vezes menos que o ASR**. A 28×, rediarizar tudo desde o
começo a cada três minutos cabe no orçamento até ~84 min de reunião — por
extrapolação de quatro pontos, que é o que esta ferramenta troca por medição.

**As três perguntas.**

1. **a curva de custo é mesmo linear?** A extrapolação supõe que diarizar 60 min
   custa o dobro de 30. Se o pyannote tiver termo quadrático no agrupamento, o
   ponto de virada é muito antes;

2. **qual cadência mantém o ciclo de trabalho sensato?** Aos 60 min, uma passada
   cumulativa a cada 3 min ocuparia 72% da GPU. A cada 9 min, ~24%. A curva real
   dá o número certo;

3. **o rótulo do falante para de mudar?** — e esta é a que decide o produto.
   Se a pessoa nomeada no minuto 10 vira outro rótulo no minuto 30, a nomeação
   ao vivo não serve para nada. Aqui isso é medido como **rotatividade**: quanto
   da linha do tempo já rotulada troca de dono a cada passada nova, depois do
   melhor casamento possível de rótulos entre as duas passadas.

Roda no ``system.wav``, como toda diarização deste app (``docs/SIDECAR.md``).

Uso::

    uv run python tools/medir_janela_cumulativa.py
    uv run python tools/medir_janela_cumulativa.py --cadencia 180 --json saida.json
    uv run python tools/medir_janela_cumulativa.py --gravacoes 2026-08-27_15-28-37
"""

from __future__ import annotations

import argparse
import json
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from medir_bloco_diarizacao import (  # noqa: E402
    ACERVO_PADRAO, COM_GEMINI, QUADRO, carregar_pipeline, comparar, grade, turnos,
)


def rotatividade(anterior: list[tuple[float, float, str]],
                 atual: list[tuple[float, float, str]],
                 ate: float) -> dict:
    """Quanto da linha do tempo já rotulada trocou de dono.

    Compara as duas passadas **só no trecho que a anterior já tinha visto** —
    o que veio depois é novidade, não mudança. Os rótulos são casados de forma
    ótima antes de contar, porque ``SPEAKER_00`` virar ``SPEAKER_01`` sem que
    ninguém mude de dono é renomeação, não rotatividade: o app resolve isso pelo
    vetor de voz.
    """
    if ate <= 0:
        return {"quadros_ref": 0, "confusao": 0, "perdida": 0, "inventada": 0,
                "n_ref": 0, "n_hip": 0}
    a = grade([t for t in anterior if t[0] < ate], 0.0, ate)
    b = grade([t for t in atual if t[0] < ate], 0.0, ate)
    return comparar(a, b)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--acervo", default=ACERVO_PADRAO)
    ap.add_argument("--gravacoes")
    ap.add_argument("--cadencia", type=float, default=180.0,
                    help="segundos entre passadas cumulativas")
    ap.add_argument("--json")
    args = ap.parse_args()

    acervo = Path(args.acervo)
    nomes = ([n.strip() for n in args.gravacoes.split(",")] if args.gravacoes
             else list(COM_GEMINI))

    import soundfile as sf
    import torch

    pipe = carregar_pipeline()
    resultados = []

    for nome in nomes:
        wav = acervo / nome / "system.wav"
        if not wav.is_file():
            print(f"  [pulei] {nome}: sem system.wav", file=sys.stderr)
            continue

        audio, taxa = sf.read(str(wav), dtype="float32", always_2d=False)
        if audio.ndim > 1:
            audio = audio.mean(axis=1)
        dur = len(audio) / taxa
        n = max(1, int(dur // args.cadencia) + (1 if dur % args.cadencia else 0))
        print(f"\n{nome}  ({dur/60:.1f} min, {n} passadas a cada "
              f"{args.cadencia/60:.0f} min)", flush=True)
        print(f"  {'até':>6} {'custo':>8} {'RTF':>7} {'ciclo':>7} {'falantes':>9}"
              f" {'rotativ.':>9}", flush=True)

        anterior: list[tuple[float, float, str]] = []
        passadas = []
        for i in range(1, n + 1):
            ate = min(i * args.cadencia, dur)
            pedaco = audio[:int(ate * taxa)]
            t0 = time.perf_counter()
            atual = turnos(pipe({"waveform": torch.from_numpy(pedaco).unsqueeze(0),
                                 "sample_rate": taxa}))
            gasto = time.perf_counter() - t0

            visto = (i - 1) * args.cadencia
            r = rotatividade(anterior, atual, min(visto, dur))
            base = r["quadros_ref"] or 1
            churn = 100 * (r["confusao"] + r["perdida"] + r["inventada"]) / base

            ciclo = 100 * gasto / args.cadencia
            print(f"  {ate/60:>5.0f}m {gasto:>7.1f}s {ate/gasto:>6.1f}x"
                  f" {ciclo:>6.0f}% {len({x[2] for x in atual}):>9}"
                  f" {churn:>8.1f}%" + ("  ← estourou" if gasto > args.cadencia else ""),
                  flush=True)

            passadas.append({
                "ate_s": round(ate, 1), "segundos": round(gasto, 1),
                "rtf": round(ate / gasto, 1), "ciclo_pct": round(ciclo, 1),
                "falantes": len({x[2] for x in atual}),
                "rotatividade_pct": round(churn, 2),
                "quadros_comparados": r["quadros_ref"],
            })
            anterior = atual

        resultados.append({"gravacao": nome, "minutos": round(dur / 60, 1),
                           "cadencia_s": args.cadencia, "passadas": passadas})

    if resultados:
        print(f"\n{'═'*72}\nRESUMO\n{'═'*72}")
        print("  a curva de custo (RTF por tamanho da janela):")
        pontos = [(p["ate_s"] / 60, p["rtf"]) for r in resultados for p in r["passadas"]]
        pontos.sort()
        faixas = [(0, 10), (10, 20), (20, 40), (40, 60), (60, 999)]
        for a, b in faixas:
            f = [rtf for m, rtf in pontos if a <= m < b]
            if f:
                print(f"    {a:>3}–{b if b < 999 else '+':>3} min   "
                      f"RTF médio {sum(f)/len(f):>5.1f}x   (n={len(f)})")

        print("\n  rotatividade do rótulo, ignorando a primeira passada de cada gravação:")
        churns = [p["rotatividade_pct"] for r in resultados
                  for p in r["passadas"][1:] if p["quadros_comparados"] > 0]
        if churns:
            churns.sort()
            print(f"    mediana {churns[len(churns)//2]:>5.1f}%   "
                  f"p90 {churns[int(0.9*len(churns))]:>5.1f}%   "
                  f"máx {max(churns):>5.1f}%   (n={len(churns)})")

        print("\n  ciclo de trabalho no fim de cada gravação:")
        for r in resultados:
            u = r["passadas"][-1]
            print(f"    {r['gravacao']:<22} {r['minutos']:>5.1f} min → "
                  f"{u['segundos']:>5.1f}s por passada, {u['ciclo_pct']:>4.0f}% do período")

    if args.json and resultados:
        Path(args.json).write_text(json.dumps(resultados, ensure_ascii=False,
                                              indent=2), encoding="utf-8")
        print(f"\nbruto em {args.json}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
