# Fase 1 — o gravador nativo: carta de execução

A Fase 0 fechou ([FASE0-RESULTADOS.md](FASE0-RESULTADOS.md)) com as decisões
que esta fase precisa e nenhuma que a bloqueie. Este documento é o escopo, a
ordem e os critérios de aceite do porte do gravador para C#.

**Definição de pronto da fase inteira: o gravador Python é aposentado.** Não
"existe um gravador novo" — o velho sai de uso porque o novo é comprovadamente
igual ou melhor em tudo que importa.

---

> **Estado da execução:** ver [FASE1-HANDOFF.md](FASE1-HANDOFF.md) — o que
> está pronto, o que falta e as armadilhas já mapeadas.

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

> **Requisito 3.1 revertido, com medição (10/08/2026).** Foi implementado como
> a carta pedia e **falhou em campo**, no caso que mais importa: headset
> Bluetooth em modo mãos-livres, que é o que o usuário usa todo dia. Ali os
> carimbos QPC avançam 11,2 ms para cada 10 ms de áudio entregue, e ancorar
> cada pacote no próprio carimbo virava 1125 correções em 70 s, 13,4 s de
> silêncio inventado e 11,3 s de áudio real descartado — audível como
> craquelado. O gravador Python, que compara o total escrito com o relógio uma
> vez por bloco e tolera 50 ms, fazia 2 correções no mesmo cenário.
>
> Vale a régua da fase: o velho ganhou, então o desenho novo sai. A âncora
> passou a ser a do Python — relógio acumulado desde a origem, tolerância de
> 50 ms —, mantendo dois refinamentos nossos que não custam nada: a correção
> entra em trecho silencioso quando existe um, e o preenchimento por relógio
> continua para o loopback que não entrega pacote nenhum (requisito 3.6), agora
> só depois de 1 s de silêncio absoluto.
>
> Medido depois, no mesmo headset, 125 s com as duas faixas: **1 correção na
> faixa do sistema e 3 na do microfone**, 0,0 clique por segundo (eram 11,3) e
> **12,6 ms de desalinhamento entre as faixas** — contra 206,7 ms do desenho
> por carimbo e 17 minutos do Python numa gravação longa.
>
> **O que fica para melhorar depois**, decidido com o dono do produto: a
> hipótese de que o carimbo por pacote é superior continua de pé para
> dispositivos que o reportam com honestidade, e o desenho ideal usaria o
> carimbo quando ele é confiável e o relógio quando não é. Isso exige detectar
> a confiabilidade em tempo real, e não valia o risco agora — o áudio limpo
> vale mais que a elegância.
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

> **Desvio do 3.8, confirmado pelo revisor (08/08/2026).** Não há pergunta na
> primeira execução: o gravador cai em `Documentos\MeetingRecordings` e a pasta
> se troca pelo menu. O que o requisito existia para matar era o `\\wsl$\...`
> hardcoded, e isso morreu. Um modal antes do primeiro uso cobra uma decisão de
> quem ainda não sabe o que o app faz, e a decisão certa para quase todo mundo é
> o default. Registrado como desvio deliberado, não como pendência.

## 4. Critérios de aceite

Objetivos e executáveis, na ordem em que destravam os seguintes:

- **A. Paridade de captura**: gravar a mesma reunião em paralelo (gravador
  novo e Python lado a lado, mesmos dispositivos) e comparar: deriva final
  entre faixas, alinhamento amostra a amostra por correlação em janelas,
  meta.json campo a campo. Empate ou melhor = aprovado.
- **B. kill -9 no meio de uma gravação de 10 min** deixa arquivos
  recuperáveis sem ferramenta externa.
- **C. Soak de 1h+** com medição de deriva (a âncora só foi validada em 36
  min até hoje). — **OK parcial (08/08/2026)**: validado em 20 min, com 72
  correções de âncora e 1,7 ms de alinhamento entre faixas. O soak longo
  acontece na validação em reunião real, item 5 da ordem de trabalho.
- **D. Disco cheio durante gravação** (volume pequeno de teste): ícone em
  WARNING, dados até o momento preservados. — **OK parcial (08/08/2026)**: a
  `GuardaDeDisco` está implementada e coberta por teste (limiares em minutos de
  gravação: 15 avisa, 3 é crítico), mas o teste com volume pequeno de verdade
  não rodou.
- **E. Tamanho ≤ 25 MB** e nenhuma dependência instalada na máquina. —
  **CUMPRIDO (08/08/2026)**: CLI 12,6 MB, bandeja **14,9 MB** self-contained
  trimada, contra os 154,8 MB de antes. A causa era o WinForms não ser trimável
  (`NETSDK1175`), e a saída foi a prevista no PLANO §5: `Shell_NotifyIcon` via
  P/Invoke. Medido *e executado* — o binário publicado gravou 20 s e produziu
  `meta.json` completo (`tools/validar_bandeja.ps1`).

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

   > ⚠️ **Continua aberto, e em 14/08/2026 descobriu-se por quê.** A varredura
   > sobre `2026-08-13_14-30-15` não separou os limiares (4036–4055 palavras nas
   > quatro configurações) porque foi feita no `mix.wav`, que tem **0,3% de
   > silêncio digital** — somar mic e sistema quase nunca dá zero exato, e isso
   > apaga o critério. O áudio certo é o `system.wav` de reuniões em que o dono
   > fala pouco. O filtro de segmentos **foi escrito** (`FiltroDeSilencio.cs`) e
   > **não está ligado**: a `Ponte.cs` não passa o parâmetro. Os dois itens
   > passam a ser critérios D e E da [Fase 6](FASE6.md).
3. ~~**Repetir resultado 5/5-A em gravação saudável**~~ — ✅ **o lado do
   resultado 5 fechou em 14/08/2026** sobre `2026-08-13_14-30-15` (0,3% de
   silêncio digital, 73,3% de fala): o `hotwords` colapsa a segmentação em 3,8×
   (207 contra 787 segmentos), custa 1,8–5,7 pontos de cobertura de fala e 4,6×
   no tempo. Não era artefato do microfone morto. O lado do 5-A — confirmar que
   a correção fonética sozinha mantém os nomes — continua aberto e é o critério
   A da [Fase 6](FASE6.md);
4. Correções dos bugs 1.1–1.4 da [AUDITORIA.md](AUDITORIA.md) no app atual,
   que continua sendo a ferramenta de produção durante as fases 1–3.
