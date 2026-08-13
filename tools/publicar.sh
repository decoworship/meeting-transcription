#!/usr/bin/env bash
# Publica o app unificado e o instala numa pasta de teste.
#
# Existe por causa de dois defeitos entregues ao usuário no mesmo dia (11/08),
# os dois por publicar na mão:
#
#   1. sem as três flags, sai um .exe de 193 KB que depende de DLLs soltas e
#      não abre;
#   2. sem -p:TokenHuggingFace, sai um binário sem o token embutido, que
#      compila, publica, e só falha na diarização, na máquina do usuário.
#
# As duas se detectam por uma régua objetiva, e é isso que este script faz:
# tamanho mínimo e exatamente uma ocorrência de hf_token. Se qualquer uma
# falhar, ele para antes de copiar — o binário quebrado nunca chega na pasta
# de quem usa.
#
# ── Fase 2.5: o destino mudou de propósito ──────────────────────────────────
#
# O padrão agora é C:\Users\andre\MeetingUnificado, e NÃO a pasta do app nem a
# do gravador. Enquanto o app fundido não for aprovado, os dois programas
# antigos continuam sendo os que gravam reunião de verdade todo dia, e o
# critério A da fase exige gravar com os dois ao mesmo tempo para comparar as
# faixas. Publicar por cima deles apagaria a régua e o plano B no mesmo comando.
#
# Depois de aprovado, é passar --destino '/mnt/c/Users/andre/MeetingApp'.
#
# O que ele NÃO faz, de propósito: abrir o app para conferir. Screenshot e
# clique sintético foram abandonados neste projeto (as janelas abriam em telas
# arbitrárias e os cliques acertavam o editor do usuário). Depois de publicar,
# peça para uma pessoa olhar.
#
# Uso:
#   tools/publicar.sh                     # publica e instala no destino de teste
#   tools/publicar.sh --so-build          # publica em dist/publicar, sem instalar
#   tools/publicar.sh --destino <pasta>   # instala em outra pasta

set -euo pipefail

RAIZ="$(cd "$(dirname "$0")/.." && pwd)"
SAIDA="$RAIZ/dist/publicar"
DESTINO="/mnt/c/Users/andre/MeetingUnificado"
TOKEN="/mnt/c/Users/andre/.meeting-recorder/hf_token.txt"
SEGREDO="/mnt/c/Users/andre/.meeting-recorder/google_client_secret.json"

# De onde vêm os 4,3 GB de Python embarcado. Não se copia: o destino de teste
# ganha uma junção para esta pasta (ver adiante).
MOTORES_FONTE="/mnt/c/Users/andre/MeetingApp/motores"

SO_BUILD=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --so-build) SO_BUILD=1; shift ;;
    --destino)  DESTINO="$2"; shift 2 ;;
    *) echo "argumento desconhecido: $1" >&2; exit 2 ;;
  esac
done

export PATH="$HOME/.dotnet:$PATH"

# O app aberto é intocável. Já custou uma transcrição do usuário no meio, e
# copiar por cima de um .exe em execução falha de qualquer forma no Windows.
#
# Confere pelo CAMINHO e não pelo nome do processo: desde a Fase 2.5 o app antigo
# e o novo se chamam MeetingApp.exe, e barrar pelo nome impediria de publicar na
# pasta de teste enquanto o usuário trabalha no app de produção — que é
# exatamente o arranjo que esta fase pede.
if [[ -n "${DESTINO:-}" ]]; then
  aberto=$(/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe -NoProfile -Command \
    "(Get-Process MeetingApp -ErrorAction SilentlyContinue).Path" 2>/dev/null | tr -d '\r')
  alvo=$(wslpath -w "$DESTINO" 2>/dev/null || echo "$DESTINO")
  if grep -qiF "$alvo" <<<"$aberto"; then
    echo "ERRO: o MeetingApp de $DESTINO está aberto. Feche-o antes de publicar." >&2
    echo "      (não mate o processo: pode haver uma transcrição — ou uma GRAVAÇÃO —" >&2
    echo "       em andamento; desde a Fase 2.5 o mesmo processo faz as duas coisas)" >&2
    exit 1
  fi
fi

if [[ ! -f "$TOKEN" ]]; then
  echo "ERRO: não achei o token em $TOKEN" >&2
  echo "      Sem ele o binário publica e falha na diarização, na máquina do usuário." >&2
  exit 1
fi

# O segredo do Google não é obrigatório para o app abrir, mas sem ele o app
# fundido perde a agenda inteira — que na Fase 1 vinha embutida no gravador.
# Avisar alto: é o tipo de perda que só aparece na próxima reunião.
if [[ ! -f "$SEGREDO" ]]; then
  echo "AVISO: não achei $SEGREDO" >&2
  echo "       O app vai sair sem o Google Calendar embutido." >&2
fi

echo "==> testes"
dotnet test "$RAIZ"/app-net/Tests/*.csproj --nologo -v q

echo "==> publicando em $SAIDA"
rm -rf "$SAIDA"
# As três flags são obrigatórias: o .csproj tem SelfContained=false de
# propósito, para o loop de desenvolvimento ficar rápido.
dotnet publish "$RAIZ/app-net/App/MeetingApp.App.csproj" \
  -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true \
  -p:TokenHuggingFace="$TOKEN" \
  -p:SegredoDoGoogle="$SEGREDO" \
  -o "$SAIDA" --nologo -v q

EXE="$SAIDA/MeetingApp.exe"

echo "==> conferindo as réguas"
bytes=$(stat -c%s "$EXE")
if (( bytes < 10000000 )); then
  echo "ERRO: $EXE tem $bytes bytes. Menos de 10 MB significa que as flags não" >&2
  echo "      pegaram — este binário não abre sozinho." >&2
  exit 1
fi

tokens=$(strings "$EXE" | grep -c hf_token || true)
if (( tokens != 1 )); then
  echo "ERRO: esperava 1 ocorrência de hf_token no binário, achei $tokens." >&2
  echo "      O USERPROFILE do MSBuild é vazio no WSL: confira o caminho do token." >&2
  exit 1
fi

# Régua nova da Fase 2.5: o gravador está mesmo dentro deste executável?
# Um erro de referência de projeto compila e publica sem reclamar, e só se
# descobre quando não aparece ícone nenhum na bandeja.
#
# `grep -c ... || true` e não `grep -q`: com pipefail, o grep -q sai no primeiro
# acerto, o strings morre de SIGPIPE (141), e a régua reprova justamente o
# binário correto. Foi o que aconteceu na primeira vez que ela rodou.
icones=$(strings "$EXE" | grep -c "bandeja-vermelho.ico" || true)
if (( icones == 0 )); then
  echo "ERRO: os ícones da bandeja não estão embutidos. Sem eles o app sobe" >&2
  echo "      sem ícone e não dá nem para sair dele." >&2
  exit 1
fi

printf '    tamanho: %.1f MB (ok)\n    token embutido: sim\n    ícones da bandeja: sim\n' \
  "$(echo "$bytes/1000000" | bc -l)"

if (( SO_BUILD )); then
  echo "==> pronto em $EXE (não instalado, a pedido)"
  exit 0
fi

echo "==> instalando em $DESTINO"
mkdir -p "$DESTINO"
cp "$SAIDA/MeetingApp.exe" "$DESTINO/"
# O carregador nativo do WebView2 não entra no single-file: ele é carregado por
# nome, do disco, antes de o host gerenciado existir.
cp "$SAIDA/WebView2Loader.dll" "$DESTINO/"
ls -la "$DESTINO/MeetingApp.exe"

# Os motores vão junto, mas o Python embarcado NÃO é copiado: são 4,3 GB, e o
# destino de teste conviveria em disco com uma cópia idêntica da instalação
# antiga. Uma junção do Windows resolve, e é reversível apagando a pasta.
#
# Só o python/ é compartilhado. Os três motor.py são cópias de verdade, e é
# deliberado: com junção, publicar aqui reescreveria os sidecars do app que o
# usuário ainda usa todo dia — exatamente o que esta pasta separada evita.
if [[ ! -e "$DESTINO/motores/python" ]]; then
  if [[ -d "$MOTORES_FONTE/python" ]]; then
    echo "==> ligando motores/python por junção a $MOTORES_FONTE/python"
    mkdir -p "$DESTINO/motores"
    destino_win=$(wslpath -w "$DESTINO/motores/python")
    fonte_win=$(wslpath -w "$MOTORES_FONTE/python")
    # Duas armadilhas do cmd.exe chamado do WSL, as duas custaram uma tentativa:
    #   - a partir de um caminho UNC (\\wsl.localhost\...) ele avisa e cai no
    #     diretório do Windows; daí o subshell com cd para /mnt/c;
    #   - sem /s ele remove a primeira e a última aspas do comando, deixando as
    #     aspas dos caminhos desemparelhadas.
    (cd /mnt/c && /mnt/c/Windows/System32/cmd.exe /s /c \
       "mklink /J \"$destino_win\" \"$fonte_win\"") >/dev/null
  else
    echo "AVISO: não achei $MOTORES_FONTE/python — o app abre, mas não transcreve." >&2
  fi
fi

echo "==> sincronizando os sidecars"
for m in asr diarizacao modelos; do
  mkdir -p "$DESTINO/motores/$m"
  cp "$RAIZ/motores/$m/motor.py" "$DESTINO/motores/$m/"
  echo "    motores/$m/motor.py"
done

echo
echo "Instalado em $DESTINO."
echo "O MeetingApp e o MeetingRecorder antigos não foram tocados."
echo "Agora abra o app e veja — é a parte que nenhum script faz."
