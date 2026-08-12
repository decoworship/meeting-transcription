"""Intervalo de confiança para diferença de WER entre dois motores.

Existe porque os ICs citados no FASE0 foram calculados em linha de comando e
não eram reproduzíveis a partir do repositório — crítica justa de revisão.

Reamostragem (bootstrap) sobre as **unidades de avaliação**, com uma ressalva
que importa: reamostrar 4 ou 6 passagens produz um intervalo instável, porque há
pouquíssimas combinações distintas. O IC estreito que sai daí é artefato do
tamanho da amostra, não precisão. Com poucas unidades, prefira o teste de sinal
(quantas unidades cada motor ganha), que ao menos não finge precisão.

Uso::

    python tools/bootstrap_ic.py --corpus bench/corpus \\
        --a bench/corpus/hip_faster-whisper-large-v3.json \\
        --b "bench/corpus/hip_whisper.cpp large-v3-q5_0 (CUDA).json"
"""

from __future__ import annotations

import argparse
import json
import random
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from benchmark_wer import _levenshtein, normalizar  # noqa: E402


def carregar(caminho: Path) -> tuple[str, dict[str, str]]:
    d = json.loads(caminho.read_text(encoding="utf-8"))
    return d["motor"], {h["id"]: h["texto"] for h in d["hipoteses"]}


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--corpus", type=Path, required=True)
    p.add_argument("--a", type=Path, required=True, help="motor de referência")
    p.add_argument("--b", type=Path, required=True, help="candidato")
    p.add_argument("--n", type=int, default=4000)
    p.add_argument("--semente", type=int, default=42)
    args = p.parse_args()

    manifesto = json.loads((args.corpus / "manifesto.json").read_text(encoding="utf-8"))
    nome_a, ha = carregar(args.a)
    nome_b, hb = carregar(args.b)

    # (erros, unidades) por item, para reamostrar itens inteiros — reamostrar
    # palavras soltas quebraria a dependência dentro de um enunciado.
    par_a, par_b = [], []
    for m in manifesto:
        r = normalizar(m["referencia"]).split()
        par_a.append((_levenshtein(r, normalizar(ha.get(m["id"], "")).split()), len(r)))
        par_b.append((_levenshtein(r, normalizar(hb.get(m["id"], "")).split()), len(r)))

    def wer(pares, indices):
        e = sum(pares[i][0] for i in indices)
        u = sum(pares[i][1] for i in indices)
        return e / u if u else 0.0

    todos = list(range(len(manifesto)))
    wa, wb = wer(par_a, todos), wer(par_b, todos)
    print(f"unidades: {len(manifesto)}  |  palavras: {sum(x[1] for x in par_a)}")
    print(f"{nome_a[:38]:40s} WER {100*wa:.2f}%")
    print(f"{nome_b[:38]:40s} WER {100*wb:.2f}%")
    print(f"{'diferença (a - b)':40s}     {100*(wa-wb):+.2f} pts")

    rnd = random.Random(args.semente)
    difs, vitorias = [], 0
    for _ in range(args.n):
        am = [rnd.randrange(len(todos)) for _ in todos]
        d = wer(par_a, am) - wer(par_b, am)
        difs.append(100 * d)
        vitorias += d > 0
    difs.sort()
    lo, hi = difs[int(0.025 * args.n)], difs[int(0.975 * args.n)]
    print()
    print(f"bootstrap ({args.n} reamostragens de {len(todos)} unidades):")
    print(f"  candidato melhor em {100*vitorias/args.n:.1f}% das reamostragens")
    print(f"  IC 95% da diferença: [{lo:+.2f}, {hi:+.2f}] pts")

    ganhos = sum(1 for x, y in zip(par_a, par_b)
                 if y[1] and x[0] / x[1] > y[0] / y[1])
    print(f"  teste de sinal: candidato ganha em {ganhos}/{len(todos)} unidades")
    if len(todos) < 15:
        print()
        print("  ⚠️  com menos de ~15 unidades o IC do bootstrap é instável:")
        print("      poucas combinações distintas, intervalo estreito por artefato.")
        print("      Leia o teste de sinal como evidência principal.")


if __name__ == "__main__":
    main()
