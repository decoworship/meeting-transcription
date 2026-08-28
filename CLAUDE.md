# CLAUDE.md

Guia para o Claude Code (claude.ai/code) trabalhar neste repositório.

## O que é

Aplicativo Windows nativo que grava reuniões em duas faixas e as transcreve com
separação de falantes. **Um executável** (`MeetingApp.exe`) que é bandeja e
janela ao mesmo tempo: C#/.NET 8 com a interface em WebView2, e os modelos
rodando em sidecars Python.

O projeto é **doc-driven**. Antes de mexer em qualquer coisa não trivial, leia
`docs/` — as cartas de fase (`FASE1.md`, `FASE2.md`, `FASE2.5.md`, `FASE3.md`)
dizem o que se pretendia, e os `*-HANDOFF.md` dizem o que de fato aconteceu e
por quê. Muito comentário no código aponta para eles.

**As fases acabaram em 27/08/2026, e o trabalho vive no
[docs/BACKLOG.md](docs/BACKLOG.md)** — features e bugs por tema, cada um com id
estável e com o gatilho que justifica fazê-lo. As cartas de fase continuam sendo
o registro histórico: é nelas que estão a medição e o raciocínio de cada item, e
o backlog aponta para elas. **A prioridade corrente é o tema 1, interface e
uso.**

**As fases 0 a 6 estão concluídas.** O app se instala
([docs/FASE4.md](docs/FASE4.md)), grava, transcreve, separa falantes, escreve a
ata e abre no tema que a pessoa escolheu — e já rodou na máquina de outra
pessoa. A Fase 5, o acabamento visual sobre o AA Design System, fechou em
18/08/2026 ([docs/FASE5-HANDOFF.md](docs/FASE5-HANDOFF.md)). A Fase 6 fechou em
27/08/2026 ([docs/FASE6.md](docs/FASE6.md), no bloco de abertura): ela entregou
da 0.4.0 à 0.6.1, e o que a matou foi o tamanho, não o conteúdo — uma lista sem
objetivo e sem teto cresce até não se conseguir mais responder *o que faço
agora*. **Qualidade de transcrição, de diarização e de ata saiu do backlog** por
decisão do dono do produto, e é tratada fora dele.

**A [Fase 7](docs/FASE7.md) é estudo, não execução**, aberta em 27/08/2026:
transcrever e separar falantes durante a própria reunião. Ela não manda fazer
nada — pergunta o que precisaria ser verdade, e o resultado legítimo é "não vale
a pena", com número. O acervo responde quase tudo offline, alimentando um motor
de streaming com áudio já gravado. **O argumento mais forte contra ela é a
gravação**: hoje o desligamento do §3.0 custa uma transcrição, que a retomada
recupera; ao vivo custaria a reunião.

**O tema mora no `app.json` e é aplicado pelo núcleo**, que reescreve o
`data-tema` do `index.html` enquanto o serve (`App/Conteudo.cs`). Não é
JavaScript: a ponte é assíncrona e o tema chegaria depois da primeira pintura.
O `data-tema="claro"` do HTML é procurado por texto exato — mudá-lo faz o tema
parar de funcionar em silêncio.

**Há um defeito aberto**: o computador do segundo usuário (RTX 4050 Laptop,
16 GB) **desliga sozinho** durante a transcrição — não trava, e não dá tela
azul. O `registro.log` da 0.2.1 mostrou que o **ASR termina bem, na GPU**, e que
o corte vem da **diarização em diante**; isso derrubou as três hipóteses
anteriores (queda para CPU, VRAM, memória do sistema, driver/TDR). Desligamento
seco sob carga de GPU é **corte de energia** — térmica ou entrega —, e
provavelmente não é conserto nosso. É o `SUP-2` do
[docs/BACKLOG.md](docs/BACKLOG.md), **bloqueado aguardando reavaliação na
máquina dele** — o relato é da 0.1.0, e de lá para cá mudaram o pipeline, a
retomada e o registro. A investigação inteira está em
[docs/FASE6.md](docs/FASE6.md) §3.0.

**O que era nosso, a 0.4.0 consertou:** o app jogava fora um ASR que tinha dado
certo, porque o `transcricao.json` só era escrito no fim de tudo.
`Nucleo/Retomada.cs` grava o texto assim que ele existe, marcado com o que falta
(`pending`), e transcrever de novo pula o ASR. **Não retomar é sempre seguro;
retomar o parcial errado devolve o texto de outro modelo em silêncio** — por
isso modelo, idioma e vocabulário são conferidos, e na dúvida o ASR roda de
novo.

**O registro ainda tem um buraco, e ele mudou de lugar** (conferido em
26/08/2026): dos oito `Registro.Escrever` do `Transcritor`, três já correm
depois da diarização, então ela deixa rastro. O mudo agora é o **fim** do
pipeline — e é lá que o reconhecimento de vozes sobe o pyannote pela segunda vez
(`AprendizadoDeVozes.ExtrairAsync`) sem assinar o `AoRegistrar`. São **três**
cargas de GPU em sequência, e a terceira é invisível. **A 0.4.1 ia fechar isso e
nunca saiu** — conferido em 27/08/2026, com a versão em 0.6.1: nenhum dos cinco
instrumentos existe. É o `SUP-1` do [docs/BACKLOG.md](docs/BACKLOG.md), e os dois
mais baratos são de uma linha cada (assinar o `AoRegistrar` no `ExtrairAsync`, e
chamar o `Registro.Ultimas()` que ninguém chama). Ele vem **antes** do `SUP-2`:
pedir a informação ao usuário já custou duas idas e voltas com respostas
erradas.

**O app se chama PulseMeet desde 19/08/2026**, e o símbolo é o monograma M.
Nenhum dos dois está fechado, então **a marca é uma constante só**: `Marca.Nome`
em `Nucleo/Marca.cs` e o `#define Marca` do `.iss`, com um teste que falha se os
dois discordarem. O que carrega o nome antigo por baixo — `AppId`,
`MeetingApp.exe`, a pasta de instalação, o mutex, os namespaces e os
`LogicalName` dos recursos — **não muda nunca**, e cada um quebra algo diferente
se mudar. O símbolo é `assets/logo.svg`, e `tools/gerar_icone.py` gera dele os
seis `.ico`. Tudo em [docs/MARCA.md](docs/MARCA.md). **O winget é o único ponto
com prazo**: enquanto os manifestos não forem submetidos, o `PackageIdentifier`
ainda pode ser trocado de graça.

**A rota de atualização existe no primeiro degrau**: o app avisa que saiu versão
nova, lendo o `versao.json` do próprio repositório — sem servidor
([docs/ATUALIZACAO.md](docs/ATUALIZACAO.md)). Ela é pré-requisito de acrescentar
modelo, porque o catálogo é código: oferecer um modelo novo é publicar uma versão
nova do app. Baixar e trocar o binário sozinho fica para depois da assinatura de
código — mas **o winget faz esse degrau sem que o app aprenda nada**: desde
19/08/2026 o instalador sai como release público, e o manifesto vive em
`instalador/winget/`. Ele ainda não está no `microsoft/winget-pkgs`, e por isso
o `winget upgrade` ainda não funciona: falta separar os motores do instalador,
que é o que torna 1,59 GB submissível e um update de 18 MB possível.

**O corpus paralelo deixou de ser tarefa pendente em 27/08/2026**, junto com a
qualidade. Ele existe — três reuniões com Notion e Gemini/Meet — e é de onde
saiu quase tudo o que se sabe sobre a transcrição e a diarização
([docs/AUDITORIA-ATAS.md](docs/AUDITORIA-ATAS.md) §9). Guardar o export como
`gemini.md` na pasta da gravação continua valendo e não custa nada; só não é
mais trabalho que alguém deva.

## Comandos

```bash
export PATH="$HOME/.dotnet:$PATH"

dotnet test app-net/Tests/MeetingApp.Tests.csproj      # 504 testes
tools/publicar.sh                                       # publica e instala
tools/publicar.sh --so-build                            # só o binário, em dist/publicar
tools/montar_instalador.sh                              # o instalador, 1,59 GB

# montam o que não muda a cada build
tools/empacotar_motores.sh                    # o Python embarcado
tools/empacotar_motor_de_ata.sh               # llama.cpp + GGUF (não vai no instalador)
tools/empacotar_modelos_de_diarizacao.sh      # os 57 MB que substituíram o token

# a interface do disco, para desenhar sem recompilar
MeetingApp.exe --web C:\caminho\para\app-net\App\web

uv sync   # só para as ferramentas de medição em tools/
```

**Os 4,3 GB de motores moram na instalação oficial**, em
`AppData\Local\Programs\MeetingApp\motores`, e em nenhum outro lugar: a pasta
de trabalho `C:\Users\andre\MeetingApp` foi apagada em 18/08/2026 para liberar
disco. Os dois scripts leem de lá. O `publicar.sh` continua instalando na pasta
de trabalho — um build meio pronto não pode cair no app que grava reunião — e a
recria com o executável mais **junções** para os motores oficiais: 18 MB em vez
de 4,3 GB. Apagar essa pasta é seguro; junção não é dona dos bytes.

**Nunca publique com `dotnet publish` na mão.** As três flags
(`--self-contained`, `PublishSingleFile`, `PublishTrimmed`) e o segredo do Google
são obrigatórios, e cada um deles já saiu faltando num binário entregue ao
usuário. O `publicar.sh` confere as réguas antes de copiar.

**Gerar uma versão nova** é editar `<Version>` em `app-net/Directory.Build.props`
(um lugar só), escrever o `CHANGELOG.md`, subir o mesmo número no `versao.json`
— que é o canal do aviso de atualização, ver [docs/ATUALIZACAO.md](docs/ATUALIZACAO.md) —
e rodar o `montar_instalador.sh`. Depois disso vem o release: `gh release create`
com o instalador, os três YAMLs do winget copiados de
`instalador/winget/<versão anterior>` com a versão e o **SHA256 novos**, e o
campo `onde` do `versao.json` apontando para a página do release. O passo a passo
com as armadilhas está em [docs/ATUALIZACAO.md](docs/ATUALIZACAO.md); o manifesto
em si, em [instalador/winget/LEIAME.md](instalador/winget/LEIAME.md). **É o
`git push` que faz o aviso aparecer** na máquina de quem já instalou, então ele
vem depois de o instalador existir. O `AppId` do `.iss` nunca muda — é por ele
que o Windows sabe que é atualização e não um segundo programa, e é dele que sai
o `ProductCode` do manifesto. Se algum `motor.py` mudou, rode o `publicar.sh` antes:
o `--so-build` não sincroniza os sidecars, e a régua reprova o build em vez de
deixar o instalador empacotar o motor velho.

## Arquitetura

```
app-net/
  App/          a janela (WebView2), a ponte, e a bandeja em App/Bandeja/
  Nucleo/       o pipeline de transcrição, projetos, vozes, exportação
  Nucleo/Atas/  as skills, o motor de ata (llama.cpp), o verificador e o redator
  Sidecar/      o protocolo com os motores Python
  Gravacao/     o núcleo do gravador: deriva, WAV, contabilidade de pacotes
  Captura/      WASAPI (Windows-only)
  Agenda/       Google Calendar, OAuth com PKCE
  Cli/          Sidecar.exe — o pipeline por linha de comando
  CliGravador/  Capture.exe — captura sem interface, para medir
  Tests/        as duas suítes, net8.0 portátil
```

**Um processo, dois papéis, um laço de mensagens.** A `JanelaDeMensagens`
(invisível, da bandeja) roda o único `GetMessage`, e ele despacha também a
janela do app. Fechar a janela **esconde**; sair é só pelo menu da bandeja.
Errar isso perde gravação — ver `Aplicacao.cs`.

**A gravação é serviço em processo; os motores são sidecars.** A captura não
tem modelo pesado nem GPU, e não pode pagar a latência de um pipe entre o
clique e o início do áudio. Os motores, que carregam modelo e disputam GPU,
ficam isolados em processos Python que falam JSON por stdin/stdout
(`docs/SIDECAR.md`).

**A ponte** (`App/Ponte.cs` + `web/ponte.js`): a página manda `{id, op, ...}` e
recebe `{id, ...}`. Respostas com `tipo: "progresso"` não encerram o pedido.
**`id: 0` é evento empurrado pelo núcleo** — é como o nível de áudio chega à
tela cinco vezes por segundo sem ninguém perguntar.

## O que não se reabre

Custou caro para acertar e é invisível quando está certo:

- `Gravacao/DriftAnchor.cs` — a âncora no relógio de parede. Já foi trocada por
  uma versão "tecnicamente melhor" e **perdeu em campo**;
- `Gravacao/StreamingResampler.cs` — `sinc_size: 256`. O usuário ouviu um
  craquelado que três métricas objetivas não pegaram;
- `Gravacao/CrashSafeWavWriter.cs`, `PacketTimeline.cs`, `TrackStats.cs`;
- `Captura/WasapiTrackCapture.cs` — inclusive o preenchimento dos buracos
  quando o loopback não dispara por não haver áudio tocando;
- **mute escreve silêncio, não interrompe a escrita.** É o que mantém as duas
  faixas alinhadas.

## Armadilhas medidas

- **`EmbeddedResource` com barra invertida não expande glob no MSBuild em
  Linux.** Compila, publica, passa nos testes, e o recurso não está lá;
- **torch e pyannote escrevem no stdout** e corrompem o protocolo do sidecar —
  os motores duplicam o fd 1 antes de qualquer import;
- **`--no-build` depois de editar** mede o binário velho;
- **`strings | grep -q` com `set -o pipefail`** falha por SIGPIPE;
- **`.ps1` com acento precisa de BOM UTF-8** para o PowerShell 5.1;
- **`cmd.exe` chamado do WSL** precisa de `/s` (senão come as aspas) e de um
  diretório atual que não seja UNC.

## O Python que sobrou

Não há mais interface Python — o Gradio e o gravador Python saíram em
13/08/2026. O que resta serve às ferramentas de medição:

- `src/transcription/`, `src/diarization/`, `src/utils/` — os motores de
  referência que cinco ferramentas de `tools/` importam. **Apagá-los quebra a
  forma como este projeto se mede**;
- `src/web/{recordings,projects,history,voices,exporters}.py` — a referência
  escrita dos formatos em disco que o C# lê e escreve. Vários comentários em
  C# apontam para eles;
- `motores/` — os três sidecars. Rodam no Python embarcado do app, não no
  `.venv` daqui.
