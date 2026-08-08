"""Varre parâmetros de VAD em gravações reais, sem precisar de transcrição.

O resultado 6 do FASE0 mediu 12 pontos de WER de ganho ao afrouxar o VAD — mas
sobre fala concatenada, praticamente sem silêncio. Numa gravação real a
situação se inverte: é justamente no silêncio que o VAD ganha o salário, evitando
que o modelo alucine.

Gravação nossa não tem transcrição de referência, então WER está fora. Mas o VAD
tem uma função objetiva e verificável **contra o próprio áudio**:

* onde há energia de fala, o texto deve aparecer;
* onde há **silêncio digital** (zeros exatos), qualquer palavra é invenção.

O segundo critério é o que dá rigor a isto sem anotador humano: zeros exatos não
são "fala baixinha", são ausência de sinal. Toda palavra transcrita ali é erro,
sem margem de interpretação.

A saída é um trade-off no estilo precisão/cobertura:

* **palavras em fala** — quanto maior, melhor (proxy de cobertura);
* **palavras em silêncio** — quanto menor, melhor (alucinação medida).

Uso::

    python tools/sweep_vad.py --audio data/bench-vocab/mix.wav --saida out/ \\
        --configs "0.35:500" "0.2:500" "0.1:300" "sem-vad"
"""

from __future__ import annotations

import argparse
import json
import time
from pathlib import Path

import numpy as np

QUADRO = 0.1  # 100 ms para classificar o áudio


def perfil_audio(caminho: Path, limiar_fala: float) -> tuple[np.ndarray, np.ndarray, float]:
    """Classifica o áudio em quadros de 100 ms.

    Devolve (mascara_silencio_digital, mascara_fala, duracao).

    "Silêncio digital" é a fração de amostras exatamente zero acima de 99% —
    critério deliberadamente severo. Ruído de sala, respiração e fala baixa não
    entram; só ausência real de sinal, que é o que o gravador produz quando o
    canal está mudo ou o dispositivo parou de entregar amostras.
    """
    import soundfile as sf

    sinal, sr = sf.read(caminho, dtype="float32")
    if sinal.ndim > 1:
        sinal = sinal.mean(axis=1)
    n = int(len(sinal) / sr / QUADRO)
    quadros = sinal[: n * int(sr * QUADRO)].reshape(n, -1)

    zeros = (quadros == 0.0).mean(axis=1)
    rms = np.sqrt((quadros ** 2).mean(axis=1))

    return zeros > 0.99, rms > limiar_fala, len(sinal) / sr


def palavras_por_regiao(segmentos, silencio, fala) -> tuple[float, float]:
    """Distribui as palavras de cada segmento entre silêncio e fala.

    Um segmento longo pode atravessar as duas regiões, então as palavras são
    rateadas pela fração de tempo em cada uma — atribuir o segmento inteiro à
    região do seu início exageraria os dois lados.
    """
    n_sil = n_fala = 0.0
    for s in segmentos:
        a, b = int(s["inicio"] / QUADRO), int(s["fim"] / QUADRO)
        b = min(b, len(silencio))
        if b <= a:
            continue
        pal = len(s["texto"].split())
        total = b - a
        n_sil += pal * silencio[a:b].sum() / total
        n_fala += pal * fala[a:b].sum() / total
    return n_sil, n_fala


def rodar(args) -> None:
    import sys
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
    from faster_whisper import WhisperModel
    from src.utils.gpu_detector import is_cuda_available, get_optimal_compute_type

    args.saida.mkdir(parents=True, exist_ok=True)
    silencio, fala, dur = perfil_audio(args.audio, args.limiar_fala)
    print(f"{args.audio.name}: {dur/60:.1f} min | "
          f"silêncio digital {100*silencio.mean():.1f}% | "
          f"fala {100*fala.mean():.1f}%\n")

    dispositivo = "cuda" if is_cuda_available() else "cpu"
    modelo = WhisperModel(args.modelo, device=dispositivo,
                          compute_type=get_optimal_compute_type()
                          if dispositivo == "cuda" else "int8")

    print(f"{'config':16s} {'palavras':>9s} {'em fala':>9s} {'em silêncio':>12s} "
          f"{'% inventado':>12s} {'tempo':>7s}")
    print("-" * 72)

    linhas = []
    for cfg in args.configs:
        kwargs = dict(language=args.idioma, beam_size=5,
                      condition_on_previous_text=False, word_timestamps=True,
                      hallucination_silence_threshold=2.0)
        if cfg == "sem-vad":
            kwargs["vad_filter"] = False
        else:
            thr, sil = cfg.split(":")
            kwargs["vad_filter"] = True
            kwargs["vad_parameters"] = dict(threshold=float(thr),
                                            min_silence_duration_ms=int(sil),
                                            max_speech_duration_s=25)

        t0 = time.time()
        segs, _ = modelo.transcribe(str(args.audio), **kwargs)
        segmentos = [{"inicio": s.start, "fim": s.end, "texto": s.text} for s in segs]
        gasto = time.time() - t0

        total = sum(len(s["texto"].split()) for s in segmentos)
        n_sil, n_fala = palavras_por_regiao(segmentos, silencio, fala)
        pct = 100 * n_sil / total if total else 0.0
        print(f"{cfg:16s} {total:9d} {n_fala:9.0f} {n_sil:12.1f} {pct:11.2f}% {gasto:6.0f}s")

        linhas.append({"config": cfg, "palavras": total, "em_fala": round(n_fala, 1),
                       "em_silencio": round(n_sil, 1), "pct_inventado": round(pct, 2),
                       "segundos": round(gasto, 1), "segmentos": len(segmentos)})
        (args.saida / f"segs-{cfg.replace(':', '_')}.json").write_text(
            json.dumps(segmentos, ensure_ascii=False), encoding="utf-8")

    (args.saida / "resumo.json").write_text(
        json.dumps({"audio": str(args.audio), "duracao_s": round(dur, 1),
                    "pct_silencio_digital": round(100 * float(silencio.mean()), 2),
                    "resultados": linhas}, ensure_ascii=False, indent=2),
        encoding="utf-8")


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--audio", type=Path, required=True)
    p.add_argument("--saida", type=Path, required=True)
    p.add_argument("--modelo", default="large-v3")
    p.add_argument("--idioma", default="pt")
    p.add_argument("--limiar-fala", type=float, default=0.01,
                   help="RMS acima do qual o quadro conta como fala")
    p.add_argument("--configs", nargs="+",
                   default=["0.35:500", "0.2:500", "0.1:300", "sem-vad"],
                   help="'threshold:min_silencio_ms' ou 'sem-vad'")
    args = p.parse_args()
    rodar(args)


if __name__ == "__main__":
    main()
