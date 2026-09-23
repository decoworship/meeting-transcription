import numpy as np, sys, wave
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

RAIZ = Path("/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores"
            "/diarizacao/modelos/community-1")
WAV = ("/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings"
       "/2026-08-25_08-59-22/mix.wav")


def _onda(n_segundos):
    with wave.open(WAV, "rb") as w:
        taxa = w.getframerate()
        q = w.readframes(int(n_segundos * taxa))
    return np.frombuffer(q, np.int16).astype(np.float32) / 32768.0, taxa


def test_a_janela_deslizante_bate_com_o_Inference_do_pyannote():
    """60 s de áudio real, contra o Inference com skip_aggregation."""
    import torch
    from pyannote.audio import Model, Inference
    from segmentacao import Segmentador

    onda, taxa = _onda(60)

    m = Model.from_pretrained(RAIZ / "segmentation" / "pytorch_model.bin").eval()
    inf = Inference(m, duration=10.0, step=1.0, skip_aggregation=True, batch_size=32)
    esperado = inf({"waveform": torch.from_numpy(onda)[None], "sample_rate": taxa})

    obtido = Segmentador(RAIZ / "segmentation" / "model.onnx",
                         preferir_gpu=False)(onda, taxa)

    assert obtido.data.shape == esperado.data.shape, \
        (obtido.data.shape, esperado.data.shape)
    # as janelas têm de cair no mesmo lugar, senão tudo depois desalinha
    assert abs(obtido.sliding_window.start - esperado.sliding_window.start) < 1e-9
    assert abs(obtido.sliding_window.step - esperado.sliding_window.step) < 1e-9
    assert np.abs(obtido.data - esperado.data).max() < 1e-2


def test_a_janela_final_incompleta_bate_com_o_Inference_do_pyannote():
    """60,5 s: número não-múltiplo de 1 s, para exercitar o `mode="pad"`.

    60 s exatos fazem `len(onda) - n` cair num múltiplo de `passo`, e o
    `if inicios[-1] + n < len(onda)` do Segmentador nunca dispara — o teste
    de 60 s puros não cobre a janela final coberta com zeros. 60,5 s cobre.
    """
    import torch
    from pyannote.audio import Model, Inference
    from segmentacao import Segmentador

    onda, taxa = _onda(60.5)
    assert (len(onda) - int(10.0 * taxa)) % int(1.0 * taxa) != 0

    m = Model.from_pretrained(RAIZ / "segmentation" / "pytorch_model.bin").eval()
    inf = Inference(m, duration=10.0, step=1.0, skip_aggregation=True, batch_size=32)
    esperado = inf({"waveform": torch.from_numpy(onda)[None], "sample_rate": taxa})

    obtido = Segmentador(RAIZ / "segmentation" / "model.onnx",
                         preferir_gpu=False)(onda, taxa)

    assert obtido.data.shape == esperado.data.shape, \
        (obtido.data.shape, esperado.data.shape)
    assert abs(obtido.sliding_window.start - esperado.sliding_window.start) < 1e-9
    assert abs(obtido.sliding_window.step - esperado.sliding_window.step) < 1e-9
    assert np.abs(obtido.data - esperado.data).max() < 1e-2
