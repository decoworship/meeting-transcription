#!/usr/bin/env bash
# Monta o instalador do MeetingApp — o artefato que se entrega a outra pessoa.
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
#   5. chama o ISCC.exe             (os motores são lidos onde já estão)
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
#   tools/montar_instalador.sh --motores /mnt/c/Users/andre/MeetingApp/motores

set -euo pipefail

RAIZ="$(cd "$(dirname "$0")/.." && pwd)"
PUBLICADO="$RAIZ/dist/publicar"
PAYLOAD="$RAIZ/dist/instalador/payload"
SAIDA="$RAIZ/dist/instalador"
MOTORES="/mnt/c/Users/andre/MeetingApp/motores"
ISCC="/mnt/c/Users/andre/AppData/Local/Programs/Inno Setup 6/ISCC.exe"
WEBVIEW2="https://go.microsoft.com/fwlink/p/?LinkId=2124703"

PULAR_BUILD=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --motores)     MOTORES="$2"; shift 2 ;;
    --pular-build) PULAR_BUILD=1; shift ;;
    *) echo "argumento desconhecido: $1" >&2; exit 2 ;;
  esac
done

export PATH="$HOME/.dotnet:$PATH"

# A versão vem do Directory.Build.props, e de lugar nenhum mais. Ela aparece em
# três lugares — no binário, no instalador e no CHANGELOG — e digitá-la aqui
# seria criar a quarta, que é a que diverge.
VERSAO=$(grep -oP '(?<=<Version>)[^<]+' "$RAIZ/app-net/Directory.Build.props")
[[ -n "$VERSAO" ]] || { echo "ERRO: não achei <Version> em Directory.Build.props" >&2; exit 1; }

echo "==> MeetingApp $VERSAO"

if (( ! PULAR_BUILD )); then
  "$RAIZ/tools/publicar.sh" --so-build
else
  echo "==> pulando o build, a pedido (--pular-build)"
  [[ -f "$PUBLICADO/MeetingApp.exe" ]] || {
    echo "ERRO: --pular-build, mas não há $PUBLICADO/MeetingApp.exe" >&2; exit 1; }
fi

echo "==> montando o payload em $PAYLOAD"
rm -rf "$PAYLOAD"
mkdir -p "$PAYLOAD"
cp "$PUBLICADO/MeetingApp.exe" "$PUBLICADO/WebView2Loader.dll" "$PAYLOAD/"
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

echo "==> conferindo as réguas"

reprovar() { echo "ERRO: $1" >&2; exit 1; }

# ── o binário ────────────────────────────────────────────────────────────────
# O publicar.sh já conferiu tamanho, ícones e ausência de token. Estas são as do
# INSTALADOR, e a plateia é outra: aqui o defeito viaja.

bytes=$(stat -c%s "$PAYLOAD/MeetingApp.exe")
(( bytes > 10000000 )) || reprovar "MeetingApp.exe tem $bytes bytes — as flags não pegaram."

# Nada secreto no artefato entregue. O do HuggingFace saiu na Fase 4; o do Google
# fica, por decisão registrada em docs/FASE4.md §4 — e a régua é sobre o outro.
tokens=$(strings "$PAYLOAD/MeetingApp.exe" | grep -c "hf_[A-Za-z0-9]\{20,\}" || true)
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
versoes=$(strings "$PAYLOAD/MeetingApp.exe" | grep -cF "$VERSAO+" || true)
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
for m in asr diarizacao modelos; do
  [[ -f "$MOTORES/$m/motor.py" ]] || reprovar "falta motores/$m/motor.py"
  if ! diff -q "$RAIZ/motores/$m/motor.py" "$MOTORES/$m/motor.py" >/dev/null; then
    reprovar "motores/$m/motor.py do repositório difere do que está em $MOTORES.
      O instalador empacotaria o sidecar velho. Rode tools/publicar.sh (sem
      --so-build) para sincronizar, ou copie o arquivo à mão."
  fi
done
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

echo "    (gguf, ata\\bin, curand, cusolverMg, tests e .pyi ficam de fora por Excludes)"

# ── privacidade ──────────────────────────────────────────────────────────────
# Nada de cliente, projeto, voz ou reunião pode entrar no instalador. A régua
# roda com os dados reais desta máquina como termo de busca; ver o cabeçalho de
# tools/conferir_privacidade.py.
echo "==> conferindo privacidade (leva alguns minutos)"
python3 "$RAIZ/tools/conferir_privacidade.py" --payload "$PAYLOAD" --motores "$MOTORES" \
  || reprovar "a régua de privacidade reprovou — veja acima o que vazou."

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
  echo "\"$(wslpath -w "$ISCC")\" \"/DVersao=$VERSAO\" \"/DPayload=$payload_win\" \"/DMotores=$motores_win\" \"/DSaida=$saida_win\" \"$iss_win\""
} > "$lote"
# BOM não, acento não: este .cmd é ASCII puro de propósito — .cmd com acento no
# PowerShell 5.1 e no cmd.exe exige BOM, e é armadilha conhecida deste projeto.

(cd /mnt/c && /mnt/c/Windows/System32/cmd.exe /c "$(wslpath -w "$lote")") | tail -20

FINAL="$SAIDA/MeetingApp-$VERSAO-instalador.exe"
[[ -f "$FINAL" ]] || reprovar "o ISCC terminou mas não produziu $FINAL"

# A última régua, e é sobre o artefato inteiro: um instalador pequeno demais não
# tem os motores dentro, e um grande demais tem um .gguf que escapou do Excludes.
tam=$(stat -c%s "$FINAL")
(( tam > 1000000000 )) || reprovar "o instalador tem $((tam/1000000)) MB — pequeno demais para conter os motores."
(( tam < 4000000000 )) || reprovar "o instalador tem $((tam/1000000)) MB — grande demais; um .gguf escapou do Excludes."

echo
printf 'Pronto: %s\n' "$FINAL"
printf '        %.2f GB\n' "$(echo "$tam/1000000000" | bc -l)"
echo
echo "Agora instale numa conta de usuário limpa e rode os critérios A a G"
echo "de docs/FASE4.md §9 — é a parte que nenhum script faz."
