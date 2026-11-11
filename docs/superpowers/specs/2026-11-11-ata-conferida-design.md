# A ata conferida

Aberto em 11/11/2026. **Estado: desenho, aguardando a aprovação do dono.** Nada
foi construído.

É o passo 1 do [RASTREABILIDADE](../../RASTREABILIDADE.md) — a ata em que dá
para confiar —, e é dele que os outros três dependem: as pendências que
atravessam reuniões (passo 2) só recebem o que saiu daqui conferido.

- **Protótipo:** a tela de conferência, clicável, sobre a reunião de 21/10. O
  dono viu e aprovou em 11/11/2026. **Fica fora do repositório de propósito**
  (`.superpowers/brainstorm/…/content/conferencia-da-ata.html`, pasta ignorada
  pelo git): ele traz falas reais de uma reunião com cliente, e este
  repositório é público. O que ele mostrou está descrito no §6.

---

## 1. De onde se parte

A ata de 21/10/2026 do Pedido Sugerido foi ao cliente, e a cliente a reescreveu:
*"não captou tudo o que falamos, e alguns pontos não estão fazendo sentido"*. A
transcrição tinha tudo o que ela escreveu. A ata errou de quatro jeitos — omitiu
um item, deu uma explicação como decisão, deu um exemplo como fato, e propagou
um termo mal transcrito (a tabela está no RASTREABILIDADE).

Dois fatos medidos orientam o desenho:

- **o modelo local cobre perto da metade.** Nas atas que o dono mandou por
  e-mail no RTM Merchan, a formalização local cobre ~51–54% dos itens (06/11).
  Não é falta de contexto: a reunião cabe inteira, e o modelo de 4 B deixa item
  de fora mesmo assim. Modelo maior não cabe na RTX 2060;
- **o Claude cobre perto de 90%, e ainda erra.** Em 11/11, escrevendo às cegas
  as três últimas reuniões do RTM, ele cobriu 36 dos 39 itens das atas enviadas
  (92%), contra ~17 (44%) das atas do app. Errou um prazo dito antes da pauta, e
  deu como fechado um número que ficou de ser conferido.

A conclusão é que **nenhum dos dois dispensa a conferência**, e que hoje a
conferência é cara: para saber se um item da ata é verdade, a pessoa procura a
fala na transcrição à mão. A formalização já extrai cada item citando as falas
de onde saiu (`AtaDeFormalizacao`), e **joga a citação fora ao salvar** — o
`ata.json` não a guarda.

## 2. O que muda, em uma frase

**Cada item da ata de formalização guarda a fala de onde saiu, a pessoa confere
item por item ouvindo o trecho, e a ata passa a ter estado — rascunho, conferida,
enviada —, seja o rascunho escrito pelo modelo local ou pelo Claude.**

## 3. Objetivos, e o que fica de fora

**Objetivos**

1. Conferir uma ata de formalização em poucos minutos, com a reunião ainda
   fresca. A meta é **cinco minutos**, e o app mede.
2. O Claude como opção para escrever o rascunho, pelo Claude Desktop e o MCP
   que já existe. O modelo local continua sendo o padrão e o que funciona sem
   rede.
3. **Uma conferência só**: a única diferença entre os dois caminhos é quem
   escreve o rascunho.
4. Cada conferência vira medida — o que a pessoa aprovou, corrigiu, tirou e
   acrescentou —, sem trabalho a mais.

**Fora deste desenho**

- **Os outros tipos de ata** (update, por tema, daily…). Só a formalização cita
  falas; os outros continuam como estão. Decisão do dono, 11/11/2026.
- **Melhorar a extração do modelo local** (usar o recap falado como lista,
  segunda passada sobre o que ficou sem citação, completar o vocabulário do
  projeto). São experimentos que se medem contra a régua do §9, depois dela.
  **Os prompts e o esquema da extração local não mudam aqui.**
- **Pendências entre reuniões, briefing, envio por e-mail** — passos 2, 3 e 4.
- **Chave de API e botão "gerar com o Claude" dentro do app.** A rota escolhida
  é o Claude Desktop (dono, 11/11/2026).
- Qualquer mudança no motor de ata, no ASR ou na diarização.

**O que seria fracasso**

- uma ata marcada como conferida com item que ninguém viu;
- o Claude sobrescrever uma ata que a pessoa já conferiu;
- um prazo que ninguém disse sair no `ata.md` de um rascunho — a garantia que a
  formalização tem hoje não pode se perder;
- a conferência levar mais tempo do que escrever a ata à mão.

## 4. O fluxo

1. A reunião termina e o app transcreve, como hoje.
2. O rascunho nasce de um de dois jeitos:
   - **no app**, botão *Gerar ata* com o tipo Formalização — o modelo local;
   - **no Claude Desktop**, a pessoa pede "faça a ata da reunião de hoje" (ou
     usa o pedido pronto `fazer-ata`). O Claude lê a reunião e devolve os itens
     com as falas citadas, como **proposta**.
3. Na aba Ata, a pessoa confere: cada item mostra a fala de onde saiu, com
   *Ouvir*, e ela aprova, corrige ou tira. O que faltou, ela acrescenta.
4. *Concluir conferência* marca a ata como **conferida**.
5. Ela copia e manda, como hoje, e marca como **enviada**.

A ata **não conferida continua podendo ser copiada e exportada** — uma reunião
sem importância pode ficar no rascunho para sempre (dono, 11/11/2026). O que o
rascunho não pode é alimentar as pendências do passo 2.

## 5. O registro da conferência

Um arquivo novo na pasta da gravação, `conferencia.json`, **que passa a ser a
fonte da ata de formalização**. O `ata.md` e o `ata.json` continuam existindo e
são **derivados dele**, reescritos a cada mudança — assim o Copiar, o Exportar,
o `ler_ata` do MCP e a lista de reuniões continuam funcionando sem saber de
nada.

O que ele guarda (os nomes dos campos ficam com quem constrói):

- **da ata:** quem escreveu o rascunho (`local` ou `claude`) e com que modelo,
  quando; o estado (`rascunho`, `conferida`); quando a conferência começou (a
  primeira ação da pessoa) e quando foi concluída; quando foi marcada como
  enviada; a abertura e a próxima reunião, quando houver;
- **de cada item:**
  - o tipo: `resolvido`, `pendente` ou `a_confirmar` (§5.2);
  - o texto — tema e decisão no resolvido; o que falta no pendente;
  - no pendente: **lado** (`nosso` ou `cliente`), responsável e prazo;
  - **as falas citadas**, cada uma com início, fim, falante e o texto;
  - os avisos da conferência automática (§5.1);
  - o estado na revisão: `a_conferir`, `aprovado`, `corrigido`, `retirado`;
  - a origem: `modelo` ou `manual` (acrescentado pela pessoa);
- **a proposta original**, intocada, para a medida do §9.

**A fala citada é guardada pelo tempo e pelo texto, e não pelo número da
linha.** O número da fala (o `L12` da extração) muda quando alguém corrige a
transcrição ou reatribui um falante; o instante no áudio não muda. É o que
mantém o *Ouvir* funcionando depois de uma correção. *(Decisão minha.)*

**A escrita é atômica** (arquivo temporário e troca), como o `Pendencias.cs` já
faz com o `ata.md`.

**Ata de formalização sem `conferencia.json`** — as geradas antes desta versão —
abre como hoje, só leitura do `ata.md`, com o convite para refazer. Não há
migração: sem as citações não há o que conferir.

### 5.1 A conferência automática

É a etapa sem modelo que já existe (`AtaDeFormalizacao.Conferir`), **a mesma
para os dois caminhos**, com uma mudança de comportamento:

| o que ela acha | hoje | passa a ser |
|---|---|---|
| item sem nenhuma fala citada que exista | cai, com observação | **igual**: sem prova não entra |
| pendente que é logística da própria reunião | cai, com observação | **igual** |
| prazo que não aparece perto das falas citadas | **apagado**, com observação | **fica no registro com aviso**, e não entra no `ata.md` enquanto a pessoa não aprovar o item |
| número que não aparece nas falas citadas | observação | aviso no item |
| responsável que não aparece nas falas nem entre os convidados | observação | aviso no item |

O porquê da mudança no prazo: a régua foi escrita para um modelo de 4 B que
inventava "28/10/2026", e apagar era o certo quando ninguém ia olhar. Com a
tela de conferência, apagar obriga a pessoa a redigitar um prazo que foi dito
de um jeito que a régua não reconhece ("dia vinte de novembro"). O aviso leva o
olho dela ao item; e o rascunho copiado sem conferir continua sem prazo que
ninguém confirmou. *(Decisão minha.)*

O que caiu continua indo para o `ata.observacoes.md`, como hoje.

### 5.2 "A confirmar"

É o erro mais caro dos dois modelos: dar como fechado o que alguém só explicou,
exemplificou ou ficou de verificar. Um item `a_confirmar` é o que **parece
decisão e não tem a fala em que o outro lado concorda**.

- Na tela ele fica num grupo à parte, e a pessoa escolhe: é resolvido, é
  pendente ("confirmar X", com quem), ou sai.
- **Nunca aparece sob "Resolvidos em reunião" sem que a pessoa o ponha lá.**
- A ata que sai continua tendo **duas listas, e só duas** (a regra do tipo). No
  `ata.md` de um rascunho, o item a confirmar sai entre os pendentes, como
  "Confirmar: …". *(Decisão minha.)*
- **Só o Claude produz esse tipo por enquanto.** A extração local não muda neste
  desenho, e os itens dela chegam como resolvido ou pendente.

### 5.3 Responsável

Todo pendente tem **lado**. O nome é usado quando se sabe; sem nome, a ata diz o
lado pelo nome da empresa — o cliente do vínculo, ou a casa —, como o tipo já
manda (dono, 10/11/2026: nem sempre se sabe o nome, e a responsabilidade
fica). Pendente sem lado definido não deixa concluir a conferência.

**O app não sabe hoje o nome da casa** — só os domínios dela
(`dominios_da_casa`). Entra um campo opcional em Ajustes › Atas, "Nome da sua
empresa"; vazio, vale o primeiro domínio da casa sem o sufixo, com a inicial
maiúscula. *(Decisão minha.)*

O lado vem de onde já vem hoje (`Organizacoes`, pelo domínio do e-mail da
agenda); a pessoa o troca na conferência. Isso também conserta o caso medido em
05/11, em que a diarização atribuiu ao Paulo uma fala do Marcelo.

## 6. A tela de conferência

Mora na **aba Ata** da reunião, quando o tipo é Formalização e existe
`conferencia.json`. O desenho fino (layout, componentes do AA Design System) é
de quem constrói; o comportamento é este:

- **No topo:** o estado ("Rascunho · escrito pelo Claude · 9 itens · 2 vistos ·
  2 pedem a sua atenção"), *Copiar*, *Exportar*, *Refazer ata* e *Concluir
  conferência*; uma **barra de andamento** com a fração de itens já vistos; e
  três **filtros** — Todos, Pedem atenção (com aviso ou a confirmar), Faltam
  ver —, cada um com a contagem.
- **Três grupos:** Resolvidos, Pendentes, A confirmar.
- **O item a confirmar diz o motivo em uma frase** ("é uma explicação de como o
  cálculo funciona hoje"). Quem a escreve é o Claude, num campo da proposta; sem
  motivo, a tela diz só que não há fala de concordância.
- **Cada item** mostra o texto como vai sair, os avisos, e **a prova**: a fala
  citada, com quem falou e o minuto, e um *Ouvir* que toca o áudio daquele
  ponto (o `ouvir()` do `tocador.js`, que a revisão já usa).
- **Ações do item:** *Aprovar*; *Corrigir* (texto, lado, responsável, prazo, e
  mudar de grupo); *Tirar*. Tirar não apaga: o item fica no registro como
  retirado, e dá para trazer de volta.
- **Adicionar item** em cada grupo, à mão. É por onde entra o que o modelo
  omitiu e o que não está no áudio (em 29/10 um item veio do material
  apresentado, e nenhum modelo o tira da transcrição). Item manual não tem fala
  citada.
- **Concluir conferência:** os itens **com aviso** têm que ter sido aprovados,
  corrigidos ou tirados um a um, e os **a confirmar** têm que ter ganhado um
  destino (resolvido, pendente ou fora) — eles não se aprovam como estão; os
  demais ainda não vistos são aprovados em bloco,
  depois de uma confirmação que diz quantos são. *(Decisão minha: é o que
  mantém a conferência rápida sem deixar passar justamente o item suspeito.)*
- **Depois de conferida:** editar um item continua possível e a ata continua
  conferida — quem editou foi a pessoa. *Refazer ata* sobre uma conferida pede
  confirmação, porque joga a conferência fora; a anterior fica guardada ao lado,
  como o `ata.anterior.md` de hoje.
- **Marcar como enviada**, disponível na ata conferida: grava a data e guarda o
  texto daquele momento como `ata-email.md` na pasta — o arquivo que o plano de
  08/11 já previa como gabarito. **É um gesto à mão só enquanto o envio for à
  mão.** Quando o passo 4 mandar o e-mail de dentro do app, o registro passa a
  ser automático e fiel ao que saiu de fato — quando, para quem e com que texto
  (dono, 11/11/2026). Por isso "enviada" é guardada como um fato com data e
  texto, e não como uma marca solta: o passo 4 preenche o mesmo lugar.
- **A marca de rascunho fica na tela, não no texto.** O que se copia é a ata
  limpa, porque ela vai para o cliente. *(Decisão minha.)*
- A aba mostra um selo enquanto a ata for rascunho.

Como hoje, tudo é montado com nós e nunca com `innerHTML`: o texto vem de um
modelo.

## 7. O caminho local

`GeracaoDeAta.GerarFormalizacaoAsync` passa a escrever o `conferencia.json`
junto com os arquivos de hoje. As três etapas ficam — extrair, conferir,
escrever —, com os mesmos prompts e esquemas.

- As falas citadas de cada item saem das linhas que sobreviveram à conferência
  automática.
- A etapa de escrita junta itens que dizem a mesma coisa; **o item juntado
  guarda as falas de todos os que entraram nele** quando der para saber, e as do
  primeiro quando não der.
- O `ata.md` de um rascunho local tem que sair **igual ao de hoje** para a mesma
  extração, tirando a regra nova do prazo do §5.1 — que, para um prazo que a
  régua reprova, dá o mesmo resultado no arquivo (fica fora).

## 8. O caminho do Claude

O Claude Desktop já abre `PulseMeet.exe --mcp`. O servidor ganha **duas
ferramentas e um pedido pronto**, e deixa de ser só leitura — a decisão "Só lê"
do [INTEGRACAO-CLAUDE](../../INTEGRACAO-CLAUDE.md) §3 é revista aqui, com o
cuidado que ela pedia.

### 8.1 `preparar_ata` (lê)

Devolve, numa chamada, tudo o que escrever a ata pede — **o mesmo que o modelo
local recebe**: título, data, cliente e projeto; as pessoas com o lado de cada
uma; o vocabulário do projeto; as notas de quem gravou; as regras do tipo
(o `formalizacao.md`); e a transcrição **com as falas numeradas**
(`[L12 03:45] Falante: texto`, o `TextoNumerado` da formalização).

Reunião longa passa do teto que o `ler_transcricao` já tem (150 mil
caracteres): o mesmo teto e a mesma continuação valem aqui.

A resposta traz também **uma marca da versão da transcrição** (a data do
arquivo basta), que o `propor_ata` exige de volta. Os números das falas só
valem para a transcrição que foi lida: se alguém a corrigiu entre a leitura e a
proposta, a numeração andou, e a proposta é recusada com o pedido de ler de
novo — em vez de gravar uma prova que aponta a fala errada.

### 8.2 `propor_ata` (escreve)

Recebe a reunião e os itens — resolvidos, pendentes e a confirmar —, cada um
com o texto, os campos do pendente e **os números das falas que o sustentam**;
o item a confirmar leva também o motivo, em uma frase.

O que ela faz:

1. valida a entrada à mão (é `JsonNode`, sem esquema que o cliente garanta);
2. roda a conferência automática do §5.1;
3. grava **um arquivo só**, `proposta-do-claude.json`, na pasta da reunião, com
   as falas já resolvidas em tempo e texto (§5) — a proposta não depende mais
   da numeração depois de gravada;
4. responde em texto o que aconteceu: quantos itens entraram, quais caíram e
   por quê, quais ficaram com aviso — para o Claude corrigir uma citação errada
   e propor de novo. Uma proposta nova substitui a anterior.

**O servidor MCP nunca toca no `ata.md`, no `ata.json` nem no
`conferencia.json`.** Quem transforma a proposta em rascunho é o app:

- reunião **sem ata**: ao abrir a aba Ata, a proposta vira o rascunho;
- reunião **com rascunho** (local ou do Claude): a aba avisa "O Claude propôs
  uma ata para esta reunião" com *Usar a proposta* e *Descartar*, dizendo
  quantos itens tem cada uma;
- reunião **com ata conferida**: o mesmo aviso, e usar a proposta pede a
  confirmação do *Refazer ata*.

Usada ou descartada, a proposta sai da pasta: quando usada, o conteúdo dela
passa a viver no `conferencia.json`, como a proposta original.

É isto que responde ao "um caminho novo que escreve é um caminho novo para
estragar o acervo": o processo que o Claude controla escreve num arquivo que só
ele usa, e **nada do que a pessoa já tem muda sem um gesto dela, no app**. Dois
processos também nunca escrevem o mesmo arquivo.

O app não vigia o disco. A aba Ata procura a proposta **ao abrir e quando a
janela volta a ter foco** — que é o que acontece quando a pessoa sai do Claude e
volta ao PulseMeet. *(Decisão minha.)*

A ferramenta se declara como escrita (`readOnlyHint: false`), não destrutiva, e
o Claude Desktop pede permissão à pessoa para chamá-la.

### 8.3 O pedido pronto `fazer-ata`

Argumento: a reunião (vazio é a aberta no app). O texto manda ler com
`preparar_ata`, propor com `propor_ata`, e **dizer à pessoa para conferir no
PulseMeet** — e não entregar a ata no chat como se estivesse pronta. As regras
que ele carrega são as da extração de hoje mais as que o teste de 11/11 ensinou:

- ler a reunião inteira, **inclusive a conversa antes da pauta** (foi onde o
  prazo da PEC se perdeu);
- quando alguém recapitula em voz alta o que ficou combinado, usar o recap como
  lista de conferência (em 21/10 o recap dos 28:42 era a ata da cliente);
- explicação de como algo funciona hoje não é decisão; exemplo não é fato;
- o que alguém ficou de verificar é pendente ou a confirmar, **nunca resolvido**;
- prazo só quando foi dito, e como foi dito; quando a reunião diz dois, o
  último;
- responsável pelo nome quando a fala o diz, e sempre o lado.

As instruções gerais do servidor passam a dizer que, para escrever a ata, o
caminho é `preparar_ata` e `propor_ata` — para o pedido em linguagem natural
funcionar sem o pedido pronto.

### 8.4 O que mais muda no MCP

- `ler_ata` passa a dizer o estado na primeira linha ("rascunho, não conferida"
  ou "conferida em…"). É o que o passo 2 e a timeline vão usar para saber em que
  confiar;
- a frase das instruções "as atas foram escritas por um modelo local e podem
  errar" passa a distinguir a ata conferida;
- o ⓘ de Ajustes › Geral › Claude passa a dizer que o Claude também pode propor
  uma ata, e que a proposta só vira ata no app.

**O que sai da máquina não muda de natureza**: ler a transcrição pelo Claude já
a manda para a conversa, e isso já é dito. Escrever não manda nada a mais.

## 9. A régua

Duas medidas, e nenhuma delas pede trabalho novo da pessoa.

**A conferência é a medida.** Com a proposta original guardada, cada ata
conferida diz: quantos itens o modelo propôs, quantos foram aprovados sem
mudança, corrigidos, tirados (o que ele inventou ou leu errado) e acrescentados
(o que ele omitiu), e quanto tempo levou. Uma ferramenta em `tools/` lê os
`conferencia.json` do acervo e imprime a tabela, por quem escreveu o rascunho.
É a resposta contínua a "dá para confiar?" e a "valeu pedir ao Claude?".

**O gabarito continua sendo a ata que foi por e-mail.** O `ata-email.md` nasce
do *Marcar como enviada*; quando a versão que valeu foi a do cliente, a pessoa
a cola por cima. As nove referências de `ata-experimento/ref` são atas
enviadas (confirmado pelo dono em 11/11/2026) e entram nas pastas das reuniões
correspondentes como `ata-email.md`.

A ressalva que fica escrita: **ata escrita pelo Claude não mede ata escrita
pelo Claude.** O que mede é a ata enviada e o que a pessoa mexeu.

## 10. O que não pode quebrar

- **O trimming.** No MCP, `(JsonNode)` em todo `Add` e `new JsonArray(a, b)` no
  lugar de `[a, b]`; o `conferencia.json` com contexto de serialização gerado em
  compilação, como o `AtaJson`. A falha compila, passa nos testes e só aparece
  no `publicar.sh` (IL2026).
- **O stdout do MCP é o protocolo.** Nada escreve nele fora da resposta.
- **O id da reunião vem de um modelo e vira caminho.** `propor_ata` passa pelo
  mesmo `Acervo.Achar` que recusa barra, `..` e caminho absoluto.
- **A data do `ata.md`.** O aviso de "ata desatualizada" compara a data dele com
  a da transcrição. **Nenhuma ação da conferência muda essa data** — aprovar,
  corrigir, tirar, acrescentar ou concluir não fazem a ata ter visto uma
  correção da transcrição —, a mesma regra que o `Pendencias.MarcarNoArquivo`
  já segue. Só gerar de novo ou usar uma proposta escreve data nova.
- **O caminho comum das outras atas** (`GerarAsync`, `VerificadorDeAta`,
  `RedatorDeAta`) não muda.
- **A contagem de pendências da lista** (`EstadoDaAta`, que conta `- [ ]`)
  continua como está: a formalização não usa caixinhas, e isso é assunto do
  passo 2.

## 11. Critérios de pronto

1. Gerar uma formalização pelo app numa reunião real deixa `conferencia.json`
   com as falas de cada item, e o `ata.md` igual ao que a versão anterior
   escreveria.
2. Na aba Ata, *Ouvir* de um item toca o áudio no ponto da fala citada.
3. Aprovar, corrigir, tirar, acrescentar e concluir funcionam, e o `ata.md`
   acompanha cada mudança.
4. No Claude Desktop, "faça a ata da reunião de hoje" termina com uma proposta
   no disco, e ela aparece na aba Ata sem reabrir o app.
5. Uma proposta do Claude sobre uma ata conferida não muda nada até a pessoa
   aceitar.
6. A ferramenta de medida imprime a tabela do §9 sobre pelo menos três atas
   conferidas de verdade.
7. **O dono confere três reuniões reais com o rascunho do Claude e três com o
   local, e o tempo de cada uma fica registrado.** A meta é cinco minutos; se o
   local passar muito disso, o achado vai para o RASTREABILIDADE, e não se
   esconde.
8. `dotnet test` e `node --test tools/web/*.test.mjs` passam, e o
   `tools/publicar.sh --so-build` publica sem aviso de trimming.

## 12. O que fica com quem constrói

- Os nomes dos campos e a forma exata do `conferencia.json` e da proposta.
- O layout da tela e os componentes; a lógica testável da tela num módulo
  `*-regras.js`, como as outras.
- O texto final das descrições das ferramentas e do pedido `fazer-ata` (o
  conteúdo é o do §8.3).
- Se a entrega sai numa versão ou em duas. A ordem natural é registro e caminho
  local, depois a tela, depois o MCP — e as duas primeiras já têm valor sem a
  terceira.
- Como a aba descobre que a janela voltou a ter foco.

## 13. Depois deste

- Atualizar o [INTEGRACAO-CLAUDE](../../INTEGRACAO-CLAUDE.md) (§2, §3 e §9) e o
  RASTREABILIDADE quando sair.
- Com a régua do §9 rodando: os experimentos na extração local e o vocabulário
  do projeto a partir das atas conferidas.
- O passo 2, que lê só ata conferida.
