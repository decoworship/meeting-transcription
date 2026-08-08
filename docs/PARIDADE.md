# Paridade de qualidade: o que copiar dos apps estudados

A interface vai mudar bastante. Os três motores — **gravação, transcrição,
diarização** — não podem perder qualidade, e de preferência devem ganhar.

Este documento é o levantamento do que anarlog (ex-Hyprnote), Meetily e Vibe já
resolveram nesses três pontos, com origem e avaliação de cada técnica. Serve
como lista de compras para as fases 1 e 2 do [PLANO.md](PLANO.md).

Licenças: os três são MIT. Copiar código é permitido com atribuição.

---

## 1. Gravação

### O que já temos e nenhum deles tem

Vale registrar antes, para não se perder no meio da cópia:

- **duas faixas separadas** (mic e sistema). Vibe e anarlog capturam áudio do
  sistema, mas **misturam** com o microfone. Perdem a informação que o
  `assign_owner` usa;
- **âncora no relógio de parede** com inserção/descarte de amostras. Nenhum
  deles corrige deriva entre dois dispositivos, porque nenhum grava dois;
- **mute que escreve silêncio** em vez de parar a escrita.

Esses três são o núcleo e vão ser portados, não substituídos.

### O que copiar

| técnica | origem | por que importa |
|---|---|---|
| **thread de captura dedicada + ring buffer lock-free** | anarlog `audio-actual/src/speaker/windows.rs` (`ringbuf::HeapRb` + `AtomicWaker`) | evita alocação e lock no caminho de áudio, que é onde nascem os *dropouts* |
| **contador de amostras descartadas** (`dropped_samples: AtomicUsize`) | anarlog, idem | **é exatamente a instrumentação que falta** — a seção 0 do plano existe porque uma gravação de 36 min pareceu saudável nos metadados |
| **taxa de amostragem como `AtomicU32`** | anarlog, idem | o dispositivo pode mudar de formato no meio da gravação; hoje isso passaria despercebido e desalinharia a faixa |
| **monitor de desconexão/reconexão** com limiar por tipo de dispositivo | Meetily `audio/device_monitor.rs` | se o headset cair no meio da reunião, hoje não há nada. Bluetooth precisa de limiar mais folgado que cabo — eles têm um `BLUETOOTH_PLAYBACK_NOTICE.md` só sobre isso |
| **diagnóstico de capacidades com avisos** | Meetily `audio/diagnostics.rs` | avisa antes de gravar quando a latência do buffer está fora do esperado, em vez de descobrir depois |
| **AGC com ganho congelado fora da fala** | anarlog `crates/agc` (`agc.freeze_gain(!is_speech)`) | AGC comum amplifica o ruído de fundo nas pausas; congelar o ganho quando o VAD diz "não é fala" evita isso. **É o Teste D do plano, já resolvido** |
| **cancelamento de eco por ONNX** | anarlog `crates/aec` | só se a medição mostrar necessidade. Com fone o vazamento é mínimo — o plano já diz para medir antes |

### Ordem sugerida

O contador de descartes e o monitor de desconexão vêm primeiro: são baratos e
atacam o modo de falha que já aconteceu de verdade. AGC e AEC ficam para depois
de medir, como o plano determina.

---

## 2. Transcrição

### O bloqueante que a fase 0 encontrou

O whisper.cpp produziu segmentos de até **73,5 segundos**, contra máximo de 14,8s
do app hoje. Um segmento assim atravessa vários turnos de fala e quebra a
atribuição de falante mesmo com diarização perfeita. Ver
[FASE0-RESULTADOS.md](FASE0-RESULTADOS.md).

### A solução que os dois usam: cortar por VAD, não por janela do ASR

Este é o achado mais importante do levantamento. Nem anarlog nem Meetily pedem
ao Whisper que segmente — eles **cortam o áudio em regiões de fala antes** e
mandam cada região para o ASR. Os segmentos saem alinhados com fronteiras de
fala por construção.

Do `crates/audio-chunking/src/vad/chunk_policy.rs` do anarlog:

| constante | valor | o que faz |
|---|---|---|
| `MIN_DETECTED_SPEECH_MS` | 200 | descarta trechos com menos de 200 ms de fala real — mata alucinação sobre silêncio |
| `MAX_SHORT_CHUNK_MERGE_GAP_MS` | 250 | funde trechos curtos separados por menos de 250 ms, para não picotar uma frase |
| `redemption_time` | configurável | quanto esperar antes de declarar que a fala terminou (conceito do Silero) |

Mais `normalize_speech_chunks`, que pós-processa as transições do VAD antes de
virar chunk.

**Isso resolve o bloqueante e o problema de alucinação de uma vez.** O `-dtw` do
whisper.cpp deixa de ser necessário: em vez de pedir timestamps por palavra para
depois recortar, corta-se antes.

E casa com o desenho de duas faixas: dá para rodar o VAD **por faixa**, e um
trecho com fala só no `mic.wav` já nasce sabendo que é você.

### Outras técnicas

| técnica | origem | avaliação |
|---|---|---|
| **Silero VAD como biblioteca** (`silero_rs`) | Meetily | evita depender do `--vad` da CLI; o mesmo modelo que já baixamos na fase 0 |
| **pool de workers com teto rígido** (`max_workers` nunca acima de 4) + orçamento de memória + retry + fallback sequencial | Meetily `whisper_engine/parallel_processor.rs` | ganho de vazão, não de qualidade. Vale, mas depois da paridade |
| **monitor de recursos do sistema** pausando o processamento | Meetily `whisper_engine/system_monitor.rs` | relevante numa RTX 2060 de 6 GB, que satura fácil |
| **supressor de stderr** do whisper.cpp | Meetily `whisper_engine/_stderr_suppressor.rs` | detalhe chato que eles já resolveram: o whisper.cpp escreve muito em stderr |

### O que NÃO copiar

O empacotamento: o Meetily linka o `whisper-rs` estaticamente e escolhe o
acelerador em tempo de compilação, e por isso o instalador de Windows deles é
**CPU apenas**. Ver a seção 5 do [PLANO.md](PLANO.md).

---

## 3. Diarização

### Um achado desconfortável

Lendo o `src/segment.rs` do `pyannote-rs` — a biblioteca que o anarlog usa e que
o Vibe originou — a inferência é:

- janela deslizante de **10 segundos**;
- para cada frame, **`find_max_index`**, ou seja **argmax sobre a dimensão de
  falante**.

O argmax por frame significa que **um frame só pode pertencer a um falante**. A
representação não comporta fala sobreposta, que é justamente o que o
`segmentation-3.0` foi treinado para modelar (saída em *powerset*, com classes
para combinações de falantes).

O pipeline Python do pyannote faz bem mais que isso: decodificação do powerset,
resegmentação com consciência de sobreposição, e agrupamento com restrições.

**Consequência prática:** a diarização nativa que anarlog e Vibe entregam é uma
versão simplificada da que temos hoje. Isso é coerente com os 29% a 45% de
discordância medidos na fase 0 — e sugere que o problema é **estrutural, não de
ajuste de threshold**, o que explica por que mexer no clustering não melhorou.

Ressalva: verifiquei o código do `pyannote-rs` diretamente; **não** auditei o
interno do sherpa-onnx, que tem um pipeline próprio com `FastClustering` e pode
tratar o powerset corretamente. Antes de concluir que toda a via nativa é
inferior, vale ler o `sherpa-onnx/csrc/offline-speaker-diarization-impl.cc`.

### O que isso muda no plano

Esta é a área onde **copiar não resolve** — os apps estudados não têm o que
queremos. As saídas, na ordem de preferência:

1. **Auditar o sherpa-onnx** para saber se ele decodifica o powerset. Se
   decodificar, o gap é menor do que a fase 0 sugeriu e a diferença medida vem
   do modelo (`segmentation-3.0` contra `community-1`), não da implementação.
2. **Apoiar-se nas duas faixas.** Tudo com energia no `mic.wav` é você, com
   certeza, sem diarização nenhuma. Isso já é o desenho do app — e é a razão de
   uma regressão na diarização doer menos aqui do que doeria no Vibe.
3. **Rotular à mão 2–3 minutos** e medir contra a verdade, em vez de medir
   concordância entre dois palpites.
4. **Manter a diarização em Python** num primeiro momento, como motor separado,
   já que a arquitetura de motores permite. Feio, mas honesto: nada obriga os
   três motores a nascerem nativos no mesmo dia.

A opção 4 merece atenção. O ponto do plano é **um app sem Python para o
usuário**, não pureza de implementação. Um motor de diarização empacotado com
seu próprio runtime embutido continua sendo uma pasta auto-contida do ponto de
vista de quem instala.

---

## Resumo: origem de cada peça

| peça | de onde vem |
|---|---|
| duas faixas, âncora de relógio, mute com silêncio | **nosso**, portar |
| vocabulário por projeto, voz por pessoa, calendário, histórico | **nosso**, portar |
| ring buffer + thread de captura + contador de descartes | anarlog |
| monitor de desconexão, diagnóstico de dispositivo | Meetily |
| AGC com ganho congelado, AEC por ONNX | anarlog |
| chunking por VAD com fusão de trechos curtos | anarlog |
| pool de workers, monitor de recursos, supressor de stderr | Meetily |
| registro de modelos, abstração de provider, sidecar com keep-alive | Meetily |
| diarização | **nenhum deles resolve melhor do que já temos** |
