#!/usr/bin/env bash
# Publica o app e o instala em C:\Users\andre\MeetingApp.
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
# tamanho ~15,7 MB e exatamente uma ocorrência de hf_token. Se qualquer uma
# falhar, ele para antes de copiar — o binário quebrado nunca chega na pasta
# de quem usa.
#
# O que ele NÃO faz, de propósito: abrir o app para conferir. Screenshot e
# clique sintético foram abandonados neste projeto (as janelas abriam em telas
# arbitrárias e os cliques acertavam o editor do usuário). Depois de publicar,
# peça para uma pessoa olhar.
#
# Uso:
#   tools/publicar.sh            # publica e instala
#   tools/publicar.sh --so-build # publica em dist/publicar, sem instalar

set -euo pipefail

RAIZ="$(cd "$(dirname "$0")/.." && pwd)"
SAIDA="$RAIZ/dist/publicar"
DESTINO="/mnt/c/Users/andre/MeetingApp"
TOKEN="/mnt/c/Users/andre/.meeting-recorder/hf_token.txt"

export PATH="$HOME/.dotnet:$PATH"

# O app aberto é intocável. Já custou uma transcrição do usuário no meio, e
# copiar por cima de um .exe em execução falha de qualquer forma no Windows.
if /mnt/c/Windows/System32/tasklist.exe 2>/dev/null | grep -qi "MeetingApp.exe"; then
  echo "ERRO: o MeetingApp está aberto. Feche-o antes de publicar." >&2
  echo "      (não mate o processo: pode haver uma transcrição em andamento)" >&2
  exit 1
fi

if [[ ! -f "$TOKEN" ]]; then
  echo "ERRO: não achei o token em $TOKEN" >&2
  echo "      Sem ele o binário publica e falha na diarização, na máquina do usuário." >&2
  exit 1
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
  -o "$SAIDA" --nologo -v q

EXE="$SAIDA/MeetingApp.exe"

echo "==> conferindo as duas réguas"
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

printf '    tamanho: %.1f MB (ok)\n    token embutido: sim\n' "$(echo "$bytes/1000000" | bc -l)"

if [[ "${1:-}" == "--so-build" ]]; then
  echo "==> pronto em $EXE (não instalado, a pedido)"
  exit 0
fi

echo "==> instalando em $DESTINO"
cp "$SAIDA/MeetingApp.exe" "$DESTINO/"
ls -la "$DESTINO/MeetingApp.exe"

# Os sidecars vão junto. Eles não estão dentro do .exe — moram em motores/, que
# tem 4,3 GB e não se reempacota a cada mudança —, então sem esta cópia um
# motor.py corrigido aqui continua o antigo na máquina de quem usa. É um erro
# silencioso: o app abre, e só a operação alterada se comporta como antes.
if [[ -d "$DESTINO/motores" ]]; then
  echo "==> sincronizando os sidecars"
  for m in asr diarizacao modelos; do
    mkdir -p "$DESTINO/motores/$m"
    cp "$RAIZ/motores/$m/motor.py" "$DESTINO/motores/$m/"
    echo "    motores/$m/motor.py"
  done
fi

echo
echo "Instalado. Agora abra o app e veja — é a parte que nenhum script faz."
