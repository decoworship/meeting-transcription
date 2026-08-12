#!/usr/bin/env bash
# Monta a pasta `motores/` que o app espera ao lado do executável.
#
# Roda no Linux/WSL e produz um ambiente Windows: o `uv` baixa wheels
# `win_amd64` a partir daqui, então não é preciso um Windows para empacotar.
#
# O resultado é:
#
#   motores/python/python.exe        Python embeddable, sem instalação
#   motores/python/Lib/site-packages faster-whisper, pyannote e dependências
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

echo "==> pacotes (wheels de Windows, baixados daqui)"
uv pip install \
  --target "$DESTINO/python/Lib/site-packages" \
  --python-platform "$PLATAFORMA" \
  --python-version 3.12 \
  --only-binary=:all: \
  faster-whisper "pyannote.audio>=4.0"

# O torch do PyPI para Windows é CPU-only (torch+cpu, cuda: False). Medido com
# o modelo `tiny`: 12,9x tempo real em CPU — e o `large-v3`, que é o de
# produção, é ~40x maior. Para a GPU servir, o torch tem que vir do índice do
# PyTorch, e é ele que traz as DLLs de CUDA (cublas, cudnn) que o ctranslate2
# do faster-whisper também usa.
echo "==> torch com CUDA (~2,4 GiB de download)"
uv pip install \
  --target "$DESTINO/python/Lib/site-packages" \
  --python-platform "$PLATAFORMA" \
  --python-version 3.12 \
  --only-binary=:all: \
  --index-url https://download.pytorch.org/whl/cu124 \
  torch torchaudio

# ~780 MB de coisas que só servem para compilar C++ contra o torch: os .lib são
# import libraries do MSVC e os headers idem. Nada disso é usado em runtime por
# um app que só chama Python. As DLLs ficam todas.
echo "==> tirando o que é de build"
find "$DESTINO/python/Lib/site-packages/torch/lib" -name "*.lib" -delete
rm -rf "$DESTINO/python/Lib/site-packages/torch/include" \
       "$DESTINO/python/Lib/site-packages/torch/test"

echo "==> os sidecars"
mkdir -p "$DESTINO/asr" "$DESTINO/diarizacao" "$DESTINO/modelos"
cp "$RAIZ/motores/asr/motor.py" "$DESTINO/asr/"
cp "$RAIZ/motores/diarizacao/motor.py" "$DESTINO/diarizacao/"
# O motor de modelos não traz dependência nova: a huggingface_hub já vem
# junto do faster-whisper e do pyannote, que a baixam por conta própria.
cp "$RAIZ/motores/modelos/motor.py" "$DESTINO/modelos/"

echo
du -sh "$DESTINO"
echo "pronto. Copie para junto do MeetingApp.exe."
