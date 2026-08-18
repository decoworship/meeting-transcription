#!/usr/bin/env bash
# Publica o app unificado e o instala numa pasta de teste.
#
# Existe por causa de dois defeitos entregues ao usuário no mesmo dia (11/08),
# os dois por publicar na mão:
#
#   1. sem as três flags, sai um .exe de 193 KB que depende de DLLs soltas e
#      não abre;
#   2. sem -p:TokenHuggingFace, saía um binário sem o token embutido, que
#      compilava, publicava, e só falhava na diarização, na máquina do usuário.
#
# As duas se detectam por uma régua objetiva, e é isso que este script faz. Se
# qualquer uma falhar, ele para antes de copiar — o binário quebrado nunca chega
# na pasta de quem usa.
#
# **A régua do token inverteu na Fase 4.** O defeito 2 não existe mais: os pesos
# de diarização viajam dentro do instalador e o binário não embute token nenhum.
# A régua continua no mesmo lugar, com o sinal trocado — agora ela reprova o
# binário que TEM um token, porque este .exe é entregue a outras pessoas.
#
# ── Fase 2.5: o destino mudou de propósito ──────────────────────────────────
#
# O padrão é C:\Users\andre\MeetingApp, a instalação de trabalho.
#
# ── 18/08/2026: os motores saíram daqui ─────────────────────────────────────
#
# O dono do produto apagou esta pasta para liberar disco: os 4,3 GB de Python
# embarcado estavam duplicados nela e na instalação que o instalador da Fase 4
# produz, e o C: estava a 97%. Quem tem os motores agora é a instalação oficial,
# em AppData\Local\Programs\MeetingApp — daí o MOTORES_FONTE apontar para lá.
#
# O destino continua sendo esta pasta, e não a oficial, de propósito: um build
# meio pronto não pode cair no app que grava reunião. Ela volta a existir com o
# executável e uma JUNÇÃO para os motores oficiais, o que custa 19 MB em vez de
# 4,3 GB.
#
# Durante a Fase 2.5 o padrão era uma pasta de teste, MeetingUnificado, porque
# os dois programas antigos ainda eram os que gravavam e o critério A exigia
# gravar com os dois ao mesmo tempo para comparar as faixas: publicar por cima
# apagaria a régua e o plano B no mesmo comando. A fase foi aprovada em
# 13/08/2026, a pasta de teste desfeita, e o dono do produto autorizou
# sobrescrever — da Fase 3 em diante são melhorias sobre um app que funciona.
#
# O que segue valendo: **o app aberto é intocável** (a checagem logo abaixo), e
# publicar continua sendo só por este script, com as três réguas.
#
# O que ele NÃO faz, de propósito: abrir o app para conferir. Screenshot e
# clique sintético foram abandonados neste projeto (as janelas abriam em telas
# arbitrárias e os cliques acertavam o editor do usuário). Depois de publicar,
# peça para uma pessoa olhar.
#
# Uso:
#   tools/publicar.sh                     # publica e instala em C:\Users\andre\MeetingApp
#   tools/publicar.sh --so-build          # publica em dist/publicar, sem instalar
#   tools/publicar.sh --destino <pasta>   # instala em outra pasta

set -euo pipefail

RAIZ="$(cd "$(dirname "$0")/.." && pwd)"
SAIDA="$RAIZ/dist/publicar"
DESTINO="/mnt/c/Users/andre/MeetingApp"
SEGREDO="/mnt/c/Users/andre/.meeting-recorder/google_client_secret.json"

# De onde vêm os 4,3 GB de Python embarcado. Não se copia: o destino ganha uma
# junção para esta pasta (ver adiante).
#
# É a instalação OFICIAL, a que o instalador produz. Era a pasta de trabalho até
# 18/08/2026, quando ela foi apagada para liberar disco — ver o cabeçalho.
MOTORES_FONTE="/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores"

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
#
# Com --so-build nada é copiado para lugar nenhum, então não há o que proteger:
# barrar ali obrigaria a fechar o app para só montar o binário — que é
# exatamente o que a Fase 4 faz o tempo todo, montando payload de instalador
# enquanto o app grava a reunião do dia.
if (( ! SO_BUILD )) && [[ -n "${DESTINO:-}" ]]; then
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

# O token do HuggingFace deixou de ser exigência na Fase 4: os pesos de
# diarização viajam dentro do instalador e o binário não carrega segredo nenhum.
# O que se confere agora é o contrário — que os pesos estão no destino.
if [[ ! -f "$DESTINO/motores/diarizacao/modelos/community-1/config.yaml" ]]; then
  echo "AVISO: não achei os pesos de diarização em $DESTINO/motores/diarizacao/modelos" >&2
  echo "       Rode tools/empacotar_modelos_de_diarizacao.sh, senão a diarização" >&2
  echo "       vai tentar o HuggingFace — e sem token embutido ela falha." >&2
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

# A régua do token INVERTEU na Fase 4.
#
# Até a Fase 3 ela exigia exatamente uma ocorrência de hf_token: o binário sem
# token compilava, publicava e só falhava na diarização, na máquina do usuário.
# Agora o token não é mais embutido — os pesos de diarização viajam dentro do
# instalador, e o binário entregue a outra pessoa não pode carregar segredo
# nenhum. A mesma linha, com o sinal trocado: presença é o defeito.
#
# `grep -c ... || true` e não `grep -q`, pelo mesmo motivo de sempre: com
# pipefail o grep -q mata o strings com SIGPIPE.
tokens=$(strings "$EXE" | grep -c "hf_[A-Za-z0-9]\{20,\}" || true)
if (( tokens != 0 )); then
  echo "ERRO: achei $tokens token(s) do HuggingFace no binário." >&2
  echo "      Desde a Fase 4 nada secreto pode ir junto: este .exe é entregue a" >&2
  echo "      outras pessoas. Confira o EmbeddedResource em MeetingApp.Nucleo.csproj." >&2
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

printf '    tamanho: %.1f MB (ok)\n    sem token do HuggingFace: sim\n    ícones da bandeja: sim\n' \
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

# O que é pesado NÃO é copiado: o destino ganha junções do Windows para a
# instalação oficial. São 4,3 GB de Python embarcado, 3,5 GB de motor de ata e
# 57 MB de pesos de diarização — copiar isso a cada publicação encheria o disco,
# e foi exatamente o que levou o dono do produto a apagar a pasta de trabalho em
# 18/08/2026. Reversível apagando a pasta: junção não é dono dos bytes.
#
# Os três motor.py continuam sendo cópias DE VERDADE, e é a distinção que
# importa: uma publicação de teste não pode reescrever os sidecars do app que
# grava reunião.
ligar_por_juncao() {
  local relativo="$1" descricao="$2"
  [[ -e "$DESTINO/motores/$relativo" ]] && return 0

  if [[ ! -d "$MOTORES_FONTE/$relativo" ]]; then
    echo "AVISO: não achei $MOTORES_FONTE/$relativo — $descricao" >&2
    return 0
  fi

  echo "==> ligando motores/$relativo por junção"
  mkdir -p "$(dirname "$DESTINO/motores/$relativo")"

  # New-Item do PowerShell, e não `mklink /J` pelo cmd.exe.
  #
  # O mklink daqui responde "The filename, directory name, or volume label
  # syntax is incorrect" mesmo com o `/s` e com o subshell em /mnt/c que
  # contornavam as duas armadilhas conhecidas do interop — e com os dois
  # caminhos existindo, listáveis por `dir` no MESMO cmd.exe. Medido em
  # 18/08/2026, na Fase 5. O New-Item faz o mesmo trabalho e funciona.
  powershell.exe -NoProfile -Command \
    "New-Item -ItemType Junction -Path '$(wslpath -w "$DESTINO/motores/$relativo")'" \
    "-Target '$(wslpath -w "$MOTORES_FONTE/$relativo")' | Out-Null" >/dev/null
}

ligar_por_juncao python              "o app abre, mas não transcreve."
ligar_por_juncao diarizacao/modelos  "o app transcreve, mas não separa falantes."
ligar_por_juncao ata                 "o app transcreve, mas gerar ata falha."

# O motor de ata é conferido, não copiado: são 3,5 GB que não mudam a cada
# build. Sem ele o app abre e transcreve; só a ata falha, e falha na hora de
# gerar — que é tarde. Ver tools/empacotar_motor_de_ata.sh.
if [[ -f "$DESTINO/motores/ata/bin/llama-server.exe" ]]; then
  echo "==> motor de ata presente"
else
  echo "AVISO: sem motor de ata em $DESTINO/motores/ata —" >&2
  echo "       rode tools/empacotar_motor_de_ata.sh, senão gerar ata vai falhar." >&2
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
