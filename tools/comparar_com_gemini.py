"""Compara nossa diarização com a do Gemini/Meet, palavra a palavra.

Por que existe: a [FASE6.md](../docs/FASE6.md) §5 chama a transcrição nativa do
Meet de a fonte paralela mais valiosa, e por um motivo específico — **os rótulos
de falante dela não são estimativa**. Cada participante entra pelo próprio canal,
então quem falou é dado do sistema de conferência, não inferência acústica. É a
única referência de diarização em reunião real que se consegue sem anotar à mão;
até aqui, DER só era medível no acervo AMI da Fase 0
([benchmark_der.py](benchmark_der.py)).

**Isto não é DER.** DER é fração de *tempo* errado e trata silêncio, sobreposição
e fronteira. Aqui a métrica é **fração de palavras alinhadas com o falante
errado** — mais grosseira, e suficiente para a pergunta que interessa: *o texto
que chega ao prompt da ata está atribuído a quem?* É esse texto, e não o áudio,
que produz a troca entre quem pede e quem executa uma tarefa.

**Os timestamps das duas fontes não batem** — o Meet ancora no início da chamada
e nós no início da gravação, e a diferença medida em 20/08 e 21/08 foi de cerca
de um minuto e meio. Por isso o alinhamento é **por texto**, não por relógio.

Como funciona:

1. cada lado vira uma fila de palavras normalizadas, cada palavra carregando o
   falante que a disse;
2. ``SequenceMatcher`` casa as duas filas — é alinhamento de sequência, então
   aguenta palavra a mais, palavra a menos e ASR divergente;
3. os nomes dos dois lados são casados por primeiro nome sem acento, e o que
   sobra por maior co-ocorrência (guloso). Sem isto, "André Yuri" e "Andre Yuri"
   contariam como dois falantes;
4. só as palavras que casaram entram na conta. Palavra que só um lado ouviu é
   erro de ASR ou de VAD, não de diarização, e é reportada à parte.

Uso::

    python tools/comparar_com_gemini.py <pasta-da-gravacao>
    python tools/comparar_com_gemini.py <pasta> --gemini outro.md --trechos 20
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import unicodedata
from collections import Counter, defaultdict
from difflib import SequenceMatcher
from pathlib import Path

FALA = re.compile(r"^\*\*(?P<quem>[^*]{2,60}?):?\*\*:?\s*(?P<texto>.*)$")
CABECALHO = re.compile(r"^\s*(#|>|\*[^*])")


def sem_acento(t: str) -> str:
    return "".join(
        c for c in unicodedata.normalize("NFD", t) if unicodedata.category(c) != "Mn"
    )


def palavras(texto: str) -> list[str]:
    """Só o que carrega conteúdo comparável: letras e dígitos, sem acento."""
    return re.findall(r"[a-z0-9]+", sem_acento((texto or "").lower()))


def primeiro(nome: str) -> str:
    partes = sem_acento((nome or "").strip().lower()).split()
    return partes[0] if partes else ""


# ------------------------------------------------------------------ leitura

def ler_gemini(caminho: Path) -> list[tuple[str, str]]:
    """(falante, texto) por turno, na ordem em que aparecem no export."""
    turnos: list[tuple[str, str]] = []
    for linha in caminho.read_text(encoding="utf-8", errors="replace").splitlines():
        linha = linha.strip()
        if not linha or CABECALHO.match(linha):
            continue
        if (m := FALA.match(linha)) and (texto := m.group("texto").strip()):
            turnos.append((m.group("quem").strip(), texto))
    return turnos


def ler_nosso(caminho: Path) -> list[tuple[str, str]]:
    dados = json.loads(caminho.read_text(encoding="utf-8", errors="replace"))
    return [
        (s.get("speaker") or "?", s.get("text") or "")
        for s in dados.get("segments") or []
        if (s.get("text") or "").strip()
    ]


def fila(turnos: list[tuple[str, str]]) -> tuple[list[str], list[str]]:
    """Fila de palavras e, em paralelo, de quem disse cada uma."""
    texto: list[str] = []
    quem: list[str] = []
    for falante, t in turnos:
        for p in palavras(t):
            texto.append(p)
            quem.append(falante)
    return texto, quem


# --------------------------------------------------------------- alinhamento

def casar_nomes(pares: list[tuple[str, str]]) -> dict[str, str]:
    """Mapa nosso -> Gemini. Primeiro nome quando dá; o resto, por co-ocorrência.

    O casamento por co-ocorrência é guloso e não ótimo. É de propósito: um
    emparelhamento ótimo esconderia o caso em que a nossa diarização fundiu duas
    pessoas numa só, que é justamente o erro que se quer ver.
    """
    juntos: dict[str, Counter] = defaultdict(Counter)
    for nosso, deles in pares:
        juntos[nosso][deles] += 1

    mapa: dict[str, str] = {}
    for nosso, contagem in juntos.items():
        iguais = [d for d in contagem if primeiro(d) == primeiro(nosso)]
        mapa[nosso] = iguais[0] if iguais else contagem.most_common(1)[0][0]
    return mapa


def comparar(nosso: list[tuple[str, str]], deles: list[tuple[str, str]]):
    a_txt, a_quem = fila(nosso)
    b_txt, b_quem = fila(deles)

    casador = SequenceMatcher(None, a_txt, b_txt, autojunk=False)
    pares: list[tuple[int, int]] = []
    for i, j, n in casador.get_matching_blocks():
        pares += [(i + k, j + k) for k in range(n)]

    mapa = casar_nomes([(a_quem[i], b_quem[j]) for i, j in pares])

    acertos = 0
    certos: list[int] = []
    confusao: Counter = Counter()
    erros: list[tuple[int, str, str, str]] = []
    for i, j in pares:
        esperado = b_quem[j]
        obtido = mapa.get(a_quem[i], a_quem[i])
        if obtido == esperado:
            acertos += 1
            certos.append(i)
        else:
            confusao[(a_quem[i], esperado)] += 1
            erros.append((i, a_txt[i], a_quem[i], esperado))

    return {
        "nossas_palavras": len(a_txt),
        "palavras_gemini": len(b_txt),
        "alinhadas": len(pares),
        "acertos": acertos,
        "certos": certos,
        "confusao": confusao,
        "erros": erros,
        "mapa": mapa,
        "a_txt": a_txt,
        "a_quem": a_quem,
    }


def trechos(r: dict, teto: int) -> list[str]:
    """Agrupa palavras erradas seguidas num trecho só, que é como se lê."""
    saida: list[str] = []
    atual: list[tuple[int, str, str, str]] = []
    for e in r["erros"]:
        if atual and e[0] == atual[-1][0] + 1 and e[2] == atual[-1][2]:
            atual.append(e)
        else:
            if atual:
                saida.append(atual)
            atual = [e]
    if atual:
        saida.append(atual)

    saida.sort(key=len, reverse=True)
    linhas = []
    for grupo in saida[:teto]:
        texto = " ".join(p for _, p, _, _ in grupo)
        linhas.append(f"  {len(grupo):>3} palavra(s)  nós: {grupo[0][2]:<18} "
                      f"Gemini: {grupo[0][3]:<18} “{texto[:60]}”")
    return linhas


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("pasta", type=Path, help="pasta da gravação")
    p.add_argument("--gemini", type=Path, help="o export (padrão: <pasta>/gemini.md)")
    p.add_argument("--trechos", type=int, default=12, help="quantos trechos mostrar")
    args = p.parse_args()

    nosso_arq = args.pasta / "transcricao.json"
    gemini_arq = args.gemini or (args.pasta / "gemini.md")
    for f in (nosso_arq, gemini_arq):
        if not f.exists():
            print(f"não achei {f}", file=sys.stderr)
            return 1

    nosso = ler_nosso(nosso_arq)
    deles = ler_gemini(gemini_arq)
    if not deles:
        print(f"{gemini_arq} não tem turnos no formato **Falante:** texto",
              file=sys.stderr)
        return 1

    r = comparar(nosso, deles)
    al, ac = r["alinhadas"], r["acertos"]
    if al == 0:
        print("as duas transcrições não têm texto em comum.", file=sys.stderr)
        return 1

    print(f"\n{args.pasta.name}\n")
    print(f"  nossos segmentos      {len(nosso):>6}")
    print(f"  turnos do Gemini      {len(deles):>6}")
    print(f"  nossas palavras       {r['nossas_palavras']:>6}")
    print(f"  palavras do Gemini    {r['palavras_gemini']:>6}")
    print(f"  alinhadas             {al:>6}  "
          f"({100 * al // max(r['nossas_palavras'], 1)}% das nossas)")
    print()
    print(f"  falante certo         {ac:>6}  ({100 * ac / al:.1f}%)")
    print(f"  falante errado        {al - ac:>6}  ({100 * (al - ac) / al:.1f}%)")

    print("\n  por falante do Gemini (quanto da fala dele nós acertamos):")
    por_falante: dict[str, list[int]] = defaultdict(lambda: [0, 0])
    for pos in r["certos"]:
        por_falante[r["mapa"].get(r["a_quem"][pos], r["a_quem"][pos])][0] += 1
    for pos, _p, _n, esperado in r["erros"]:
        por_falante[esperado][1] += 1
    for quem, (ok, ruim) in sorted(
            por_falante.items(), key=lambda kv: -(kv[1][0] + kv[1][1])):
        tot = ok + ruim
        print(f"    {quem:<20} {ok:>5}/{tot:<5} palavras  "
              f"{100 * ok / max(tot, 1):>5.1f}% certas")

    print("\n  quem vira quem (nós -> Gemini), por palavra:")
    for (nos, eles), n in r["confusao"].most_common(10):
        print(f"    {n:>4}  {nos:<20} deveria ser {eles}")

    print(f"\n  os {args.trechos} maiores trechos com falante errado:")
    for linha in trechos(r, args.trechos):
        print(linha)

    d = diagnosticar(nosso, deles, r)
    c, total = d["classes"], d["segmentos"]
    print(f"\n  os {total} segmentos nossos, por tipo de erro:")
    for classe, rotulo in (
        ("limpo", "falante certo, sem respingo do vizinho"),
        ("respingo", "falante certo, ponta do vizinho junto  (fronteira mal posta)"),
        ("trocado", "fala inteira no falante errado          (atribuição)"),
        ("misturado", "duas ou mais pessoas no mesmo segmento   (sub-segmentação)"),
        ("sem_alinhamento", "sem par no Gemini — fora da conta"),
    ):
        n = c.get(classe, 0)
        if n or classe in ("trocado", "misturado"):
            print(f"    {n:>4}  ({100 * n / max(total, 1):>4.1f}%)  {rotulo}")
    print("\n  sem depender do corte de 0,8:")
    print(f"    {c.get('_fundido', 0):>4}  ({100 * c.get('_fundido', 0) / max(total, 1):>4.1f}%)"
          "  segmentos com fala de mais de uma pessoa dentro")
    print(f"    {c.get('_rotulo_errado', 0):>4}  ({100 * c.get('_rotulo_errado', 0) / max(total, 1):>4.1f}%)"
          "  segmentos cujo rótulo não é o falante dominante")
    for classe in ("trocado", "misturado"):
        for ex in d["exemplos"].get(classe, []):
            print(f"         {classe}: {ex}")

    faltando = r["nossas_palavras"] - al
    print(f"\n  {faltando} palavra(s) nossas não casaram com nada do Gemini "
          "— é ASR ou VAD divergente, não diarização.\n")
    return 0


def diagnosticar(nosso: list[tuple[str, str]], deles: list[tuple[str, str]],
                 r: dict) -> dict:
    """Classifica cada segmento nosso pelo tipo de erro, não pela posição dele.

    **A primeira versão disto media posição da palavra errada dentro do
    segmento, e o número saiu enganoso.** Com 49% dos nossos segmentos tendo
    quatro palavras ou menos, toda palavra está a menos de três de uma borda —
    então "erro na borda" dava 97% por construção, inclusive para segmentos
    inteiramente atribuídos à pessoa errada. Medir posição não distingue nada
    quando o segmento é do tamanho da janela.

    O que distingue é comparar o **rótulo do segmento** com o falante que de fato
    domina as palavras dele:

    * ``limpo`` — o nosso rótulo é o dominante e não há respingo;
    * ``respingo`` — o rótulo é o dominante, mas uma ou duas palayras da ponta
      são do vizinho. É fronteira mal posta: custa pouco e se costura depois;
    * ``trocado`` — um único falante domina o segmento e **não** é o que
      escrevemos. É atribuição errada de fala inteira, e só o modelo acústico
      resolve;
    * ``misturado`` — nenhum falante domina: duas ou mais pessoas dentro de um
      segmento nosso. É sub-segmentação, e é o pior dos três para a ata, porque
      põe na boca de alguém uma frase que é de outro.
    """
    # posição nossa -> falante do Gemini, só onde alinhou
    ref: dict[int, str] = {}
    for pos, _palavra, _nosso_quem, esperado in r["erros"]:
        ref[pos] = esperado
    for pos in r["certos"]:
        ref[pos] = r["mapa"].get(r["a_quem"][pos], r["a_quem"][pos])

    limites: list[tuple[int, int, str]] = []
    i = 0
    for falante, texto in nosso:
        n = len(palavras(texto))
        if n:
            limites.append((i, i + n - 1, falante))
        i += n

    classes: Counter = Counter()
    exemplos: dict[str, list[str]] = defaultdict(list)
    for ini, fim, nosso_quem in limites:
        vistos = [ref[p] for p in range(ini, fim + 1) if p in ref]
        if not vistos:
            classes["sem_alinhamento"] += 1
            continue
        dominante, quantos = Counter(vistos).most_common(1)[0]
        fracao = quantos / len(vistos)
        esperado = r["mapa"].get(nosso_quem, nosso_quem)

        if fracao < 0.8:
            classe = "misturado"
        elif esperado != dominante:
            classe = "trocado"
        elif quantos < len(vistos):
            classe = "respingo"
        else:
            classe = "limpo"
        classes[classe] += 1
        # Dois números que não dependem do corte de 0,8, e por isso são os que
        # eu reportaria se tivesse de escolher um só:
        if len(set(vistos)) > 1:
            classes["_fundido"] += 1          # tem fala de mais de uma pessoa
        if esperado != dominante:
            classes["_rotulo_errado"] += 1    # o rótulo não é o falante dominante
        if classe != "limpo" and len(exemplos[classe]) < 3:
            trecho = " ".join(r["a_txt"][ini:fim + 1])[:64]
            exemplos[classe].append(f"{nosso_quem} -> {dominante}: “{trecho}”")

    return {"classes": classes, "exemplos": exemplos, "segmentos": len(limites)}


if __name__ == "__main__":
    raise SystemExit(main())
