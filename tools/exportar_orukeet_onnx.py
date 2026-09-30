#!/usr/bin/env python3
"""Exporta o Orukeet para ONNX fp32, no formato que o ``onnx_asr`` lê.

**Por que exportar, se o repositório já traz ONNX.** O ONNX de lá é int8, e o
int8 do Parakeet já mostrou perder conteúdo (14% menos tokens) e ser mais lento
na GPU (CONVERGENCIA.md, 11/09/2026). A linha de base é o Parakeet v3 em
**fp32**; comparar o Orukeet em int8 contra ele misturaria modelo e quantização
de novo.

**O grafo é o do Parakeet v3**, com outros pesos: o card diz que os filtros de
Gabor foram materializados como convolução comum. Por isso o ``nemo128.onnx``
(o pré-processador) é copiado do Parakeet fp32, e o ``config.json`` também —
isto confere que as duas configurações de pré-processamento são iguais antes de
copiar.

Uso::

    V=~/.cache/pulsemeet-medicoes/venv-nemo
    $V/bin/python tools/exportar_orukeet_onnx.py
"""

from __future__ import annotations

import shutil
from pathlib import Path

RAIZ = Path.home() / ".cache" / "pulsemeet-medicoes"
NEMO = RAIZ / "orukeet" / "orukeet-v0.1.0.nemo"
DESTINO = RAIZ / "orukeet-fp32"
PARAKEET = RAIZ / "parakeet-fp32"


def main() -> int:
    import nemo.collections.asr as nemo_asr

    m = nemo_asr.models.ASRModel.restore_from(str(NEMO), map_location="cpu").eval()
    pre = m.cfg.preprocessor
    print("pré-processador:", {k: pre.get(k) for k in
                               ("features", "n_fft", "window_size", "window_stride",
                                "normalize", "dither", "sample_rate")})
    assert pre.features == 128 and pre.sample_rate == 16000, pre

    DESTINO.mkdir(parents=True, exist_ok=True)
    m.export(str(DESTINO / "model.onnx"))
    with (DESTINO / "vocab.txt").open("wt", encoding="utf-8") as f:
        for i, t in enumerate([*m.tokenizer.vocab, "<blk>"]):
            f.write(f"{t} {i}\n")
    for nome in ("nemo128.onnx", "config.json"):
        shutil.copy(PARAKEET / nome, DESTINO / nome)
    for p in sorted(DESTINO.iterdir()):
        print(f"  {p.stat().st_size/1e6:9.1f} MB  {p.name}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
