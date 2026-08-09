"""Critério A da Fase 2: o pipeline novo empata ou ganha do app Gradio.

Roda o pipeline Python de hoje (``src/``) sobre uma gravação e compara com o
JSON que o ``Sidecar.exe --gravacao`` produziu para a mesma gravação. É o
equivalente, para a Fase 2, do que o ``comparar_gravadores.py`` foi para a
Fase 1: a paridade vira dois arquivos comparáveis, não duas telas.

A comparação é em camadas, da mais barata e mais decisiva para a mais frouxa:

1. **o mix**, byte a byte — se as duas somas de faixas divergem, o ASR recebeu
   entradas diferentes e comparar o texto depois não diria nada;
2. **os segmentos** — quantos, com que tempos, com que texto;
3. **os falantes** — quantos, e quantos segmentos são seus.

Uso::

    python tools/comparar_pipeline.py data/recordings/2026-08-07_15-39-58
"""

from __future__ import annotations

import argparse
import hashlib
import json
import logging
import os
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

logging.basicConfig(level=logging.WARNING)


def sha(caminho: Path) -> str:
    return hashlib.sha256(caminho.read_bytes()).hexdigest()


def rodar_python(pasta: Path, modelo: str) -> dict:
    """O pipeline de hoje, exatamente como o app o executa."""
    from src.diarization.speaker_diarizer import SpeakerDiarizer
    from src.transcription.faster_whisper_transcriber import FasterWhisperTranscriber
    from src.web import recordings as rec_mod

    rec = rec_mod.Recording(
        path=pasta, mic=pasta / "mic.wav", system=pasta / "system.wav", meta={}
    )

    mix = pasta / "mix-python.wav"
    rec_mod.mix_tracks(rec, mix)

    t0 = time.time()
    transcritor = FasterWhisperTranscriber(model_size=modelo)
    resultado = transcritor.transcribe(str(mix))
    transcritor.unload_model()
    t_asr = time.time() - t0

    t0 = time.time()
    diarizador = SpeakerDiarizer()
    segmentos = diarizador.diarize(str(rec.system))
    diarizador.unload_model()
    t_diar = time.time() - t0

    diarizador.assign_speakers(resultado, segmentos)
    meus, total = rec_mod.assign_owner(resultado, rec)

    return {
        "mix": mix,
        "t_asr": t_asr,
        "t_diar": t_diar,
        "meus": meus,
        "json": {
            "language": resultado.language,
            "duration": resultado.duration,
            "segments": [s.to_dict() for s in resultado.segments],
        },
    }


def comparar(a: dict, b: dict) -> int:
    """Imprime as diferenças. Devolve o número de divergências que importam."""
    problemas = 0
    sa, sb = a["segments"], b["segments"]

    print(f"\n{'':22} {'python':>12} {'c#':>12}")
    print(f"{'idioma':22} {str(a['language']):>12} {str(b['language']):>12}")
    print(f"{'duração':22} {a['duration']:>12.2f} {b['duration']:>12.2f}")
    print(f"{'segmentos':22} {len(sa):>12} {len(sb):>12}")

    falantes_a = {s.get("speaker") for s in sa if s.get("speaker")}
    falantes_b = {s.get("speaker") for s in sb if s.get("speaker")}
    print(f"{'falantes':22} {len(falantes_a):>12} {len(falantes_b):>12}")
    print(f"{'segmentos seus':22} "
          f"{sum(1 for s in sa if s.get('speaker') == 'You'):>12} "
          f"{sum(1 for s in sb if s.get('speaker') == 'You'):>12}")

    if len(sa) != len(sb):
        print(f"\n! contagem de segmentos difere: {len(sa)} x {len(sb)}")
        problemas += 1

    # Texto e tempos, segmento a segmento, até onde os dois existem.
    dif_texto = dif_tempo = dif_falante = 0
    pior_tempo = 0.0
    for x, y in zip(sa, sb):
        if x["text"].strip() != y["text"].strip():
            if dif_texto < 5:
                print(f"\n  texto difere em {x['start']:.2f}s:")
                print(f"    python: {x['text'].strip()[:90]}")
                print(f"    c#:     {y['text'].strip()[:90]}")
            dif_texto += 1
        d = max(abs(x["start"] - y["start"]), abs(x["end"] - y["end"]))
        pior_tempo = max(pior_tempo, d)
        if d > 0.01:
            dif_tempo += 1
        if x.get("speaker") != y.get("speaker"):
            dif_falante += 1

    print(f"\n{'texto diferente':22} {dif_texto} de {min(len(sa), len(sb))} segmentos")
    print(f"{'tempo > 10 ms':22} {dif_tempo}  (pior: {pior_tempo*1000:.1f} ms)")
    print(f"{'falante diferente':22} {dif_falante}")

    problemas += (dif_texto > 0) + (dif_tempo > 0) + (dif_falante > 0)
    return problemas


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("pasta", type=Path)
    p.add_argument("--modelo", default="large-v3")
    p.add_argument("--csharp", type=Path, default=None,
                   help="JSON do Sidecar.exe (padrão: <pasta>/transcricao.json)")
    args = p.parse_args()

    caminho_cs = args.csharp or args.pasta / "transcricao.json"
    if not caminho_cs.is_file():
        print(f"falta o JSON do C#: {caminho_cs}\n"
              f"rode antes: Sidecar.exe --gravacao {args.pasta}", file=sys.stderr)
        return 2

    cs = json.loads(caminho_cs.read_text(encoding="utf-8"))

    print(f"rodando o pipeline Python em {args.pasta.name}...")
    py = rodar_python(args.pasta, args.modelo)
    print(f"  asr {py['t_asr']:.1f}s, diarização {py['t_diar']:.1f}s")

    # A comparação decisiva: mesmo mix significa que o ASR viu a mesma coisa.
    mix_cs = args.pasta / "mix.wav"
    if mix_cs.is_file():
        iguais = sha(mix_cs) == sha(py["mix"])
        print(f"\nmix byte a byte: {'IDÊNTICO' if iguais else 'DIFERE'}"
              f"  ({mix_cs.stat().st_size} x {py['mix'].stat().st_size} bytes)")
    else:
        iguais = None
        print(f"\nmix do C# não encontrado em {mix_cs}")

    problemas = comparar(py["json"], cs)
    if iguais is False:
        problemas += 1

    print(f"\n{'PARIDADE' if problemas == 0 else f'{problemas} divergência(s)'}")
    return 0 if problemas == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
