#!/usr/bin/env python3
"""Os dois pipelines lado a lado, para julgar com o olho — T8

**Por que existe.** Os números dizem que o MOSS ganha em cobertura, em estrutura
e na ata (§7, §12 do ``docs/FASE7-RESULTADOS.md``). Nenhum deles diz **como o
texto fica para quem lê**. Fragmentação, quebra no meio da frase e rótulo de
falante são coisas que se veem melhor do que se medem.

**A diferença de carimbo importa aqui.** O app produz `word_timestamps` e usa
isso para **cortar o segmento na troca de falante** (``docs/FASE6.md`` §4.5); o
MOSS carimba por segmento. Este comparativo mostra o efeito prático dos dois.

**Como escolhe o trecho.** Não pega o começo — reunião começa com "bom dia" e
não distingue nada. Pega a janela com **mais troca de falante por minuto**, que
é onde os dois pipelines mais divergem e onde a atribuição é mais difícil.

Uso::

    uv run python tools/comparar_lado_a_lado.py --gravacao 2026-08-20_15-59-20
    uv run python tools/comparar_lado_a_lado.py --saida comparativo.md
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

ACERVO = Path("/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings")
COSTURA = Path.home() / ".cache/pulsemeet-medicoes/costura"
LIMIAR = 55

COM_GEMINI = ["2026-08-21_11-00-33", "2026-08-25_08-59-22",
              "2026-08-20_15-59-20", "2026-08-27_15-28-37"]


def ler(p: Path) -> list[dict]:
    if not p.is_file():
        return []
    return [s for s in json.loads(p.read_text(encoding="utf-8")).get("segments") or []
            if isinstance(s.get("start"), (int, float))]


def ler_gemini(p: Path) -> list[tuple[str, str]]:
    import sys
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import comparar_com_gemini as C
    return C.ler_gemini(p) if p.is_file() else []


def janela_movimentada(segs: list[dict], largura: float) -> tuple[float, float]:
    """A janela com mais trocas de falante — onde os pipelines mais divergem."""
    if not segs:
        return 0.0, largura
    fim = max(s["end"] for s in segs)
    melhor, pontos = (0.0, largura), -1
    passo = largura / 3
    t = 0.0
    while t + largura <= fim:
        dentro = [s for s in segs if t <= s["start"] < t + largura]
        trocas = sum(1 for a, b in zip(dentro, dentro[1:])
                     if a.get("speaker") != b.get("speaker"))
        if trocas > pontos:
            melhor, pontos = (t, t + largura), trocas
        t += passo
    return melhor


def recortar(segs: list[dict], a: float, b: float) -> list[dict]:
    return [s for s in segs if s["end"] > a and s["start"] < b]


def bloco(titulo: str, segs: list[dict]) -> list[str]:
    linhas = [f"### {titulo}", ""]
    if not segs:
        linhas += ["_(sem saída)_", ""]
        return linhas
    for s in segs:
        quem = (s.get("speaker") or "—").strip() or "—"
        linhas.append(f"`{s['start']:7.1f}` **{quem}** · {(s.get('text') or '').strip()}")
    linhas.append("")
    return linhas


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--gravacoes")
    ap.add_argument("--largura", type=float, default=90.0,
                    help="segundos do trecho mostrado")
    ap.add_argument("--saida", help="arquivo markdown; padrão: stdout")
    args = ap.parse_args()

    nomes = ([n.strip() for n in args.gravacoes.split(",")] if args.gravacoes
             else list(COM_GEMINI))
    out: list[str] = []

    for nome in nomes:
        pasta = ACERVO / nome
        app = ler(pasta / "transcricao.json")
        moss = ler(COSTURA / f"{nome}__costurado_{LIMIAR}" / "transcricao.json")
        if not app or not moss:
            continue

        a, b = janela_movimentada(app, args.largura)
        out += [f"## {nome} — de {a/60:.1f} a {b/60:.1f} min", "",
                f"A janela com mais troca de falante da gravação.", ""]
        out += bloco(f"Pipeline de hoje · {len(recortar(app, a, b))} segmentos",
                     recortar(app, a, b))
        out += bloco(f"MOSS costurado · {len(recortar(moss, a, b))} segmentos",
                     recortar(moss, a, b))

        gem = ler_gemini(pasta / "gemini.md")
        if gem:
            out += ["### Gemini (referência, sem carimbo de tempo)", ""]
            out += [f"**{q}** · {t}" for q, t in gem[:12]] + [""]
        out += ["---", ""]

    texto = "\n".join(out)
    if args.saida:
        Path(args.saida).write_text(texto, encoding="utf-8")
        print(f"{args.saida}  ({len(out)} linhas)")
    else:
        print(texto)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
