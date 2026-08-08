# Fase 1 — o gravador nativo: carta de execução

A Fase 0 fechou ([FASE0-RESULTADOS.md](FASE0-RESULTADOS.md)) com as decisões
que esta fase precisa e nenhuma que a bloqueie. Este documento é o escopo, a
ordem e os critérios de aceite do porte do gravador para C#.

**Definição de pronto da fase inteira: o gravador Python é aposentado.** Não
"existe um gravador novo" — o velho sai de uso porque o novo é comprovadamente
igual ou melhor em tudo que importa.

---

## 1. O que a fase entrega

Porte de `recorder/capture.py` + `tray.py` + `calendar_sync.py` +
`settings.py` (~900 linhas) para um executável C# self-contained:

| | hoje (Python) | alvo |
|---|---|---|
| tamanho | 186 MB | ~15 MB |
| runtime | python312.dll + 6 pacotes | nenhum |
| captura | PyAudioWPatch | NAudio (WASAPI) |
| bandeja | pystray + PIL | NotifyIcon |
| pasta | tkinter | IFileOpenDialog |
| agenda | googleapiclient (99 MB) | HTTP puro (ver nota) |

> **Desvio da carta, com medição.** A carta previa `Google.Apis.Calendar.v3`.
> Medido num "hello world" publicado com trim: 11,5 MB sem o SDK, 19,5 MB com
> ele, mais 11 avisos de trim — o mesmo tipo de reflexão que já derrubou a
> primeira versão trimada deste projeto. A superfície usada são duas chamadas
> REST (listar eventos, ler o calendário "primary") e um POST de refresh, mais
> o fluxo OAuth de loopback. Feito à mão custa ~600 linhas com teste e 0 MB.
> Se um dia a agenda precisar de escrita ou push notifications, o SDK volta à
> mesa.

**O formato de saída não muda**: `system.wav` + `mic.wav` (16 kHz mono int16)
+ `meta.json` com o mesmo schema. O app Docker continua consumindo as
gravações sem saber que o gravador trocou — zero acoplamento com as fases
seguintes.

Stack decidida no PLANO §5: .NET self-contained + trimmed + single-file (não
NativeAOT), Inno Setup fica para a Fase 4. Nesta fase o executável basta.

## 2. O que porta inalterado (o valor acumulado)

- **duas faixas separadas** — mic e loopback, nunca misturadas;
- **mute escreve silêncio**, não interrompe a escrita;
- **instrumentação de silêncio por faixa**: `total_silent_s`,
  `longest_silence_s`, `muted_s`, `usable_pct`, `ever_heard` — a lição da
  gravação de 06/08;
- **bandeja**: estados/cores (parado, gravando, mudo, aviso), clique muta e
  não para, parar só pelo menu, lembretes de mute em 2/5/15/30 min;
- **calendário**: consulta *depois* do início da captura, em thread própria,
  com a distinção de status (não configurado ≠ token morto) e a regra de ouro
  — nada da agenda pode atrasar ou impedir uma gravação;
- **seleção de dispositivos** pelo menu, travada durante a gravação;
- escolha de pasta com escrita de teste antes de aceitar.

## 3. O que muda no porte (correções da auditoria, agora requisitos)

1. **Âncora no relógio do dispositivo, não no de chegada.** O `_correct_drift`
   atual compara com `time.monotonic()` do writer — backlog da fila vira
   correção espúria. No C#, usar a posição do dispositivo que o WASAPI expõe
   (`IAudioClock`/QPC). Refinamento: aplicar inserção/descarte
   preferencialmente em trechos silenciosos, nunca no meio de fala.
2. **WAV crash-safe.** O `wave`/`WaveFileWriter` só finaliza o header no
   close — um crash perde a reunião. Flush periódico com patch do header (a
   cada ~10 s), ou PCM cru + finalização no stop.
3. **Disco**: checar espaço livre no start; falha de escrita na thread de
   escrita promove o ícone para WARNING em vez de morrer em silêncio.
4. **Instância única** (mutex nomeado).
5. **Contador de amostras descartadas** no caminho de captura (a
   instrumentação que o PARIDADE.md importou do anarlog) + registrar no
   meta.json.
6. **Loopback sem áudio não dispara `DataAvailable`** (armadilha do NAudio já
   registrada no PLANO): o laço de escrita preenche os buracos com silêncio
   explicitamente, ancorado no relógio do item 1.
7. **Desconexão de dispositivo** (headset caindo): detectar, marcar no
   meta.json, avisar na bandeja. Reconexão automática fica para depois —
   detectar já é o essencial.
8. **Pasta padrão**: primeira execução pergunta (o caminho `\\wsl$\...`
   hardcoded morre com o Python).

## 4. Critérios de aceite

Objetivos e executáveis, na ordem em que destravam os seguintes:

- **A. Paridade de captura**: gravar a mesma reunião em paralelo (gravador
  novo e Python lado a lado, mesmos dispositivos) e comparar: deriva final
  entre faixas, alinhamento amostra a amostra por correlação em janelas,
  meta.json campo a campo. Empate ou melhor = aprovado.
- **B. kill -9 no meio de uma gravação de 10 min** deixa arquivos
  recuperáveis sem ferramenta externa.
- **C. Soak de 1h+** com medição de deriva (a âncora só foi validada em 36
  min até hoje).
- **D. Disco cheio durante gravação** (volume pequeno de teste): ícone em
  WARNING, dados até o momento preservados.
- **E. Tamanho ≤ 25 MB** e nenhuma dependência instalada na máquina.

## 5. Ordem de trabalho

1. **Núcleo de captura como CLI** (espelho do `python capture.py --seconds
   30`): duas faixas, âncora, instrumentação. Sem bandeja. Valida com o
   critério A em gravação curta.
2. **Crash-safety + disco + contador de descartes** (critérios B e D).
3. **Bandeja** com estados e menu completos.
4. **Calendário** (OAuth + lookup + conta) — por último, porque é o único
   pedaço com dependência externa e o gravador é útil sem ele.
5. Soak final (critério C) numa reunião real, em paralelo com o Python, e
   aposentadoria do gravador antigo.

## 6. O que NÃO entra na Fase 1

Registrado para a fase não crescer:

- integração com Teams (PLANO §2.1) — depois da paridade;
- AGC, supressão de ruído, AEC — só com medição, e a medição precisa do gold
  set de áudio saudável;
- qualquer trabalho de motor, UI web ou instalador;
- autostart com o Windows — item pequeno, entra na Fase 4 com o instalador.

## 7. Trilhas paralelas (não bloqueiam nem são bloqueadas)

Enquanto a Fase 1 anda, o agente de benchmarks segue com o que ficou da
Fase 0, nesta ordem de valor:

1. **Teste de segmentação do whisper.cpp** (corte por turno de diarização vs
   `-dtw` vs VAD) — é o único gate restante da migração do ASR;
2. **Confirmar VAD 0,15 em mais 1–2 gravações reais** + filtro de segmentos
   sobre silêncio digital no núcleo → mudar o default do app atual;
3. **Repetir resultado 5/5-A em gravação saudável** — fecha a pendência do
   "prompt dispensável";
4. Correções dos bugs 1.1–1.4 da [AUDITORIA.md](AUDITORIA.md) no app atual,
   que continua sendo a ferramenta de produção durante as fases 1–3.
