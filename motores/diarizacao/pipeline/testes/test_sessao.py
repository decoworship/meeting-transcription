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


def test_tenta_de_novo_em_cpu_quando_cuda_falha_ao_carregar(monkeypatch, capsys):
    """O caso não gracioso: o provedor CUDA está listado como disponível, mas
    `InferenceSession` levanta ao tentar carregá-lo de verdade (o
    "Cannot load symbol cudnnCreate" medido no achado de 22/09/2026). Como
    esta suíte roda sem GPU real, o CUDA EP disponível e a falha ao carregar
    são simulados — o que se verifica é só a lógica de novo-tentativa e o
    aviso, não o ONNX Runtime em si.
    """
    import sessao

    monkeypatch.setattr(sessao.ort, "get_available_providers",
                         lambda: ["CUDAExecutionProvider", "CPUExecutionProvider"])

    chamadas = []

    class SessaoFalsa:
        def __init__(self, providers):
            self._providers = providers

        def get_providers(self):
            return self._providers

    def inference_session_falsa(caminho, providers):
        chamadas.append(list(providers))
        if "CUDAExecutionProvider" in providers:
            raise RuntimeError("Cannot load symbol cudnnCreate")
        return SessaoFalsa(providers)

    monkeypatch.setattr(sessao.ort, "InferenceSession", inference_session_falsa)
    monkeypatch.setattr(sessao, "_preparar_path_cuda_windows", lambda: None)

    s, prov = sessao.abrir(RAIZ / "segmentation" / "model.onnx", preferir_gpu=True)

    assert prov == "CPUExecutionProvider"
    assert chamadas == [["CUDAExecutionProvider", "CPUExecutionProvider"], ["CPUExecutionProvider"]]
    erro = capsys.readouterr().err
    assert "cudnnCreate" in erro
    assert "CPUExecutionProvider" in erro


def test_propaga_quando_cpu_tambem_falha_apos_cuda_falhar(monkeypatch):
    """Se a CPU também falhar depois da tentativa em CUDA, o erro tem de
    subir — não pode ser engolido silenciosamente."""
    import sessao

    monkeypatch.setattr(sessao.ort, "get_available_providers",
                         lambda: ["CUDAExecutionProvider", "CPUExecutionProvider"])

    def inference_session_sempre_falha(caminho, providers):
        raise RuntimeError(f"falhou com {providers}")

    monkeypatch.setattr(sessao.ort, "InferenceSession", inference_session_sempre_falha)
    monkeypatch.setattr(sessao, "_preparar_path_cuda_windows", lambda: None)

    import pytest
    with pytest.raises(RuntimeError):
        sessao.abrir(RAIZ / "segmentation" / "model.onnx", preferir_gpu=True)
