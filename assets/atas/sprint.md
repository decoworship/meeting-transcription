# Ata — Reunião de sprint

Interna. O leitor é o próprio time, então corte cerimônia e vá para o operacional. A ata precisa ser acionável dentro do board.

## Estrutura

```markdown
# Ata — Sprint [identificação] · [data]

**Participantes:** ...

## Situação da sprint
[Andamento geral: o que está no caminho, o que não.]

## Por pessoa

### [Nome]
**Em andamento:** [ISSUE-000 — descrição curta] · [status reportado]
**Concluído:** ...
**Impedimentos:** ...

## Impedimentos
[Consolidado, com quem precisa destravar e o que exatamente falta.]

## Mudanças de escopo
**Entrou:** [ISSUE — motivo]
**Saiu / repriorizado:** [ISSUE — motivo]

## Decisões

## Ações
- [ ] Ação — **Responsável** — prazo

## Pontos em aberto
```

## Notas específicas

**Extraia códigos de issue.** Padrões como `ABC-1234` são o elo entre a ata e o board — sempre preserve. Transcrição automática costuma quebrá-los ("PBC onze mil duzentos e dois", "P B C dash 11202"): reconstrua para o formato canônico quando o contexto permitir, e marque `[sic?]` quando não.

Se uma tarefa foi discutida sem código de issue, registre a descrição e marque `[sem issue]` — geralmente significa trabalho não rastreado, o que é informação relevante por si.

**Impedimentos aparecem duas vezes de propósito:** junto da pessoa (contexto) e consolidados (ação). A seção consolidada é a que alguém lê para agir. Se não houve impedimento, omita.

**Mudanças de escopo com motivo.** "Saiu da sprint" sem porquê é inútil em retrospectiva. Se o motivo não foi dito, marque `[motivo não registrado]` — a lacuna também é informação.

**Estimativas e capacidade:** se o time discutiu pontos, velocidade ou disponibilidade (férias, alocação em outro projeto), registre. É o que explica a sprint seguinte.

**Não transforme desabafo em impedimento formal.** "Isso tá chato de mexer" é comentário; "não consigo avançar sem o acesso ao ambiente" é impedimento. A diferença é se existe algo concreto a destravar.
