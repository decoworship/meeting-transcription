# Fase 3 — a reunião depois da reunião: carta de execução

Escrita em 13/08/2026, no dia seguinte ao fechamento da Fase 2.5, a pedido do
dono do produto. As fases 0 a 2.5 entregaram **um app que grava e transcreve**.
Esta entrega o que se faz com o resultado: notas escritas por quem estava lá,
uma transcrição que não some quando se troca de tela, e a **ata**.

O equivalente das anteriores são [FASE1.md](FASE1.md), [FASE2.md](FASE2.md) e
[FASE2.5.md](FASE2.5.md); o que elas dizem sobre WASAPI, âncora de relógio,
motores e ciclo de vida continua valendo inteiro.

**Esta fase mudou a ordem do plano.** O que era a Fase 3 (redesign da interface
com o AA Design System) virou a **Fase 5**, depois do instalador. O motivo está
em [PLANO.md](PLANO.md) §3: com o Gradio aposentado, o redesign deixou de ser
pré-requisito de qualquer coisa e virou acabamento — e acabamento se faz por
último, sobre uma interface cujo conjunto de telas já parou de crescer. Esta
fase acrescenta uma tela e mexe em duas; fazer o redesign antes seria redesenhar
telas que ainda vão mudar.

---

## 1. O que a fase entrega

Na ordem em que será feita, que é também a ordem de risco crescente:

| # | o quê | tamanho |
|---|---|---|
| **1** | a transcrição em curso **sobrevive à navegação**, e o trilho mostra que ela está rodando | pequeno |
| **2** | **notas de reunião** escritas durante a gravação, guardadas junto dela | pequeno |
| **3** | a **ata por LLM**, com o tipo de reunião escolhendo o modelo de ata | é a fase inteira |

Os dois primeiros são independentes entre si e do terceiro. Cada um entrega
valor sozinho e pode ser publicado sozinho — o que importa, porque o terceiro é
o único do projeto que depende de baixar um modelo novo e de caber na VRAM.

---

## 2. Item 1 — a transcrição deixa de morar na tela

### O que acontece hoje

O trabalho **não** se perde: o `Transcritor` grava `transcricao.json` na pasta da
gravação ao terminar (`Nucleo/Transcritor.cs:272`), e o `Task.Run` da ponte
(`App/Ponte.cs:604`) não é cancelado por ninguém. O que se perde é a **vista**:
a barra de progresso, o texto de etapa e a promessa que abriria a revisão vivem
todos dentro de `transcrever()` (`App/web/app.js:285`), em closure sobre nós de
DOM que `tela.replaceChildren()` joga fora no primeiro clique no trilho.

O resultado prático é o pior dos dois mundos: a transcrição continua rodando,
consumindo GPU, e o usuário não tem como saber disso nem como voltar para ela.
Quem sai da tela no meio descobre o fim por acaso, reabrindo a reunião.

### O desenho

**O trabalho vira estado do núcleo, e a tela passa a desenhá-lo** — exatamente o
que a Fase 2.5 já fez com o gravador, e pelo mesmo motivo. O caminho está
aberto: o canal de eventos `id: 0` existe desde a 2.5 e é como o nível de áudio
chega à tela cinco vezes por segundo.

- um registro de transcrição em curso no núcleo, com gravação, etapa, fração,
  texto e instante de início;
- **uma de cada vez.** Duas transcrições disputando a mesma GPU não terminam mais
  rápido; a segunda é recusada com uma frase que diz qual reunião está ocupando o
  motor. É também o que mantém o item 3 simples: só um motor pesado carregado por
  vez, com a mesma trava;
- `op: "transcricoes"` devolve o estado, para quem acabou de abrir a tela;
- o progresso passa a ser empurrado como evento `id: 0`, `tipo: "transcricao"`,
  em vez de resposta parcial ao pedido de quem chamou. O pedido `transcrever`
  responde na hora ("aceita") e o fim chega pelo mesmo canal;
- ao terminar, a bandeja **notifica** — o mesmo caminho de notificação que o
  gravador já usa, respeitando o mesmo ajuste de ligar/desligar.

### A bolinha

No trilho, o item **Reuniões** ganha um ponto pulsando enquanto houver
transcrição em curso. Três regras:

1. o ponto sai do estado, não de um `setTimeout` — some quando o núcleo diz que
   acabou, inclusive se acabou em erro;
2. `prefers-reduced-motion` desliga a pulsação e mantém o ponto estático. Um
   ponto que pisca sem parar num app que fica aberto o dia inteiro é exatamente
   o caso que essa media query existe para resolver;
3. o ponto é redundante com texto para quem usa leitor de tela — o item recebe
   um rótulo que diz "transcrevendo", não só uma cor.

---

## 3. Item 2 — notas de reunião

**Onde:** um bloco na tela do Gravador, ao lado dos medidores, ativo enquanto se
grava; e o mesmo texto editável depois, na tela da reunião — antes ou depois de
transcrever. Decisão do dono do produto: quem toma nota corre atrás do que
perdeu quando a reunião acaba, e uma nota que só existe durante a gravação
obrigaria a escrever em outro lugar cinco minutos depois.

**Formato:** `notas.md` ao lado de `mic.wav`, `system.wav` e `meta.json`. Texto
puro, Markdown, legível e editável fora do app — a mesma escolha de formato leve
do resto das gravações. Nada de campo novo no `meta.json`: seu schema não muda
(ver `Gravacao/Meta.cs`, e o motivo escrito lá).

**Salvamento:** automático, com atraso de ~1 s depois da última tecla, mais uma
gravação forçada ao parar a gravação e ao sair da tela. Nota de reunião perdida
por falta de um clique em "salvar" é a falha mais boba que este item pode ter.

**Um botão que vale o que custa:** "marcar momento" insere `[00:12:34]` com o
tempo decorrido da gravação. É o que liga a nota ao áudio depois, e custa uma
linha do relógio que a tela do Gravador já mostra.

**Duas coisas que as notas alimentam de graça**, e que são o motivo de este item
vir antes da ata e não depois:

- **o vocabulário.** Nome próprio, sigla e nome de sistema escritos à mão por
  quem estava na reunião são exatamente o que o ASR erra e o que a correção
  fonética conserta. As notas viram sugestão de vocabulário na tela de preparo —
  sugestão, não injeção automática: o usuário confirma;
- **a ata.** O que o humano escreveu vale mais que o que o modelo ouviu, e entra
  no prompt marcado como tal (§4).

---

## 4. Item 3 — a ata por LLM

É a "próxima fase de verdade" que a FASE2.5.md §8 e o PLANO.md §5 vêm anotando
desde que o Meetily foi estudado. O desenho de referência já está registrado em
[PLANO.md](PLANO.md) §5 ("O motor de resumo do Meetily é o modelo a seguir") e
não se reabre aqui.

> **A arquitetura do motor foi escrita em 14/08/2026: [ATA.md](ATA.md).** Ela
> mediu o que esta carta só estimava, e duas coisas mudaram de figura:
>
> - **as skills não são grandes** — 2.400 tokens com a referência junto, 7% de um
>   contexto de 32k. O que aperta é a transcrição, e só passando de ~1h45. O
>   problema real do modelo pequeno não é caber, é **obedecer**;
> - **sem keep-alive na v1**, ao contrário do que esta carta antecipou do
>   Meetily: gera-se uma ata por reunião, com o usuário esperando, e manter
>   2,6 GB de VRAM presos enquanto ele grava a próxima custa mais do que
>   carregar o modelo de novo.
>
> A ATA.md também decide o que esta carta deixava em aberto: o app classifica o
> tipo e o modelo só redige, a saída é constrangida por gramática, e um
> verificador determinístico procura dono inventado e decisão inventada antes de
> a ata virar arquivo.

### Decisão fechada: modelo local

O motor roda **na máquina**, como sidecar, com modelo GGUF baixado pelo
catálogo. Decisão do dono do produto em 13/08/2026. O que ela compra e o que
custa, dito na cara:

- **compra:** nenhuma transcrição de reunião com cliente sai da máquina; nenhum
  custo por reunião; funciona sem internet, como o resto do app;
- **custa:** ata pior que a de um modelo de fronteira, e mais engenharia —
  keep-alive, timeout de ociosidade, desligamento gracioso, contexto finito.

A porta para um provedor remoto **não é fechada com pregos**: o motor de ata
fica atrás de uma interface, como o `BaseTranscriber` fez pelo ASR. Mas nada de
provedor remoto é escrito nesta fase, e a interface não vira abstração
especulativa: um implementador só, com o segundo cabendo se e quando for pedido.

### O que precisa ser medido antes de escrever a tela

Duas perguntas que decidem o desenho e que só a máquina responde. Elas vêm
**primeiro**, no espírito da Fase 0:

1. **Como o llama.cpp entra.** `llama-cpp-python` no Python embarcado que já
   existe (reaproveita a infraestrutura de `Motores`, mas depende de wheel CUDA
   para Windows) ou o binário do llama.cpp como sidecar próprio (o desenho do
   Meetily, variante por acelerador sem tocar no app, ao custo de mais MB e de
   conferir se o CUDA dele convive com o que o torch já traz). Medir tamanho em
   disco, tempo de carga e tokens/s numa transcrição real.
2. **Se cabe, e quanto rende.** Um 4B em Q4_K_M são ~2,6 GB e **não convivem com
   o `large-v3` carregado** na RTX 2060 de 6 GB. Processos separados resolvem
   naturalmente — resume-se depois de transcrever, com a VRAM já liberada —, e a
   trava de "um motor pesado por vez" do item 1 é o que garante isso na prática.

Candidatos, do registro do Meetily: Qwen 3.5 4B (2.614 MiB, 32k) como alvo de
qualidade, Gemma 3 1B (1.019 MiB) como piso de velocidade. **Presets de
amostragem por modelo** — o Qwen de resumo não usa amostragem gulosa —, que é um
detalhe que eles já resolveram e que a gente descobriria doendo.

### O contexto: uma reunião de uma hora cabe; uma de três, não

Uma hora de fala transcrita dá da ordem de 8–12 mil tokens, folgado em 32k. A
régua é medir tokens antes de mandar e ter um caminho de blocos (resumo parcial
por trecho, depois consolidação) para o que não couber — **não escrever o
caminho de blocos antes de ter a medida**, porque ele degrada a ata e não deve
ser o caminho comum.

### Os modelos de ata

Vêm prontos: `transcrição para atas/` na raiz do repositório já tem a skill
escrita e testada, com seis tipos e uma referência por tipo.

| tipo | referência |
|---|---|
| update com cliente | `references/cliente-update.md` |
| sprint | `references/sprint.md` |
| sessão de trabalho | `references/trabalho.md` |
| kickoff | `references/kickoff.md` |
| apresentação de resultados | `references/resultados.md` |
| daily / status curto | `references/daily.md` |

O trabalho é **portá-los para templates embutidos no executável** — o mesmo
caminho de `EmbeddedResource` da UI, com a armadilha da barra invertida já
anotada no CLAUDE.md — e adaptar o tom: a skill foi escrita para um modelo de
fronteira que lê a referência e decide; um 4B local precisa da estrutura mais
explícita e de menos margem de julgamento.

**As regras comuns da skill não se diluem no porte.** Separar decidido de
discutido, action item com dono e prazo (`[responsável a definir]` quando não
houver), fidelidade a números citados, `[sic?]` no que não deu para inferir. É
onde uma ata automática causa dano: uma hipótese promovida a decisão cria
memória falsa, e o modelo local tem *mais* tendência a isso, não menos.

**O tipo de reunião é escolhido pelo usuário**, com o padrão vindo das
preferências do projeto — o `Projetos.cs` já guarda preferência por
cliente/projeto, e "as reuniões deste projeto são sprint" é da mesma natureza
que "o modelo deste projeto é o large-v3". Sem classificação automática nesta
fase: errar o tipo de ata em silêncio é pior que perguntar.

### A tela

**Um destino novo no trilho, abaixo do Gravador: "Atas".** Decisão do dono do
produto: gerar e ler acontecem ali, sem passar por Reuniões. Escolhe-se a
reunião (entre as já transcritas), o tipo, e gera-se; a ata gerada fica na
lista, com o mesmo desenho de cartão da lista de reuniões.

Reuniões continua sendo gravação, transcrição e revisão. A ata **não** é uma
aba escondida dentro da revisão porque ela tem vida própria: é o que se copia
para o e-mail, o que se revisa dias depois, e o que se regenera quando a
transcrição foi corrigida.

**Onde fica em disco:** `ata.md` na pasta da gravação, ao lado de `notas.md` e
`transcricao.json`. Regenerar sobrescreve, e a tela avisa quando há uma ata mais
velha que a última edição da transcrição.

**O que entra no prompt**, nesta ordem de precedência: as notas do humano, o
cabeçalho da reunião (título, cliente, projeto, participantes da agenda), a
transcrição com falantes já nomeados. O vocabulário do projeto entra como lista
de grafias corretas.

---

## 5. Critérios de aceite

| | o quê |
|---|---|
| **A** | sair da tela no meio de uma transcrição e voltar mostra a mesma barra, na mesma etapa; fechar a janela e reabrir também |
| **B** | a bolinha acende ao começar e apaga ao terminar — **inclusive quando termina em erro** — e não acende sem transcrição rodando |
| **C** | uma segunda transcrição pedida durante a primeira é recusada com uma frase que nomeia a reunião ocupada, e nenhuma das duas corrompe a outra |
| **D** | notas escritas durante a gravação estão em `notas.md` depois de parar, **e depois de matar o processo pelo Gerenciador de Tarefas** no meio |
| **E** | gerar a ata de uma reunião real de 1 h, com a transcrição do dia, e comparar com a ata que a skill produz num modelo de fronteira. A régua não é igualdade: é **nenhuma decisão inventada e nenhum action item com dono errado** |
| **F** | gravar enquanto se gera uma ata não perde pacote — a mesma régua B da Fase 2.5, agora com o segundo motor pesado |
| **G** | as 181 tests continuam verdes, e o `publicar.sh` continua passando nas três réguas |

O **E** é o que decide o item 3. Uma ata que inventa decisão é pior que nenhuma
ata, e se o modelo local não passar nessa régua, o resultado honesto da fase é
registrar isso e reabrir a decisão de provedor — não entregar uma ata em que não
se pode confiar.

---

## 5.1 O que foi entregue (14/08/2026)

> **Estado final em [FASE3-HANDOFF.md](FASE3-HANDOFF.md)** — o que foi medido, as
> decisões que valem revisitar, e o que a Fase 4 herda.

Os três itens estão de pé. O que mudou de desenho no caminho, e por quê, está em
[ATA.md](ATA.md) — este é o resumo:

| item | estado |
|---|---|
| 1. transcrição sobrevive à navegação, bolinha no trilho | ✅ + **parar a transcrição**, que não estava previsto e o uso pediu |
| 2. notas de reunião | ✅ `notas.md`, no Gravador e na reunião, alimentando o vocabulário |
| 3. ata por LLM local | ✅ motor, verificador, redator e o destino **Atas** no trilho |

Fora do previsto, porque o uso cobrou: **o vínculo cliente/projeto passou a
viver na gravação** (`reuniao.json`), o seletor de diarização — que era
decorativo — passou a valer, **cada destino do trilho ganhou a sua bolinha**
(gravar convive com transcrever ou com escrever ata; transcrever e escrever ata
nunca convivem), e **a organização de cada participante passa a sair do domínio
do e-mail da agenda**, não de dedução do modelo ([ATA.md](ATA.md) §5.1).

**O que a Fase 4 herda:** o motor de ata são 3,5 GB (llama.cpp + GGUF) montados
por `tools/empacotar_motor_de_ata.sh`, e o instalador precisa deles. O build de
CUDA tem que casar com o driver — o 12.4 funciona onde o 13.3 falha.

---

## 6. Ordem de trabalho

1. **Item 1**, inteiro: estado no núcleo, evento, bolinha, notificação. É o
   menor, mexe no que já existe e a trava de "um motor por vez" que ele cria é
   pré-requisito do item 3.
2. **Item 2**, inteiro: `notas.md`, o bloco na tela do Gravador, a edição na
   reunião, o botão de marcar momento. Deixar a alimentação do vocabulário para
   o fim do item — é o pedaço que pode ser cortado sem perder o resto.
3. **Medir o motor de ata** (§4), antes de escrever tela nenhuma. Responde as
   duas perguntas de desenho e pode reabrir a escolha de modelo.
4. **O sidecar de ata**, validável por linha de comando antes de existir UI —
   como a Fase 2 fez com o ASR.
5. **Os templates portados**, com a régua do critério E numa reunião real.
6. **A tela Atas**, por último, quando o que ela mostra já existe.

---

## 7. Fora desta fase

- **O instalador** — é a Fase 4, e a próxima depois desta;
- **O redesign da interface** — virou a Fase 5, depois do instalador
  ([PLANO.md](PLANO.md) §3);
- **Motores como pacotes baixáveis** por acelerador (CUDA/Vulkan/CPU), que é o
  que encolheria o instalador de verdade. O motor de ata **nasce** com o desenho
  certo para isso, mas a migração dos motores existentes não é desta fase;
- **Provedor remoto de LLM** (§4);
- **Classificação automática do tipo de reunião** (§4);
- **A integração com o Teams**, que continua na PLANO.md §2.1 e não depende de
  nada disto;
- **Linux e Mac.**
