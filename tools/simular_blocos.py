#!/usr/bin/env python3
"""Simula o troceamento da reunião em blocos, sobre o acervo já transcrito.

**Por que existe.** A Fase 7 propõe um produto que roda o pipeline de hoje em
blocos de poucos minutos, durante a reunião, em vez de uma passada só no fim
(``docs/FASE7.md``, e a fila da rodada 0). Antes de gastar GPU medindo a perda
de qualidade do bloco curto, há três perguntas que o acervo responde **de
graça**, porque a resposta está na estrutura de quem falou quando — e isso o
``transcricao.json`` já tem:

1. **quantos falantes aparecem dentro de um bloco?** A contagem por gravação
   inteira já foi medida (FASE7.md §3.1: só 43% têm 1–4 falantes). Mas quem
   diariza por bloco só vê os falantes *daquele* bloco, e num recorte de três
   minutos costuma falar menos gente. Se o teto de 4 do Streaming Sortformer
   não for atingido **por bloco**, ele deixa de ser o impedimento que a carta
   descreve;

2. **dá para costurar o falante entre blocos?** O ``SPEAKER_00`` do bloco N não
   é o mesmo do bloco N+1 — quem os liga é o vetor de voz. A costura por
   continuidade só funciona quando o falante aparece nos dois blocos, então o
   número que interessa é quantos falantes são **órfãos**: aparecem num bloco
   só, e não têm âncora nenhuma;

3. **quanto custa o corte?** Um bloco parte no meio de uma frase. Contar quantos
   segmentos atravessam a fronteira dá o tamanho do problema de emenda.

Nada aqui mede qualidade de transcrição — isso é o T0.1, e precisa rodar os
motores. Isto aqui mede a **estrutura**, que é o que decide se o T0.1 vale a
pena e com qual tamanho de bloco.

Uso::

    uv run python tools/simular_blocos.py
    uv run python tools/simular_blocos.py --acervo /caminho/para/MeetingRecordings
    uv run python tools/simular_blocos.py --blocos 60,180,300 --json saida.json
"""

from __future__ import annotations

import argparse
import json
import statistics
import sys
from collections import Counter
from dataclasses import dataclass, field
from pathlib import Path

# O teto do nvidia/diar_streaming_sortformer_4spk-v2. Ver docs/FASE7.md §3.1.
TETO_SORTFORMER = 4

# O teto do LS-EEND, o candidato aberto que vai além. Ver o dossiê da pesquisa.
TETO_LSEEND = 8

ACERVO_PADRAO = "/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings"


@dataclass
class Bloco:
    """Um recorte de tempo, e quem falou dentro dele."""

    indice: int
    inicio: float
    fim: float
    falantes: set[str] = field(default_factory=set)
    segmentos: int = 0


@dataclass
class Gravacao:
    nome: str
    duracao: float
    falantes_no_total: set[str]
    blocos: list[Bloco]
    atravessam: int
    segmentos: int
    crus: list = field(default_factory=list)


def _falante_util(seg: dict) -> str | None:
    """O rótulo do segmento, quando ele diz alguma coisa.

    ``Unknown`` é o rótulo explícito de "ninguém foi identificado aqui"
    (``docs/SIDECAR.md``), e contá-lo como pessoa inflaria a contagem de
    falantes justamente no número que estamos medindo.
    """
    f = seg.get("speaker")
    if not isinstance(f, str):
        return None
    f = f.strip()
    if not f or f.lower() in {"unknown", "desconhecido"}:
        return None
    return f


def recortar(segmentos: list[dict], duracao: float, tamanho: float) -> tuple[list[Bloco], int]:
    """Divide a gravação em blocos de ``tamanho`` segundos.

    Um segmento entra em **todos** os blocos que ele toca: é o que um pipeline
    por blocos veria, já que ele recebe o áudio daquela janela e transcreve o
    que houver ali dentro. O contador de travessias é dos segmentos que tocam
    mais de um.
    """
    if duracao <= 0 or tamanho <= 0:
        return [], 0

    n = max(1, int(duracao // tamanho) + (1 if duracao % tamanho else 0))
    blocos = [Bloco(i, i * tamanho, min((i + 1) * tamanho, duracao)) for i in range(n)]
    atravessam = 0

    for seg in segmentos:
        try:
            ini = float(seg["start"])
            fim = float(seg["end"])
        except (KeyError, TypeError, ValueError):
            continue
        if fim < ini:
            continue

        primeiro = min(int(ini // tamanho), n - 1)
        ultimo = min(int(max(fim - 1e-9, ini) // tamanho), n - 1)
        if ultimo > primeiro:
            atravessam += 1

        falante = _falante_util(seg)
        for i in range(primeiro, ultimo + 1):
            blocos[i].segmentos += 1
            if falante:
                blocos[i].falantes.add(falante)

    return blocos, atravessam


def carregar(pasta: Path) -> tuple[list[dict], float] | None:
    alvo = pasta / "transcricao.json"
    if not alvo.is_file():
        return None
    try:
        dados = json.loads(alvo.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as e:
        print(f"  [pulei] {pasta.name}: {e}", file=sys.stderr)
        return None

    segmentos = dados.get("segments")
    if not isinstance(segmentos, list) or not segmentos:
        return None

    duracao = dados.get("duration")
    if not isinstance(duracao, (int, float)) or duracao <= 0:
        # Sem duração declarada, o fim do último segmento serve.
        finais = [float(s["end"]) for s in segmentos if isinstance(s.get("end"), (int, float))]
        duracao = max(finais) if finais else 0.0

    return segmentos, float(duracao)


def analisar(acervo: Path, tamanho: float) -> list[Gravacao]:
    saida = []
    for pasta in sorted(p for p in acervo.iterdir() if p.is_dir()):
        lido = carregar(pasta)
        if lido is None:
            continue
        segmentos, duracao = lido
        blocos, atravessam = recortar(segmentos, duracao, tamanho)
        if not blocos:
            continue
        todos = {f for b in blocos for f in b.falantes}
        saida.append(Gravacao(pasta.name, duracao, todos, blocos, atravessam,
                              len(segmentos), segmentos))
    return saida


def orfaos(g: Gravacao) -> tuple[int, int]:
    """Falantes sem âncora, e o total de falantes da gravação.

    Órfão é quem aparece em **um bloco só**: não há bloco vizinho em que ele
    também esteja, então não existe continuidade que o costure. Só o vetor de
    voz o liga ao resto — ou nada o liga, e ele vira uma pessoa a mais na conta
    final.
    """
    em_quantos = Counter()
    for b in g.blocos:
        for f in b.falantes:
            em_quantos[f] += 1
    return sum(1 for n in em_quantos.values() if n == 1), len(em_quantos)


# As regras do AprendizadoDeVozes.TrechosDe, portadas para cá. Divergir delas
# mediria uma costura que o app não faz.
FOLGA_ENTRE_TURNOS = 0.5   # vizinho de outra pessoa mais perto que isto contamina
SEGUNDOS_DO_TRECHO = 4.0   # cada trecho limpo é truncado aqui


def trechos_limpos(segmentos: list[dict]) -> dict[str, float]:
    """Segundos de fala **limpa** por falante, pela régua do app.

    Um segmento só serve de amostra de voz se não tiver vizinho de outra pessoa
    a menos de meio segundo — senão o vetor sai contaminado e envenena o perfil
    em silêncio. É a condição que decide se um falante pode ser **ancorado** num
    bloco: sem amostra limpa, não há vetor, e sem vetor não há costura com o
    bloco seguinte.
    """
    ordenados = sorted(
        (s for s in segmentos
         if isinstance(s.get("start"), (int, float))
         and isinstance(s.get("end"), (int, float))),
        key=lambda s: s["start"])

    limpos: dict[str, float] = {}
    for i, s in enumerate(ordenados):
        falante = _falante_util(s)
        if not falante:
            continue
        sujo_antes = (i > 0
                      and _falante_util(ordenados[i - 1]) != falante
                      and s["start"] - ordenados[i - 1]["end"] < FOLGA_ENTRE_TURNOS)
        sujo_depois = (i + 1 < len(ordenados)
                       and _falante_util(ordenados[i + 1]) != falante
                       and ordenados[i + 1]["start"] - s["end"] < FOLGA_ENTRE_TURNOS)
        if sujo_antes or sujo_depois:
            continue
        limpos[falante] = limpos.get(falante, 0.0) + min(
            s["end"] - s["start"], SEGUNDOS_DO_TRECHO)
    return limpos


def ancoragem(segmentos: list[dict], duracao: float, tamanho: float) -> tuple[int, int]:
    """Pares (bloco, falante) que têm amostra limpa, e o total de pares."""
    passo = tamanho
    n = max(1, int(duracao // passo) + (1 if duracao % passo else 0))
    com_ancora = total = 0
    for i in range(n):
        ini, fim = i * passo, (i + 1) * passo
        dentro = [s for s in segmentos
                  if isinstance(s.get("start"), (int, float))
                  and isinstance(s.get("end"), (int, float))
                  and s["end"] > ini and s["start"] < fim]
        if not dentro:
            continue
        presentes = {f for f in (_falante_util(s) for s in dentro) if f}
        limpos = trechos_limpos(dentro)
        total += len(presentes)
        com_ancora += sum(1 for f in presentes if limpos.get(f, 0.0) >= 1.0)
    return com_ancora, total


def pct(parte: int, todo: int) -> str:
    return f"{100 * parte / todo:5.1f}%" if todo else "    —"


def relatar(tamanho: float, gravacoes: list[Gravacao]) -> dict:
    blocos = [b for g in gravacoes for b in g.blocos]
    povoados = [b for b in blocos if b.falantes]
    contagens = [len(b.falantes) for b in povoados]

    dentro_4 = sum(1 for c in contagens if c <= TETO_SORTFORMER)
    dentro_8 = sum(1 for c in contagens if c <= TETO_LSEEND)

    # A mesma pergunta por gravação: de quantas reuniões *todos* os blocos
    # cabem no teto? É a régua honesta — um bloco estourado já estraga a
    # reunião inteira para quem confia no rótulo.
    inteiras_4 = sum(
        1 for g in gravacoes
        if g.blocos and all(len(b.falantes) <= TETO_SORTFORMER for b in g.blocos if b.falantes)
    )
    inteiras_8 = sum(
        1 for g in gravacoes
        if g.blocos and all(len(b.falantes) <= TETO_LSEEND for b in g.blocos if b.falantes)
    )

    total_orfaos = total_falantes = 0
    for g in gravacoes:
        o, t = orfaos(g)
        total_orfaos += o
        total_falantes += t

    atravessam = sum(g.atravessam for g in gravacoes)
    segmentos = sum(g.segmentos for g in gravacoes)

    print(f"\n{'─' * 66}")
    print(f"BLOCO DE {tamanho / 60:.0f} MIN  ·  {len(gravacoes)} gravações  ·  {len(blocos)} blocos")
    print(f"{'─' * 66}")

    print(f"\n  falantes por bloco     mediana {statistics.median(contagens):.0f}"
          f"   média {statistics.mean(contagens):.1f}"
          f"   máx {max(contagens)}")
    dist = Counter(contagens)
    for n in sorted(dist):
        barra = "█" * max(1, round(40 * dist[n] / len(contagens)))
        print(f"    {n:2d} falante(s)  {dist[n]:5d}  {pct(dist[n], len(contagens))}  {barra}")

    print(f"\n  cabe no teto de {TETO_SORTFORMER} (Sortformer)")
    print(f"    por bloco      {dentro_4:5d} de {len(contagens):5d}   {pct(dentro_4, len(contagens))}")
    print(f"    por gravação   {inteiras_4:5d} de {len(gravacoes):5d}   {pct(inteiras_4, len(gravacoes))}"
          "   ← todos os blocos dentro")

    print(f"\n  cabe no teto de {TETO_LSEEND} (LS-EEND)")
    print(f"    por bloco      {dentro_8:5d} de {len(contagens):5d}   {pct(dentro_8, len(contagens))}")
    print(f"    por gravação   {inteiras_8:5d} de {len(gravacoes):5d}   {pct(inteiras_8, len(gravacoes))}")

    com_ancora = pares = 0
    for g in gravacoes:
        a, t = ancoragem(g.crus, g.duracao, tamanho)
        com_ancora += a
        pares += t

    print(f"\n  costura entre blocos")
    print(f"    pares (bloco, falante) com voz limpa  {com_ancora:5d} de {pares:5d}"
          f"   {pct(com_ancora, pares)}   ← dá para ancorar por vetor")
    print(f"    falantes órfãos (só num bloco)   {total_orfaos:5d} de {total_falantes:5d}"
          f"   {pct(total_orfaos, total_falantes)}")
    print(f"    segmentos que atravessam corte   {atravessam:5d} de {segmentos:5d}"
          f"   {pct(atravessam, segmentos)}")

    return {
        "bloco_s": tamanho,
        "gravacoes": len(gravacoes),
        "blocos": len(blocos),
        "blocos_povoados": len(contagens),
        "falantes_por_bloco": {
            "mediana": statistics.median(contagens),
            "media": round(statistics.mean(contagens), 2),
            "max": max(contagens),
            "distribuicao": {str(k): v for k, v in sorted(dist.items())},
        },
        "teto_4": {"blocos": dentro_4, "gravacoes_inteiras": inteiras_4},
        "teto_8": {"blocos": dentro_8, "gravacoes_inteiras": inteiras_8},
        "pares_com_ancora": com_ancora,
        "pares_bloco_falante": pares,
        "orfaos": total_orfaos,
        "falantes_total": total_falantes,
        "segmentos_atravessam": atravessam,
        "segmentos_total": segmentos,
    }


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--acervo", default=ACERVO_PADRAO, help="pasta das gravações")
    ap.add_argument("--blocos", default="60,180,300",
                    help="tamanhos de bloco em segundos, separados por vírgula")
    ap.add_argument("--json", help="grava o resultado bruto neste arquivo")
    args = ap.parse_args()

    acervo = Path(args.acervo)
    if not acervo.is_dir():
        print(f"acervo não encontrado: {acervo}", file=sys.stderr)
        return 1

    try:
        tamanhos = [float(t) for t in args.blocos.split(",") if t.strip()]
    except ValueError:
        print(f"--blocos inválido: {args.blocos}", file=sys.stderr)
        return 1

    base = analisar(acervo, tamanhos[0])
    if not base:
        print("nenhuma gravação com transcricao.json utilizável", file=sys.stderr)
        return 1

    horas = sum(g.duracao for g in base) / 3600
    print(f"acervo: {acervo}")
    print(f"{len(base)} gravações transcritas, {horas:.1f} h de áudio")

    resultados = [relatar(t, analisar(acervo, t) if t != tamanhos[0] else base)
                  for t in tamanhos]

    if args.json:
        Path(args.json).write_text(
            json.dumps({"acervo": str(acervo), "rodadas": resultados},
                       ensure_ascii=False, indent=2),
            encoding="utf-8")
        print(f"\nbruto em {args.json}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
