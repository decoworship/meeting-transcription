import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

def test_valor_desconhecido_cai_no_padrao_em_silencio():
    """Um app.json com lixo na chave não pode recusar a transcrição.

    Mesma razão pela qual o MotorAceito sobreviveu à saída do MOSS com um
    valor só: recusar a diarização por causa de uma chave é pior que
    ignorá-la (docs/CONVERGENCIA.md, "O MOSS é o primeiro a sair").
    """
    from motor import escolher_motor
    assert escolher_motor(None) == "torch"
    assert escolher_motor("torch") == "torch"
    assert escolher_motor("onnx") == "onnx"
    assert escolher_motor("moss") == "torch"
    assert escolher_motor("") == "torch"
