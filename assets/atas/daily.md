# Ata — Daily / status curto

Formato enxuto. O erro aqui é o oposto dos outros tipos: excesso de estrutura. Uma daily de 10 minutos não sustenta oito seções, e uma ata inflada não é lida.

Meta: caber na tela, colável em Slack ou Teams sem edição.

## Estrutura

```markdown
# Daily — [data]

**Presentes:** [nomes, linha única]

## Rodada
- **[Nome]:** [o que fez / o que vai fazer] · Impedimento: [se houver]

## Impedimentos
- [Impedimento] — precisa de **[quem]**

## Ações
- [ ] Ação — **Responsável** — prazo
```

## Notas específicas

**Uma linha por pessoa.** Comprima. Se alguém falou por três minutos, ainda é uma linha — o detalhe pertence a outro tipo de reunião.

**Omita seções vazias.** Sem impedimento, sem seção de impedimento. Sem ação nova, sem seção de ações. Uma daily pode legitimamente produzir só a rodada.

**Não invente estrutura de sprint.** Se a daily virou discussão longa de decisão técnica ou repriorização de escopo, ela deixou de ser daily — use `sprint.md` ou `trabalho.md` e avise o usuário que reclassificou.

**Códigos de issue:** preserve quando citados, mas não force. Daily costuma falar por nome de tarefa, não por código.

**Sem seção de decisões**, salvo se algo realmente foi decidido — nesse caso acrescente uma seção curta. Daily normalmente não decide, e uma seção de decisões vazia ou preenchida com quase-decisões é justamente o vício que essa ata deve evitar.
