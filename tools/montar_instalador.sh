#!/usr/bin/env bash
# Monta o instalador do PulseMeet — o artefato que se entrega a outra pessoa.
#
# Irmão do publicar.sh, e com a mesma filosofia: **réguas objetivas antes de
# produzir o artefato**, porque cada uma delas corresponde a um defeito que já
# foi entregue. A diferença é a plateia. O publicar.sh instala numa pasta que
# quem escreveu o código consegue consertar em trinta segundos; isto aqui produz
# um arquivo que vai para a máquina de outra pessoa, onde nada se conserta.
#
# O que ele faz, em ordem:
#   1. testes
#   2. publicar.sh --so-build       (as três flags + as réguas do binário)
#   3. monta o payload pequeno      (.exe, DLL, docs, ícone, WebView2)
#   4. confere as réguas do instalador
#   5. monta o pacote de motores    (.7z, só se a impressão digital for nova)
#   6. chama o ISCC.exe
#
# **Desde o DIST-1 (30/09/2026) são dois artefatos.** O instalador, ~20 MB, com
# o app e o código dos sidecars; e o pacote de motores, ~1,7 GB, que o
# instalador baixa do release `motores-<impressão>` só quando a versão
# instalada é outra. A impressão digital é o sha256 da lista "caminho tamanho"
# do que viaja: mudou um arquivo do Python embarcado ou um peso, muda a
# impressão; mudou só o app, o pacote é o mesmo e nem é remontado.
# Com --completo, sai o instalador de antes, com os motores dentro — para
# instalar sem internet.
#
# **Os motores não são copiados.** São 5,4 GB que produziriam os mesmos bytes;
# o Inno lê da instalação existente e exclui o que não deve viajar. Ver
# instalador/MeetingApp.iss.
#
# Pré-requisito: Inno Setup 6.
#   winget install --id JRSoftware.InnoSetup
#
# Uso:
#   tools/montar_instalador.sh
#   tools/montar_instalador.sh --motores /outra/instalacao/motores
#   tools/montar_instalador.sh --completo     # os motores dentro, sem download

set -euo pipefail

RAIZ="$(cd "$(dirname "$0")/.." && pwd)"
PUBLICADO="$RAIZ/dist/publicar"
PAYLOAD="$RAIZ/dist/instalador/payload"
SAIDA="$RAIZ/dist/instalador"
# A instalação OFICIAL, a que o instalador produz — é ela que tem os 4,3 GB.
#
# Era C:\Users\andre\MeetingApp até 18/08/2026, quando o dono do produto apagou
# aquela pasta para liberar disco: os motores estavam duplicados nas duas, e o
# C: estava a 97%. Ler daqui é leitura pura, e o .iss já diz que os motores são
# lidos onde já estão.
MOTORES="/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores"
ISCC="/mnt/c/Users/andre/AppData/Local/Programs/Inno Setup 6/ISCC.exe"
WEBVIEW2="https://go.microsoft.com/fwlink/p/?LinkId=2124703"
SETEZIP="/mnt/c/Program Files/7-Zip/7z.exe"
REPO_URL="https://github.com/decoworship/meeting-transcription"

PULAR_BUILD=0
COMPLETO=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --motores)     MOTORES="$2"; shift 2 ;;
    --pular-build) PULAR_BUILD=1; shift ;;
    --completo)    COMPLETO=1; shift ;;
    *) echo "argumento desconhecido: $1" >&2; exit 2 ;;
  esac
done

export PATH="$HOME/.dotnet:$PATH"

# A versão vem do Directory.Build.props, e de lugar nenhum mais. Ela aparece em
# três lugares — no binário, no instalador e no CHANGELOG — e digitá-la aqui
# seria criar a quarta, que é a que diverge.
VERSAO=$(grep -oP '(?<=<Version>)[^<]+' "$RAIZ/app-net/Directory.Build.props")
[[ -n "$VERSAO" ]] || { echo "ERRO: não achei <Version> em Directory.Build.props" >&2; exit 1; }

echo "==> PulseMeet $VERSAO"

if (( ! PULAR_BUILD )); then
  "$RAIZ/tools/publicar.sh" --so-build
else
  echo "==> pulando o build, a pedido (--pular-build)"
  [[ -f "$PUBLICADO/PulseMeet.exe" ]] || {
    echo "ERRO: --pular-build, mas não há $PUBLICADO/PulseMeet.exe" >&2; exit 1; }
fi

echo "==> montando o payload em $PAYLOAD"
rm -rf "$PAYLOAD"
mkdir -p "$PAYLOAD"
cp "$PUBLICADO/PulseMeet.exe" "$PUBLICADO/WebView2Loader.dll" "$PAYLOAD/"
# O que reponta os atalhos de quem tinha o MeetingApp.exe (docs/MARCA.md); o .iss
# o instala e o roda no fim.
cp "$RAIZ/tools/repontar_atalhos.ps1" "$PAYLOAD/"
cp "$RAIZ/docs/INSTALAR.md" "$RAIZ/CHANGELOG.md" "$PAYLOAD/"
cp "$RAIZ/assets/logo.ico" "$PAYLOAD/"

# O bootstrapper do WebView2, 1,7 MB. Windows 11 já tem a runtime e o Windows 10
# quase sempre também, pelo Edge — mas "quase sempre" na máquina de outra pessoa
# é uma janela em branco sem explicação. O .iss só o executa se faltar.
if [[ ! -f "$SAIDA/MicrosoftEdgeWebview2Setup.exe" ]]; then
  echo "==> baixando o bootstrapper do WebView2"
  curl -sSL --fail -o "$SAIDA/MicrosoftEdgeWebview2Setup.exe" "$WEBVIEW2"
fi
cp "$SAIDA/MicrosoftEdgeWebview2Setup.exe" "$PAYLOAD/"

# O código dos sidecars, do REPOSITÓRIO (DIST-1). Viaja sempre dentro do
# instalador, e não no pacote de motores: muda em quase toda versão, e com ele
# lá dentro cada mudança num motor.py custaria 1,7 GB de download.
for m in asr diarizacao modelos legenda; do
  mkdir -p "$PAYLOAD/codigo/$m"
  cp "$RAIZ/motores/$m/motor.py" "$PAYLOAD/codigo/$m/"
done
rsync -a --exclude __pycache__ --exclude testes \
  "$RAIZ/motores/diarizacao/pipeline/" "$PAYLOAD/codigo/diarizacao/pipeline/"

echo "==> conferindo as réguas"

reprovar() { echo "ERRO: $1" >&2; exit 1; }

# ── o binário ────────────────────────────────────────────────────────────────
# O publicar.sh já conferiu tamanho, ícones e ausência de token. Estas são as do
# INSTALADOR, e a plateia é outra: aqui o defeito viaja.

bytes=$(stat -c%s "$PAYLOAD/PulseMeet.exe")
(( bytes > 10000000 )) || reprovar "PulseMeet.exe tem $bytes bytes — as flags não pegaram."

# Nada secreto no artefato entregue. O do HuggingFace saiu na Fase 4; o do Google
# fica, por decisão registrada em docs/FASE4.md §4 — e a régua é sobre o outro.
tokens=$(strings "$PAYLOAD/PulseMeet.exe" | grep -c "hf_[A-Za-z0-9]\{20,\}" || true)
(( tokens == 0 )) || reprovar "achei $tokens token(s) do HuggingFace no binário."

# A versão do binário tem que ser a mesma do instalador. Sem isto, "Aplicativos
# Instalados" diria 0.1.1 sobre um .exe que se identifica como 0.1.0, e o
# diagnóstico que a pessoa manda apontaria para a versão errada.
#
# Sem âncora de início de linha: o AssemblyInformationalVersion vive nos
# metadados com um byte de comprimento na frente, então o `strings` entrega
# "\x2e0.1.0+<sha>" e um "^" nunca casaria. E `grep -c ... || true` em vez de
# `grep -q`, senão o pipefail transforma o SIGPIPE do strings em reprovação do
# binário correto — o mesmo tropeço que o publicar.sh documenta.
versoes=$(strings "$PAYLOAD/PulseMeet.exe" | grep -cF "$VERSAO+" || true)
(( versoes > 0 )) || reprovar "o binário não carrega a versão $VERSAO — rode sem --pular-build."

# ── os motores ───────────────────────────────────────────────────────────────
[[ -d "$MOTORES" ]] || reprovar "não achei os motores em $MOTORES"
[[ -f "$MOTORES/python/python.exe" ]] || reprovar "falta o Python embarcado — o app abre e não transcreve."

# Os sidecars do instalador têm que ser os DO REPOSITÓRIO.
#
# Este é um buraco real do ciclo de build, achado em 15/08/2026: o
# `publicar.sh --so-build` sai antes de "sincronizando os sidecars", que só roda
# no caminho de instalar. Então quem edita um motor.py, roda este script e manda
# o instalador para um amigo empacota o motor **velho** — compila, passa nos
# testes, passa em todas as outras réguas, e falha só na máquina de quem
# recebeu. É a mesma família do EmbeddedResource com barra invertida.
#
# Reprova em vez de sincronizar de propósito: montar um instalador não deve
# mexer, de lado, na instalação que o usuário está usando para trabalhar.
#
# **Resolvido em 17/09/2026**, quando o publicar.sh passou a instalar na oficial
# — a mensagem abaixo foi reescrita em 29/09/2026. O histórico:
# **a instrução da mensagem abaixo envelheceu em 18/08/2026**, quando os motores
# mudaram de casa: o publicar.sh sincroniza os sidecars na pasta de TRABALHO
# (C:\Users\andre\MeetingApp), e o MOTORES daqui aponta para a instalação
# OFICIAL. Rodar o publicar.sh e voltar aqui reprova de novo, com a mesma
# mensagem, o que manda quem lê rodá-lo uma terceira vez. As duas saídas que
# funcionam estão na mensagem.
#
for m in asr diarizacao modelos; do
  [[ -f "$MOTORES/$m/motor.py" ]] || reprovar "falta motores/$m/motor.py"
  if ! diff -q "$RAIZ/motores/$m/motor.py" "$MOTORES/$m/motor.py" >/dev/null; then
    reprovar "motores/$m/motor.py do repositório difere do que está em $MOTORES.
      O instalador empacotaria o sidecar velho. Duas saídas:
        1. tools/publicar.sh (sem --so-build), com o app fechado: desde
           17/09/2026 ele sincroniza na instalação oficial, que é de onde
           este script lê;
        2. copie o motor.py à mão para $MOTORES."
  fi
done
# **O torchcodec não pode viajar, e esta régua está aqui e não só no
# empacotador.** Ele vem como dependência do pyannote, nunca é usado por este app
# — o motor de diarização entrega `{waveform, sample_rate}` e nunca um caminho —,
# e as DLLs dele não casam com o torch do índice do PyTorch. O sintoma é uma
# **caixa modal do Windows** ("Entry Point Not Found: torch_get_const_data_ptr")
# que trava o python.exe até alguém clicar em OK.
#
# **Por que a régua precisa estar AQUI.** Em 09/09/2026 ela existia só no
# tools/empacotar_motores.sh, que monta a pasta. Mas quem produz o que o usuário
# instala é este script, e ele aceita `--motores` de qualquer pasta — inclusive
# uma montada por uma versão ANTERIOR do empacotador, que foi exatamente o que
# aconteceu: o instalador saiu com o torchcodec dentro, reescreveu os motores da
# máquina do dono do produto e trouxe a caixa de volta, depois de eu ter dito que
# ela não voltaria.
#
# A régua pertence a quem produz o artefato, e não só a quem monta o insumo.
if compgen -G "$MOTORES/python/Lib/site-packages/torchcodec*" >/dev/null; then
  reprovar "o torchcodec está em $MOTORES e não pode viajar.
      Ele abre uma caixa modal do Windows que trava o python.exe, e não é usado.
      Conserto: rm -rf \"$MOTORES\"/python/Lib/site-packages/torchcodec*
      (o tools/empacotar_motores.sh já não o inclui desde 09/09/2026)."
fi
echo "    torchcodec: fora"

# O motor de ata NÃO viaja mais (1,1 GB, docs/FASE4.md §5): o app o baixa da
# release oficial do llama.cpp quando fizer falta. Aqui a régua se inverte —
# conferir que ele está EXCLUÍDO, porque um Excludes com erro de digitação o
# traria de volta em silêncio, e só o tamanho do arquivo final denunciaria.
grep -q 'ata\\bin' "$RAIZ/instalador/MeetingApp.iss" \
  || reprovar "o .iss não exclui mais ata\\bin — o instalador vai engordar 1,1 GB."
# Os pesos de diarização, que desde a Fase 4 são o que substitui o token.
[[ -f "$MOTORES/diarizacao/modelos/community-1/config.yaml" ]] \
  || reprovar "faltam os pesos de diarização — rode tools/empacotar_modelos_de_diarizacao.sh."
# CC-BY-4.0 exige crédito, e o crédito viaja com os pesos.
[[ -f "$MOTORES/diarizacao/modelos/ATRIBUICAO.md" ]] \
  || reprovar "falta a ATRIBUICAO.md dos pesos de diarização."

# Os artefatos ONNX (diarizacao-onnx): desde o porte, é isto que o motor.py
# carrega em produção — não mais os pytorch_model.bin acima, que agora só
# alimentam o exportador. Sem eles a diarização e o
# reconhecimento de vozes falham 100% na máquina de quem instalou, e a
# régua pertence aqui: é este script que produz o artefato entregue, e o
# comentário do topo do arquivo diz por quê. Cada um tem seu próprio comando
# porque a mensagem precisa nomear qual falta — os seis vêm juntos do mesmo
# tools/exportar_diarizacao_onnx.py, então quando um falta os outros cinco
# costumam faltar também, mas não custa nada ser exato.
ONNX_BASE="$MOTORES/diarizacao/modelos"
for f in community-1/segmentation/model.onnx \
         community-1/embedding/codificador.onnx \
         community-1/embedding/cabeca.onnx \
         community-1/embedding/mel.npy \
         wespeaker-voxceleb-resnet34-LM/voz.onnx \
         wespeaker-voxceleb-resnet34-LM/mel.npy \
         nemotron-3/embed.onnx nemotron-3/step.onnx nemotron-3/mel.npy \
         nemotron-3/silencio.npy nemotron-3/config.yaml \
         nemotron-3/LICENSE-OpenMDW-1.1.txt; do
  [[ -f "$ONNX_BASE/$f" ]] \
    || reprovar "falta $f — rode tools/empacotar_modelos_de_diarizacao.sh
      (e, se ele reclamar que não encontra a fonte, antes
      tools/exportar_diarizacao_onnx.py, no venv do WSL)."
done

echo "    (gguf, ata\\bin, curand, cusolverMg, tests e .pyi ficam de fora por Excludes)"

# ── privacidade ──────────────────────────────────────────────────────────────
# Nada de cliente, projeto, voz ou reunião pode entrar no instalador. A régua
# roda com os dados reais desta máquina como termo de busca; ver o cabeçalho de
# tools/conferir_privacidade.py.
echo "==> conferindo privacidade (leva alguns minutos)"
python3 "$RAIZ/tools/conferir_privacidade.py" --payload "$PAYLOAD" --motores "$MOTORES" \
  || reprovar "a régua de privacidade reprovou — veja acima o que vazou."

# ── o pacote de motores ──────────────────────────────────────────────────────
# O que viaja são duas árvores: motores/python e motores/diarizacao/modelos. O
# resto de motores/ é código (vai no payload, acima) ou já não viajava — o
# llama.cpp (ata/bin) e os GGUF são baixados pelo app. As exclusões são as
# MESMAS do Excludes do .iss; se uma mudar lá, muda aqui.
echo "==> calculando a impressão digital dos motores"
LISTA="$SAIDA/motores-lista.tsv"
(cd "$MOTORES" && find python diarizacao/modelos -type f \
    ! -name '*.gguf' ! -name 'curand64_10.dll' ! -name 'cusolverMg64_11.dll' \
    ! -name '*.pyi' ! -path '*/sklearn/datasets/*' ! -path '*/tests/*' \
    ! -path '*/test/*' ! -path '*/.cache/*' ! -path '*/__pycache__/*' \
    -printf '%p\t%s\n' | LC_ALL=C sort) > "$LISTA"
# O formato do pacote entra na impressão: trocar como ele é montado muda o
# arquivo publicado, e o mesmo nome com outro conteúdo reprovaria o Hash de
# quem já tem o instalador anterior.
FORMATO_DO_PACOTE="7z-mx9-naosolido"
MOTORES_VERSAO=$( { echo "$FORMATO_DO_PACOTE"; cat "$LISTA"; } | sha256sum | cut -c1-12)
echo "    $(wc -l < "$LISTA") arquivos · impressão $MOTORES_VERSAO"

iscc_motores="\"/DMotoresVersao=$MOTORES_VERSAO\""
if (( COMPLETO )); then
  iscc_motores="$iscc_motores \"/DCompleto=1\""
else
  PACOTE="$SAIDA/PulseMeet-motores-$MOTORES_VERSAO.7z"
  if [[ -f "$PACOTE" ]]; then
    echo "==> o pacote de motores já existe: $(basename "$PACOTE")"
  else
    [[ -f "$SETEZIP" ]] || reprovar "não achei o 7-Zip em $SETEZIP — winget install --id 7zip.7zip"
    echo "==> montando o pacote de motores (leva uns bons minutos)"
    # A lista vai ao 7-Zip com barra invertida e CRLF, que é como ele a lê no
    # Windows.
    #
    # **Não sólido, e o número é medido.** O Inno extrai arquivo por arquivo, e
    # num bloco sólido cada arquivo obriga a descomprimir o bloco desde o
    # começo: com blocos de 64 MB (30/09/2026), 369 arquivos levaram 8 min 44 s
    # — os 5.518 levariam duas horas. O download de 1,65 GB, no mesmo teste,
    # levou 2 min 17 s.
    lista_win="$SAIDA/motores-lista.txt"
    cut -f1 "$LISTA" | sed 's|/|\\|g; s/$/\r/' > "$lista_win"
    tmp="$PACOTE.parcial"
    rm -f "$tmp"
    lote7="$SAIDA/empacotar.cmd"
    {
      echo "@echo off"
      echo "cd /d \"$(wslpath -w "$MOTORES")\""
      echo "\"$(wslpath -w "$SETEZIP")\" a -t7z -mx=9 -mmt=on -ms=off -bso0 -bsp0 \"$(wslpath -w "$tmp")\" @\"$(wslpath -w "$lista_win")\""
    } > "$lote7"
    (cd /mnt/c && /mnt/c/Windows/System32/cmd.exe /c "$(wslpath -w "$lote7")") \
      || reprovar "o 7-Zip falhou ao montar o pacote de motores."
    mv "$tmp" "$PACOTE"
  fi
  PACOTE_TAM=$(stat -c%s "$PACOTE")
  # O limite de um arquivo num release do GitHub é 2 GiB.
  (( PACOTE_TAM < 2147483648 )) || reprovar "o pacote de motores tem $((PACOTE_TAM/1000000)) MB — passa do limite de 2 GiB do GitHub."
  (( PACOTE_TAM > 500000000 )) || reprovar "o pacote de motores tem $((PACOTE_TAM/1000000)) MB — pequeno demais para ter o Python embarcado."
  PACOTE_SHA=$(sha256sum "$PACOTE" | cut -d' ' -f1)
  MOTORES_URL="$REPO_URL/releases/download/motores-$MOTORES_VERSAO/$(basename "$PACOTE")"
  iscc_motores="$iscc_motores \"/DMotoresUrl=$MOTORES_URL\" \"/DMotoresSha=$PACOTE_SHA\" \"/DMotoresTamanho=$PACOTE_TAM\""
fi

echo "==> compilando o instalador"
[[ -f "$ISCC" ]] || reprovar "não achei o ISCC.exe em $ISCC — winget install --id JRSoftware.InnoSetup"

iss_win=$(wslpath -w "$RAIZ/instalador/MeetingApp.iss")
payload_win=$(wslpath -w "$PAYLOAD")
motores_win=$(wslpath -w "$MOTORES")
saida_win=$(wslpath -w "$SAIDA")

# O comando vai por um .cmd, e não direto no cmd.exe /c.
#
# Motivo medido: o caminho do ISCC.exe tem espaço ("Inno Setup 6"), então ele
# precisa de aspas — e o interop do WSL **escapa as aspas** ao montar a linha de
# comando do processo Windows. O cmd recebe \"C:\...\ISCC.exe\" e responde que
# não reconhece o comando. Com o .cmd escrito daqui, as aspas nascem do lado
# Windows e ninguém as toca no caminho.
lote="$SAIDA/compilar.cmd"
{
  echo "@echo off"
  echo "\"$(wslpath -w "$ISCC")\" \"/DVersao=$VERSAO\" \"/DPayload=$payload_win\" \"/DMotores=$motores_win\" \"/DSaida=$saida_win\" $iscc_motores \"$iss_win\""
} > "$lote"
# BOM não, acento não: este .cmd é ASCII puro de propósito — .cmd com acento no
# PowerShell 5.1 e no cmd.exe exige BOM, e é armadilha conhecida deste projeto.

(cd /mnt/c && /mnt/c/Windows/System32/cmd.exe /c "$(wslpath -w "$lote")") | tail -20

if (( COMPLETO )); then
  FINAL="$SAIDA/PulseMeet-$VERSAO-instalador-completo.exe"
else
  FINAL="$SAIDA/PulseMeet-$VERSAO-instalador.exe"
fi
[[ -f "$FINAL" ]] || reprovar "o ISCC terminou mas não produziu $FINAL"

# A última régua, e é sobre o artefato inteiro: um instalador pequeno demais não
# tem os motores dentro, e um grande demais tem um .gguf que escapou do Excludes.
#
# **A expectativa mudou em 04/09/2026**, e vale escrita: eram 1,59 GB, e o
# `transcribe.cpp` da legenda ao vivo acrescenta ~200 MB de nativo ao Python
# embarcado — passa a ~1,79 GB. Os GGUF **não** entra aqui: ele
# é pacote do Catalogo, baixado sob demanda, justamente porque o `Excludes` do
# .iss descartaria o arquivo em silêncio. Ver docs/FASE7-BACKEND.md A1.
#
# Os limites não mudaram porque não precisavam: o de baixo pergunta "os motores
# estão aí?" e o de cima, "escapou um .gguf?" — e 0,70 GB a mais ainda cabe entre
# os dois. Se um dia o instalador chegar perto de 4 GB por crescimento legítimo,
# o conserto é o DIST-1 do backlog (separar os motores), não afrouxar a régua.
tam=$(stat -c%s "$FINAL")
if (( COMPLETO )); then
  (( tam > 1000000000 )) || reprovar "o instalador tem $((tam/1000000)) MB — pequeno demais para conter os motores."
  (( tam < 4000000000 )) || reprovar "o instalador tem $((tam/1000000)) MB — grande demais; um .gguf escapou do Excludes."
else
  # Sem os motores, o instalador é o app: os 18 MB do .exe comprimem para ~10.
  (( tam > 5000000 )) || reprovar "o instalador tem $((tam/1000000)) MB — pequeno demais para conter o app."
  (( tam < 150000000 )) || reprovar "o instalador tem $((tam/1000000)) MB — os motores entraram nele."
fi

echo
printf 'Pronto: %s\n' "$FINAL"
printf '        %.2f GB\n' "$(echo "$tam/1000000000" | bc -l)"
echo
if (( ! COMPLETO )); then
  echo "Motores: $(basename "$PACOTE") · impressão $MOTORES_VERSAO"
  echo "         SHA256 $PACOTE_SHA"
  if gh release view "motores-$MOTORES_VERSAO" >/dev/null 2>&1; then
    echo "         o release motores-$MOTORES_VERSAO já existe — nada a subir."
  else
    echo "         o release motores-$MOTORES_VERSAO NÃO existe. Suba-o ANTES do"
    echo "         instalador, senão ele baixa um 404 na máquina de quem instala:"
    echo "           gh release create motores-$MOTORES_VERSAO --latest=false \\"
    echo "             --title \"Motores $MOTORES_VERSAO\" --notes \"Pacote de motores do PulseMeet.\" \\"
    echo "             \"$PACOTE\""
  fi
  echo
fi
echo "Agora instale numa conta de usuário limpa e rode os critérios A a G"
echo "de docs/FASE4.md §9 — é a parte que nenhum script faz."
