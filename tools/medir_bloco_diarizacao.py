#!/usr/bin/env python3
"""O que o bloco custa à diarização — o buraco que a rodada 0 deixou aberto.

**O que já se sabe.** O [`simular_blocos.py`](simular_blocos.py) mediu a
*estrutura*: num bloco de 1 min cabem 2 falantes na mediana, 98% dos blocos
ficam dentro do teto de 4, e 85% dos pares (bloco, falante) têm voz limpa para
ancorar. E o [`medir_bloco_asr.py`](medir_bloco_asr.py) mostrou que o texto não
piora.

**O que não se sabia.** Nada disso diz que o *pyannote* rodando num bloco de 1
minuto encontra os mesmos falantes que ele encontra na gravação inteira. Ele tem
contexto muito menor para agrupar vozes, e agrupamento é justamente o que ele
faz. É plausível que ele parta uma pessoa em duas dentro do bloco, ou funda duas
numa — e aí a costura entre blocos herda um erro que nenhum vetor conserta.

**A régua.** A diarização da gravação inteira é a referência; a por blocos é a
hipótese. Dentro de cada bloco os rótulos são casados de forma **ótima**
(atribuição húngara sobre a matriz de sobreposição) — o melhor caso para a
costura. O que sobra depois disso é erro que o bloco causou e que nenhuma
costura desfaz:

* **confusão** — quadro atribuído à pessoa errada, já com o melhor casamento;
* **perdida** — a inteira diz que há fala, o bloco não achou ninguém;
* **inventada** — o bloco achou fala onde a inteira não vê.

Roda no ``system.wav``, e não no mix: é onde a diarização deste app roda
(``docs/SIDECAR.md``), porque quem falou no microfone já se sabe.

Uso::

    uv run python tools/medir_bloco_diarizacao.py
    uv run python tools/medir_bloco_diarizacao.py --gravacoes 2026-08-21_11-00-33
    uv run python tools/medir_bloco_diarizacao.py --blocos 60,180 --json saida.json
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import time
from pathlib import Path

ACERVO_PADRAO = "/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings"

# O pipeline embarcado da instalação oficial. Mesmos bytes do HuggingFace —
# muda só precisar ou não de token e de rede (motores/diarizacao/motor.py).
LOCAIS = [
    "/mnt/c/Users/andre/MeetingApp/motores/diarizacao/modelos/community-1",
    str(Path(__file__).resolve().parent.parent
        / "motores/diarizacao/modelos/community-1"),
]

COM_GEMINI = [
    "2026-08-21_11-00-33",   # 7,7 min
    "2026-08-25_08-59-22",   # 14,6 min
    "2026-08-20_15-59-20",   # 32,1 min
    "2026-08-27_15-28-37",   # 48,5 min
]

TAXA = 16_000
QUADRO = 0.010          # 10 ms, o passo da grade de comparação


def carregar_pipeline():
    from pyannote.audio import Pipeline
    import torch

    origem = next((p for p in LOCAIS
                   if os.path.isfile(os.path.join(p, "config.yaml"))), None)
    if origem is None:
        token = os.environ.get("HF_TOKEN")
        if not token:
            raise SystemExit(
                "pipeline community-1 não encontrado localmente e sem HF_TOKEN. "
                "Rode tools/empacotar_modelos_de_diarizacao.sh.")
        pipe = Pipeline.from_pretrained("pyannote/speaker-diarization-community-1",
                                        token=token)
        print("pipeline: HuggingFace")
    else:
        pipe = Pipeline.from_pretrained(origem)
        print(f"pipeline: {origem}")

    dispositivo = "cuda" if torch.cuda.is_available() else "cpu"
    pipe.to(torch.device(dispositivo))
    print(f"carregado em {dispositivo}")
    return pipe


def turnos(saida) -> list[tuple[float, float, str]]:
    """Os turnos, seja qual for a casca que o pyannote devolva.

    O 4.x embrulha a anotação num ``DiarizeOutput``; o 3.x a devolve direto. É
    o mesmo ``getattr`` que motores/diarizacao/motor.py faz — divergir aqui
    quebraria com a versão que o app usa.
    """
    anotacao = getattr(saida, "speaker_diarization", saida)
    return [(t.start, t.end, rot)
            for t, _, rot in anotacao.itertracks(yield_label=True)]


def grade(turnos_: list[tuple[float, float, str]], inicio: float, fim: float
          ) -> list[str | None]:
    """Um rótulo por quadro de 10 ms, ou ``None`` onde ninguém fala.

    Sobreposição é resolvida pelo último a escrever — é aproximação, e ela vale
    igual para os dois lados da comparação, então não enviesa.
    """
    n = max(0, int(round((fim - inicio) / QUADRO)))
    g: list[str | None] = [None] * n
    for a, b, rot in turnos_:
        i = max(0, int(round((a - inicio) / QUADRO)))
        j = min(n, int(round((b - inicio) / QUADRO)))
        for k in range(i, j):
            g[k] = rot
    return g


def comparar(ref: list[str | None], hip: list[str | None]) -> dict:
    """Confusão, perdida e inventada, com o melhor casamento de rótulos."""
    from collections import Counter

    import numpy as np
    from scipy.optimize import linear_sum_assignment

    fala_ref = sum(1 for x in ref if x is not None)
    perdida = sum(1 for r, h in zip(ref, hip) if r is not None and h is None)
    inventada = sum(1 for r, h in zip(ref, hip) if r is None and h is not None)

    rr = sorted({x for x in ref if x is not None})
    hh = sorted({x for x in hip if x is not None})
    if not rr or not hh:
        return {"quadros_ref": fala_ref, "confusao": fala_ref - perdida,
                "perdida": perdida, "inventada": inventada,
                "n_ref": len(rr), "n_hip": len(hh)}

    par = Counter((r, h) for r, h in zip(ref, hip)
                  if r is not None and h is not None)
    m = np.zeros((len(rr), len(hh)))
    for (r, h), n in par.items():
        m[rr.index(r), hh.index(h)] = n

    linhas, colunas = linear_sum_assignment(-m)
    acertos = int(m[linhas, colunas].sum())
    sobrepostos = int(m.sum())
    return {
        "quadros_ref": fala_ref,
        "confusao": sobrepostos - acertos,
        "perdida": perdida,
        "inventada": inventada,
        "n_ref": len(rr),
        "n_hip": len(hh),
    }


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--acervo", default=ACERVO_PADRAO)
    ap.add_argument("--gravacoes")
    ap.add_argument("--blocos", default="60,180")
    ap.add_argument("--json")
    args = ap.parse_args()

    acervo = Path(args.acervo)
    nomes = ([n.strip() for n in args.gravacoes.split(",")] if args.gravacoes
             else list(COM_GEMINI))
    tamanhos = [float(t) for t in args.blocos.split(",") if t.strip()]

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
        print(f"\n{nome}  ({dur/60:.1f} min)", flush=True)

        t0 = time.perf_counter()
        inteira = turnos(pipe({"waveform": torch.from_numpy(audio).unsqueeze(0),
                               "sample_rate": taxa}))
        gasto = time.perf_counter() - t0
        falantes = sorted({r for _, _, r in inteira})
        print(f"    inteira        {gasto:6.1f}s  {len(falantes)} falantes,"
              f" {len(inteira)} turnos", flush=True)

        linha = {"gravacao": nome, "minutos": round(dur / 60, 1),
                 "inteira": {"segundos": round(gasto, 1),
                             "falantes": len(falantes), "turnos": len(inteira)},
                 "blocos": []}

        for t in tamanhos:
            passo = int(t * taxa)
            n = (len(audio) + passo - 1) // passo
            tot = {"quadros_ref": 0, "confusao": 0, "perdida": 0, "inventada": 0}
            n_hip, n_ref_b, blocos_ok = [], [], 0
            t0 = time.perf_counter()

            for i in range(n):
                pedaco = audio[i * passo:(i + 1) * passo]
                if len(pedaco) < taxa:
                    continue
                ini, fim = i * t, i * t + len(pedaco) / taxa
                saida = pipe({"waveform": torch.from_numpy(pedaco).unsqueeze(0),
                              "sample_rate": taxa})
                hip = grade([(a + ini, b + ini, r) for a, b, r in turnos(saida)], ini, fim)
                ref = grade([(a, b, r) for a, b, r in inteira
                             if b > ini and a < fim], ini, fim)
                c = comparar(ref, hip)
                for k in tot:
                    tot[k] += c[k]
                n_hip.append(c["n_hip"])
                n_ref_b.append(c["n_ref"])
                blocos_ok += 1

            gasto = time.perf_counter() - t0
            base = tot["quadros_ref"] or 1
            der = 100 * (tot["confusao"] + tot["perdida"] + tot["inventada"]) / base
            print(f"    bloco {t/60:>4.0f} min  {gasto:6.1f}s  {blocos_ok} blocos"
                  f"   confusão {100*tot['confusao']/base:5.1f}%"
                  f"   perdida {100*tot['perdida']/base:5.1f}%"
                  f"   inventada {100*tot['inventada']/base:5.1f}%"
                  f"   → {der:5.1f}%", flush=True)
            print(f"                     falantes por bloco: inteira "
                  f"{sum(n_ref_b)/max(1,len(n_ref_b)):.1f}  bloco "
                  f"{sum(n_hip)/max(1,len(n_hip)):.1f}", flush=True)

            linha["blocos"].append({
                "bloco_s": t, "n_blocos": blocos_ok, "segundos": round(gasto, 1),
                "der": round(der, 2),
                "confusao_pct": round(100 * tot["confusao"] / base, 2),
                "perdida_pct": round(100 * tot["perdida"] / base, 2),
                "inventada_pct": round(100 * tot["inventada"] / base, 2),
                "falantes_medio_inteira": round(sum(n_ref_b) / max(1, len(n_ref_b)), 2),
                "falantes_medio_bloco": round(sum(n_hip) / max(1, len(n_hip)), 2),
            })
        resultados.append(linha)

    if resultados:
        print(f"\n{'═'*70}\nRESUMO — diarização por bloco contra a da gravação inteira\n{'═'*70}")
        print("  gravação                min" + "".join(
            f"{t/60:>9.0f} min" for t in tamanhos))
        for r in resultados:
            print(f"  {r['gravacao']:<20} {r['minutos']:>5.1f}" +
                  "".join(f"{b['der']:>11.1f}%" for b in r["blocos"]))
        print("\n  agregado, ponderado por quadro de fala:")
        for i, t in enumerate(tamanhos):
            num = sum(b["der"] * b["n_blocos"] for b in
                      (r["blocos"][i] for r in resultados))
            den = sum(b["n_blocos"] for b in (r["blocos"][i] for r in resultados))
            print(f"    bloco {t/60:>4.0f} min   {num/max(1,den):5.1f}%")

    if args.json and resultados:
        Path(args.json).write_text(json.dumps(resultados, ensure_ascii=False,
                                              indent=2), encoding="utf-8")
        print(f"\nbruto em {args.json}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
