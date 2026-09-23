import numpy as np, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

MODELO = ("/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores"
          "/diarizacao/modelos/community-1/embedding/pytorch_model.bin")

def test_fbank_bate_com_o_pyannote():
    """O fbank em numpy contra o compute_fbank do pyannote, no mesmo áudio."""
    import torch
    from pyannote.audio import Model
    from fbank import banco_mel, fbank

    rng = np.random.default_rng(0)
    onda = rng.standard_normal(16000 * 5).astype(np.float32) * 0.1

    m = Model.from_pretrained(MODELO).eval()
    with torch.no_grad():
        esperado = m.compute_fbank(torch.from_numpy(onda)[None, None, :]).numpy()[0]

    obtido = fbank(onda * (1 << 15), banco_mel())
    obtido = obtido - obtido.mean(axis=0, keepdims=True)   # o compute_fbank centra

    assert obtido.shape == esperado.shape, (obtido.shape, esperado.shape)
    assert np.abs(obtido - esperado).max() < 1e-3
