"""Palavras que o ASR escreveu onde não havia sinal.

Mede alucinação **sem gabarito e sem transcrição paralela**, que é o caso da
maioria das gravações do acervo. A régua é o próprio áudio: onde há silêncio
digital — amostras em zero, ou abaixo de um piso de ruído —, qualquer palavra
transcrita é invenção, sem margem de interpretação.

Existe porque as outras duas medidas de VAD não alcançam isto:

* ``wer_contra_gemini.py`` mede cobertura, e precisa de transcrição paralela;
* a coluna "sobrando" dele é ambígua — pode ser invenção nossa ou backchannel
  que o Gemini descartou.

É o outro lado do mesmo trade-off: afrouxar o VAD ganha cobertura e cobra
invenção. Sem esta medida, só se enxerga metade da conta.

**É porte do critério do ``sweep_vad.py``**, que não roda fora do `.venv` do
projeto — ele importa `src.utils.gpu_detector`. Aqui só se lê WAV e JSON, então
roda em qualquer Python.

Uso::

    python tools/palavras_no_silencio.py <audio.wav> <pasta>...
"""

from __future__ import annotations

import argparse
import json
import math
import wave
from array import array
from pathlib import Path


def rms_por_janela(caminho: Path, janela_s: float = 0.05) -> tuple[list[float], int]:
    with wave.open(str(caminho), "rb") as w:
        taxa = w.getframerate()
        a = array("h")
        a.frombytes(w.readframes(w.getnframes()))

    n = int(janela_s * taxa)
    saida = []
    for i in range(0, len(a) - n + 1, n):
        soma = 0.0
        for x in a[i:i + n]:
            v = x / 32768.0
            soma += v * v
        saida.append(math.sqrt(soma / n))
    return saida, taxa


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("audio", type=Path)
    p.add_argument("pastas", nargs="+", type=Path)
    # 1e-4 é duas ordens de grandeza abaixo da fala mais baixa medida (0,03) e
    # uma acima do zero exato: pega silêncio digital e ruído de fundo mudo, sem
    # confundir com fala baixinha.
    p.add_argument("--piso", type=float, default=1e-4)
    p.add_argument("--janela", type=float, default=0.05)
    args = p.parse_args()

    janelas, _ = rms_por_janela(args.audio, args.janela)
    mudo = [r < args.piso for r in janelas]
    total_mudo = sum(mudo) * args.janela
    print(f"\n{args.audio.name}: {len(janelas) * args.janela / 60:.0f} min, "
          f"{total_mudo / 60:.1f} min abaixo do piso ({100 * sum(mudo) // len(mudo)}%)\n")

    print(f"{'variante':<16} {'segmentos':>10} {'palavras':>9} "
          f"{'no mudo':>9} {'% no mudo':>10}")
    print("-" * 60)

    for pasta in args.pastas:
        arq = pasta / "transcricao.json"
        if not arq.exists():
            print(f"{pasta.name:<16} sem transcricao.json"); continue

        segs = [s for s in json.loads(arq.read_text(encoding="utf-8")).get("segments") or []
                if (s.get("text") or "").strip()]
        palavras = inventadas = 0
        for s in segs:
            n = len((s.get("text") or "").split())
            palavras += n
            # O segmento inteiro cai em janelas mudas: não é fala baixa, é
            # ausência de sinal.
            i0 = int(s["start"] / args.janela)
            i1 = min(int(s["end"] / args.janela), len(mudo))
            if i1 > i0 and all(mudo[i0:i1]):
                inventadas += n

        print(f"{pasta.name:<16} {len(segs):>10} {palavras:>9} {inventadas:>9} "
              f"{100 * inventadas / max(palavras, 1):>9.2f}%")

    print("\nMenos palavras no mudo é melhor. Zero é o esperado; qualquer coisa\n"
          "acima disso é o VAD deixando o modelo escrever sobre ausência de sinal.\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
