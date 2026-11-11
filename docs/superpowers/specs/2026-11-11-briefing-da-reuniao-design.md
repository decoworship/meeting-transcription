# O briefing da reunião

Aberto em 11/11/2026. **Estado: desenho inicial, escrito a pedido do dono para
registrar a direção antes de implementar.** As decisões marcadas *(decisão
minha)* são propostas, e as perguntas do §9 precisam de resposta antes do plano.

É o passo 3 do [RASTREABILIDADE](../../RASTREABILIDADE.md). **Depende do passo
2** ([as pendências do projeto](2026-11-11-pendencias-do-projeto-design.md)): o
briefing é uma leitura do registro, e não tem informação própria.

---

## 1. De onde se parte

Antes de cada reunião de uma série, o dono quer chegar — e quer que os outros
cheguem — sabendo o que está pendente e o que ficou combinado na última. Hoje
isso é reler a ata anterior, ou pedir ao Claude o `preparar-reuniao`, que relê
as atas e devolve uma pauta no chat.

Uma decisão do dono já delimita o passo (11/11/2026): **primeiro o briefing
existe só dentro do app, para copiar e mandar à mão; o envio direto entra depois
que o passo 4 estiver funcionando.**

## 2. O que muda, em uma frase

**Para cada projeto, o app monta um texto curto e pronto para mandar — o que
está pendente de cada lado, o que se decidiu na última reunião e o que foi
entregue desde então — e lembra dele na véspera da próxima reunião da série.**

## 3. Objetivos, e o que fica de fora

**Objetivos**

1. Um briefing **sempre disponível** na página do projeto, a um clique de
   copiar.
2. **Um lembrete na véspera** da próxima reunião da série, quando a agenda
   permitir saber qual é.
3. O texto no formato em que a ata já sai — colável num e-mail, com HTML e
   texto puro.

**Fora deste desenho**

- **Enviar.** Nem e-mail, nem escrever no convite: é o passo 4. Aqui a pessoa
  copia.
- **Pauta sugerida, riscos, análise.** O briefing diz o que está registrado. O
  que pede julgamento continua com o Claude (o `preparar-reuniao`, que passa a
  partir do registro — passo 2, §7).
- Qualquer conteúdo de ata não conferida.

**O que seria fracasso**

- o briefing dizer ao cliente que algo está pendente quando já foi entregue —
  porque o painel estava atrasado e ninguém avisou;
- uma nota interna aparecer num texto que vai para o cliente;
- qualquer coisa sair sozinha.

## 4. O conteúdo

**Sem modelo.** *(Decisão minha.)* Tudo o que entra já foi escrito e conferido
no passo 1 e ligado no passo 2; montar o briefing é escolher e ordenar. É o que
o torna instantâneo, igual toda vez, e incapaz de inventar.

Na ordem:

1. **Cabeçalho:** o projeto, a reunião a que ele se refere (quando se sabe) e
   "posição em dd/mm".
2. **Pendentes do lado do cliente**, depois **do nosso lado** — o que falta,
   quem, prazo; e a marca de vencida quando houver data. As mais antigas
   primeiro.
3. **Decidido na última reunião** — as decisões da ata conferida mais recente
   do projeto, com a data dela.
4. **Entregue desde a última reunião** — as pendências concluídas depois dela.
5. **Próxima reunião**, se a última ata a registrou.

Seção vazia não aparece. Um projeto sem nada aberto tem um briefing de duas
linhas, e está certo.

**O que não entra nunca:** nota interna, item retirado, item de ata em
rascunho, e a fala citada — a prova é para quem confere, não para quem lê o
e-mail.

**A ordem do cliente primeiro** é proposta: o briefing vai para os convidados, e
o que depende deles é o que mais se beneficia do lembrete. *(Decisão minha; o
dono pode inverter.)*

## 5. O aviso de atraso

O briefing só é tão bom quanto o registro. Antes do texto, **na tela e nunca no
que se copia**, o app diz o que pode estar errado:

- "A reunião de 05/11 deste projeto ainda não foi conferida. O briefing vai até
  29/10."
- "Há 3 pendências com prazo vencido — confira se alguma já foi entregue antes
  de mandar."

O segundo aviso é o que evita o fracasso número um: cobrar o que já chegou.

## 6. A tela

Na página do projeto (passo 2), um painel **Briefing**:

- o texto montado, como vai sair;
- **caixas para tirar itens** daquele envio, sem mexer no registro — uma
  pendência que não vale lembrar agora;
- uma linha de abertura opcional, escrita pela pessoa;
- *Copiar* (HTML e texto, como a ata) e a sugestão de assunto, "Briefing —
  [título da reunião] — [data]", copiável à parte.

O que a pessoa tirou ou escreveu **não é guardado** entre um briefing e outro.
*(Decisão minha: guardar transformaria o briefing em mais um documento para
manter.)*

## 7. A véspera

O lembrete precisa saber qual é a próxima reunião do projeto. O app já lê a
agenda do Google (`ClienteDaAgenda`, só leitura), mas hoje só olha perto de
agora, para o gravador.

- **A série.** A agenda diz quando um evento é parte de uma série
  (`recurringEventId`); o app passa a ler esse campo e a guardá-lo no
  `meta.json`, junto do `calendar_event_id` que já guarda. Uma série fica ligada
  a um projeto pela **reunião mais recente dela que tem cliente e projeto**.
  *(Decisão minha.)* Sem série — reunião marcada avulsa toda semana —, vale o
  título igual ao de reuniões já ligadas ao projeto; e sem isso, não há
  lembrete, e o briefing continua na página do projeto.
- **A véspera.** Uma vez por dia o app olha a agenda do dia seguinte. Para cada
  evento ligado a um projeto com algo a dizer, avisa pela bandeja: "Amanhã,
  11:30 — Pedido Sugerido. O briefing está pronto." O aviso abre o painel.
- **Se o app não estava aberto na véspera**, o aviso sai na manhã do dia, ao
  abrir.
- **Ligar e desligar** em Ajustes, por projeto, e desligado para quem não
  conectou a agenda. Nasce desligado, como tudo o que é novo e interrompe.
  *(Decisão minha.)*

**A conferir na implementação:** o convite de um dos projetos é organizado pelo
cliente, de outro domínio e outro calendário. O evento chega à agenda do dono
como cópia, e não está confirmado que a cópia traz o `recurringEventId`. Se não
trouxer, aquele projeto cai no caminho do título.

## 8. O que não pode quebrar

- **O escopo da agenda continua só leitura** neste passo. Escrever no convite é
  assunto do passo 4, e só quando o dono for o organizador.
- **A leitura da agenda roda ao lado da gravação**, com timeout curto e sem
  nunca atrapalhar o gravador — a regra que o `ClienteDaAgenda` já segue.
- **Sem rede, sem agenda, sem série:** o briefing existe do mesmo jeito, na
  página do projeto. O lembrete é conveniência; o texto não depende dele.

## 9. Perguntas para o dono, antes do plano

1. **Para quem vai o briefing?** Para todos os convidados, cliente incluído — ou
   existe uma versão só para o time de casa, com as notas internas?
2. **Cliente primeiro ou nós primeiro**, na ordem das pendências?
3. **A que horas é "a véspera"?** Um horário fixo (17h), ou ao abrir o app no
   dia anterior?
4. **O briefing também vale para reunião que não é de série** — a próxima
   reunião marcada de um projeto, mesmo avulsa?
5. **Quando o passo 4 chegar:** o briefing vai num e-mail novo, ou — nos
   convites em que você é o organizador — escrito na descrição do evento?

## 10. Critérios de pronto

1. Num projeto com atas conferidas, o painel mostra o briefing sem esperar por
   modelo nenhum, e *Copiar* cola certo no Gmail e no Outlook.
2. Um projeto com a última ata em rascunho mostra o aviso do §5, e o texto não
   traz nada dela.
3. Nota interna e item retirado não aparecem em nenhuma variação do texto.
4. Na véspera de uma reunião de série ligada a um projeto, o aviso aparece uma
   vez, e abre o painel certo.
5. O dono manda o briefing de três reuniões reais, e o que ele teve que mudar
   à mão antes de mandar fica registrado no RASTREABILIDADE.
