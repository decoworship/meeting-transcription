---
name: transcricao-para-ata
description: Transforma transcrições de reunião em atas estruturadas em Markdown, com tipos distintos para reunião com cliente, sprint, sessão de trabalho, kickoff, apresentação de resultados e daily. Use sempre que o usuário fornecer uma transcrição, gravação transcrita, notas cruas de reunião, texto de Teams/Meet/Zoom, ou pedir "ata", "minuta", "resumo da reunião", "meeting notes" ou "MoM" — inclusive quando ele apenas colar um texto longo que claramente é fala transcrita de uma reunião, sem pedir a ata explicitamente. Também use quando pedirem para extrair action items, decisões ou pendências de uma reunião.
---

# Transcrição → Ata

Transforma transcrição bruta em ata útil. O objetivo não é resumir a conversa: é produzir um registro que sirva para cobrar, decidir e consultar depois.

## Passo 1: Identificar o tipo de reunião

Leia a transcrição e classifique. Sinais úteis:

| Tipo | Sinais | Referência |
|---|---|---|
| Update com cliente | participantes de outra organização, revisão de status por frente, cobrança de prazos | `references/cliente-update.md` |
| Sprint | discussão de issues, capacidade, impedimentos, "entra"/"sai" do escopo | `references/sprint.md` |
| Sessão de trabalho | discussão técnica aprofundada, alternativas, decisões de arquitetura, debug conjunto | `references/trabalho.md` |
| Kickoff | primeira reunião, definição de escopo, papéis, cronograma macro | `references/kickoff.md` |
| Apresentação de resultados | um lado apresenta números, o outro reage; perguntas e questionamentos de dados | `references/resultados.md` |
| Daily / status curto | menos de ~15 min de fala, rodada rápida, sem decisões estruturais | `references/daily.md` |

Se o tipo for ambíguo — ou se a reunião for mista, o que é comum (update com cliente que virou sessão de trabalho) — pergunte ao usuário antes de escrever, oferecendo sua leitura como sugestão. Não gaste tempo com isso quando o tipo é óbvio.

Reuniões mistas: use a referência dominante e adicione a seção mais relevante da outra. Não force duas estruturas completas em uma ata.

Leia a referência escolhida antes de escrever. Cada uma define a estrutura de seções específica daquele tipo.

## Passo 2: Regras comuns a todos os tipos

Estas regras são a parte que mais importa. Uma ata que erra aqui é pior que nenhuma ata, porque cria falsa memória.

### Separar decidido de discutido

O erro mais frequente em ata automática é promover uma hipótese a decisão. "Acho que poderíamos migrar pro SAM" **não** é uma decisão; é uma ideia levantada. Só entra em **Decisões** o que teve conclusão explícita, ou concordância clara de quem tem autoridade para decidir.

Tudo que foi levantado e não concluiu vai para **Pontos em aberto**, com a pergunta que ficou sem resposta. Essa seção costuma ser a mais valiosa da ata — não a esvazie por parecer incompleta.

### Action items exigem dono

Formato: `- [ ] Ação — **Responsável** — prazo`

Se a transcrição não nomeia responsável, escreva `[responsável a definir]`. Se não há prazo, `[prazo a definir]`. Nunca atribua ao participante que parece mais plausível: um dono inventado faz a tarefa não ser cobrada de ninguém.

Se o dono for de outra organização, marque a organização junto ao nome. Depende-de-terceiros é informação de gestão.

### Fidelidade

Registre o que foi dito, não o que faria sentido ter sido dito. Se a transcrição está truncada, confusa ou com fala inaudível relevante, sinalize no final da ata em **Observações sobre a transcrição** — não preencha a lacuna por inferência.

Se números foram citados (volume, adoção, percentual), transcreva exatamente. Se um número parecer inconsistente com outro citado na mesma reunião, registre ambos e sinalize a divergência em vez de escolher um.

Transcrições automáticas erram nomes próprios, siglas e termos técnicos. Corrija quando o contexto deixa claro (ex.: "Jira" transcrito como "gira", códigos de issue mal segmentados). Quando não estiver claro, mantenha o original e marque com `[sic?]`.

### Idioma

Escreva a ata no idioma predominante da transcrição. Reunião em inglês → ata em inglês, incluindo os títulos das seções. Reunião em português com termos técnicos em inglês → ata em português, preservando os termos como falados. Não traduza nomes de projetos, produtos ou sistemas.

### O que deixar de fora

Small talk, problemas de conexão, agendamento de próxima call que não gerou compromisso, e desentendimentos interpessoais. Discordância *técnica* relevante fica — ela explica a decisão. Atrito pessoal não.

## Passo 3: Cabeçalho padrão

Toda ata começa assim, ajustando os campos ao tipo:

```markdown
# Ata — [Nome da reunião / projeto]

**Data:** [data] · **Duração:** [se inferível]
**Participantes:** [nomes, agrupados por organização quando houver mais de uma]
**Ausentes citados:** [apenas se relevante]
```

Se a data não estiver na transcrição, use `[data a confirmar]` em vez de assumir a data de hoje.

## Passo 4: Entrega

Saída em Markdown, direto na conversa, pronto para copiar. Não crie arquivo a menos que o usuário peça — ata é conteúdo de consumo rápido, e a maioria vai colar em Jira, Confluence ou e-mail.

Ao final, ofereça em uma linha: converter para .docx, extrair só os action items, ou gerar mensagem de follow-up para os participantes. Não execute sem pedido.

Se a ata contiver algo que merece atenção — action item sem dono, decisão que contradiz outra da mesma reunião, número inconsistente — mencione em uma ou duas linhas depois da ata. Isso é mais útil que qualquer seção extra.
