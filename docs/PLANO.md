# Plano de trabalho

Três frentes, em ordem de dependência: validar o áudio gravado, melhorar o
gravador, redesenhar a interface.

---

## 0. Bloqueio: o microfone da gravação de 06/08 está mudo

A gravação `2026-08-06_10-31-03` tem a faixa do microfone **95,3% em silêncio**:

```
mic.wav     2204s, 2099s de zeros exatos
            maior trecho: 1400s (23 min) a partir de 0:81
            segundo:       690s (11,5 min) a partir de 24:57
system.wav  saudável, 0,5% de zeros
```

São **zeros exatos**, não fala baixa. Duas causas possíveis:

1. **Mute pela bandeja.** O clique no ícone agora muta em vez de parar. Um
   clique para "conferir o estado" muta a faixa por toda a reunião.
2. **Mute no hardware** do headset AN01, ou o dispositivo capturado em modo
   exclusivo por outro app.

O `meta.json` não denuncia nenhuma das duas: diz `no_audio: false`, porque o
canal produziu áudio nos primeiros 81 segundos e o campo só marca "nunca teve
áudio". **Essa é uma falha de instrumentação, não só de operação** — a
gravação parecia saudável pelos metadados.

### Correções necessárias antes da próxima gravação

- Registrar no `meta.json` o **maior trecho silencioso** e o **tempo total
  mudo** por faixa, não apenas o booleano.
- Registrar quanto tempo a gravação passou **mutada pela bandeja**, separando
  mute deliberado de canal morto.
- Avisar na bandeja quando o mute passar de N minutos — mute esquecido é agora
  o modo de falha mais provável, dado o novo comportamento do clique.

---

## 1. Testes de validação do áudio gravado

### O que esta gravação consegue e não consegue validar

| pergunta | possível hoje? |
|---|---|
| O áudio do sistema do gravador serve tão bem quanto o do OBS? | **sim** |
| A deriva de clock aguenta 36 minutos? | **sim** |
| As duas faixas melhoram a atribuição de falante? | não — mic morto |
| A minha voz precisa de supressão de ruído? | não — mic morto |

### Teste A — gravador vs OBS (executável agora)

Mesma reunião, duas capturas independentes: `system.wav` do gravador e a faixa
de áudio do MP4 do OBS. Transcrever ambas com config idêntica e comparar.

Métricas, todas sem verdade de referência:

- cobertura: contagem de palavras, número de segmentos, maior lacuna
- acertos de vocabulário: nomes do projeto e jargão conhecidos
- vazamento de idioma: trechos em inglês/espanhol no meio do português
- divergência: alinhar por tempo e extrair só os pontos onde discordam

Critério de decisão: se o gravador empatar ou ganhar, o OBS sai de cena. Se
perder, investigar antes de abandonar o OBS.

### Teste B — deriva ao longo de 36 minutos (executável agora)

Correlacionar `system.wav` com o áudio do MP4 do OBS em janelas ao longo da
gravação e medir o deslocamento em cada uma. Se o deslocamento crescer ao longo
do tempo, a âncora de relógio não está segurando em duração real — só foi
verificada em 5 minutos até agora.

### Teste C — duas faixas vs faixa única (precisa de gravação boa)

Processar a mesma reunião pelos dois caminhos e comparar a atribuição de
falante com o proxy já construído: quanto tempo de fala fica atribuído ao
falante errado porque o segmento atravessa turnos.

Também: quantos segmentos o `assign_owner` reivindica e quantos estão certos.
É o que calibra o `OWNER_MARGIN` (hoje 2.0, escolhido por raciocínio, não
medido).

### Teste D — supressão de ruído (precisa de gravação boa)

**Não adicionar processamento por fé.** O método:

1. medir o piso de ruído do `mic.wav` nos trechos em que você está calado
2. se o piso for desprezível, não há o que suprimir — encerrar aqui
3. se não for, transcrever a faixa crua e a tratada, comparar

Candidatos, do mais barato ao mais caro: gate por energia, `afftdn` do ffmpeg,
RNNoise, DeepFilterNet.

Critério: só entra se melhorar métrica de transcrição. Supressão agressiva
costuma comer consoantes e piorar o resultado.

Nota: com fone (o seu caso) o vazamento acústico do sistema para o microfone é
mínimo, então cancelamento de eco provavelmente é desnecessário. Confirmar
medindo a correlação entre as faixas nos trechos em que só o sistema fala.

### Próxima gravação: o que fazer diferente

Uma reunião em que **você fale bastante**. A de 06/08 não serviria nem com o
microfone funcionando — sem fala sua não há o que avaliar em atribuição de
falante nem em supressão de ruído.

---

## 2. Melhorias no gravador

### 2.1 Integração com o Teams

Espelhar o estado de mute do Teams no gravador, para não existirem dois mutes
independentes na cabeça de quem usa.

**Mecanismo:** WebSocket local em `ws://127.0.0.1:8124` — a mesma API que
plugins de Stream Deck usam. Ela reporta estado por eventos, incluindo
`isMuted` e se há reunião ativa.

**Escopo:**

- conectar ao subir; token emitido pelo Teams na primeira conexão, com prompt
  de pareamento que você aprova uma vez, e persistido
- assinar os eventos de estado e espelhar o mute na faixa do microfone
- indicar na bandeja se a ponte está ativa, para o estado do ícone ser legível
- reconectar sozinho quando o Teams reiniciar

**Degradação:** sem Teams, sem token ou sem resposta, o gravador funciona
exatamente como hoje, com o mute manual. A ponte é bônus, nunca dependência.

**Riscos:** só Teams novo; API não documentada pela Microsoft, pode mudar entre
versões; só reporta estado dentro de uma reunião.

**Teste:** exige reunião real. Verificar mute nos dois sentidos, entrada e
saída da reunião, e o Teams fechando no meio da gravação.

### 2.2 Integração com o Google Calendar

Registrar, no início da gravação, qual reunião da agenda está acontecendo.

**Escopo:**

- OAuth de aplicativo desktop, escopo somente leitura
  (`calendar.readonly`), token guardado em `%USERPROFILE%\.meeting-recorder`
- ao iniciar, localizar o evento que cobre o instante atual (ou o mais próximo
  dentro de ±15 min)
- preencher o `meta.json`, que já tem os campos reservados: `title`,
  `attendees`, `calendar_event_id`
- se houver mais de um candidato, escolher pela bandeja em vez de adivinhar

**O ganho que fecha um ciclo:** os participantes do evento alimentam o
vocabulário customizado do transcritor. O caso "Dimi → Jimmy" que originou toda
essa investigação deixa de depender de alguém lembrar de digitar o nome.

**Mapeamento para cliente/projeto:** o app já guarda configurações por projeto,
incluindo o vocabulário. Uma regra simples (por participante, por domínio de
e-mail ou por palavra no título) pode sugerir o projeto; confirmação manual na
primeira vez, memorizada depois.

**Degradação:** falha de rede, token expirado ou nenhum evento encontrado nunca
podem impedir ou atrasar a gravação. A associação com o calendário é
posterior e opcional — inclusive editável depois no app.

---

## 3. Redesign da interface com o AA Design System

### O que o design system é

CSS puro com custom properties, um único `styles.css` como entrada, fontes
auto-hospedadas (Fraunces e Hanken Grotesk), tema escuro por
`data-tema="escuro"`, tokens em `tokens.json`. Componentes em React ou como
classes CSS. Português como língua padrão. Sem biblioteca de ícones.

### O conflito estrutural

O app é Gradio, que gera o próprio HTML e traz o próprio CSS. Isso limita o
alcance do redesign, mas de forma desigual:

- **Blocos de HTML escritos à mão** (transcrição, cartões de falante, barra de
  etapas, cabeçalho) — controle total, fidelidade total possível
- **Widgets do Gradio** (dropdowns, sliders, accordions, upload) — dá para
  tematizar via tokens e CSS, mas o DOM é dele; alguns detalhes não se dobram
- **Componentes React do design system** — inutilizáveis dentro do Gradio

### Estratégia em três fases

**Fase 1 — tokens e tema.** Trazer `tokens.css` e as fontes para dentro da
imagem (o CSP e o modo offline impedem depender de CDN) e mapear para um
`gr.themes.Base` customizado: cores, tipografia, raios, espaçamentos. Risco
baixo, ganho visual grande, nada de estrutura muda.

**Fase 2 — blocos próprios.** Reescrever o HTML que já é nosso usando as
classes do design system: a transcrição, os cartões de falante, a barra de
etapas, o cabeçalho, o painel de tempos. É onde a identidade de fato aparece.

**Fase 3 — decidir sobre o Gradio.** Com as fases 1 e 2 prontas, avaliar quanto
ainda destoa. Se for pouco, parar. Se incomodar, aí sim considerar uma
interface própria (FastAPI servindo estático + os componentes React do design
system), sabendo que é reescrever a camada de UI inteira.

> **A decisão da seção 5 antecipa essa escolha.** Empacotar como app nativo
> Windows tira o Gradio de cena de qualquer forma — a fase 3 deixa de ser
> opcional e vira pré-requisito. Em compensação os componentes React do design
> system, hoje inutilizáveis dentro do Gradio, passam a ser a via normal. Fazer
> as fases 1 e 2 sabendo que a 3 vem depois muda a prioridade: vale investir
> nos blocos de HTML próprios (fase 2, que sobrevive ao porte) e economizar em
> tematizar widget do Gradio (fase 1, que será jogado fora).

### Decisões necessárias antes de começar

1. **Idioma.** A interface hoje é inglês; o design system é português-primeiro,
   inclusive nos nomes de token e no tom de voz. Traduzir a UI para pt-BR faz
   parte do redesign ou fica para depois?
2. **Como consumir o design system.** Submódulo git, cópia versionada dentro do
   repositório, ou pacote publicado? A imagem Docker precisa dos arquivos
   embutidos de qualquer forma.
3. **Tema escuro.** O app hoje segue o tema do Gradio. Passar a expor o toggle
   `data-tema` do design system, ou seguir a preferência do sistema?
4. **Ordem.** Antes ou depois das integrações do gravador? O redesign é o item
   mais longo e o que menos muda a qualidade da transcrição.

---

## 4. Correções pequenas

- **Campo de data não preenche ao escolher uma gravação.** O
  `extract_date_from_filename` está ligado apenas ao `file_input`; o seletor de
  gravações não dispara nada. O nome da pasta (`2026-08-06_10-31-03`) já traz a
  data, e o `meta.json` traz `recorded_at` — a segunda fonte é melhor, por ser
  o instante real e não o nome do arquivo.
- **`user_label` não persiste** entre sessões, volta para "You" a cada reload.

---

## 5. Empacotamento: um app Windows nativo, sem Python

### A decisão

**Rota escolhida: um instalador único, sem runtime Python, sem Docker, sem
WSL.** O que a máquina alvo precisa ter: Windows e, para acelerar, driver
NVIDIA. Nada mais — nem toolkit CUDA, nem Python, nem biblioteca instalada
previamente.

Isso não é uma mudança de empacotamento. É uma **troca de stack**: sai
torch/pyannote/Gradio, entram bibliotecas nativas que carregam os mesmos
modelos em formatos leves. É a decisão mais cara deste documento e a única que
inviabiliza voltar atrás barato — por isso o registro do porquê vem antes do
como.

### Por que não dá para chegar lá pelo caminho Python

Três medições encerram o assunto:

| medida | valor |
|---|---|
| `.venv` do transcritor | **7,8 GB** |
| wheel do torch para Linux (traz CUDA junto) | 888 MB |
| wheel do torch para Windows no PyPI | 241 MB — **e é CPU-only** |

O terceiro item é o que mais surpreende. Os pacotes `nvidia-*-cu12` estão todos
marcados `sys_platform == 'linux'` no `uv.lock`: um `uv sync` no Windows entrega
CPU silenciosamente, sem erro. Para ter GPU seria preciso o índice
`download.pytorch.org/whl/cu128`, mais `nvidia-cublas-cu12` e `nvidia-cudnn-cu12`
instalados à mão, mais `os.add_dll_directory()` antes de importar o ctranslate2 —
que no Windows não embute cuBLAS nem cuDNN e falha com
`Could not locate cudnn_ops64_9.dll`. Some a isso congelar torch, numba,
lightning e Gradio (que lê o próprio código-fonte em runtime) com PyInstaller.

Fim da linha: ~4 GB de bundle, montado sobre três camadas de gambiarra de DLL.

### O gravador de hoje já é a prova do problema

A pergunta "o gravador é nativo?" tem resposta desconfortável: **não é**. O
bundle atual pesa **186 MB** para gravar dois WAVs de 16 kHz:

| | |
|---|---|
| `googleapiclient` | **99 MB** (documentos de descoberta de *toda* API do Google) |
| `numpy.libs` | 21 MB |
| `PIL` | 13 MB (o pystray desenha o ícone) |
| `cryptography` + OpenSSL | 17 MB |
| `python312.dll` | 6,7 MB |
| tcl/tk | 8 MB (o seletor de pasta) |

**Não, não deveria ser assim.** A captura em si é `IAudioClient` com
`AUDCLNT_STREAMFLAGS_LOOPBACK` — uma API do próprio Windows. O que o Python
adiciona ali é o custo de chegar até ela: PyAudioWPatch para falar WASAPI, numpy
para mexer em buffers, PIL para desenhar um ícone de 16×16, tkinter para abrir um
seletor de pasta, e o interpretador para segurar tudo.

Em C# isso vira: `NAudio` (`WasapiRecorder` com `WithLoopbackCapture()`, ou o
`WasapiLoopbackCapture` clássico), `Shell_NotifyIcon` para a bandeja,
`IFileOpenDialog` para a pasta, `Google.Apis.Calendar.v3` para a agenda.
Estimativa: **~15 MB**, contra 186 MB — e sem `python312.dll`.

O que **não** pode se perder no porte, porque é o valor real do `capture.py`:

- **âncora no relógio de parede** com inserção/descarte de amostras — a deriva
  medida é de +0,10% e +0,145%, ou seja 3,7 s e 5,2 s por hora sem correção;
- **mute escreve silêncio**, não interrompe a escrita;
- **instrumentação de silêncio** por faixa (maior trecho, total mudo, tempo
  mutado) — a lição da gravação de 06/08 na seção 0;
- Atenção a uma armadilha do NAudio: em loopback, **sem áudio tocando o evento
  `DataAvailable` não dispara**. O laço de escrita ancorado no relógio depende
  de escrita contínua; o porte precisa preencher esses buracos explicitamente.

### A stack nativa: os mesmos modelos, formatos leves

O ponto que torna a rota viável é que **os modelos que o app usa hoje já existem
em formato nativo**, convertidos e redistribuídos:

| hoje | nativo | tamanho |
|---|---|---|
| faster-whisper (CTranslate2 + torch) | **whisper.cpp** via `Whisper.net` | modelo GGML: 574 MB (turbo q5_0) / 874 MB (turbo q8_0) |
| pyannote segmentação (torch + lightning) | **sherpa-onnx** `pyannote-segmentation-3.0` | **7 MB** |
| `pyannote/wespeaker-voxceleb-resnet34-LM` | **o mesmo modelo em ONNX** (`wespeaker_en_voxceleb_resnet34_LM.onnx`) | **26,5 MB** |
| `voices.py` (fingerprint de voz) | `SpeakerEmbeddingManager` do sherpa-onnx | — |
| Gradio | HTML/CSS/JS próprio dentro de **WebView2** | — |
| PyAudioWPatch + pystray | NAudio + `Shell_NotifyIcon` | — |

Dois ganhos que não são de tamanho:

- **Some a dependência do HuggingFace.** O sherpa-onnx redistribui os modelos
  convertidos como assets de release no GitHub. Token, aceite de termos e o
  401/403 na primeira execução — hoje a maior fricção de onboarding, com guia
  próprio de 5 KB — deixam de existir. *(Verificar as licenças de
  redistribuição antes de embutir; a segmentação pyannote é MIT mas gated.)*
- **A GPU deixa de ser um problema de instalação.** O `Whisper.net` seleciona o
  runtime sozinho, em ordem: CUDA 13 → CUDA 12 → Vulkan → CPU. As DLLs do cuBLAS
  vêm dentro do pacote. Driver NVIDIA presente, roda em CUDA; ausente, cai para
  Vulkan (serve AMD e Intel) ou CPU. **Sem toolkit CUDA na máquina do usuário.**

### Orçamento de tamanho

Números medidos dos pacotes reais (comprimidos):

| componente | tamanho |
|---|---|
| App .NET self-contained, trimmed | ~50 MB |
| `Whisper.net.Runtime.Cuda.Windows` | 142 MB |
| `Whisper.net.Runtime` (CPU, fallback) | 18 MB |
| `Whisper.net.Runtime.Vulkan` (opcional) | 37 MB |
| `org.k2fsa.sherpa.onnx.runtime.win-x64` | 8 MB |
| ffmpeg.exe (só para importar vídeo) | ~80 MB |
| **instalador** | **~300 MB** |
| modelos, baixados na 1ª execução | ~900 MB |

**~1,2 GB no total, contra 7,8 GB de `.venv` mais 5–10 GB de cache do
HuggingFace hoje.** Uma ordem de grandeza.

Três economias merecem nota: o whisper.cpp **quantizado** é o que corta o
modelo de 3,1 GB para 574–874 MB; a diarização inteira cabe em **33 MB** de
ONNX no lugar de torch+lightning+speechbrain; e o `ffmpeg` só é necessário para
importar vídeo — gravação própria já sai em WAV 16 kHz mono, então dá para
adiar ou tornar opcional.

### O que já está pronto e não precisa ser escrito

Antes de escolher linguagem, vale saber o que existe. Há três níveis de reúso,
do mais conservador ao mais radical.

**Nível 1 — o motor como processo separado ("estilo LM Studio").**

O release oficial do whisper.cpp para Windows já inclui **`whisper-server.exe`
(0,7 MB)**, com API HTTP compatível com a da OpenAI. O pacote inteiro para CPU
tem 8,2 MB e traz DLLs despachadas por arquitetura de CPU, mais
`whisper-cli.exe`, VAD e `whisper-quantize.exe`.

**Isto elimina a camada mais arriscada do plano.** Não é preciso escrever
binding de FFI para nada: o app sobe um processo filho e conversa HTTP com ele.
Consequências diretas:

- a escolha de linguagem do app deixa de estar acoplada à do motor;
- trocar de motor (whisper.cpp → sherpa-onnx → Parakeet) vira mudar de endereço;
- o motor pode morrer sem levar a gravação junto — o isolamento de processo que
  o plano já queria vem de graça;
- dá para validar o motor por `curl` antes de existir uma linha de UI.

Existe também o **OWhisper**, do time do Hyprnote, anunciado como "Ollama para
speech-to-text", com `pull`/`run` de modelos. Conceito certo, mas **status
duvidoso**: a página de produto responde 404, o domínio `hyprnote.com` agora
redireciona para `char.com`, e o crate de CLI não está mais na árvore do
repositório. Ficar com o `whisper-server` oficial é mais seguro.

**Nível 2 — bibliotecas que resolvem exatamente as partes difíceis.**

| biblioteca | licença | o que entrega |
|---|---|---|
| [`pyannote-rs`](https://github.com/thewh1teagle/pyannote-rs) | MIT | **os dois modelos que usamos hoje** — segmentation-3.0 + wespeaker-voxceleb-resnet34-LM — em ONNX, com DirectML no Windows. "1 hora de áudio em menos de 1 minuto na CPU" |
| `sherpa-onnx` | Apache-2.0 | mesmo papel, muito mais mantido, com bindings C#/Rust/Go oficiais |
| `Whisper.net` | MIT | caminho C# para o whisper.cpp, com seleção automática de runtime |

O `pyannote-rs` é o achado mais direto: faz exatamente o que o `speaker_diarizer.py`
e o `voices.py` fazem, com os mesmos pesos. Ressalva: **último commit em
setembro de 2025**. O anarlog o consome fixado num rev de git — copiar esse
padrão (vendorizar e fixar) em vez de depender do crate publicado.

**Nível 3 — aplicativos inteiros, para bifurcar ou saquear.**

| projeto | ★ | licença | stack | relevância |
|---|---|---|---|---|
| [anarlog](https://github.com/fastrepl/anarlog) (ex-Hyprnote) | 8,9k | MIT | Rust + Tauri | gêmeo funcional: notetaker local, mic + áudio do sistema, whisper local, diarização local |
| [meetily](https://github.com/Zackriya-Solutions/meetily) | 28,4k | MIT | Rust | atas de reunião self-hosted com whisper.cpp |
| [vibe](https://github.com/thewh1teagle/vibe) | 7,0k | MIT | Rust + Tauri | transcrição de arquivo com diarização, CUDA/Vulkan, gestão de modelos |

O dado que encerra a discussão sobre viabilidade: **o anarlog publica
`anarlog-windows-x86_64-setup.exe` com 115,7 MB**, atualizado em 06/08/2026. Um
notetaker local completo, com whisper e diarização, cabe em 116 MB. Não é
teoria.

**Mas não bifurcar o anarlog.** São 7.131 arquivos e centenas de crates,
incluindo adaptadores para ~40 provedores de STT em nuvem, autenticação,
assinatura e sincronização — superfície demais para herdar. E o projeto trocou
de nome duas vezes em um ano (Hyprnote → anarlog; hyprnote.com → char.com):
base instável para se apoiar. **É almoxarifado e implementação de referência,
não fundação.** O que vale copiar dele, arquivo por arquivo:

- `crates/audio-actual/src/speaker/windows.rs` — loopback WASAPI resolvido;
- `crates/pyannote-local` — diarização ONNX pronta (é o `pyannote-rs` embrulhado);
- `crates/aec` — cancelamento de eco, que é o Teste D da seção 1;
- a configuração de empacotamento do Tauri, que é o que produz os 115,7 MB.

**O que nenhum deles resolve** — e por isso o projeto continua existindo:
gravação em **duas faixas** com âncora de relógio, `assign_owner`, vocabulário
por projeto, impressão digital de voz entre reuniões, e o histórico. Vibe e
anarlog capturam áudio do sistema, mas **misturam** com o microfone. O gravador
continua sendo nosso; o que se copia é o encanamento do WASAPI, não o desenho.

### A arquitetura que isso permite: motores como plugins

O achado do sidecar (nível 1) não é só uma economia de trabalho — ele sugere a
forma do produto. **O app deixa de conter IA e passa a orquestrá-la.**

```
  nucleo (o app)                        motores (features)
  ─────────────────                     ──────────────────────────
  gravacao em 2 faixas                  transcricao   whisper.cpp
  config e projetos            ◄────►   diarizacao    sherpa-onnx
  organizacao das notas                 resumo/ata    LLM local
  historico e busca                     (futuros...)
  integracao com calendario
```

O núcleo é o que **não** existe pronto em lugar nenhum e é onde está o valor
acumulado: as duas faixas com âncora de relógio, o vocabulário por projeto, a
impressão digital de voz, o histórico, e a integração com o Google Calendar —
que nem o Vibe nem o Meetily têm, e que hoje já alimenta o vocabulário com os
participantes do evento.

Cada motor é uma **pasta auto-contida**: binário, modelos, e um manifesto que
declara nome, versão, o que sabe fazer, o que exige de hardware e como ser
iniciado. O app baixa, verifica, sobe como processo filho e conversa por
stdin/stdout (ver "Correção: stdin/stdout, não HTTP" adiante). Nunca linka,
nunca importa.

O que essa separação compra:

- **instalador base pequeno.** O núcleo sem motor nenhum é dezenas de MB. O
  usuário escolhe o que baixar — e quem só quer gravar não baixa modelo algum.
- **acrescentar resumo e ata vira acrescentar um motor**, não mexer no app. É a
  feature que o Meetily tem e nós não; com esse desenho ela entra sem
  refatoração.
- **escolher o motor na UI** — que é o que agradou no Meetily — deixa de ser
  um `if` e passa a ser a consequência natural do desenho. O app já tem a
  semente disso: engine de transcrição e modelo de diarização já são
  configuráveis.
- **GPU decidida por motor.** Transcrição na GPU, diarização na CPU, resumo na
  GPU quando couber — sem um único processo tentando reservar tudo.
- **atualizar motor sem atualizar app**, e vice-versa.
- **Linux e Mac depois ficam viáveis.** Os motores já são multiplataforma; só o
  núcleo precisa de porte. É o que torna "Windows primeiro" uma decisão de
  ordem, não uma dívida.

Ordem de trabalho decorrente: **Windows primeiro, com o núcleo e dois motores**
(transcrição e diarização). Resumo por LLM e outras plataformas vêm depois, sem
reabrir o que já estiver pronto.

Falta desenhar, e é a decisão de projeto mais importante desta seção: **o
contrato do manifesto e da API do motor**. Errar aqui custa caro, porque todo
motor futuro herda o erro. Vale escrevê-lo depois de ter dois motores rodando —
dois pontos definem a reta; um ponto só define uma abstração inventada.

### Como o Meetily empacota (e por que não copiar tudo)

Vale estudar porque é o que mais se parece com o alvo, e porque o resultado é
impressionante: **o instalador do Windows tem 43,3 MB** (`.exe` NSIS; o `.msi`
tem 70,1 MB). Como:

| decisão | consequência |
|---|---|
| **modelos baixados em runtime** | o instalador não carrega nenhum GGML |
| **`whisper-rs` linkado estaticamente** | sem processo separado, sem DLL solta |
| **backend escolhido em tempo de compilação** | `cfg!(feature = "cuda")` — um binário por acelerador |
| **`llama-helper` e `ffmpeg` como sidecars** | `externalBin` do Tauri; o `build.rs` os baixa na hora do build |
| **registro de modelos = tabela em Rust** | nome → URL do HuggingFace + tamanho esperado, com detecção de corrupção por tamanho de arquivo |
| **segundo motor via ONNX** | `parakeet_engine/` espelha `whisper_engine/`, com `whisper_provider.rs` e `parakeet_provider.rs` atrás de uma abstração comum |

A tela de modelos que agradou é exatamente isso: a tabela em Rust alimentando um
componente React, com botão de download por modelo e barra de progresso.

**A armadilha:** o build do Windows que eles distribuem é

```toml
[target.'cfg(target_os = "windows")'.dependencies]
whisper-rs = { version = "0.13.2", features = ["raw-api"] }
```

— sem `cuda`, sem `vulkan`, sem `openblas`. **O instalador de 43 MB é CPU
apenas.** Para ter GPU no Windows o usuário precisa recompilar do zero com
`--features cuda`. Os 43 MB são lindos porque não há CUDA dentro deles.

Isso conflita frontalmente com o requisito daqui, que é justamente GPU no
Windows sem o usuário compilar nada. Copiar o empacotamento do Meetily herdaria
o problema.

**E é aqui que a arquitetura de motores como pacotes se paga.** Linkar
estaticamente força a escolha do acelerador para dentro do build: querer CUDA e
Vulkan e CPU vira três instaladores. Com o motor sendo uma **pasta baixável**, o
acelerador vira mais um atributo do pacote:

```
motores/
  whisper-cuda12/     <- baixado se houver driver NVIDIA
  whisper-vulkan/     <- baixado se houver AMD/Intel
  whisper-cpu/        <- sempre disponível, o fallback
```

O app detecta a GPU na primeira execução e baixa o pacote certo — o mesmo
mecanismo que já baixa modelo, e sem instalador separado. O Meetily não pode
fazer isso porque linkou; nós ainda podemos escolher.

O que **vale copiar**, tal e qual:

- a **tabela de modelos** com URL e tamanho esperado, e a detecção de corrupção
  comparando o tamanho baixado com o esperado — simples e eficaz;
- a **abstração de provider** (`whisper_provider` / `parakeet_provider`), que é
  a forma concreta do que a seção anterior descreve em abstrato;
- o download de binários no `build.rs` em vez de versioná-los no repositório;
- a assinatura de código do Windows via `signCommand`, que eles já resolveram.

### O motor de resumo do Meetily é o modelo a seguir — não o de transcrição

A parte mais útil de estudar o Meetily não é como ele empacota o whisper, e sim
como empacota o **LLM de resumo**. São abordagens opostas dentro do mesmo app:

| | transcrição | resumo |
|---|---|---|
| forma | `whisper-rs` **linkado** | `llama-helper`, **binário separado** |
| acelerador | fixo no build do app | fixo no build **do sidecar** |
| trocar de acelerador | recompilar o app inteiro | trocar só o sidecar |
| ciclo de vida | junto com o app | processo próprio, com keep-alive e timeout de ociosidade |

O `llama-helper` é um crate à parte, com `llama-cpp-2` e as mesmas features
`cuda`/`vulkan`/`metal` — mas por ser um **binário separado**, dá para publicar
uma variante por acelerador sem tocar no app. **É exatamente a arquitetura de
motores como pacotes**, já validada em produção por eles. Só não foi aplicada ao
whisper, que é o caminho legado.

Conclusão prática: **copiar o desenho do motor de resumo e aplicá-lo a todos os
motores**, transcrição inclusive.

O `summary_engine/sidecar.rs` merece leitura direta antes de escrever o nosso. O
que ele resolve, e que a gente teria que descobrir doendo:

- **keep-alive com timeout de ociosidade** — carregar um modelo de 2,6 GB a cada
  requisição é inviável; o processo fica quente e morre sozinho depois de um
  tempo parado;
- **desligamento gracioso com contagem de requisições ativas** (guarda RAII) —
  não matar o processo no meio de um resumo;
- **monitoramento de saúde**, para detectar sidecar morto e resubir;
- **`CREATE_NO_WINDOW` no Windows**, senão cada spawn pisca um console preto — o
  app já faz isso no `extractor.py`.

#### Correção: stdin/stdout, não HTTP

Antes ficou registrado que o app conversaria com os motores por HTTP. O Meetily
usa **stdin/stdout com protocolo por linha**, e é melhor para este caso:

- **nenhuma porta** para alocar, e portanto nenhum conflito com outro app;
- **nenhum aviso do Firewall do Windows** — um servidor HTTP local dispara o
  diálogo de permissão na primeira execução, o que num app de bandeja parece
  malware;
- a morte do processo é detectável na hora, pelo *pipe* fechado, em vez de por
  *timeout*;
- nada escuta em rede, então não há superfície de ataque local.

O `whisper-server.exe` continua útil como **caminho de validação** (dá para
testar por `curl` antes de existir app), mas o motor definitivo deve falar por
*pipe*. Vale checar se o `whisper-server` tem modo stdio; se não tiver, um
wrapper fino resolve.

#### Para quando chegar a vez do resumo

O registro deles serve de ponto de partida — modelos GGUF do HuggingFace, com
`display_name`, `size_mb` e `context_size` por modelo:

| modelo | tamanho | contexto |
|---|---|---|
| Gemma 3 1B (Fast) | 1019 MiB | 32k |
| Qwen 3.5 2B (Balanced) | 1221 MiB | 32k |
| Gemma 3 4B | 2374 MiB | 32k |
| Qwen 3.5 4B (High Quality) | 2614 MiB | 32k |

Dois detalhes que não são óbvios e eles já resolveram: **presets de amostragem
por modelo** (o Qwen de resumo usa amostragem não-gulosa com controle de
repetição; o Gemma usa outro) e **`summary/templates/`**, que separa o formato
da ata do código — é o que permitiria ter modelos de ata diferentes por cliente,
casando com o vocabulário por projeto que já existe.

Nota de dimensionamento para a RTX 2060 de 6 GB: um modelo de 4B em Q4_K_M
(~2,6 GB) **não cabe junto** com o whisper `large-v3` carregado. Os dois motores
sendo processos separados resolve isso naturalmente — resume-se depois de
transcrever, com a VRAM já liberada.

### Escolha de plataforma: C# / .NET

Não por gosto, mas porque **todas as peças existem como NuGet de primeira
classe**: `Whisper.net` 1.9.1, `org.k2fsa.sherpa.onnx` 1.13.4 (com exemplo
`offline-speaker-diarization` pronto), `NAudio`, `Microsoft.Web.WebView2`,
`Google.Apis.Calendar.v3`. Rust/Tauri geraria binário menor, mas a captura
WASAPI com seleção de dispositivo e correção de deriva viraria trabalho manual,
e o OAuth do Google é pior servido.

Com o motor rodando como sidecar HTTP (nível 1 acima), essa escolha pesa bem
menos do que parecia: o que a linguagem precisa entregar bem é **WASAPI,
bandeja, WebView2 e OAuth do Google** — não inferência. Rust/Tauri produziria
binário menor e daria acesso direto ao `pyannote-rs`, ao custo de escrever a
captura à mão. C# permanece a recomendação por causa do `NAudio` e do
`Google.Apis`, mas deixou de ser uma decisão de sentido único.

**Publicar como self-contained + trimmed + single-file, não NativeAOT.**
WinForms não é suportado sob NativeAOT, e o AOT economizaria ~30 MB sobre um
payload de 400 MB de CUDA — não paga a incompatibilidade. Self-contained já
satisfaz o requisito: **nenhum .NET instalado na máquina alvo**.

Dependências residuais, a serem declaradas com honestidade:

- **WebView2** — pré-instalado no Windows 11 e distribuído ao Windows 10 via
  Edge. Ainda assim, embutir o bootstrapper no instalador.
- **VC++ runtime** — exigido pelo onnxruntime e pelo whisper.cpp. Distribuir as
  DLLs ao lado do executável (app-local).
- Instalador: **Inno Setup**. Assinatura de código fica como item à parte —
  sem ela, SmartScreen reclama.

### Os riscos reais, que são de qualidade e não de empacotamento

Esta seção existe porque o branch se chama `feat/recorder-and-accuracy`. Trocar
a stack mexe justamente no que se estava tentando melhorar.

1. **Diarização pode piorar.** O app usa `community-1`, mais novo que o 3.1. O
   sherpa-onnx oferece `pyannote-segmentation-3.0` e `reverb-diarization-v1/v2`
   — **não há ONNX do community-1**. *Mitigação:* o desenho de duas faixas já
   reduz a dependência da diarização — tudo no `mic.wav` é você, com certeza, e
   o pyannote só separa os outros. Mas é preciso **medir antes de migrar**, com
   o mesmo proxy da seção 1 (Teste C).
2. **Quantização e português.** Q5_0 custa ~1% de WER; a recomendação corrente é
   ficar em **q8_0 para trabalho multilíngue**, porque a degradação é maior em
   idiomas com menos representação no treino. Default sugerido:
   `large-v3-turbo-q8_0` (874 MB). Bônus: no RTX 2060 de 6 GB o turbo cabe
   folgado, onde o `large-v3` fp16 (~4,7 GB) hoje aperta.
3. **Alinhamento por palavra.** O WhisperX alinha com um modelo dedicado; o
   whisper.cpp usa DTW sobre os pesos de atenção (`--dtw`), menos maduro e
   dependente de heads de alinhamento por modelo. Se o player sincronizado
   depender de timestamp fino, medir antes.
4. **Os perfis de voz precisam ser refeitos.** O `voices.json` guarda embeddings
   como listas de float. O modelo ONNX é *o mesmo* wespeaker, mas a exportação
   não produz embeddings bit a bit idênticos — os perfis salvos devem ser
   descartados e reinscritos. Barato agora, caro depois de meses de uso.
5. **A UI é reescrita.** 1.916 linhas de `gradio_app.py` viram HTML/JS servido
   ao WebView2. É o maior custo isolado do plano — mas converge com a fase 3 da
   seção 3, que já estava na mesa.

### Fases

Ordenadas para que cada uma entregue algo sozinha e para que a decisão de
qualidade venha **antes** do trabalho irreversível.

**Fase 0 — medir antes de migrar.** Sem escrever app nenhum: baixar
`whisper-bin-x64.zip` (8,2 MB) e os modelos ONNX de diarização, rodar
`whisper-cli.exe` e `sherpa-onnx` sobre gravações já existentes e comparar com a
saída atual, usando as métricas da seção 1. Decide os itens 1–3 dos riscos.
*Se a diarização regredir demais, o plano inteiro é reconsiderado aqui, tendo
gasto dias e não semanas.*

**Fase 1 — o gravador nativo.** Portar `capture.py` + `tray.py` para C#. É a
metade menor (~900 linhas), tem critério de aceite objetivo (gravar em paralelo
com o gravador Python e comparar deriva e faixas amostra a amostra) e entrega
valor sozinha: 186 MB → ~15 MB, sem `python312.dll`. Serve de aprendizado da
stack antes do porte grande.

**Fase 2 — o motor de transcrição, como processo separado.** Embutir
`whisper-server.exe` como sidecar e falar HTTP com ele, em vez de escrever
binding. Diarização por `sherpa-onnx` ou `pyannote-rs` vendorizado. Atrás da
mesma interface de hoje (`BaseTranscriber`), produzindo o mesmo
`TranscriptionResult`. Validável por `curl`, antes de existir UI.

**Fase 3 — a UI em WebView2.** Reescrever a interface com o AA Design System, aí
sim com os componentes React disponíveis. Maior esforço, menor risco técnico.

**Fase 2.5 — um app só.** ✅ **Concluída em 13/08/2026.** Juntou o gravador e o
app de transcrição num executável, com bandeja e janela no mesmo processo. **Não
estava neste plano** e foi acrescentada em 12/08/2026, quando o dono do produto
notou a lacuna: as fases 1 e 2 entregaram dois programas que não se conhecem.
Carta em [FASE2.5.md](FASE2.5.md), estado final em
[FASE2.5-HANDOFF.md](FASE2.5-HANDOFF.md).

> Ela **não** entregou o instalador, e isso empurra trabalho para a Fase 4: o
> app fundido se instala copiando um `.exe` e uma DLL numa pasta, com o
> `motores/` de 4,3 GB montado à parte. O que a 2.5 fechou desse assunto foi a
> migração das duas configurações, que era a metade que arriscava o usuário
> reconfigurar coisas.

**Fase 4 — instalador.** Inno Setup, download dos modelos na primeira execução,
migração do `%USERPROFILE%\.meeting-transcription` existente. Assinatura de
código se e quando sair da própria máquina.

**O Docker sobrevive até a fase 2 provar seu valor em reunião real.** Ele é hoje
o caminho de GPU mais confiável que existe no projeto, e aposentá-lo cedo tira
a rede de segurança.

### Decisões pendentes

1. **Turbo ou large-v3?** O turbo é 4–8× mais rápido e cabe folgado em 6 GB, mas
   é mais fraco em multilíngue. Só a fase 0 responde, em português e com áudio
   real de reunião.
2. **Vulkan entra?** +37 MB no instalador para funcionar em máquina sem NVIDIA.
   Se o alvo é só a sua máquina, não paga; se o app for distribuído, paga.
3. **ffmpeg embutido ou opcional?** Só serve para importar vídeo. Um segundo
   instalador "com suporte a vídeo" evita 80 MB no caso comum.
4. **O histórico migra?** `history/`, `projects.json` e `voices.json` são JSON e
   portáveis — exceto os embeddings (risco 4). Migrar tudo menos as vozes, ou
   começar limpo?
5. **Sidecar ou biblioteca?** O `whisper-server.exe` custa um processo filho e
   um contrato HTTP; a biblioteca embutida custa binding e acopla o motor ao
   ciclo de vida do app. A fase 0 já usa os binários — se o desempenho e a
   ergonomia agradarem ali, a resposta se dá sozinha.
