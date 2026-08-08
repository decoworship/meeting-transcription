"""Mede DER de diarização contra referência anotada à mão (corpus AMI).

Por que existe: as medições anteriores compararam a diarização de hoje com a
candidata **entre si**, sobre gravações nossas sem verdade de referência. Isso
responde "quanto discordam", nunca "quem erra". Aqui a referência é humana e a
métrica é a padrão da área.

Corpus: `diarizers-community/ami`, configuração **`ihm`** (headset mix). Cada
pessoa entra pelo próprio microfone e o resultado é misturado — é o análogo mais
próximo do nosso `system.wav`, que é a mistura digital da chamada. A alternativa
`sdm` (um microfone distante na sala) tem outro perfil acústico.

Está em inglês, e isso é aceitável: diarização opera sobre características
acústicas de locutor, não sobre fonemas de um idioma. É muito menos sensível a
língua que o ASR — e não existe corpus de diarização anotado em português.

DER = (fala perdida + falso alarme + confusão de falante) / fala de referência.

Uso::

    python tools/benchmark_der.py preparar --parquet ami_ihm_test_0.parquet \\
        --saida ami/ --reunioes 2 --max-segundos 600
    python tools/benchmark_der.py pyannote --corpus ami/ --modelo community-1
    python tools/benchmark_der.py sherpa --corpus ami/ --segmentation ... --embedding ...
    python tools/benchmark_der.py pontuar --corpus ami/ --hipoteses ami/hip_*.json
"""

from __future__ import annotations

import argparse
import json
import time
from pathlib import Path

import numpy as np

QUADRO = 0.010  # 10 ms — resolução do cálculo


# ── Preparo ─────────────────────────────────────────────────────────────


def preparar(args) -> None:
    """Extrai reuniões do parquet do AMI: wav + referência de falante."""
    import io
    import pyarrow.parquet as pq
    import soundfile as sf

    saida = args.saida
    (saida / "audio").mkdir(parents=True, exist_ok=True)

    pf = pq.ParquetFile(args.parquet)
    manifesto = []
    lidas = 0

    for grupo in range(pf.num_row_groups):
        if lidas >= args.reunioes:
            break
        for linha in pf.read_row_group(grupo).to_pylist():
            if lidas >= args.reunioes:
                break
            dados = linha["audio"]["bytes"]
            sinal, sr = sf.read(io.BytesIO(dados), dtype="float32")
            if sinal.ndim > 1:
                sinal = sinal.mean(axis=1)

            limite = args.max_segundos if args.max_segundos > 0 else len(sinal) / sr
            sinal = sinal[: int(limite * sr)]

            nome = f"reuniao_{lidas:02d}"
            destino = saida / "audio" / f"{nome}.wav"
            sf.write(destino, sinal, sr, subtype="PCM_16")

            # A referência do AMI vem como três listas paralelas. Recortamos na
            # mesma janela do áudio, senão o DER contaria como "fala perdida"
            # trechos que o motor nem chegou a ouvir.
            ref = [
                {"inicio": a, "fim": min(b, limite), "falante": s}
                for a, b, s in zip(linha["timestamps_start"],
                                   linha["timestamps_end"],
                                   linha["speakers"])
                if a < limite
            ]
            manifesto.append({
                "id": nome,
                "audio": str(destino.resolve()),
                "duracao_s": round(len(sinal) / sr, 2),
                "falantes": sorted({r["falante"] for r in ref}),
                "referencia": ref,
            })
            fala = sum(r["fim"] - r["inicio"] for r in ref)
            print(f"  {nome}: {len(sinal)/sr/60:.1f} min, "
                  f"{len(manifesto[-1]['falantes'])} falantes, "
                  f"{len(ref)} turnos, {fala/60:.1f} min de fala")
            lidas += 1

    (saida / "manifesto.json").write_text(
        json.dumps(manifesto, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"\n{len(manifesto)} reuniões -> {saida}")


# ── Motores ─────────────────────────────────────────────────────────────


def rodar_pyannote(args) -> None:
    """Roda o diarizador que o app usa hoje."""
    import sys
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
    from dotenv import load_dotenv
    load_dotenv()
    from src.diarization.speaker_diarizer import SpeakerDiarizer

    manifesto = json.loads((args.corpus / "manifesto.json").read_text(encoding="utf-8"))
    d = SpeakerDiarizer(model=args.modelo)

    hip, t0 = {}, time.time()
    for item in manifesto:
        segs = d.diarize(item["audio"])
        hip[item["id"]] = [
            {"inicio": float(s.start), "fim": float(s.end), "falante": str(s.speaker)}
            for s in segs
        ]
        print(f"  {item['id']}: {len(segs)} turnos")
    gasto = time.time() - t0

    destino = args.corpus / f"hip_pyannote-{args.modelo}.json"
    destino.write_text(json.dumps(
        {"motor": f"pyannote {args.modelo}", "segundos": round(gasto, 1), "hipoteses": hip},
        ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"{gasto:.0f}s -> {destino}")


def rodar_sherpa(args) -> None:
    """Roda o sherpa-onnx (segmentation-3.0 + wespeaker)."""
    import sherpa_onnx
    import soundfile as sf

    manifesto = json.loads((args.corpus / "manifesto.json").read_text(encoding="utf-8"))
    cfg = sherpa_onnx.OfflineSpeakerDiarizationConfig(
        segmentation=sherpa_onnx.OfflineSpeakerSegmentationModelConfig(
            pyannote=sherpa_onnx.OfflineSpeakerSegmentationPyannoteModelConfig(
                model=str(args.segmentation)),
            provider=args.provider),
        embedding=sherpa_onnx.SpeakerEmbeddingExtractorConfig(
            model=str(args.embedding), provider=args.provider),
        clustering=sherpa_onnx.FastClusteringConfig(
            num_clusters=args.num_speakers if args.num_speakers > 0 else -1,
            threshold=args.threshold),
    )
    sd = sherpa_onnx.OfflineSpeakerDiarization(cfg)

    hip, t0 = {}, time.time()
    for item in manifesto:
        sinal, sr = sf.read(item["audio"], dtype="float32")
        if sr != sd.sample_rate:
            raise SystemExit(f"esperado {sd.sample_rate} Hz, achei {sr}")
        r = sd.process(sinal).sort_by_start_time()
        hip[item["id"]] = [
            {"inicio": float(x.start), "fim": float(x.end), "falante": f"S{x.speaker}"}
            for x in r
        ]
        print(f"  {item['id']}: {len(r)} turnos")
    gasto = time.time() - t0

    rotulo = args.rotulo or f"sherpa-onnx (thr={args.threshold})"
    destino = args.corpus / f"hip_sherpa-{args.threshold}.json"
    destino.write_text(json.dumps(
        {"motor": rotulo, "segundos": round(gasto, 1), "hipoteses": hip},
        ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"{gasto:.0f}s -> {destino}")


# ── DER ─────────────────────────────────────────────────────────────────


def _matriz_quadros(segs, falantes, n_quadros) -> np.ndarray:
    """(n_falantes, n_quadros) booleana: quem fala em cada quadro de 10 ms."""
    idx = {f: i for i, f in enumerate(falantes)}
    m = np.zeros((len(falantes), n_quadros), dtype=bool)
    for s in segs:
        a = max(0, int(s["inicio"] / QUADRO))
        b = min(n_quadros, int(s["fim"] / QUADRO))
        if b > a:
            m[idx[s["falante"]], a:b] = True
    return m


def _mascara_collar(ref_segs, n_quadros, collar) -> np.ndarray:
    """Quadros a ignorar: vizinhança das fronteiras da referência.

    O collar existe porque anotação humana não é precisa ao milissegundo, e
    penalizar o motor por 100 ms numa troca de turno mede o anotador, não o
    motor. 0,25 s é o valor convencional na literatura de DER.
    """
    usar = np.ones(n_quadros, dtype=bool)
    if collar <= 0:
        return usar
    meio = int(collar / QUADRO)
    for s in ref_segs:
        for t in (s["inicio"], s["fim"]):
            a = max(0, int(t / QUADRO) - meio)
            b = min(n_quadros, int(t / QUADRO) + meio)
            usar[a:b] = False
    return usar


def der_de_uma(ref_segs, hip_segs, duracao, collar) -> dict:
    """DER de uma reunião, com casamento ótimo de rótulos.

    Segue a formulação do NIST: por quadro, compara **quantos** falantes a
    referência tem contra quantos a hipótese tem, e quantos deles casam.
    Isso trata sobreposição corretamente — um quadro com duas pessoas falando
    conta como duas unidades de referência.
    """
    from scipy.optimize import linear_sum_assignment

    n = int(duracao / QUADRO)
    fr = sorted({s["falante"] for s in ref_segs})
    fh = sorted({s["falante"] for s in hip_segs})
    if not fr:
        return {"der": 0.0, "perdida": 0.0, "falso": 0.0, "confusao": 0.0, "total": 0.0}

    R = _matriz_quadros(ref_segs, fr, n)
    H = _matriz_quadros(hip_segs, fh, n) if fh else np.zeros((0, n), dtype=bool)
    usar = _mascara_collar(ref_segs, n, collar)
    R, H = R[:, usar], (H[:, usar] if H.size else H)

    # Casamento um-para-um que maximiza quadros em comum. Os rótulos são
    # arbitrários dos dois lados; sem isso mediríamos a nomeação, não a
    # separação.
    par = {}
    if H.size:
        coocor = R.astype(np.int32) @ H.T.astype(np.int32)
        li, co = linear_sum_assignment(-coocor)
        par = {int(i): int(j) for i, j in zip(li, co)}

    n_ref = R.sum(axis=0)
    n_hip = H.sum(axis=0) if H.size else np.zeros(R.shape[1], dtype=int)
    certos = np.zeros(R.shape[1], dtype=int)
    for i, j in par.items():
        certos += (R[i] & H[j])

    perdida = np.maximum(0, n_ref - n_hip).sum() * QUADRO
    falso = np.maximum(0, n_hip - n_ref).sum() * QUADRO
    confusao = (np.minimum(n_ref, n_hip) - certos).sum() * QUADRO
    total = n_ref.sum() * QUADRO

    return {"der": (perdida + falso + confusao) / total if total else 0.0,
            "perdida": perdida, "falso": falso, "confusao": confusao, "total": total,
            "n_ref": len(fr), "n_hip": len(fh)}


def pontuar(args) -> None:
    manifesto = json.loads((args.corpus / "manifesto.json").read_text(encoding="utf-8"))
    print(f"corpus: {len(manifesto)} reuniões, "
          f"{sum(m['duracao_s'] for m in manifesto)/60:.1f} min, collar {args.collar}s\n")
    print(f"{'motor':34s} {'DER':>8s} {'perdida':>9s} {'falso':>8s} {'confusão':>9s} {'tempo':>8s}")
    print("-" * 82)

    detalhes = []
    for caminho in args.hipoteses:
        d = json.loads(caminho.read_text(encoding="utf-8"))
        acc = {"perdida": 0.0, "falso": 0.0, "confusao": 0.0, "total": 0.0}
        por_reuniao = []
        for m in manifesto:
            r = der_de_uma(m["referencia"], d["hipoteses"].get(m["id"], []),
                           m["duracao_s"], args.collar)
            for k in acc:
                acc[k] += r[k]
            por_reuniao.append((m["id"], r))
        t = acc["total"]
        der = (acc["perdida"] + acc["falso"] + acc["confusao"]) / t if t else 0.0
        seg = d.get("segundos")
        print(f"{d['motor'][:34]:34s} {100*der:7.2f}% {100*acc['perdida']/t:8.2f}% "
              f"{100*acc['falso']/t:7.2f}% {100*acc['confusao']/t:8.2f}% "
              f"{(f'{seg:.0f}s' if seg else '—'):>8s}")
        detalhes.append((d["motor"], por_reuniao))

    if args.por_item:
        print()
        for motor, itens in detalhes:
            print(f"--- {motor}")
            for nome, r in itens:
                print(f"    {nome:14s} DER {100*r['der']:6.2f}%  "
                      f"falantes ref={r['n_ref']} hip={r['n_hip']}")


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    sub = p.add_subparsers(dest="cmd", required=True)

    a = sub.add_parser("preparar")
    a.add_argument("--parquet", type=Path, required=True)
    a.add_argument("--saida", type=Path, required=True)
    a.add_argument("--reunioes", type=int, default=2)
    a.add_argument("--max-segundos", type=float, default=600.0, help="0 = reunião inteira")
    a.set_defaults(func=preparar)

    b = sub.add_parser("pyannote")
    b.add_argument("--corpus", type=Path, required=True)
    b.add_argument("--modelo", default="community-1")
    b.set_defaults(func=rodar_pyannote)

    c = sub.add_parser("sherpa")
    c.add_argument("--corpus", type=Path, required=True)
    c.add_argument("--segmentation", type=Path, required=True)
    c.add_argument("--embedding", type=Path, required=True)
    c.add_argument("--threshold", type=float, default=0.5)
    c.add_argument("--num-speakers", type=int, default=-1)
    c.add_argument("--rotulo", default=None)
    # GPU é o padrão: a diarização roda no mesmo hardware que a transcrição, e
    # medir em CPU não diz nada sobre o alvo. Cai para "cpu" se a wheel
    # instalada não tiver o provider CUDA.
    c.add_argument("--provider", default="cuda", choices=["cuda", "cpu"])
    c.set_defaults(func=rodar_sherpa)

    d = sub.add_parser("pontuar")
    d.add_argument("--corpus", type=Path, required=True)
    d.add_argument("--hipoteses", type=Path, nargs="+", required=True)
    d.add_argument("--collar", type=float, default=0.25)
    d.add_argument("--por-item", action="store_true")
    d.set_defaults(func=pontuar)

    args = p.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
