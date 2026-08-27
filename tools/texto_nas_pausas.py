"""Quanto texto o ASR produz nas pausas longas de uma reunião.

Complementa as outras duas medidas de VAD, e cobre o caso que nenhuma alcança:

* ``wer_contra_gemini.py`` mede **cobertura** — o que faltou. Precisa de uma
  transcrição paralela, que a maioria das gravações não tem;
* ``sweep_vad.py`` mede **alucinação sobre silêncio digital** — zeros exatos,
  onde qualquer palavra é invenção. Rigoroso, e cego quando a gravação não tem
  zeros: na reunião de 122 minutos do acervo, com 22 minutos de pausa, a faixa
  de sistema tem **8,6 segundos** de silêncio digital. As pausas são áudio com
  ruído baixo — respiração, teclado, sala —, e não ausência de sinal.

Aqui a régua é a pausa como o próprio ASR a enxergou: os intervalos entre
segmentos da transcrição de referência. Afrouxar o VAD deve **encurtar** essas
pausas — é o que significa recuperar fala baixa. Mas se o texto novo aparecer
espalhado por pausas que continuam longas, é invenção com outro nome.

Não decide sozinha: é o terceiro lado de um trade-off, e a leitura é comparar
duas configurações do mesmo áudio.

Uso::

    python tools/texto_nas_pausas.py referencia/ candidata/...
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path


def segmentos(pasta: Path) -> list[dict]:
    d = json.loads((pasta / "transcricao.json").read_text(encoding="utf-8"))
    return [s for s in d.get("segments") or [] if (s.get("text") or "").strip()]


def pausas(segs: list[dict], minimo: float) -> list[tuple[float, float]]:
    return [(a["end"], b["start"]) for a, b in zip(segs, segs[1:])
            if b["start"] - a["end"] >= minimo]


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("pastas", nargs="+", type=Path)
    p.add_argument("--pausa-minima", type=float, default=8.0)
    args = p.parse_args()

    ref = segmentos(args.pastas[0])
    janelas = pausas(ref, args.pausa_minima)
    total = sum(f - i for i, f in janelas)
    print(f"\nreferência: {args.pastas[0].name} · {len(janelas)} pausas de "
          f"{args.pausa_minima:.0f}s ou mais, somando {total/60:.0f} min\n")

    print(f"{'variante':<16} {'segmentos':>10} {'palavras':>9} "
          f"{'nas pausas':>11} {'min de pausa':>13}")
    print("-" * 64)

    for pasta in args.pastas:
        segs = segmentos(pasta)
        palavras = sum(len((s.get("text") or "").split()) for s in segs)
        # Palavra dentro de uma janela que a REFERÊNCIA considerou pausa.
        nas = sum(len((s.get("text") or "").split()) for s in segs
                  if any(i <= s["start"] < f for i, f in janelas))
        restante = sum(f - i for i, f in pausas(segs, args.pausa_minima))
        print(f"{pasta.name:<16} {len(segs):>10} {palavras:>9} {nas:>11} "
              f"{restante/60:>12.0f}")

    print("\nMenos minutos de pausa com MAIS palavras nas pausas é recuperação de\n"
          "fala baixa. Mais palavras nas pausas SEM encurtá-las é invenção.\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
