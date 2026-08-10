# Fase 1 — onde parou e o que falta

Documento de passagem de bastão. A carta de execução é a
[FASE1.md](FASE1.md); aqui está o estado real contra ela, em 08/08/2026.

Branch: `feat/recorder-and-accuracy`. Último commit desta fase: ver
`git log --oneline recorder-net/`.

---

## 1. Estado em uma frase

O gravador nativo **funciona, cabe no orçamento e grava áudio limpo**: duas
faixas, sobrevive a crash, identifica a reunião pela agenda, menu com paridade
sobre o `tray.py`, 14,9 MB, e 12,6 ms de desalinhamento entre as faixas no
cenário mais duro (headset Bluetooth em mãos-livres). O que falta para
aposentar o `recorder/` Python é **repetir o soak com o binário atual** — a
âncora mudou depois da última medição longa (§6) — e o clique do OAuth inicial.

---

## 2. O que está pronto

### Requisitos da seção 3 da carta

| # | requisito | estado |
|---|---|---|
| 3.1 | deriva corrigida contra o relógio, em trecho silencioso | ✅ **com reversão registrada** — o desenho por carimbo QPC perdeu em campo, ver §6 e a nota na FASE1.md |
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
| A. paridade de captura | 🟡 **refazer** — aprovada em 57,4 min, mas com o binário anterior à troca da âncora (§6) |
| B. kill -9 no meio da gravação | ✅ verificado com kill de verdade |
| C. soak de 1h+ | 🟡 **refazer** — 57,4 min contínuos, mesmo motivo do critério A (§6) |
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

   > **Credenciais embutidas (10/08/2026).** O `google_client_secret.json`
   > deixou de ser obrigatório na máquina de quem usa: o build embute o do
   > perfil de quem publica, e o arquivo local continua tendo precedência para
   > quem já configurou o seu. O repositório segue sem o segredo — o `.csproj`
   > só embute se o arquivo existir, e o caminho é passado por
   > `-p:SegredoDoGoogle=...`. É aceitável porque, em cliente OAuth do tipo
   > "aplicativo instalado", o Google documenta que o segredo não é secreto:
   > quem protege o fluxo é o PKCE, que já está implementado.
   >
   > **O que isso NÃO resolve, e é o que aparece na tela de quem recebe o app:**
   > um projeto em modo *Testing* no Google Cloud só autoriza contas
   > cadastradas como testadoras e **expira todo refresh token em 7 dias**. Para
   > outra pessoa usar o calendário é preciso adicionar o e-mail dela em
   > *OAuth consent screen → Test users*, ou publicar o app — e publicar com o
   > escopo `calendar.readonly` exige passar pela verificação do Google.
2. ~~**A UI nova não foi vista por olhos humanos.**~~ **Resolvido
   (10/08/2026)**: o menu Win32 foi visto funcionando em outra máquina, com
   submenus, item padrão em negrito, marcador em "Notificações" e o estado da
   gravação em duas linhas. Falta só o diálogo de pasta (`IFileOpenDialog`),
   que ninguém abriu ainda.
3. **Sugestão fora de escopo, registrada para não se perder:** o `disconnected`
   novo no `meta.json` não é lido por ninguém ainda. O lugar natural é
   `Recording.avisos()` em `src/web/recordings.py`, junto de `no_audio` e
   `usable_pct` — enquanto o app Gradio for a ferramenta de produção, é lá que o
   aviso seria visto.

---

## 5. Fechamento da fase

A definição de pronto da carta é **o gravador Python aposentado**. O que falta:

1. **Repetir a reunião em paralelo com o binário atual.** A de 57 min (§6)
   rodou antes da troca da âncora, e mede um gravador que não existe mais. Os
   indícios apontam para melhor — 12,6 ms de alinhamento contra 206,7 ms, zero
   descarte contra 11,3 s —, mas indício não é medição.
2. **O clique do OAuth** (§4.1), a última coisa do gravador nunca exercitada.
3. **A decisão do dono do produto** de tirar o `recorder/` de uso.

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

Conclusão: **não há perda de fala** — e essa conclusão estava certa e
**incompleta**, do jeito mais instrutivo possível. Ver a seção seguinte.

### O craquelado: o ouvido achou o que as métricas não achavam

Ao escutar as gravações, o dono do produto ouviu **artefato digital no áudio do
C# e não no do Python** — e lembrou que o mesmo já acontecera na primeira versão
do gravador Python, por descasamento de taxa. Nenhuma das medições anteriores
tinha apontado para lá: contar palavras é cego para artefato (cortes de poucas
amostras não removem palavras, apenas crepitam), e a contagem de
descontinuidades no tempo até favorecia o C#, que tinha *menos* saltos por
segundo que o Python.

Quem achou foi o espectro médio das duas faixas `system` da mesma reunião:

| faixa | C# | Python | diferença |
|---|---|---|---|
| 6000–7000 Hz | −31,1 dB | −38,2 dB | **+7,2** |
| 7000–7500 Hz | −31,6 dB | −58,9 dB | **+27,3** |
| 7500–8000 Hz | −31,7 dB | −67,7 dB | **+36,0** |

O Python desce a −67 dB perto do Nyquist; o C# ficava num **platô plano de
−31 dB** — assinatura de filtro anti-aliasing insuficiente, que se ouve como
chiado de banda larga no agudo.

**A causa** estava no `StreamingResampler`, no comentário que dizia "qualidade
do WDL é suficiente para fala a 16 kHz, o áudio existe para alimentar o Whisper,
não para masterização". A afirmação nunca tinha sido medida. O
`WdlResamplingSampleProvider` é o WDL configurado **sem sinc**, e um tom de
10 kHz — que está acima do Nyquist de 8 kHz e é obrigado a desaparecer —
voltava rebatido em 6 kHz a **−43,3 dB**.

**A correção** foi usar o `WdlResampler` direto, com sinc ligado. O tamanho do
filtro foi escolhido por medição, não por gosto:

| sinc_size | alias em 6 kHz |
|---|---|
| sem sinc (era) | −43,3 dB |
| 64 | −60,5 dB |
| 128 | −68,6 dB |
| **256** | **−109,9 dB** |

Custo: 71,6 µs por bloco de 10 ms, ou 140× tempo real — 0,7% do orçamento de
cada bloco, para duas faixas simultâneas.

Validado por execução, não só por teste unitário: gravado um sinal de ruído
restrito a **9–20 kHz**, que não pode existir num arquivo de 16 kHz. Chegou ao
dispositivo com RMS 0,08 e saiu do resampler como **zero exato** (−137 dBFS,
atenuação acima de 116 dB). O teste
`StreamingResamplerTests.TomAcimaDoNyquistNaoVoltaComoAlias` prende a regressão
em −90 dB.

> **As gravações feitas antes de 10/08/2026 têm o artefato.** O conteúdo é
> aproveitável — as palavras estão lá, como a contagem mostrou —, mas o áudio
> tem chiado no agudo. A reunião de 57 min citada acima é uma delas.

**A lição de método**, que vale mais que o conserto: três medições objetivas
(palavras, descontinuidades, energia total) disseram "está bom", e um ouvido
disse "está ruim" — e o ouvido estava certo. Nenhuma das três media *timbre*.
Quando o usuário relata um sintoma que as métricas não veem, a hipótese certa é
que **falta métrica**, não que falta problema.

### E o craquelado continuou: a segunda causa, que era a principal

Corrigido o resampler, uma gravação nova no mesmo headset **ainda craquejava** —
e a lição acima se repetiu, agora contra o meu próprio diagnóstico. Os números
da gravação `2026-08-10_10-06-09` (56 s, AN01) contra o Python simultâneo:

| | C# | Python |
|---|---|---|
| correções de âncora | **790** | 2 |
| descarte líquido | **−126.400 amostras (7,9 s, 14%)** | +118.920 |
| cliques detectados | 1.207 (21,6/s) | 460 (8,3/s) |

**A causa.** Pacotes descrevem o passado: o carimbo é de quando o hardware
digitalizou, e ele chega depois. O preenchimento ocioso avançava até "agora
menos 200 ms" — margem que cobria a placa integrada e **não cobria o headset
Bluetooth, que entrega com ~400 ms de atraso**. O silêncio era escrito por cima
do tempo que o pacote real ia ocupar; a âncora via a faixa adiantada e
**descartava o áudio verdadeiro** para compensar. Cada descarte, um corte
abrupto no meio da fala.

Reprodução sem hardware, em
`DriftAnchorTests.PreenchimentoOciosoNaoFazAAncoraDescartarAudioReal`: com
400 ms de atraso simulado, a âncora descartava **4,6 s de áudio em 5 s** de
captura, com 460 correções; com 100 ms, zero. O teste roda nos dois regimes.

**A correção**: a margem passou a ser **medida**, não presumida —
`PacketTimeline.MargemOciosa` acompanha o pior atraso já observado entre o
carimbo do pacote e o relógio, com piso de 500 ms (e não de 200, porque a
adaptação só começa depois do primeiro pacote, e até lá uma captura Bluetooth
já perderia áudio). Margem grande demais é inofensiva: só adia a escrita do
silêncio, que o pacote seguinte corrige. Margem pequena demais destrói áudio.

**Medido depois, no mesmo headset Bluetooth** (`tools/teste_de_descarte.ps1`,
125 s):

| | antes | depois |
|---|---|---|
| correções de âncora | 790 em 56 s (14/s) | **6 em 124,7 s (0,05/s)** |
| descarte | 7,9 s (14%) | **0,06 s (0,05%)** |
| marcadores preservados | — | **120 de 120**, intervalo 1,0005 s |

### E ainda não era o fim: o modo mãos-livres

A gravação seguinte do usuário **continuou craquejando**, e o motivo de eu ter
errado duas vezes seguidas estava no método: eu vinha testando com **uma faixa
só**. Com as duas, o headset comuta de A2DP para mãos-livres, e o quadro muda:

| | uma faixa (A2DP) | duas faixas (mãos-livres) |
|---|---|---|
| correções de âncora | 6 em 125 s | **1125 em 70 s** |
| silêncio inserido | ~0 | **13,4 s** |
| áudio descartado | 0,06 s | **11,3 s** |

No mãos-livres os carimbos QPC avançam **11,2 ms para cada 10 ms de áudio
entregue**. Cada pacote parecia deixar um buraco; a faixa recebia silêncio que
não existia, e a âncora descartava áudio real para caber. O desenho ancorado no
carimbo amplificava a irregularidade em vez de absorvê-la.

**A decisão foi do dono do produto**: adotar a solução do gravador Python, que
nunca teve esse problema, e marcar a melhoria para depois. Ver a nota do
requisito 3.1 na [FASE1.md](FASE1.md) — o desenho por carimbo foi revertido, e
o que ficou é o do Python (relógio acumulado, tolerância de 50 ms) mais dois
refinamentos nossos que não custam nada: correção em trecho silencioso quando
há um, e preenchimento por relógio só depois de 1 s sem pacote nenhum, para o
requisito 3.6 continuar valendo.

**Medido depois, mesmo headset, 125 s, duas faixas:**

| | antes | depois |
|---|---|---|
| correções (system / mic) | 1125 / 1109 | **1 / 3** |
| áudio descartado | 11,3 s | **nenhum** (as correções são inserção) |
| cliques por segundo | 11,3 | **0,0** |
| desalinhamento entre faixas | 206,7 ms | **12,6 ms** |

**A lição de método, de novo, e mais cara desta vez:** o teste que eu usava
para validar (uma faixa, sinal sintético) não reproduzia o cenário do usuário
(duas faixas, mãos-livres). Duas correções foram publicadas com base nele, e as
duas eram consertos reais de problemas reais — mas nenhuma era *o* problema. Um
teste que não reproduz o caso do usuário aprova qualquer coisa.

**Confirmado por escuta em 10/08/2026**: áudio limpo. A comutação de perfil do
headset (a queda de volume quando o microfone abre) foi reconhecida como
comportamento do próprio Bluetooth e aceita — inclusive explica variações de
som que antes se atribuíam a algum filtro do Windows.

> ### ⚠ Os critérios A e C precisam ser refeitos
>
> A validação de 57 min que os fechou (§6) rodou com o binário que descartava
> 14% do áudio e craquejava. Os números de lá — 206,7 ms de alinhamento, 6.632
> palavras — descrevem um gravador que **não existe mais**: a âncora foi
> trocada por completo depois daquela medição.
>
> Não é regressão, é o contrário: os indícios apontam para melhor (12,6 ms de
> alinhamento contra 206,7 ms, zero descarte contra 11,3 s). Mas "aponta para
> melhor" não é medição, e a régua da fase é a reunião real em paralelo. **O
> soak precisa ser repetido com o binário atual antes de aposentar o
> `recorder/` Python.** É barato: acontece sozinho na próxima reunião gravada
> com os dois.

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
