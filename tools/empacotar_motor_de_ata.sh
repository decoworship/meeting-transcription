#!/usr/bin/env bash
#
# Monta motores/ata/ ao lado do app: o llama.cpp e o GGUF que escreve as atas.
#
# Fica à parte do publicar.sh pelo mesmo motivo do empacotar_motores.sh: são
# 3 GB que não mudam a cada build, e baixá-los junto de cada publicação
# transformaria um ciclo de 40 segundos num de vinte minutos.
#
# Uso:
#   tools/empacotar_motor_de_ata.sh
#   tools/empacotar_motor_de_ata.sh --destino /mnt/c/Users/andre/MeetingApp
#
# **O build de CUDA tem que casar com o driver.** O 13.3 falha na máquina do
# usuário com "the provided PTX was compiled with an unsupported toolchain"
# (driver 595.97, que anuncia CUDA 13.2); o 12.4 funciona e é compatível para
# trás. Medido em 14/08/2026 — ver docs/ATA.md §8.

set -euo pipefail

DESTINO="/mnt/c/Users/andre/MeetingApp"
VERSAO_LLAMA="b10427"
CUDA="12.4"
MODELO_REPO="unsloth/Qwen3-4B-Instruct-2507-GGUF"
MODELO_ARQUIVO="Qwen3-4B-Instruct-2507-Q4_K_M.gguf"
MODELO_LOCAL="qwen3-4b-instruct-q4km.gguf"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --destino) DESTINO="$2"; shift 2 ;;
    --versao)  VERSAO_LLAMA="$2"; shift 2 ;;
    --cuda)    CUDA="$2"; shift 2 ;;
    *) echo "argumento desconhecido: $1" >&2; exit 2 ;;
  esac
done

RAIZ="$DESTINO/motores/ata"
mkdir -p "$RAIZ/bin" "$RAIZ/modelos"

# ---- llama.cpp
if [[ -f "$RAIZ/bin/llama-server.exe" ]]; then
  echo "==> llama.cpp já está em $RAIZ/bin"
else
  base="https://github.com/ggml-org/llama.cpp/releases/download/$VERSAO_LLAMA"
  tmp=$(mktemp -d)
  trap 'rm -rf "$tmp"' EXIT

  echo "==> baixando llama.cpp $VERSAO_LLAMA (CUDA $CUDA)"
  curl -sL -o "$tmp/llama.zip" "$base/llama-$VERSAO_LLAMA-bin-win-cuda-$CUDA-x64.zip"
  curl -sL -o "$tmp/cudart.zip" "$base/cudart-llama-bin-win-cuda-$CUDA-x64.zip"

  unzip -q -o "$tmp/llama.zip" -d "$RAIZ/bin"
  unzip -q -o "$tmp/cudart.zip" -d "$RAIZ/bin"

  # O resto do pacote é ferramenta de linha de comando que o app não usa, e são
  # dezenas de MB no instalador da Fase 4.
  find "$RAIZ/bin" -maxdepth 1 -name "llama-*.exe" \
       ! -name "llama-server.exe" -delete
  rm -f "$RAIZ/bin/ggml-rpc-server.exe" "$RAIZ/bin/rpc-server.exe" 2>/dev/null || true
fi

# ---- o modelo
if [[ -f "$RAIZ/modelos/$MODELO_LOCAL" ]]; then
  echo "==> modelo já está em $RAIZ/modelos"
else
  # Se já foi baixado para a pasta de teste, aproveita: são 2,5 GB, e baixar de
  # novo o mesmo arquivo é desperdício de banda e de paciência.
  cache="/mnt/c/Users/andre/ata-teste/qwen3-4b-q4km.gguf"
  if [[ -f "$cache" ]]; then
    echo "==> movendo o modelo de $cache"
    mv "$cache" "$RAIZ/modelos/$MODELO_LOCAL"
  else
    echo "==> baixando $MODELO_ARQUIVO (2,5 GB)"
    curl -sL -o "$RAIZ/modelos/$MODELO_LOCAL" \
      "https://huggingface.co/$MODELO_REPO/resolve/main/$MODELO_ARQUIVO"
  fi
fi

# ---- as réguas: sem elas o app publica e a ata só falha na hora de gerar
servidor="$RAIZ/bin/llama-server.exe"
modelo="$RAIZ/modelos/$MODELO_LOCAL"

[[ -f "$servidor" ]] || { echo "ERRO: falta $servidor" >&2; exit 1; }
[[ -f "$modelo" ]] || { echo "ERRO: falta $modelo" >&2; exit 1; }

tamanho=$(stat -c%s "$modelo")
if (( tamanho < 2000000000 )); then
  echo "ERRO: o modelo tem $((tamanho/1000000)) MB — download incompleto." >&2
  exit 1
fi

# A DLL do CUDA é o que faz a diferença entre gerar em 1 minuto e gerar em 20:
# sem ela o llama.cpp cai para CPU e ninguém avisa.
[[ -f "$RAIZ/bin/ggml-cuda.dll" ]] || {
  echo "ERRO: falta ggml-cuda.dll — sem ela a ata roda em CPU, e ninguém avisa." >&2
  exit 1
}

echo "==> pronto"
du -sh "$RAIZ" | sed 's/^/    /'
echo "    servidor: $(basename "$servidor")"
echo "    modelo:   $MODELO_LOCAL ($((tamanho/1000000)) MB)"
