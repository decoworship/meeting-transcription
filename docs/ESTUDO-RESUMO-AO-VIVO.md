# Estudo — o resumo do que já foi falado, em seis modelos

Feito em 16/09/2026, a pedido do dono do produto, sobre a caixa de perguntar que
entrou em [CONVERGENCIA.md](CONVERGENCIA.md) §T2.1 no mesmo dia.

**A pergunta:** como fazer o resumo dos primeiros 20 minutos de uma reunião, e
qual dos modelos que temos mapeados o faz melhor.

**O método, e ele é o de sempre:** uma instrução só, byte por byte igual para
todos, contra o mesmo texto, na mesma placa, medindo tempo, tokens e VRAM — e
depois lendo as saídas contra a transcrição para ver o que cada uma inventou.

| | |
|---|---|
| **o material** | reunião de 15/09/2026 às 14:01 — 32 min, 818 trechos, 7 falantes |
| **o corte** | os trechos com `start < 1200 s` da passada final: **466 trechos, 51 turnos, 16.401 caracteres** |
| **o formato** | turnos do mesmo falante juntos numa linha, `Nome: fala`, **sem carimbo** |
| **a placa** | RTX 2060 6 GB, com o desktop rodando |
| **o contexto** | 8.192 para todos, `-ctk q8_0 -ctv q8_0 -fa on -ngl 99` — o que se compara é o modelo |
| **a saída** | `max_tokens` 1.024, `temperature` 0.3, `enable_thinking: false` — os mesmos da produção |

**Por que a passada final e não a legenda ao vivo.** A legenda não tem carimbo,
então "os primeiros 20 minutos" sairia por proporção de caracteres, e o estudo
começaria com um corte aproximado. A escolha tem um custo honesto: **estas
saídas trazem nome de participante, e ao vivo isso não existe** — lá são
"Você" e "Outra pessoa". A qualidade do texto também é melhor (large-v3 contra
Nemotron). O que este estudo compara é **modelo contra modelo**, e para isso o
corte exato vale mais.

---

## 1. A instrução proposta

É o objeto medido. Ela vive em duas partes, como tudo que vai ao
`llama-server`: o papel no `system`, a tarefa no fim do `user` — depois da
transcrição, porque o que fica perto do fim é o que mais pesa na geração
(mesma ordem do [PromptDeAta](../app-net/Nucleo/Atas/PromptDeAta.cs)).

**O papel:**

> Você resume reuniões em andamento, em português do Brasil, para alguém que
> precisa se situar rápido. Escreva APENAS a partir da transcrição fornecida: se
> algo não estiver lá, não escreva. Nunca complete com o que seria plausível. A
> transcrição é automática e troca palavras — leia pelo sentido, e não cite
> trecho que não faz sentido. A reunião AINDA NÃO ACABOU: não escreva conclusão,
> encerramento nem resultado final. Não invente horário: a transcrição não tem
> relógio.

**A tarefa — cinco seções, e cada uma com uma saída explícita para o vazio:**

```
## Em uma frase          do que a reunião trata. Máximo 25 palavras.
## Assuntos tratados     3 a 6 itens, do mais ao menos tratado.
## Decisões              …ou "Nada foi decidido até agora."
## Pendências            quem ficou de quê …ou "Nenhuma pendência foi atribuída."
## Em aberto             perguntas sem resposta …ou "Nada em aberto."
```

**A saída explícita para o vazio é a peça central, e o estudo provou que ela é
necessária mas não suficiente.** Sem uma frase pronta para "não houve", um modelo
pequeno preenche a seção com o que tiver à mão — e o que ele tem à mão são os
assuntos que acabou de listar. Foi exatamente o que dois dos seis fizeram,
**mesmo com a frase pronta na instrução**.

**Três regras existem por defeito medido neste projeto:**

- *"não cite trecho que não faz sentido"* — a transcrição dos 20 min diz
  *"só quem abriu **o objetivo** e for solicitar o suporte"*. "Objetivo" é erro
  de ASR. Quem copia isso está transcrevendo, não resumindo;
- *"a reunião AINDA NÃO ACABOU"* — sem isso o modelo escreve encerramento para
  uma conversa que continua, e quem lê acha que perdeu o fim;
- *"não invente horário"* — a legenda ao vivo não carimba turno, e o modelo
  inventa hora com a mesma naturalidade com que inventaria o resto.

---

## 2. O achado que muda a produção hoje: o esquema JSON estraga quatro de seis

O [MotorDeAta](../app-net/Nucleo/Atas/MotorDeAta.cs) prende a saída a um
`response_format` de `json_schema` estrito. Para a ata isso é o que a torna
verificável. **Para o resumo, é o que o impede de existir.**

Rodado duas vezes, mesmo prompt, mesma placa, só mudando o esquema:

| | **com** esquema | **sem** esquema |
|---|---|---|
| `qwen3-1.7b` | 71 tokens — **só a 1ª seção** | 527 tokens, as cinco |
| `ministral-3-3b` | 56 tokens — **só a 1ª seção** | 559 tokens, as cinco |
| `smollm3-3b` | 106 tokens — **só a 1ª seção** | 344 tokens, as cinco |
| `gemma-4-e4b` | **1.024 tokens para 190 caracteres**, e cortado | 313 tokens, as cinco |
| `qwen3-4b-instruct` | 432 tokens, as cinco | 371 tokens, as cinco |
| `qwen3.5-4b` | 303 tokens, as cinco | 258 tokens, as cinco |

**Os três pequenos escreveram a primeira seção e pararam por conta própria** —
`finish_reason: stop`, não `length`. Eles não foram cortados: decidiram que
tinham terminado.

**O Gemma é o caso extremo, e ele nomeia a causa: 0,19 caractere por token.** A
gramática do esquema o obrigou a emitir a string quase byte a byte — queimou o
orçamento inteiro de saída no meio da primeira frase. Sem o esquema, o mesmo
modelo escreve 1.411 caracteres em 313 tokens, **4,5 caracteres por token**, e
termina sozinho.

> **A razão de o esquema existir continua válida, e não se aplica aqui.** O
> `MotorDeAta.ResponderAsync` documenta a medição de 25/08: sem esquema e **com
> raciocínio ligado**, o Qwen3.5 deliberou 3.000 tokens sem emitir nada. Nas doze
> rodadas deste estudo, todas com `enable_thinking: false`, **nenhum modelo
> deliberou** — os seis terminaram com `stop` e contagem sadia. Quem protege
> contra a deliberação é a chave do raciocínio; o esquema estava pagando por um
> trabalho que outro já fazia, e cobrando quatro modelos por isso.

---

## 3. Os números

Sem o esquema — que é a configuração em que os seis funcionam:

| modelo | carga | resposta | tok/s | saída | VRAM do modelo | disco | contexto nativo |
|---|---:|---:|---:|---:|---:|---:|---:|
| `qwen3-1.7b` | 4,8 s | 11,9 s | 76,5 | 527 tk | **1.723 MiB** | 1,03 GB | 40.960 |
| `smollm3-3b` | 9,4 s | 12,2 s | 54,7 | 344 tk | 2.269 MiB | 1,78 GB | 65.536 |
| **`ministral-3-3b`** | 15,4 s | 17,2 s | 44,6 | 559 tk | **2.652 MiB** | **2,00 GB** | **262.144** |
| `qwen3.5-4b` | 14,2 s | 8,6 s | 45,5 | 258 tk | 2.939 MiB | 2,55 GB | 262.144 |
| `qwen3-4b-instruct` | 12,3 s | 11,1 s | 42,4 | 371 tk | 3.195 MiB | 2,33 GB | 262.144 |
| `gemma-4-e4b` | 21,9 s | 11,1 s | 38,2 | 313 tk | 3.127 MiB | 4,64 GB | 131.072 |

O prompt deu entre **4.260 e 5.000 tokens** conforme o tokenizador — 16.401
caracteres de transcrição, ou **3,3 a 3,8 caracteres por token**.

**Todos cabem nos ~3,5 GB que a [CONVERGENCIA](CONVERGENCIA.md) mediu livres
durante uma reunião de verdade** — mas os 4B chegam perto do teto, e esta medição
é com contexto de 8k. Uma reunião de duas horas pede quatro vezes isso de cache.

---

## 4. A leitura, e ela separa os seis em dois grupos

Duas réguas, as duas falsificáveis contra a transcrição:

| modelo | inventou decisão? | copiou o erro de ASR? | inventou pessoa? |
|---|---|---|---|
| `qwen3-1.7b` | **sim — cinco** | **sim**, "quem abriu o objetivo" | não |
| `smollm3-3b` | **sim — três** | **sim**, "abriram o objetivo" | não |
| `ministral-3-3b` | não | não — leu "clientes que abriram o chat" | não |
| `qwen3-4b-instruct` | não | não — leu "os que solicitaram suporte" | não |
| `qwen3.5-4b` | não | não — leu "quem solicitou suporte" | não |
| `gemma-4-e4b` | não | não — leu "quem solicitou suporte" | não |

**Nenhum dos seis inventou participante.** Toda pessoa a quem algum resumo
atribuiu pendência é citada na transcrição — conferido nome por nome, incluindo
os que não são falantes (Guilherme e Paloma aparecem só na fala de outros, e
foram usados corretamente por `ministral` e `gemma`).

**O `qwen3-1.7b` reprova, e o modo de reprovar importa.** Ele transformou os
assuntos em decisões, um a um:

> ## Decisões
> - A reunião decidiu implementar um chat dentro do app…
> - A reunião decidiu **discutir** a comunicação massiva…

"A reunião decidiu discutir" não é decisão — é a lista de assuntos com um verbo
na frente. E ele distribuiu a mesma não-tarefa por quatro pessoas em Pendências.
**É o pior defeito possível aqui**, porque uma decisão falsa lida no meio de uma
reunião é citada em voz alta na reunião.

**O `smollm3-3b` reprova pelo mesmo motivo**, com três decisões inventadas, e
carrega o mesmo erro de ASR para dentro do resumo.

**Os quatro maiores acertaram a régua difícil:** os primeiros 20 minutos desta
reunião são discussão, e **nada foi decidido**. Os quatro escreveram exatamente
isso, em vez de preencher a seção.

**Uma checagem de detalhe, para não confiar só na forma.** Três resumos citam
*"push a cada 30 minutos"*. A transcrição tem **os dois** números: "a cada 15
minutos" descrevendo um evento passado de que alguém participou, e "a cada 30
minutos" na proposta de push que estava sendo feita. Os que citaram 30 pegaram o
contexto certo.

---

## 5. Uma correção à CONVERGENCIA §T2.1

Lido dos cabeçalhos GGUF, não de ficha técnica:

- **o `qwen3-1.7b` é 40.960 de contexto nativo, não 32K.** A seção diz que ele
  *"só cobre o acervo inteiro via YaRN"* — não é verdade: a maior reunião do
  acervo, de 122 min, dá 31.891 tokens e **cabe nativa**. O argumento que o
  reprovava por contexto cai; o que o reprova neste estudo é outro, e é pior;
- **o `qwen3-4b-instruct` — o titular — é 262.144**, e a tabela de competidores
  não o lista. Ele não tem problema de contexto nenhum;
- **o Ministral 3 confirma os 256K** (262.144) e o SmolLM3 os 64K (65.536).

---

## 6. O que eu faria

> **Aplicado em 16/09/2026, e a §8 acrescentou uma ressalva ao Ministral.** A
> produção saiu sem esquema **e sem instrução de sistema**, por decisão do dono
> do produto. Leia a §8 antes de agir por esta seção.

**Primeiro, tirar o esquema do caminho do resumo — a decisão vem antes da do
modelo.** Ele custa quatro dos seis modelos e não protege contra nada que
`enable_thinking: false` já não proteja. A ata continua com esquema: lá a
estrutura *é* o produto, e o `VerificadorDeAta` depende dela.

**Depois, e só depois, o modelo.** Com o esquema fora:

- **promover o `ministral-3-3b` a candidato do resumo** — *com a ressalva da §8:
  sem instrução, ele inventa mensagem entre aspas.* Mesma honestidade dos
  4B, **540 MiB de VRAM a menos** que o titular, 2,00 GB em disco, e 262K de
  contexto. Foi o único que leu *"abriram o chat"* onde o áudio dizia
  "objetivo" — sinal de que leu pelo sentido, que é o que a instrução pede;
- **manter o `qwen3-4b-instruct` como titular seguro.** É o único que funciona
  bem **nas duas** configurações, com e sem esquema, e é o que já está instalado;
- **reprovar o `qwen3-1.7b` e o `smollm3-3b` para resumo.** Os dois inventam
  decisão. Não é questão de ajustar a instrução: a saída explícita para o vazio
  já estava escrita, e os dois passaram por cima dela;
- **o `gemma-4-e4b` fica de fora por preço**, não por qualidade: a saída dele é
  das melhores, mas são 4,64 GB de disco e 3.127 MiB de VRAM para empatar com um
  modelo de 2 GB.

---

## 7. O que este estudo não mediu

- **uma reunião, um corte, uma rodada por modelo.** `temperature` 0.3 não é zero:
  repetir daria saídas um pouco diferentes. As duas reprovações são grosseiras o
  bastante para sobreviver a isso; a ordem entre os quatro aprovados, não;
- **a legenda ao vivo como entrada.** É o que a funcionalidade recebe de verdade,
  e é texto pior e sem nomes. Quanto disso o resumo perde é a próxima medição, e
  ela vale mais que trocar de modelo;
- **reunião longa.** 16 mil caracteres é um quarto do que uma reunião de duas
  horas produz. O que muda com 4× de contexto é a VRAM, e aí os 540 MiB de
  diferença entre o Ministral e o titular deixam de ser detalhe;
- **o custo de rodar isto durante a reunião.** Todas as medições foram com a placa
  livre de legenda. O terceiro contexto CUDA continua sendo o risco conhecido.

---

## 8. E sem instrução nenhuma? — a rodada que decidiu a produção

Feita depois, quando o dono do produto corrigiu o enquadramento: **"para esse
ponto não vamos ter prompt nem esquema, por enquanto é livre para perguntar
sobre a reunião"**. Então a pergunta certa não era qual modelo segue melhor uma
instrução de cinco seções — era **o que cada um faz quando não recebe nenhuma.**

Mesma transcrição, mesma placa, sem `system`, sem esquema, uma pergunta livre:
*"O que já foi discutido nesta reunião até agora?"*

| modelo | resposta | saída | terminou? | cabeçalhos | bullets | negritos |
|---|---:|---:|---|---:|---:|---:|
| `qwen3-1.7b` | 9,4 s | 1.024 tk | **cortado** | 10 | 45 | 63 |
| `ministral-3-3b` | 15,2 s | 1.024 tk | **cortado** | 3 | 40 | 53 |
| `qwen3-4b-instruct` | 21,9 s | 1.024 tk | **cortado** | 9 | 42 | 37 |
| `qwen3.5-4b` | 19,0 s | 942 tk | sim | 5 | 20 | 26 |
| `gemma-4-e4b` | 19,6 s | 778 tk | sim | 0 | 18 | 20 |
| `smollm3-3b` | 9,3 s | 631 tk | sim | 0 | 0 | 8 |

**Cinco comportamentos, e cada um pede uma linha de instrução:**

**1. Metade é cortada no meio da frase.** Três dos seis batem nos 1.024 tokens
com `finish_reason: length`. Sem instrução, "o que já foi discutido" vira
especificação de projeto: 3,5 a 3,8 KB contra os 1,2 a 2,2 KB que a instrução de
cinco seções produzia. **Ou o teto sobe, ou a instrução limita** — e limitar é
melhor, porque quem pergunta no meio de uma reunião lê de relance.

**2. A forma varia de prosa corrida a documento de dez cabeçalhos.** O
`smollm3-3b` responde em texto puro, sem um bullet; o `qwen3-1.7b` monta dez
seções com numeração e negrito. **Nenhum dos dois foi pedido.** Se a forma
importa, ela tem que ser dita; se não for dita, ela é sorteada por modelo.

**3. O `ministral-3-3b` inventa mensagem entre aspas — e foi ele que a §6
recomendou.** Quatro trechos que ninguém falou, apresentados como exemplo:

> - **Educação do cliente**: Ajuda a otimizar o uso do Wi-Fi
>   (ex.: *"Você está muito longe do modem? Tente voltar para o 2.4 GHz"*).

Está marcado com `ex.:`, então não é atribuição falsa — mas está entre aspas,
dentro de um resumo do que foi discutido, e quem lê rápido leva aquilo como fala.
**A instrução precisa proibir exemplo ilustrativo**, e não só invenção de fato.
Foi a régua de citações da ferramenta que pegou isto; lendo, eu tinha deixado
passar.

**4. Preâmbulo.** O `qwen3.5-4b` abre com *"Com base na transcrição da reunião,
aqui está um resumo detalhado do que foi discutido até o momento:"* — uma linha
inteira para dizer que vai responder.

**5. O `qwen3-1.7b` afirma decisão de novo**, e agora sem instrução alguma que o
provocasse: *"os participantes discutiram **e definiram** vários pontos"*. A
reprovação da §4 não era efeito da instrução.

### A instrução que estes cinco comportamentos pedem

Não é para escrever agora — é o que a próxima rodada deve testar, uma de cada
vez, com a ferramenta da §9:

1. **um teto de tamanho**, dito em frases e não em tokens;
2. **proibir exemplo ilustrativo**, inclusive marcado como "ex.:";
3. **proibir preâmbulo** — começar pela resposta;
4. **dizer a forma**, ou aceitar que ela varie por modelo;
5. **separar o que foi dito do que foi decidido** — é onde o 1.7B cai sempre.

### O que a produção passou a fazer

**Sem `system` e sem esquema**, como pedido. O
[Ponte.cs](../app-net/App/Ponte.cs) manda a transcrição e a pergunta, e nada
mais; o `MotorDeAta.CorpoDoPedido` passou a aceitar os dois vazios e a omitir o
campo em vez de mandá-lo em branco — mensagem de sistema vazia alguns templates
Jinja renderizam como turno vazio, e outros recusam.

**O que sobrou no prompt não é instrução, é entrega:** a moldura
`=== TRANSCRIÇÃO ===` e, quando a reunião não coube, a linha que diz ao modelo
que ele está vendo só o fim. A segunda é fato sobre o que ele recebeu — sem ela,
ele afirma como a reunião começou olhando para o meio dela.

---

## 9. A ferramenta

[`tools/comparar_modelos_de_pergunta.py`](../tools/comparar_modelos_de_pergunta.py),
irmã do `comparar_modelos_de_ata.py`. **Prompt e esquema são opções**, e o padrão
é sem os dois — que é o que menos assume.

```bash
tools/comparar_modelos_de_pergunta.py \
    --gravacao 2026-09-15_14-01-30 --ate 1200 \
    --pergunta "O que já foi discutido nesta reunião?" \
    --modelo qwen3-4b-instruct-q4km.gguf --modelo ministral-3-3b-q4km.gguf
```

**As três réguas apontam onde olhar, e não dão veredito** — pergunta livre não
tem gabarito, mas tem uma coisa falsificável: o que a resposta afirma e a
transcrição não contém.

| régua | o que pega |
|---|---|
| **termos de fora** | palavra em maiúscula que não está na transcrição nem na pergunta — é como se acha nome de pessoa inventado |
| **números de fora** | número que dimensiona algo e ninguém disse. Reusa a régua do `comparar_modelos_de_ata.py`, que já aprendeu a ignorar conta em voz alta |
| **citações de fora** | trecho entre aspas que ninguém falou — **foi esta que pegou o Ministral** |

**A coluna de termos tem falso positivo por construção**, e ele é informativo:
"Resumo", "Próximos", "Foco" aparecem porque o modelo inventou *estrutura*, não
fato. Ler a lista leva dez segundos e diz as duas coisas.

---

## Apêndice — o resumo do `ministral-3-3b`, inteiro

Sem esquema, 559 tokens, 17,2 s. Reproduzido como saiu, incluindo os separadores
`---` que a instrução não pediu (é o desvio de forma dele).

```markdown
## Em uma frase
Reunião para definir o **fluxo de comunicação** e **texto das mensagens** do chat
interno do app para clientes durante diagnóstico técnico de banda larga,
alinhando com a Bigo e ajustando o tom de comunicação.

## Assuntos tratados
- **Projeto de chat interno** no app para resolver problemas técnicos sem abertura de OS.
- **Fluxo de diagnóstico** no chat: verificação inicial, coleta de dados, diagnóstico
  técnico e possível abertura de OS.
- **Mensagens padrão** para clientes em diferentes etapas (ex: bloqueio financeiro,
  massiva, diagnóstico).
- **Tons de comunicação** a serem adotados, alinhados com a marca TamoJunto.
- **Atendimento humano** (PA interno) para casos de clientes muito irritados.
- **Comunicação adicional** para massivas: proposta de push de atualização e
  mensagem final de conclusão.

## Decisões
Nada foi decidido até agora.

## Pendências
- **André** e **Roger** ficaram de compartilhar exemplos de mensagens pré-definidas.
- **Daniela** ficou de discutir com **Guilherme** (comunicação) o texto final das mensagens.
- **André** e **Roger** discutiram sobre **quem receberá a comunicação de massiva**.

## Em aberto
- **Como será o fluxo exato** do chat após o cliente clicar em "suporte técnico".
- **Detalhes do fluxo de diagnóstico técnico** com os dados do Beegol embarcado.
- **Se o cliente receber push de atualização** durante uma massiva, e qual frequência.
- **Se a comunicação de massiva será direcionada** apenas a quem abriu o chat.
- **Se as mensagens pré-definidas** já estão prontas.
- **Qual o tom exato** das mensagens.
```
