# O motor de ata: arquitetura

Desenho do item 3 da Fase 3 — a ata gerada por LLM local. Escrito em 14/08/2026,
antes de escrever código, porque três perguntas do dono do produto mudavam o
desenho inteiro e duas delas se respondem medindo.

A carta da fase está em [FASE3.md](FASE3.md) §4; o que **este** documento decide
é como o motor é feito por dentro. As skills que ele executa estão em
`transcrição para atas/` na raiz do repositório.

---

## 1. As três perguntas, respondidas com número

### "As skills são grandes demais para um modelo pequeno?"

**Não. Não é nem perto disso.** Medido com um tokenizador BPE multilíngue (o do
`large-v3`, que está em cache — não é o do Qwen, mas a razão chars/token em
português fica na mesma ordem):

| o quê | tokens |
|---|---:|
| `SKILL.md` inteiro | 1.716 |
| `references/daily.md` | 466 |
| `references/sprint.md` | 655 |
| `references/cliente-update.md` | 666 |
| `references/resultados.md` | 707 |
| `references/kickoff.md` | 749 |
| `references/trabalho.md` | 798 |
| **skill + uma referência** | **≈ 2.400** |

Num contexto de 32k, a instrução ocupa **7%**. O que ocupa o resto é a
transcrição, medida nas suas gravações reais:

| gravação | duração | tokens | por minuto |
|---|---:|---:|---:|
| 2026-08-13 (Vivo) | 29,2 min | 6.951 | 238 |
| 2026-08-12 (Algar) | 21,3 min | 6.203 | 291 |

**Uma reunião de 1 h cabe folgada**: 14–17,5k tokens de transcrição + 2,4k de
instrução + a ata gerada (1–2k) = ~20k dos 32k. O aperto começa perto de **1h45**
e vira problema real em 2h30. Ou seja: a divisão em blocos existe, mas **não é o
caminho comum** e não deve ser construída antes de medir.

### "Como vamos usar as skills?"

O problema não é tamanho, é **para quem elas foram escritas**. O `SKILL.md`
instrui um agente que lê, classifica, escolhe a referência, e — se o tipo for
ambíguo — **pergunta ao usuário**. Um 4B local não faz nada disso bem.

**A inversão que resolve: o app classifica, o modelo redige.** O tipo de reunião
já é escolhido na tela (com padrão vindo das preferências do projeto), então o
Passo 1 da skill nunca chega ao modelo. O que chega é só o tipo escolhido, sem
ramificação nenhuma:

```
prompt = regras comuns (do SKILL.md, §"Passo 2")
       + esqueleto do tipo escolhido (o bloco ```markdown da referência)
       + notas específicas do tipo (o resto da referência)
       + contexto da reunião (participantes, cliente/projeto, vocabulário)
       + as notas do humano
       + a transcrição
```

**As skills continuam sendo a fonte única.** Os sete arquivos vão embutidos no
executável como estão, e a montagem acontece em tempo de execução, recortando
por título de seção. Nada de uma segunda cópia "adaptada para o modelo local":
cópia paralela diverge, e a que diverge em silêncio é a que gera ata errada seis
meses depois. Um teste garante que os títulos que o recorte procura ainda existem
— se alguém reescrever o `SKILL.md`, a suíte reprova antes do usuário descobrir.

O que **não** vai para o modelo, e por quê:

| trecho da skill | destino |
|---|---|
| Passo 1 (classificar o tipo) | some: quem classifica é o usuário na tela |
| "pergunte ao usuário antes de escrever" | some: não há conversa, há um botão |
| "reuniões mistas: use a dominante" | some: consequência da classificação |
| Regras comuns (decidido × discutido, dono, fidelidade, idioma) | **vai inteiro** — é o que impede ata errada |
| Esqueleto de seções do tipo | vai, e também vira contrato de validação (§4) |
| Notas específicas do tipo | vai |

### "Dá para adicionar tools para a LLM?"

Dá, e a resposta útil é **em qual nível cada coisa paga**. Do que mais paga para
o que menos paga:

| nível | o que é | vale na v1? |
|---|---|---|
| **0. Dados injetados** | participantes da agenda, cliente/projeto, vocabulário, notas do humano, ata anterior do mesmo projeto — tudo no prompt | **sim, é a base** |
| **1. Saída constrangida** | gramática GBNF do llama.cpp forçando o formato da resposta | **sim, é o que mais muda o resultado** |
| **2. Verificação a jusante** | o C# confere dono inventado, número inventado, código de issue | **sim** (§4) |
| **3. Tool calling de verdade** | o modelo decide chamar `buscar_trecho(...)`, `pendencias_da_ata_anterior(...)` | **não na v1** |

O nível 3 é tecnicamente possível — o llama.cpp suporta *tool calling* com os
templates do Qwen, e o Qwen 3 foi treinado para isso. Mas num 4B ele troca um
problema resolvido (injetar 300 tokens de contexto) por um não resolvido (o
modelo decidir *quando* chamar, acertar os argumentos, e não entrar em laço). O
ganho real aparece em reunião longa demais para caber, onde buscar trecho vira
alternativa ao map-reduce — e isso é o item que só se constrói depois de medir.

**A interface do motor prevê o nível 3 sem implementá-lo**: o pedido de geração
já carrega uma lista de ferramentas disponíveis (vazia hoje). É barato deixar o
campo, e caro reabrir o protocolo depois.

---

## 2. A forma da arquitetura

```
   tela "Atas"                    Nucleo/                        motores/ata/
   ───────────                    ───────                        ────────────
   escolhe reunião  ──ponte──▶  MotorDeAtas                       motor.py
   escolhe tipo                   ├─ monta o prompt   ──stdio──▶  llama.cpp
   [Gerar]                        │   (skills + dados)             (GGUF)
                                  ├─ gramática do tipo  ─────────▶
                                  ◀──────────────────  JSON da ata
                                  ├─ Verificador (§4)
                                  ├─ Redator → ata.md
                                  ▼
                                ata.md na pasta da gravação
```

**Sidecar, como os outros motores.** JSON por linha em stdin/stdout, o contrato
do [SIDECAR.md](SIDECAR.md), reaproveitando o `MotorSidecar` que já existe — com
o cancelamento que a Fase 3 acabou de ligar (matar o processo é o que devolve a
VRAM na hora, e vale igual para a ata).

**Um processo por geração, sem keep-alive.** Isto **revisa** o que a FASE3.md §4
antecipou do Meetily. Eles mantêm o processo quente porque servem requisições de
resumo o tempo todo; aqui se gera **uma ata por reunião**, com o usuário
esperando na frente da tela. Carregar 2,6 GB uma vez por ata é aceitável; manter
2,6 GB de VRAM ocupados enquanto o usuário grava a próxima reunião não é. Se a
medição mostrar carga acima de ~20 s, a decisão se reabre — e o keep-alive entra
como opção, não como padrão.

**O modelo entra pelo catálogo que já existe.** `PacoteDeModelo` ganha a família
`"ata"`, e o GGUF baixa pela mesma tela dos modelos de ASR e diarização, com
tamanho esperado e verificação de download interrompido. Nada de um segundo
mecanismo de download.

**Nunca junto do ASR.** A trava de "um motor pesado por vez"
(`RegistroDeTranscricoes`) passa a valer para os dois: um 4B em Q4_K_M (~2,6 GB)
não convive com o `large-v3` numa placa de 6 GB. Gerar ata durante uma
transcrição é recusado com a mesma frase que nomeia quem está ocupando a placa.

---

## 3. A saída: JSON constrangido, e o Markdown montado aqui

**O modelo não escreve o arquivo final.** Ele preenche uma estrutura; o C#
renderiza o Markdown. Três razões, em ordem de peso:

1. **Verificar exige campos.** "Este action item tem dono?" é uma pergunta sobre
   um campo, não sobre um parágrafo. Sem estrutura, o critério E da fase — nenhum
   dono errado, nenhuma decisão inventada — vira leitura humana de cada ata;
2. **Formato garantido.** Com gramática GBNF, o modelo **não consegue** emitir
   um action item fora de `- [ ] Ação — **Responsável** — prazo`, porque o
   decodificador não deixa. É a diferença entre pedir formato e impor formato, e
   num 4B ela é grande;
3. **A ata vira dado.** Exportar em DOCX, colar no e-mail, listar "todas as ações
   em aberto do cliente X" — tudo isso é trivial com campos e caro com prosa.

**Um esquema por tipo, derivado do esqueleto da referência.** Cada referência já
traz o esqueleto num bloco ```` ```markdown ````, e ele mapeia quase um a um:

| seção do esqueleto | tipo de campo | como é renderizada |
|---|---|---|
| `## Situação da sprint` | texto curto | parágrafo |
| `## Por pessoa` | lista de `{nome, em_andamento, concluido, impedimentos}` | subtítulo por pessoa |
| `## Decisões` | lista de texto | lista |
| `## Ações` | lista de `{acao, responsavel, prazo}` | `- [ ] … — **X** — prazo` |
| `## Pontos em aberto` | lista de texto | lista |

O renderizador é **um só e genérico**: lê a declaração de seções do tipo e
monta. Acrescentar um sétimo tipo de reunião passa a ser escrever uma referência
e uma declaração de dez linhas — não escrever código.

**Seções vazias somem**, como o `daily.md` manda. É regra da skill que o
renderizador aplica, em vez de esperar que o modelo lembre.

---

## 4. O verificador: onde a ata deixa de ser confiável

É a peça que existe por causa do critério E, e a razão de o modelo local ser
aceitável. Roda sobre o JSON, antes de virar Markdown, e é **determinístico**:

| verificação | o que faz quando falha |
|---|---|
| **dono inventado** — responsável que não é participante da agenda, nem falante da transcrição, nem nome conhecido | troca por `[responsável a definir]` e registra o que veio |
| **número inventado** — algarismo na ata que não aparece na transcrição | marca com `[conferir]` |
| **código de issue** — `ABC-1234` que não aparece na transcrição | marca com `[sic?]`, como a skill pede |
| **decisão sem âncora** — item de "Decisões" sem sobreposição léxica com nenhum trecho | move para "Pontos em aberto" |
| **prazo relativo** — "sexta", "semana que vem" | resolve contra a data da reunião, ou deixa como veio |

A quarta é a mais valiosa e a mais grosseira: promover hipótese a decisão é o
erro que estraga ata, e um 4B tende **mais** a isso que um modelo de fronteira.
Sobreposição léxica não é entendimento — é uma rede embaixo, e uma rede grosseira
que pega metade dos casos vale mais que nenhuma.

**Nada disto é silencioso.** O que o verificador mexeu vai para o fim da ata, em
"Observações sobre a transcrição", que é seção que a skill já prevê.

---

## 5. As features do motor

Em ordem de construção. As cinco primeiras são a v1.

1. **Gerar ata de uma reunião transcrita**, com tipo escolhido pelo usuário e
   padrão vindo das preferências do projeto;
2. **Contexto injetado**: participantes da agenda, cliente/projeto, vocabulário
   do projeto, notas do humano (§6), data e duração;
3. **Saída constrangida por gramática**, com esquema por tipo;
4. **Verificação a jusante** (§4), com as correções registradas na própria ata;
5. **`ata.md` na pasta da gravação**, regenerável, com aviso quando a transcrição
   mudou depois da ata gerada;
6. **Regenerar com ajuste** — mudar o tipo e gerar de novo sem repetir a
   transcrição;
7. **Exportar** nos formatos que a exportação já tem (a ata entra como mais um
   conteúdo, não como um segundo mecanismo);
8. **Reuniões longas por blocos** (§7), *se* a medição mostrar que faz falta;
9. **Ata anterior como contexto** — "o que ficou pendente na última reunião deste
   projeto" é a pergunta que mais se faz numa série de reuniões, e o app é o
   único que sabe respondê-la;
10. **Ferramentas para o modelo** (nível 3 do §1), atrás do campo que o protocolo
    já reserva.

---

## 6. As notas do humano têm precedência

Ordem de confiança no prompt, dita explicitamente ao modelo:

1. **as notas** (`notas.md`) — escritas por quem estava na reunião;
2. **o cabeçalho** — título, cliente, projeto, participantes da agenda;
3. **a transcrição** — a melhor tentativa de uma máquina.

Quando a nota e a transcrição discordam sobre um número, um nome ou uma decisão,
**a nota ganha**, e a divergência é registrada. Este é o pagamento do item 2 da
fase: as notas não existem só para o humano reler.

---

## 7. Reuniões longas, quando chegarem

Acima de ~1h45 a transcrição não cabe. O caminho é map-reduce, e ele **degrada a
ata** — perde-se a visão do todo que faz "Situação da sprint" ter sentido:

1. dividir por janelas de tempo com sobreposição (a sobreposição existe para uma
   decisão tomada na fronteira não sumir);
2. extrair de cada janela o mesmo JSON, sem redigir;
3. consolidar os JSONs num só, deduplicando ações e decisões;
4. redigir do consolidado.

**Não construir antes de medir.** Metade das reuniões deste usuário tem menos de
30 minutos, e um caminho de blocos existindo é um caminho de blocos sendo usado
por engano.

---

## 8. O que a medição precisa responder, antes de escrever o motor

No espírito da Fase 0, e com as gravações reais que já existem no disco:

| # | pergunta | como |
|---|---|---|
| 1 | `llama-cpp-python` no Python embarcado ou binário do llama.cpp ao lado? | instalar os dois, medir tamanho em disco, tempo de carga e tokens/s |
| 2 | O 4B cabe na 2060 com o ASR descarregado, e em quanto tempo gera? | gerar a ata de uma reunião de 29 min, cronometrando |
| 3 | Qwen 3 4B ou Gemma 3 4B? | a mesma transcrição nos dois, comparando com a ata da skill num modelo de fronteira |
| 4 | A gramática GBNF segura o formato sem estragar o conteúdo? | gerar com e sem, comparar |
| 5 | Quanto o modelo local inventa? | contar donos e decisões inventadas — é o critério E |

A 5 é a que decide se a fase entrega ou se a decisão de provedor reabre.

---

## 9. O risco, dito em voz alta

**Um 4B não segue 1.700 tokens de instrução como um modelo de fronteira segue.**
Toda a arquitetura acima é uma sequência de compensações para isso: o app
classifica em vez do modelo, a gramática impõe em vez de pedir, o verificador
confere em vez de confiar.

Se, mesmo assim, a ata inventar decisão, o resultado honesto é registrar e
reabrir a escolha de provedor — não entregar uma ata em que não se pode confiar.
Uma ata errada é pior que nenhuma, porque cria memória falsa.
