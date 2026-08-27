"""Divergência de palavra entre a nossa transcrição e a do Gemini/Meet.

**Isto não é WER.** WER exige gabarito, e o Gemini não é gabarito — ele erra por
conta própria, e nas gravações medidas errou coisas que nós acertamos ("salos
coach" onde escrevemos "sales coach") e acertou coisas que erramos ("2%" onde
escrevemos "12%"). O que se mede aqui é **quanto duas transcrições independentes
discordam**.

Para o que serve, então: **comparar variantes nossas entre si**. Se a variante A
diverge menos do Gemini que a B, e as duas foram feitas do mesmo áudio, é
evidência de que A está mais perto do que foi dito — porque a alternativa é
supor que A e o Gemini erraram juntos, do mesmo jeito, por acaso.

Para o que NÃO serve: dizer a taxa de erro do app. O número absoluto não tem
significado, só a diferença entre duas colunas.

As três parcelas são separadas de propósito, porque doem diferente:

* **trocadas** — a palavra saiu errada. É o que estraga nome próprio e número;
* **faltando** — está no Gemini e não em nós. É VAD cortando fala, e é o erro
  caro: o que não foi transcrito não existe para a ata;
* **sobrando** — está em nós e não no Gemini. Pode ser alucinação nossa, mas
  também é o Gemini descartando backchannel, então ele é o mais ambíguo dos três.

Uso::

    python tools/wer_contra_gemini.py <pasta>...
    python tools/wer_contra_gemini.py varredura/A varredura/B varredura/C
"""

from __future__ import annotations

import argparse
import json
import sys
from difflib import SequenceMatcher
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import comparar_com_gemini as C   # noqa: E402


def nossas(pasta: Path) -> list[str]:
    dados = json.loads((pasta / "transcricao.json").read_text(encoding="utf-8"))
    return [p for s in dados.get("segments") or [] for p in C.palavras(s.get("text") or "")]


def deles(pasta: Path, gemini: Path | None) -> list[str]:
    arq = gemini or (pasta / "gemini.md")
    return [p for _, t in C.ler_gemini(arq) for p in C.palavras(t)]


def medir(a: list[str], b: list[str]) -> dict:
    """Alinha as duas filas e conta o que cada operação custou.

    ``b`` é a referência. Os nomes seguem WER — substituição, deleção,
    inserção — porque a aritmética é a mesma, mesmo sem gabarito.
    """
    m = SequenceMatcher(None, a, b, autojunk=False)
    iguais = trocadas = faltando = sobrando = 0

    for tag, i1, i2, j1, j2 in m.get_opcodes():
        n, r = i2 - i1, j2 - j1
        if tag == "equal":
            iguais += n
        elif tag == "replace":
            trocadas += min(n, r)
            sobrando += max(0, n - r)
            faltando += max(0, r - n)
        elif tag == "delete":
            sobrando += n
        elif tag == "insert":
            faltando += r

    ref = len(b) or 1
    return {
        "nossas": len(a), "referencia": len(b), "iguais": iguais,
        "trocadas": trocadas, "faltando": faltando, "sobrando": sobrando,
        "divergencia": 100.0 * (trocadas + faltando + sobrando) / ref,
        "cobertura": 100.0 * iguais / ref,
    }


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("pastas", nargs="+", type=Path)
    p.add_argument("--gemini", type=Path, help="o export, quando não estiver na pasta")
    args = p.parse_args()

    print(f"\n{'variante':<22} {'nossas':>7} {'ref':>6} {'iguais':>7} "
          f"{'trocadas':>9} {'faltando':>9} {'sobrando':>9} {'diverg.':>8} {'cobert.':>8}")
    print("-" * 96)

    for pasta in args.pastas:
        if not (pasta / "transcricao.json").exists():
            print(f"{pasta.name:<22} sem transcricao.json"); continue
        ref = deles(pasta, args.gemini)
        if not ref:
            print(f"{pasta.name:<22} sem gemini.md"); continue

        m = medir(nossas(pasta), ref)
        print(f"{pasta.name:<22} {m['nossas']:>7} {m['referencia']:>6} {m['iguais']:>7} "
              f"{m['trocadas']:>9} {m['faltando']:>9} {m['sobrando']:>9} "
              f"{m['divergencia']:>7.1f}% {m['cobertura']:>7.1f}%")

    print("\nMenor divergência e maior cobertura são melhores. O número absoluto não\n"
          "significa taxa de erro — o Gemini não é gabarito. Só a comparação entre\n"
          "linhas do mesmo áudio tem sentido.\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
