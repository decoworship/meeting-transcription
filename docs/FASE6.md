# Fase 6 — revisões: carta de execução

Escrita em 14/08/2026, ao fechar a Fase 3, por decisão do dono do produto:
**lançar primeiro, melhorar depois.** Tudo o que as fases anteriores marcaram
como "revisitar" vem para cá, em vez de atrasar a primeira versão.

Esta carta é diferente das outras. As anteriores descrevem o que **vai** ser
feito; esta lista o que **pode** ser feito, cada item com o **gatilho** que
justifica fazê-lo. Uma fase de revisões sem gatilhos vira lista de desejos, e
lista de desejos se executa pela ordem de quem gosta mais — não pela ordem do
que dói.

**A ordem é a do incômodo, não a desta lista.** Nada aqui é obrigatório.

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
de cobertura depois. **Se não bastarem**, em ordem de custo:

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

## 4. O que **não** entra nesta fase

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

## 5. A régua desta fase

Cada item acima tem um gatilho porque **nenhum deles é sabidamente necessário
hoje**. A Fase 3 saiu de uso real e o que ela consertou saiu de uso real; a Fase
6 deve sair do mesmo lugar.

Se um item for executado sem o gatilho ter disparado, o custo não é o tempo — é
que ele passa a ser mantido para sempre, e código que existe sem doer é código
que ninguém sabe por que existe.
