# Envio e compartilhamento

Aberto em 11/11/2026. **Estado: desenho inicial, escrito a pedido do dono para
registrar a direção antes de implementar.** É o passo mais distante e o que mais
depende de coisas fora do app — a conta do Google, a verificação do Google, onde
o time e o cliente trabalham. As decisões marcadas *(decisão minha)* são
propostas, e as perguntas do §9 **mudam o desenho**, e não só o detalhe.

É o passo 4 do [RASTREABILIDADE](../../RASTREABILIDADE.md). Depende do passo 1
([a ata conferida](2026-11-11-ata-conferida-design.md)) para o envio, e do passo
2 ([as pendências do projeto](2026-11-11-pendencias-do-projeto-design.md)) para o
compartilhamento.

---

## 1. De onde se parte

Hoje a ata sai do app pelo *Copiar*: a pessoa cola no e-mail, escolhe os
destinatários, escreve o assunto e manda. Funciona, e tem três custos:

- os destinatários são digitados de novo, embora o `meta.json` da reunião já
  tenha os e-mails dos convidados;
- o app **não sabe o que foi enviado** — nem se foi. No passo 1, "enviada" é uma
  marca à mão;
- as pendências do projeto só existem dentro do app de quem gravou.

O dono quer a ata formalizada por e-mail **sempre**, e quer que outras pessoas
acompanhem as pendências. E deixou dito (11/11/2026) que, com o envio pelo app,
**o registro de "enviada" passa a ser automático e fiel ao que aconteceu**.

## 2. O que muda, em uma frase

**A ata conferida e o briefing saem de dentro do app com os destinatários já
preenchidos, o app guarda o que saiu de fato, e as pendências de um projeto
ficam visíveis para quem não tem o PulseMeet.**

São duas entregas independentes — **enviar** e **compartilhar** —, e cada uma
tem degraus. Podem sair em versões separadas, em qualquer ordem.

## 3. Objetivos, e o que fica de fora

**Objetivos**

1. Mandar a ata conferida em poucos cliques, sem redigitar destinatário.
2. Registrar o envio sozinho: quando, para quem, com que texto.
3. Mandar o briefing (passo 3) pelo mesmo caminho.
4. Dar a outra pessoa uma vista das pendências do projeto, sempre atual.

**Fora deste desenho**

- **Ler a caixa de entrada.** Nem para responder dentro da conversa do convite,
  nem para saber se o cliente respondeu. É o escopo mais caro do Gmail (§4.3).
- **Enviar qualquer coisa sem um gesto da pessoa**, agendado ou automático.
- **Um servidor nosso.** O app é local e continua local.
- Enviar ata que não foi conferida.

**O que seria fracasso**

- um e-mail para o cliente que a pessoa não mandou conscientemente;
- o mesmo e-mail duas vezes, por uma tentativa repetida em silêncio;
- o app dizer "enviada" sem ter enviado, ou não dizer tendo enviado;
- a pendência interna, ou a nota interna, visível para o cliente;
- o painel compartilhado virar um segundo lugar para manter à mão.

## 4. Enviar

### 4.1 Degrau 1 — abrir no programa de e-mail, sem permissão nova

Um botão *Abrir no e-mail* na ata conferida: o app põe a ata na área de
transferência (como o *Copiar*) e abre o programa de e-mail padrão com **os
destinatários e o assunto já preenchidos** (um link `mailto:`). A pessoa cola o
corpo e manda.

- Os destinatários saem dos convidados da reunião, sem ela mesma.
- O corpo não vai pelo link de propósito: `mailto:` só leva texto puro, com
  limite de tamanho, e a ata perderia o negrito e as listas.
- Funciona com Gmail, Outlook ou o que estiver instalado, e **não pede nada ao
  Google**.
- O registro continua à mão ("Marcar como enviada"), porque o app não vê o
  envio.

É pouco, e resolve o custo que mais se repete — redigitar destinatário. *(Decisão
minha: vale sair antes do degrau 2, que depende da verificação do Google.)*

### 4.2 Degrau 2 — enviar pelo Gmail, de dentro do app

A conexão com o Google é a mesma da agenda: o mesmo cliente OAuth, **mais um
escopo, `gmail.send`**, pedido só quando a pessoa clica em enviar pela primeira
vez. Quem usa só a agenda nunca vê o pedido. *(Decisão minha.)*

O fluxo:

1. *Enviar por e-mail*, na ata conferida, abre uma tela com **Para** (os
   convidados, editável), **Cc**, **Assunto** ("Ata — [título] — [data]"), uma
   linha de abertura opcional e **o corpo como vai chegar**.
2. *Enviar* pede uma confirmação que diz para quantas pessoas vai, e de que
   domínios.
3. O app manda, e **só então** registra: a data, os destinatários, o
   identificador que o Gmail devolve, e o texto exato — que vira o
   `ata-email.md`, o gabarito do passo 1.
4. A mensagem aparece nos Enviados da pessoa, como qualquer outra.

**As regras que evitam os fracassos:**

- **Uma tentativa por gesto.** Se o envio falha, a tela diz o erro e a pessoa
  decide. Se o envio **fica sem resposta** (queda de rede no meio), o app não
  sabe se saiu: ele diz isso, pede para conferir os Enviados, e **não tenta de
  novo sozinho**.
- **"Enviada" só com a confirmação do Gmail.**
- **Editar a ata depois de enviada** é permitido e fica marcado: "mudou depois
  do envio de 21/10". O `ata-email.md` guarda o que foi, e não o que é.
- **Reenviar** é outro gesto, com outra confirmação, e vira um segundo envio no
  registro — não substitui o primeiro.

O briefing usa a mesma tela, com assunto "Briefing — [título] — [data]".

### 4.3 O que o Google exige

Conferido na documentação do Google em 11/11/2026:

| escopo | classe | o que custa |
|---|---|---|
| `gmail.send` — só enviar | **sensível** | verificação do app pelo Google; sem auditoria de segurança |
| `gmail.compose` — criar rascunho | **restrito** | verificação **e** auditoria de segurança de terceiros |
| `gmail.readonly` — ler | **restrito** | idem |

Por isso o desenho é **só enviar**: nada de rascunho no Gmail, nada de responder
dentro da conversa do convite (que exige ler a caixa para achar a conversa).

**O que falta conferir antes do plano**, e não se resolve lendo documentação:

- **o estado do nosso cliente OAuth.** O FASE4-HANDOFF deixou aberto se ele
  está em *Testing* — em que só contas cadastradas autorizam e o token expira em
  7 dias — ou publicado. Acrescentar um escopo sensível a um app publicado pede
  nova verificação;
- **a conta de trabalho.** Uma conta do Google Workspace pode ter a
  administração bloqueando apps de terceiros nos escopos do Gmail. Vale testar
  com a conta de onde as atas saem antes de construir a tela.

### 4.4 O convite

O dono perguntou se dava para deixar tudo no convite. Não dá, em geral:

- a agenda do Google **não tem comentário** em evento;
- descrição e anexo só podem ser mudados por quem tem permissão de editar o
  evento — em geral, quem organiza —, e anexo é arquivo do Drive;
- parte das reuniões é organizada pelo cliente, de outro domínio.

O que fica como opção, **só para os convites em que o dono é o organizador**:
escrever o briefing na descrição do evento. Pede o escopo de escrita na agenda
(`calendar.events`, também sensível) e muda uma coisa que os convidados já
receberam. Fica fora do primeiro corte; a decisão é do dono (§9).

## 5. Compartilhar

O que se compartilha é **a vista do passo 2**: as pendências abertas de um
projeto, por lado, com responsável e prazo, e as concluídas recentes. Sem nota
interna, sem fala citada, sem áudio.

### 5.1 Degrau 1 — um arquivo numa pasta que já sincroniza

O app escreve, a cada mudança do registro, **um arquivo por projeto** — uma
página HTML que se lê em qualquer navegador, e uma planilha `.xlsx` — numa pasta
que a pessoa escolhe. Se essa pasta é do OneDrive ou do Google Drive, ela
compartilha o link uma vez, e quem abre vê sempre a versão mais nova.

- **Nenhuma permissão nova, nenhum servidor.** A pasta das gravações do dono já
  vive numa pasta sincronizada; a de exportação é configurável desde sempre.
- **Uma via só:** o app escreve, os outros leem. O que alguém mudar no arquivo é
  sobrescrito na próxima atualização — e o arquivo diz isso no topo.
- "Em tempo real" aqui quer dizer **o tempo da sincronização**, alguns segundos
  a minutos depois de a pessoa conferir uma ata ou fechar uma pendência.

*(Decisão minha: é o degrau que entrega "outra pessoa vê as pendências" sem
depender do Google.)*

### 5.2 Degrau 2 — uma planilha do Google por projeto

O app cria e mantém uma planilha no Drive da pessoa, com o escopo `drive.file` —
que só alcança os arquivos que o próprio app criou. A pessoa compartilha a
planilha pelo Google, como qualquer outra.

Vale a pena **só se** o degrau 1 não bastar: se o time vive no Google e quer
comentar, filtrar e ver no celular sem baixar nada. A classificação do
`drive.file` e o que ele pede de verificação ficam **a conferir**: ele costuma
ser descrito como o escopo recomendado e não sensível, e isso não foi
confirmado na documentação para este desenho.

### 5.3 O que não se promete

**Duas vias** — o cliente marcar como feita uma pendência dele, e isso voltar ao
app — não está em nenhum dos degraus. Exige ler o que os outros escreveram,
decidir quem pode mudar o quê, e resolver conflito com o que a ata diz. Se o
dono quiser, é um passo 5, e a pergunta do §9 existe para decidir isso cedo.

## 6. O que não pode quebrar

- **O segredo do Google** é obrigatório no binário publicado, e já saiu
  faltando uma vez; o `publicar.sh` confere. Um escopo novo não muda o segredo,
  mas muda o consentimento: quem já conectou a agenda **continua conectado**, e
  só reconsente ao usar o envio.
- **Os destinatários vêm de listas paralelas** (`attendees` e
  `attendee_emails`, o `GRA-2` do BACKLOG): o envio usa **só os e-mails**, e
  nunca casa nome com e-mail por posição.
- **O e-mail é HTML montado pelo app** (`htmlParaEmail`), a partir de texto que
  veio de um modelo e de uma pessoa. Continua montado por nós, escapado, sem
  nada externo — imagem remota, script, link que a ata não tinha.
- **Nada de reunião vai para lugar novo sem a pessoa saber.** O ⓘ de Ajustes
  passa a dizer o que o envio manda (a ata, para os destinatários que ela
  escolheu) e o que o compartilhamento escreve (um arquivo, na pasta que ela
  escolheu).
- A regra do passo 1 continua: **só ata conferida** é enviada.

## 7. A ordem sugerida

1. Enviar, degrau 1 (*Abrir no e-mail*) — pequeno, sem dependência externa.
2. Compartilhar, degrau 1 (o arquivo na pasta) — depende só do passo 2.
3. Conferir o cliente OAuth e a conta de trabalho (§4.3).
4. Enviar, degrau 2 (Gmail) — e com ele o envio do briefing.
5. O resto, se fizer falta: a planilha do Google, a descrição do convite.

## 8. Critérios de pronto

**Enviar**

1. *Abrir no e-mail* abre o programa padrão com os convidados e o assunto, e a
   ata cola com a formatação.
2. Pelo Gmail: a mensagem chega com a formatação da ata, aparece nos Enviados,
   e o registro guarda quando, para quem e o texto.
3. Com a rede cortada no meio do envio, o app não marca como enviada e não
   reenvia sozinho.
4. Quem só usa a agenda nunca vê o pedido do escopo novo.

**Compartilhar**

5. Fechar uma pendência no app atualiza o arquivo do projeto, e outra pessoa,
   com o link, vê a mudança sem instalar nada.
6. O arquivo não traz nota interna, fala citada, nem item de ata em rascunho.

## 9. Perguntas para o dono, antes do plano

1. **De que conta as atas saem?** A mesma conta do Google em que a agenda está
   conectada, ou outra (Outlook, outro endereço)? Se não for a mesma, o degrau 2
   do envio muda de natureza.
2. **Quem precisa ver as pendências?** O time de casa, o cliente, ou os dois —
   e eles vivem no Google, na Microsoft, ou em nenhum dos dois?
3. **Ver basta, ou eles precisam mexer?** Se o cliente tem que poder marcar o
   que entregou, isso é duas vias (§5.3) e precisa ser desenhado de outro jeito.
4. **O arquivo numa pasta sincronizada serve como primeiro degrau**, ou você já
   sabe que precisa ser uma planilha do Google?
5. **Vale escrever na descrição do convite** quando você é o organizador, ou o
   e-mail basta?
6. **O app vai ser usado por mais gente** com o envio ligado? Para uma pessoa
   só, o cliente OAuth em modo de teste pode bastar; para outras, a verificação
   do Google entra no caminho, com o prazo dela.
