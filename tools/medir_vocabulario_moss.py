#!/usr/bin/env python3
"""O MOSS acerta o vocabulário sem hotwords? — T1.5

**O problema, e ele é de bloqueio.** O app injeta o vocabulário do projeto como
``hotwords`` no faster-whisper, e a [FASE6.md](../docs/FASE6.md) §4.1 mostra que
é isso que salva nome próprio e sigla — o caso "Dimi/Jimmy" que originou o
projeto. **Nenhum runtime ggml expõe o hotword do MOSS:**

* no ``transcribe.cpp``, ``Model.supports("initial_prompt")`` devolve **False**
  para o MOSS;
* no ``moss-transcribe.cpp``, o prompt está **embutido no GGUF como metadado**,
  sem flag de sobrescrita nem API.

Então adotar o MOSS por esse caminho **perde a feature de vocabulário**. Mas
perder o mecanismo só importa se o mecanismo estiver fazendo diferença — e é
isso que se mede aqui.

**O desenho é o mesmo do [`benchmark_vocab.py`](benchmark_vocab.py)**, que já
tinha a lição: *"sem os braços 'sem prompt' não dá para separar 'o motor já
acertaria sozinho' de 'o mecanismo funcionou'"*. Três colunas:

* **app** — faster-whisper **com** hotwords, a saída real do produto;
* **MOSS** — **sem** hotword nenhum, porque não há como passar;
* **Gemini** — a referência independente, que diz qual das duas contagens está
  certa. Sem ela, mais ocorrências poderia ser acerto ou alucinação.

A métrica é **contagem de ocorrências por termo**, não WER: um nome próprio
errado custa uma palavra no WER e custa a utilidade inteira do parágrafo para
quem lê a ata.

Uso::

    uv run python tools/medir_vocabulario_moss.py
    uv run python tools/medir_vocabulario_moss.py --gravacoes 2026-08-21_11-00-33
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import unicodedata
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import comparar_com_gemini as C  # noqa: E402

ACERVO_PADRAO = "/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings"
PROJETOS = Path("/mnt/c/Users/andre/.meeting-transcription/projects.json")
VARREDURA_MOSS = Path.home() / ".cache/pulsemeet-medicoes/varredura-moss"

#: Só as que têm Gemini — sem a terceira coluna a comparação não decide nada.
GRAVACOES = ["2026-08-21_11-00-33", "2026-08-25_08-59-22",
             "2026-08-20_15-59-20", "2026-08-27_15-28-37"]

#: Termos de até 3 letras dão falso positivo dentro de outras palavras. Mesmo
#: filtro do benchmark_vocab.py — mudá-lo mudaria a comparação com ele.
MINIMO_DE_LETRAS = 4


def desacentuar(s: str) -> str:
    s = unicodedata.normalize("NFKD", s.lower())
    return "".join(c for c in s if not unicodedata.combining(c))


def vocabulario_de(cliente: str, projeto: str) -> list[str]:
    """Os termos do projeto, como o app os injeta."""
    d = json.loads(PROJETOS.read_text(encoding="utf-8"))
    no = (d.get("clients", {}).get(cliente, {})
          .get("projects", {}).get(projeto, {}))
    bruto = no.get("initial_prompt") or ""
    return [t.strip() for t in bruto.split(",")
            if len(t.strip()) >= MINIMO_DE_LETRAS]


def contar(texto: str, termos: list[str]) -> dict[str, int]:
    """Conta o termo **na forma canônica do cliente**, exata."""
    t = desacentuar(texto)
    return {x: len(re.findall(rf"\b{re.escape(desacentuar(x))}\b", t))
            for x in termos}


def _colar(s: str) -> str:
    """Sem espaço nem hífen: ``next best`` e ``nextbest`` viram a mesma coisa."""
    return re.sub(r"[\s\-]+", "", desacentuar(s))


def contar_solto(texto: str, termos: list[str]) -> dict[str, int]:
    """Conta ignorando espaço e hífen — mede se o termo foi **ouvido**.

    A diferença entre esta contagem e a exata separa dois erros que doem
    diferente: **não ouvir** o termo (perda de informação, insalvável depois) e
    **grafá-lo de outro jeito** (``next best`` por ``nextbest``), que a
    ``RevisaoDeTermos`` do app já sabe consertar.
    """
    t = _colar(texto)
    return {x: t.count(_colar(x)) for x in termos}


def texto_de_json(caminho: Path) -> str:
    d = json.loads(caminho.read_text(encoding="utf-8"))
    return " ".join((s.get("text") or "") for s in d.get("segments") or [])


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--acervo", default=ACERVO_PADRAO)
    ap.add_argument("--gravacoes")
    ap.add_argument("--varredura", default=str(VARREDURA_MOSS))
    ap.add_argument("--json")
    args = ap.parse_args()

    acervo = Path(args.acervo)
    nomes = ([n.strip() for n in args.gravacoes.split(",")] if args.gravacoes
             else list(GRAVACOES))
    resultados = []

    for nome in nomes:
        pasta = acervo / nome
        reuniao = pasta / "reuniao.json"
        gemini = pasta / "gemini.md"
        moss = Path(args.varredura) / f"{nome}__moss_bloco180s" / "transcricao.json"
        if not (reuniao.is_file() and gemini.is_file() and moss.is_file()):
            print(f"{nome}: falta arquivo, pulei", file=sys.stderr)
            continue

        r = json.loads(reuniao.read_text(encoding="utf-8"))
        termos = vocabulario_de(r.get("cliente") or "", r.get("projeto") or "")
        if not termos:
            print(f"{nome}: sem vocabulário para "
                  f"{r.get('cliente')}/{r.get('projeto')}", file=sys.stderr)
            continue

        textos = {
            "app (com hotwords)": texto_de_json(pasta / "transcricao.json"),
            "MOSS (sem hotword)": texto_de_json(moss),
            "Gemini (referência)": " ".join(t for _, t in C.ler_gemini(gemini)),
        }
        contagens = {k: contar(v, termos) for k, v in textos.items()}
        soltas = {k: contar_solto(v, termos) for k, v in textos.items()}

        # Só os termos que alguém disse: os demais não estavam na reunião, e
        # contá-los mediria a lista, não a transcrição.
        ditos = [t for t in termos
                 if any(c.get(t) for c in contagens.values())
                 or any(c.get(t) for c in soltas.values())]
        if not ditos:
            print(f"\n{nome}  ({r.get('cliente')}/{r.get('projeto')}) — "
                  "nenhum termo do vocabulário foi dito")
            continue

        print(f"\n{nome}  ({r.get('cliente')} / {r.get('projeto')})")
        larg = max(len(k) for k in contagens)
        print(f"  {'':{larg}}  {'total':>6} {'termos':>8}   " +
              "  ".join(f"{t[:10]:>10}" for t in ditos))
        for k, c in contagens.items():
            tot = sum(c.get(t, 0) for t in ditos)
            dist = sum(1 for t in ditos if c.get(t))
            print(f"  {k:{larg}}  {tot:>6} {dist:>4}/{len(ditos):<3}   " +
                  "  ".join(f"{c.get(t, 0):>10}" for t in ditos))

        ref = contagens["Gemini (referência)"]
        linha = {"gravacao": nome, "termos_ditos": ditos, "contagens": contagens}
        ref_solta = soltas["Gemini (referência)"]
        for k in ("app (com hotwords)", "MOSS (sem hotword)"):
            # Duas réguas: a forma canônica (o que a ata mostra) e a solta
            # (se o termo foi ouvido). A distância entre elas é o que a
            # RevisaoDeTermos consertaria.
            no_gemini = [t for t in ditos if ref.get(t) or ref_solta.get(t)]
            linha[k] = {
                "canonico": sum(1 for t in no_gemini if contagens[k].get(t)),
                "ouvido": sum(1 for t in no_gemini if soltas[k].get(t)),
                "de": len(no_gemini),
            }
        resultados.append(linha)

    if resultados:
        print(f"\n{'═'*66}\nTERMOS QUE O GEMINI VIU, E QUE CADA UM TAMBÉM PEGOU\n{'═'*66}")
        print(f"  {'':<22} {'na forma canônica':>18} {'ouvido de algum jeito':>23}")
        for k in ("app (com hotwords)", "MOSS (sem hotword)"):
            d = sum(r[k]["de"] for r in resultados)
            can = sum(r[k]["canonico"] for r in resultados)
            ouv = sum(r[k]["ouvido"] for r in resultados)
            print(f"  {k:<22} {can:>3}/{d:<3} {100*can/d if d else 0:>10.1f}%"
                  f" {ouv:>8}/{d:<3} {100*ouv/d if d else 0:>10.1f}%")
        print("\n  'canônico' = escreveu o termo como o cliente o escreve — é o que a ata mostra.")
        print("  'ouvido'   = escreveu de algum jeito (next best por nextbest).")
        print("  A distância entre as duas é o que a RevisaoDeTermos consertaria;")
        print("  o que falta na coluna 'ouvido' é informação perdida, e essa não volta.")

    if args.json and resultados:
        Path(args.json).write_text(json.dumps(resultados, ensure_ascii=False,
                                              indent=2), encoding="utf-8")
        print(f"\nbruto em {args.json}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
