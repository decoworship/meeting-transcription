#!/usr/bin/env python3
"""Converte um ``transcricao.json`` para o formato de export do Gemini.

**Por que existe.** As duas réguas do projeto —
[`comparar_com_gemini.py`](comparar_com_gemini.py) e
[`wer_contra_gemini.py`](wer_contra_gemini.py) — leem uma referência no formato
``**Falante:** texto``. Só quatro gravações do acervo têm esse export, e as
quatro são do lado fácil: 3, 3, 3 e 6 falantes. As difíceis — 61 min com 12
pessoas, 122 min com 8 — não têm nenhuma.

Isto põe a **saída do próprio app** naquele formato, e assim ela vira referência
para as mesmas réguas, sem escrever comparação nova.

**O que isso mede, e o que não mede.** Com o app como referência, o número deixa
de ser qualidade e passa a ser **divergência**: quanto um candidato se afasta do
que temos hoje. Não diz quem está certo — para isso é preciso um terceiro
independente, que é o papel do Gemini. Serve para detectar desabamento: um
candidato que diverge 60% numa reunião de 12 falantes e 25% numa de 3 está
degradando com o número de pessoas, e isso se vê sem gabarito.

Uso::

    uv run python tools/como_gemini.py <pasta-da-gravacao> > referencia.md
    uv run python tools/como_gemini.py <pasta> --saida <pasta>/app.md
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


def converter(dados: dict) -> str:
    """Um turno por linha, juntando segmentos seguidos do mesmo falante.

    Juntar importa: a régua alinha filas de palavras, e um falante partido em
    vinte segmentos curtos produz os mesmos pares que um turno só. Mas o export
    do Gemini vem por turno, e manter as duas formas parecidas evita que a
    diferença de granularidade apareça como divergência.
    """
    linhas: list[str] = []
    atual: str | None = None
    buffer: list[str] = []

    for s in dados.get("segments") or []:
        texto = (s.get("text") or "").strip()
        if not texto:
            continue
        quem = (s.get("speaker") or "").strip() or "Desconhecido"
        if quem != atual:
            if atual is not None and buffer:
                linhas.append(f"**{atual}:** {' '.join(buffer)}")
            atual, buffer = quem, []
        buffer.append(texto)

    if atual is not None and buffer:
        linhas.append(f"**{atual}:** {' '.join(buffer)}")
    return "\n\n".join(linhas) + "\n"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("pasta", type=Path)
    ap.add_argument("--saida", type=Path, help="padrão: stdout")
    args = ap.parse_args()

    alvo = args.pasta / "transcricao.json" if args.pasta.is_dir() else args.pasta
    if not alvo.is_file():
        print(f"não achei {alvo}", file=sys.stderr)
        return 1

    md = converter(json.loads(alvo.read_text(encoding="utf-8", errors="replace")))
    if args.saida:
        args.saida.write_text(md, encoding="utf-8")
        print(f"{args.saida}  ({len(md.splitlines())} linhas)", file=sys.stderr)
    else:
        sys.stdout.write(md)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
