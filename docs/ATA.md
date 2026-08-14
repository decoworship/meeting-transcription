# O motor de ata: arquitetura

Desenho do item 3 da Fase 3 — a ata gerada por LLM local. Escrito em 14/08/2026,
antes de escrever código, porque três perguntas do dono do produto mudavam o
desenho inteiro e duas delas se respondem medindo.

> **Medido na máquina do usuário em 14/08/2026** (RTX 2060 de 6 GB, Qwen3-4B
> Instruct Q4_K_M, llama.cpp b10427), com as gravações reais dela. Os números
> estão no §8, e três coisas que este documento supunha mudaram:
>
> - **a reunião de 2 h cabe numa passada só** — 42.822 tokens em contexto de
>   49k, com o KV em q4_0. O caminho de blocos do §7 **sai da v1**;
> - **o conteúdo saiu bom e o formato saiu torto**: nenhum número inventado,
>   nenhum dono fora da lista de participantes, e mesmo assim o Markdown livre
>   derrapou do formato pedido. É a medição que justifica a saída constrangida
>   do §3 — que, testada, saiu **JSON válido de primeira**;
> - **o modelo carrega em 5 s e a VRAM pode ficar ocupada** (decisão do dono do
>   produto: gravar não disputa GPU). O keep-alive volta à mesa — ver §2.

A carta da fase está em [FASE3.md](FASE3.md) §4; o que **este** documento decide
é como o motor é feito por dentro. As skills que ele executa estão em
`transcrição para atas/` na raiz do repositório.

---

## 1. As perguntas do dono do produto, respondidas com número

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
instrução + a ata gerada (1–2k) = ~20k dos 32k.

> **A medição depois corrigiu esta estimativa para pior e o limite para melhor.**
> Com o tokenizador do próprio Qwen, uma reunião de 122 minutos deu **42.822
> tokens** — mais denso que a régua acima. Em compensação, ela **coube numa
> passada só** em contexto de 49k com o KV em q4_0, que é o que a placa de 6 GB
> aguenta (§8). Blocos ficam para reunião de 3 h.

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

### "Vamos precisar ajustar as skills para o modelo menor?"

**Cirurgia, não reescrita** — e agora isso é medido, não opinião. O Qwen3-4B leu
as regras comuns inteiras e as obedeceu no conteúdo: não inventou número, não
inventou dono, separou decidido de discutido, excluiu small talk e registrou o
que não deu para inferir (§8). O que ele **não** fez foi obedecer ao *formato*:
escreveu `**Responsável: Dimi Randel**` onde a skill pede
`Ação — **Responsável** — prazo`.

Isso desenha exatamente onde mexer:

| na skill | ajuste | por quê |
|---|---|---|
| Passo 1 (classificar) | **remover do prompt** | quem classifica é o usuário na tela |
| "pergunte ao usuário" | **remover do prompt** | não há conversa, há um botão |
| Regras comuns | **nada** | foram obedecidas como estão |
| Notas específicas do tipo | **nada** | idem |
| Esqueleto de seções (o bloco ```markdown) | **vira esquema JSON** | é a parte que o modelo erra, e a única que dá para impor por decodificação |

O esqueleto deixar de ser texto e virar esquema **não é adaptar a skill para o
modelo pequeno** — é tirar do modelo uma responsabilidade que ele não precisa
ter. Um modelo de fronteira também erraria menos com o formato imposto; a
diferença é que nele o erro é raro e aqui é a regra.

### "As skills são fixas ou dá para customizar?"

**As duas coisas, com precedência.** As seis que existem vão embutidas no
executável e são a base; quem quiser mexer, mexe numa pasta do perfil:

```
%USERPROFILE%\.meeting-transcription\atas\
    sprint.md            ← substitui a embutida, se existir
    comite-de-dados.md   ← um tipo novo, que passa a aparecer na tela
```

Regras:

- **arquivo do usuário ganha** do embutido de mesmo nome;
- **um arquivo novo = um tipo novo** na lista da tela, sem recompilar nada;
- **o esquema não é do usuário.** Ele é universal (§3): resumo, seções, decisões,
  ações, pontos em aberto, riscos, observações. O que o arquivo do usuário
  descreve é *quais seções produzir e o que pôr nelas* — não a forma da saída.
  É isso que permite customizar escrevendo Markdown, sem escrever JSON Schema;
- **as embutidas não são editadas no lugar.** Customizar copia para a pasta do
  perfil e edita a cópia, e um botão "voltar ao original" apaga a cópia. Editar o
  embutido faria a atualização do app apagar o trabalho do usuário sem avisar.

O ganho não é estética: **ata por cliente**. "As reuniões da Vivo têm uma seção
de SLA no fim" é exatamente o tipo de coisa que o usuário sabe e o app não, e
casa com o vocabulário por projeto que já existe.

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

**Um processo por geração, sem keep-alive — e agora por outro motivo.** A
primeira versão deste documento justificava isso com a VRAM: manter 2,6 GB
presos atrapalharia a gravação. **O dono do produto corrigiu**: a transcrição já
ocupa a placa inteira e a gravação continua funcionando, porque capturar áudio
não usa GPU. A justificativa caiu.

O que sobrou de justificativa é mais fraca, e por isso a decisão vira ajuste em
vez de dogma: **o modelo carrega em 5 segundos** (medido, modelo em SSD), então
um processo por ata custa 5 s por ata. Manter o motor quente economiza esses 5 s
e custa um processo vivo e 2,5 GB de VRAM que a próxima transcrição vai querer —
e a transcrição, essa sim, aperta os 6 GB.

**A regra que fica:** um processo por geração na v1, e um `--manter-vivo N` no
sidecar para quando gerar várias atas em sequência virar rotina. A trava de "um
motor pesado por vez" continua valendo entre **ata e transcrição**, que disputam
a mesma placa — e não entre ata e gravação, que não disputam nada.

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

**Um esquema universal, e não um por tipo.** É o que permite customizar
escrevendo Markdown (§1): o arquivo do tipo diz *quais seções produzir*, e o
esquema garante a forma do que é verificável.

```
resumo            texto
secoes[]          { titulo, situacao?, texto }   ← a parte específica do tipo
decisoes[]        texto
acoes[]           { acao, responsavel, prazo, lado }  ← "nosso" ou "cliente"
pontos_em_aberto[] texto
riscos[]          texto
observacoes[]     texto                          ← o que o verificador escreveu
```

O esqueleto de cada referência vira a **lista de seções esperadas** — usada para
pedir ao modelo e para conferir o que voltou, não para gerar um esquema por
tipo. O que se perde: as seções muito estruturadas de alguns tipos (o "Por
pessoa" da sprint e da daily) caem em `secoes[].texto` como prosa, em vez de
virar lista de pessoas. É perda aceitável na v1, e o dia em que incomodar,
`secoes[]` ganha um campo opcional de itens.

O mapeamento, para um update com cliente:

| seção do esqueleto | tipo de campo | como é renderizada |
|---|---|---|
| `## Situação da sprint` | texto curto | parágrafo |
| `## Por pessoa` | lista de `{nome, em_andamento, concluido, impedimentos}` | subtítulo por pessoa |
| `## Decisões` | lista de texto | lista |
| `## Ações` | lista de `{acao, responsavel, prazo}` | `- [ ] … — **X** — prazo` |
| `## Pontos em aberto` | lista de texto | lista |

O renderizador é **um só e genérico**: recebe o JSON e monta o Markdown na
ordem canônica. Acrescentar um sétimo tipo de reunião passa a ser **escrever um
arquivo Markdown** — nem código, nem esquema (§1).

**Medido:** com JSON Schema pelo `llama-server`, o Qwen3-4B devolveu JSON válido
de primeira, com responsáveis reais e `[prazo a definir]` onde faltava prazo
(§8). Sem o esquema, o mesmo modelo escreveu o responsável fora do formato.

**Seções vazias somem**, como o `daily.md` manda. É regra da skill que o
renderizador aplica, em vez de esperar que o modelo lembre.

---

## 4. O verificador: onde a ata deixa de ser confiável

É a peça que existe por causa do critério E, e a razão de o modelo local ser
aceitável. Roda sobre o JSON, antes de virar Markdown, e é **determinístico**:

| verificação | o que faz quando falha |
|---|---|
| **dono inventado** — responsável que não é participante da agenda, nem falante da transcrição, nem nome conhecido | troca por `[responsável a definir]` e registra o que veio |
| **falante genérico na lista de participantes** — `Speaker 1`, `Speaker 8` | tira da lista: medido na ata da reunião de 2 h, onde os não nomeados entraram como se fossem gente |
| **número inventado** — algarismo na ata que não aparece na transcrição | marca com `[conferir]` |
| **código de issue** — `ABC-1234` que não aparece na transcrição | marca com `[sic?]`, como a skill pede |
| **decisão sem âncora** — item de "Decisões" sem sobreposição léxica com nenhum trecho | move para "Pontos em aberto" |
| **prazo relativo** — "sexta", "semana que vem" | resolve contra a data da reunião, ou deixa como veio |
| **número não incorporado** — citado na transcrição e ausente da ata | lista em "Observações"; é a rede contra **omissão**, que é o modo de falha real do modelo pequeno (§8) |

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
8. ~~**Reuniões longas por blocos**~~ — **sai da v1**: a medição mostrou que uma
   reunião de 2 h cabe numa passada só, com contexto de 49k e KV em q4_0 (§8). O
   §7 fica registrado para o dia em que aparecer uma reunião de 3 h;
9. **Ata anterior como contexto** — "o que ficou pendente na última reunião deste
   projeto" é a pergunta que mais se faz numa série de reuniões, e o app é o
   único que sabe respondê-la;
10. **Ferramentas para o modelo** (nível 3 do §1), atrás do campo que o protocolo
    já reserva.

---

## 5.1 Quem é da casa e quem é do cliente

**O domínio do e-mail decide, e não o assunto da conversa.** Pedido do dono do
produto em 14/08/2026, a partir de um erro concreto: a ata atribuiu uma ação a
"Andre Monlevade (Vivo)" — Andre é da equipe, e o modelo deduziu a organização
porque a reunião falava de Vivo o tempo todo.

Como funciona:

1. **a agenda passa a guardar os e-mails.** O `meta.json` ganhou
   `attendee_emails`, chave nova pela mesma regra do `dropped_samples`: quem lê
   ignora o que não conhece, então acrescentar é seguro. O e-mail sempre esteve
   disponível no evento do Google — só era descartado na hora de gravar;
2. **configura-se só a nossa lista de domínios**, em Ajustes › Transcrição.
   `beegol.com` na lista significa que todo o resto é cliente. Não se cadastra o
   domínio de cada cliente: essa regra não precisaria de manutenção hoje e
   precisaria no dia em que aparecesse um cliente novo;
3. **o prompt recebe os dois grupos** ("Da nossa equipe: …", "Do cliente (Vivo):
   …") e a instrução explícita de não deduzir organização pelo assunto;
4. **o verificador corrige o lado** de cada pendência pelo domínio, e registra
   quantas mudaram — é fato contra chute, e o chute era do modelo;
5. **o redator agrupa os participantes** no cabeçalho, que é o que o esqueleto
   da ata de cliente sempre pediu e não dava para cumprir.

**Sem e-mail, não se afirma nada.** As gravações anteriores a esta versão só
guardam o nome; nelas a organização fica desconhecida e o verificador não mexe
no lado que o modelo escolheu. Preferir "não sei" a chutar é a diferença entre
uma ata que se pode conferir e uma que inventa com confiança.

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

> **Medido: o limite não é 1h45, é ~2h15** — e nesta placa, com KV em q4_0. A
> reunião de 122 minutos do usuário coube numa passada só. Esta seção deixa de
> ser plano e vira registro para quando aparecer uma reunião de 3 h.

Acima disso a transcrição não cabe. O caminho é map-reduce, e ele **degrada a
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

## 8. A medição, feita em 14/08/2026

Máquina do usuário: RTX 2060 de 6 GB (≈950 MiB já ocupados pelo desktop),
driver 595.97. Modelo **Qwen3-4B-Instruct-2507 Q4_K_M** (2,5 GB), llama.cpp
b10427, build CUDA 12.4. Ferramenta: `tools/medir_motor_de_ata.py`, que monta o
mesmo prompt que o app vai montar.

### Cabe, e quanto demora

| reunião | tokens do prompt | contexto / KV | tempo | pico de VRAM |
|---|---:|---|---:|---:|
| 29 min, 205 trechos | 10.258 | 16k / q8_0 | **55 s** | 4.732 MiB |
| 42 min, 964 trechos | ~19.900 | 24k / q8_0 | **98 s** | 5.386 MiB |
| 122 min, 2.058 trechos | **42.822** | 32k / q8_0 | — | 5.727 MiB, **não coube** |
| 122 min, 2.058 trechos | 42.822 | 49k / **q4_0** | **236 s** | 5.343 MiB |

Velocidade: **1.500–1.600 t/s** processando o prompt e **44 t/s** gerando em
contexto curto; **866 t/s / 17 t/s** no contexto de 49k. Carga do modelo: **5 s**.

**A régua do KV.** Medindo o pico entre 16k e 32k: **62 KiB por token** de
contexto com KV em q8_0 — cerca de 124 KiB em f16 e 31 KiB em q4_0. É o que
manda na conta, não o modelo: o Qwen3-4B ocupa 2,5 GB e o KV de 49k ocupa 1,6 GB
em q4_0 e ocuparia 6 GB em f16.

**Regra prática que sai daí, para os 6 GB desta placa:**

| duração da reunião | contexto | KV |
|---|---|---|
| até ~45 min | 16k–24k | q8_0 |
| até ~1h30 | 32k | q8_0 |
| até ~2h15 | 49k | q4_0 |
| acima disso | blocos (§7), ainda não construído |

### O conteúdo saiu bom; o formato, torto

Ata da reunião de 29 min (update com cliente, Vivo/Faturamento B2B), conferida
contra a transcrição:

- **números inventados: nenhum.** Os 15 números da ata aparecem na transcrição;
- **donos inventados: nenhum.** Os quatro responsáveis são falantes reconhecidos
  da reunião, e o prazo ausente virou `[prazo a definir]`, como a skill manda;
- **decisões**: quatro, todas rastreáveis a trechos com conclusão explícita;
- **small talk** sobre a vida pessoal de uma participante foi excluído, e a
  exclusão foi registrada nas observações — que é literalmente o que a regra pede;
- **formato**: derrapou. `**Responsável: Fulano**` no lugar de
  `Ação — **Responsável** — prazo`.

### A saída constrangida resolve o formato

Mesma reunião, mesmo modelo, com JSON Schema pelo `llama-server`:

- **JSON válido de primeira**, com as sete chaves do esquema;
- 1.337 tokens gerados em **37 s** (44 t/s);
- responsáveis todos reais, prazos ausentes já como `[prazo a definir]`;
- pico de 4.512 MiB.

**Atenção ao caminho.** A gramática **não funciona pelo `llama-cli` em modo
conversa**: ela é aplicada desde o primeiro token e colide com o
`<|im_start|>` do template de chat (`Unexpected empty grammar stack`). Pelo
`llama-server`, aplicada só à resposta do assistente, funciona. Quem escrever o
sidecar precisa saber disto antes de perder uma tarde.

### Duas armadilhas medidas, que não são do modelo

- **o build de CUDA tem que casar com o driver.** O `llama-b10427-bin-win-cuda-13.3`
  falha nesta máquina com *"the provided PTX was compiled with an unsupported
  toolchain"* — driver 595.97 anuncia CUDA 13.2. O build **12.4 funciona**. Vale
  para o instalador da Fase 4: publicar o 12.4, ou detectar;
- **`llama-cli.exe` não abre caminho do WSL.** Argumentos como `/mnt/c/...`
  falham; caminhos relativos com o diretório certo funcionam. O caminho do
  próprio `.exe` pode ser do WSL, porque quem o resolve é o interop.

### O critério E, lado a lado

A mesma reunião de 29 min, escrita pelo Qwen3-4B e por um modelo de fronteira
seguindo a mesma skill. Comparadas fato a fato contra a transcrição:

| | 4B local | fronteira |
|---|---|---|
| seções da estrutura | 7 de 8 | 8 de 8 |
| separou pendências por lado | sim | sim |
| fatos inventados | **nenhum** | nenhum |
| donos inventados | **nenhum** | nenhum |
| itens de ação | **4** | 8 |
| números-chave recuperados | 7 de 14 | 14 de 14 |

**O 4B não erra: ele omite.** Não inventou nada — e deixou de fora metade da
substância, incluindo **o impacto financeiro** (R$ 180 mil/mês, mais de R$ 2
milhões no ano), que é provavelmente a linha mais importante de um update com
cliente. Também perdeu o terceiro citado para quem a apresentação é feita, a
reunião do dia seguinte, o recorte por tipo de produto e o risco de dependência
do TI do cliente.

**Isso muda o desenho, e é o motivo de a comparação vir antes de construir.** O
verificador do §4 foi desenhado contra invenção, e invenção não é o modo de
falha deste modelo. Contra omissão ele não faz nada. Entram duas peças:

1. **Roteiro de fatos, extraído deterministicamente** e injetado no prompt: os
   números citados com o trecho em volta, os compromissos verbais ("te mando
   amanhã", "vou dar uma olhada"), os nomes de terceiros citados e as datas.
   Não é o modelo que procura — é o C#, com expressão regular, e o modelo recebe
   a lista pronta para usar o que for relevante;
2. **Conferência de cobertura**, depois: número citado na transcrição que não
   apareceu na ata entra em "Observações" como *não incorporado*. Não é o modelo
   que julga o que faltou; é uma lista que o humano bate o olho.

Nenhuma das duas exige modelo maior, e as duas são do tipo de coisa que este
projeto já faz bem: regra determinística embaixo, modelo por cima.

### Uma variação que a estrutura resolve

Duas rodadas iguais, mesma reunião e mesma temperatura: numa saiu a seção
"Observações sobre a transcrição", na outra não. Com o Markdown livre, a
presença de uma seção é sorte; com o esquema, o renderizador emite o que existe
e omite o que está vazio — e "vazio" passa a ser um fato, não um esquecimento.

### O que ainda não foi medido

| # | pergunta | por que ainda não |
|---|---|---|
| 1 | Gemma 3 4B contra o Qwen3 4B | o Qwen passou; comparar só paga se ele começar a falhar |
| 2 | `llama-cpp-python` no Python embarcado × binário ao lado | o binário resolveu; a decisão de empacotamento é da Fase 4 |
| 3 | Reunião de 2 h **com esquema JSON** | o caminho de 49k foi medido em Markdown livre |
| 4 | ~~Qualidade contra ata de fronteira~~ | **feito** — ver acima |
| 5 | ~~CUDA 12.4 × 13.x~~ | **decidido em 14/08/2026: fica na 12.4.** Ela funciona em driver novo e velho; a 13.3 falha no driver de hoje. Compatibilidade ganha de um desempenho que ninguém mediu fazer falta — remedir só se houver motivo ([FASE6.md](FASE6.md) §1.5) |

A 4 respondeu: **entrega, com as duas peças contra omissão acima**. Uma ata que
recupera 7 de 14 números não serve sozinha — com o roteiro de fatos, a aposta é
que sirva. Se não servir, o resultado honesto continua sendo reabrir a escolha
de provedor.

---

## 9. O que já existe

Construído em 14/08/2026, no núcleo (`Nucleo/Atas/`), portátil e com testes:

- **`ModelosDeAta`** — os seis tipos embutidos como recurso, a pasta do usuário
  sobrepondo por nome, o recorte das regras comuns do `SKILL.md` e a lista de
  seções extraída do esqueleto de cada referência. O teste do recorte é a rede
  contra alguém reescrever o `SKILL.md` e o app passar a mandar um prompt sem as
  regras que impedem ata errada;
- **`RoteiroDeFatos`** — a rede contra omissão (§8): números com o trecho em
  volta, compromissos verbais com prazo, e a conferência do que a ata não
  incorporou.

Tudo isso foi construído e está em uso. O que ficou para depois, com o gatilho de
cada coisa, está em [FASE6.md](FASE6.md).

---

## 10. O risco, dito em voz alta

**Um 4B não segue 1.700 tokens de instrução como um modelo de fronteira segue.**
Toda a arquitetura acima é uma sequência de compensações para isso: o app
classifica em vez do modelo, a gramática impõe em vez de pedir, o verificador
confere em vez de confiar.

Se, mesmo assim, a ata inventar decisão, o resultado honesto é registrar e
reabrir a escolha de provedor — não entregar uma ata em que não se pode confiar.
Uma ata errada é pior que nenhuma, porque cria memória falsa.
