import numpy as np, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

RAIZ = Path("/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores"
            "/diarizacao/modelos/community-1")

def test_embedding_onnx_respeita_os_pesos():
    """O caminho ONNX com máscara bate com o torch COM a mesma máscara.

    É o teste que pega o defeito do artefato de 18/09/2026: um embedding que
    ignora `weights` passa num teste sem máscara e erra todo trecho com dois
    falantes.
    """
    import torch, onnxruntime as ort
    from pyannote.audio import Model
    from fbank import banco_mel, fbank_centrado
    from embedding import estatisticas_ponderadas

    rng = np.random.default_rng(0)
    onda = rng.standard_normal(16000 * 5).astype(np.float32) * 0.1
    # máscara que liga só a primeira metade — é onde o defeito aparece
    m = Model.from_pretrained(RAIZ / "embedding" / "pytorch_model.bin").eval()
    with torch.no_grad():
        n_quadros = m.compute_fbank(torch.from_numpy(onda)[None, None, :]).shape[1]
    pesos = np.zeros((1, n_quadros), np.float32)
    pesos[0, : n_quadros // 2] = 1.0

    with torch.no_grad():
        esperado = m(torch.from_numpy(onda)[None, None, :],
                     weights=torch.from_numpy(pesos)).numpy()

    cod = ort.InferenceSession(str(RAIZ / "embedding" / "codificador.onnx"),
                               providers=["CPUExecutionProvider"])
    cab = ort.InferenceSession(str(RAIZ / "embedding" / "cabeca.onnx"),
                               providers=["CPUExecutionProvider"])
    fb = fbank_centrado(onda, banco_mel())
    quadros = cod.run(None, {"fbank": fb})[0]
    stats = estatisticas_ponderadas(quadros, pesos)
    obtido = cab.run(None, {"estatisticas": stats})[0]

    assert np.abs(obtido - esperado).max() < 1e-3, np.abs(obtido - esperado).max()
