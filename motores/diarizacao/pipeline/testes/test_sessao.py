import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

RAIZ = Path("/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores"
            "/diarizacao/modelos/community-1")

def test_devolve_o_provedor_efetivo_e_nao_o_pedido():
    from sessao import abrir
    s, prov = abrir(RAIZ / "segmentation" / "model.onnx", preferir_gpu=False)
    assert prov == "CPUExecutionProvider"
    assert prov in s.get_providers()

def test_cai_para_cpu_com_aviso_quando_nao_ha_gpu(capsys):
    from sessao import abrir
    s, prov = abrir(RAIZ / "segmentation" / "model.onnx", preferir_gpu=True)
    # numa máquina sem CUDA EP a queda é legítima, mas tem de ser DITA
    if prov != "CUDAExecutionProvider":
        assert "CUDA" in capsys.readouterr().err
