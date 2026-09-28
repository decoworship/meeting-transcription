#!/usr/bin/env python3
"""Quais DLLs de CUDA os motores carregam de fato (26/09/2026).

O instalador leva ~2,2 GB de ``nvidia/*`` e parte disso nenhum motor abre — mas
``cublasLt`` e ``cudnn64_9`` carregam DLLs **por nome, em tempo de execução**,
e o ``objdump`` não vê isso. A única régua é rodar. Este script roda os quatro
motores que usam a placa, cada um em CUDA, sobre 3 min de uma gravação, e
imprime uma linha JSON por motor: onde rodou, quanto levou e um hash da saída.

Roda **numa cópia** do ``motores/python`` da instalação, de onde se apagam os
candidatos, e o resultado se compara com o da cópia intacta. Mesmo hash e
mesmo dispositivo = a DLL não fazia falta. A cópia não tem ``ata/bin``, de
propósito: a diarização procura DLLs lá, e o instalador não leva o motor de
ata — medir com ele presente esconderia uma dependência::

    T=/mnt/c/Users/andre/pulsemeet-medicoes/motores-teste
    $T/python/python.exe tools/conferir_dlls_cuda.py
"""
from __future__ import annotations

import hashlib
import json
import os
import sys
import time
import traceback
import wave
from pathlib import Path

import numpy as np

SITE = Path(sys.prefix) / "Lib" / "site-packages"
MOTORES = Path(sys.prefix).parent
INSTALADO = Path(r"C:\Users\andre\AppData\Local\Programs\MeetingApp\motores")
AUDIO = Path(r"C:\Users\andre\OneDrive\Documents\MeetingRecordings\2026-08-21_11-00-33\mix.wav")
NEMOTRON3 = Path(r"C:\Users\andre\pulsemeet-medicoes\nemotron3")
TAXA = 16000


def audio(segundos: int = 180) -> np.ndarray:
    with wave.open(str(AUDIO), "rb") as w:
        return np.frombuffer(w.readframes(segundos * TAXA), "<i2").astype(np.float32) / 32768


def h(x) -> str:
    return hashlib.sha1(json.dumps(x, ensure_ascii=False).encode()).hexdigest()[:12]


def achar_cuda():
    """O mesmo que ``motores/asr/motor.py:_achar_cuda``, sobre a cópia."""
    pastas = [SITE / "ctranslate2", SITE / "onnxruntime" / "capi"]
    pastas += sorted((SITE / "nvidia").glob("*/bin"))
    pastas = [str(p) for p in pastas if p.is_dir()]
    for p in pastas:
        os.add_dll_directory(p)
    os.environ["PATH"] = os.pathsep.join(pastas) + os.pathsep + os.environ["PATH"]


def asr(a):
    from faster_whisper import WhisperModel
    snap = next((Path.home() / ".cache" / "huggingface" / "hub" / "models--Systran--faster-whisper-large-v3"
                 / "snapshots").iterdir())
    m = WhisperModel(str(snap), device="cuda", compute_type="float16")
    t = time.perf_counter()
    segs, _ = m.transcribe(a, language="pt", beam_size=5, condition_on_previous_text=False,
                           word_timestamps=True, vad_filter=True)
    txt = [(round(s.start, 2), s.text) for s in segs]
    return "cuda", time.perf_counter() - t, txt


def legenda(a):
    import transcribe_cpp as t
    m = t.Model(str(INSTALADO / "legenda" / "modelos"
                    / "nemotron-3.5-asr-streaming-0.6b-Q8_0.gguf"), backend="cuda")
    t0 = time.perf_counter()
    r = t.transcribe(m, a, language="pt-BR")
    return "cuda", time.perf_counter() - t0, r.text


def pyannote(a):
    sys.path.insert(0, str(MOTORES / "diarizacao" / "pipeline"))
    from diarizacao import Diarizador
    d = Diarizador(MOTORES / "diarizacao" / "modelos" / "community-1", preferir_gpu=True)
    t = time.perf_counter()
    r = d(a)
    return f"{d.seg.provedor}/{d.emb.provedor}", time.perf_counter() - t, \
        [(round(x["inicio"], 2), x["falante"]) for x in r]


def voz(a):
    sys.path.insert(0, str(MOTORES / "diarizacao" / "pipeline"))
    from voz import ExtratorDeVoz
    v = ExtratorDeVoz(MOTORES / "diarizacao" / "modelos" / "wespeaker-voxceleb-resnet34-LM")
    t = time.perf_counter()
    r = v(a[: 30 * TAXA])
    return v.provedor, time.perf_counter() - t, [round(float(x), 3) for x in np.ravel(r)[:64]]


def nemotron3(a):
    import onnxruntime as ort
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import nemotron3_onnx as n3
    eng = n3.Nemotron3(NEMOTRON3, gpu=True)
    t = time.perf_counter()
    lg = eng.logits(a, "offline")
    return eng.step.get_providers()[0], time.perf_counter() - t, \
        (n3.sigmoid(lg) > 0.5).sum(0).tolist()


def main() -> int:
    achar_cuda()
    a = audio()
    quais = sys.argv[1:] or ["asr", "legenda", "pyannote", "voz", "nemotron3"]
    for nome in quais:
        try:
            onde, seg, saida = globals()[nome](a)
            print(json.dumps({"motor": nome, "onde": onde, "s": round(seg, 2),
                              "hash": h(saida)}), flush=True)
        except Exception as e:
            print(json.dumps({"motor": nome, "erro": f"{type(e).__name__}: {e}"[:300]}),
                  flush=True)
            traceback.print_exc()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
