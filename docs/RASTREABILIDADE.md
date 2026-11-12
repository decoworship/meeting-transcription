# Rastreabilidade — da ata ao acompanhamento do projeto

Aberto em 10/11/2026. É um roteiro de quatro passos, e não o desenho de nenhum
deles: cada passo tem o seu desenho em `docs/superpowers/specs/` e o seu plano em
`docs/superpowers/plans/`, e nenhum virou código ainda.

| passo | desenho | plano | estado em 12/11/2026 |
|---|---|---|---|
| 1 · Ata em que dá para confiar | [a ata conferida](superpowers/specs/2026-11-11-ata-conferida-design.md) | [plano](superpowers/plans/2026-11-11-ata-conferida.md) | discutido com o dono ponto a ponto, com protótipo visto; plano escrito, nada construído |
| 2 · Pendências que atravessam reuniões | [as pendências do projeto](superpowers/specs/2026-11-11-pendencias-do-projeto-design.md) | [plano](superpowers/plans/2026-11-11-pendencias-do-projeto.md) | desenho inicial; plano escrito sobre respostas assumidas |
| 3 · Briefing antes da reunião | [o briefing da reunião](superpowers/specs/2026-11-11-briefing-da-reuniao-design.md) | [plano](superpowers/plans/2026-11-11-briefing-da-reuniao.md) | desenho inicial; plano escrito sobre respostas assumidas |
| 4 · Envio e compartilhamento | [envio e compartilhamento](superpowers/specs/2026-11-11-envio-e-compartilhamento-design.md) | [plano](superpowers/plans/2026-11-11-envio-e-compartilhamento.md) | desenho inicial; plano escrito, com um portão antes do envio pelo Gmail |

**Os planos dos passos 2 a 4 foram escritos em 12/11/2026, a pedido do dono,
antes de as perguntas dos desenhos serem respondidas.** Cada um abre com a
tabela "Respostas assumidas": o que foi assumido em cada pergunta e que tarefas
mudam se a resposta for outra. Ela é para ser lida antes de construir o passo.

**Os desenhos foram escritos antes de implementar, a pedido do dono**, que tinha
outras coisas a resolver primeiro. Os passos 2 a 4 não foram discutidos como o
1: cada um termina numa lista de perguntas que precisa de resposta antes do
plano. O que está abaixo é o resumo de cada passo e o raciocínio que levou a
eles; onde o resumo e o desenho discordarem, **vale o desenho**.

## O que se quer

O PulseMeet hoje grava, transcreve e escreve a ata de **uma** reunião. O que o
dono do produto quer é a ferramenta com que ele **gerencia o projeto pelas
reuniões**:

- uma linha do tempo, por projeto, do que foi discutido, decidido e entregue;
- um painel das pendências, com dono e estado, que atravessa as reuniões;
- a ata formalizada por e-mail com o time do cliente, sempre;
- um briefing antes de cada reunião recorrente — o que está pendente e o que
  ficou combinado na última —, que possa ir para os convidados na véspera;
- as pendências compartilhadas com outras pessoas.

Tudo isso se apoia numa coisa só: **confiar no que a ata diz**. Um painel de
pendências alimentado por uma ata que inventa ou omite é pior que nenhum painel.
Por isso a ordem é esta, e cada passo depende do anterior.

## O caso que orienta o passo 1

Reunião de 21/10/2026, Pedido Sugerido, Coca Cola - Sorocaba
(`2026-10-21_11-30-55`). A ata do app foi ao cliente; a Tati respondeu que ela
"não captou tudo" e que "alguns pontos não fazem sentido", e escreveu a dela.
Comparadas as duas com a transcrição, **a transcrição tinha tudo** o que a Tati
escreveu. A ata do app errou de quatro jeitos:

| erro | o que a ata disse | o que foi dito |
|---|---|---|
| omissão | nada sobre os impulsionados | 12:15–19:36 e de novo no recap, 28:42: "os impulsionados do app, você vai falar com o Diego" |
| explicação virou decisão | "Decidido: a aderência acumulada considera o último dia de vendas" | 23:37 é o Yuri explicando como é hoje; o combinado foi ele revisar o cálculo (25:33), e essa ação se perdeu |
| hipótese virou fato | "90% das sugestões estão abaixo do pedido mínimo" | 09:54: "poxa, tem 90%…" é um exemplo |
| erro de transcrição propagado | "a sugestão de funcionário deve ser de apenas um funcionário" | era "impulsionado", Estratégia 1 e 2; o termo saiu também como "funcionado" e "imperdoáveis" |

Três são da escrita da ata e um nasce na transcrição. O vocabulário do projeto
não tem "impulsionado", "Estratégia", "CCIL" nem "aderência".

Dois achados a mais:

- **A reunião traz o próprio gabarito.** Aos 28:42 a Tati recapitula em voz alta
  os cinco itens, e são os cinco da ata dela;
- **a formalização melhora e não fecha.** Gerada em 08/11 sobre a mesma reunião
  (`AtaDeFormalizacao`), ela pegou os impulsionados e não repetiu os outros três
  erros, mas ainda perde a revisão do cálculo do BI. E o `ata.json` **não guarda
  as linhas citadas**: a prova de cada item existe na extração e é jogada fora.

## O teste de 11/11/2026 — o Claude escrevendo a ata

As três últimas reuniões do RTM Merchan com o cliente (16/10, 29/10 e 05/11).
O Claude leu só a transcrição e as notas e escreveu a ata de formalização **sem
ver a referência**; depois as três foram comparadas com
`ata-experimento/ref`. As atas do teste estão em
`C:\Users\andre\ata-experimento\claude-2026-11-11`.

| reunião | itens na referência | Claude cobriu | ata do app cobriu |
|---|---|---|---|
| 05/11 | 11 | 10, e 1 pela metade | ~6 |
| 29/10 | 10 | 9 | ~4 |
| 16/10 | 18 | 17 | ~7 |
| **total** | **39** | **36 (92%)** | **~17 (44%)** |

**O que o Claude errou**, e cada erro diz algo do desenho:

- perdeu o prazo da PEC (rota até 20/11, valendo em 01/12), dito aos 17:01 da
  reunião de 16/10, numa conversa antes da pauta;
- escreveu "as lojas novas são 30, e não 26" como fechado, quando ficou de ser
  conferido — o mesmo tipo de erro do modelo local, numa taxa muito menor;
- pôs "hoje" onde a referência diz "até amanhã" (a reunião diz as duas coisas);
- um item da referência de 29/10 — como margem e CTS pesam no Planner — **não
  está no áudio**: veio do material apresentado. Nenhum modelo o tira da
  transcrição; ele só entra pelas notas, pelo print ou pela conferência.

**O que a ata do app errou** nas mesmas reuniões: "renegociar o contrato"
(ninguém vai renegociar: nenhum contrato obriga), "corte em seis meses" (era
exemplo), dono trocado em duas pendências, e números embaralhados.

**As ressalvas.** São três reuniões, e quem julgou a cobertura foi o próprio
Claude. Duas das três atas do app são do formato antigo, geradas no dia, e não
da formalização — o número do app é aproximado, e bate com os ~51–54% medidos
em 06/11. As referências são as atas que foram por e-mail (confirmado pelo dono
em 11/11/2026).

**Um achado de transcrição:** em 05/11, de 11:51 a 13:41, quem fala é o
Marcelo, e a transcrição atribui a fala ao Paulo. Uma pendência dessas falas
sairia com o dono errado.

## Passo 1 — Ata em que dá para confiar

**Pronto quando:** o dono manda a ata ao cliente depois de uma conferência de
poucos minutos, e o cliente não precisa reescrevê-la.

O que se propôs em 10/11, e que o
[desenho](superpowers/specs/2026-11-11-ata-conferida-design.md) fechou — os
itens 2, 4 e 5 entraram nele; o 1 virou a régua do §9 dele; o 3 ficou para
depois da régua, como experimento medido:

1. **A régua antes do conserto.** O gabarito é a ata que foi de fato por e-mail,
   guardada como `ata-email.md` na pasta da gravação (plano de 08/11, meta de
   ~20 até 22/11; hoje há zero no acervo). A da Tati de 21/10 é a primeira. A
   medida é cobertura dos itens e erros por tipo — os quatro da tabela.
2. **A prova à vista.** Cada item da ata guarda de onde saiu (falas e tempo), e
   uma tela de conferência mostra item por item com "ouvir o trecho", aprovar,
   corrigir ou tirar. A ata passa a ter estado: rascunho, conferida, enviada. É
   isto que transforma "confiar no modelo" em "conferir em três minutos".
3. **Os quatro erros, um a um.** Omissão: usar o recap falado como lista de
   conferência quando ele existe, e medir uma segunda passada sobre o que ficou
   sem citação. Decisão que não é decisão: item sem a concordância do outro lado
   sai como "a confirmar", e não como "decidido". Termo errado: completar o
   vocabulário do projeto a partir das atas já validadas.
4. **O modelo.** Decidido pelo dono em 10/11/2026: **o Claude entra como opção
   para escrever a ata**, e modelo local maior fica fora — não cabe na 2060. O
   modelo local continua sendo o padrão e o que funciona sem rede. Os dois
   erros de leitura (hipótese e explicação) são do tipo que modelo de 4 B
   comete. **A rota foi decidida em 11/11/2026: o Claude Desktop, pelo MCP que
   já existe**, com um pedido pronto e uma ferramenta de escrita que grava só
   rascunho; chave de API com botão no app fica fora por ora. **A conferência
   é uma só para os dois caminhos** — a única diferença é quem escreve o
   rascunho, o botão do app ou o pedido no Claude Desktop. O Claude faz **a
   extração com citação**, e a conferência determinística e a tela do item 2
   valem igual: a confiança vem da prova, e não do tamanho do modelo. O
   rascunho local chega com mais omissão, e a tela não mostra o que ficou de
   fora: ela precisa de "adicionar item" à mão.
5. **O formato.** A formalização como padrão de reunião com cliente; todo item
   com responsável, e o lado (nosso ou do cliente) quando o nome não é
   conhecido. **A conferência vale só para a formalização neste passo**
   (decidido em 11/11/2026): é a única que cita as falas de cada item, a que
   vai ao cliente e a que alimenta o passo 2. Os outros tipos de ata continuam
   como estão.

**Não é falta de contexto.** O motor dimensiona o contexto pelo tamanho do
prompt e recusa a reunião que não cabe (`MotorDeAta.Dimensionar`); esta, de 30
minutos, coube inteira. O modelo leu tudo e mesmo assim deixou item de fora.

## Passo 2 — Pendências que atravessam reuniões

**Pronto quando:** abrir um projeto mostra o que está aberto, com quem, desde
quando, e a história de cada item.

- Hoje a pendência é uma caixinha `- [ ]` dentro do `ata.md` de cada reunião
  (`Pendencias.cs`), sem identidade fora dela. Passa a existir um registro por
  projeto: item com id, origem (reunião e trecho), responsável, prazo, estado e
  histórico;
- ao conferir uma ata nova, o app propõe o que ela fecha ou atualiza das
  abertas ("a base foi enviada em 26/10" fecha a pendência de 21/10), e a pessoa
  confirma;
- a linha do tempo do projeto sai do mesmo registro: decisões e entregas por
  data;
- **só ata conferida alimenta o registro**;
- absorve o `ATA-2` (a ata anterior como contexto) e entra no MCP, para o
  Claude ler as pendências.

## Passo 3 — Briefing antes da reunião recorrente

**Pronto quando:** na véspera de uma reunião de série, existe um texto curto
pronto para mandar: pendências abertas por lado, o que se decidiu na última, o
que foi entregue desde então.

- Sai do registro do passo 2, sem modelo ou com o mínimo dele;
- **primeiro só dentro do app**, para copiar e mandar à mão; o envio direto
  entra depois que o passo 4 estiver funcionando (dono, 11/11/2026);
- a série é achada pela agenda (o evento recorrente) ou por cliente e projeto;
- **como chega aos convidados:** a API do Google Calendar não tem comentário em
  evento. Descrição e anexo só podem ser alterados por quem organiza — e anexo
  é arquivo do Drive. O convite de Sorocaba é organizado pela Tati, de outro
  domínio; nele não se escreve. O caminho que serve a qualquer convite é um
  e-mail aos convidados (os endereços já estão no `meta.json`), com o assunto
  da reunião. Responder dentro da conversa do convite exige ler a caixa de
  entrada, que é o escopo mais caro do Gmail. Quando o dono é o organizador,
  escrever o briefing na descrição do evento fica como opção.

## Passo 4 — Envio e compartilhamento

**Pronto quando:** a ata conferida vai ao cliente de dentro do app, e outra
pessoa consegue ver as pendências do projeto sem ter o PulseMeet.

- **E-mail.** A conexão com o Google é reaproveitada: mesmo cliente OAuth, mais
  um escopo, e a pessoa consente de novo. Hoje o escopo é só
  `calendar.readonly`. Enviar (`gmail.send`) é escopo "sensível"; ler ou criar
  rascunho são "restritos", e num app publicado para qualquer conta isso pede
  verificação do Google. **Conferir a classificação e o estado do nosso cliente
  OAuth antes de desenhar.** O degrau zero já existe: Copiar leva a ata pronta
  para colar;
- **Compartilhar.** O app é local e não tem servidor, então o painel precisa de
  um lugar para morar. O desenho propõe dois degraus: primeiro um arquivo por
  projeto numa pasta que já sincroniza (sem permissão nova), e só depois, se
  fizer falta, uma planilha do Google escrita com o escopo `drive.file` — que só
  alcança arquivos criados pelo próprio app;
- **antes do Gmail, um degrau sem permissão:** abrir o programa de e-mail com
  destinatários e assunto preenchidos, e a ata na área de transferência;
- **nada sai sozinho.** Enviar é sempre um gesto da pessoa, sobre ata conferida;
- **o envio registra a si mesmo.** No passo 1 "enviada" é marcada à mão; aqui
  passa a ser automática e fiel ao que saiu — quando, para quem e com que texto
  (dono, 11/11/2026).

## O que seria fracasso

- uma ata formalizada com item inventado ou com decisão ao contrário;
- pendência que entra no painel sem ninguém ter conferido;
- e-mail enviado ao cliente sem um gesto explícito;
- o painel virar mais um lugar para manter à mão.

## Decisões em aberto

- onde mora o painel compartilhado? (passo 4)

Fechadas em 11/11/2026, todas do passo 1: a ata não conferida pode ser copiada
e exportada, e só não alimenta as pendências — uma reunião sem importância fica
no rascunho; o gabarito é a ata que foi por e-mail, e ata escrita pelo Claude
não mede ata escrita pelo Claude; a conferência tem que ser rápida, com a
reunião ainda fresca, e a meta é cinco minutos.
