# Ata — Sessão de trabalho (cliente ou interna)

A menos estruturada e a mais valiosa de registrar bem. Não é reunião de status: é reunião onde algo foi construído, decidido ou depurado. O leitor futuro mais provável é você mesmo, meses depois, querendo saber **por que** decidiram daquele jeito.

Por isso o foco desta ata é o raciocínio, não o placar.

## Estrutura

```markdown
# Ata — [Tema da sessão] · [data]

**Participantes:** ... · **Contexto:** [1 linha: por que essa sessão aconteceu]

## Problema trabalhado
[O que estava em questão, com precisão suficiente para alguém de fora entender.]

## Decisões técnicas

### [Decisão]
**Definido:** ...
**Por quê:** ...
**Alternativas descartadas:** [opção — motivo do descarte]
**Implicações:** [o que isso obriga ou impede daqui pra frente]

## Descobertas
[Fatos apurados durante a sessão: causa raiz, comportamento de sistema, limitação de API, número real que ninguém sabia.]

## Ações
- [ ] Ação — **Responsável** — prazo

## Pontos em aberto
[O que ficou sem resposta, com a pergunta explícita.]

## Referências citadas
[Docs, links, arquivos, tickets, trechos de código mencionados.]
```

## Notas específicas

**Alternativas descartadas é a seção que justifica esta ata.** Quando alguém revisitar a decisão em três meses, a pergunta será "já pensamos em X?". Registre a opção e o motivo do descarte, mesmo que o motivo tenha sido pragmático ("levaria tempo demais") em vez de técnico. Se nenhuma alternativa foi discutida, omita o campo — não invente opções para preencher.

**Implicações** captura o que a decisão trava para o futuro: dependência nova, custo recorrente, caminho que deixou de estar disponível. Só inclua quando foi dito ou é consequência direta e inequívoca do que foi dito.

**Descobertas ≠ decisões.** Descobrir que um token expira em uma hora é descoberta; decidir renová-lo via cache é decisão. Separar as duas ajuda porque descobertas continuam verdadeiras mesmo quando a decisão é revertida.

**Preserve detalhe técnico literal.** IDs, nomes de variável de ambiente, endpoints, mensagens de erro, versões, valores de configuração. Esta é a única ata onde detalhe granular vale mais que concisão — é justamente o que não se recupera depois. Não abrevie nem "limpe" esses valores.

**Participantes e cerimônia:** mínimo. Ninguém consulta esta ata para saber quem estava na call.

**Se a sessão não concluiu nada**, diga isso claramente no início e concentre-se em Descobertas e Pontos em aberto. Sessão inconclusiva bem registrada economiza a repetição da sessão.
