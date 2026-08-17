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

**As fases 0 a 4 estão concluídas.** O app se instala
([docs/FASE4.md](docs/FASE4.md)), grava, transcreve, separa falantes e escreve a
ata — e já rodou na máquina de outra pessoa. **A fase corrente é a 5**, o
acabamento visual sobre o AA Design System (`docs/PLANO.md` §3); depois vem a 6,
qualidade da transcrição ([docs/FASE6.md](docs/FASE6.md)).

**A dívida aberta que atravessa as duas:** não há rota de atualização. Existe
gente com 0.1.0 instalado e nenhum jeito de saber que saiu versão nova —
[docs/FASE4-HANDOFF.md](docs/FASE4-HANDOFF.md) §6.1 desenha os três degraus. O
número que decide o desenho: o `.exe` tem 18,5 MB e os motores têm 4,1 GB, então
quase toda versão nova é um arquivo de 18,5 MB.

**Uma tarefa da Fase 6 já começou:** transcrever as reuniões **em paralelo por
outras fontes** (Notion, Teams/Meet, o app sem `hotwords`) e guardar as saídas
junto da gravação. A Fase 6 nasceu de uma comparação dessas e hoje se apoia em
**uma** reunião; sem corpus ela calibraria um default com *n* = 1.

## Comandos

```bash
export PATH="$HOME/.dotnet:$PATH"

dotnet test app-net/Tests/MeetingApp.Tests.csproj      # 289 testes
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

**Nunca publique com `dotnet publish` na mão.** As três flags
(`--self-contained`, `PublishSingleFile`, `PublishTrimmed`) e o segredo do Google
são obrigatórios, e cada um deles já saiu faltando num binário entregue ao
usuário. O `publicar.sh` confere as réguas antes de copiar.

**Gerar uma versão nova** é editar `<Version>` em `app-net/Directory.Build.props`
(um lugar só), escrever o `CHANGELOG.md` e rodar o `montar_instalador.sh`. O
`AppId` do `.iss` nunca muda — é por ele que o Windows sabe que é atualização e
não um segundo programa. Se algum `motor.py` mudou, rode o `publicar.sh` antes:
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
