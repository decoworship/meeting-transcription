#!/usr/bin/env python3
"""A costura de falante entre blocos — o que a régua deu de graça ao MOSS.

**O buraco que este teste fecha.** A §7.4 do ``docs/FASE7-RESULTADOS.md`` mostra
o MOSS ganhando do pipeline atual no falante, mas com uma ressalva grande: os
rótulos dele são **locais ao bloco** (``b0_S1``, ``b1_S1``…), e o ``casar_nomes``
da régua casa cada bloco com a referência **independentemente**. Ou seja, o MOSS
recebeu de graça exatamente o trabalho difícil — saber que o ``S1`` do bloco 3 é
a mesma pessoa que o ``S2`` do bloco 7.

Num produto de verdade isso não é de graça. Quem faz é o vetor de voz, e a §2.3
mediu que há fala limpa para ancorar em ~88% dos pares (bloco, falante). Falta
saber o que sobra **depois de costurar de verdade**.

**O algoritmo é o que o app faria**, e é o mesmo do *Arrival-Order Speaker Cache*
do Sortformer, feito por fora: percorre os blocos em ordem; para cada falante do
bloco monta um vetor; compara com as identidades já conhecidas; acima do limiar é
a mesma pessoa e o centroide é atualizado, abaixo é gente nova. Sem olhar o
futuro — porque ao vivo não há futuro para olhar.

**O que se mede:** a mesma régua de falante de sempre, sobre a saída costurada.
A diferença contra o número da §7.4 é exatamente o que a régua estava dando de
presente.

Uso::

    uv run python tools/medir_costura.py
    uv run python tools/medir_costura.py --limiares 0.50,0.60,0.70 --json saida.json
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from medir_nomear_cedo import (  # noqa: E402
    SEGUNDOS_DO_TRECHO, carregar_voz, ler_wav, vetor,
)

ACERVO_PADRAO = "/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings"
VARREDURA_PADRAO = str(Path.home() / ".cache/pulsemeet-medicoes/varredura-moss")

#: As quatro com Gemini — referência independente — e as duas sem, onde a
#: referência é a saída do próprio app (ver tools/como_gemini.py).
GRAVACOES = [
    ("2026-08-21_11-00-33", "gemini"),
    ("2026-08-25_08-59-22", "gemini"),
    ("2026-08-20_15-59-20", "gemini"),
    ("2026-08-27_15-28-37", "gemini"),
    ("2026-08-18_15-59-53", "app"),
    ("2026-08-10_15-00-15", "app"),
]


def por_bloco(segmentos: list[dict]) -> dict[str, list[dict]]:
    """Agrupa os segmentos pelo rótulo local do bloco (``b3_S1``)."""
    grupos: dict[str, list[dict]] = {}
    for s in segmentos:
        rot = (s.get("speaker") or "").strip()
        if rot:
            grupos.setdefault(rot, []).append(s)
    return grupos


def trechos_do_rotulo(segmentos: list[dict]) -> list[tuple[float, float]]:
    """Trechos para embedar, do mais longo para o mais curto.

    Aqui **não** se aplica a regra de vizinhança do ``AprendizadoDeVozes``: os
    segmentos já vêm de um diarizador que decidiu que aquela fala é daquela
    pessoa, e refiltrar por vizinho descartaria justamente os blocos de conversa
    rápida, que são os que mais precisam de costura.
    """
    return sorted(
        ((s["start"], min(s["end"], s["start"] + SEGUNDOS_DO_TRECHO))
         for s in segmentos if s["end"] > s["start"]),
        key=lambda t: t[1] - t[0], reverse=True)


def costurar(inferencia, onda, taxa: int, segmentos: list[dict],
             limiar: float) -> tuple[list[dict], dict]:
    """Troca rótulo local por identidade global, em ordem de chegada."""
    import numpy as np

    grupos = por_bloco(segmentos)
    # A ordem é a de chegada — o primeiro instante em que o rótulo aparece.
    ordem = sorted(grupos, key=lambda r: min(s["start"] for s in grupos[r]))

    identidades: list[dict] = []      # {"vetor": np, "n": int}
    mapa: dict[str, str] = {}
    sem_voz = 0

    for rot in ordem:
        v = vetor(inferencia, onda, taxa, trechos_do_rotulo(grupos[rot]))
        if v is None:
            # Sem fala limpa suficiente: vira identidade própria. É o que o app
            # faria — inventar um vínculo sem vetor é pior que admitir que não
            # se sabe.
            sem_voz += 1
            mapa[rot] = f"P{len(identidades)+1}"
            identidades.append({"vetor": None, "n": 0})
            continue

        melhor, sim = None, -1.0
        for i, ident in enumerate(identidades):
            if ident["vetor"] is None:
                continue
            s_ = float(v @ ident["vetor"])
            if s_ > sim:
                melhor, sim = i, s_

        if melhor is not None and sim >= limiar:
            ident = identidades[melhor]
            # Centroide corrente: a voz da pessoa melhora a cada bloco em que
            # ela aparece, e é isso que segura a costura em reunião longa.
            ident["vetor"] = (ident["vetor"] * ident["n"] + v) / (ident["n"] + 1)
            ident["vetor"] /= float(np.linalg.norm(ident["vetor"])) or 1.0
            ident["n"] += 1
            mapa[rot] = f"P{melhor+1}"
        else:
            mapa[rot] = f"P{len(identidades)+1}"
            identidades.append({"vetor": v, "n": 1})

    saida = [dict(s, speaker=mapa.get((s.get("speaker") or "").strip(), "Unknown"))
             for s in segmentos]
    return saida, {"rotulos_locais": len(ordem), "identidades": len(identidades),
                   "sem_voz": sem_voz}


def acuracia(pasta: Path, referencia: Path) -> float | None:
    """Roda a régua de falante e devolve o percentual certo."""
    import re
    p = subprocess.run(
        [sys.executable, str(Path(__file__).parent / "comparar_com_gemini.py"),
         str(pasta), "--gemini", str(referencia)],
        capture_output=True, text=True)
    m = re.search(r"falante certo\s+\d+\s+\(([\d.]+)%\)", p.stdout)
    return float(m.group(1)) if m else None


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--acervo", default=ACERVO_PADRAO)
    ap.add_argument("--varredura", default=VARREDURA_PADRAO)
    ap.add_argument("--limiares", default="0.50,0.60,0.70")
    ap.add_argument("--saida", default=str(Path.home() / ".cache/pulsemeet-medicoes/costura"))
    ap.add_argument("--json")
    args = ap.parse_args()

    acervo, varredura = Path(args.acervo), Path(args.varredura)
    refs = Path.home() / ".cache/pulsemeet-medicoes/ref"
    limiares = [float(x) for x in args.limiares.split(",") if x.strip()]
    inferencia = carregar_voz()
    resultados = []

    print(f"\n{'gravação':<22} {'rót.':>5} " +
          "".join(f"{l:>18.2f}" for l in limiares))
    print(f"{'':22} {'locais':>5} " +
          "".join(f"{'ident/acerto':>18}" for _ in limiares))
    print("-" * (28 + 18 * len(limiares)))

    for nome, tipo in GRAVACOES:
        origem = varredura / f"{nome}__moss_bloco180s" / "transcricao.json"
        mix = acervo / nome / "mix.wav"
        ref = (acervo / nome / "gemini.md") if tipo == "gemini" else (refs / f"{nome}.md")
        if not (origem.is_file() and mix.is_file() and ref.is_file()):
            print(f"{nome:<22} (falta arquivo)")
            continue

        dados = json.loads(origem.read_text(encoding="utf-8"))
        segs = [s for s in dados.get("segments") or []
                if isinstance(s.get("start"), (int, float))
                and isinstance(s.get("end"), (int, float))]
        onda, taxa = ler_wav(mix)

        linha, celulas = {"gravacao": nome, "tipo": tipo, "limiares": {}}, []
        for lim in limiares:
            costurado, info = costurar(inferencia, onda, taxa, segs, lim)
            destino = Path(args.saida) / f"{nome}__costurado_{int(lim*100)}"
            destino.mkdir(parents=True, exist_ok=True)
            (destino / "transcricao.json").write_text(json.dumps(
                {"language": "pt", "duration": dados.get("duration"),
                 "segments": costurado}, ensure_ascii=False), encoding="utf-8")
            a = acuracia(destino, ref)
            celulas.append(f"{info['identidades']:>3}/{a if a is not None else 0:>7.1f}%   ")
            linha["limiares"][str(lim)] = {**info, "acerto_pct": a}
        print(f"{nome:<22} {linha['limiares'][str(limiares[0])]['rotulos_locais']:>5} " +
              "".join(celulas), flush=True)
        resultados.append(linha)

    if resultados:
        print("\n  'ident' = quantas pessoas a costura concluiu que existem.")
        print("  'acerto' = a régua de falante de sempre, sobre a saída costurada.")
        print("  Comparar com a §7.4, onde a régua costurava de graça.")

    if args.json and resultados:
        Path(args.json).write_text(json.dumps(resultados, ensure_ascii=False,
                                              indent=2), encoding="utf-8")
        print(f"\nbruto em {args.json}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
