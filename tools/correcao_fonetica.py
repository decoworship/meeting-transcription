"""Recupera termos do vocabulário grafados errado, por similaridade fonética.

Motivação medida (FASE0, resultado 5): o nome "Dimi" sai como **"Jimmy" 10
vezes** nos dois motores quando não há vocabulário injetado, e como **"Dimmy" 3
vezes** no whisper.cpp mesmo com prompt. São erros de *grafia de som parecido*,
não de compreensão — o modelo ouviu certo e escreveu errado.

Isso importa por dois motivos:

1. **é conserto a jusante, no núcleo**, e portanto vale igual para
   faster-whisper e whisper.cpp — some a dependência de um mecanismo de
   vocabulário que varia entre implementações;
2. **corrige uma falha de medição**: contar termos por string exata marcou 5/9
   para o whisper.cpp quando o desempenho fonético era 8/9.

O casamento é conservador de propósito. Um falso positivo aqui **reescreve uma
palavra que o usuário disse**, o que é pior que deixar o erro: quem lê a ata não
tem como desconfiar. Por isso exige simultaneamente código fonético igual e
distância de edição pequena.

Uso::

    python tools/correcao_fonetica.py --texto arquivo.json
    python tools/correcao_fonetica.py --candidatos   # o que casaria, sem aplicar
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import unicodedata
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(RAIZ / "tools"))


def desacentuar(s: str) -> str:
    s = unicodedata.normalize("NFKD", s.lower())
    return "".join(c for c in s if not unicodedata.combining(c))


# Regras de equivalência sonora do português brasileiro. Ordem importa: as
# substituições de dígrafo vêm antes das de letra isolada.
_REGRAS = [
    (r"ph", "f"), (r"ch", "x"), (r"lh", "l"), (r"nh", "n"),
    (r"qu", "k"), (r"gu", "g"), (r"ss", "s"), (r"sc", "s"), (r"ç", "s"),
    (r"rr", "r"), (r"mm", "m"), (r"nn", "n"), (r"tt", "t"), (r"dd", "d"),
    (r"[cq]", "k"), (r"z", "s"), (r"[jg](?=[ei])", "j"), (r"g", "g"),
    (r"y", "i"), (r"w", "v"), (r"h", ""),
    # /d/ e /dʒ/ antes de i colapsam na fala carioca/paulista ("Dimi"~"Jimi"),
    # que é exatamente o caso "Dimi"→"Jimmy".
    (r"d(?=i)", "j"),
]

# Uma regra de "remover vogal final" foi testada e **removida**: ela fazia
# "fixo" e "Fixa" colidirem, e o corretor reescrevia "Do IP fixo" (português
# legítimo numa reunião de telecom) como "Fixa". Os casos verdadeiros não
# precisam dela — "Dimi", "Dimmy" e "Jimmy" já convergem para "jimi" pelas
# regras acima. Falso positivo aqui reescreve o que a pessoa disse, e quem lê a
# ata não tem como desconfiar; o custo é assimétrico.


def foneticar(palavra: str) -> str:
    p = desacentuar(palavra)
    for padrao, troca in _REGRAS:
        p = re.sub(padrao, troca, p)
    # Colapsa letras repetidas ("jimmi" -> "jimi")
    return re.sub(r"(.)\1+", r"\1", p)


def _levenshtein(a: str, b: str) -> int:
    if len(a) < len(b):
        a, b = b, a
    ant = list(range(len(b) + 1))
    for i, x in enumerate(a, 1):
        cur = [i]
        for j, y in enumerate(b, 1):
            cur.append(min(ant[j] + 1, cur[j - 1] + 1, ant[j - 1] + (x != y)))
        ant = cur
    return ant[-1]


def dist_maxima(termo: str) -> int:
    """Teto de distância de edição na forma escrita.

    **Escalar pelo tamanho do termo foi testado e rejeitado.** A ideia era que
    distância 3 é folgada para nome de 4 letras; medido, ela derruba justamente o
    caso central: ``levenshtein("Jimmy", "Dimi") == 3`` num termo de 4 letras.
    Com o teto escalado (1 para ≤4 letras), as 10 correções de Jimmy→Dimi
    desapareciam e sobrava 1 troca em todo o corpus.

    A lição: **quando o código fonético já é idêntico, a distância de superfície
    é um guarda ruim** — grafias de som igual podem divergir muito na escrita, e é
    exatamente isso que se quer capturar. Quem filtra de verdade é a combinação
    de código fonético com a exigência de capitalização (ver ``corrigir``): o
    falso positivo ``fixo`` → ``Fixa`` é bloqueado por ser minúsculo, sem precisar
    de teto apertado.

    O teto fica generoso e só evita casamento absurdo em termo longo.
    """
    n = len(desacentuar(termo))
    return 3 if n <= 10 else 4


def casa(candidato: str, termo: str, max_dist: int | None = None) -> bool:
    """Decide se `candidato` é grafia errada de `termo`.

    Duas condições, ambas necessárias:

    * **código fonético idêntico** — garante que soam igual;
    * **distância de edição pequena** na forma escrita — impede que palavras
      curtas de som parecido mas sentido distinto sejam trocadas.
    """
    if candidato.lower() == termo.lower():
        return False                              # já está certo
    if foneticar(candidato) != foneticar(termo):
        return False
    limite = dist_maxima(termo) if max_dist is None else max_dist
    return _levenshtein(desacentuar(candidato), desacentuar(termo)) <= limite


def corrigir(texto: str, termos: list[str], max_dist: int | None = None,
             excecoes: set[str] | None = None,
             so_capitalizadas: bool = True) -> tuple[str, list[dict]]:
    """Substitui grafias erradas pelos termos do vocabulário.

    ``so_capitalizadas`` é o guarda que faltava na primeira versão: o docstring
    prometia "só palavras capitalizadas ou fora do léxico comum", mas o código
    candidatava qualquer palavra — e foi por essa porta que ``fixo`` → ``Fixa``
    quase passou. Exigir maiúscula no meio da frase aproveita um sinal que já
    está no texto: o Whisper capitaliza nome próprio.

    Não resolve o caso de termo minúsculo (``bill_invoice``); para esses, o
    guarda correto é ausência de um léxico pt-BR (hunspell), que fica para a
    versão de produto.

    Devolve o texto corrigido e a **lista de trocas com posição**, para a UI
    poder marcar cada substituição — se quem lê a ata não tem como desconfiar,
    tem que poder inspecionar.
    """
    excecoes = {e.lower() for e in (excecoes or set())}
    trocas: list[dict] = []

    def repor(m: re.Match) -> str:
        pal = m.group(0)
        if pal.lower() in excecoes:
            return pal
        # Início de frase também é maiúsculo, então maiúscula só vale como sinal
        # quando há texto antes. Sem isso, "Fixo" abrindo frase seria candidato.
        anterior = texto[:m.start()].rstrip()
        meio_de_frase = bool(anterior) and anterior[-1] not in ".!?"
        if so_capitalizadas and not (pal[0].isupper() and meio_de_frase):
            return pal
        for t in termos:
            if casa(pal, t, max_dist):
                trocas.append({"de": pal, "para": t, "posicao": m.start()})
                return t
        return pal

    return re.sub(r"\b[A-Za-zÀ-ÿ][A-Za-zÀ-ÿ']{2,}\b", repor, texto), trocas


def main() -> None:
    from benchmark_vocab import termos as termos_do_config, contar_em

    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--config", type=Path,
                   default=RAIZ / "data/meeting-transcription/config.json")
    p.add_argument("--saida", type=Path, default=RAIZ / "data/bench-vocab/out")
    p.add_argument("--referencia", type=Path,
                   default=RAIZ / "data/meeting-transcription/history/1786026866038.json")
    p.add_argument("--max-dist", type=int, default=None,
                   help="fixo; padrão é escalar pelo tamanho do termo")
    args = p.parse_args()

    lista = termos_do_config(args.config)
    ref = json.loads(args.referencia.read_text(encoding="utf-8"))
    ref_txt = " ".join(s.get("text", "") for s in ref.get("segments", []))
    c_ref = contar_em(ref_txt, lista)
    total_ref = sum(c_ref.values())

    print(f"referência: {total_ref} ocorrências de vocabulário\n")
    print(f"{'motor':34s} {'antes':>7s} {'depois':>7s} {'ganho':>7s}   trocas aplicadas")
    print("-" * 100)

    for caminho in sorted(args.saida.glob("*.json")):
        d = json.loads(caminho.read_text(encoding="utf-8"))
        if "texto" not in d:
            continue
        antes = sum(contar_em(d["texto"], lista).values())
        novo, trocas = corrigir(d["texto"], lista, args.max_dist)
        depois = sum(contar_em(novo, lista).values())
        from collections import Counter
        cont = Counter(f"{t['de']} -> {t['para']}" for t in trocas)
        det = ", ".join(f"{k}×{v}" for k, v in cont.most_common(4))
        print(f"{d['motor'][:34]:34s} {antes:7d} {depois:7d} {depois-antes:+7d}   {det}")


if __name__ == "__main__":
    main()
