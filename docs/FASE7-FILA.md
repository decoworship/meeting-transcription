# Fase 7 — a fila, e os três produtos

Escrito em 04/09/2026, e é um documento de **registro**, não de decisão.

> **Leia a [FASE7-ROTA.md](FASE7-ROTA.md) antes desta.** Este documento continua
> sendo o glossário dos ids `T*` — é para isso que ele serve. Mas o **estado** do
> Produto A no §2 está vencido: ele foi construído e está em RC.

**Por que ele existe.** Os testes da Fase 7 são citados por id — `T0.1`, `T1.4`,
`T4.3` — na tabela de estado da [FASE7-RESULTADOS.md](FASE7-RESULTADOS.md) §6, em
seções soltas do mesmo arquivo, e em docstrings de `tools/`. **Nenhum documento
os definia.** Não havia como saber o que `T4.3` é senão perguntando a quem
escreveu — e é, em escala menor, o defeito que matou a Fase 6 e que o
[BACKLOG.md](BACKLOG.md) abre explicando. Está apontado como o buraco nº 1 do §1.2
da [FASE7-FRONTEND.md](FASE7-FRONTEND.md).

Aqui não há item novo. **Nada foi inventado**: cada linha aponta para onde o
teste foi executado ou para o parágrafo que o descartou, e o que não tem origem
rastreável está marcado como tal.

---

## 1. A fila, por rodada

Estado: **✅ feito** · **⛔ reprovado** (foi medido e não serve) · **⏸ adiado**
(deixou de ser caminho crítico) · **⬜ nunca feito**.

### Rodada 0 — o pipeline de hoje, em blocos

A rodada que decidiu tudo, e cuja pergunta central **não estava na lista
original** da carta: *o pipeline de hoje, cortado em blocos, entrega texto pior?*

| id | a pergunta | como se responde | estado |
|---|---|---|---|
| **T0.1** | o bloco de 3 min custa qualidade de texto? | `tools/medir_bloco_asr.py` + `wer_contra_gemini.py` | ✅ **não** — 11,53% de divergência contra a passada inteira, e as duas divergem do Gemini na mesma medida ([§1](FASE7-RESULTADOS.md)) |
| **T0.2** | a gravação sobrevive ao bloco rodando junto? | `tools/carga_de_bloco.py --conferir` | ✅ **passou** — 0 amostras perdidas em duas gravações reais, deriva abaixo da mediana do acervo (§6). **Medido na 2060 do dono, não na RTX 4050 que desliga** |
| **T0.3** | dá para costurar o falante entre blocos? | `tools/medir_bloco_diarizacao.py` | ✅ sim — e o T0.4 tornou desnecessário ([§2](FASE7-RESULTADOS.md)) |
| **T0.3b** | o pyannote por bloco acha as mesmas pessoas? | idem | ✅ 4,0% de erro contra a passada inteira; o T0.4 zera isso ([§3](FASE7-RESULTADOS.md)) |
| **T0.4** | e se a diarização rediarizar tudo desde o começo? | `tools/medir_janela_cumulativa.py` | ✅ **é o desenho** — custo linear, sem teto de falantes, ~21% da placa aos 60 min ([§5](FASE7-RESULTADOS.md)) |
| **T0.5** | o motor de diarização lê um WAV que está crescendo? | gravar, ler em paralelo, comparar com a leitura depois de fechado | ✅ **feito em 09/09/2026, e o desenho não caiu.** `File.OpenRead` **falha** num WAV que está sendo gravado — ele pede `FileShare.Read`, que quer dizer *"eu não permito que escrevam"*, e já existe um handle de escrita. `FileShare.ReadWrite` abre e lê. Ver [Faixas.cs:109](../app-net/Nucleo/Faixas.cs#L109) |

### Rodada 1 — o MOSS-Transcribe-Diarize

| id | a pergunta | como se responde | estado |
|---|---|---|---|
| **T1.1** | MOSS na gravação inteira | `tools/medir_moss.py` | ⛔ **não escala nesta placa** — interrompido depois de 1h36 numa gravação de 32 min, a 0,35× o tempo real ([§7.2](FASE7-RESULTADOS.md)) |
| **T1.2** | MOSS em bloco de 3 min | idem | ✅ **ganha do pipeline atual** no texto e no falante ([§7.4](FASE7-RESULTADOS.md), §7.6) |
| **T1.3** | VRAM e tempo na RTX 2060 | idem | ✅ **2,6× mais rápido**, modelo de 0,70 GB, sem degradar com a duração ([§7.3](FASE7-RESULTADOS.md)) |
| **T1.4** | a costura de falante entre blocos | `tools/medir_costura.py` | ✅ **custa ~1 ponto** — e o limiar não regula acerto, regula quanta gente o app inventa ([§11](FASE7-RESULTADOS.md)) |
| **T1.5** | o vocabulário sem `hotwords` | `tools/medir_vocabulario_moss.py` | ✅ **bloqueio menor do que parecia** — 15,8 pontos de distância, não 26,4, e parte é grafia ([§12](FASE7-RESULTADOS.md)) |
| **T1.6** | rodar o MOSS em cada faixa separada | `tools/medir_faixas_separadas.py` | ⛔ **reprovada** — o `mic.wav` é silêncio em 43,7% dos blocos, e bloco mudo faz o modelo alucinar em chinês ([§13](FASE7-RESULTADOS.md)) |

### Rodada 2 — o LLM ao vivo

| id | a pergunta | como se responde | estado |
|---|---|---|---|
| **T2.1** | perguntar ao LLM sobre a reunião em curso | `tools/medir_llm_ao_vivo.py` | ✅ **cabe até ~60 min** de reunião ([§10](FASE7-RESULTADOS.md)) |

### Rodada 3 — o falante com nome cedo

| id | a pergunta | como se responde | estado |
|---|---|---|---|
| **T3** | a ata sai pior a partir do MOSS? | `tools/comparar_atas_moss.py` | ✅ **não** — mesmo prompt, mesmo motor, e nenhuma alucinação no braço do MOSS ([§14](FASE7-RESULTADOS.md)). **A auditoria estrutural do `ata.json` continua não feita** (§14.3) |
| **T3.1** | quanto nomear cedo melhora o resto | `tools/medir_nomear_cedo.py` | ✅ **funciona com zero erro de pessoa** em 42 mil palavras; cobertura de 78,5% no limiar de 0,60 ([§8](FASE7-RESULTADOS.md)) |

### Rodada 4 — os motores de streaming

| id | a pergunta | como se responde | estado |
|---|---|---|---|
| **T4.1** | exportar o Sortformer para ONNX e rodá-lo offline | era o passo 1 da ordem do §5 da [FASE7.md](FASE7.md) | ⏸ **adiado, e nunca começado** — o T0.4 removeu a necessidade |
| **T4.2** | repetir em streaming, nos quatro modos de latência | idem | ⏸ mesma razão |
| **T4.3** | a contagem de falantes que ele devolve nas reuniões de 5+ | idem | ⏸ mesma razão. **Era o risco principal da carta** (§3.1), e a janela cumulativa o esvaziou: rediarizar desde o começo não tem teto de falantes |
| **T4.4** | o Nemotron em CPU | `tools/comparar_pipeline.py` | ✅ **0,99× o tempo real — sem folga nesta CPU** ([§9](FASE7-RESULTADOS.md)) |
| **T4.5** | a política de emissão (*commit*) do streaming | leitura do runtime | ✅ **já existe pronta** (§9.3) |

> **O que foi descartado sem teste**, e está registrado no §6 dos resultados: usar
> o `num_speakers` do convite da agenda. O convite é um limite superior inflado, e
> o modo de falha medido é *fusão*, não excesso de divisão.

---

## 2. Os três produtos

**Os nomes A, B e C são da [FASE7-FRONTEND.md](FASE7-FRONTEND.md) §6.** Antes
dela, só o A tinha definição anterior, e ela era uma frase de passagem no §4 dos
resultados — *"a consciência da reunião, com o LLM respondendo sobre o que já foi
dito e o falante ganhando nome cedo"*. Ou seja: a única definição que existia
misturava os três.

### Produto A — a consciência da reunião

Durante a reunião, o texto do que já foi dito aparece na tela, em blocos de 3
minutos, com o falante identificado. **Não depende de modelo novo nenhum** — é o
pipeline de hoje, cortado. Mora no Gravador, e não num destino novo.

- **Sustentado por:** T0.1, T0.3b, T0.4 e T0.2.
- **Custo medido:** ~41% da placa (ASR em bloco 15–24%, diarização cumulativa
  ~21% aos 60 min), antes de qualquer LLM e com o Meet já rodando.
- **A latência é de 3 minutos mais o bloco**, e a tela não pode chamar isso de
  tempo real: quem espera legenda e recebe bloco de três minutos acha que está
  quebrado.
- **Estado em 10/09/2026: construído e em RC (`0.7.0-rc3`).** O `T0.5` passou, o
  `FE-2` está feito e o `FE-8` — o ensaio sem backend — foi **pulado**: o painel
  de verdade foi construído direto. Ver [FASE7-ROTA.md](FASE7-ROTA.md) §1.

### Produto B — perguntar sobre a reunião, ao vivo

Uma caixa de pergunta sobre o que já foi dito, respondida pelo motor de ata.

- **Sustentado por:** T2.1 e a medição do motor de ata na [ATA.md](ATA.md) §8.
- **É o mais caro dos três:** 20 a 40 s por pergunta, com a placa inteira — o que
  **para o bloco de ASR** que estaria rodando. A tela precisa dizer isso *antes*.
- **O que decide se ele existe** são duas medições que ninguém fez: o cache de
  prompt do `llama.cpp` e quanto de transcrito basta.

### Produto C — o falante ganha nome cedo

Aprende-se a voz nos primeiros minutos e nomeia-se o resto da reunião.

- **Sustentado por:** T3.1, o mais bem medido dos três (40 gravações).
- **Metade dos trechos tem nome e metade não, e essa proporção é o produto**, não
  uma falha: a tela precisa de duas classes visuais distintas.
- **O nome não aparece antes dos ~9 minutos** — até a janela esquentar, a
  rotatividade do rótulo chega a 22,8% (§5.3). Um nome que troca sozinho é pior
  que "Falante 2".

---

## 3. O que a fila não decide

**Nada aqui autoriza ligar coisa nenhuma ao vivo.** O argumento mais forte contra
a Fase 7 continua de pé e não é técnico: hoje o desligamento do segundo usuário
custa uma transcrição, e a retomada devolve; ao vivo custaria a reunião. O
`SUP-2` do [BACKLOG.md](BACKLOG.md) segue aberto.

O `SUP-1` — o registro que diria *o que o app estava fazendo quando a máquina
caiu* — **ganhou os dois instrumentos baratos em 04/09/2026**: o
`AprendizadoDeVozes.ExtrairAsync` passou a assinar o `AoRegistrar`, e o
`Registro.Ultimas()`, que ninguém chamava, entrou no bloco de diagnóstico como
campo próprio. Os outros três continuam não existindo.

---

## 4. Fontes

- [FASE7.md](FASE7.md) — a carta de estudo, e a ordem de medição que estava errada
- [FASE7-RESULTADOS.md](FASE7-RESULTADOS.md) — onde cada número deste documento foi medido
- [FASE7-BACKEND.md](FASE7-BACKEND.md) — o plano do MOSS como motor opcional
- [FASE7-FRONTEND.md](FASE7-FRONTEND.md) — a auditoria de interface e as dez decisões
- [BACKLOG.md](BACKLOG.md) — `SUP-1`, `SUP-2`, `VOZ-1`, `DIST-1`
