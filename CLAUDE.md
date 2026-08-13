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

**A fase corrente é a 3** ([docs/FASE3.md](docs/FASE3.md)): notas de reunião,
transcrição que sobrevive à navegação, e a ata por LLM local. Depois vêm o
instalador (Fase 4) e só então o acabamento visual (Fase 5, que era a antiga
Fase 3 — ver a reordenação em `docs/PLANO.md`).

## Comandos

```bash
export PATH="$HOME/.dotnet:$PATH"

dotnet test app-net/Tests/MeetingApp.Tests.csproj      # 181 testes
tools/publicar.sh --so-build                            # publica em dist/publicar
tools/publicar.sh --destino /mnt/c/Users/andre/MeetingApp

# a interface do disco, para desenhar sem recompilar
MeetingApp.exe --web C:\caminho\para\app-net\App\web

uv sync   # só para as ferramentas de medição em tools/
```

**Nunca publique com `dotnet publish` na mão.** As três flags
(`--self-contained`, `PublishSingleFile`, `PublishTrimmed`), o token do
HuggingFace e o segredo do Google são obrigatórios, e cada um deles já saiu
faltando num binário entregue ao usuário. O `publicar.sh` confere as réguas
antes de copiar.

## Arquitetura

```
app-net/
  App/          a janela (WebView2), a ponte, e a bandeja em App/Bandeja/
  Nucleo/       o pipeline de transcrição, projetos, vozes, exportação
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
