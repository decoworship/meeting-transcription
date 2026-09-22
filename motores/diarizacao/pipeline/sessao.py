"""Abrir sessão ONNX dizendo em voz alta onde ela vai rodar.

**Por que este arquivo existe.** Em 18/09/2026 uma medição de CUDA EP
devolveu 32,11x o tempo real e quase entrou no registro como resultado. O
provedor tinha falhado ao carregar (`libcublasLt.so.13: cannot open shared
object file`) e caído para CPU **em silêncio** — 27 s que pareciam GPU e eram
CPU.

Pedir um provedor não é obtê-lo. Aqui o efetivo é sempre lido de volta com
`get_providers()` e devolvido a quem chamou.
"""
import sys
from pathlib import Path

import onnxruntime as ort

_PATH_CUDA_PREPARADO = False


def _preparar_path_cuda_windows() -> None:
    """No Windows, põe no PATH as pastas que têm as DLLs do CUDA EP.

    **Por que não basta `os.add_dll_directory()`.** Medido em 18-22/09/2026 na
    instalação real: `onnxruntime_providers_cuda.dll` depende diretamente de
    `cublas64_12.dll`, `cublasLt64_12.dll`, `cudart64_12.dll`, `cudnn64_9.dll`
    e `cufft64_11.dll` — essas o `add_dll_directory` resolve, porque o loader
    do Windows as procura nas pastas registradas por ele. Mas o `cudnn64_9.dll`
    por sua vez carrega suas próprias sublibs (`cudnn_graph64_9.dll`,
    `cudnn_cnn64_9.dll`, `cudnn_ops64_9.dll`, ...) **buscando no PATH do
    processo, não nas pastas que o loader registrou** — é um carregamento de
    segundo nível, feito pela própria DLL em tempo de execução, e
    `add_dll_directory` não alcança isso. Sem a entrada em `PATH`, a sessão
    chega a carregar o provedor CUDA e só então morre com
    "Could not locate cudnn_graph64_9.dll ... Cannot load symbol cudnnCreate".
    A confirmação, na mesma máquina: com `add_dll_directory` sozinho falha
    assim; com a pasta também prependida a `os.environ["PATH"]`, funciona.

    No-op fora do Windows: `os.add_dll_directory` nem existe em outro SO, e
    Linux/macOS resolvem `.so`/`.dylib` por outro mecanismo — é onde a suíte
    de testes e todas as réguas medidas até hoje rodam.

    As pastas são localizadas relativas à própria instalação, não por caminho
    fixo de usuário: `onnxruntime.__file__` aponta para dentro do
    `site-packages` do Python embarcado, e as duas outras (`ata/bin` e
    `ctranslate2`) são vizinhas dela nesse mesmo layout. Uma pasta que não
    existe é só pulada — a sessão ONNX ainda decide o provedor efetivo
    sozinha, e quem chama esta função só quer que ele tenha a chance de achar
    as DLLs.
    """
    global _PATH_CUDA_PREPARADO
    if _PATH_CUDA_PREPARADO or sys.platform != "win32":
        return
    _PATH_CUDA_PREPARADO = True

    import os

    capi = Path(ort.__file__).resolve().parent / "capi"
    # .../motores/python/Lib/site-packages/onnxruntime/capi -> .../motores
    # parents[4] pede len(parents) >= 5 (parents[0] já conta como o primeiro).
    motores = capi.parents[4] if len(capi.parents) >= 5 else None

    candidatas = [capi]
    if motores is not None:
        candidatas.append(motores / "ata" / "bin")
        candidatas.append(
            motores / "python" / "Lib" / "site-packages" / "ctranslate2"
        )
        # Transitório: cufft64_11.dll e as sublibs do cuDNN só existem hoje
        # dentro de torch/lib (docs/DIARIZACAO-ONNX.md §5.2, achado de
        # 22/09/2026). Quando o torch sair, essas DLLs precisam vir de outro
        # lugar, ou o CUDA EP volta a cair para CPU em silêncio.
        candidatas.append(
            motores / "python" / "Lib" / "site-packages" / "torch" / "lib"
        )

    pastas = [str(p) for p in candidatas if p.is_dir()]
    for pasta in pastas:
        try:
            os.add_dll_directory(pasta)
        except (OSError, AttributeError):
            pass

    if pastas:
        os.environ["PATH"] = os.pathsep.join(pastas) + os.pathsep + os.environ.get("PATH", "")


def abrir(caminho: str | Path, preferir_gpu: bool = True):
    """A sessão e o provedor que ela de fato usa.

    Returns
    -------
    (sessao, provedor_efetivo)
    """
    caminho = str(caminho)
    if preferir_gpu:
        _preparar_path_cuda_windows()
    disponiveis = ort.get_available_providers()

    pedidos = []
    if preferir_gpu and "CUDAExecutionProvider" in disponiveis:
        pedidos.append("CUDAExecutionProvider")
    pedidos.append("CPUExecutionProvider")

    try:
        sessao = ort.InferenceSession(caminho, providers=pedidos)
    except Exception as e:
        # A queda graciosa (provedor pedido, ORT usa outro) é tratada abaixo,
        # pelo get_providers(). Isto aqui é a queda que NÃO é graciosa: o CUDA
        # EP chega a começar a carregar e morre no meio, ex.
        # "Cannot load symbol cudnnCreate" quando falta alguma DLL — visto na
        # máquina do segundo usuário (SUP-2). Se CUDA foi pedido, tenta de
        # novo só com CPU; se não foi, ou a CPU também falhar, propaga.
        if "CUDAExecutionProvider" not in pedidos:
            raise
        print(f"[diarizacao] falha ao carregar o provedor CUDA: {e}. "
              f"Continuando em CPUExecutionProvider.", file=sys.stderr, flush=True)
        sessao = ort.InferenceSession(caminho, providers=["CPUExecutionProvider"])

    efetivo = sessao.get_providers()[0]

    if preferir_gpu and efetivo != "CUDAExecutionProvider":
        # não é erro — a máquina pode não ter NVIDIA. Mas é caro e tem de
        # aparecer: a diarização em CPU roda a ~1,28x o tempo real (V1,
        # docs/DIARIZACAO-ONNX.md §5.1), contra 11,80x no CUDA EP (V5).
        print(f"[diarizacao] CUDA indisponível, rodando em {efetivo}. "
              f"Disponíveis: {disponiveis}", file=sys.stderr, flush=True)

    return sessao, efetivo
