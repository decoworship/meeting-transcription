# Fase 7 — rodada 0: o que o acervo respondeu

Medido em 28/08/2026, na **RTX 2060 de 6 GB** da máquina do dono do produto,
sobre o acervo real (49 gravações transcritas, 23,5 h de áudio; 4 delas com
transcrição paralela do Gemini/Meet).

Esta rodada responde a pergunta que a [FASE7.md](FASE7.md) §5 deixou para o fim
e que na verdade decide tudo: **o pipeline de hoje, rodado em blocos de poucos
minutos durante a reunião, entrega um texto pior?**

> **A resposta é não.** Em três réguas independentes, o texto por blocos empata
> com a passada inteira — e na régua que não é nossa, empata ligeiramente **por
> cima**. O troceamento não custa qualidade; ele custa outra coisa, e essa
> outra coisa é o falante.

As ferramentas, todas novas nesta rodada:
[`simular_blocos.py`](../tools/simular_blocos.py) (estrutura, sem GPU),
[`medir_bloco_asr.py`](../tools/medir_bloco_asr.py) (texto),
[`medir_bloco_diarizacao.py`](../tools/medir_bloco_diarizacao.py) (falante) e
[`medir_janela_cumulativa.py`](../tools/medir_janela_cumulativa.py) (o desenho que venceu) e
[`carga_de_bloco.py`](../tools/carga_de_bloco.py) (o T0.2).
As duas de GPU usam os parâmetros exatos de `motores/`.

---

## 0. A síntese — os quatro métodos, lado a lado

Antes das onze seções de detalhe, a leitura que alguém vai querer primeiro.

### 0.1 O que é "durante" e o que é "depois"

**Todos os números deste relatório são de áudio gravado.** Nada foi medido
durante uma reunião de verdade, exceto o T0.2 (§6).

O que aqui se chama **regime de bloco** não é o modelo rodando ao vivo: é pegar
3 minutos de áudio e processá-los isoladamente, sem ver o resto — que é o que
aconteceria ao vivo, simulado alimentando gravações antigas em pedaços. A
diferença para o ao vivo verdadeiro é só *quando* o áudio chega.

**Passada inteira** é o modelo vendo tudo de uma vez, que é o que o app faz hoje.

A rodada 0 perguntou se o bloco custa qualidade. **Não custa** — e é isso que
torna todo o resto possível.

### 0.2 Os quatro métodos

| | **1. hoje** | **2. bloco** | **3. MOSS** | **4. Nemotron** |
|---|---|---|---|---|
| quando | depois | durante | durante | durante |
| latência | minutos, no fim | 3 min | 3 min | sub-segundo |
| motores | 2 | 2 | **1** | 1 (só texto) |
| GPU durante a reunião | — | ~41% | **~9%** | disputa |
| texto vs Gemini | referência | empata | **ganha 3/4** | empata com o MOSS |
| falante vs Gemini | referência | erro zero¹ | **ganha 3/4**² | não faz |
| disco | ~3,1 GB + 58 MB | idem | **0,70 GB** | 0,75 GB |
| dependências | Python, torch, CTranslate2, pyannote | idem | **um binário ggml** | idem |

¹ com janela cumulativa (§5); em bloco isolado são 4,0% (§3).
² já pagando a costura de verdade (§11); a régua sozinha dava ~1 ponto a mais.

### 0.3 Velocidade, no mesmo áudio

```
gravação      hoje (ASR + diarização)    MOSS em bloco    ganho
 7,7 min       104s + 18s =  122s             46s          2,7x
14,6 min       192s + 31s =  224s             85s          2,6x
32,1 min       454s + 75s =  529s            205s          2,6x
48,5 min       469s + 89s =  559s            265s          2,1x
```

Por bloco de 3 min o MOSS gasta de **15,3 a 18,6 segundos** — 8,5% a 10,4% da
GPU, contra os ~41% do método 2 fazendo o mesmo trabalho com dois motores.

### 0.4 Por que o MOSS ganha no texto

O mecanismo é um só e aparece na coluna `faltando` da régua: **ele não usa VAD
separado, então não descarta fala**. Na gravação de 48,5 min, palavras faltando
caem de 1189 para 737. O custo é falar mais e errar mais junto — trocadas sobem
de 376 para 548, sobrando de 173 para 218. O saldo é positivo em 3 de 4.

É o mesmo gate da [FASE6.md](FASE6.md) §4.3, resolvido por não existir.

---

## 0.5 As ressalvas — leia antes de decidir qualquer coisa

Estas valem para tudo o que vem depois, e são o que separa "os números dizem"
de "então faça".

**A passada inteira do MOSS não escala nesta placa.** Interrompida depois de
**1h36** numa gravação de 32 min, a 0,35× o tempo real, com a VRAM em 5794 de
6144 MiB (§7.2). Ele **não substitui o pipeline offline** — substitui o pipeline
*em bloco*. Uma passada final completa continua sendo `faster-whisper` +
`pyannote`, ou não existe.

**A costura divide gente demais.** Mesmo no melhor limiar (0,55), a reunião de
122 min sai com **15 identidades para 8 pessoas**; a de 41 min, 21 para 11
(§11.2). A métrica de acerto não enxerga isso, porque casa rótulos de forma ótima
e dividir custa menos que fundir — mas na tela apareceriam 15 falantes. Promove o
`VOZ-1` do [BACKLOG.md](BACKLOG.md) de conveniência a requisito.

**São 6 gravações, e só 4 com referência independente.** As quatro com Gemini são
todas do lado fácil (3, 3, 3 e 6 falantes); as difíceis usam a saída do próprio
app como referência, o que mede **divergência, não qualidade**. Suficiente para
as conclusões qualitativas; insuficiente para intervalo de confiança.

**A régua de falante do MOSS foi favorável até a §11.** Os números da §7.4 davam
a ele a costura de graça. Os da §11.1 são os honestos, e são ~1 ponto piores.

**O Gemini não é gabarito.** Ele erra por conta própria — nas gravações medidas
errou coisas que o app acertou e vice-versa. O que a régua mede é **quanto duas
transcrições independentes discordam**. Só a comparação entre colunas do mesmo
áudio tem sentido; o número absoluto, não.

**Nada foi medido ao vivo.** O regime de bloco simula o ao vivo alimentando
arquivo. Áudio chegando de uma captura WASAPI enquanto o disco é escrito é outra
coisa, e não foi testada.

**Uma máquina, uma placa.** Tudo na RTX 2060 de 6 GB com um Ryzen 5 3400G de 4
núcleos. O veredito de CPU do Nemotron (§9.1) em particular é sobre este
processador, não sobre o modelo.

**Três conclusões deste relatório já foram corrigidas** — bloco de 1 min (§3),
MOSS reprovado por desempenho (§7), e o convite da agenda como `num_speakers`
(§6). As três eram plausíveis e erradas. É prudente supor que ainda há outras.

---

## 1. O que o bloco custa ao texto — T0.1

Cada gravação foi transcrita quatro vezes pelo **mesmo modelo com os mesmos
parâmetros** (`large-v3`, fp16, `beam_size=5`,
`condition_on_previous_text=False`, VAD em 0,25): uma passada inteira e três em
blocos exatos, **sem sobreposição**. Corte ingênuo de propósito: é o piso
honesto, e emenda com janela sobreposta só melhora.

### 1.1 A divergência entre o bloco e a passada inteira

```
gravação              min      1 min      3 min      5 min
2026-08-21_11-00-33   7,7      9,63%      9,22%      6,82%
2026-08-25_08-59-22  14,6     13,78%     12,63%      7,70%
2026-08-20_15-59-20  32,1     17,27%     15,69%     15,26%
2026-08-27_15-28-37  48,5      9,54%      8,73%      8,41%

agregado                      12,61%     11,53%     10,39%
saldo de palavras               −150       −160        −87   (em 13.221)
```

Dez a treze por cento de divergência parece muito. **Não é o número que
interessa**, e por dois motivos.

O primeiro: a passada inteira **não é gabarito**. Ela é só a outra variante.
Divergência entre duas variantes não diz qual está certa.

O segundo, que aponta para a causa: **a contagem de palavras é praticamente a
mesma** — saldo de ~1% em 13.221 palavras. Não há conteúdo sumindo. O texto está
sendo *reescrito*, não *perdido*.

E há uma pista que elimina a explicação fácil. No bloco de 5 minutos da gravação
de 7,7 min existe **uma única fronteira**, e ainda assim 51 palavras divergem.
Efeito de borda não explica isso. O que explica é o Whisper realinhar as janelas
internas de 30 s: um corte em outro lugar empacota o áudio de outro jeito, e o
modelo decide diferente. Diferente, não pior — o que a §1.2 confirma.

### 1.2 A régua que decide: os dois lados contra o Gemini

O Gemini não é gabarito ([`wer_contra_gemini.py`](../tools/wer_contra_gemini.py)
explica por quê), mas é **independente dos dois**. Se as duas variantes divergem
dele na mesma medida, nenhuma é melhor. Agregado sobre as quatro gravações,
15.565 palavras de referência:

```
variante      divergência   cobertura   faltando   trocadas
inteiro           26,24%      76,28%       2.737        955
bloco 1 min       25,86%      76,86%       2.617        985
bloco 3 min       26,15%      76,74%       2.634        986
bloco 5 min       25,74%      76,85%       2.659        945
```

**Os três tamanhos de bloco divergem menos e cobrem mais que a passada
inteira.** A diferença é pequena — de 0,10 a 0,51 ponto — mas é do mesmo sinal
nos três, e o mecanismo aparece na coluna que importa: **palavras faltando cai
de 78 a 120**. O bloco recupera fala que a passada inteira engole.

Por gravação, contando quem diverge menos que a passada inteira: o bloco de 1
min ganha em 3 de 4, o de 5 min em 4 de 4.

**A causa provável é o VAD, e ela é conhecida deste projeto.** O `vad_filter`
roda sobre o que recebe; cortar em blocos força um recomeço a cada fronteira, e
o gate deixa de engolir trechos que ele engoliria numa varredura longa. É o
mesmo defeito que a [FASE6.md](FASE6.md) §4.3 documenta pelo outro lado. O
troceamento o alivia **de graça, sem querer**.

> **Honestidade sobre o tamanho do efeito:** são 4 gravações, e diferenças de
> 0,1 a 0,5 ponto. Isso estabelece **ausência de degradação**, que era a
> pergunta. Não estabelece melhora — para isso faltam gravações e um intervalo
> de confiança, e não vale gastar GPU nisso: ninguém vai adotar bloco *para
> melhorar o texto*.

### 1.3 O bloco também não é mais lento

```
gravação              inteiro     1 min     3 min     5 min
2026-08-21 ( 7,7min)    4,44x     6,42x     6,49x     5,72x
2026-08-25 (14,6min)    4,55x     4,52x     4,50x     4,24x
2026-08-20 (32,1min)    4,24x     4,33x     4,76x     5,48x
2026-08-27 (48,5min)    6,20x     6,50x     6,71x     6,85x
```

Entre **4,2× e 6,9×** o tempo real, com o modelo quente entre os blocos. O bloco
empata ou ganha — provavelmente porque a lógica de *long-form* do Whisper tem
custo próprio.

**O ciclo de trabalho sai daqui e não depende do tamanho do bloco:** é 1/RTF,
entre **15% e 24%**. Um bloco de 1 minuto ocupa a GPU por ~14 s no pior caso
medido.

---

## 2. O que o bloco custa ao falante — T0.3

Aqui está o custo real do troceamento, e ele decide o tamanho do bloco. Sobre as
49 gravações, sem GPU.

```
                                          1 min     3 min     5 min
falantes por bloco (mediana)                  2         3         4
blocos dentro do teto de 4 (Sortformer)   98,0%     82,5%     69,8%
blocos dentro do teto de 8 (LS-EEND)     100,0%     99,8%     99,7%
falantes órfãos (aparecem num bloco só)    2,9%      6,9%      9,8%
  destes, participantes reais (>50 palavras)   0         3         8
pares (bloco, falante) com voz limpa      84,6%     88,0%     88,8%
segmentos cortados na fronteira            3,9%      1,2%      0,7%
```

### 2.1 O teto de 4 falantes muda de figura

A [FASE7.md](FASE7.md) §3.1 mediu **43%** das gravações dentro do teto de 4 do
Streaming Sortformer e concluiu, corretamente, que "43% não é suficiente".

Mas aquela contagem é **por gravação inteira**. Quem diariza por bloco só vê os
falantes daquele bloco, e num recorte de um minuto fala menos gente: **98,0% dos
blocos de 1 min cabem no teto de 4**. O teto deixa de ser o impedimento que a
carta descreve — desde que se diarize por bloco.

Isso não reabre a §3.1: a conclusão dela continua válida para o desenho que ela
avaliava, que era diarização ao vivo contínua. Muda porque o desenho mudou.

### 2.2 Os órfãos são quem decide o tamanho

Órfão é o falante que aparece num bloco só: não há vizinho para costurar por
continuidade. O número cru assusta menos que a composição dele:

- **bloco de 1 min** — 7 órfãos em 245 falantes, mediana de **8 palavras** na
  reunião inteira, 86% com ≤10 palavras, e **nenhum** com mais de 50. São os
  "bom dia" e os "tchau". Perdê-los não custa nada;
- **bloco de 5 min** — 24 órfãos, dos quais **8 são participantes reais**, um
  deles com **434 palavras**. Esses sairiam da costura como pessoas separadas.

### 2.3 A costura tem base

Em **84,6% a 88,8%** dos pares (bloco, falante) existe pelo menos 1 s de fala
limpa pela régua do próprio app — a de `AprendizadoDeVozes.TrechosDe`, com
`FolgaEntreTurnos = 0,5 s`. Ou seja: na grande maioria dos casos dá para extrair
vetor de voz e ancorar o falante entre blocos, com a máquina que já existe.

É o mesmo trabalho que o *Arrival-Order Speaker Cache* do Sortformer faz por
dentro — só que aqui feito por fora, com peça nossa, e **sem teto de falantes**.

---

## 3. O que o bloco custa à diarização — T0.3b

A §2 mediu a *estrutura*: quantos falantes cabem num bloco, e se dá para
ancorá-los. **Não mediu se o pyannote encontra as mesmas pessoas** quando tem
um minuto de áudio em vez de quarenta. Ele agrupa vozes, e agrupar é o que mais
depende de contexto.

Régua: a diarização da gravação inteira é a referência, a por blocos é a
hipótese, e dentro de cada bloco os rótulos são casados de forma **ótima**
(atribuição húngara). O que sobra é erro que nenhuma costura desfaz.
Ferramenta: [`tools/medir_bloco_diarizacao.py`](../tools/medir_bloco_diarizacao.py).

```
gravação              min  falantes      1 min     3 min     5 min
2026-08-21_11-00-33   7,7         5       9,8%      9,6%      7,7%
2026-08-25_08-59-22  14,6         2       3,6%      2,6%      2,4%
2026-08-20_15-59-20  32,1         2       9,8%      3,5%      3,6%
2026-08-27_15-28-37  48,5         3       6,4%      3,8%      3,8%

agregado                              →   7,3%      4,0%      3,9%
```

**O bloco de 1 minuto custa quase o dobro do de 3.** E de 3 para 5 minutos não
se ganha mais nada (4,0% contra 3,9%) — o pyannote já tem o contexto de que
precisa aos três minutos.

O erro é quase todo **confusão** — 8,1% de 9,8% na gravação de 32 min, com fala
perdida em 0,9% e inventada em 0,7%. Ou seja: o bloco acerta *onde* há fala e
erra *de quem* ela é. A segmentação não sofre; o agrupamento sim.

E o modo de falha tem nome: **o bloco funde pessoas**. Os falantes que o pyannote
encontra por bloco, contra os que a passada inteira vê na mesma janela:

```
                       1 min        3 min        5 min
2026-08-21_11-00-33   2,9→1,9      4,0→3,3      4,5→2,5
2026-08-25_08-59-22   1,9→1,7      2,0→2,0      2,0→2,0
2026-08-20_15-59-20   1,9→1,9      2,0→2,1      2,0→2,1
2026-08-27_15-28-37   2,0→1,4      2,4→1,9      2,6→2,2
```

Com um minuto de áudio, duas pessoas que falam pouco viram uma. Isso **não é o
problema que a §2.3 resolve**: o vetor de voz costura falante entre blocos, mas
não separa dois que já foram fundidos dentro de um.

> **Correção.** Uma versão anterior deste relatório recomendava o bloco de 1
> minuto, com base só na §2 — teto de falantes e órfãos, que de fato o favorecem.
> A medição desta seção inverte a conclusão: **o bloco é de 3 minutos.**

---

## 4. O que isto decide

Juntando os três eixos:

| eixo | 1 min | 3 min | 5 min |
|---|---|---|---|
| texto (§1.2, contra o Gemini) | empata | empata | empata |
| **diarização (§3)** | 7,3% | **4,0%** | 3,9% |
| blocos no teto de 4 falantes (§2.1) | 98,0% | 82,5% | 69,8% |
| órfãos que são participantes reais (§2.2) | 0 | 3 | 8 |
| segmentos cortados na fronteira | 3,9% | 1,2% | 0,7% |
| latência da consciência | 1 min | 3 min | 5 min |

**O bloco de 3 minutos ganha.** Ele tem a diarização do bloco de 5 sem os órfãos
dele, o texto empata em todos os tamanhos, e três minutos continuam sendo
"minutos" para o produto de consciência da reunião — que é o que a fila da Fase 7
chama de produto A.

O que o bloco de 3 min perde é o teto de 4 falantes: 82,5% dos blocos cabem,
contra 98% no de 1 min. **Isso só importa se o diarizador for o Streaming
Sortformer.** Com o pyannote de hoje, que não tem teto, o ponto é irrelevante —
e a §3 mostra que o pyannote por bloco de 3 min já entrega 4,0%.

| pergunta da fila | resposta |
|---|---|
| **T0.1** — o bloco custa qualidade de texto? | **Não.** Empata com a passada inteira, e reduz palavra faltando |
| **T0.3** — dá para costurar o falante? | **Sim**, em ~88% dos pares no bloco de 3 min |
| **T0.3b** — o pyannote por bloco acha as mesmas pessoas? | **Quase.** 4,0% de erro no bloco de 3 min, e o erro é fusão, não segmentação |
| tamanho do bloco | **3 minutos** |
| ciclo de trabalho na 2060 | **15% a 24%** da GPU, com a placa livre |

### O que isto libera na fila

O produto A — a consciência da reunião, com o LLM respondendo sobre o que já foi
dito e o falante ganhando nome cedo — **não depende de modelo novo nenhum**. O
pipeline de hoje, em blocos de 3 minutos, entrega texto equivalente ao da passada
inteira e falante com 4% de erro, ocupando um quinto da GPU.

E o §7 da carta ganha uma saída que ela não previu: como o texto por blocos
empata com o da passada inteira, ele **pode** virar o parcial da
`Nucleo/Retomada.cs` em vez de ser um rascunho descartável. Não é mais a mesma
situação do defeito de 0.4.0 — lá o parcial era de outro modelo, aqui é do mesmo
modelo com os mesmos parâmetros. **Vale só para o texto**: a diarização por bloco
tem 4% de erro contra a inteira, então ela continua sendo rascunho.

---

---

## 5. A janela cumulativa — T0.4

A §3 mostrou que o bloco isolado funde falantes. A §1.3 mostrou por que existe
alternativa: **a diarização custa 32× o tempo real, cinco vezes menos que o
ASR**. Então dá para não trocear a diarização — **rediarizar tudo desde o começo**
a cada passada. Ferramenta:
[`tools/medir_janela_cumulativa.py`](../tools/medir_janela_cumulativa.py), 36
passadas sobre as quatro gravações.

### 5.1 O custo é linear, sem surpresa

```
faixa da janela     RTF médio
    0–10 min          31,8x
   10–20 min          31,9x
   20–40 min          32,7x
   40–60 min          32,2x

ajuste sobre os 36 pontos:  custo ≈ janela / 32,2   (termo fixo −0,2 s)
```

Nenhum termo quadrático: o agrupamento do pyannote não fica mais caro conforme a
janela cresce. **A extrapolação da versão anterior deste relatório (27,9×,
virada aos 84 min) era pessimista** — o número medido é 32,2× e a virada vem
depois.

### 5.2 A cadência é o que controla o custo

Ciclo de trabalho da diarização, por cadência e duração da reunião:

```
reunião        3 min     5 min     9 min    15 min
 30 min          31%       19%       10%        6%
 60 min          62%       37%       21%       12%
 90 min          93%       56%       31%       19%
120 min         124%       75%       41%       25%

a passada deixa de caber na própria cadência em:
  3 min →  97 min de reunião      9 min → 290 min
  5 min → 161 min                15 min → 482 min
```

**A cadência de 9 minutos é a que resolve.** Ela põe a diarização em 21% aos 60
min — o mesmo custo do bloco isolado — e cabe em reunião de até quase cinco
horas. Somada ao ASR em bloco de 3 min (~20%), dá ~41% da GPU aos 60 min, com
folga para o Meet.

### 5.3 O rótulo é estável, depois de esquentar

Rotatividade — quanto da linha do tempo já rotulada troca de dono a cada passada
nova, já com o melhor casamento de rótulos:

```
n = 32 passadas     mediana 0,5%     p90 2,3%     máx 22,8%
acima de 5%: 2 passadas, ambas na mesma gravação
   2026-08-21_11-00-33, até 6 min → 22,8%  (4 falantes)
   2026-08-21_11-00-33, até 8 min →  6,3%  (5 falantes)
```

**Trinta das 32 passadas têm rotatividade abaixo de 5%, e a mediana é 0,5%.** As
duas exceções são a gravação mais difícil do conjunto — 5 falantes em 7,7 min —
e ambas acontecem **nos primeiros 8 minutos**, quando a janela ainda é curta.

Daí sai uma regra de produto que não estava prevista: **não mostrar nome de
falante antes de a janela ter uns 9 minutos.** Antes disso o rótulo ainda se
mexe; depois, praticamente não.

### 5.4 O desenho que os números fecham

| | estratégia | ciclo aos 60 min | erro |
|---|---|---|---|
| **ASR** | bloco isolado de 3 min | ~20% | nenhum (§1.2) |
| **diarização** | janela cumulativa, cadência de 9 min | ~21% | **zero, por construção** |

A janela cumulativa não tem erro de diarização porque, no fim, ela *é* a passada
inteira. Sem costura, sem fusão, sem órfão, e o teto de falantes do Sortformer
deixa de ser assunto — o que também esvazia a §2.1 como argumento.

**Isto substitui a recomendação da §4.** O bloco de 3 min continua valendo para o
ASR; para a diarização, a janela cumulativa é melhor em qualidade e igual em
custo.

---

## 6. O que falta testar

| # | teste | estado |
|---|---|---|
| ~~T0.1~~ | o bloco custa qualidade de texto? | ✅ **não** (§1) |
| ~~T0.2~~ | o bloco com a chamada aberta | ✅ **passou** — ver abaixo |
| ~~T0.3~~ | dá para costurar o falante? | ✅ sim, e a §5 tornou desnecessário |
| ~~T0.3b~~ | o pyannote por bloco acha as mesmas pessoas? | ✅ 4,0%, e a §5 zera isso |
| ~~T0.4~~ | a janela cumulativa | ✅ **é o desenho** (§5) |
| ~~T1.1~~ | MOSS na gravação inteira | ⛔ não escala nesta placa, nem em GGUF (§7.2) |
| ~~T1.2~~ | MOSS em bloco de 3 min | ✅ **ganha do pipeline atual** (§7.4, §7.6) |
| ~~T1.3~~ | MOSS: VRAM e tempo na 2060 | ✅ **2,6× mais rápido**, 0,70 GB (§7.3) |
| ~~T2.1~~ | perguntar ao LLM sobre a reunião | ✅ **cabe até ~60 min** (§10) |
| ~~T3.1~~ | quanto nomear cedo melhora o resto | ✅ **funciona, com zero erro** (§8) |
| ~~T4.4~~ | Nemotron em CPU | ✅ **0,99× — sem folga nesta CPU** (§9) |
| T4.1–4.3 | Sortformer em streaming | **adiado**: o T0.4 removeu a necessidade |
| T4.5 | política de commit | ✅ **existe pronta** no runtime (§9.3) |

### T0.2 — feito em 02/09/2026

Duas gravações reais com a carga rodando, conferidas por
[`carga_de_bloco.py --conferir`](../tools/carga_de_bloco.py):

```
2026-09-02_15-00-28   12,4 min   dropped_samples 0   3,9 correções/min
2026-09-02_15-29-48   35,6 min   dropped_samples 0   9,6 correções/min
```

Zero amostra perdida nas duas, e a deriva ficou **abaixo da mediana do acervo**
(15,6 correções/min) — ou seja, nem sinal de relógio sob pressão. A metade
subjetiva (os outros participantes ouviram picotado?) continua sem sensor.

### O teto do T3.1, que saiu de graça

Antes de medir se nomear cedo funciona, dá para medir **até onde ele poderia
chegar**: quanto da reunião é dito por quem já falou nos primeiros minutos.
Sobre o acervo, ponderado por palavra:

```
quem já falou até o minuto    falantes cobertos    palavras cobertas
                     3 min          72,4%               81,9%
                     5 min          76,8%               86,4%
                    10 min          82,2%               92,8%
                    15 min          87,2%               95,2%
```

**Nomear quem falou nos primeiros 10 minutos cobre 92,8% do que ainda será
dito.** O teto é alto, e casa com a regra da §5.3 — a janela precisa de ~9 min
para o rótulo estabilizar, e aos 10 min a cobertura já é quase toda.

### O que foi descartado sem precisar de teste

- **`num_speakers` vindo do convite da agenda.** Foi cogitado na versão anterior
  deste relatório como conserto barato da fusão. **Não serve**, por dois motivos
  que se somam: o convite é um **limite superior inflado** — clientes incluem
  quem não entra —, e o modo de falha medido na §3 é *fusão*, não excesso de
  divisão. Um teto alto demais não conserta encontrar gente de menos. Como
  `max_speakers` seria inofensivo e inútil; como `num_speakers`, ativamente
  errado.

---

---

## 7. O MOSS na RTX 2060 — T1.1 a T1.3

Medido em 03/09/2026. O `OpenMOSS-Team/MOSS-Transcribe-Diarize` (0,9 B,
Apache-2.0) faz texto e falante numa passada só, e venceu o 2º MLC-SLM do
Interspeech 2026 numa prova com ~200 h de português.

> ### A correção, e ela é o principal desta seção
>
> **A primeira versão desta medição reprovou o MOSS por desempenho, e estava
> errada.** Ela rodou o modelo pelo `transformers`, onde ele estourava 8,88 GB
> de VRAM aos 8 minutos de áudio numa placa de 6 GB e não terminava uma gravação
> de 7,7 min em vinte minutos.
>
> **Aquilo não era o modelo, era o runtime.** O PyTorch codifica o áudio inteiro
> de uma vez; o port em ggml fatia em blocos de 30 s, codifica cada um e
> concatena. Na mesma placa, com o mesmo modelo em GGUF, a gravação de 7,7 min
> sai em **51 segundos**.
>
> A lição de método: eu atribuí ao modelo uma propriedade da implementação, e a
> conclusão teria matado um candidato bom se ficasse no documento.

### 7.1 O runtime muda a escala do problema

Mesma gravação de 7,7 min, mesma RTX 2060:

```
                      PyTorch (transformers)    GGUF (transcribe.cpp)
carga do modelo               8,0s                      1,3s
passada inteira          >20 min (interrompida)        51,2s
fator de tempo real      0,95x aos 8 min, caindo       9,02x
modelo em disco            1,82 GB bf16              0,70 GB Q5_K_M
pico de memória          8,88 GB aos 8 min          não estoura
```

Para comparar com o que roda hoje na mesma gravação: `faster-whisper` leva 104 s
e o pyannote 18,4 s — **~122 s para os dois motores**. O MOSS em GGUF faz os dois
em **51 s**.

O runtime é o [`transcribe.cpp`](https://github.com/handy-computer/transcribe.cpp)
(MIT, ggml), com **binário Linux CUDA pronto** — não precisa compilar. Ele carrega
16+ famílias, e as mesmas ligações servem para Sortformer, Voxtral Realtime e
Parakeet streaming: **o T4 inteiro passa a ser trocar o caminho de um arquivo.**

> **Armadilha do build GGUF:** ele declara `languages = ('en', 'zh')` e **recusa
> `pt`** com `UnsupportedRequest`. Mas transcreve português corretamente quando o
> parâmetro `language` é omitido. A lista de capacidades do port está
> subdeclarada; o modelo não está. Passar o idioma faria alguém descartá-lo por
> engano.

### 7.2 A passada inteira ainda não escala — o bloco, sim

```
gravação        passada inteira       bloco de 3 min
 7,7 min        51,2s  ( 9,02x)      45,8s  (10,09x)
14,6 min       176,7s  ( 4,97x)      89,1s  ( 9,86x)
32,1 min       interrompida a 1h36   (medindo)
                      (<0,35x)
```

**O bloco fica plano em ~10×; a passada inteira degrada com a duração.** É o
mesmo mecanismo do PyTorch — geração autoregressiva sobre contexto que cresce —
só que muito mais suave. Ainda assim, a 6 GB ele bate no teto entre 15 e 30
minutos: a de 32,1 min foi interrompida depois de **1h36 a 0,35× o tempo real**,
com a VRAM em 5794 de 6144 MiB, que é a mesma condição de quase-estouro do
PyTorch.

Conclusão de desenho: **o MOSS serve em bloco, não em passada inteira.** Isso o
põe como candidato ao produto A (§5), não como substituto do pipeline offline.

### 7.3 Em bloco, a velocidade não degrada com nada

Sete passadas em blocos de 3 min, de 7,7 a 122 minutos de áudio:

```
duração      tempo      RTF   blocos
  7,7 min    45,8s   10,09x        3
 14,6 min    85,3s   10,30x        5
 32,1 min   205,0s    9,40x       11
 41,0 min   241,0s   10,22x       14
 48,5 min   264,7s   10,98x       17
121,9 min   559,5s   13,07x       41
```

**Plano, e a mais longa é a mais rápida** — a carga do modelo amortiza sobre
mais blocos. Nem duração nem número de falantes movem a agulha.

Para dimensionar: na gravação de 32 min o MOSS fez texto **e** falante em 205 s.
O pipeline de hoje leva 454 s só no ASR, mais 75 s de diarização — **529 s, dois
motores, 2,6× o tempo**.

### 7.4 Qualidade contra o Gemini — ele ganha

Quatro gravações com transcrição paralela do Meet. A coluna do app é a saída
**final** dele, com filtro de silêncio, correção fonética e revisão de termos; a
do MOSS é crua.

```
                    TEXTO (divergência / cobertura)      FALANTE
gravação          app              MOSS bloco          app     MOSS
 7,7 min · 6 fal  24,6% / 77,2%   23,7% / 79,8%      95,0%   94,2%
14,6 min · 3 fal  23,1% / 80,4%   24,4% / 81,7%      97,9%   99,8%
32,1 min · 3 fal  31,7% / 70,3%   27,7% / 76,3%      92,6%   98,5%
48,5 min · 3 fal  23,5% / 78,8%   20,3% / 82,6%      96,6%   99,5%
```

**Menor divergência em 3 de 4, maior cobertura em 4 de 4, melhor falante em 3 de
4** — e por margem grande no falante (5,9 pontos na de 32 min).

O mecanismo do texto está na coluna `faltando`: **1185 → 683** na de 32 min,
**1189 → 737** na de 48 min. O MOSS recupera fala que o nosso pipeline engole,
porque não usa VAD separado. É o gate da [FASE6.md](FASE6.md) §4.3 outra vez, e
desta vez resolvido por não existir. O custo é mais palavra trocada (340 → 532) e
mais sobrando (103 → 208); o saldo é a favor.

> **A régua de falante favorece o MOSS, e isso pesa mais agora que ele ganha.**
> Os rótulos dele são locais ao bloco (`b0_S1`, `b1_S1`…), e o `casar_nomes` casa
> cada bloco com os nomes da referência **independentemente** — ele recebe de
> graça a costura entre blocos, que é o trabalho difícil. Os números do app são
> de ponta a ponta. O que estes números medem é **separação dentro do bloco**, e
> nisso ele é excelente; não medem identidade entre blocos.

### 7.5 Ele não desaba com mais gente — a hipótese caiu

As quatro com Gemini são todas do lado fácil (3, 3, 3 e 6 falantes). Para testar
o eixo que faltava, duas sem export — 41 min com **11 falantes** e 122 min com 8
— com a saída do app como referência via
[`tools/como_gemini.py`](../tools/como_gemini.py). Com a referência do mesmo tipo
em todas, a série fica comparável:

```
falantes   duração   divergência   cobertura
    3      14,6 min      23,6%       88,4%
    3      32,1 min      31,2%       87,2%
    3      48,5 min      20,0%       90,5%
    6       7,7 min      20,5%       88,3%
    8     121,9 min      20,2%       89,4%
   11      41,0 min      21,9%       88,6%
```

**A de 11 falantes fica no meio da faixa, e a cobertura é plana em ~88%.** O pior
caso é uma de 3 falantes. Não há degradação com número de pessoas nem com
duração.

Isso **desfaz uma leitura anterior deste relatório**: os 4,9 pontos que o MOSS
perdia na gravação de 6 falantes eram artefato do PyTorch. Com o runtime certo,
aquela mesma gravação foi a 20,5% — a segunda melhor da série.

No falante, contra os rótulos do próprio app: **95,6%** na de 11 falantes (83% dos
segmentos limpos) e **90,3%** na de 122 min (69% limpos). A de 122 min é o ponto
mais fraco de todas as medições, e é a única acima do ponto de virada da §5.2.

### 7.6 Veredito — o oposto do que esta seção dizia antes

| | app hoje | MOSS em bloco de 3 min |
|---|---|---|
| texto | referência | **ganha** em 3 de 4 contra o Gemini |
| cobertura | referência | **ganha em 4 de 4** |
| falante | referência | ganha em 3 de 4, com régua favorável |
| custo | 4,4–6,9× (ASR) + 32× (diar.) | **9,4–13,1×**, fazendo os dois |
| modelo em disco | ~3,1 GB + 58 MB | **0,70 GB** |
| dependências | Python, torch, CTranslate2, pyannote | **um binário ggml** |

**O MOSS em GGUF é um candidato sério a substituir os dois motores** — em bloco,
que é o regime do produto A de qualquer forma. Ele é mais rápido, ocupa um quarto
do disco, e troca quatro dependências por uma.

O que falta antes de qualquer decisão:

* **a costura entre blocos**, que a régua deu de graça e o app teria de fazer. A
  §2.3 diz que há voz limpa para ancorar em ~88% dos pares; falta medir o
  resultado real;
* **a passada inteira continua fora** (§7.2), então ele não substitui o pipeline
  offline nesta placa — substitui o pipeline *em bloco*;
* **n = 6 gravações**, e só 4 com referência independente.

## 8. Nomear o falante cedo — T3.1

Medido em 03/09/2026 sobre **40 gravações** do acervo (as de 15 min ou mais, com
pelo menos dois falantes além do dono). Ferramenta:
[`tools/medir_nomear_cedo.py`](../tools/medir_nomear_cedo.py).

A operação medida é a do produto: aprende-se a voz de cada participante nos
primeiros N minutos, e depois, **para cada bloco e cada falante daquele bloco**,
monta-se um vetor e compara-se com o que foi aprendido. Todas as constantes são
as do app (`SegundosMinimos = 3,0`, `SegundosDoTrecho = 4,0`,
`FolgaEntreTurnos = 0,5`, `LimiarDeReconhecimento = 0,70`).

**O dono do microfone fica de fora de propósito** — ele já vem de graça da faixa
separada. O problema é nomear os outros.

### 8.1 O app nunca erra de pessoa

```
corte    acerto    erro   não-rec   sem voz     fora   precisão   cobertura
 3 min    31111      19     50986      1154    53356      99,9%      37,4%
 5 min    42078      13     46716      1255    39265     100,0%      46,7%
10 min    41867       0     49388      1384    15703     100,0%      45,2%
```

**Zero palavras atribuídas à pessoa errada** no corte de 10 minutos, em 42 mil
palavras julgadas. Dezenove no corte de 3 min. Esse é o modo de falha certo
para uma feature de nomeação: um nome errado na tela é pior que nenhum, porque
o usuário confia nele e a ata herda o erro.

O preço é cobertura: **~45%**. E ela **não melhora** indo de 5 para 10 minutos de
aprendizado (46,7% → 45,2%), o que identifica o gargalo — não é a quantidade de
fala aprendida, é o limiar.

### 8.2 O limiar de 0,70 está caro demais

Varrendo o limiar sobre as mesmas decisões, sem reprocessar áudio:

```
corte de 10 min                     corte de 5 min
limiar  cobertura  precis.  erradas | limiar  cobertura  precis.  erradas
  0,70      45,9%   100,0%        0 |   0,70      47,4%   100,0%       13   ← hoje
  0,65      64,5%   100,0%        0 |   0,65      64,5%    99,9%       52
  0,60      78,5%   100,0%        0 |   0,60      76,7%    99,9%       52
  0,55      86,1%    99,6%      285 |   0,55      85,9%    99,6%      337   ← o penhasco
  0,50      90,4%    99,4%      463 |   0,50      91,7%    99,5%      412
```

**Baixar de 0,70 para 0,60 compra 33 pontos de cobertura — 45,9% para 78,5% — e
não custa erro nenhum.** O penhasco está em 0,55, onde os erros saltam de zero
para centenas. A margem entre 0,60 e 0,55 é larga e o comportamento dos dois
lados é inequívoco.

### 8.3 A ressalva que impede a mudança direta

> **Isto mede casamento *dentro* da mesma reunião**, e o `LimiarDeReconhecimento`
> governa também o banco de vozes **entre** reuniões — outro dia, outro fone,
> outra sala. São problemas diferentes: aqui o alvo e a amostra vêm do mesmo
> áudio, com as mesmas condições acústicas, o que é o caso fácil.
>
> A conclusão que os dados sustentam é **um limiar mais frouxo para o casamento
> intra-reunião**, não a troca da constante global. O app já tem os dois usos e
> hoje os trata igual; separá-los é uma linha de código e uma decisão de desenho,
> não uma consequência automática desta medição.

### 8.4 O que isto decide

* nomear cedo **funciona**, e funciona com segurança: a 0,60, quatro quintos da
  fala restante ganham nome sem um único erro;
* o teto continua sendo quem nunca falou antes do corte — a coluna `fora`, que
  cai de 53 mil palavras (corte de 3 min) para 16 mil (corte de 10 min). Bate com
  o teto da §6: aos 10 minutos, 92,8% do que ainda será dito é de alguém já
  conhecido;
* em reunião grande a estratégia rende menos, e isso é estrutural. Numa das
  gravações, com 7 participantes além do dono, foram 8.871 palavras `fora`
  contra 123 acertos: quase todo mundo fala pela primeira vez tarde;
* a regra dos 9 minutos da §5.3 casa com isto: a janela precisa esquentar antes
  de o rótulo estabilizar, e aos 10 minutos a cobertura já é quase toda.

---

---

## 9. O Nemotron em streaming — T4.4

Medido em 03/09/2026. O `nemotron-3.5-asr-streaming-0.6b` (NVIDIA, OpenMDW-1.1)
é o único candidato que poderia fazer o texto ao vivo custar **zero GPU** — o que
dissolveria o §4.2 da [FASE7.md](FASE7.md), a disputa de placa com o Meet. Roda
no mesmo `transcribe.cpp` do MOSS: 0,75 GB em Q8_0.

**Ao contrário do MOSS, ele declara o português corretamente:**

```
languages = ('en-US','en-GB','es-US','es-ES','fr-FR','fr-CA','it-IT',
             'pt-BR','pt-PT','nl-NL','de-DE','tr-TR','ru-RU', ...)
supports_streaming = True    supports_language_detect = True
max_timestamp_kind = token
```

### 9.1 Em CPU ele não tem folga

```
 2,0 min (quase só silêncio) → 1,69x
 7,7 min (fala real)         → 0,99x    ← o número honesto
 7,7 min em CUDA             → 8,85x
```

O 1,69× inicial era artefato: os dois primeiros minutos daquela gravação são
quase todos silêncio, e havia pouco o que decodificar. **Com densidade de fala
real ele fica em 0,99×** — exatamente o tempo real, sem margem. Para o produto em
bloco isso reprova: um bloco de 3 min levaria 3 min, e a fila nunca drena.

**A ressalva importa:** a máquina é um Ryzen 5 3400G de 4 núcleos. A conclusão é
sobre esta máquina, não sobre o modelo. Num processador moderno a margem
provavelmente existe, e o §4.2 voltaria a ser dissolvido — vale remedir se
aparecer hardware melhor.

### 9.2 Contra o MOSS, ele empata no texto e perde no resto

```
                   TEXTO contra o Gemini (divergência / cobertura)
gravação          app             MOSS bloco       Nemotron bloco
 7,7 min      24,6% / 77,2%     23,7% / 79,8%     26,2% / 76,1%
14,6 min      23,1% / 80,4%     24,4% / 81,7%     22,6% / 81,3%
32,1 min      31,7% / 70,3%     27,7% / 76,3%     27,2% / 76,2%
48,5 min      23,5% / 78,8%     20,3% / 82,6%     20,7% / 82,5%

                   VELOCIDADE em bloco (CUDA)
 7,7 min   MOSS 10,09x   Nemotron 8,46x
14,6 min   MOSS 10,30x   Nemotron 8,61x
32,1 min   MOSS  9,40x   Nemotron 6,47x
48,5 min   MOSS 10,98x   Nemotron 3,77x
```

**Empate técnico no texto** — cada um ganha 2 de 4, por 0,4 a 0,5 ponto. Mas o
MOSS é plano em velocidade e o Nemotron cai de 8,46× para 3,77×, **em blocos**,
que deveriam ser independentes. Como reuso o mesmo objeto `Model` entre blocos, a
causa provável é acúmulo de estado na sessão — a API tem `Stream.reset()`, o que
sugere que estado se acumula. **É ressalva de medição, não propriedade
confirmada**, mas para um produto que roda uma hora seguida importa, e é barato
de checar depois.

E o Nemotron **não faz diarização nenhuma**, enquanto o MOSS entrega o falante
junto.

### 9.3 O que ele é, então

Não é o motor do produto A. É o motor do **produto B** — a legenda ao vivo —, e
para isso ele traz o que o MOSS não tem: streaming de verdade. A API do
`transcribe.cpp` expõe exatamente as peças que a carta pedia:

```
CommitPolicy:  'auto' | 'on_finalize' | 'stable_prefix'
StreamText:    full | committed | tentative
StreamUpdate:  is_final, revision, buffered_ms, committed_changed
```

**`stable_prefix` é o LocalAgreement** que a §3.2 da carta propunha implementar, e
a separação `committed`/`tentative` é o que impede o texto de tremer na tela —
os dois problemas que eu havia listado como trabalho em aberto, resolvidos por
configuração.

Mas para o produto B ele tem o defeito da §9.1: **0,99× em CPU obriga a disputar
a GPU**, que era justamente o que ele deveria evitar.

---

## 10. O LLM respondendo sobre a reunião — T2.1

Medido em 03/09/2026, com o **llama.cpp e o Qwen3-4B da própria instalação**
(via `cmd.exe`, com CUDA), sobre a gravação de 122 min — a única do acervo que
cobre uma hora de reunião. Ferramenta:
[`tools/medir_llm_ao_vivo.py`](../tools/medir_llm_ao_vivo.py).

O que decide não é a qualidade da resposta, é a conta: **o prompt *é* a
transcrição corrente**, e ela só cresce.

### 10.1 A espera do usuário

```
corte de reunião   contexto        pior espera
     10 min        1.316 palavras     11,4s     ok
     30 min        3.575 palavras     12,8s     ok
     60 min        8.405 palavras     18,9s     no limite
     90 min       10.867 palavras     22,8s     passou
```

A régua de 20 s é **escolha, não medição** — acima disso deixa de ser resposta ao
vivo e vira "abri um relatório". Se 30 s servir, o teto passa dos 90 minutos.

A degradação aparece nos dois eixos: geração de 66 → 35 t/s, prompt de 1861 →
1389 t/s. É a atenção sobre a transcrição acumulada — o mesmo mecanismo que
derruba MOSS e Nemotron na passada inteira, aqui em versão suave.

### 10.2 Três decisões que isso fecha

* **sob demanda funciona, e é o desenho certo.** Os tempos acima **incluem
  carregar o modelo** (~5 s), porque medem o pior caso: subir o Qwen3-4B,
  responder, descarregar. Não é preciso prender 2,5 GB de VRAM a reunião inteira
  — some a disputa de memória com o ASR e a diarização, que era a conta levantada
  como provável impedimento;
* **o 4B serve.** Uma versão anterior deste relatório sugeria o 1.7B para caber
  na vaga; ele nem existe mais na pasta de modelos, e não é preciso;
* **reunião longa precisa de outra estratégia.** Acima de ~60 min o prompt cresce
  demais. A saída óbvia é não mandar a transcrição inteira — um resumo corrente
  mais os últimos N minutos. É trabalho de desenho, não de modelo, e **não foi
  medido**.

### 10.3 A qualidade parece estar lá

Numa reunião técnica real em português, aos 30 minutos o modelo extraiu os quatro
endpoints do agente em discussão, nomeados corretamente; aos 90, o fluxo de
autenticação e as pendências com responsável nomeado.

> **Isto não é uma medição de qualidade.** São quatro respostas lidas por mim,
> sem gabarito e sem régua. Serve para dizer que o caminho não está quebrado —
> não para dizer que está bom. A régua da ata (`tools/auditar_atas.py`) existe e
> não foi aplicada aqui.

---

---

## 11. A costura de falante entre blocos — T1.4

Medido em 03/09/2026. Ferramenta:
[`tools/medir_costura.py`](../tools/medir_costura.py).

**O buraco que isto fecha.** A §7.4 mostra o MOSS ganhando no falante, com uma
ressalva grande: os rótulos dele são locais ao bloco, e a régua casa cada bloco
com a referência **independentemente** — ele recebia de graça o trabalho difícil.
Este teste paga essa conta.

**O algoritmo é o que o app faria, e não olha o futuro:** percorre os blocos em
ordem de chegada, monta o vetor de voz de cada falante do bloco, compara com as
identidades já conhecidas, e atualiza o centroide quando casa. É o *Arrival-Order
Speaker Cache* do Sortformer feito por fora, com o `wespeaker` que já está
instalado — e **sem teto de falantes**.

### 11.1 A costura custa cerca de um ponto

```
gravação            costurado    régua de graça (§7.4)    custo
 7,7 min · 6 fal      92,6%           94,2%              −1,6
14,6 min · 3 fal      99,8%           99,8%               0
32,1 min · 3 fal      95,1%           98,5%              −3,4
48,5 min · 3 fal      99,5%           99,5%               0
41,0 min · 11 fal     95,4%           95,6%              −0,2
121,9 min · 8 fal     89,4%           90,3%              −0,9
```

**A ressalva da §7.4 se resolve por ~1 ponto**, não pelo desabamento que se podia
temer. O MOSS continua ganhando do pipeline atual mesmo pagando a costura.

### 11.2 O limiar não regula acerto — regula quanta gente o app inventa

```
gravação           reais    0,45         0,55         0,65         0,75
 7,7 min             6    6/92,6%     7/92,6%      8/94,2%     12/94,2%
14,6 min             3    3/99,8%     4/99,8%      5/99,8%      9/99,8%
32,1 min             3    3/95,1%     3/95,1%      7/95,1%     15/95,1%
48,5 min             3    4/99,5%     4/99,5%      5/99,5%     13/99,5%
41,0 min            11   20/91,4%    21/95,4%     24/95,4%     34/95,5%
121,9 min            8   11/89,2%    15/89,2%     23/89,7%     44/89,6%
```

**A acurácia é quase insensível ao limiar; a contagem de pessoas não é.** Em 0,75
a costura conclui que há **44 falantes onde há 8**. A métrica de acerto não
enxerga isso, porque ela casa rótulos de forma ótima e dividir custa menos que
fundir — mas na tela apareceriam 44 pessoas, e alguém teria de juntá-las.

**0,55 é o ponto.** Acerto no máximo ou junto dele em todas as seis, e a contagem
muito mais perto da real — em reunião pequena chega a acertar. Abaixo disso
começa a fundir demais: a de 41 min perde 4 pontos em 0,45.

### 11.3 Duas medições independentes apontam para o mesmo lugar

O T3.1 achou **0,60** para nomear cedo; a costura acha **0,55**. Os dois dizem
que o `Vozes.LimiarDeReconhecimento = 0,70` é apertado demais **para casamento
dentro da mesma reunião** — o que reforça a §8.3: esse limiar precisa ser
separado do que governa o banco de vozes **entre** reuniões, que é o caso difícil
e onde 0,70 pode estar certo.

### 11.4 O que isto promove no backlog

A divisão excessiva deixa de ser hipótese e vira número: mesmo em 0,55, a reunião
de 122 min sai com 15 identidades para 8 pessoas. **O `VOZ-1` do
[BACKLOG.md](BACKLOG.md) — "sugerir fusão de perfis parecidos" — deixa de ser
conveniência e vira requisito** de qualquer desenho que costure por bloco.

---

## 12. Como reproduzir


```bash
# estrutura — segundos, sem GPU
uv run python tools/simular_blocos.py --blocos 60,180,300

# qualidade — ~85 min de GPU nas quatro gravações com Gemini
uv run python tools/medir_bloco_asr.py \
    --varredura ~/.cache/pulsemeet-medicoes/varredura \
    --json ~/.cache/pulsemeet-medicoes/t01.json

# diarização por bloco — ~25 min de GPU
uv run python tools/medir_bloco_diarizacao.py --blocos 60,180,300

# a janela cumulativa — ~40 min de GPU
uv run python tools/medir_janela_cumulativa.py --cadencia 180

# o T0.2, durante uma reunião de verdade
uv run python tools/carga_de_bloco.py
uv run python tools/carga_de_bloco.py --conferir "<pasta da gravação>"

# MOSS, no venv separado (ver o cabeçalho de tools/medir_moss.py)
~/.cache/pulsemeet-medicoes/venv-moss/bin/python tools/medir_moss.py \
    --so-blocos --varredura ~/.cache/pulsemeet-medicoes/varredura-moss

# nomear cedo, com a varredura do limiar — ~40 min de GPU
uv run python tools/medir_nomear_cedo.py --cortes 3,5,10 --minimo-min 15

# a costura entre blocos, com varredura de limiar — ~20 min de GPU
uv run python tools/medir_costura.py --limiares 0.45,0.55,0.65,0.75

# o LLM respondendo sobre a reunião (usa o llama.cpp do app, via cmd.exe)
uv run python tools/medir_llm_ao_vivo.py --cortes 10,30,60,90

# a régua que decide, uma vez por gravação
uv run python tools/wer_contra_gemini.py \
    ~/.cache/pulsemeet-medicoes/varredura/<gravação>__* \
    --gemini "<acervo>/<gravação>/gemini.md"
```
