"""O vetor de voz sem torch, contra o caminho de produção de verdade.

**A lacuna que este teste fecha.** O S1 (``tools/medir_vetor_onnx.py``,
10/09/2026) mediu o ONNX contra ``modelo(onda)`` — uma chamada direta ao
modelo. A produção nunca fez isso: ela usa
``Inference(modelo, window="whole")``, que passa pelo ``model.audio`` antes do
forward. Comparar contra a chamada direta e assumir que dá no mesmo é
exatamente a classe de diferença que este porte já produziu três vezes (a
janela fora de lugar, o NaN inventado, o limiar 150x errado), então a régua
aqui é o ``Inference``.
"""
import numpy as np, sys, wave
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

RAIZ_VOZ = Path("/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores"
                "/diarizacao/modelos/wespeaker-voxceleb-resnet34-LM")
WAV = ("/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings"
       "/2026-08-25_08-59-22/mix.wav")

#: os mesmos trechos que o núcleo manda: pedaços soltos, concatenados
TRECHOS = ((12.0, 20.0), (35.0, 48.5), (61.25, 70.0))


def _onda_concatenada():
    with wave.open(WAV, "rb") as w:
        taxa = w.getframerate()
        q = w.readframes(90 * taxa)
    onda = np.frombuffer(q, np.int16).astype(np.float32) / 32768.0
    return np.concatenate([onda[int(a * taxa):int(b * taxa)] for a, b in TRECHOS]), taxa


def test_vetor_de_voz_bate_com_o_Inference_whole_do_pyannote():
    """O caminho de produção (Inference window="whole") contra o ONNX."""
    import torch
    from pyannote.audio import Model, Inference
    from voz import ExtratorDeVoz

    onda, taxa = _onda_concatenada()

    modelo = Model.from_pretrained(RAIZ_VOZ / "pytorch_model.bin")
    esperado = np.asarray(
        Inference(modelo, window="whole")(
            {"waveform": torch.from_numpy(onda).unsqueeze(0), "sample_rate": taxa}
        )
    ).astype(np.float64).ravel()

    obtido = np.asarray(ExtratorDeVoz(RAIZ_VOZ, preferir_gpu=False)(onda),
                        dtype=np.float64).ravel()

    assert obtido.shape == esperado.shape == (256,)
    cos = float(np.dot(obtido, esperado)
                / (np.linalg.norm(obtido) * np.linalg.norm(esperado)))
    # O cosseno é a régua porque é a conta que o app faz com estes vetores
    # (Vozes.LimiarDeReconhecimento). A diferença absoluta entra junto para
    # pegar uma escala trocada, que o cosseno sozinho não veria.
    assert cos > 0.9999, cos
    assert np.abs(obtido - esperado).max() < 1e-2, np.abs(obtido - esperado).max()


def test_inference_whole_nao_faz_nada_alem_do_forward():
    """Por que o S1 valia mesmo comparando com a chamada direta.

    ``Inference.__call__`` com ``window="whole"`` faz três coisas: fixa as
    sementes, passa o dict por ``model.audio`` (downmix e reamostragem) e
    chama ``model(onda[None])[0]``. Com áudio mono a 16 kHz — o único que o
    nosso gravador produz — o ``model.audio`` é identidade, e sobra o forward.
    Este teste prende essa igualdade: se uma versão nova do pyannote puser
    normalização ou padding aí, ele quebra aqui, e não no banco de vozes.
    """
    import torch
    from pyannote.audio import Model, Inference

    onda, taxa = _onda_concatenada()
    modelo = Model.from_pretrained(RAIZ_VOZ / "pytorch_model.bin")
    arquivo = {"waveform": torch.from_numpy(onda).unsqueeze(0), "sample_rate": taxa}

    pelo_inference = np.asarray(Inference(modelo, window="whole")(arquivo))
    onda_do_audio, taxa_do_audio = modelo.audio(arquivo)
    assert taxa_do_audio == taxa
    assert torch.equal(onda_do_audio, arquivo["waveform"])
    with torch.no_grad():
        direto = modelo(onda_do_audio[None]).numpy()[0]
    assert np.array_equal(pelo_inference, direto)


def test_o_caminho_da_voz_nao_importa_torch():
    """A régua do porte inteiro: extrair uma voz sem torch carregado.

    Num subprocesso, porque no processo do pytest os outros testes já
    importaram torch — e aí a checagem passaria sempre, medindo nada.
    """
    import subprocess, sys as _sys, textwrap

    codigo = textwrap.dedent(f"""
        import sys, wave, numpy as np
        sys.path.insert(0, {str(Path(__file__).resolve().parents[1])!r})
        from voz import ExtratorDeVoz
        with wave.open({WAV!r}, "rb") as w:
            taxa = w.getframerate(); q = w.readframes(20 * taxa)
        onda = np.frombuffer(q, np.int16).astype(np.float32) / 32768.0
        v = ExtratorDeVoz({str(RAIZ_VOZ)!r}, preferir_gpu=False)(onda)
        pesados = [m for m in ("torch", "torchaudio", "pyannote") if m in sys.modules]
        print(len(v), pesados)
    """)
    r = subprocess.run([_sys.executable, "-c", codigo], capture_output=True, text=True)
    assert r.returncode == 0, r.stderr
    assert r.stdout.strip() == "256 []", r.stdout
