#!/usr/bin/env python3
"""Exporta o nvidia/Nemotron-3-Diarization para os dois grafos do porte (25/09/2026).

Gera em ``tools/_nemotron3/`` (derivado, fora do git):

- ``embed.onnx``  (2 MB)   — mel ``[1, N, 128]`` → embeds ``[1, ceil(N/8), 512]``;
- ``step.onnx``   (377 MB) — embeds de um passo ``[cache, FIFO, bloco, contexto]``
  → logits a 10 ms ``[1, 8T, 8]``. As posições do RoPE recomeçam a cada passo;
- ``mel.npy`` — o banco de filtros slaney (librosa), para o sidecar não precisar
  de librosa;
- ``silencio.npy`` — o ``silence_embeds`` aprendido, que o cache comprimido usa.

A receita dos dois grafos é a do ``NealCaren/Nemotron-3-Diarization-ONNX``. O
laço e o cache ficam em numpy, em ``tools/nemotron3_onnx.py``.

**fp16 não está aqui de propósito.** O ``onnxconverter_common`` quebra no RoPE
(um ``Cast`` explícito para float), e contornado com ``op_block_list`` trava.

Precisa do ``transformers`` do git, que o ``.venv`` do projeto não tem::

    uv run --with "git+https://github.com/huggingface/transformers" \\
        --with onnx --with librosa python tools/exportar_nemotron3_onnx.py
"""
from pathlib import Path

import librosa
import numpy as np
import torch
from transformers import AutoModelForAudioFrameClassification

DESTINO = Path(__file__).parent / "_nemotron3"


def main() -> None:
    DESTINO.mkdir(exist_ok=True)
    m = AutoModelForAudioFrameClassification.from_pretrained(
        "nvidia/Nemotron-3-Diarization", attn_implementation="eager").eval()

    class Embed(torch.nn.Module):
        def __init__(s):
            super().__init__()
            s.e = m.model.audio_tower.embedder

        def forward(s, feats):
            return s.e(feats)

    class Step(torch.nn.Module):
        def __init__(s):
            super().__init__()
            s.m, s.c = m.model, m.classifier

        def forward(s, embeds):
            pos = torch.arange(embeds.shape[1], device=embeds.device)[None, :]
            return s.c(s.m(inputs_embeds=embeds, position_ids=pos).last_hidden_state)

    with torch.no_grad():
        torch.onnx.export(Embed(), (torch.randn(1, 340 * 8, 128),), DESTINO / "embed.onnx",
                          input_names=["features"], output_names=["embeds"],
                          dynamic_axes={"features": {1: "n"}, "embeds": {1: "t"}},
                          opset_version=17, dynamo=False)
        torch.onnx.export(Step(), (torch.randn(1, 420, 512),), DESTINO / "step.onnx",
                          input_names=["embeds"], output_names=["logits"],
                          dynamic_axes={"embeds": {1: "t"}, "logits": {1: "t8"}},
                          opset_version=17, dynamo=False)

    np.save(DESTINO / "mel.npy", librosa.filters.mel(
        sr=16000, n_fft=512, n_mels=128, fmin=0.0, fmax=8000, norm="slaney").astype(np.float32))
    np.save(DESTINO / "silencio.npy", m.silence_embeds.detach().numpy().astype(np.float32))
    for f in sorted(DESTINO.iterdir()):
        print(f"{f.name:14s} {f.stat().st_size / 2**20:8.1f} MB")


if __name__ == "__main__":
    main()
