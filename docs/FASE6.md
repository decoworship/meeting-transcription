# Fase 6 — a qualidade do que sai: carta de execução

Escrita em 14/08/2026, a pedido do dono do produto, depois de uma comparação que
não estava no plano: a reunião de 13/08 às 14:30 (`Sherlock Diário – Status e
Ações`, Vivo / Faturamento B2B, 29,2 min, 5 falantes) foi transcrita **em
paralelo pelo app e pelo Notion**, e as duas saídas foram postas lado a lado.

O app ganhou no que importa — falantes nomeados, timestamps, os números certos.
O Notion soletrou `27.529` como "27, 5, 2, 9" e escreveu "Jimmy", "Chudu" e
"Odime" para a mesma pessoa. Mas ele **registrou uma pergunta que o app não
registrou**, e puxar esse fio descobriu que o vocabulário do projeto — o
mecanismo que faz o app acertar "Dimi" — está cobrando um preço que ninguém
tinha medido.

Esta fase é sobre esse preço. Ela vem **depois da Fase 5** por escolha do dono do
produto: nada aqui bloqueia a ata, o instalador ou o acabamento visual, e tudo
aqui melhora o que o app já entrega hoje.

---

## 1. O que a fase entrega

Uma transcrição que perde menos fala e atribui melhor os falantes, **sem trocar
motor nenhum e sem perder o acerto dos nomes próprios**. Três coisas concretas:

1. o `hotwords` deixa de ser o único caminho do vocabulário, e o trade-off passa
   a ser uma decisão medida em vez de herdada;
2. os vetores de voz param de ser aprendidos de áudio com mais de uma pessoa
   dentro;
3. o gate do VAD 0,15, aberto desde a Fase 1, fecha — ou é declarado
   irrelevante, com número.

---

## 2. A medição que originou a fase

Todas as medidas abaixo são sobre `2026-08-13_14-30-15`, o mesmo `mix.wav` que o
app escreveu e o motor abriu. Nenhuma delas exigiu anotador humano.

### 2.1 O perfil do áudio

| faixa | silêncio digital | fala (RMS > 0,01) |
|---|---|---|
| `mic.wav` | 89,3% | 1,5% |
| `system.wav` | 1,5% | 72,1% |
| `mix.wav` | **0,3%** | 73,3% |

O `mix.wav` tem 21,4 min de fala. E tem **0,3% de silêncio digital**, contra os
32,2% da gravação do resultado 6-A — o que já antecipa o item 2.2.

### 2.2 O VAD não é o problema, e o gate continua aberto

Varredura com [`tools/sweep_vad.py`](../tools/sweep_vad.py), sem `hotwords`:

```
config              palavras   em fala   em silêncio   % inventado    tempo
0.35:500 (hoje)         4055      3344           2,9        0,07%      376s
0.25:500                4036      3325           2,7        0,07%      396s
0.15:500                4049      3332           2,9        0,07%      962s
0.1:300                 4051      3346           3,1        0,08%      557s
```

**O limiar não faz diferença aqui** — 19 palavras separam o melhor do pior, e o
eixo de invenção mede ruído: com 0,3% de silêncio digital não há onde alucinar
mensuravelmente. Somar mic e sistema quase nunca dá zero exato, e é isso que
apaga o critério.

Isto **não confirma nem refuta o 0,15** do gate registrado em
[FASE1.md](FASE1.md) §7.2. Diz outra coisa, também útil: **o `mix.wav` é o áudio
errado para essa medição**, e por isso o gate ficou um mês sem fechar. O áudio
certo é o `system.wav` de reuniões em que o dono fala pouco — que é o padrão de
uso real — ou o `mic.wav`, que aqui é 89,3% silêncio digital.

### 2.3 O `hotwords` é a causa, e o efeito é estrutural

Mesmo `mix.wav`, mesmo `large-v3`, mesmo VAD `0.35:500`. Só o `hotwords` muda —
e ele recebe o `initial_prompt` do projeto inteiro, 72 palavras, reinjetadas a
cada janela de 30 s:

| | segs | palavras | dur. mediana | cobertura da fala | segs > 25 s | tempo |
|---|---|---|---|---|---|---|
| app (o arquivo entregue) | 205 | 3835 | 3,6 s | 88,0% | 24 | — |
| rerun **com** `hotwords` | 207 | 3540 | 4,2 s | 84,1% | 15 | 1514 s |
| rerun **sem** `hotwords` | **787** | **4055** | **1,8 s** | **89,8%** | **0** | **331 s** |

**Isto é uma replicação, não uma descoberta.** O resultado 5 da Fase 0 já tinha
medido o mesmo efeito — 3409 contra 3692 palavras, **279 contra 752 segmentos**,
2,5× mais lento — e registrou uma ressalva: aquela era a gravação com o
microfone morto em 95% do tempo, e *"repetir numa gravação saudável separaria as
duas coisas. Fica como pendência."*

**Esta é a gravação saudável, e a pendência fecha:** 0,3% de silêncio digital,
73,3% de fala, e o efeito se repete com a mesma forma e maior magnitude. Fecha
também o item 3 das trilhas paralelas da [FASE1.md](FASE1.md) §7.

Quatro efeitos, em ordem de importância:

**A segmentação colapsa.** 205–207 segmentos com `hotwords`, 787 sem —
**3,8×**. E **24 segmentos acima de 25 s contra zero**. Este é o efeito robusto:
as duas execuções com `hotwords` concordam entre si e a sem `hotwords` está
noutro regime.

**Perde-se fala.** Entre 1,8 e 5,7 pontos de cobertura, entre 220 e 515
palavras. *A margem é honesta:* o arquivo entregue (3835 palavras) e a
reexecução na configuração idêntica (3540) diferem em 295 palavras, variação que
não controlei. A direção é firme, a magnitude é um intervalo.

**Custa 4,6× o tempo de decodificação** — 1514 s contra 331 s. Numa reunião de
29 min, 25 minutos de GPU contra 5.

**Injeta termo não dito.** O `NoBill vai ter tudo` em `[16:09]` do arquivo
entregue: "NoBill" está no `initial_prompt` e não estava na fala.

### 2.4 Por que a segmentação é o efeito grave

A atribuição de falante é **um rótulo por segmento do ASR**
([`Transcricao.cs:126`](../app-net/Nucleo/Transcricao.cs#L126)): soma-se a
sobreposição por falante e o segmento inteiro recebe o vencedor. Não existe
granularidade menor. Quando o segmento tem várias pessoas dentro, as
minoritárias somem sem deixar rastro.

Nesta gravação, com `hotwords`:

```
segmentos > 10 s:  46 (22% dos segs) carregam 65% das palavras
segmentos > 25 s:  24 (12% dos segs) carregam 46% das palavras
```

**Metade do texto está em segmentos longos demais para ter um dono só.** O
primeiro deles tem 43,7 s — `[5.8–49.5]`, rotulado "Vanessa Levorato", e contém
a saudação inteira com três pessoas. Sem `hotwords`, a mesma janela vira 13
segmentos, cada um com um falante:

```
  5.8-19.9 (14,1s) Oi gente, boa tarde
 24.4-25.6 ( 1,2s) E o bebê?
 27.6-28.9 ( 1,3s) Tá bem, ele tá
 28.9-31.0 ( 2,1s) Foi dar uma volta, visitar a avó
 32.4-34.1 ( 1,6s) Casa de avó
 35.1-38.0 ( 2,8s) Tá com a babá
 ...
```

Isto reabre, por um caminho novo, o diagnóstico do resultado 3-D da Fase 0 — *"o
problema é atribuição, não contagem"*. Parte da atribuição ruim não é do
pyannote: é do ASR não dar onde pousar.

### 2.5 A contaminação dos vetores de voz

É a consequência que o usuário nota primeiro (a confusão de falantes na
saudação) e a que custa mais caro, porque **persiste entre reuniões**.

[`AprendizadoDeVozes.cs:42-64`](../app-net/Nucleo/AprendizadoDeVozes.cs#L42-L64)
ordena os trechos `OrderByDescending(t => t.Duracao)` e a
[linha 153](../app-net/Nucleo/AprendizadoDeVozes.cs#L153) passa os **3 maiores**
ao extrator. A guarda de cross-talk (`FolgaEntreTurnos`) olha só os **vizinhos**
do segmento; não há nenhuma guarda contra outra pessoa **dentro** dele.

Ordenar por duração é, portanto, uma preferência ativa pelos segmentos de maior
risco. Medido contra o `mic.wav`, que é verdade e não estimativa:

```
mic com fala na gravação inteira:      1,5%
bloco #1 do vetor da Vanessa (5.8–49.5):  30,1% do bloco  (13,2 s)
bloco #2 da Vanessa                        0,0%
bloco #3 da Vanessa                        0,0%
blocos de Ellen e de Monlevade             0,0%
```

**13,2 segundos da voz do dono entraram no vetor de voz da Vanessa** — 20× a
taxa base. O `.wav` guardado em `trechos/` está limpo, porque `RecortarTrecho`
corta em `SegundosDoTrecho = 4.0`; o **vetor** usa o intervalo inteiro. Quem
audita a amostra ouvindo o arquivo não encontra o defeito.

O `AtribuirDono` não salvou porque também é por segmento: 13,2 s diluídos em
43,7 s não passam do `MargemDoDono = 2.0`.

### 2.6 Dois achados de código que a investigação encontrou de passagem

**O `FiltroDeSilencio` nunca roda.** `Transcritor.ExecutarAsync` recebe
`filtrarSilencio = false` por default
([`Transcritor.cs:170`](../app-net/Nucleo/Transcritor.cs#L170)) e a
[`Ponte.cs:907`](../app-net/App/Ponte.cs#L907) não passa o parâmetro. A classe
inteira — com a justificativa do resultado 6-A, os testes e as duas constantes
calibradas — é código morto no caminho da GUI. Não afetou nada nesta gravação
(0,3% de silêncio digital), mas afeta o `system.wav` de reuniões normais.

**O `word_timestamps` é calculado e jogado fora.**
[`motores/asr/motor.py:99`](../motores/asr/motor.py#L99) pede
`word_timestamps=True`; a [linha 114](../motores/asr/motor.py#L114) guarda só
`inicio`, `fim` e `texto`. Paga-se o alinhamento por palavra e descarta-se o
resultado — que é exatamente o insumo para cortar segmento na troca de falante.

---

## 3. O trade-off já foi resolvido — e a decisão nunca foi aplicada

Este é o achado desconfortável da investigação, e vale dizer sem rodeio: **a
Fase 0 já respondeu a pergunta, e concluiu contra o `hotwords`.**

O resultado 5 justificou o vocabulário com uma medida forte — **"Dimi", 9
ocorrências com `hotwords`, 1 sem** — e a comparação com o Notion, que escreveu
"Jimmy", "Chudu", "Odime" e "Helen", confirma em reunião real que o problema é
verdadeiro.

Mas o **resultado 5-A** mediu o caminho alternativo:

```
motor                       antes  depois  ganho   trocas
faster-whisper com-prompt      36      36     +0   —
faster-whisper sem-prompt      25      36    +11   Jimmy→Dimi×10, Helen→Ellen×1
```

**A correção fonética a jusante empata com o `hotwords`** — 36 contra 36, sobre
uma referência de 37. E o 5-A registrou a conclusão com todas as letras:
*"melhor ainda, o prompt fica dispensável — e isso é ganho, porque o prompt
custa 2,5× mais tempo, menos da metade dos segmentos e regurgitação temática"*.

A `CorrecaoFonetica` **foi escrita**, está no núcleo
([`Transcritor.cs:237-255`](../app-net/Nucleo/Transcritor.cs#L237-L255)), roda
em produção e grava cada troca no `swaps` para a tela poder desfazer. O que não
aconteceu foi o segundo passo: **desligar o `hotwords`**. Hoje o app paga o
preço dos dois mecanismos e recebe o benefício de um.

O que esta fase acrescenta ao 5-A não é a decisão — é o **tamanho do preço**,
que o 5-A não conhecia:

- que o colapso de segmentação **quebra a atribuição de falantes** (§2.4), e não
  só encompridava linhas;
- que ele **contamina os vetores de voz**, que persistem entre reuniões (§2.5);
- que o efeito se sustenta em gravação saudável, e não era artefato do microfone
  morto (§2.3).

Com isso, a fase deixa de ser "decidir um trade-off" e passa a ser **aplicar uma
decisão já medida, e consertar o que ela deixou passar**.

---

## 4. Critérios de aceite

| | o quê |
|---|---|
| **A** | **Confirmar o 5-A em gravação saudável**: sem `hotwords`, com `CorrecaoFonetica` ligada, os nomes próprios saem tão certos quanto hoje. O 5-A mediu isso em `2026-08-06_10-31-03`, que tem o microfone morto em 95% do tempo; o corpus da §10 dá o áudio saudável. Desenho 2×2 de novo — sem o braço "sem nada" não se separa "o motor já acertaria" de "a correção funcionou" |
| **B** | Nenhum segmento acima de 25 s numa reunião de 30 min com 5 falantes, e a mediana de duração abaixo de 3 s |
| **C** | Nenhum trecho usado para aprender voz tem energia de `mic.wav` acima do piso quando o falante não é o dono. Verificável sem anotador: a faixa do microfone é verdade |
| **D** | O gate do VAD 0,15 fecha sobre **`system.wav` de 2 a 3 gravações reais** com silêncio digital acima de 20% — ou é registrado como irrelevante, com o número que mostra isso |
| **E** | O `FiltroDeSilencio` roda no app, ou sai do código. As duas saídas são aceitáveis; deixá-lo morto não é |
| **F** | Cobertura de fala não regride em nenhuma das gravações do acervo, medida contra o áudio |

O **A** é o que decide a fase, e vem primeiro por isso.

---

## 5. Ordem de trabalho sugerida

1. **Medir o A antes de mudar qualquer default.** É uma execução de
   [`tools/benchmark_vocab.py`](../tools/benchmark_vocab.py) com um braço novo, e
   responde a pergunta que ordena todo o resto.
2. **Truncar o intervalo do vetor de voz** — a
   [linha 153](../app-net/Nucleo/AprendizadoDeVozes.cs#L153) passa `t.Fim`
   cheio; passar `min(t.Fim, t.Inicio + SegundosDoTrecho)` alinha o vetor ao
   `.wav` que já se guarda, e teria excluído a contaminação medida aqui. Uma
   linha, e protege o `vozes.json` enquanto o resto anda.
3. **Descartar do aprendizado o trecho com `mic.wav` ativo** quando o falante
   não é o dono. Determinístico, barato, e ataca a classe inteira em vez do caso.
4. **Preservar as palavras no motor** e cortar segmento na troca de falante da
   diarização. É o conserto de raiz da §2.4, e o custo do alinhamento já está
   sendo pago.
5. **Fechar o gate do VAD** sobre o áudio certo (§2.2, critério D).
6. **Resolver o `FiltroDeSilencio`** — ligar ou remover.

Os itens 2 e 3 não dependem do 1 e podem entrar antes, porque param uma
contaminação que hoje acontece em toda reunião gravada.

---

## 6. Os vetores de voz já contaminados

Vale a mesma regra do risco 4 do [PLANO.md](PLANO.md) §5: **perfil de voz se
reinscreve, não se conserta**. Os vetores aprendidos até aqui passaram pelo
caminho descrito na §2.5, e não há como saber quais estão sujos sem reprocessar
as gravações de origem.

A decisão de descartar e reinscrever é do dono do produto, e fica registrada
como pendência desta fase — não como parte dela.

---

## 7. Fora desta fase

- **Trocar de motor de ASR.** O resultado 6 da Fase 0 já mostrou que a vantagem
  do whisper.cpp era artefato de configuração. Nada aqui pede motor novo.
- **Mexer na diarização.** O `community-1` fica. O que esta fase entrega é
  *onde* ela pode pousar, não como ela decide.
- **Interface.** Se algum destes ajustes precisar de tela — escolher vocabulário
  por projeto, mostrar o que foi trocado — ela entra pela régua da Fase 5, por
  necessidade e não por gosto.
- **A ata.** Ela consome a transcrição e melhora junto, de graça. Não é preciso
  mexer no motor de ata para isso.

---

## 8. O risco que vale dizer em voz alta

**O vocabulário é a feature que originou o projeto.** O resultado 5 abre dizendo
isso: "a pergunta que originou o projeto: um nome conhecido ('Dimi') sair
transcrito como outra coisa ('Jimmy')". Esta fase propõe desligar o mecanismo
que resolve exatamente essa pergunta.

Ela só pode fazer isso porque existe um segundo caminho para o mesmo ganho, e
porque **o resultado 5-A já mediu que ele funciona** — 36 contra 36. O critério
A é confirmação em áudio saudável, não descoberta. Se ele falhar, o `hotwords`
fica e a fase se reduz aos itens 2–6, que já valem sozinhos.

Vale registrar por que a decisão do 5-A ficou um mês sem ser aplicada: ela está
no fim de um resultado longo, escrita como consequência de um bloqueio de
migração que caiu, e não virou item de nenhuma carta de fase. **O achado ficou
no documento e não no plano** — que é o modo de falha típico de um projeto
doc-driven, e a razão de esta carta existir.

O pior resultado possível desta fase é trocar um defeito visível (o nome errado,
que o usuário vê e corrige) por um invisível (a fala que não está lá, que
ninguém procura). Por isso o critério F mede cobertura em todo o acervo, e por
isso a `CorrecaoFonetica` grava o `swaps`: o que ela troca continua auditável.

---

## 9. Anexo — a comparação com o Notion

O que a comparação de 14/08/2026 mostrou, além do que originou a fase.

**Onde o app ganhou:** os nomes próprios (o Notion escreveu "Jimmy", "Chudu",
"Odime" e "de Chudu" para o mesmo "Dimi", e "Helen" para "Ellen"); os números
âncora do estudo (`27.529` e `26.573` contra "27, 5, 2, 9, 26, 5, 7, 13");
falantes nomeados e timestamps, que o Notion não tem; e a ausência dos loops de
alucinação em silêncio — o Notion abre com "Olá." sete vezes e fecha repetindo
as despedidas.

**Onde o Notion ganhou:** registrou a pergunta central da Vanessa em
`[17:03–17:30]`, que o app perdeu inteira e que estrutura os oito minutos
seguintes; registrou um item da lista de flags que o app engoliu; e acertou
"vamos alinhar" onde o app escreveu "vou marinha" e `106` onde o app escreveu
`26`.

**Alucinações que só o app teve:** `"Vocês tem virtually todos os programas de
investimento disponíveis"` em `[06:18]`, `"Atendentes científicos"` em `[19:46]`,
um vazamento de caractere cirílico em `[21:39]`, e o `"NoBill"` da §2.3.

**A leitura:** o Notion não é utilizável como fonte de ata — sem falantes e com
os números soletrados, o trecho decisivo se perde. Mas ele serve como o que foi
aqui: um segundo par de olhos sem custo de anotação, útil exatamente por errar
de outro jeito.

---

## 10. Antes da fase: repetir a comparação, com mais de uma fonte

**Esta fase está longe.** Vêm a 3, a 4 e a 5 antes dela, e até lá a evidência
que a originou continua sendo **uma reunião**. Não dá para calibrar um default
com *n* = 1 — é a mesma disciplina do resultado 3-C da Fase 0, que já custou uma
conclusão errada por medir no corpus errado.

Por isso a tarefa que **começa agora e roda em paralelo às fases 3 a 5**: a cada
reunião gravada, transcrever **em paralelo por outras fontes** e guardar as
saídas junto da gravação. Não custa trabalho de anotação, acontece durante a
reunião de qualquer forma, e é o que transforma este documento de um caso num
corpus.

**Por que mais de uma fonte, e não só o Notion.** O valor da comparação não é o
Notion estar certo — ele erra muito. É que ele erra **de outro jeito**, com
outro VAD, outra segmentação e outro viés de decodificação. Duas fontes já
separam "o áudio é difícil aqui" de "o nosso pipeline falhou aqui"; três tornam
a resposta quase sempre óbvia. Uma fonte sozinha só levanta hipótese — foi
exatamente o que aconteceu aqui, e as duas primeiras hipóteses que levantei
(o VAD, e depois a cobertura dos buracos) **estavam erradas**; quem decidiu foi
a reexecução controlada, não a comparação.

Candidatos, por ordem de facilidade:

| fonte | o que ela testa que o Notion não testa |
|---|---|
| **Notion** | já em uso; sem falantes, sem timestamps |
| **transcrição nativa do Teams / Meet** | tem falantes de verdade, vindos do canal de cada participante — é a coisa mais próxima de verdade de referência para diarização que se consegue sem anotar |
| **o próprio app sem `hotwords`** | isola o mecanismo desta fase, na mesma máquina e no mesmo áudio |
| **whisper.cpp** (já no acervo da Fase 0) | outro decodificador sobre o mesmo modelo |

O **Teams/Meet é o mais valioso dos quatro**, e por um motivo que muda o que se
pode medir: os rótulos de falante dele não são estimativa. Onde eles existirem,
o DER da nossa diarização passa a ser medível em reunião real — hoje ele só é
medível no acervo anotado da Fase 0.

**O que guardar**, junto da pasta da gravação, para a comparação não depender de
memória: a saída bruta de cada fonte, com data e nome da fonte, e uma nota de
uma linha dizendo o que se notou de diferente. Foi assim que esta fase nasceu.

**A régua para abrir a fase:** três a cinco reuniões com pelo menos duas fontes
paralelas, cobrindo os dois padrões de uso — reunião em que o dono fala pouco
(que é o comum, e onde o `system.wav` tem silêncio de verdade) e reunião em que
fala muito. Com isso na mão, o critério A da §4 deixa de ser um teste e vira uma
confirmação, e o gate do VAD (critério D) fecha com o áudio certo em vez de
esperar mais um mês.
