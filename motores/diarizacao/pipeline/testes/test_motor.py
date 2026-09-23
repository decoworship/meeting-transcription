import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

def test_valor_desconhecido_cai_no_padrao_em_silencio():
    """Um app.json com lixo na chave não pode recusar a transcrição.

    Mesma razão pela qual o MotorAceito sobreviveu à saída do MOSS com um
    valor só: recusar a diarização por causa de uma chave é pior que
    ignorá-la (docs/CONVERGENCIA.md, "O MOSS é o primeiro a sair").

    O `"torch"` entrou nessa lista em 22/09/2026, quando o torch saiu do
    empacotamento e a chave voltou a ter um valor só: quem testou o porte tem
    a chave escrita no app.json, e o valor que ela guarda deixou de existir.
    """
    from motor import escolher_motor
    assert escolher_motor(None) == "onnx"
    assert escolher_motor("onnx") == "onnx"
    assert escolher_motor("torch") == "onnx"
    assert escolher_motor("moss") == "onnx"
    assert escolher_motor("") == "onnx"
