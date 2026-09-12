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
#   motores/moss/motor.py            o sidecar do MOSS (opcional, Fase 7)
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
# Cravada, e não "a mais nova": é a versão em que o MOSS foi medido, e o
# `transcribe.cpp` ainda está em 0.x — onde a compatibilidade não é promessa.
TRANSCRIBE_CPP_VERSAO="0.2.3"

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
#
# **Ele vem ANTES do torch, e isso não é arrumação.** O `uv pip install` num
# `--target` re-resolve o ambiente inteiro: rodá-lo depois da etapa do torch faz
# o torch **cu124 ser substituído pelo do PyPI, que é CPU-only** — medido em
# 04/09/2026, quando esta etapa nasceu depois e o instalador saiu 660 MB menor
# com `torch 2.14.0+cpu` no lugar do `2.6.0+cu124`. A etapa do torch é a última
# a falar sobre o torch, e é isso que a mantém correta. A régua no fim confere.
#
# **São duas instalações, e a ordem importa.** O `transcribe-cpp` declara
# `Requires-Dist: transcribe-cpp-native==0.2.3.*` — o backend de CPU —, e é ele
# que vem do PyPI junto. O de CUDA é um pacote SEPARADO (`-native-cu12`),
# declarado como `extra`, e é esse que **não** pode vir do PyPI: lá ele é um stub
# de versão 0.0.0 que não registra o backend, e a falha é muda — o modelo carrega,
# roda em CPU, e a reunião leva a tarde. É a mesma sequência do cabeçalho do
# tools/medir_moss.py, que é onde o motor foi medido.
echo "==> transcribe.cpp para o motor MOSS (~200 MB)"
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

echo "==> torch com CUDA (~2,4 GiB de download)"
#
# **O `--reinstall-package` não é zelo: sem ele esta etapa não faz nada.**
# O passo anterior já instalou um `torch` — o pyannote o traz do PyPI, e o do
# PyPI para Windows é CPU-only. Com o requisito `torch` sem versão, o uv o
# considera satisfeito e responde "Checked 2 packages in 45ms" sem baixar coisa
# alguma: o índice do PyTorch nunca é consultado, e o ambiente sai com
# `torch+cpu`. Medido em 04/09/2026, com uv 0.11.3 — a instalação que está em
# produção tem `2.6.0+cu124` e foi montada quando o uv ainda substituía.
#
# O sintoma é mudo por dois caminhos: a transcrição cai para CPU (~40x mais
# lenta no large-v3) e o ctranslate2 perde as DLLs de CUDA que ele também usa.
# A régua no fim deste script existe por causa disto.
uv pip install \
  --target "$DESTINO/python/Lib/site-packages" \
  --python-platform "$PLATAFORMA" \
  --python-version 3.12 \
  --only-binary=:all: \
  --reinstall-package torch \
  --reinstall-package torchaudio \
  --index-url https://download.pytorch.org/whl/cu124 \
  torch torchaudio

# ~780 MB de coisas que só servem para compilar C++ contra o torch: os .lib são
# import libraries do MSVC e os headers idem. Nada disso é usado em runtime por
# um app que só chama Python. As DLLs ficam todas.
# **O torchcodec sai, e não é economia de disco.**
#
# Ele vem como dependência do pyannote.audio e **nunca foi usado por este app**:
# o motores/diarizacao/motor.py lê o WAV ele mesmo e entrega
# `{"waveform", "sample_rate"}` ao pipeline — nunca um caminho de arquivo —, que
# é exatamente o caminho que dispensa o decodificador. Ele nunca funcionou aqui,
# nas duas instalações que existiram.
#
# **O que ele causa é pior que não existir.** As DLLs dele são compiladas contra
# uma versão exata do torch, e o nosso vem do índice do PyTorch, então as duas
# não casam. Em 09/09/2026 isso apareceu na tela do dono do produto como uma
# **caixa modal do Windows** — "Entry Point Not Found: torch_get_const_data_ptr"
# — que trava o python.exe até alguém clicar em OK. O `try/except` do pyannote
# captura o erro, mas só DEPOIS do clique: quem mostra a caixa é o carregador do
# Windows, antes de o Python ver qualquer coisa.
#
# Um sidecar que abre diálogo é um sidecar que pendura a transcrição.
echo "==> tirando o torchcodec (não é usado, e as DLLs não casam com o torch)"
rm -rf "$DESTINO/python/Lib/site-packages/torchcodec" \
       "$DESTINO/python/Lib/site-packages"/torchcodec-*.dist-info

echo "==> tirando o que é de build"
find "$DESTINO/python/Lib/site-packages/torch/lib" -name "*.lib" -delete
rm -rf "$DESTINO/python/Lib/site-packages/torch/include" \
       "$DESTINO/python/Lib/site-packages/torch/test"

# O motor opcional da Fase 7: texto e falante numa passada só. ~200 MB de
# nativo, e é a única dependência nova do empacotamento desde a Fase 4.
#
# **O wheel nativo vem do release do GitHub, e não do PyPI.** O do PyPI instala
# um stub de versão 0.0.0 que **não registra o backend de CUDA** — e a falha é
# muda: o modelo carrega, roda em CPU, e a reunião leva a tarde. Medido em
# 02/09/2026 (docs/FASE7-RESULTADOS.md §7.1).
#
# **E é o win_amd64**, não o manylinux que as ferramentas de medição usam: as
# medições rodaram no WSL, o app é Windows, e o mesmo release publica os dois.
echo "==> os sidecars"
mkdir -p "$DESTINO/asr" "$DESTINO/diarizacao" "$DESTINO/modelos" "$DESTINO/moss"
cp "$RAIZ/motores/asr/motor.py" "$DESTINO/asr/"
cp "$RAIZ/motores/diarizacao/motor.py" "$DESTINO/diarizacao/"
# O motor de modelos não traz dependência nova: a huggingface_hub já vem
# junto do faster-whisper e do pyannote, que a baixam por conta própria.
cp "$RAIZ/motores/modelos/motor.py" "$DESTINO/modelos/"

# **O GGUF do MOSS não vem aqui, e isso é decisão e não esquecimento.** São
# 0,70 GB, e o instalador exclui `*.gguf` por decisão registrada
# (instalador/MeetingApp.iss, e a régua do montar_instalador.sh reprova se um
# escapar): pôr o arquivo nesta pasta o faria ser descartado em silêncio, e o
# motor subiria sem modelo na máquina de quem instalou.
#
# Ele entra pelo caminho que os GGUF de ata já usam: é um pacote do
# `Nucleo/Catalogo.cs`, baixado sob demanda pelo sidecar `modelos`. Quem liga a
# chave `motor_de_transcricao` baixa 0,70 GB uma vez; quem não liga não paga
# nada. Ver docs/FASE7-BACKEND.md A1.
cp "$RAIZ/motores/moss/motor.py" "$DESTINO/moss/"

# ── as réguas ────────────────────────────────────────────────────────────────
#
# **A que faltava, e custou um instalador.** Este script sempre dependeu da
# ORDEM para o torch sair com CUDA: instala o do PyPI junto do pyannote e depois
# o do índice do PyTorch por cima. Nada conferia o resultado — e quando uma
# etapa nova entrou no fim, em 04/09/2026, ela re-resolveu o ambiente e trocou o
# `2.6.0+cu124` por um `2.14.0+cpu`. Compilou, empacotou, e só o tamanho do
# instalador denunciou. Sem a régua de tamanho, teria chegado ao usuário como
# "a transcrição ficou lenta".
echo "==> conferindo as réguas"
reprovar() { echo "ERRO: $1" >&2; exit 1; }

TORCH_V=$(grep -oP "__version__ = '\K[^']+" \
  "$DESTINO/python/Lib/site-packages/torch/version.py" 2>/dev/null || true)
[[ "$TORCH_V" == *"+cu"* ]] \
  || reprovar "o torch empacotado é '${TORCH_V:-nenhum}' — precisa ser um build +cuXXX.
      Sem CUDA a transcrição roda em CPU: ~40x mais lenta no large-v3, e o app
      não tem como perceber. Alguma etapa depois da do torch re-resolveu o
      ambiente; ela tem de vir ANTES."
echo "    torch: $TORCH_V"

# O backend de CUDA do MOSS é um pacote separado, e é o que o PyPI entrega como
# stub 0.0.0. Se ele sumir, o MOSS carrega e roda em CPU — mesma falha muda.
[[ -d "$DESTINO/python/Lib/site-packages/transcribe_cpp_native_cu12" ]] \
  || reprovar "falta o transcribe_cpp_native_cu12 — o motor MOSS rodaria em CPU."
echo "    transcribe.cpp: com backend de CUDA"

for m in asr diarizacao modelos moss; do
  [[ -f "$DESTINO/$m/motor.py" ]] || reprovar "falta $m/motor.py no empacotamento."
done
echo "    os quatro sidecars: no lugar"

# Ele volta sozinho se alguém acrescentar um pacote que o puxe, e o sintoma é
# uma caixa modal na máquina de quem instalou — longe daqui.
compgen -G "$DESTINO/python/Lib/site-packages/torchcodec*" >/dev/null \
  && reprovar "o torchcodec voltou ao empacotamento.
      Ele não é usado (o motor entrega waveform em memória) e as DLLs dele não
      casam com o torch do índice do PyTorch: o carregador do Windows abre uma
      caixa 'Entry Point Not Found' que trava o python.exe até alguém clicar."
echo "    torchcodec: fora"

echo
du -sh "$DESTINO"
echo "pronto. Copie para junto do MeetingApp.exe."
