#!/usr/bin/env bash
# Monta a pasta `motores/` que o app espera ao lado do executável.
#
# Roda no Linux/WSL e produz um ambiente Windows: o `uv` baixa wheels
# `win_amd64` a partir daqui, então não é preciso um Windows para empacotar.
#
# O resultado é:
#
#   motores/python/python.exe        Python embeddable, sem instalação
#   motores/python/Lib/site-packages faster-whisper, onnxruntime-gpu, CUDA
#   motores/asr/motor.py             o sidecar de transcrição
#   motores/diarizacao/motor.py      o sidecar de diarização
#   motores/modelos/motor.py         o sidecar que baixa modelo sob controle
#
# Um Python só para os dois motores, e não um por motor: o app aponta para um
# `python.exe` (ver Nucleo/Transcritor.cs), e separar os ambientes só valeria
# para poupar disco — o que importa em memória já está resolvido, porque cada
# motor roda no próprio processo e a VRAM do primeiro volta antes de o segundo
# subir.
#
# Uso:
#   tools/empacotar_motores.sh [destino]

set -euo pipefail

RAIZ="$(cd "$(dirname "$0")/.." && pwd)"
DESTINO="${1:-$RAIZ/dist/motores}"
PYTHON_VERSAO="3.12.8"
PLATAFORMA="x86_64-pc-windows-msvc"
# Cravada, e não "a mais nova": é a versão em que a legenda foi medido, e o
# `transcribe.cpp` ainda está em 0.x — onde a compatibilidade não é promessa.
TRANSCRIBE_CPP_VERSAO="0.2.3"

# **1.23.2 e não "a mais nova", e isto não é conservadorismo.** O que o
# `uv`/`pip` resolvem por padrão hoje é a 1.30, que é um build de **CUDA 13**:
# ela não carrega contra as nossas DLLs de CUDA 12 (`cublasLt64_13.dll` /
# `libcublasLt.so.13` ausentes) e **cai para CPU em silêncio** — foi assim que
# uma medição de 18/09/2026 devolveu 32,11x o tempo real e quase entrou no
# registro como resultado de GPU (ver o cabeçalho de
# motores/diarizacao/pipeline/sessao.py). A 1.23.2 é build de CUDA 12.2 e é a
# versão em que o porte inteiro foi medido: acordo 1,0000 na diarização e
# cosseno mínimo 0,9999999998 nos vetores de voz.
ONNXRUNTIME_VERSAO="1.23.2"

# As DLLs de CUDA que o torch trazia e agora vêm de wheels próprios. As versões
# são cravadas pela mesma razão que a do onnxruntime: um build novo pode trocar
# de major de CUDA sem avisar, e a falha é muda.
#
# **São estas quatro e não mais**, e não é chute: o
# `onnxruntime_providers_cuda.dll` da 1.23.2 importa exatamente
# `cublas64_12`, `cublasLt64_12`, `cudart64_12`, `cudnn64_9` e `cufft64_11`
# (mais o `nvcuda.dll` do driver, que é da máquina), e o `ctranslate2.dll`
# importa `cublas64_12` mais o `cudnn64_9` que ele já embarca — e esse, por sua
# vez, procura as sublibs `cudnn_*64_9.dll` no PATH. O `nvidia-curand-cu12` e o
# `nvidia-cuda-nvrtc-cu12`, que o extra `[cuda]` do onnxruntime traria, não
# aparecem em nenhum dos dois: ficam de fora, e são ~180 MB.
CUBLAS_VERSAO="12.9.2.10"
CUDART_VERSAO="12.9.79"
CUDNN_VERSAO="9.8.0.87"
CUFFT_VERSAO="11.4.1.4"

echo "empacotando em $DESTINO"
rm -rf "$DESTINO"
mkdir -p "$DESTINO/python"

echo "==> Python embeddable $PYTHON_VERSAO"
curl -sL -o /tmp/py-embed.zip \
  "https://www.python.org/ftp/python/$PYTHON_VERSAO/python-$PYTHON_VERSAO-embed-amd64.zip"
unzip -q /tmp/py-embed.zip -d "$DESTINO/python"
rm /tmp/py-embed.zip

# O embeddable vem com `import site` comentado: ele é feito para ser embutido
# num app que gerencia os caminhos. Sem descomentar, nenhum pacote instalado é
# encontrado — e o erro não diz isso, só reclama de módulo ausente.
cat > "$DESTINO/python/python312._pth" <<'PTH'
python312.zip
.
Lib\site-packages

import site
PTH
mkdir -p "$DESTINO/python/Lib/site-packages"

# **O `pyannote.audio` não está aqui, e é a mudança inteira de 22/09/2026.**
# Era ele que arrastava torch, torchaudio, pytorch_lightning e torchmetrics —
# 3,6 GB só de `torch/` —, e o app não usava nada disso desde que a diarização
# e o vetor de voz passaram a rodar em ONNX Runtime + numpy
# (docs/DIARIZACAO-ONNX.md). O que `motores/diarizacao/` de fato importa são as
# peças abaixo, levantadas import a import: `pyannote.core` (Annotation,
# SlidingWindow) e `pyannote.pipeline` (o `Pipeline` que o clustering herda) —
# nenhuma das duas tem torch —, mais numpy, scipy, scikit-learn e einops.
#
# O `pyannote.metrics` fica de fora de propósito: ele aparece só dentro de
# `pipeline/vendor/diarizacao_utils.py:optimal_mapping`, atrás de um import
# adiado, e nenhum caminho deste app chama aquele método.
echo "==> pacotes (wheels de Windows, baixados daqui)"
uv pip install \
  --target "$DESTINO/python/Lib/site-packages" \
  --python-platform "$PLATAFORMA" \
  --python-version 3.12 \
  --only-binary=:all: \
  faster-whisper \
  "pyannote.core" "pyannote.pipeline" einops scipy scikit-learn

# **Ele vem ANTES da etapa do CUDA, e isso não é arrumação.** O
# `uv pip install` num `--target` re-resolve o ambiente inteiro, então qualquer
# etapa posterior pode desfazer o que a anterior cravou. Até 22/09/2026 quem
# vinha por último era o torch, pela mesma razão; hoje é o onnxruntime-gpu.
#
# **São duas instalações, e a ordem importa.** O `transcribe-cpp` declara
# `Requires-Dist: transcribe-cpp-native==0.2.3.*` — o backend de CPU —, e é ele
# que vem do PyPI junto. O de CUDA é um pacote SEPARADO (`-native-cu12`),
# declarado como `extra`, e é esse que **não** pode vir do PyPI: lá ele é um stub
# de versão 0.0.0 que não registra o backend, e a falha é muda — o modelo carrega,
# roda em CPU, e a reunião leva a tarde. É a mesma sequência do cabeçalho do
# tools/medir_legenda.py, que é onde o motor foi medido.
echo "==> transcribe.cpp para a legenda ao vivo (~200 MB)"
TCPP="https://github.com/handy-computer/transcribe.cpp/releases/download/v$TRANSCRIBE_CPP_VERSAO"

# 1. o pacote e o backend de CPU, do PyPI — resolvidos como dependência normal.
uv pip install \
  --target "$DESTINO/python/Lib/site-packages" \
  --python-platform "$PLATAFORMA" \
  --python-version 3.12 \
  --only-binary=:all: \
  "transcribe-cpp==$TRANSCRIBE_CPP_VERSAO"

# 2. o backend de CUDA, do release do GitHub, e win_amd64 e não manylinux: as
#    medições rodaram no WSL, o app é Windows, e o mesmo release publica os dois.
uv pip install \
  --target "$DESTINO/python/Lib/site-packages" \
  --python-platform "$PLATAFORMA" \
  --python-version 3.12 \
  --only-binary=:all: \
  "$TCPP/transcribe_cpp_native_cu12-$TRANSCRIBE_CPP_VERSAO-py3-none-win_amd64.whl"

# ── o CUDA, e ele é a ÚLTIMA palavra sobre o assunto ─────────────────────────
#
# **Esta etapa é a última de propósito, e isso não é arrumação.** O
# `uv pip install` num `--target` re-resolve o ambiente inteiro: qualquer etapa
# depois desta pode trocar o `onnxruntime-gpu` cravado pelo `onnxruntime` de
# CPU que o faster-whisper pede, ou subir o cravado para a 1.30 de CUDA 13. Foi
# exatamente assim que, em 04/09/2026, uma etapa nova no fim trocou o
# `torch 2.6.0+cu124` por um `2.14.0+cpu` e o instalador saiu 660 MB menor com
# a transcrição em CPU. A régua no fim confere o resultado.
#
# **`--no-deps` é o que torna esta etapa determinística.** Sem ele o uv
# re-resolveria tudo e reinstalaria o `onnxruntime` de CPU junto — os dois
# wheels escrevem na MESMA pasta `onnxruntime/`, e quem ganha é quem o uv
# copiar por último, que não é nossa escolha. Com ele, instalamos só o que está
# nomeado aqui; as dependências de import do onnxruntime (numpy, protobuf,
# flatbuffers, coloredlogs, sympy, packaging) já vieram da etapa anterior, pelo
# `onnxruntime` de CPU que o faster-whisper arrasta.
echo "==> onnxruntime-gpu $ONNXRUNTIME_VERSAO e as DLLs de CUDA (~2 GiB)"

# O de CPU sai inteiro antes, pasta e dist-info: sobrescrever arquivo a arquivo
# deixaria para trás os que só a versão de CPU tem, e o `onnxruntime` passaria a
# ser uma mistura das duas.
rm -rf "$DESTINO/python/Lib/site-packages/onnxruntime" \
       "$DESTINO/python/Lib/site-packages"/onnxruntime-*.dist-info

uv pip install \
  --target "$DESTINO/python/Lib/site-packages" \
  --python-platform "$PLATAFORMA" \
  --python-version 3.12 \
  --only-binary=:all: \
  --no-deps \
  "onnxruntime-gpu==$ONNXRUNTIME_VERSAO" \
  "nvidia-cublas-cu12==$CUBLAS_VERSAO" \
  "nvidia-cuda-runtime-cu12==$CUDART_VERSAO" \
  "nvidia-cudnn-cu12==$CUDNN_VERSAO" \
  "nvidia-cufft-cu12==$CUFFT_VERSAO"

echo "==> os sidecars"
# **A legenda estava na régua e não estava na cópia** desde 18/09/2026, quando
# o MOSS saiu e levou junto a linha que criava a pasta: o script reprovava a si
# mesmo no fim. Corrigido em 22/09/2026, na mesma varredura que tirou o torch.
mkdir -p "$DESTINO/asr" "$DESTINO/diarizacao" "$DESTINO/modelos" "$DESTINO/legenda"
cp "$RAIZ/motores/asr/motor.py" "$DESTINO/asr/"
cp "$RAIZ/motores/diarizacao/motor.py" "$DESTINO/diarizacao/"
# O motor de modelos não traz dependência nova: a huggingface_hub já vem
# junto do faster-whisper, que a usa para baixar por conta própria.
cp "$RAIZ/motores/modelos/motor.py" "$DESTINO/modelos/"
cp "$RAIZ/motores/legenda/motor.py" "$DESTINO/legenda/"

# A diarização é o único motor com pacote próprio: o `motor.py` importa de
# `pipeline/` (fbank, sessao, segmentacao, embedding, diarizacao, vendor/), e
# sem ele o motor não sobe — imports quebrados. Desde 22/09/2026 não há mais o
# ramo pyannote para cair, então isto deixou de ser opcional. Mesma cópia que o
# tools/publicar.sh faz, com as mesmas exclusões: `testes/` é gabarito de
# desenvolvimento e `__pycache__` é bytecode de outra máquina.
rsync -a --exclude='__pycache__' --exclude='testes' \
  "$RAIZ/motores/diarizacao/pipeline/" "$DESTINO/diarizacao/pipeline/"

# **O GGUF não vem aqui, e isso é decisão e não esquecimento.** São
# 0,70 GB, e o instalador exclui `*.gguf` por decisão registrada
# (instalador/MeetingApp.iss, e a régua do montar_instalador.sh reprova se um
# escapar): pôr o arquivo nesta pasta o faria ser descartado em silêncio, e o
# motor subiria sem modelo na máquina de quem instalou.
#
# Ele entra pelo caminho que os GGUF de ata já usam: é um pacote do
# `Nucleo/Catalogo.cs`, baixado sob demanda pelo sidecar `modelos`. Quem liga a
# chave `motor_de_transcricao` baixa 0,70 GB uma vez; quem não liga não paga
# nada. Ver docs/FASE7-BACKEND.md A1.

# ── as réguas ────────────────────────────────────────────────────────────────
#
# **A que faltava, e custou um instalador.** Este script sempre dependeu da
# ORDEM para o CUDA sair certo, e nada conferia o resultado — em 04/09/2026 uma
# etapa nova no fim re-resolveu o ambiente e trocou o `torch 2.6.0+cu124` por um
# `2.14.0+cpu`. Compilou, empacotou, e só o tamanho do instalador denunciou. Sem
# régua, teria chegado ao usuário como "a transcrição ficou lenta". O torch saiu
# em 22/09/2026, a dependência da ordem não — só mudou de pacote.
echo "==> conferindo as réguas"
reprovar() { echo "ERRO: $1" >&2; exit 1; }
SITE="$DESTINO/python/Lib/site-packages"

# **O torch não pode voltar, e ele volta sozinho.** Basta alguém acrescentar
# `pyannote.audio` — ou qualquer pacote que o peça — para os 3,6 GB e as duas
# horas de download voltarem sem ninguém decidir nada. E o app não usa mais uma
# linha dele: a diarização, o vetor de voz e o diagnóstico de placa do ASR são
# todos ONNX Runtime, numpy ou ctranslate2.
for proibido in torch torchaudio pyannote/audio; do
  [[ -e "$SITE/$proibido" ]] \
    && reprovar "o $proibido voltou ao empacotamento.
      São 3,6 GB que o app não usa desde o porte de 18-22/09/2026
      (docs/DIARIZACAO-ONNX.md). Algum pacote novo o arrastou de volta —
      provavelmente o pyannote.audio, que é de onde ele sempre veio."
done
echo "    torch: fora"

# **O build de CUDA, e não só "tem onnxruntime".** A 1.30, que é o que o
# resolvedor entrega sem pino, é CUDA 13: ela instala, importa, e o CUDA EP não
# carrega — a sessão cai para CPU em silêncio.
ORT_V=$(grep -oP "^__version__ = '\K[^']+" "$SITE/onnxruntime/capi/build_and_package_info.py" 2>/dev/null || true)
ORT_PKG=$(grep -oP "^package_name = '\K[^']+" "$SITE/onnxruntime/capi/build_and_package_info.py" 2>/dev/null || true)
ORT_CUDA=$(grep -oP "^cuda_version = '\K[^']+" "$SITE/onnxruntime/capi/build_and_package_info.py" 2>/dev/null || true)
[[ "$ORT_PKG" == "onnxruntime-gpu" && "$ORT_CUDA" == 12.* ]] \
  || reprovar "o onnxruntime empacotado é '${ORT_PKG:-nenhum} ${ORT_V:-?}' com CUDA '${ORT_CUDA:-nenhum}'.
      Precisa ser o onnxruntime-gpu de CUDA 12. Com o de CPU, ou com um de
      CUDA 13, a diarização e o vetor de voz rodam em CPU sem dizer nada:
      ~1,28x o tempo real contra 7,91x, e nenhuma mensagem de erro."
echo "    onnxruntime: $ORT_PKG $ORT_V (CUDA $ORT_CUDA)"

# As DLLs que o CUDA EP e o ctranslate2 carregam pelo nome. Cada uma que falta
# é uma queda para CPU em silêncio — ou, no caso das sublibs do cuDNN, um
# "Cannot load symbol cudnnCreate" no meio da reunião.
for dll in cublas64_12 cublasLt64_12 cudart64_12 cufft64_11 \
           cudnn64_9 cudnn_graph64_9 cudnn_cnn64_9 cudnn_ops64_9 \
           cudnn_adv64_9 cudnn_heuristic64_9; do
  compgen -G "$SITE/nvidia/*/bin/$dll.dll" >/dev/null \
    || compgen -G "$SITE/ctranslate2/$dll.dll" >/dev/null \
    || reprovar "falta a DLL $dll.dll no empacotamento.
      Ela vinha de torch/lib até 22/09/2026 e agora vem dos wheels nvidia-*.
      Sem ela o provedor CUDA não carrega, e a queda para CPU é muda."
done
echo "    DLLs de CUDA: as dez no lugar"

# O backend de CUDA é um pacote separado, e é o que o PyPI entrega como
# stub 0.0.0. Se ele sumir, a legenda carrega e roda em CPU — mesma falha muda.
[[ -d "$DESTINO/python/Lib/site-packages/transcribe_cpp_native_cu12" ]] \
  || reprovar "falta o transcribe_cpp_native_cu12 — a legenda rodaria em CPU."
echo "    transcribe.cpp: com backend de CUDA"

for m in asr diarizacao modelos legenda; do
  [[ -f "$DESTINO/$m/motor.py" ]] || reprovar "falta $m/motor.py no empacotamento."
done
echo "    os quatro sidecars: no lugar"

# O torchcodec vinha do pyannote.audio, e as DLLs dele nunca casaram com o
# torch do índice do PyTorch: em 09/09/2026 o carregador do Windows abriu uma
# caixa "Entry Point Not Found" na tela do dono do produto, que trava o
# python.exe até alguém clicar. A régua do torch acima já o impede de voltar —
# ele não tem outro caminho de entrada —, e esta o nomeia por ser o sintoma que
# a pessoa vê.
compgen -G "$SITE/torchcodec*" >/dev/null \
  && reprovar "o torchcodec voltou ao empacotamento.
      Ele não é usado (o motor lê o WAV em numpy) e as DLLs dele abrem uma
      caixa 'Entry Point Not Found' que trava o python.exe até alguém clicar."
echo "    torchcodec: fora"

echo
du -sh "$DESTINO"
echo "pronto. Copie para junto do MeetingApp.exe."
