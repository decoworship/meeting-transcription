"""O Nemotron-3 como segundo diarizador do sidecar (MOD-2, 29/09/2026).

Três réguas, da mais barata à que precisa dos pesos:

* a pasta é reconhecida pelos quatro artefatos, e o ``community-1`` não é;
* o embed por bloco — o que o sidecar roda — decide **100% igual** ao embed da
  gravação inteira, que é a referência medida em docs/NEMOTRON-DIARIZACAO.md;
* a saída tem a forma do ``Diarizador`` do pyannote, que é o que o núcleo lê.
"""
import sys
from pathlib import Path

import numpy as np
import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

MODELOS = Path("/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores"
               "/diarizacao/modelos")
WAV = ("/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings"
       "/2026-08-21_11-00-33/system.wav")


def test_a_pasta_e_nemotron_pelos_quatro_artefatos(tmp_path, monkeypatch):
    import motor

    monkeypatch.setattr(motor, "_LOCAIS", str(tmp_path))
    pasta = tmp_path / "nemotron-3"
    pasta.mkdir()
    for a in ("embed.onnx", "step.onnx", "mel.npy"):
        (pasta / a).write_bytes(b"x")
    # Faltando um, não é Nemotron: cai no pyannote, que recusa alto.
    assert motor._nemotron3_local("nemotron-3") is None
    (pasta / "silencio.npy").write_bytes(b"x")
    assert motor._nemotron3_local("nemotron-3") == str(pasta)
    assert motor._nemotron3_local("../nemotron-3") is None
    assert motor._nemotron3_local("") is None


def test_o_community_1_nao_e_nemotron(tmp_path, monkeypatch):
    import motor

    monkeypatch.setattr(motor, "_LOCAIS", str(tmp_path))
    (tmp_path / "community-1").mkdir()
    (tmp_path / "community-1" / "config.yaml").write_text("x")
    assert motor._nemotron3_local("community-1") is None


def _modelo():
    pasta = MODELOS / "nemotron-3"
    if not (pasta / "step.onnx").is_file() or not Path(WAV).is_file():
        pytest.skip("sem os pesos do Nemotron-3 ou sem a gravação de referência")
    import soundfile as sf
    from nemotron3 import Nemotron3

    onda, taxa = sf.read(WAV, dtype="float32")
    return Nemotron3(pasta, gpu=True), onda[: 120 * taxa], taxa


def test_o_embed_por_bloco_decide_igual_ao_inteiro():
    from nemotron3 import sigmoid

    eng, onda, _ = _modelo()
    inteiro = sigmoid(eng.logits(onda, "offline")) > 0.5
    bloco = sigmoid(eng.logits_por_bloco(onda, "offline")) > 0.5
    assert inteiro.shape == bloco.shape
    assert (inteiro == bloco).mean() == 1.0


def test_a_saida_tem_a_forma_do_pyannote():
    eng, onda, taxa = _modelo()
    segs = eng(onda, taxa)
    assert segs, "dois minutos de reunião sem fala nenhuma"
    assert all(set(s) == {"inicio", "fim", "falante"} for s in segs)
    assert all(s["falante"].startswith("SPEAKER_") for s in segs)
    assert all(0 <= s["inicio"] < s["fim"] <= len(onda) / taxa + 0.01 for s in segs)
    assert eng(np.zeros(100, np.float32), taxa) == []
