# Fase 1 — onde parou e o que falta

Documento de passagem de bastão. A carta de execução é a
[FASE1.md](FASE1.md); aqui está o estado real contra ela, em 08/08/2026.

Branch: `feat/recorder-and-accuracy`. Último commit desta fase: ver
`git log --oneline recorder-net/`.

---

## 1. Estado em uma frase

O gravador nativo **funciona e está validado em uso real**: captura duas
faixas, sobrevive a crash, identifica a reunião pela agenda e tem o menu com
paridade de features sobre o `tray.py`. O que falta é tamanho (critério E) e
uma validação longa.

---

## 2. O que está pronto

### Requisitos da seção 3 da carta

| # | requisito | estado |
|---|---|---|
| 3.1 | âncora no relógio do dispositivo (QPC), correção em trecho silencioso | ✅ |
| 3.2 | WAV crash-safe (header reescrito a cada 10 s) | ✅ |
| 3.3 | checagem de disco no start | ✅ parcial — ver §4 |
| 3.4 | instância única (mutex nomeado) | ✅ |
| 3.5 | contador de amostras descartadas no `meta.json` | ✅ |
| 3.6 | loopback sem áudio preenchido com silêncio ancorado no relógio | ✅ |
| 3.7 | desconexão de dispositivo detectada e avisada | ✅ parcial — ver §4 |
| 3.8 | pasta padrão sem caminho `\\wsl$` hardcoded | ✅ com desvio — ver §4 |

### Critérios de aceite

| | estado |
|---|---|
| A. paridade de captura | ✅ aprovado, com conteúdo real |
| B. kill -9 no meio da gravação | ✅ verificado com kill de verdade |
| C. soak de 1h+ | 🟡 OK parcial — 20 min, 72 correções de âncora |
| D. disco cheio | 🟡 OK parcial — guarda implementada e testada, sem teste de volume cheio |
| E. ≤ 25 MB | ❌ CLI 12,6 MB passa; bandeja 154,8 MB não — **é o trabalho principal restante** |

### Além da carta

- **Google Calendar** portado sem o SDK do Google (REST + OAuth loopback com
  PKCE, ~600 linhas). Usa o mesmo `google_token.json` do gravador Python, e a
  interop foi verificada nas duas direções com credenciais reais.
- **Menu com paridade** sobre o `tray.py`: submenus de microfone e áudio do
  sistema, pasta, calendário, notificações (requisito A14).
- **Ícone próprio** nos dois executáveis, conferido por hash de conteúdo contra
  o `assets/logo.ico`.
- 84 testes, todos passando.

---

## 3. O trabalho principal restante: matar o WinForms

**O problema.** O `Tray` usa `UseWindowsForms=true` por causa do `NotifyIcon`.
Isso arrasta o framework `Microsoft.WindowsDesktop.App` inteiro e recusa
trimming (`NETSDK1175`). O gravador que cabe em 12,6 MB no CLI volta para
154,8 MB só por causa da casca — e, de quebra, é o que faz o executável exigir
o **.NET Desktop Runtime** quando publicado framework-dependent, erro que já
apareceu uma vez na validação.

**A correção**, já prescrita no PLANO §5:

| sai | entra |
|---|---|
| `NotifyIcon` | `Shell_NotifyIcon` (`NIM_ADD`/`MODIFY`/`DELETE`) |
| `ContextMenuStrip` | `CreatePopupMenu` + `AppendMenuW` + `TrackPopupMenuEx` |
| `ShowBalloonTip` | `NIF_INFO` + `szInfo`/`szInfoTitle`/`dwInfoFlags` |
| `Forms.Timer` | `SetTimer` → `WM_TIMER` |
| `FolderBrowserDialog` | `IFileOpenDialog` com `FOS_PICKFOLDERS` |
| `Application.Run` | `GetMessageW`/`TranslateMessage`/`DispatchMessageW` |
| `SynchronizationContext` | `PostMessageW(WM_EXECUTAR)` + fila de ações |
| `IconeDaBandeja` (GDI+) | `CreateIconFromResourceEx` sobre os `.ico` embutidos |

Estimativa: 300–400 linhas de costura, **zero mudança no Core** — toda a lógica
de estado já está em `EstadoDaBandeja`, coberta por teste. Resultado esperado:
14–18 MB self-contained trimado, critério E passa com folga.

### O que já foi feito desta troca

- **`recorder-net/Tray/Nativo/Win32.cs`** — as declarações de P/Invoke
  (janela, bandeja, menu, ícone, `MessageBox`). Compila; **ainda não está
  ligado**, o `Programa` continua no WinForms.
- **`tools/gerar_icone.py`** — agora também gera
  `assets/bandeja-{cinza,vermelho,laranja,amarelo}.ico`, em 16/20/24/32 px.
  Substituem o tingimento em runtime com GDI+, que era a última dependência de
  `System.Drawing` da bandeja.
- **`AllowUnsafeBlocks`** ligado no `.csproj` (exigido pelo gerador do
  `LibraryImport`).

### O que falta escrever

1. `Nativo/JanelaDeMensagens.cs` — classe de janela, `WndProc`, laço de
   mensagens, `SetTimer` de 1 s, e a fila de ações para `WM_EXECUTAR`.
2. `Nativo/IconeDeNotificacao.cs` — `Shell_NotifyIcon` com `NOTIFYICON_VERSION_4`,
   troca de ícone, tooltip e balão.
3. `Nativo/MenuNativo.cs` — construtor de menu com submenus, itens marcados,
   desabilitados e separadores, mapeando id de comando para `Action`.
4. `Nativo/SeletorDePasta.cs` — `IFileOpenDialog` via `ComImport`.
5. `IconesDaBandeja.cs` — parsear o `ICONDIR` dos `.ico` embutidos, escolher o
   tamanho por `GetSystemMetrics(SM_CXSMICON)` e criar o `HICON` com
   `CreateIconFromResourceEx`. Guardar em cache e `DestroyIcon` no fim.
6. Reescrever `Program.cs` sobre isso, e tirar `UseWindowsForms`,
   `IncludeNativeLibrariesForSelfExtract` e o `EmbeddedResource` do
   `logo-256.png` do `.csproj`.

### Armadilhas conhecidas, para não serem redescobertas

- **Janela escondida, não *message-only*.** Uma janela `HWND_MESSAGE` não
  recebe broadcast, e é por broadcast que chega o `TaskbarCreated` — a
  mensagem que avisa que o Explorer reiniciou e que o ícone precisa ser
  readicionado. Sem isso, um crash do Explorer faz a bandeja sumir para sempre.
  Registrar com `RegisterWindowMessage("TaskbarCreated")`.
- **`SetForegroundWindow` antes do `TrackPopupMenuEx`**, e `PostMessage(WM_NULL)`
  depois. Sem isso o menu não fecha ao clicar fora.
- **Menu do Win32 não quebra linha.** O item de status hoje concatena o título
  do evento com `\n`; virar dois itens desabilitados.
- **`szTip` tem 128 chars** na estrutura V2 (o limite de 63 era da V1). O código
  atual corta em 62 por causa do WinForms; dá para afrouxar.
- **Manter a referência gerenciada do `WndProc` viva**, senão o GC a coleta e o
  processo morre em uma callback do Windows.
- **`[STAThread]`** continua necessário por causa do COM do `IFileOpenDialog`.

---

## 4. Pendências menores

1. **Requisito 3.3, segunda metade.** Falha de escrita hoje notifica, mas não
   promove o ícone para WARNING — a cor continua vermelha. `Atualizar()` em
   `Program.cs` calcula `CanalSemAudio` só a partir de `JaOuviu`; basta incluir
   `FalhaDeEscrita`. ~10 linhas.
2. **Requisito 3.7.** Desconexão é detectada e avisada, mas não é marcada no
   `meta.json`. O gravador Python também não marca, então é feature nova, não
   regressão. Pede um campo novo em `MetaTrack` (acrescentar é seguro; remover
   ou renomear não). ~15 linhas.
3. **Requisito 3.8 — desvio a confirmar.** Nem o Python nem o C# perguntam a
   pasta na primeira execução; ambos caem em um padrão (aqui,
   `Documentos\MeetingRecordings`). O que a carta queria evitar era o
   `\\wsl$\...` hardcoded, e isso morreu. Julgamento de quem implementou:
   default + menu é melhor que um modal no primeiro start. Se o dono do produto
   discordar, é rápido.
4. **Fluxo OAuth interativo não verificado por execução.** O caminho de
   *refresh* — o do dia a dia — está provado contra a API real. O de
   autorização inicial abre navegador e não foi possível exercitá-lo do
   ambiente de desenvolvimento. Só falta alguém clicar em "Conectar conta...".

---

## 5. Fechamento da fase

A definição de pronto da carta é **o gravador Python aposentado**. Para chegar
lá:

1. Gravar uma reunião real com os dois em paralelo (fecha o critério C de
   verdade e valida o calendário em uso).
2. Comparar `meta.json` campo a campo, como no critério A.
3. Aposentar o `recorder/` Python.

Um número para observar nessa validação: o `desalinhamento entre faixas` que o
CLI imprime é a **diferença de comprimento** entre as faixas, não alinhamento
temporal ([`Cli/Program.cs`](../recorder-net/Cli/Program.cs), busca por
`desalinhamento`). Em gravações curtas com nada tocando, a faixa `system` sai
100% silêncio sintetizado e o número mede o preenchimento de silêncio, não a
captura — deu 18–44 ms nesses casos, contra 1,7 ms medidos com conteúdo nas duas
faixas. Vale conferir com áudio real antes de tirar conclusão.

---

## 6. Como validar um build

```bash
export PATH="$HOME/.dotnet:$PATH"

# testes
dotnet test recorder-net/Tests/MeetingRecorder.Tests.csproj

# CLI (trimado, este é o que precisa caber no critério E)
dotnet publish recorder-net/Cli/MeetingRecorder.Cli.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -o <saida>

# bandeja (hoje sem trim, ver §3)
dotnet publish recorder-net/Tray/MeetingRecorder.Tray.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o <saida>
```

**`--self-contained true` é obrigatório na linha de comando** — o `.csproj` tem
`SelfContained=false` para o loop de desenvolvimento ficar rápido. Publicar sem
a flag gera um executável de ~190 KB que pede o .NET Desktop Runtime na
máquina do usuário. Isso já aconteceu uma vez.

E a regra que esta fase aprendeu caro: **medir tamanho sem executar não vale
nada**. O primeiro binário trimado tinha 11,9 MB e morria na primeira linha,
porque o trim completo desliga o COM embutido e sem COM o WASAPI não
inicializa. Todo build vai para uma execução real: `--list` e uma gravação
curta que produza `meta.json`.
