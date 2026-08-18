# Fase 6 — revisões: carta de execução

Escrita em 14/08/2026, ao fechar a Fase 3, por decisão do dono do produto:
**lançar primeiro, melhorar depois.** Tudo o que as fases anteriores marcaram
como "revisitar" vem para cá, em vez de atrasar a primeira versão.

Esta carta é diferente das outras. As anteriores descrevem o que **vai** ser
feito; esta lista o que **pode** ser feito, cada item com o **gatilho** que
justifica fazê-lo. Uma fase de revisões sem gatilhos vira lista de desejos, e
lista de desejos se executa pela ordem de quem gosta mais — não pela ordem do
que dói.

**A ordem é a do incômodo, não a desta lista.** Nada aqui é obrigatório — com
**uma exceção**, acrescentada em 18/08/2026: a §3.0 não é revisão nem melhoria.
É um usuário que não consegue transcrever, com a causa em aberto.

---

## 1. A ata (o que mais provavelmente vai doer)

### 1.1 A omissão do modelo pequeno

**Gatilho:** a linha "Números citados na reunião que não aparecem nesta ata"
trazer, com frequência, coisa que devia estar lá.

Medido na Fase 3: o Qwen3-4B **não inventa** (zero fatos falsos, zero donos fora
da lista) e **omite metade** — recuperou 7 de 14 números da reunião comparada, e
o que ficou de fora incluía o impacto financeiro, que era a linha mais importante
daquela reunião ([ATA.md](ATA.md) §8).

Contra isso já existem duas redes: o roteiro de fatos no prompt e a conferência
de cobertura depois.

> ✅ **As duas redes funcionaram, medido em 14/08/2026** na ata da mesma reunião
> de 29 min, agora gerada pelo app com o roteiro ligado. **O impacto financeiro
> está lá** — "R$ 180 mil por mês, totalizando mais de R$ 2 milhões ao ano" —, e
> era exatamente a linha que a comparação da Fase 3 apontou como a mais grave
> das omissões. O que sobra de omissão é de segunda ordem, e está na §1.6.

**Se não bastarem**, em ordem de custo:

1. **subir de quantização** — Q5_K_M ou Q8_0 do mesmo Qwen3-4B, que cabem na
   placa com contexto menor. É trocar VRAM por qualidade sem trocar de modelo;
2. **subir de modelo** — um 7–8B em Q4 não cabe nos 6 GB com contexto útil, mas
   caberia numa placa maior. Vira decisão de hardware;
3. **duas passadas** — a primeira extrai fatos, a segunda redige com a lista na
   mão. Dobra o tempo e é o caminho mais provável de funcionar sem trocar nada;
4. **reabrir o provedor** — o motor está atrás de uma interface, e um modelo de
   fronteira escreveria a ata que a skill descreve. Custa mandar transcrição de
   reunião com cliente para fora, que é exatamente o que a decisão de rodar local
   comprou.

> **Não apertar o prompt.** Já foram três rodadas de ajuste de instrução, e o
> ganho foi zero: o modelo entende o que se pede e não consegue executar. O
> caminho é dar menos trabalho a ele, não pedir melhor.

### 1.2 Reunião acima de ~2h15

**Gatilho:** aparecer uma reunião que não cabe. Hoje 2 h cabem numa passada.

O caminho é map-reduce, com o custo registrado em [ATA.md](ATA.md) §7: perde-se a
visão do todo que faz "Situação da sprint" ter sentido. **Não construir antes de
precisar** — um caminho de blocos existindo é um caminho de blocos usado por
engano.

### 1.3 As seções estruturadas viram prosa

**Gatilho:** usar ata de sprint ou daily de verdade e sentir falta.

O esquema é universal, e o "Por pessoa" da sprint e da daily cai em
`secoes[].texto` como parágrafo, em vez de virar lista de pessoas. Foi escolha
consciente ([ATA.md](ATA.md) §3): é o que permite customizar um tipo escrevendo
Markdown, sem escrever JSON Schema. Quando incomodar, `secoes[]` ganha um campo
opcional de itens — e só os tipos que precisarem o usam.

### 1.4 A ata anterior como contexto

**Gatilho:** a segunda ou terceira reunião da mesma série.

"O que ficou pendente da última vez" é a pergunta que mais se faz numa série de
reuniões, e o app é o único que sabe respondê-la: ele tem as atas anteriores do
mesmo cliente/projeto. Estava previsto como feature 9 do motor e não entrou na
v1.

### 1.5 Comparações que não foram feitas

**Gatilho:** insatisfação com a ata, antes de trocar de arquitetura.

- **Gemma 3 4B** contra o Qwen3-4B. O Qwen passou, então comparar só paga se ele
  começar a falhar;
- **com e sem gramática**, medindo se a saída constrangida custa conteúdo. Hoje
  se sabe que ela conserta o formato; não se sabe o que ela cobra por isso;
- **CUDA 12.4 × 13.x** com driver novo. **Decidido em 14/08/2026: fica na
  12.4**, por compatibilidade — ela funciona em driver novo e velho, e a 13.3
  falha na máquina de hoje. Só remedir se houver motivo de desempenho.

A ferramenta para as três já existe: `tools/medir_motor_de_ata.py`.

### 1.6 O que a comparação com o resumo do Notion mostrou

**Gatilho:** já disparou — a comparação foi feita em 14/08/2026, sobre a ata da
reunião `2026-08-13_14-30-15`, contra o resumo que o Notion produziu da mesma
reunião. Os itens abaixo são **os únicos desta carta que não esperam gatilho**,
porque saíram de uma saída real conferida contra a transcrição.

A ata do app tem duas coisas que o resumo do Notion não tem, e que valem
registrar antes das críticas: **pendências separadas por lado** (nosso/cliente,
com prazo) e a **conferência de cobertura**, que declara o que ficou de fora. O
Notion entrega uma lista de tarefas sem dono organizado e sem nenhum rastro do
que não entrou.

Dito isso, quatro defeitos, em ordem de gravidade:

**1. O lado da pendência saiu errado — e é o defeito mais caro.** A ata pôs
`"lado": "cliente"` na entrega da base de 27.529 registros. Na reunião quem se
compromete é o André, do nosso lado: *"eu vou te mandar esse arquivo, tá bom?"*.
O verificador **fez** o seu trabalho — pegou que o responsável "Vivo" não era
participante e trocou por `[responsável a definir]` — mas ele confere o *dono*,
não o *lado*. Uma pendência nossa arquivada como do cliente é a falha que uma
ata pode ter e que mais custa: ela não é cobrada de ninguém.

*Caminho:* o sinal está na transcrição e é forte — quem diz "eu vou te mandar"
é o dono, e o lado dele já é conhecido pelo classificador de participantes da
Fase 3. É o mesmo desenho de sempre: regra determinística embaixo, modelo por
cima.

**2. Números certos com unidade errada.** A ata escreve *"106 produtos com
registros zerados (R$ 129 mil)"*. Os 129 mil são **contagem de registros**, não
reais — o Notion acertou (*"129 mil registros zerados"*). O roteiro de fatos
entrega o número com o trecho em volta e o modelo recupera o número; a unidade
ele reconstrói sozinho, e às vezes reconstrói errado. Num documento cujo valor é
ser citável, `R$` colado num número que não é dinheiro é pior que a omissão.

*Caminho:* o roteiro já extrai o número com contexto; falta carregar junto o
substantivo que o acompanha na fala ("registros", "produtos", "contas", "reais")
e pedir que a ata use esse, em vez de deixar o modelo escolher.

**3. A omissão encolheu, mas não acabou.** Três ações contra seis do Notion. O
que ficou de fora, tudo presente na transcrição:

- **a apresentação para a Carla no dia seguinte** — que era o *propósito* da
  reunião. A ata diz apenas "A próxima reunião será marcada para amanhã", sem
  dizer para quem nem para quê; o Notion dedica uma seção a isso;
- **a dependência do TI do cliente** — o incidente já aberto com prioridade, e o
  time de TI orientado a fazer o levantamento em vez de receber a base. É um
  risco de cronograma, e a ata tem uma seção `riscos` que saiu vazia;
- **a confirmação do override** pela Kiros/Quiros, que destravou os filtros;
- **duas das oito linhas do detalhamento de flags** (as de 8 e de 4 casos). A de
  4 a transcrição do app perdeu, então a ata não tinha como tê-la — mas a de 8
  estava lá.

**4. A seção "Observações sobre a transcrição" se contradiz.** Ela afirma
*"Todos foram registrados com precisão conforme o contexto"* e, quatro linhas
abaixo, lista onze números que **não** aparecem na ata. A lista de números
citados repete "106 produtos" três vezes e traz "2.300.000" nos dois lados. E há
uma interpretação inventada: *"O termo 'SVA' foi corrigido para 'suspensão
temporária'"* — na reunião são coisas distintas que a Vanessa pede para
relembrar, não uma correção.

*Caminho:* a conferência de cobertura é determinística e boa; o problema é
deixar o **modelo** narrar o que ela achou. A lista deduplicada, emitida pelo
C#, sem prosa em volta, diz a mesma coisa sem poder se contradizer.

> **A leitura geral.** O resumo do Notion é mais fácil de ler e mais completo em
> cobertura de assunto; a ata do app é mais confiável no que afirma e é a única
> das duas que se pode auditar. **A distância encolheu com o roteiro de fatos e
> não se fecha com prompt** — os quatro itens acima são três de engenharia
> determinística e um de modelo. Vale repetir a comparação depois de resolvidos,
> pela §5, a regra de fontes paralelas.

### 1.7 O modelo: o que existe hoje que caberia na placa

**Gatilho:** a §1.1 esgotar as redes contra omissão e ainda doer. Pesquisa feita
em 14/08/2026; **nada aqui foi medido nesta máquina** — é levantamento, não
resultado.

Hoje: **Qwen3-4B-Instruct-2507 Q4_K_M**, 2,5 GB, numa RTX 2060 de 6 GB com ~950
MiB já ocupados pelo desktop. O que manda na conta é o KV, a 62 KiB por token em
q8_0 ([ATA.md](ATA.md) §8) — o modelo é a parte pequena do orçamento.

| candidato | tamanho Q4_K_M | cabe nos 6 GB? |
|---|---|---|
| Qwen3-4B-Instruct-2507 (hoje) | 2,5 GB | sim, com 49k de contexto em q4_0 |
| **Qwen3.5-4B** (02/03/2026) | **2,74 GB** | provavelmente sim, com menos contexto |
| Qwen3.5-9B | ~5,5 GB | não sobra KV útil |
| Qwen3.6-27B / 35B-A3B (07/2026) | ~18–23 GB | não |

**O Qwen3.6 não tem modelo pequeno.** A família são dois modelos, 27B denso e
35B-A3B MoE, com piso de 18 GB. Está fora desta placa e não é candidato.

**O Qwen3.5-4B é o único sucessor direto**, e o ganho de papel é grande: índice
de raciocínio **27 contra 18** do Qwen3-4B-2507. Mas três ressalvas, e elas
importam mais que o número:

1. **O ganho publicado é contra alucinação, não contra omissão.** A melhora medida
   vem de queda na taxa de alucinação (84% → 80% no AA-Omniscience), com acurácia
   praticamente igual (12,7% → 12,8%). **Alucinação não é o nosso modo de falha** —
   o Qwen3-4B já não inventa nada. O benchmark que melhorou não fala do problema
   que temos;
2. **É thinking híbrido**, e pensar custa tokens de geração. Hoje a ata de 29 min
   sai em 55 s a 44 t/s; um modelo que raciocina antes de escrever pode
   multiplicar isso, e a reunião de 2h já leva 236 s;
3. **+240 MB de peso saem do orçamento de KV**, que é justamente o que aperta.
   Os 262k de contexto nativo do Qwen3.5 são irrelevantes aqui — quem limita é a
   VRAM, em ~49k.

**Recomendação:** quando o gatilho disparar, o Qwen3.5-4B é o primeiro a testar,
por ser troca de arquivo e nada mais. Mas o teste tem que medir **omissão**
(números-chave recuperados, ações encontradas) e **tempo de geração com thinking
desligado** — não adiantam pontos de benchmark num eixo que não é o nosso. E ele
entra na fila **depois** das duas passadas da §1.1 item 3, que atacam a omissão
diretamente e não dependem de modelo novo.

---

## 2. O que a Fase 3 deixou incompleto de propósito

### 2.1 As gravações antigas não sabem de que lado cada um está

**Gatilho:** gerar ata de uma reunião anterior a 14/08/2026 e ver as pendências
no lado errado.

O `meta.json` só passou a guardar `attendee_emails` na Fase 3, e sem e-mail a
organização de cada participante fica desconhecida — o verificador não corrige o
lado, de propósito.

**Tem conserto barato:** o `calendar_event_id` está no `meta.json` das gravações
antigas, e o evento no Google ainda tem os e-mails. Uma migração que relê os
eventos e preenche os e-mails que faltam resolveria o histórico inteiro.

### 2.2 Nome e e-mail casados por posição

**Gatilho:** um nome aparecer atribuído ao domínio errado.

`attendees` e `attendee_emails` são duas listas paralelas, e as duas passam por
deduplicações independentes — em teoria podem desalinhar. Hoje o casamento é por
posição só quando o e-mail existe, e o classificador usa o e-mail direto, então o
risco é baixo. Vira problema se alguém passar a cruzar as duas listas.

### 2.3 O tempo marcado na nota não leva ao áudio

**Gatilho:** usar "marcar momento" e querer ouvir aquele trecho.

`[00:12:34]` numa nota é texto. Na revisão, cada trecho já toca o áudio a partir
dele; a nota podia fazer o mesmo, e o dado para isso já está lá.

### 2.4 A lista de Atas vai crescer

**Gatilho:** passar de umas trinta reuniões transcritas.

A tela lista todas, sem busca e sem filtro por cliente. As atas agora são
dobradas, o que resolveu a rolagem; achar a reunião certa continua sendo rolar.

### 2.5 Exportar ata só em Markdown

**Gatilho:** precisar mandar a ata para quem quer DOCX.

A transcrição exporta em TXT/SRT/VTT/DOCX; a ata copia o `.md`. O `Exportacao`
já sabe escrever DOCX, e a ata já é estruturada — o caminho é curto.

### 2.6 O caminho da ata na ponte não tem teste automatizado

**Gatilho:** mexer no `GerarAta` e não ter rede.

O núcleo tem 271 testes e o caminho inteiro foi exercitado em duas reuniões
reais pela linha de comando, mas a `Ponte` é interna ao executável e a suíte não
a alcança — a mesma fronteira que a Fase 1 desenhou. O `Cli/GeradorDeAta.cs`
existe e faz o mesmo caminho: dava para rodá-lo num teste com um motor falso.

---

## 3. O que vem de fases anteriores e continua de pé

### 3.0 O travamento na máquina de outra pessoa — **aberto, sem causa**

**Gatilho:** já disparou. É o único item desta carta que representa um usuário
sem conseguir usar o app.

Relatado em 18/08/2026 pelo segundo usuário do app — RTX 4050 Laptop, Windows
10.0.26200, driver 595.97, versão 0.1.0. **O gravador funcionou; a transcrição
travou o computador dele.**

#### O que já foi descartado

| hipótese | como caiu |
|---|---|
| o torch não enxerga a placa | ele enxerga: `cuda 12.4`, `is_available() True`, `device_count() 1`, `init()` sem exceção, conferido na máquina dele |
| queda silenciosa para CPU por falta de CUDA | consequência da anterior — não houve queda por esse motivo |
| payload incompleto pelo emagrecimento do instalador | os cortes (`curand`, `cusolverMg`, `tests`, `*.pyi`) foram medidos com o pipeline rodando, e nada em `torch/lib` os referencia |

**A primeira hipótese estava errada, e é o registro que importa aqui:** o
diagnóstico dizia "placa: RTX 4050" e a conclusão fácil era queda para CPU. Foi a
resposta do usuário que a derrubou, não o raciocínio.

#### As hipóteses que sobraram, e o que separa uma da outra

1. **Driver de vídeo caindo sob carga** (`VIDEO_TDR_FAILURE`) — comum em
   notebook, e coerente com "o PC travou" em vez de "o app deu erro". Separa-se
   pelo Monitor de Confiabilidade: houve tela azul, e com qual código;
2. **Memória do sistema.** Duas fontes somam antes de a transcrição começar:
   - o passo do mix mantém **três `float[]` de 460 MB** para uma reunião de 2 h
     (`mic`, `sistema` e o `mix`), ~1,4 GB em pico. Ver `Nucleo/Faixas.cs`;
   - as gravações dele estão no **OneDrive**, que sincroniza o `mix.wav` de
     centenas de MB no exato momento em que a GPU está ocupada;
3. **VRAM.** RTX 4050 Laptop tem 6 GB, e o `large-v3` em fp16 ocupa ~3,1 GB com
   o display na mesma placa. Isso normalmente produz erro do CTranslate2, e não
   travamento — o que enfraquece a hipótese sem eliminá-la.

#### O que foi feito, e por que não é o conserto

A 0.2.1 entregou **capacidade de diagnóstico**, não correção:

- `Nucleo/Registro.cs` — o app passou a escrever o que faz, em qual placa, e o
  que os motores dizem. Antes disto **não havia nada para olhar**, e é essa a
  lição de fundo: o `stderr` dos motores era capturado desde a Fase 2 e só
  aparecia se o processo morresse;
- a transcrição recusa a CPU sem escolha explícita (chave em Ajustes ›
  Transcrição). **Não vai barrar este usuário** — a placa dele funciona;
- o progresso e o log dizem o dispositivo.

#### O que decide o próximo passo

Duas respostas, e elas apontam para consertos opostos:

- o **`registro.log`** da reprodução. Se ele termina abruptamente, o sistema caiu
  junto e a pista é o driver; se termina com exceção, ela nomeia a causa;
- o **Monitor de Confiabilidade**: tela azul ou congelamento, e o código.

Mais: quanta RAM o notebook tem, e a duração da reunião.

**O conserto que já está desenhado, esperando confirmação:** fazer o mix em
blocos, o que derruba o pico de 1,4 GB para alguns MB. Vale por si, e é a
resposta certa se a pista for memória — mas fazê-lo agora seria consertar a
hipótese mais confortável em vez da causa.

#### O que este caso ensina, além dele mesmo

**Software que roda na máquina de outra pessoa precisa deixar rastro.** O bloco
de diagnóstico da Fase 4 dá a *foto* — versão, placa, modelos — e respondeu bem
às perguntas que ele foi feito para responder. A pergunta aqui era outra: o que o
app *fez*. Uma foto não responde isso, e a distância entre as duas custou uma
hipótese errada e uma versão inteira.

### 3.1 Motores como pacotes por acelerador

**Gatilho:** o instalador da Fase 4 ficar grande demais, ou o app precisar rodar
em máquina sem NVIDIA.

É o que encolheria o instalador de verdade ([PLANO.md](PLANO.md) §5): CUDA,
Vulkan e CPU como variantes baixáveis, em vez de tudo embutido. O motor de ata já
nasceu com o desenho certo — é um binário à parte, trocável sem tocar no app —,
mas os motores Python não.

### 3.2 A integração com o Teams

**Gatilho:** esquecer o mute do gravador de novo por causa do mute do Teams.

Espelhar o estado de mute, por WebSocket local ([PLANO.md](PLANO.md) §2.1). Não
depende de nada que as fases recentes mudaram.

### 3.3 A gestão de vozes que a VOZES.md descreve

**Gatilho:** a impressão vocal errar alguém com frequência.

Fila de revisão, play por amostra, indicador de saúde por perfil. O modelo de
dados já é o certo; falta a tela ([VOZES.md](VOZES.md) §6).

### 3.4 Linux e Mac

**Gatilho:** precisar rodar fora do Windows.

Os motores são multiplataforma; o núcleo não. Continua sendo decisão de ordem,
não dívida.

---

## 4. A transcrição que alimenta a ata

Medido em 14/08/2026 sobre `2026-08-13_14-30-15`, ao comparar a transcrição do
app com a do Notion. **Estes itens têm gatilho disparado**: o defeito acontece
em toda reunião gravada hoje.

### 4.1 O `hotwords` colapsa a segmentação — e é isso que erra os falantes

**Gatilho:** já disparou. Medido em duas gravações.

Mesmo áudio, mesmo `large-v3`, mesmo VAD. Só o vocabulário do projeto, passado
como `hotwords`, muda:

| | segs | palavras | cobertura da fala | segs > 25 s | tempo |
|---|---|---|---|---|---|
| **com** `hotwords` | 207 | 3540 | 84,1% | 15 | 1514 s |
| **sem** `hotwords` | **787** | **4055** | **89,8%** | **0** | **331 s** |

A atribuição de falante é **um rótulo por segmento do ASR**
([`Transcricao.cs:126`](../app-net/Nucleo/Transcricao.cs#L126)): quando o
segmento tem 43,7 s e três pessoas dentro, duas somem. Nesta reunião, **12% dos
segmentos carregavam 46% das palavras**. Sem `hotwords`, nenhum segmento passa
de 25 s.

Isto **replica o resultado 5 da Fase 0** (279 contra 752 segmentos) e fecha a
ressalva dele: não era artefato da gravação com microfone morto.

**E a decisão já está tomada, desde a Fase 0.** O resultado 5-A mediu que a
correção fonética a jusante empata com o `hotwords` na recuperação de nomes — 36
contra 36 — e concluiu que *"o prompt fica dispensável"*. A `CorrecaoFonetica`
foi escrita e roda em produção; o `hotwords` nunca foi desligado. **O app paga o
preço dos dois mecanismos e recebe o benefício de um.**

*Caminho:* confirmar o 5-A em gravação saudável (o corpus da §5 dá o áudio) e
desligar o `hotwords`. Ganha-se segmentação, fala, e 4,6× no tempo de
decodificação.

### 4.2 Os vetores de voz aprendem de áudio com mais de uma pessoa

**Gatilho:** já disparou, e este é o que **persiste entre reuniões**.

[`AprendizadoDeVozes.cs:42-64`](../app-net/Nucleo/AprendizadoDeVozes.cs#L42-L64)
ordena os trechos por duração decrescente e usa os três maiores. A guarda de
cross-talk olha só os **vizinhos** do segmento — não há guarda contra outra
pessoa **dentro** dele. Ordenar por duração é preferir ativamente os segmentos
de maior risco.

Medido contra o `mic.wav`, que é verdade e não estimativa: o bloco nº 1 do vetor
de voz de uma participante contém **13,2 s da voz do dono do microfone** — 30%
do bloco, contra 1,5% de taxa base na gravação inteira.

O `.wav` guardado em `trechos/` está limpo, porque `RecortarTrecho` corta em 4 s;
o **vetor** usa o intervalo inteiro. Quem audita a amostra ouvindo o arquivo não
encontra o defeito.

*Caminho, em duas linhas de código:* truncar o intervalo do vetor ao mesmo
tamanho do trecho guardado, e descartar do aprendizado qualquer bloco com
`mic.wav` ativo quando o falante não é o dono.

*Pendência que fica:* os vetores já aprendidos passaram por este caminho. Vale a
regra do risco 4 do [PLANO.md](PLANO.md) §5 — **perfil de voz se reinscreve, não
se conserta** —, e decidir isso é do dono do produto.

### 4.3 O gate do VAD 0,15 continua aberto, e agora se sabe por quê

**Gatilho:** o corpus da §5 trazer `system.wav` com silêncio de verdade.

A varredura sobre esta reunião não separou os limiares (4036–4055 palavras em
quatro configurações) porque foi feita no `mix.wav`, que tem **0,3% de silêncio
digital** — somar mic e sistema quase nunca dá zero exato, e isso apaga o
critério de invenção. O áudio certo é o `system.wav` de reuniões em que o dono
fala pouco, que é o padrão de uso comum.

### 4.4 O `FiltroDeSilencio` nunca roda

**Gatilho:** já disparou.

`Transcritor.ExecutarAsync` recebe `filtrarSilencio = false` por default e a
[`Ponte.cs:907`](../app-net/App/Ponte.cs#L907) não passa o parâmetro. A classe
inteira — com a justificativa do resultado 6-A, os testes e duas constantes
calibradas — é código morto no caminho da GUI. **Ligar ou remover; deixar morto
não é opção**, porque parece uma rede que não existe.

### 4.5 O `word_timestamps` é calculado e jogado fora

**Gatilho:** querer cortar segmento na troca de falante (o conserto de raiz da
§4.1).

[`motores/asr/motor.py:99`](../motores/asr/motor.py#L99) pede
`word_timestamps=True`; a [linha 114](../motores/asr/motor.py#L114) guarda só
`inicio`, `fim` e `texto`. Paga-se o alinhamento por palavra e descarta-se
exatamente o insumo que resolveria a atribuição de falante.

### 4.6 A escolha do modelo de diarização não chega ao pipeline

**Gatilho:** já disparou — a Fase 4 esbarrou nele ao tirar o token.

`diarizacao_padrao` no `app.json` e `diar_model` nas preferências do projeto são
colhidos na tela, salvos em disco, e **ignorados**. O motor pede o `community-1`
pelo nome ([`motores/diarizacao/motor.py`](../motores/diarizacao/motor.py)) e não
aceita parâmetro de modelo; `Transcritor.ExecutarAsync` não tem por onde passar
um.

É o mesmo defeito que a Fase 3 corrigiu no seletor de *ligar/desligar* a
diarização — o de *qual modelo* ficou para trás.

A Fase 4 tirou o "Pyannote 3.1" do catálogo por causa disso: era um download de
26 MB para um modelo que o app não sabe carregar, e, sem o token embutido, um
download que ainda por cima falha (ele tem portão). **Quando o seletor passar a
valer, a entrada volta** — e volta com pesos locais, como o `community-1` tem
hoje, senão ela reintroduz a dependência de token que a Fase 4 acabou de tirar.

---

## 5. A tarefa que **não** espera esta fase: comparar com outras fontes

Tudo o que a §1.6 e a §4 sabem veio de uma comparação com uma segunda fonte, e
**de uma única reunião**. Não dá para calibrar defaults com *n* = 1 — é a
disciplina do resultado 3-C da Fase 0, que já custou uma conclusão errada por
medir no corpus errado.

Por isso esta é a única coisa desta carta que **começa agora e roda em paralelo
às fases 3 a 5**: a cada reunião gravada, transcrever e resumir **em paralelo
por outras fontes**, e guardar as saídas junto da gravação. Não custa trabalho de
anotação, acontece durante a reunião de qualquer forma, e é o que transforma esta
carta de um caso num corpus.

**Por que mais de uma fonte.** O valor não é a outra fonte estar certa — o Notion
erra muito, escreveu "Jimmy", "Chudu" e "Odime" para o mesmo nome. É que ela erra
**de outro jeito**, com outro VAD, outra segmentação e outro viés. Uma fonte só
levanta hipótese: nesta investigação as duas primeiras hipóteses (o VAD, e depois
a cobertura dos buracos) **estavam erradas**, e quem decidiu foi a reexecução
controlada. Duas fontes separam "o áudio é difícil aqui" de "o nosso pipeline
falhou aqui".

| fonte | o que ela testa que o Notion não testa |
|---|---|
| **Notion** | já em uso; sem falantes, sem timestamps |
| **transcrição nativa do Teams / Meet** | falantes vindos do canal de cada participante — o mais próximo de verdade de referência para diarização que se consegue sem anotar |
| **o próprio app sem `hotwords`** | isola o mecanismo da §4.1, na mesma máquina e no mesmo áudio |
| **whisper.cpp** (já no acervo da Fase 0) | outro decodificador sobre o mesmo modelo |

O **Teams/Meet é o mais valioso**, e por um motivo que muda o que se pode medir:
os rótulos de falante dele não são estimativa. Onde existirem, o DER da nossa
diarização passa a ser medível em reunião real — hoje só é medível no acervo
anotado da Fase 0.

**A régua para abrir a fase:** três a cinco reuniões com pelo menos duas fontes
paralelas, cobrindo os dois padrões de uso — reunião em que o dono fala pouco
(onde o `system.wav` tem silêncio de verdade, e é o que fecha a §4.3) e reunião
em que fala muito.

---

## 6. O que **não** entra nesta fase

Para a lista não virar depósito:

- **redesign visual** — é a Fase 5, tem carta própria ([PLANO.md](PLANO.md) §3);
- **o que está na lista de "não se reabre"** do CLAUDE.md. A âncora de relógio, o
  resampler, o mute que escreve silêncio: custaram caro para acertar, são
  invisíveis quando certos, e "melhorar de passagem" já perdeu em campo uma vez;
- **tool calling de verdade** no motor de ata. O protocolo reserva o campo, e num
  4B ele troca um problema resolvido (injetar contexto) por um não resolvido (o
  modelo decidir quando chamar). Só entra se a 1.1 levar a um modelo maior;
- **keep-alive do motor de ata.** O modelo carrega em 5 s; manter o processo vivo
  economiza isso e prende 2,5 GB que a próxima transcrição vai querer. Só faz
  sentido se gerar várias atas em sequência virar rotina.

---

## 7. A régua desta fase

Cada item acima tem um gatilho porque **quase nenhum deles é sabidamente
necessário hoje**. A Fase 3 saiu de uso real e o que ela consertou saiu de uso
real; a Fase 6 deve sair do mesmo lugar.

**As exceções são a §1.6 e a §4 inteira**, cujos gatilhos já dispararam: elas
saíram de uma ata e uma transcrição reais, conferidas contra o áudio e contra
uma segunda fonte. Nelas o defeito não é uma hipótese esperando incomodar — ele
acontece em toda reunião gravada, e a §4.2 contamina dado que persiste.

Se um item for executado sem o gatilho ter disparado, o custo não é o tempo — é
que ele passa a ser mantido para sempre, e código que existe sem doer é código
que ninguém sabe por que existe.
