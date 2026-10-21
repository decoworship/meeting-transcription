#!/usr/bin/env bash
#
# Monta motores/ocr/modelos/ — os pesos do leitor de nomes (UI-12), ao lado do
# sidecar.
#
# ── Por que eles viajam dentro do app ───────────────────────────────────────
#
# Sem o caminho explícito, o RapidOCR baixa os modelos na primeira chamada, de
# um servidor na China (modelscope.cn), sem progresso e sem funcionar offline.
# O motor (motores/ocr/motor.py) só abre o que está nesta pasta, e é este
# script que a enche — o mesmo arranjo dos pesos de diarização (FASE4.md §4).
#
# PP-OCRv6 small, detecção e reconhecimento multilíngue: 31 MB, medidos em
# 21/10/2026 contra o PP-OCRv5 latino nos prints reais do dono (34 de 34 nomes
# do Teams contra 33). Ver o docstring do motor.
#
# ── ATRIBUIÇÃO ──────────────────────────────────────────────────────────────
#
# Os modelos são do PaddleOCR (PaddlePaddle), Apache-2.0, convertidos para ONNX
# pelo RapidOCR (RapidAI), também Apache-2.0. O ATRIBUICAO.md viaja com eles.
#
# Uso:
#   tools/empacotar_modelos_de_ocr.sh                     # na instalação oficial
#   tools/empacotar_modelos_de_ocr.sh --destino motores   # na árvore, para desenvolver

set -euo pipefail

DESTINO="/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --destino) DESTINO="$2"; shift 2 ;;
    *) echo "argumento desconhecido: $1" >&2; exit 2 ;;
  esac
done

# A versão do RapidOCR cujos modelos se usa. Os hashes são os do
# default_models.yaml dela, e a URL é a mesma que ela usaria: trocar a versão
# é trocar os três juntos.
RAPIDOCR_VERSAO="3.10.0"
BASE="https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v$RAPIDOCR_VERSAO/onnx/PP-OCRv6"

ALVO="$DESTINO/ocr/modelos"
mkdir -p "$ALVO"

baixar() {
  local caminho="$1" sha="$2"
  local arquivo
  arquivo="$(basename "$caminho")"
  if [[ -f "$ALVO/$arquivo" ]] && echo "$sha  $ALVO/$arquivo" | sha256sum -c --status; then
    echo "    $arquivo (já estava)"
    return
  fi
  echo "==> $arquivo"
  curl -fsSL --retry 3 -o "$ALVO/$arquivo.parcial" "$BASE/$caminho"
  # Conferido antes de ocupar o nome final: um download cortado com o nome
  # certo faria o motor falhar com um erro do onnxruntime que não diz "baixe
  # de novo".
  echo "$sha  $ALVO/$arquivo.parcial" | sha256sum -c --status \
    || { rm -f "$ALVO/$arquivo.parcial"; echo "ERRO: $arquivo veio diferente do esperado" >&2; exit 1; }
  mv "$ALVO/$arquivo.parcial" "$ALVO/$arquivo"
}

baixar det/PP-OCRv6_det_small.onnx 090f04abcd9d9a7498bc4ebf677e4cb9bdce1fe4197ddb7e529f1ef44e1ff94f
baixar rec/PP-OCRv6_rec_small.onnx 6f327246b50388f3c176ae304bd95767ea6dc0c9ae92153ef8cbe210b3c14884

cat > "$ALVO/ATRIBUICAO.md" <<'EOF'
# Modelos de OCR

`PP-OCRv6_det_small.onnx` e `PP-OCRv6_rec_small.onnx` são os modelos PP-OCRv6
do PaddleOCR (https://github.com/PaddlePaddle/PaddleOCR), © PaddlePaddle
Authors, sob a Apache License 2.0, na conversão para ONNX publicada pelo
RapidOCR (https://github.com/RapidAI/RapidOCR), © RapidAI, também Apache-2.0.

Licença: https://www.apache.org/licenses/LICENSE-2.0
EOF

echo "==> pronto em $ALVO"
