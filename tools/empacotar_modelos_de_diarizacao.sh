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
# ── artefatos ONNX (diarizacao-onnx) ────────────────────────────────────────
#
# Desde o porte para ONNX, o motor.py em produção não abre mais os
# pytorch_model.bin do community-1 e do wespeaker — abre model.onnx,
# codificador.onnx, cabeca.onnx e voz.onnx, mais um mel.npy ao lado de cada
# embedding. Esses seis arquivos não são baixados: são exportados dos pesos
# pytorch por tools/exportar_diarizacao_onnx.py, que precisa de torch e roda
# no venv do WSL — o oposto do que este script, que só usa curl e cp, pode
# fazer sozinho. Por isso este script COPIA os artefatos ONNX de dentro da
# instalação oficial (onde o exportador os escreve) para o destino do pacote,
# e se não os achar lá, para com a régua pedindo para rodar o exportador
# primeiro — nunca tenta exportar sozinho. Ver pegar_onnx() abaixo.
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

# Onde tools/exportar_diarizacao_onnx.py escreve os artefatos ONNX — ele tem
# esse caminho cravado (constante MODELOS), porque precisa ler os
# pytorch_model.bin já instalados para exportar, e só a instalação oficial os
# tem de forma confiável. Não é configurável por --destino: exportar é uma
# operação sobre a instalação oficial, empacotar é que pode mirar outro lugar.
FONTE_ONNX="/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores/diarizacao/modelos"

ALVO="$DESTINO/motores/diarizacao/modelos"
PIPELINE="$ALVO/community-1"
PIPELINE31="$ALVO/pyannote-3.1"
VOZ="$ALVO/wespeaker-voxceleb-resnet34-LM"

# Os nomes das pastas são os que o motor procura, e não os do repositório: quem
# lê é motores/diarizacao/motor.py, e mudar um sem o outro faz o motor cair no
# caminho do HuggingFace sem avisar. É por isso que a régua do fim confere os
# caminhos exatos.

mkdir -p "$PIPELINE/segmentation" "$PIPELINE/embedding" "$PIPELINE/plda" "$VOZ"
mkdir -p "$PIPELINE31/segmentation" "$PIPELINE31/embedding"

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

# Copia um artefato ONNX de FONTE_ONNX para dentro do pacote. Diferente de
# pegar(): não há download possível — o exportador precisa de torch, roda no
# venv do WSL, e não faz sentido reimplementá-lo aqui em bash. Se a fonte
# também não tem o artefato, o jeito de resolver é rodar o exportador, e a
# mensagem de erro diz exatamente o comando.
pegar_onnx() {
  local relativo="$1" saida="$2"

  if [[ -f "$saida" ]]; then
    echo "    já está: ${saida#$ALVO/}"
    return
  fi

  local origem="$FONTE_ONNX/$relativo"
  if [[ -f "$origem" ]]; then
    # Quando ALVO é a própria instalação oficial, origem e saida já são o
    # mesmo caminho, e o "-f \"$saida\"" acima já teria retornado. Este cp só
    # roda quando são pastas diferentes (--destino, ou instalação nova).
    cp "$origem" "$saida"
    echo "    da instalação oficial: ${saida#$ALVO/}"
    return
  fi

  echo "ERRO: falta $relativo, e não achei em $origem." >&2
  echo "      Este arquivo não se baixa — ele é exportado do checkpoint" >&2
  echo "      pytorch com tools/exportar_diarizacao_onnx.py, que roda no" >&2
  echo "      venv do WSL (precisa de torch) contra a instalação oficial:" >&2
  echo "        uv run --with onnx --with onnxscript --with onnxruntime \\" >&2
  echo "          python tools/exportar_diarizacao_onnx.py" >&2
  echo "      Rode-o e depois este script de novo." >&2
  exit 1
}

echo "==> pipeline de diarização (community-1)"
pegar pyannote/speaker-diarization-community-1 config.yaml                 "$PIPELINE/config.yaml"
pegar pyannote/speaker-diarization-community-1 segmentation/pytorch_model.bin "$PIPELINE/segmentation/pytorch_model.bin"
pegar pyannote/speaker-diarization-community-1 embedding/pytorch_model.bin    "$PIPELINE/embedding/pytorch_model.bin"
pegar pyannote/speaker-diarization-community-1 plda/plda.npz                  "$PIPELINE/plda/plda.npz"
pegar pyannote/speaker-diarization-community-1 plda/xvec_transform.npz        "$PIPELINE/plda/xvec_transform.npz"

echo "==> modelo de voz (wespeaker)"
pegar pyannote/wespeaker-voxceleb-resnet34-LM pytorch_model.bin "$VOZ/pytorch_model.bin"

# ── artefatos ONNX (docs/DIARIZACAO-ONNX.md) ────────────────────────────────
#
# O motor.py não abre mais os pytorch_model.bin acima em produção — eles ficam
# só como insumo do exportador (e do pyannote-3.1, que ainda é torch puro). O
# que o motor carrega em tempo de execução é isto aqui, e desde 583fb1d o
# reconhecimento de vozes (voz.onnx + mel.npy) também depende disso, sem
# fallback para torch se faltar.
echo "==> artefatos ONNX (segmentação, embedding, voz)"
pegar_onnx community-1/segmentation/model.onnx        "$PIPELINE/segmentation/model.onnx"
pegar_onnx community-1/embedding/codificador.onnx     "$PIPELINE/embedding/codificador.onnx"
pegar_onnx community-1/embedding/cabeca.onnx          "$PIPELINE/embedding/cabeca.onnx"
pegar_onnx community-1/embedding/mel.npy              "$PIPELINE/embedding/mel.npy"
pegar_onnx wespeaker-voxceleb-resnet34-LM/voz.onnx    "$VOZ/voz.onnx"
pegar_onnx wespeaker-voxceleb-resnet34-LM/mel.npy     "$VOZ/mel.npy"

# ── o pyannote 3.1, como segunda opção ──────────────────────────────────────
#
# Ele voltou em 20/08/2026, quando a escolha de modelo de diarização passou a
# chegar ao motor (docs/FASE6.md §4.6). Antes disso era um download para um
# modelo que o app não sabia carregar.
#
# **Ele é MIT, e não CC-BY como o community-1.** MIT permite redistribuir com o
# aviso de licença junto — que é o que o LICENSE baixado abaixo cumpre.
#
# O community-1 vence por 6,7 pontos de DER (Fase 0), então o 3.1 não é o
# padrão. Ele existe para poder comparar na máquina de quem usa, e porque um
# seletor com uma opção só não é um seletor.
echo "==> pipeline de diarização (pyannote 3.1)"

# O peso é só a segmentação: o embedding do 3.1 é o **mesmo** wespeaker que o
# motor já usa para as vozes, e ele é copiado logo abaixo em vez de baixado de
# novo.
pegar pyannote/segmentation-3.0 pytorch_model.bin "$PIPELINE31/segmentation/pytorch_model.bin"
pegar pyannote/segmentation-3.0 LICENSE           "$PIPELINE31/LICENSE-segmentation-3.0"
pegar pyannote/speaker-diarization-3.1 config.yaml "$PIPELINE31/config.yaml.remoto"

# ── por que o config.yaml precisa ser reescrito ─────────────────────────────
#
# O do community-1 já aponta os pesos por `$model/...`, caminho relativo à
# própria pasta — é isso que faz a carga local funcionar. O do 3.1 **não**:
#
#     segmentation: pyannote/segmentation-3.0
#     embedding: pyannote/wespeaker-voxceleb-resnet34-LM
#
# São nomes de repositório. Copiá-lo como veio produziria uma pasta que parece
# local e vai à rede na primeira reunião — e, pior, a um repositório com
# portão, que é o defeito que a Fase 4 acabou de tirar do app.
#
# A reescrita troca só essas duas linhas. Tudo o mais — os limiares do
# clustering, que são o modelo — vem do arquivo original e não de uma cópia
# escrita aqui, que envelheceria em silêncio se o upstream mudasse.
sed -e 's|^\(\s*\)segmentation: pyannote/segmentation-3.0\s*$|\1segmentation: $model/segmentation|' \
    -e 's|^\(\s*\)embedding: pyannote/wespeaker-voxceleb-resnet34-LM\s*$|\1embedding: $model/embedding|' \
    "$PIPELINE31/config.yaml.remoto" > "$PIPELINE31/config.yaml"
rm -f "$PIPELINE31/config.yaml.remoto"

# O embedding do 3.1 é o mesmo modelo de voz que já está empacotado. Cópia e
# não link: o instalador leva arquivos, e um link simbólico dentro dele viraria
# um arquivo de texto com um caminho do WSL na máquina de quem instalou.
cp "$VOZ/pytorch_model.bin" "$PIPELINE31/embedding/pytorch_model.bin"

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

## pyannote/speaker-diarization-3.1 e pyannote/segmentation-3.0

- autoria: Hervé Bredin e colaboradores
- origem: https://huggingface.co/pyannote/speaker-diarization-3.1
          https://huggingface.co/pyannote/segmentation-3.0
- licença: **MIT** — o texto está em `pyannote-3.1/LICENSE-segmentation-3.0`

O `config.yaml` deste pipeline **foi modificado**: as duas referências a
repositórios do HuggingFace (`pyannote/segmentation-3.0` e
`pyannote/wespeaker-voxceleb-resnet34-LM`) foram trocadas pelos caminhos locais
`$model/segmentation` e `$model/embedding`. Nenhum peso foi alterado, e nenhum
hiperparâmetro do pipeline foi tocado.

O `embedding/pytorch_model.bin` desta pasta é uma cópia do
`wespeaker-voxceleb-resnet34-LM` acima — é o mesmo modelo que o 3.1 usa.

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
         "$PIPELINE31/config.yaml" \
         "$PIPELINE31/segmentation/pytorch_model.bin" \
         "$PIPELINE31/embedding/pytorch_model.bin" \
         "$PIPELINE31/LICENSE-segmentation-3.0" \
         "$VOZ/pytorch_model.bin" \
         "$PIPELINE/segmentation/model.onnx" \
         "$PIPELINE/embedding/codificador.onnx" \
         "$PIPELINE/embedding/cabeca.onnx" \
         "$PIPELINE/embedding/mel.npy" \
         "$VOZ/voz.onnx" \
         "$VOZ/mel.npy"; do
  [[ -f "$f" ]] || { echo "ERRO: falta $f" >&2; exit 1; }
done

# Os ONNX não passam pela régua de tamanho abaixo: eles não vêm de download,
# então não há risco de "página de erro do portão salva como se fosse peso" —
# o torch.onnx.export levanta exceção e pegar_onnx já teria falhado antes de
# chegar aqui. A régua de presença acima já é a proteção que faz sentido para
# eles.

# Um HTML de erro do portão tem uns 2 KB e passaria por "arquivo presente". Os
# pesos têm dezenas de MB; o config.yaml tem centenas de bytes e é o único que
# pode ser pequeno.
for f in "$PIPELINE/segmentation/pytorch_model.bin" \
         "$PIPELINE/embedding/pytorch_model.bin" \
         "$PIPELINE31/segmentation/pytorch_model.bin" \
         "$PIPELINE31/embedding/pytorch_model.bin" \
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
for c in "$PIPELINE/config.yaml" "$PIPELINE31/config.yaml"; do
  grep -q '\$model/segmentation' "$c" || {
    echo "ERRO: $c não usa \$model/ — a carga local não vai resolver" >&2
    echo "      os pesos, e o pyannote vai tentar a rede." >&2
    exit 1
  }
done

# E o inverso, só para o 3.1: se a reescrita não pegou, o nome do repositório
# continua lá e o arquivo passaria na régua acima por causa da outra linha.
grep -q 'pyannote/segmentation-3.0\|pyannote/wespeaker' "$PIPELINE31/config.yaml" && {
  echo "ERRO: $PIPELINE31/config.yaml ainda cita repositório do HuggingFace." >&2
  echo "      A reescrita do sed não pegou — o upstream mudou o formato." >&2
  exit 1
}

echo
du -sh "$ALVO" | sed 's/^/    /'
echo "    pronto em $ALVO"
