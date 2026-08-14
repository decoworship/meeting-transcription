# Meeting Transcription

Aplicativo Windows nativo que **grava** reuniões em duas faixas e as
**transcreve** com separação de falantes, tudo local e sem nuvem.

Um executável, um ícone na bandeja, uma janela. Publicado sob a
[licença MIT](LICENSE).

## O que ele faz

**Grava**, pela bandeja ou pela janela:

- duas faixas em separado — microfone e áudio do sistema (WASAPI loopback) —,
  alinhadas por construção e ancoradas no relógio de parede;
- mute que escreve silêncio em vez de interromper a escrita, para as faixas não
  se deslocarem uma em relação à outra;
- WAV que sobrevive a desligamento no meio da reunião;
- medidores de nível ao vivo e aviso de microfone mudo esquecido;
- identifica a reunião pelo Google Calendar e usa os participantes como
  vocabulário na transcrição.

**Transcreve**, na mesma janela:

- faster-whisper com aceleração por GPU, e pyannote para separar quem falou;
- reconhece pessoas entre reuniões por impressão vocal;
- vocabulário e preferências por cliente/projeto, com correção fonética a
  jusante — o termo é recuperado mesmo quando o modelo erra a grafia;
- áudio sincronizado com o texto, edição de trecho e de falante;
- exporta em TXT / SRT / VTT / DOCX.

## Como é feito

| pasta | o quê |
|---|---|
| `app-net/` | o aplicativo: C# (.NET 8) com a interface em WebView2 |
| `motores/` | os três sidecars Python (ASR, diarização, modelos), falando JSON por stdin/stdout |
| `assets/ds/` | o AA Design System, embutido no executável |
| `tools/` | as ferramentas de medição — é como este projeto se mede |
| `src/` | o Python que as ferramentas importam: os motores de referência e o formato dos arquivos em disco |
| `docs/` | o projeto é doc-driven; comece pelo [PLANO.md](docs/PLANO.md) |

Não há mais interface Python: o app Gradio e o gravador Python foram aposentados
em 13/08/2026, depois que o app nativo os substituiu integralmente. Ver
[FASE2.5-HANDOFF.md](docs/FASE2.5-HANDOFF.md).

## Compilar e publicar

Requer o SDK do .NET 8. Publica do WSL ou do Windows.

```bash
export PATH="$HOME/.dotnet:$PATH"

dotnet test app-net/Tests/MeetingApp.Tests.csproj

# publica, confere as réguas e instala
tools/publicar.sh --destino /mnt/c/Users/andre/MeetingApp
```

O `tools/publicar.sh` recusa publicar um binário que falhe em qualquer uma das
três réguas: tamanho mínimo (as flags de publicação pegaram), token do
HuggingFace embutido, e os ícones da bandeja embutidos. Cada uma existe por um
defeito que chegou ao usuário.

Os motores (`motores/python`, ~4,3 GB de Python embarcado) são montados à parte
por `tools/empacotar_motores.sh` e ficam ao lado do executável. **Ainda não há
instalador** — é a Fase 4.

## O que vem a seguir

| fase | o quê |
|---|---|
| **3** (corrente) | notas de reunião, transcrição que sobrevive à navegação, e a **ata por LLM** local — [docs/FASE3.md](docs/FASE3.md) |
| **4** | o instalador (Inno Setup), com os modelos baixados na primeira execução |
| **5** | acabamento visual sobre o AA Design System — [docs/PLANO.md](docs/PLANO.md) §3 |

## HuggingFace

A diarização e a impressão vocal usam modelos pyannote com termos de uso. Quem
compila o app precisa de um token com os termos aceitos; quem **recebe** o app
não precisa de nada, porque o token vai embutido no binário.

1. Crie um token em https://huggingface.co/settings/tokens (escopo Read basta).
2. Aceite os termos, logado, nos dois modelos:
   - https://huggingface.co/pyannote/speaker-diarization-community-1
   - https://huggingface.co/pyannote/wespeaker-voxceleb-resnet34-LM
3. Salve o token em `%USERPROFILE%\.meeting-recorder\hf_token.txt`. O
   `publicar.sh` o embute e confere que ele entrou.

Diagnóstico de acesso: `tools/diagnosticar_acesso_hf.py`.

## Ferramentas de medição

Precisam do ambiente Python (`uv sync`), que existe só para elas:

| ferramenta | o que mede |
|---|---|
| `benchmark_wer.py` | erro de palavra do ASR |
| `benchmark_der.py` | erro de diarização |
| `benchmark_vocab.py` | ganho do vocabulário por projeto |
| `comparar_gravadores.py` | as faixas de dois gravadores, amostra a amostra |
| `comparar_pipeline.py` | o pipeline C# contra o Python, saída a saída |
| `medir_layout.py` | se a interface cabe na janela, num navegador de verdade |
| `validar_bandeja.ps1` | executa o binário publicado e exige o `meta.json` como prova |
