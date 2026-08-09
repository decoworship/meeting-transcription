# Fase 1 — onde parou e o que falta

Documento de passagem de bastão. A carta de execução é a
[FASE1.md](FASE1.md); aqui está o estado real contra ela, em 08/08/2026.

Branch: `feat/recorder-and-accuracy`. Último commit desta fase: ver
`git log --oneline recorder-net/`.

---

## 1. Estado em uma frase

O gravador nativo **funciona, cabe no orçamento e está validado em uso real**:
captura duas faixas, sobrevive a crash, identifica a reunião pela agenda, tem o
menu com paridade sobre o `tray.py` e pesa 14,9 MB. O que falta para fechar a
fase é uma reunião de verdade gravada em paralelo com o Python — e o Python
sair de uso.

---

## 2. O que está pronto

### Requisitos da seção 3 da carta

| # | requisito | estado |
|---|---|---|
| 3.1 | âncora no relógio do dispositivo (QPC), correção em trecho silencioso | ✅ |
| 3.2 | WAV crash-safe (header reescrito a cada 10 s) | ✅ |
| 3.3 | checagem de disco no start + falha de escrita promove o ícone | ✅ |
| 3.4 | instância única (mutex nomeado) | ✅ |
| 3.5 | contador de amostras descartadas no `meta.json` | ✅ |
| 3.6 | loopback sem áudio preenchido com silêncio ancorado no relógio | ✅ |
| 3.7 | desconexão detectada, avisada e marcada no `meta.json` | ✅ |
| 3.8 | pasta padrão sem caminho `\\wsl$` hardcoded | ✅ com desvio registrado na carta |

### Critérios de aceite

| | estado |
|---|---|
| A. paridade de captura | ✅ aprovado, com conteúdo real |
| B. kill -9 no meio da gravação | ✅ verificado com kill de verdade |
| C. soak de 1h+ | 🟡 OK parcial — 20 min, 72 correções de âncora |
| D. disco cheio | 🟡 OK parcial — guarda implementada e testada, sem teste de volume cheio |
| E. ≤ 25 MB | ✅ **14,9 MB** — era 154,8 MB; ver §3 |

### Além da carta

- **Google Calendar** portado sem o SDK do Google (REST + OAuth loopback com
  PKCE, ~600 linhas). Usa o mesmo `google_token.json` do gravador Python, e a
  interop foi verificada nas duas direções com credenciais reais.
- **Menu com paridade** sobre o `tray.py`: submenus de microfone e áudio do
  sistema, pasta, calendário, notificações (requisito A14).
- **Ícone próprio** nos dois executáveis.
- 86 testes, todos passando.

---

## 3. O WinForms morreu: 154,8 MB → 14,9 MB

**O que era.** O `Tray` usava `UseWindowsForms=true` por causa do `NotifyIcon`.
Isso arrastava o framework `Microsoft.WindowsDesktop.App` inteiro e recusava
trimming (`NETSDK1175`): o gravador que cabe em 12,6 MB no CLI voltava para
154,8 MB só por causa da casca.

**O que é agora** — a troca prescrita no PLANO §5, feita item a item:

| saiu | entrou |
|---|---|
| `NotifyIcon` | `Shell_NotifyIcon` + `NOTIFYICON_VERSION_4` (`Nativo/IconeDeNotificacao.cs`) |
| `ContextMenuStrip` | `CreatePopupMenu`/`AppendMenuW`/`TrackPopupMenuEx` (`Nativo/MenuNativo.cs`) |
| `ShowBalloonTip` | `NIF_INFO` + `szInfo`/`szInfoTitle`/`dwInfoFlags` |
| `Forms.Timer` | `SetTimer` → `WM_TIMER` (`Nativo/JanelaDeMensagens.cs`) |
| `FolderBrowserDialog` | `IFileOpenDialog` com `FOS_PICKFOLDERS` (`Nativo/SeletorDePasta.cs`) |
| `Application.Run` | `GetMessageW`/`TranslateMessage`/`DispatchMessageW` |
| `SynchronizationContext` | `PostMessageW(WM_EXECUTAR)` + fila de ações |
| `IconeDaBandeja` (GDI+) | `CreateIconFromResourceEx` sobre os `.ico` embutidos (`IconesDaBandeja.cs`) |

**Zero mudança no Core** — a estimativa de 300–400 linhas se confirmou, e toda a
lógica de estado continua em `EstadoDaBandeja`, coberta por teste.

### O que a execução ensinou (e a compilação não ensinaria)

- **IL2050 no `SHCreateItemFromParsingName`.** Declarar `out IShellItem` é
  marshalling COM na fronteira do P/Invoke, e o linker não consegue provar que a
  interface sobrevive ao trim — vira **erro**, não aviso. A saída foi passar só
  o `IUnknown*` e resolver o RCW do lado gerenciado, onde o linker enxerga o uso.
  Vale para qualquer P/Invoke com `[MarshalAs(UnmanagedType.Interface)]`.
- **`NIM_ADD` falhando passava em silêncio.** O processo ficava vivo e invisível:
  sem ícone não há menu, nem clique, nem como sair sem o gerenciador de tarefas.
  Hoje a inicialização inteira está num `try` que morre com uma `MessageBox`
  dizendo o motivo.
- **O `$null` do PowerShell não é `NULL`.** No script de validação,
  `FindWindowW("classe", $null)` procura janela de **título vazio** e nunca acha;
  o `$null` é marshalado como string vazia. Custou uma rodada de investigação
  achando que o binário não subia — ele estava lá, com a janela criada.

### Armadilhas que valeram a pena ter mapeado antes

Todas se confirmaram na implementação; ficam registradas para a próxima janela
Win32 que este projeto escrever (a Fase 2 vai precisar de uma).

- **Janela escondida, não *message-only*.** `HWND_MESSAGE` não recebe broadcast,
  e é por broadcast que chega o `TaskbarCreated` — sem ele, um crash do Explorer
  faz o ícone sumir para sempre. Registrado com `RegisterWindowMessage`.
- **`NOTIFYICON_VERSION_4` muda o contrato do callback**: o clique chega como
  `NIN_SELECT` e o botão direito como `WM_CONTEXTMENU`, com o evento em
  `LOWORD(lParam)` e as coordenadas no `wParam`. Código que trata `WM_RBUTTONUP`
  funciona na V3 e falha em silêncio na V4.
- **`NIF_SHOWTIP` é obrigatório na V4**, senão o tooltip padrão não aparece.
- **`SetForegroundWindow` antes do `TrackPopupMenuEx`**, e `PostMessage(WM_NULL)`
  depois, senão o menu não fecha ao clicar fora.
- **Menu do Win32 não quebra linha**: o status virou uma linha por item
  desabilitado.
- **Manter a referência gerenciada do `WndProc` viva** (é campo, não local).
- **`[STAThread]`** continua necessário por causa do COM do `IFileOpenDialog`.

---

## 4. Pendências

1. **Fluxo OAuth interativo não verificado por execução.** O caminho de
   *refresh* — o do dia a dia — está provado contra a API real. O de autorização
   inicial abre navegador e não dá para exercitar sem um clique humano. Falta
   alguém abrir o menu → Google Calendar → "Conectar conta...".
2. **A UI nova não foi vista por olhos humanos.** A validação automatizada
   cobre janela, ícone, timer, captura e `meta.json`; **menu, balão de
   notificação e diálogo de pasta exigem interação** e não foram exercitados.
   São exatamente as três peças reescritas do zero. Checagem de 2 minutos:
   botão direito no ícone (menu e submenus aparecem, fecham ao clicar fora),
   "Escolher outra pasta..." (o diálogo abre na pasta atual), e um clique no
   ícone durante a gravação (ícone fica laranja).
3. **Sugestão fora de escopo, registrada para não se perder:** o `disconnected`
   novo no `meta.json` não é lido por ninguém ainda. O lugar natural é
   `Recording.avisos()` em `src/web/recordings.py`, junto de `no_audio` e
   `usable_pct` — enquanto o app Gradio for a ferramenta de produção, é lá que o
   aviso seria visto.

---

## 5. Fechamento da fase

A definição de pronto da carta é **o gravador Python aposentado**. Para chegar
lá:

1. Gravar uma reunião real com os dois em paralelo (fecha o critério C de
   verdade e valida o calendário em uso). A ferramenta existe:
   `tools/comparar_gravadores.py --segundos N`.
2. Comparar `meta.json` campo a campo, como no critério A.
3. Aposentar o `recorder/` Python.

Dois números para observar nessa validação:

- O `desalinhamento entre faixas` que o CLI imprime é a **diferença de
  comprimento** entre as faixas, não alinhamento temporal
  ([`Cli/Program.cs`](../recorder-net/Cli/Program.cs), busca por
  `desalinhamento`). Em gravação com nada tocando, a faixa `system` sai 100%
  silêncio sintetizado e o número mede o preenchimento, não a captura — deu
  18–44 ms nesses casos, contra 1,7 ms medidos com conteúdo nas duas faixas.
- Nas duas gravações de validação da bandeja nativa (sala silenciosa, `system`
  100% sintetizado) essa diferença deu **254,8 ms em 19,93 s** (4077 amostras) e
  **278,7 ms em 14,95 s** (4459 amostras) — acima dos 18–44 ms anteriores. O que
  esses dois pontos dizem: o número **não cresce com a duração**, e a gravação
  mais curta deu a diferença maior. Isso é a assinatura de um offset de partida
  constante (~4 mil amostras, o `system` começando a preencher silêncio depois
  do `mic`), não de deriva — que é justamente a distinção que o
  `comparar_gravadores.py` mede por correlação em janelas. Dois pontos não são
  uma série: **confirmar com conteúdo real nas duas faixas** antes de concluir
  que não é nada.

---

## 6. Como validar um build

```bash
export PATH="$HOME/.dotnet:$PATH"

# testes
dotnet test recorder-net/Tests/MeetingRecorder.Tests.csproj

# CLI
dotnet publish recorder-net/Cli/MeetingRecorder.Cli.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -o <saida>

# bandeja — agora com as mesmas flags do CLI, inclusive PublishTrimmed
dotnet publish recorder-net/Tray/MeetingRecorder.Tray.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -o <saida>
```

**`--self-contained true` é obrigatório na linha de comando** — o `.csproj` tem
`SelfContained=false` para o loop de desenvolvimento ficar rápido. Publicar sem
a flag gera um executável de ~190 KB que pede o .NET Runtime na máquina do
usuário. Isso já aconteceu uma vez.

E a regra que esta fase aprendeu caro: **medir tamanho sem executar não vale
nada**. O primeiro binário trimado tinha 11,9 MB e morria na primeira linha,
porque o trim completo desliga o COM embutido e sem COM o WASAPI não inicializa.
Todo build vai para uma execução real:

```bash
# CLI: --list e uma gravação curta que produza meta.json
# bandeja: o mesmo, sem ninguém olhando a tela
powershell.exe -ExecutionPolicy Bypass -File tools/validar_bandeja.ps1 \
  -Exe C:\...\MeetingRecorder.exe -Segundos 20
```

O `validar_bandeja.ps1` lança o `.exe` publicado, espera a janela aparecer,
dispara o clique pelo contrato da `NOTIFYICON_VERSION_4`, grava e sai pelo
`WM_CLOSE` — que é o caminho por onde o `meta.json` é escrito. Ele falha em voz
alta se o processo morrer, se a janela não aparecer ou se a saída travar. O que
ele **não** cobre está na pendência §4.2.
