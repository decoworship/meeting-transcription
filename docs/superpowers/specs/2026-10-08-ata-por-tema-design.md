# A ata por tema

Aberto em 08/10/2026. **Estado: implementado no ramo `ata-por-tema` e validado.**
O critério 3 de §7.2 deu 81% contra a meta de 75%, e o dono aprovou assim; a
leitura das atas tirou a marca da sessão de trabalho e do kickoff, e **só o
update com cliente agrupa** (decisão do dono, 08/10/2026). O resultado está no
`ATA-5` do [BACKLOG](../../BACKLOG.md).

- **Protótipo:** https://claude.ai/artifact/9VLKL5wqtmYN5Gr8GfuNeN, com a ata de
  22/09 (Pedido Sugerido) como ela foi enviada, ao lado da mesma ata agrupada por
  tema. O dono escolheu a camada **"Redator + prompt"**.
- **Plano:** [../plans/2026-10-08-ata-por-tema.md](../plans/2026-10-08-ata-por-tema.md).
- **Depois deste:** os modelos de ata editáveis em Ajustes (parte 2, abaixo, §8).

É formato, e não qualidade: entra no tema 2 do [BACKLOG](../../BACKLOG.md) como
`ATA-5`.

---

## 1. De onde se parte

A ata de update com cliente de 22/09 foi para uma leitora do cliente, que
respondeu: *"ficou bem completa e contempla todos os pontos discutidos. Só achei
que ficou um pouco extensa […] o conteúdo acaba ficando um pouco repetitivo.
Poderia deixar mais compacto, já direcionando, dentro de cada tema, as decisões
e os respectivos responsáveis."*

A contagem confirma. A ata é organizada pelo **tipo** de informação (resumo,
frentes, decisões, pendências, em aberto, riscos), e cada tema reaparece em cada
seção: o pedido mínimo em **7 lugares**, o dashboard em 6, a ruptura da Hiper em
4. Além disso:

- as **observações do verificador** ("confira se algum deveria estar aqui")
  foram junto para a cliente;
- **"[prazo a definir]"** aparece sete vezes;
- o dono está duas vezes na mesma linha ("Andre Yuri investigar… — **Andre
  Yuri**");
- as `notas.md` da reunião já estavam no formato pedido (tema e área por linha),
  e a ata perdeu as áreas.

O protótipo mediu as duas camadas sobre o mesmo `ata.json`: **só reagrupar**
leva de 943 para 848 palavras e deixa a repetição lado a lado; **reagrupar e
pedir ao modelo texto sem repetição** leva a 572 (−39%).

## 2. O que muda, em uma frase

**Cada decisão, ação, ponto em aberto e risco diz em qual seção do corpo ele
mora, e o redator o escreve lá dentro.** O que não pertence a nenhuma seção vai
para a lista geral do fim, como hoje.

## 3. A chave: formato de hoje × por tema

- `app.json` ganha `formato_da_ata`: `"classico"` ou `"por-tema"`. **Padrão
  `"por-tema"`**; chave ausente cai no padrão; valor desconhecido cai em
  `"classico"`.
- **`"classico"` é o app de hoje, byte a byte**: mesmo prompt, mesmo esquema
  efetivo, mesmo `ata.md` (observações incluídas). Um teste de referência
  garante isso.
- A chave vale para a **próxima ata gerada**. Ao gerar de novo, o `ata.md` que
  existia é guardado como `ata.anterior.md`, para dar para comparar os dois
  formatos na mesma reunião.
- Mora numa aba nova de Ajustes, **Atas**, entre Transcrição e Clientes:
  *"Formato da ata: De hoje · Por tema"*, com ⓘ. A aba recebe o editor de
  modelos na parte 2.

## 4. Quem agrupa: a marca no arquivo do tipo

- Um tipo agrupa por seção quando o Markdown dele traz a linha
  `<!-- ata: itens-por-secao -->`. Recebem a marca **`cliente-update.md`,
  `trabalho.md` e `kickoff.md`**. Sprint, daily e resultados, não: são
  organizadas por pessoa ou por pauta, e continuam como hoje.
- A marca é texto do arquivo do tipo, e não uma lista no código, porque o tipo é
  personalizável (ATA.md §1): um tipo do usuário liga o agrupamento escrevendo a
  linha, e o editor da parte 2 a mostra como um interruptor.
- `TextoParaPrompt()` tira a marca antes de mandar ao modelo.

| | formato de hoje | por tema, tipo **sem** a marca | por tema, tipo **com** a marca |
|---|---|---|---|
| prompt | o de hoje | o de hoje | + bloco "por tema" (§6) |
| itens dentro da seção | não | não | **sim** |
| "Quem ficou com o quê" | não | não | sim, quando há ação dentro de seção |
| sem "[prazo a definir]" e sem dono repetido | não | **sim** | **sim** |
| observações fora do `ata.md` | não | **sim** | **sim** |

## 5. Dados e redator

### 5.1 O esquema (`AtaGerada.cs`)

- `decisoes`, `pontos_em_aberto` e `riscos` deixam de ser `string[]` e passam a
  `ItemDaAta[]`, com `{ texto, secao? }`. `acoes[]` ganha `secao?`.
- `secoes[]` ganha dois opcionais: `grupo?` (o "Pedido Sugerido: próximos
  passos" do protótipo) e `responsavel?` (a área ou pessoa dona do tema).
- **Ata antiga continua abrindo:** um conversor JSON aceita o item como
  string (vira `{ texto }`) ou como objeto. `ParaJson` escreve sempre objeto.
- O JSON Schema que prende o modelo é montado por formato: **só com a chave em
  por-tema e um tipo com a marca** ele leva os campos novos. Em qualquer outro
  caso é o esquema de hoje, com as três listas em `string[]`. É isso que mantém
  o clássico idêntico, porque o esquema é parte do prompt.
- O verificador (`VerificadorDeAta`), a dobra (`SecaoDobrada`) e o eco de
  decisão passam a ler `.Texto`. O que retiram, retiram igual; o que a dobra
  recupera de uma seção canônica entra sem `secao`.

### 5.2 O redator (`RedatorDeAta.cs`)

Com a marca e a chave em "por tema":

- Cada seção do corpo sai com seus itens dentro, em ordem fixa, separados por
  linha em branco: **Decidido:**, **Atenção:**, as ações (`- [ ] … — **dono**`)
  e **Em aberto:**. Os rótulos são negrito no começo do parágrafo, o que o
  `renderizar()` do app e o `DocxDaAta` já entendem.
- O casamento item → seção usa o `Parecido` que o redator já tem (normalizado,
  por prefixo). **Item que não casa vai para a lista geral do fim**, sob os
  títulos de hoje: fora do lugar, mas nunca perdido.
- Seções com o mesmo `grupo` saem sob um `## grupo`, com as seções em `###`, na
  ordem em que o modelo as devolveu.
- O `responsavel` da seção sai no título: `### Pedido mínimo · Comercial`.
- Quando ao menos uma ação está dentro de uma seção, as "Pendências: do nosso
  lado / do lado do cliente" do fim viram **"Quem ficou com o quê"**: uma linha
  por lado, com cada pessoa e os títulos das seções onde ela tem ação (`(2)`
  quando são duas). Ação sem seção continua em "Pendências" e entra no índice
  como "Outras pendências" (e sai sob `## Outras pendências` quando há ações
  aninhadas). É o que preserva a regra da skill, *"separar
  pendências por lado é a razão de existir desta ata"*, sem repetir o texto.
- A seção do esqueleto que vira container de subtítulos (o "Status por frente"
  com `###` dentro, que o modelo escreveu em 22/09) não é desmontada pelo
  redator. Evitar isso é trabalho do prompt (§6); se acontecer, a ata sai como
  hoje e o critério 2 da validação (§7.2) pega.

No formato por tema, com ou sem a marca:

- prazo vazio ou `[prazo a definir]` não é escrito;
- o nome do dono sai do começo do texto da ação quando o primeiro nome bate com
  o `responsavel` ("Andre Yuri investigar…" → "Investigar…");
- as observações saem do `ata.md` e vão para **`ata.observacoes.md`**, na mesma
  pasta. O `ata.json` continua com elas.

### 5.3 Onde as observações aparecem

- A operação `ata` da ponte devolve também `observacoes` (o conteúdo do
  `ata.observacoes.md`, se existir).
- A aba Ata da reunião mostra um bloco recolhido no pé, **"Observações sobre a
  transcrição"**, com o mesmo `renderizar()`.
- Exportar (`.md` e `.docx`) e Copiar levam só o `ata.md`, então nunca mais
  levam as observações. Uma ata antiga, gerada no clássico, continua com elas
  dentro; não se reescreve o que já está em disco.

## 6. O prompt no formato por tema

`PromptDeAta.Montar` recebe o formato. No clássico, o texto é o de hoje. No
por-tema **e** com a marca, o `ContratoDoJson` ganha um bloco depois de "O que
você escreve, e o que o app escreve":

- **um tema por seção** em `secoes`. Nunca uma seção com subtítulos `###`
  dentro: cada frente, assunto ou módulo é uma entrada própria;
- todo item de `decisoes`, `acoes`, `pontos_em_aberto` e `riscos` preenche
  `secao` com o **título exato** da seção a que pertence, ou deixa vazio se não
  pertence a nenhuma;
- o `texto` da seção dá o **contexto e não repete** a decisão, a ação nem o
  ponto em aberto que vão nos campos;
- a ação vem **curta**: o que fazer, sem o "para avaliar a aplicação de…" que o
  tema já diz;
- `grupo` só quando várias seções são partes de um mesmo assunto maior;
- `responsavel` da seção **só quando a área ou pessoa foi dita ou está nas
  notas**. Nunca deduzido.

As descrições do esquema também instruem (é assim que ele já funciona): o
`resumo` muda de *"3 a 5 linhas"* para **"no máximo três frases; os temas
detalham"** no por-tema com a marca.

O texto dos arquivos dos tipos não muda, só ganha a marca. O "Pendências por
lado" do esqueleto do update já é retirado antes de chegar ao modelo
(`DoRedator`), e continua.

## 7. Testes e validação

### 7.1 Unidade (`app-net/Tests`)

- **O clássico não muda:** antes de mexer, a saída de hoje do redator e do
  prompt para fixtures dos três tipos marcados e da daily vira referência em
  disco; o teste exige igualdade byte a byte no formato clássico.
- **Ata antiga abre:** `ata.json` com as listas em `string[]` carrega, e
  `ParaJson` regrava como objeto.
- **Por tema:** itens na seção certa e na ordem fixa; `grupo` vira `##` com
  `###`; `responsavel` no título; item sem par vai para a lista geral; **nenhum
  item some** (conta os itens do JSON e os do Markdown); "Quem ficou com o quê"
  por lado, com "Outras pendências"; sem prazo vazio; sem dono repetido.
- **A marca manda:** daily e sprint ignoram `secao` com a chave em por-tema, mas
  levam as melhorias de formato; a marca não chega ao modelo.
- **Observações** vão para `ata.observacoes.md`, e não para o `ata.md` nem para
  o `.docx`.
- **Prompt e esquema:** o bloco novo só entra com por-tema **e** marca; o
  esquema clássico continua com `string[]`.
- **Gerar de novo** guarda o `ata.md` anterior como `ata.anterior.md`.
- Verificador, dobra, eco de decisão e `DonoPelaFala` continuam passando.
- **Nenhum dado de cliente entra no repositório.** As fixtures são uma reunião
  inventada com a forma da de 22/09.

### 7.2 Com o modelo de verdade

`Sidecar.exe --ata <pasta> --tipo <id>` ganha `--formato classico|por-tema`
(sem ele, vale o do `app.json`). Ele já passa pelo `GeracaoDeAta.GerarAsync`, o
mesmo caminho do app, e escreve na pasta que recebe: a validação roda sobre
cópias das reuniões, sem os `.wav`, nunca sobre o acervo.
**Só roda com a placa livre e com o aviso do dono**: a RTX 2060 também roda a
legenda ao vivo das reuniões dele. Conferir `nvidia-smi` antes.

Sete reuniões do acervo, nos dois formatos: a de 22/09 (comparada com a camada
"Redator + prompt" do protótipo), mais dois updates, duas sessões de trabalho, o
kickoff (o acervo só tem um) e uma daily.

**Critérios para o padrão ficar em "por tema":**

1. pelo menos **80% dos itens** caem numa seção, somando as seis reuniões
   marcadas;
2. **nenhuma** seção com subtítulos `###` dentro;
3. a ata por tema sai **pelo menos 25% menor** (em palavras) que a clássica da
   mesma reunião, na mediana;
4. a daily sai com a mesma estrutura de seções da clássica.

**Se algum falhar**, o padrão volta para `"classico"`, a chave fica, e as atas
vão para o dono lado a lado antes de qualquer mudança no prompt. O resultado
vira uma página de comparação como a do protótipo, com o que o modelo escreveu.

## 8. Fora deste trabalho

- **Modelos de ata em Ajustes (parte 2).** O núcleo já sabe copiar e restaurar
  (`ModelosDeAta.Customizar`, `VoltarAoOriginal`) e a ponte já tem as duas
  operações, sem tela. Falta a lista, o editor do Markdown, "novo tipo" e a
  marca como interruptor. Fica para depois, de propósito: **um modelo
  personalizado congela a versão de quando foi copiado**, e personalizar o
  update antes deste trabalho o deixaria sem o formato por tema.
- **Gerar nos dois formatos de uma vez.** A chave e o `ata.anterior.md` bastam
  para comparar.
- **Versão, release e push.** A 0.10.0 ainda não saiu; o dono decide quando.
- **Qualidade do conteúdo** (o que o modelo omite ou entende errado) continua
  fora do backlog, por decisão de 27/08/2026.
