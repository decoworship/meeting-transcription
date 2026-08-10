# Fase 1 — onde parou e o que falta

Documento de passagem de bastão. A carta de execução é a
[FASE1.md](FASE1.md); aqui está o estado real contra ela, em 08/08/2026.

Branch: `feat/recorder-and-accuracy`. Último commit desta fase: ver
`git log --oneline recorder-net/`.

---

## 1. Estado em uma frase

O gravador nativo **funciona, cabe no orçamento e ganhou do Python em gravação
real**: captura duas faixas, sobrevive a crash, identifica a reunião pela
agenda, tem o menu com paridade sobre o `tray.py`, pesa 14,9 MB — e num soak de
57 min com headset Bluetooth terminou com as faixas a 206,7 ms uma da outra,
contra **17 minutos** de desalinhamento do Python (§6). Falta o clique do OAuth
inicial e a decisão de aposentar o `recorder/`.

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
| A. paridade de captura | ✅ aprovado — 57,4 min em paralelo, ver §6 |
| B. kill -9 no meio da gravação | ✅ verificado com kill de verdade |
| C. soak de 1h+ | ✅ 57,4 min contínuos com headset Bluetooth, ver §6 |
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

A definição de pronto da carta é **o gravador Python aposentado**. Os passos 1 e
2 estão feitos (§6): a reunião em paralelo aconteceu, e o novo ganhou por uma
margem que não deixa dúvida. **Sobra a decisão do dono do produto de tirar o
`recorder/` de uso** — e o clique do OAuth (§4.1), que é a última coisa do
gravador nunca exercitada.

Lembrete de leitura para quem for medir de novo: o `desalinhamento entre faixas`
que o CLI imprime é a **diferença de comprimento** entre elas, não alinhamento
temporal ([`Cli/Program.cs`](../recorder-net/Cli/Program.cs), busca por
`desalinhamento`). Em gravação com nada tocando, a faixa `system` sai 100%
silêncio sintetizado e o número mede o preenchimento, não a captura.

---

## 6. O soak de 57 min, e o que ele decidiu (10/08/2026)

Gravação real de **3442,7 s (57,4 min)** com os dois gravadores em paralelo,
sobre uma reunião reproduzida, **com headset Bluetooth (AN01)** — o caso mais
duro que existe para deriva, e por acaso o que o usuário usa no dia a dia.
Pastas: `2026-08-10_08-08-10` (novo) e `2026-08-10_08-08-08` (Python).

| | novo (C#) | Python |
|---|---|---|
| faixa `system` | 55.079.887 amostras | 38.684.947 |
| **alinhamento entre as faixas** | **206,7 ms** | **17 min 9 s** |
| correções de âncora (sys/mic) | 38.556 / 55.826 | 21 / 168 |
| palavras transcritas em `system` | 6.632 | 6.703 |

**O Python perdeu 17 minutos da faixa do sistema** — 40 min de arquivo para
57 min de reunião. É exatamente o defeito que o requisito 3.1 existe para
corrigir (âncora no relógio de chegada em vez do relógio do dispositivo), agora
medido em escala real em vez de deduzido. O gravador novo terminou com as duas
faixas a 206,7 ms uma da outra.

**Isto também fecha o número que ficou sem explicação na versão anterior deste
handoff**: os 254,8 ms e 278,7 ms das gravações curtas não eram deriva. Em 57
minutos o desalinhamento ficou em 206,7 ms — não cresce com o tempo, é offset
de partida constante, como a hipótese previa. Confirmado com conteúdo real nas
duas faixas e em escala 170× maior.

### O susto: 38 mil correções e 21% menos energia

O novo descartou 11% das amostras de `system` e 16% das de `mic`, e a energia
total capturada ficou 21% abaixo da do Python. O requisito 3.1 manda descartar
em trecho silencioso e **nunca no meio de fala**, então isso precisava de
resposta antes de aprovar o critério A. Duas medições responderam:

1. **Teste com marcadores** (`tools/teste_de_descarte.ps1`): um sinal com um
   bipe a cada 1,000 s exato, tocado e capturado pelo loopback. Resultado:
   **120 bipes de 120**, intervalo médio 1,0003 s, desvio 2,2 ms, deriva total
   de 40 ms em 119 s. Nada descartado. *Ressalva que o próprio teste expôs*:
   ele rodou na saída HDMI do monitor e produziu **1** correção de âncora,
   contra as 38.556 da reunião — mediu um regime fácil, não o difícil.
2. **Contagem de palavras** nas duas faixas `system` reais, que é o regime
   difícil: **6.632 contra 6.703, diferença de 1,1%**. O conteúdo é o mesmo.

Conclusão: **não há perda de fala**. A diferença de energia é compatível com o
filtro anti-aliasing da reamostragem de 48 kHz para 16 kHz — o Python tem mais
energia total *e* pico RMS menor, combinação que aponta para energia espúria de
aliasing no lado dele, não para conteúdo faltando no nosso.

Fica aberto, sem bloquear nada: **por que o Bluetooth exige 38 mil correções**
onde o HDMI exige 1. A explicação provável é o clock livre do A2DP, e a âncora
está fazendo exatamente o que deveria — mas o número nunca foi medido de
propósito. O teste dos bipes repetido com o AN01 conectado fecharia isso com
precisão de milissegundos.

## 7. Como validar um build

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
