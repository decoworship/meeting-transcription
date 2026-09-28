#!/usr/bin/env python3
"""CUDA contra Vulkan/DirectML: o mesmo modelo, outro acelerador (26/09/2026).

A pergunta é da convergência (docs/CONVERGENCIA.md): **dá para o app rodar sem
as bibliotecas de CUDA, e o que isso custa?** O driver da NVIDIA traz só o
``nvcuda.dll``; cuBLAS, cuDNN e cuFFT viajam no instalador. Vulkan (ggml) e
DirectML (onnxruntime) falam direto com o driver, e rodam em AMD e Intel.

Três motores, cada um nos dois aceleradores:

``asr``
    O Nemotron-3.5 da legenda, **em bloco de 3 min**, pelo ``transcribe_cpp``
    com ``backend=cuda`` ou ``backend=vulkan``. É o candidato a passada final
    que dispensaria o ``faster-whisper`` — o único motor preso ao CUDA.
``pyannote``
    A diarização de produção (``motores/diarizacao/pipeline``, community-1),
    com o CUDA EP ou o DirectML EP.
``nemotron3``
    O Nemotron-3-Diarization em ONNX (docs/NEMOTRON-DIARIZACAO.md), offline,
    com o CUDA EP ou o DirectML EP.

**Roda no Python do Windows, não no WSL**: lá o Vulkan é emulado e o DirectML
não existe. O lado CUDA usa o Python embarcado da instalação oficial — é o que
roda em produção —; o lado DirectML, um venv com ``onnxruntime-directml``
(os dois pacotes se chamam ``onnxruntime`` e não convivem)::

    APP=/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores/python/python.exe
    DML=/mnt/c/Users/andre/pulsemeet-medicoes/venv-dml/Scripts/python.exe
    $APP tools/medir_aceleradores.py asr --acel cuda
    $APP tools/medir_aceleradores.py asr --acel vulkan
    $APP tools/medir_aceleradores.py pyannote --acel cuda
    $DML tools/medir_aceleradores.py pyannote --acel dml
    ...

Cada rodada escreve ``<saida>/<motor>-<acel>/<gravação>/transcricao.json`` com
o ``gemini.md`` ao lado, e um ``tempos.json``. Na diarização o **texto é o do
app, intocado**: só o falante de cada segmento é trocado, e a régua é o
``comparar_com_gemini.py``. No ASR a régua é o ``wer_contra_gemini.py``.

**Confira o nvidia-smi antes**: a mesma 2060 roda a legenda de reunião de
verdade, e velocidade com a placa ocupada é piso, não medida.
"""
from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
import threading
import time
import wave
from pathlib import Path

import numpy as np

AQUI = Path(__file__).resolve().parent
ACERVO = Path(r"C:\Users\andre\OneDrive\Documents\MeetingRecordings")
MOTORES = Path(r"C:\Users\andre\AppData\Local\Programs\MeetingApp\motores")
SAIDA = Path(r"C:\Users\andre\pulsemeet-medicoes\aceleradores")
NEMOTRON3 = Path(r"C:\Users\andre\pulsemeet-medicoes\nemotron3")   # cópia de tools/_nemotron3
GGUF = MOTORES / "legenda" / "modelos" / "nemotron-3.5-asr-streaming-0.6b-Q8_0.gguf"
TAXA = 16000
BLOCO_S = 180

#: as duas mais curtas das quatro com Gemini; as outras duas se ficar dúvida
PADRAO = ["2026-08-21_11-00-33", "2026-08-25_08-59-22"]

EP = {"cuda": "CUDAExecutionProvider", "dml": "DmlExecutionProvider",
      "cpu": "CPUExecutionProvider"}


def onda(gravacao: str) -> np.ndarray:
    with wave.open(str(ACERVO / gravacao / "mix.wav"), "rb") as w:
        assert w.getframerate() == TAXA and w.getsampwidth() == 2, gravacao
        a = np.frombuffer(w.readframes(w.getnframes()), "<i2").astype(np.float32) / 32768
        return a.reshape(-1, w.getnchannels()).mean(1)


class Vram:
    """Pico de VRAM da placa durante a rodada, pelo nvidia-smi."""

    def __init__(self):
        self.base = self.pico = self._ler()
        self._vivo = True
        threading.Thread(target=self._vigiar, daemon=True).start()

    @staticmethod
    def _ler() -> int:
        r = subprocess.run(["nvidia-smi", "--query-gpu=memory.used",
                            "--format=csv,noheader,nounits"], capture_output=True, text=True)
        return int(r.stdout.split()[0])

    def _vigiar(self):
        while self._vivo:
            self.pico = max(self.pico, self._ler())
            time.sleep(0.2)

    def parar(self) -> int:
        self._vivo = False
        return self.pico - self.base


def destino(motor: str, acel: str, gravacao: str) -> Path:
    d = SAIDA / f"{motor}-{acel}" / gravacao
    d.mkdir(parents=True, exist_ok=True)
    shutil.copy(ACERVO / gravacao / "gemini.md", d / "gemini.md")
    return d


def segmentos_do_app(gravacao: str) -> dict:
    return json.loads((ACERVO / gravacao / "transcricao.json").read_text(encoding="utf-8"))


# ------------------------------------------------------------------ ASR

def rodar_asr(acel: str):
    import transcribe_cpp as t

    t0 = time.perf_counter()
    modelo = t.Model(str(GGUF), backend=acel)
    carga = time.perf_counter() - t0
    t.transcribe(modelo, np.zeros(TAXA * 5, np.float32), language="pt-BR")   # aquece

    def por_gravacao(a: np.ndarray):
        segs = []
        for ini in range(0, len(a), BLOCO_S * TAXA):
            pcm = a[ini: ini + BLOCO_S * TAXA]
            if len(pcm) < TAXA:
                continue
            r = t.transcribe(modelo, pcm, language="pt-BR", timestamps="segment")
            off = ini / TAXA
            segs += [{"start": off + s.t0_ms / 1000, "end": off + s.t1_ms / 1000,
                      "text": s.text} for s in r.segments if s.text.strip()]
        return {"segments": segs}

    return carga, por_gravacao


# ------------------------------------------------------------------ diarização

def rotular_por_sobreposicao(segs: list[dict], turnos: list[tuple[float, float, str]]):
    for s in segs:
        melhor, quem = 0.0, None
        for i, f, r in turnos:
            sob = min(s["end"], f) - max(s["start"], i)
            if sob > melhor:
                melhor, quem = sob, r
        s["speaker"] = quem or "?"


def rodar_pyannote(acel: str):
    pipeline = MOTORES / "diarizacao" / "pipeline"
    sys.path.insert(0, str(pipeline))
    import onnxruntime as ort
    import sessao

    # a produção só sabe pedir CUDA; aqui o provedor é o da rodada, e o
    # efetivo é conferido — um DirectML que cai calado para CPU mediria a CPU
    if acel == "cuda":
        sessao._preparar_path_cuda_windows()

    def abrir(caminho, preferir_gpu=True):
        s = ort.InferenceSession(str(caminho), providers=[EP[acel], "CPUExecutionProvider"])
        efetivo = s.get_providers()[0]
        if efetivo != EP[acel]:
            raise SystemExit(f"pediu {EP[acel]}, veio {efetivo}")
        return s, efetivo

    sessao.abrir = abrir
    from diarizacao import Diarizador

    t0 = time.perf_counter()
    d = Diarizador(MOTORES / "diarizacao" / "modelos" / "community-1")
    carga = time.perf_counter() - t0
    d(np.zeros(TAXA * 30, np.float32))   # aquece

    def por_gravacao(a: np.ndarray, gravacao: str):
        dados = segmentos_do_app(gravacao)
        turnos = [(x["inicio"], x["fim"], x["falante"]) for x in d(a)]
        return dados, turnos

    return carga, por_gravacao


def rodar_nemotron3(acel: str):
    sys.path.insert(0, str(AQUI))
    import onnxruntime as ort
    if acel == "cuda":
        ort.preload_dlls()
    import nemotron3_onnx as n3

    t0 = time.perf_counter()
    eng = n3.Nemotron3.__new__(n3.Nemotron3)
    pr = [EP[acel], "CPUExecutionProvider"]
    eng.embed = ort.InferenceSession(str(NEMOTRON3 / "embed.onnx"), providers=pr)
    eng.step = ort.InferenceSession(str(NEMOTRON3 / "step.onnx"), providers=pr)
    for s in (eng.embed, eng.step):
        if s.get_providers()[0] != EP[acel]:
            raise SystemExit(f"pediu {EP[acel]}, veio {s.get_providers()[0]}")
    eng.filtros = np.load(NEMOTRON3 / "mel.npy")
    eng.sil = np.load(NEMOTRON3 / "silencio.npy")
    carga = time.perf_counter() - t0
    eng.logits(np.zeros(TAXA * 30, np.float32))   # aquece

    def por_gravacao(a: np.ndarray, gravacao: str):
        dados = segmentos_do_app(gravacao)
        turnos = [(x["Start"], x["End"], f"spk{x['Speaker']}")
                  for x in n3.segmentos(eng.logits(a, "offline"))]
        return dados, turnos

    return carga, por_gravacao


# ------------------------------------------------------------------ main

def main() -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("motor", choices=["asr", "pyannote", "nemotron3"])
    p.add_argument("--acel", required=True, choices=["cuda", "vulkan", "dml", "cpu"])
    p.add_argument("--gravacoes", nargs="*", default=PADRAO)
    a = p.parse_args()

    vram = Vram()
    fabrica = {"asr": rodar_asr, "pyannote": rodar_pyannote, "nemotron3": rodar_nemotron3}
    carga, rodar = fabrica[a.motor](a.acel)
    tempos = {"motor": a.motor, "acel": a.acel, "carga_s": round(carga, 2), "gravacoes": {}}
    print(f"{a.motor} em {a.acel}: carregou em {carga:.1f} s", flush=True)

    for g in a.gravacoes:
        audio = onda(g)
        dur = len(audio) / TAXA
        t0 = time.perf_counter()
        if a.motor == "asr":
            dados = rodar(audio)
        else:
            dados, turnos = rodar(audio, g)
        gasto = time.perf_counter() - t0
        if a.motor != "asr":
            rotular_por_sobreposicao(dados["segments"], turnos)
        d = destino(a.motor, a.acel, g)
        (d / "transcricao.json").write_text(json.dumps(dados, ensure_ascii=False), encoding="utf-8")
        tempos["gravacoes"][g] = {"audio_s": round(dur, 1), "gasto_s": round(gasto, 2),
                                  "xrt": round(dur / gasto, 2)}
        print(f"  {g}  {dur / 60:5.1f} min  {gasto:7.1f} s  {dur / gasto:6.1f}x", flush=True)

    tempos["vram_mb"] = vram.parar()
    print(f"  VRAM do processo: ~{tempos['vram_mb']} MB", flush=True)
    (SAIDA / f"{a.motor}-{a.acel}" / "tempos.json").write_text(
        json.dumps(tempos, indent=2), encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
