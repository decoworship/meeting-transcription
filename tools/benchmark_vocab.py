"""Mede se o vocabulário customizado chega à transcrição — o caso "Dimi/Jimmy".

Esta é a pergunta que originou o projeto: um nome próprio conhecido
("Dimi") sair transcrito como outra coisa ("Jimmy"). O app resolve injetando um
vocabulário; a migração para whisper.cpp precisa provar que injeta igual.

Os dois mecanismos não são o mesmo:

* **faster-whisper** usa ``hotwords``. O app escolheu isso de propósito: o
  ``initial_prompt`` só influencia a primeira janela de 30 s quando
  ``condition_on_previous_text=False``, e é truncado nos últimos 223 tokens,
  descartando justamente os nomes que vêm no começo do prompt;
* **whisper.cpp** usa ``--prompt`` com ``--carry-initial-prompt``, que reinjeta
  o prompt em toda janela.

Desenho 2×2 — cada motor com e sem vocabulário. Sem os braços "sem prompt" não
dá para separar "o motor já acertaria sozinho" de "o mecanismo funcionou".

A métrica é contagem de ocorrências por termo, comparada com a referência do
histórico do app. Não é WER: um nome próprio errado custa uma palavra no WER e
custa a utilidade inteira do parágrafo para quem lê a ata.

Uso::

    python tools/benchmark_vocab.py faster-whisper --audio mix.wav --saida out/ --com-prompt
    python tools/benchmark_vocab.py faster-whisper --audio mix.wav --saida out/
    python tools/benchmark_vocab.py contar --saida out/ --referencia history/xxx.json
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import time
import unicodedata
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(RAIZ))
sys.path.insert(0, str(RAIZ / "tools"))


def desacentuar(s: str) -> str:
    s = unicodedata.normalize("NFKD", s.lower())
    return "".join(c for c in s if not unicodedata.combining(c))


def termos(config: Path) -> list[str]:
    from compare_engines import termos_do_prompt
    # Termos de 1-3 letras (siglas curtas) dão falso positivo demais dentro de
    # outras palavras; o filtro de comprimento evita medir ruído.
    return [t for t in termos_do_prompt(config) if len(t) > 3]


def contar_em(texto: str, lista: list[str]) -> dict[str, int]:
    t = desacentuar(texto)
    return {x: len(re.findall(rf"\b{re.escape(desacentuar(x))}\b", t)) for x in lista}


def rodar_faster_whisper(args) -> None:
    from dotenv import load_dotenv
    load_dotenv()
    from src.transcription.faster_whisper_transcriber import FasterWhisperTranscriber

    prompt = None
    if args.com_prompt:
        prompt = json.loads(args.config.read_text(encoding="utf-8"))["initial_prompt"]

    t = FasterWhisperTranscriber(model_size=args.modelo)
    t.load_model()
    t0 = time.time()
    r = t.transcribe(str(args.audio), language=args.idioma, initial_prompt=prompt)
    gasto = time.time() - t0

    tag = "com-prompt" if args.com_prompt else "sem-prompt"
    destino = args.saida / f"fw-{tag}.json"
    destino.parent.mkdir(parents=True, exist_ok=True)
    destino.write_text(json.dumps({
        "motor": f"faster-whisper {args.modelo} ({tag})",
        "mecanismo": "hotwords" if args.com_prompt else "—",
        "segundos": round(gasto, 1),
        "texto": " ".join(s.text for s in r.segments),
        "segmentos": len(r.segments),
    }, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"{gasto:.0f}s, {len(r.segments)} segmentos -> {destino}")


def coletar_whispercpp(args) -> None:
    """Converte a saída `-oj` do whisper-cli no mesmo formato dos demais."""
    d = json.loads(args.json.read_text(encoding="utf-8"))
    texto = " ".join(s.get("text", "").strip() for s in d.get("transcription", []))
    destino = args.saida / f"wcpp-{args.tag}.json"
    destino.parent.mkdir(parents=True, exist_ok=True)
    destino.write_text(json.dumps({
        "motor": f"whisper.cpp ({args.tag})",
        "mecanismo": "--carry-initial-prompt" if "com" in args.tag else "—",
        "segundos": args.segundos,
        "texto": texto,
        "segmentos": len(d.get("transcription", [])),
    }, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"{len(d.get('transcription', []))} segmentos -> {destino}")


def contar(args) -> None:
    lista = termos(args.config)
    linhas = []

    if args.referencia:
        d = json.loads(args.referencia.read_text(encoding="utf-8"))
        txt = " ".join(s.get("text", "") for s in d.get("segments", []))
        linhas.append(("referência (histórico do app)", "—", contar_em(txt, lista), None))

    for caminho in sorted(args.saida.glob("*.json")):
        d = json.loads(caminho.read_text(encoding="utf-8"))
        if "texto" not in d:
            continue
        linhas.append((d["motor"], d.get("mecanismo", "—"),
                       contar_em(d["texto"], lista), d.get("segundos")))

    presentes = [t for t in lista if any(c[2].get(t) for c in linhas)]
    larg = max(len(l[0]) for l in linhas) + 1

    print(f"{'motor':{larg}s} {'mecanismo':22s} {'total':>6s} {'termos':>7s}   " +
          "  ".join(f"{t[:9]:>9s}" for t in presentes))
    print("-" * (larg + 40 + 11 * len(presentes)))
    for motor, mec, c, _ in linhas:
        tot = sum(c.get(t, 0) for t in presentes)
        dist = sum(1 for t in presentes if c.get(t))
        print(f"{motor:{larg}s} {mec:22s} {tot:6d} {dist:4d}/{len(presentes):<2d}   " +
              "  ".join(f"{c.get(t, 0):9d}" for t in presentes))


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--config", type=Path,
                   default=RAIZ / "data/meeting-transcription/config.json")
    sub = p.add_subparsers(dest="cmd", required=True)

    a = sub.add_parser("faster-whisper")
    a.add_argument("--audio", type=Path, required=True)
    a.add_argument("--saida", type=Path, required=True)
    a.add_argument("--modelo", default="large-v3")
    a.add_argument("--idioma", default="pt")
    a.add_argument("--com-prompt", action="store_true")
    a.set_defaults(func=rodar_faster_whisper)

    b = sub.add_parser("coletar-whispercpp")
    b.add_argument("--json", type=Path, required=True)
    b.add_argument("--saida", type=Path, required=True)
    b.add_argument("--tag", required=True, help="com-prompt | sem-prompt")
    b.add_argument("--segundos", type=float, default=None)
    b.set_defaults(func=coletar_whispercpp)

    c = sub.add_parser("contar")
    c.add_argument("--saida", type=Path, required=True)
    c.add_argument("--referencia", type=Path, default=None)
    c.set_defaults(func=contar)

    args = p.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
