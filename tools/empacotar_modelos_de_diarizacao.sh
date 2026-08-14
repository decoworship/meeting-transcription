#!/usr/bin/env bash
#
# Monta motores/diarizacao/modelos/ — os pesos do pyannote, ao lado do sidecar.
#
# ── Por que eles passaram a viajar dentro do app (Fase 4) ───────────────────
#
# Até a Fase 3 o pyannote baixava tudo do HuggingFace na primeira execução, e
# para isso o binário publicado carregava um token de leitura embutido. Isso
# funciona numa máquina só; deixa de funcionar quando o app é entregue a outra
# pessoa, porque o token vai junto e `strings` o encontra.
#
# A medição de 14/08/2026 mostrou que o problema é pequeno:
#
#   speaker-diarization-community-1   32 MB   CC-BY-4.0   com portão
#   wespeaker-voxceleb-resnet34-LM    26 MB   CC-BY-4.0   sem portão
#
# 58 MB, os dois sob uma licença que permite redistribuir com atribuição. Então
# eles entram no instalador, o motor os carrega por caminho local, e o token
# some do binário. Ver docs/FASE4.md §4.
#
# Três ganhos, e o terceiro não é sobre segredo nenhum:
#   1. nada secreto viaja no binário entregue;
#   2. se o HuggingFace mudar as condições de acesso do community-1, as
#      instalações já entregues não param;
#   3. **a diarização deixa de precisar de rede na primeira execução.**
#
# ── ATRIBUIÇÃO ──────────────────────────────────────────────────────────────
#
# CC-BY-4.0 exige crédito. Ele é escrito em ATRIBUICAO.md junto dos pesos, e o
# arquivo viaja com eles — é o que torna a redistribuição regular.
#
# Uso:
#   tools/empacotar_modelos_de_diarizacao.sh
#   tools/empacotar_modelos_de_diarizacao.sh --destino dist/payload

set -euo pipefail

RAIZ="$(cd "$(dirname "$0")/.." && pwd)"
DESTINO="/mnt/c/Users/andre/MeetingApp"
TOKEN_ARQ="/mnt/c/Users/andre/.meeting-recorder/hf_token.txt"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --destino) DESTINO="$2"; shift 2 ;;
    *) echo "argumento desconhecido: $1" >&2; exit 2 ;;
  esac
done

ALVO="$DESTINO/motores/diarizacao/modelos"
PIPELINE="$ALVO/community-1"
VOZ="$ALVO/wespeaker-voxceleb-resnet34-LM"

# Os nomes das pastas são os que o motor procura, e não os do repositório: quem
# lê é motores/diarizacao/motor.py, e mudar um sem o outro faz o motor cair no
# caminho do HuggingFace sem avisar. É por isso que a régua do fim confere os
# caminhos exatos.

mkdir -p "$PIPELINE/segmentation" "$PIPELINE/embedding" "$PIPELINE/plda" "$VOZ"

# O cache do HuggingFace desta máquina, que já tem os dois. Copiar de lá é
# instantâneo e não depende de rede nem de portão.
CACHE="/mnt/c/Users/andre/.cache/huggingface/hub"

# Copia do cache se estiver lá; senão baixa. O caminho de download existe para a
# máquina que ainda não rodou uma diarização — e ele ainda precisa do token,
# porque o community-1 tem portão. Quem EMPACOTA precisa de token; quem RECEBE o
# instalador, não. É a diferença que esta fase estabelece.
pegar() {
  local repo="$1" arquivo="$2" saida="$3"

  if [[ -f "$saida" ]]; then
    echo "    já está: ${saida#$ALVO/}"
    return
  fi

  local doCache
  doCache=$(find "$CACHE/models--${repo//\//--}/snapshots" -path "*/$arquivo" \
            -type f 2>/dev/null | head -1 || true)
  # -type f de propósito: o snapshot do HF é feito de links para blobs/, e um
  # link resolvido copia o conteúdo — que é o que se quer. O que não se quer é
  # copiar o link como link para dentro do instalador.
  if [[ -n "$doCache" ]]; then
    cp -L "$doCache" "$saida"
    echo "    do cache: ${saida#$ALVO/}"
    return
  fi

  local cabecalho=()
  if [[ -f "$TOKEN_ARQ" ]]; then
    cabecalho=(-H "Authorization: Bearer $(tr -d '\r\n' < "$TOKEN_ARQ")")
  fi

  echo "    baixando: $repo/$arquivo"
  # --fail para o curl não gravar a página de erro do portão como se fosse peso:
  # sem ele, um 403 vira um "modelo" de 2 KB e o defeito só aparece na primeira
  # reunião de quem instalou.
  curl -sSL --fail "${cabecalho[@]}" -o "$saida" \
    "https://huggingface.co/$repo/resolve/main/$arquivo" || {
      echo "ERRO: não consegui baixar $repo/$arquivo." >&2
      [[ -f "$TOKEN_ARQ" ]] || echo "      (não achei $TOKEN_ARQ — o community-1 tem portão)" >&2
      rm -f "$saida"
      exit 1
    }
}

echo "==> pipeline de diarização (community-1)"
pegar pyannote/speaker-diarization-community-1 config.yaml                 "$PIPELINE/config.yaml"
pegar pyannote/speaker-diarization-community-1 segmentation/pytorch_model.bin "$PIPELINE/segmentation/pytorch_model.bin"
pegar pyannote/speaker-diarization-community-1 embedding/pytorch_model.bin    "$PIPELINE/embedding/pytorch_model.bin"
pegar pyannote/speaker-diarization-community-1 plda/plda.npz                  "$PIPELINE/plda/plda.npz"
pegar pyannote/speaker-diarization-community-1 plda/xvec_transform.npz        "$PIPELINE/plda/xvec_transform.npz"

echo "==> modelo de voz (wespeaker)"
pegar pyannote/wespeaker-voxceleb-resnet34-LM pytorch_model.bin "$VOZ/pytorch_model.bin"

echo "==> atribuição"
cat > "$ALVO/ATRIBUICAO.md" <<'MD'
# Modelos de diarização

Estes pesos não são deste projeto. Eles são redistribuídos aqui, sem
modificação, sob os termos das respectivas licenças.

## pyannote/speaker-diarization-community-1

- autoria: Hervé Bredin e colaboradores (CNRS, pyannoteAI)
- origem: https://huggingface.co/pyannote/speaker-diarization-community-1
- licença: **CC-BY-4.0** (https://creativecommons.org/licenses/by/4.0/)

## pyannote/wespeaker-voxceleb-resnet34-LM

- autoria: pyannote, a partir do WeSpeaker (voxceleb resnet34-LM)
- origem: https://huggingface.co/pyannote/wespeaker-voxceleb-resnet34-LM
- licença: **CC-BY-4.0** (https://creativecommons.org/licenses/by/4.0/)

A biblioteca que os carrega, `pyannote.audio`, é MIT.

Se você citar este app num trabalho, cite também os artigos do pyannote —
eles estão nos README dos repositórios acima.
MD

echo "==> réguas"

# Cada uma corresponde a um jeito de o instalador sair inteiro e a diarização
# falhar na primeira reunião de quem recebeu.
for f in "$PIPELINE/config.yaml" \
         "$PIPELINE/segmentation/pytorch_model.bin" \
         "$PIPELINE/embedding/pytorch_model.bin" \
         "$PIPELINE/plda/plda.npz" \
         "$PIPELINE/plda/xvec_transform.npz" \
         "$VOZ/pytorch_model.bin"; do
  [[ -f "$f" ]] || { echo "ERRO: falta $f" >&2; exit 1; }
done

# Um HTML de erro do portão tem uns 2 KB e passaria por "arquivo presente". Os
# pesos têm dezenas de MB; o config.yaml tem centenas de bytes e é o único que
# pode ser pequeno.
for f in "$PIPELINE/segmentation/pytorch_model.bin" \
         "$PIPELINE/embedding/pytorch_model.bin" \
         "$VOZ/pytorch_model.bin"; do
  bytes=$(stat -c%s "$f")
  if (( bytes < 1000000 )); then
    echo "ERRO: $f tem $bytes bytes — é página de erro, não peso." >&2
    exit 1
  fi
done

# O config.yaml aponta os pesos por "$model/..." — caminho relativo à própria
# pasta. É isso que faz a carga local funcionar; um config que aponte para um
# repositório remoto reintroduziria a rede sem ninguém perceber.
grep -q '\$model/segmentation' "$PIPELINE/config.yaml" || {
  echo "ERRO: o config.yaml não usa \$model/ — a carga local não vai resolver" >&2
  echo "      os pesos, e o pyannote vai tentar a rede." >&2
  exit 1
}

echo
du -sh "$ALVO" | sed 's/^/    /'
echo "    pronto em $ALVO"
