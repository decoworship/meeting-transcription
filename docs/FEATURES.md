# O que a ferramenta entrega hoje

Inventário do que existe, descrito **pelo que o usuário consegue fazer**, não
por como está implementado. É a especificação do alvo: o app Windows precisa
entregar esta lista.

Levantado lendo o código em 07/08/2026. Cada item foi conferido na fonte.

Colunas: **crítico** = perder isso descaracteriza a ferramenta; **origem** =
onde vive hoje (`app` = Gradio/Python, `grav` = gravador Windows).

---

## A. Capturar a reunião

| # | entrega | crítico | origem |
|---|---|---|---|
| A1 | Gravar a reunião em **duas faixas separadas** — o que sai pelos alto-falantes e o seu microfone | ✅ | grav |
| A2 | Gravar direto em 16 kHz mono, o formato que a transcrição consome | | grav |
| A3 | Escolher qual microfone e qual saída capturar, pelo menu da bandeja | ✅ | grav |
| A4 | **Mutar o microfone sem desalinhar as faixas** (escreve silêncio em vez de parar) | ✅ | grav |
| A5 | Ver o estado da gravação pela cor do ícone: parado, gravando, mudo, canal sem áudio | ✅ | grav |
| A6 | Ser avisado quando o microfone está mudo há muito tempo | | grav |
| A7 | **Corrigir a deriva entre os clocks dos dois dispositivos**, ancorando no relógio de parede | ✅ | grav |
| A8 | Proteção contra parar a gravação por clique acidental (só para pelo menu) | | grav |
| A9 | Escolher onde as gravações são salvas, e abrir a pasta no Explorer | | grav |
| A10 | **Identificar automaticamente qual reunião da agenda está acontecendo** (Google Calendar) | ✅ | grav |
| A11 | Escolher qual conta Google conectar, e desconectar | | grav |
| A12 | Registrar metadados da gravação: duração, dispositivos, deriva corrigida, saúde de cada faixa, dados da reunião | ✅ | grav |
| A14 | **⚠️ Não implementado.** Ligar/desligar as notificações do Windows pelo menu da bandeja, com a escolha persistida. Pedido a partir de uso real: em reunião onde se fala pouco, ficar mudo é o comportamento correto, e o lembrete de mute esquecido (aos 2, 5, 15 e 30 min) vira incômodo. Ver nota abaixo — desligar é seguro | | grav |
| A13 | **⚠️ Não implementado.** Desempatar agendas sobrepostas pela **presença confirmada**. Hoje o `_pick` desempata pela **duração** — numa sobreposição de bloco de foco de 4 h com reunião de 30 min, ganha a mais curta. Isso acerta esse caso e erra quando há duas reuniões de verdade no mesmo horário e você só aceitou uma. O `responseStatus` já vem na resposta da API junto com `attendees`, então é adição pequena | | grav |

> **Nota sobre A14 — desligar o lembrete não desliga a rede de proteção.**
>
> São dois mecanismos independentes, e é fácil confundi-los:
>
> - **lembrete de mute esquecido** (`_check_forgotten_mute`, `tray.py`) — só
>   dispara quando o microfone foi mutado **pela bandeja**, ou seja, por decisão
>   sua. É esse que incomoda, e é esse que o A14 desliga;
> - **detecção de canal morto** (`NEVER_HEARD_WARN_S` / `GONE_QUIET_WARN_S`,
>   `capture.py`) — dispara quando uma faixa fica sem áudio **sem estar mutada**,
>   e é o que pinta o ícone de amarelo. Esse é o que existe por causa da gravação
>   de 06/08, que saiu 95% muda.
>
> Como a falha real (canal morto por hardware, dispositivo errado, cabo solto)
> cai no segundo mecanismo, desligar o primeiro não cria ponto cego. A escolha
> deve ir para o `settings.json` do gravador, junto com `start_muted` e
> `use_calendar`, para sobreviver a reinícios.
>
> Alternativa a considerar no lugar do liga/desliga puro: **silenciar só nesta
> gravação**. Preserva o aviso para o caso em que você de fato esqueceu, sem
> exigir que você lembre de religar depois — que é como um interruptor global
> costuma acabar desligado para sempre.

## B. Preparar o material

| # | entrega | crítico | origem |
|---|---|---|---|
| B1 | Transcrever **arquivo de vídeo ou áudio** avulso — mp4, mkv, avi, mov, webm, wav, mp3, m4a, flac, ogg | ✅ | app |
| B2 | Transcrever uma **gravação de duas faixas** vinda do gravador, escolhida por uma lista | ✅ | app |
| B3 | Extrair e normalizar o áudio automaticamente (FFmpeg → 16 kHz mono) | ✅ | app |
| B4 | **Somar as duas faixas preservando o balanço entre elas** — é esse balanço que identifica quem falou | ✅ | app |
| B5 | Ver avisos sobre a saúde da gravação antes de processar | | app |
| B6 | Preencher a data da reunião a partir do nome do arquivo | | app |

## C. Transcrever

| # | entrega | crítico | origem |
|---|---|---|---|
| C1 | Escolher o **motor**: whisper, faster-whisper ou whisperx | | app |
| C2 | Escolher o **modelo**, de `tiny` a `large-v3` (7 opções) | ✅ | app |
| C3 | Escolher o idioma | ✅ | app |
| C4 | **Vocabulário customizado** — nomes de pessoas, jargão, nomes de tabelas — reinjetado em toda janela | ✅ | app |
| C5 | Ligar/desligar o uso do texto anterior como contexto | | app |
| C6 | **Usar a GPU quando existir, com queda para CPU** sem intervenção | ✅ | app |
| C7 | Acompanhar o progresso por etapa (áudio, modelo, transcrição, diarização, saída) | | app |
| C8 | **Cancelar** uma transcrição em andamento | | app |
| C9 | Ver quanto tempo cada etapa levou | | app |

## D. Saber quem falou

| # | entrega | crítico | origem |
|---|---|---|---|
| D1 | **Separar automaticamente os falantes** (diarização), com escolha do modelo | ✅ | app |
| D2 | **Marcar como seus os trechos em que o seu microfone domina** — atribuição garantida, sem depender de diarização | ✅ | app |
| D3 | **Reconhecer pessoas pela voz entre reuniões diferentes** — quem já foi nomeado antes aparece nomeado | ✅ | app |
| D4 | Ajustar o quanto o reconhecimento de voz é exigente | | app |
| D5 | **Nomear os falantes** e aplicar os nomes à transcrição inteira | ✅ | app |
| D6 | **Fundir dois falantes** que a diarização separou por engano | ✅ | app |
| D7 | **Aprender a voz** de alguém ao confirmar o nome, para as próximas reuniões | ✅ | app |
| D8 | Listar e apagar as vozes salvas | | app |
| D9 | Definir como você quer ser chamado na transcrição | | app |

## E. Revisar e corrigir

| # | entrega | crítico | origem |
|---|---|---|---|
| E1 | Ler a transcrição formatada, **com cor por falante** e marca de tempo | ✅ | app |
| E2 | **Clicar num trecho e ouvir o áudio daquele ponto** | ✅ | app |
| E3 | **Corrigir o texto** de um trecho, ali mesmo | ✅ | app |
| E4 | **Trocar o falante** de um trecho específico | ✅ | app |
| E5 | **Buscar** dentro da transcrição | ✅ | app |
| E6 | Filtrar a transcrição por falante | | app |
| E7 | Ver o texto puro, para copiar | | app |

## F. Organizar

| # | entrega | crítico | origem |
|---|---|---|---|
| F1 | Marcar cada reunião com **cliente e projeto** | ✅ | app |
| F2 | **Guardar configurações por projeto** — vocabulário, idioma, modelo — e recarregá-las ao escolher o projeto | ✅ | app |
| F3 | Gerenciar a lista de clientes e projetos | | app |
| F4 | **Histórico das transcrições**, com recarregar e apagar | ✅ | app |
| F5 | Registrar a data da reunião | | app |

## G. Entregar

| # | entrega | crítico | origem |
|---|---|---|---|
| G1 | **Exportar em TXT, SRT, VTT e DOCX** | ✅ | app |
| G2 | Escolher se o export inclui os nomes dos falantes | | app |
| G3 | DOCX formatado, com cor por falante | | app |

---

## O que isso soma

**53 entregas prontas**, das quais **27 marcadas como críticas**, mais 1 lacuna
identificada (A13).

Distribuição por área:

```
A. Capturar      12 entregas   ( 7 críticas)   ← só nosso, ninguém mais tem
                 +1 lacuna (A13)
B. Preparar       6            ( 4)
C. Transcrever    9            ( 5)
D. Quem falou     9            ( 6)            ← o diferencial mais denso
E. Revisar        7            ( 5)
F. Organizar      5            ( 3)
G. Entregar       3            ( 1)
```

## Leitura para a arquitetura

Três coisas ficam evidentes ao olhar a lista por entrega em vez de por código:

**1. O motor de IA é uma fatia menor do que parece.** Dos 53 itens, apenas
**C1–C3, C6 e D1** dependem de qual biblioteca faz inferência. Todo o resto —
captura, organização, revisão, exportação — é aplicação comum. A troca de stack
discutida no [PLANO.md](PLANO.md) ameaça 5 itens, não 53.

**2. O grupo D é o coração.** Nove entregas sobre "quem falou", das quais seis
críticas, e três delas (**D2, D3, D7**) não existem em nenhum app estudado. A
diarização crua é commodity; **o que é nosso é a memória de vozes entre reuniões
e a certeza que vem do microfone separado**.

**3. Os grupos A e E são o que o usuário toca.** A UI vai mudar, mas E1–E5
descrevem interações, não telas: clicar para ouvir, corrigir no lugar, trocar o
falante, buscar. Qualquer interface nova precisa entregar essas cinco, ou
regride.

## O que está fora

Registrado para não voltar como surpresa:

- **resumo e ata por LLM** — não existe hoje; entra como motor novo depois da
  paridade;
- **transcrição em tempo real** — hoje é tudo depois da reunião;
- **Linux e macOS** — o app roda em Docker hoje, mas o gravador é Windows, então
  a ferramenta completa já é Windows-only na prática;
- **espelhar o mute do Teams** — está no plano, seção 2.1, não implementado.

## Próximo passo

Com esta lista fechada, desenhar a arquitetura **a partir dela**: quais entregas
ficam no núcleo, quais viram motor, e o que cada uma exige de contrato entre os
dois. A regra que a lista sugere: **o núcleo é dono de A, B, E, F e G; os
motores entregam C e D**, e o núcleo continua dono de D2, D3, D5, D6, D7 —
porque atribuir dono pelo microfone e lembrar vozes são decisões de produto, não
de inferência.
