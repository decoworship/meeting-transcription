"""Compara a diarização do sherpa-onnx com a do pyannote que o app usa hoje.

Parte da fase 0 do plano: o app usa ``community-1``, e **não existe conversão
ONNX dele**. Migrar para uma stack nativa significa cair para
``segmentation-3.0``. Este script mede o tamanho dessa queda.

Não há verdade de referência, então a métrica é concordância entre os dois, não
acerto absoluto. O procedimento:

1. roda o sherpa-onnx sobre o mesmo ``system.wav`` que o app diariza;
2. casa os rótulos dos dois (que são arbitrários — "Speaker 1" de um não é o do
   outro) escolhendo o pareamento que maximiza tempo em comum;
3. reporta quanto tempo de fala os dois atribuem à mesma pessoa.

Uso::

    python tools/compare_diarization.py \\
        --audio system_trecho.wav \\
        --baseline data/meeting-transcription/history/1786024581252.json \\
        --offset 240 --window 240 660 \\
        --segmentation models/sherpa-onnx-pyannote-segmentation-3-0/model.onnx \\
        --embedding models/wespeaker_en_voxceleb_resnet34_LM.onnx
"""

from __future__ import annotations

import argparse
import json
import wave
from collections import defaultdict
from pathlib import Path

import numpy as np


def ler_wav(path: Path) -> tuple[np.ndarray, int]:
    with wave.open(str(path), "rb") as w:
        sr = w.getframerate()
        raw = w.readframes(w.getnframes())
    return np.frombuffer(raw, dtype=np.int16).astype(np.float32) / 32768.0, sr


def rodar_sherpa(audio: Path, seg_model: Path, emb_model: Path,
                 num_speakers: int, threshold: float) -> list[tuple[float, float, str]]:
    """Diariza com o sherpa-onnx e devolve (início, fim, rótulo)."""
    import sherpa_onnx

    config = sherpa_onnx.OfflineSpeakerDiarizationConfig(
        segmentation=sherpa_onnx.OfflineSpeakerSegmentationModelConfig(
            pyannote=sherpa_onnx.OfflineSpeakerSegmentationPyannoteModelConfig(
                model=str(seg_model)
            ),
        ),
        embedding=sherpa_onnx.SpeakerEmbeddingExtractorConfig(model=str(emb_model)),
        clustering=sherpa_onnx.FastClusteringConfig(
            # num_clusters=-1 deixa o threshold decidir quantas pessoas existem,
            # que é o caso realista: não se sabe de antemão quem entrou na call.
            num_clusters=num_speakers if num_speakers > 0 else -1,
            threshold=threshold,
        ),
    )
    sd = sherpa_onnx.OfflineSpeakerDiarization(config)

    samples, sr = ler_wav(audio)
    if sr != sd.sample_rate:
        raise SystemExit(f"esperado {sd.sample_rate} Hz, o arquivo tem {sr} Hz")

    resultado = sd.process(samples).sort_by_start_time()
    return [(r.start, r.end, f"S{r.speaker}") for r in resultado]


def baseline_do_historico(path: Path, ini: float, fim: float,
                          offset: float) -> list[tuple[float, float, str]]:
    """Segmentos do histórico do app, recortados na janela e rebaseados em 0."""
    d = json.loads(path.read_text(encoding="utf-8"))
    saida = []
    for s in d.get("segments", []):
        a, b = float(s["start"]), float(s["end"])
        if b <= ini or a >= fim:
            continue
        a, b = max(a, ini), min(b, fim)
        rotulo = s.get("speaker") or "?"
        saida.append((a - offset, b - offset, rotulo))
    return saida


def matriz_sobreposicao(x, y) -> dict[tuple[str, str], float]:
    """Tempo em comum entre cada par de rótulos das duas diarizações."""
    m: dict[tuple[str, str], float] = defaultdict(float)
    for a0, a1, la in x:
        for b0, b1, lb in y:
            ov = min(a1, b1) - max(a0, b0)
            if ov > 0:
                m[(la, lb)] += ov
    return m


def parear(m: dict[tuple[str, str], float], rot_a: list[str],
           rot_b: list[str]) -> dict[str, str]:
    """Casa os rótulos gulosamente, do par com mais tempo em comum para o menor.

    Guloso e não ótimo (Hungarian seria), mas com 3-5 falantes a diferença é
    nula e a leitura do código é melhor.
    """
    pares = sorted(m.items(), key=lambda kv: kv[1], reverse=True)
    mapa, usados_a, usados_b = {}, set(), set()
    for (la, lb), _ in pares:
        if la in usados_a or lb in usados_b:
            continue
        mapa[la] = lb
        usados_a.add(la)
        usados_b.add(lb)
    return mapa


def tempo_total(segs) -> float:
    return sum(b - a for a, b, _ in segs)


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--audio", type=Path, required=True)
    p.add_argument("--baseline", type=Path, required=True)
    p.add_argument("--segmentation", type=Path, required=True)
    p.add_argument("--embedding", type=Path, required=True)
    p.add_argument("--window", type=float, nargs=2, default=[0, 1e9],
                   help="janela (s) do histórico a considerar")
    p.add_argument("--offset", type=float, default=0.0,
                   help="quanto subtrair dos tempos do histórico para casar com o recorte")
    p.add_argument("--num-speakers", type=int, default=-1)
    p.add_argument("--threshold", type=float, default=0.5)
    p.add_argument("--audio-inteiro", action="store_true",
                   help="o áudio dado é a gravação completa; recortar a saída na janela")
    p.add_argument("--excluir-dono", default=None,
                   help="rótulo do baseline vindo do assign_owner (ex.: Yuri), a excluir")
    args = p.parse_args()

    ini, fim = args.window
    base = baseline_do_historico(args.baseline, ini, fim, args.offset)
    if args.excluir_dono:
        # Os rótulos vindos do `assign_owner` não saíram do pyannote: vieram da
        # energia da faixa do microfone. Mantê-los dá ao baseline uma fonte de
        # informação que o candidato não teve, e mede duas coisas ao mesmo tempo.
        antes = len(base)
        base = [s for s in base if s[2] != args.excluir_dono]
        print(f"excluídos {antes - len(base)} segmentos rotulados "
              f"'{args.excluir_dono}' (vêm do microfone, não da diarização)")

    import time
    t0 = time.time()
    cand = rodar_sherpa(args.audio, args.segmentation, args.embedding,
                        args.num_speakers, args.threshold)
    gasto = time.time() - t0

    # Diarizar o arquivo inteiro e só depois recortar a janela: o agrupamento
    # melhora com mais áudio por falante, então dar ao candidato apenas 7 dos
    # 11 minutos que o baseline viu seria competição desigual. Os tempos do
    # sherpa são absolutos; rebaseamos para casar com o baseline já deslocado.
    if args.audio_inteiro:
        recorte = []
        for a, b, l in cand:
            if b <= ini or a >= fim:
                continue
            recorte.append((max(a, ini) - args.offset, min(b, fim) - args.offset, l))
        print(f"sherpa: {len(cand)} turnos no arquivo inteiro -> "
              f"{len(recorte)} na janela de avaliação")
        cand = recorte

    dur = fim - ini
    print(f"janela: {ini:.0f}s -> {fim:.0f}s ({dur:.0f}s de áudio)")
    print(f"sherpa-onnx levou {gasto:.1f}s  ({dur/max(gasto,1e-9):.1f}x tempo real, CPU)")
    print()

    rot_base = sorted({l for _, _, l in base})
    rot_cand = sorted({l for _, _, l in cand})
    print(f"{'baseline (pyannote community-1)':38s} {len(rot_base)} falantes  "
          f"{tempo_total(base):6.1f}s de fala  {len(base)} segmentos")
    print(f"{'sherpa-onnx (segmentation-3.0)':38s} {len(rot_cand)} falantes  "
          f"{tempo_total(cand):6.1f}s de fala  {len(cand)} segmentos")
    print(f"  baseline: {rot_base}")
    print(f"  sherpa  : {rot_cand}")
    print()

    m = matriz_sobreposicao(base, cand)
    mapa = parear(m, rot_base, rot_cand)
    print("pareamento (por tempo em comum):")
    for la, lb in mapa.items():
        print(f"  {la:<14} <-> {lb:<6} {m[(la, lb)]:7.1f}s em comum")

    concordante = sum(m[(la, lb)] for la, lb in mapa.items())
    total_sobreposto = sum(m.values())
    print()
    if total_sobreposto > 0:
        print(f"tempo em que os dois se sobrepõem : {total_sobreposto:.1f}s")
        print(f"  atribuído à MESMA pessoa        : {concordante:.1f}s "
              f"({100*concordante/total_sobreposto:.1f}%)")
        print(f"  atribuído a pessoas DIFERENTES  : {total_sobreposto-concordante:.1f}s "
              f"({100*(1-concordante/total_sobreposto):.1f}%)")


if __name__ == "__main__":
    main()
