"""Abrir sessão ONNX dizendo em voz alta onde ela vai rodar.

**Por que este arquivo existe.** Em 18/09/2026 uma medição de CUDA EP
devolveu 32,11x o tempo real e quase entrou no registro como resultado. O
provedor tinha falhado ao carregar (`libcublasLt.so.13: cannot open shared
object file`) e caído para CPU **em silêncio** — 27 s que pareciam GPU e eram
CPU.

Pedir um provedor não é obtê-lo. Aqui o efetivo é sempre lido de volta com
`get_providers()` e devolvido a quem chamou.
"""
import sys
from pathlib import Path

import onnxruntime as ort


def abrir(caminho: str | Path, preferir_gpu: bool = True):
    """A sessão e o provedor que ela de fato usa.

    Returns
    -------
    (sessao, provedor_efetivo)
    """
    caminho = str(caminho)
    disponiveis = ort.get_available_providers()

    pedidos = []
    if preferir_gpu and "CUDAExecutionProvider" in disponiveis:
        pedidos.append("CUDAExecutionProvider")
    pedidos.append("CPUExecutionProvider")

    sessao = ort.InferenceSession(caminho, providers=pedidos)
    efetivo = sessao.get_providers()[0]

    if preferir_gpu and efetivo != "CUDAExecutionProvider":
        # não é erro — a máquina pode não ter NVIDIA. Mas é caro e tem de
        # aparecer: a diarização em CPU roda a ~1,28x o tempo real (V1,
        # docs/DIARIZACAO-ONNX.md §5.1), contra 11,80x no CUDA EP (V5).
        print(f"[diarizacao] CUDA indisponível, rodando em {efetivo}. "
              f"Disponíveis: {disponiveis}", file=sys.stderr, flush=True)

    return sessao, efetivo
