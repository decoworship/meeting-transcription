import numpy as np, sys, wave
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

RAIZ = Path("/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores"
            "/diarizacao/modelos/community-1")
WAV = ("/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings"
       "/2026-08-25_08-59-22/mix.wav")


def test_embeddings_batem_com_o_get_embeddings_do_pyannote():
    """60 s reais: o vetor de cada (janela, falante), contra o pyannote."""
    import torch
    from pyannote.audio import Model
    from pyannote.audio.pipelines import SpeakerDiarization
    from segmentacao import Segmentador
    from embedding import Extrator

    with wave.open(WAV, "rb") as w:
        taxa = w.getframerate(); q = w.readframes(60 * w.getframerate())
    onda = np.frombuffer(q, np.int16).astype(np.float32) / 32768.0

    seg = Segmentador(RAIZ / "segmentation" / "model.onnx", preferir_gpu=False)
    binaria = seg(onda, taxa)

    pipe = SpeakerDiarization(
        segmentation=str(RAIZ / "segmentation" / "pytorch_model.bin"),
        embedding=str(RAIZ / "embedding" / "pytorch_model.bin"),
        embedding_exclude_overlap=True,
    )
    arquivo = {"waveform": torch.from_numpy(onda)[None], "sample_rate": taxa,
               "uri": "teste"}
    esperado = pipe.get_embeddings(arquivo, binaria, exclude_overlap=True)

    obtido = Extrator(RAIZ / "embedding", preferir_gpu=False)(
        onda, binaria, excluir_sobreposicao=True)

    assert obtido.shape == esperado.shape, (obtido.shape, esperado.shape)
    # NaN aparece onde o falante não fala na janela, e tem de aparecer nos dois
    assert np.array_equal(np.isnan(obtido), np.isnan(esperado))
    ok = ~np.isnan(obtido)
    assert np.abs(obtido[ok] - esperado[ok]).max() < 1e-2
