"""O log-mel do Kaldi em numpy, sem torch.

O ``compute_fbank`` do pyannote embrulha o fbank num ``torch.vmap``, que
nenhum exportador de ONNX atravessa, e o que sobra dele sem o vmap é o
``aten::fft_rfft``, que o opset 17 não tem. Daí este arquivo — que é o mesmo
caminho que o ``infer_onnx.py`` do WeSpeaker já fazia, e que o
``tools/medir_vetor_onnx.py`` (o S1) validou em 10/09/2026.

Ver docs/DIARIZACAO-ONNX.md §3.
"""
import numpy as np

TAXA, N_MEL = 16000, 80
JANELA, PASSO, PAD = 400, 160, 512          # 25 ms, 10 ms, próxima potência de 2
PREENFASE = 0.97
EPS = np.float32(1.1920929e-07)             # torch.finfo(torch.float).eps


def _hamming(n: int) -> np.ndarray:
    """A janela do Kaldi: ``periodic=False``, alpha 0,54."""
    return (0.54 - 0.46 * np.cos(2 * np.pi * np.arange(n) / (n - 1))).astype(np.float32)


def banco_mel() -> np.ndarray:
    """Os filtros mel, com os parâmetros que o pyannote passa ao Kaldi.

    Vem do torchaudio por ser tabela de constantes — é calculado uma vez na
    exportação e guardado em .npy, para o app não importar torchaudio.
    """
    from torchaudio.compliance.kaldi import get_mel_banks
    import torch
    banco = get_mel_banks(N_MEL, PAD, TAXA, 20.0, 0.0, 100.0, -500.0, 1.0)
    banco = banco[0] if isinstance(banco, tuple) else banco
    return torch.nn.functional.pad(banco, (0, 1)).numpy().astype(np.float32)


def fbank(onda: np.ndarray, mel: np.ndarray) -> np.ndarray:
    """O log-mel. ``onda`` já vem na escala do pyannote (×2¹⁵)."""
    m = 1 + (len(onda) - JANELA) // PASSO    # snip_edges=True
    if m <= 0:
        return np.zeros((0, N_MEL), np.float32)

    idx = np.arange(m)[:, None] * PASSO + np.arange(JANELA)[None, :]
    q = onda[idx].astype(np.float32)
    q = q - q.mean(axis=1, keepdims=True)                      # remove_dc_offset
    ant = np.concatenate([q[:, :1], q[:, :-1]], axis=1)        # pad "replicate"
    q = (q - PREENFASE * ant) * _hamming(JANELA)[None, :]
    q = np.pad(q, ((0, 0), (0, PAD - JANELA)))

    pot = np.abs(np.fft.rfft(q, axis=1).astype(np.complex64)) ** 2
    return np.log(np.maximum(pot.astype(np.float32) @ mel.T, EPS))


def fbank_centrado(onda: np.ndarray, mel: np.ndarray) -> np.ndarray:
    """O que o ``compute_fbank`` entrega: escala, fbank e a média global fora."""
    f = fbank(onda.astype(np.float32) * np.float32(1 << 15), mel)
    return (f - f.mean(axis=0, keepdims=True))[None, :, :]
