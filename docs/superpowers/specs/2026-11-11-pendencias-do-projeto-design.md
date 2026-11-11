# As pendências do projeto

Aberto em 11/11/2026. **Estado: desenho inicial, escrito a pedido do dono para
registrar a direção antes de implementar.** Diferente do passo 1, este não foi
discutido ponto a ponto: as decisões marcadas *(decisão minha)* são propostas, e
as perguntas do §10 precisam de resposta antes do plano.

É o passo 2 do [RASTREABILIDADE](../../RASTREABILIDADE.md). **Depende do passo
1** ([a ata conferida](2026-11-11-ata-conferida-design.md)): só entra aqui o que
saiu de uma ata conferida.

---

## 1. De onde se parte

*"O que ficou pendente da última vez"* é a pergunta que mais se faz numa série
de reuniões, e o app é o único que sabe respondê-la — é o `ATA-2` do
[BACKLOG](../../BACKLOG.md), em espera desde a v1.

Hoje a pendência não tem vida fora da reunião em que nasceu:

- no `ata.md` dos outros tipos ela é uma caixinha `- [ ]`, marcada pelo número
  da linha (`Pendencias.cs`); a formalização nem caixinha tem;
- a lista de reuniões conta caixinhas abertas por reunião (`EstadoDaAta`), e
  não por projeto;
- para saber o que está aberto num projeto, a pessoa relê as atas, ou pede ao
  Claude a `timeline` — que relê as atas por ela, e herda os erros delas.

Com o passo 1, cada item da ata de formalização passa a ter texto conferido,
lado, responsável, prazo e a fala de onde saiu. É matéria-prima suficiente para
a pendência ganhar identidade.

## 2. O que muda, em uma frase

**Cada pendência e cada decisão de uma ata conferida ganha identidade no
projeto, a ata seguinte diz o que fecha ou muda das anteriores, e o projeto
passa a ter uma página com o que está aberto, com quem, desde quando — e a
história de cada item.**

## 3. Objetivos, e o que fica de fora

**Objetivos**

1. Abrir um projeto e ver **o que está aberto**, separado por lado, com
   responsável, prazo e idade.
2. De cada item, ver **a história**: em que reunião nasceu (e ouvir a fala), em
   quais voltou, quando mudou de dono ou de prazo, quando fechou.
3. **A linha do tempo do projeto**: decisões e entregas por data.
4. Manter isso **sem trabalho à parte**: o registro se atualiza na conferência
   da ata, que a pessoa já faz.

**Fora deste desenho**

- Pendências de atas não conferidas, e dos tipos que não são formalização. As
  caixinhas do `ata.md` continuam como estão.
- Gerência de projeto em geral: cronograma, esforço, dependências, quadro
  kanban. O registro é o que as reuniões disseram, e nada além.
- O briefing (passo 3) e o compartilhamento (passo 4), que **leem** daqui.
- Reconstruir o histórico das reuniões antigas automaticamente (§8).

**O que seria fracasso**

- item no painel que ninguém conferiu;
- o app fechar uma pendência sozinho, porque "pareceu" que a ata nova a
  resolvia;
- a mesma pendência aparecendo três vezes, uma por reunião em que foi cobrada;
- o painel virar mais um lugar para manter à mão.

## 4. O modelo

### 4.1 O item

Dois tipos, vindos da ata conferida: **pendência** e **decisão**.

- **Identidade:** a reunião em que nasceu mais o item dentro dela. É estável
  porque o `conferencia.json` do passo 1 já guarda os itens um a um.
- **Pendência:** o que falta, lado, responsável, prazo, e o estado — `aberta`,
  `concluída`, `cancelada`.
- **Decisão:** tema e texto, e o estado — `vigente` ou `revista` (quando uma
  decisão posterior a substitui; a regra do tipo já manda dizer "o que mudou o
  combinado anterior").
- **Origem:** a reunião e a fala citada, quando houver. É o que deixa *ouvir de
  onde saiu* meses depois.

### 4.2 Os acontecimentos

Um item muda por **acontecimentos datados**, e o estado dele é o resultado de
aplicá-los em ordem:

| acontecimento | de onde vem |
|---|---|
| criado | a ata conferida em que nasceu |
| cobrado de novo | uma ata posterior que o repete sem mudar |
| prazo ou responsável mudou | uma ata posterior, ou a pessoa, na página do projeto |
| concluído | uma ata posterior ("a base foi enviada em 26/10"), ou a pessoa |
| cancelado | idem |
| decisão revista | uma ata posterior |

Cada acontecimento guarda a data, a origem (a reunião, ou "fora de reunião") e,
quando veio de uma reunião, a fala.

### 4.3 Onde mora

**O registro do projeto é derivado, e não um segundo cadastro.** *(Decisão
minha.)*

- Os itens moram onde nasceram: no `conferencia.json` de cada reunião.
- Os acontecimentos que vêm de uma reunião moram no `conferencia.json` dela
  ("esta ata conclui o item X da reunião de 21/10").
- Os acontecimentos **fora de reunião** — a pessoa marca como concluída numa
  terça qualquer — moram num arquivo por projeto, **dentro da pasta das
  gravações**, numa subpasta que a lista de reuniões ignora.
- A página do projeto lê as atas conferidas do projeto em ordem de data, aplica
  os acontecimentos e mostra o resultado.

O porquê: é a regra que o app já segue no `EstadoDaAta` — *lido do arquivo, e
não guardado à parte*, porque um segundo arquivo com os mesmos fatos envelhece
no dia em que a ata é refeita por outro caminho. E tudo fica dentro da pasta das
gravações, que é a que tem backup e sincronização; o `projects.json`, no perfil
do usuário, continua guardando só preferências.

O custo é ler várias atas para abrir um projeto. Medido em 22/10, o servidor MCP
respondeu oito pedidos sobre o acervo inteiro em 3,4 s no Windows; um projeto
tem dezenas de reuniões, e não centenas. Se pesar, entra um cache que se
reconstrói — e nunca vira a fonte.

### 4.4 O prazo

O prazo é guardado **como foi dito** ("quinta", "até 20/11"), porque é o que a
ata formaliza. Para o painel dizer "vencida", ele precisa de data: o app
**propõe** a data a partir do que foi dito e da data da reunião ("quinta" numa
reunião de terça, 21/10, é 23/10), e a pessoa confirma na conferência. Prazo que
não dá para resolver com segurança fica sem data, e a pendência aparece como
"sem data". *(Decisão minha.)* Nunca se inventa prazo — a regra do passo 1
continua valendo.

### 4.5 O responsável

Texto e lado, como no passo 1. Para o painel agrupar por pessoa sem "Marcelo" e
"Marcelo Souza" virarem dois, a conferência oferece **as pessoas do projeto** —
os convidados das reuniões dele, com o lado que o e-mail já diz — e ainda aceita
texto livre.

## 5. Ligar a ata nova ao que já existe

É o coração do passo, e onde ele pode falhar dos dois jeitos — duplicar ou
fechar o que não fechou.

Na conferência de uma ata, quando o projeto já tem itens abertos, cada item novo
pode ser:

- **novo**;
- **a continuação** de um aberto (cobrado de novo, com ou sem mudança de prazo
  ou dono);
- **o fechamento** de um aberto;
- **a revisão** de uma decisão vigente.

**Quem propõe a ligação:**

- no caminho do Claude, o `preparar_ata` passa a devolver também os itens
  abertos do projeto, com os ids, e a proposta diz a que item cada um se liga,
  citando a fala;
- no caminho local, o app sugere por semelhança de palavras, sem modelo.
  *(Decisão minha: não pedir mais nada ao modelo de 4 B antes de medir.)*

**Quem decide é sempre a pessoa**, na tela de conferência: a ligação proposta
aparece no item ("parece fechar: *Enviar a base atualizada do pedido mínimo*, de
21/10"), com *Confirmar*, *É outro item* e *Escolher outro*. **Ligação não
confirmada não vira acontecimento** — e não impede de concluir a conferência: o
item entra como novo.

Além disso, ao concluir a conferência, a tela lista **os abertos do projeto que
a ata não mencionou**, para a pessoa dizer se algum fechou sem ninguém falar. É
opcional, e é onde uma pendência para de se arrastar em silêncio.

## 6. A página do projeto

O desenho fino fica para quando o dono vir um protótipo, como no passo 1. O
comportamento:

- **Abertas**, em dois grupos — do nosso lado e do lado do cliente —, cada uma
  com responsável, prazo (e "vencida há N dias" quando houver data), idade, e em
  quantas reuniões já foi cobrada.
- **Abrir um item** mostra a história dele, com a fala de cada acontecimento e
  *Ouvir*, e o atalho para a reunião.
- **Ações fora de reunião:** concluir, cancelar, mudar prazo ou dono, e uma
  **nota interna** — que nunca vai para o briefing nem para o cliente.
- **Linha do tempo:** decisões e conclusões por data, da mais nova para a mais
  antiga, com a reunião de origem.
- **O que não foi conferido fica visível**: se a última reunião do projeto tem
  ata em rascunho, a página diz "a reunião de 05/11 ainda não foi conferida; o
  painel vai até 29/10". É o que impede de confiar num painel atrasado.

Onde a página mora na navegação é pergunta para o dono (§10).

## 7. O MCP

- Uma ferramenta de leitura, `listar_pendencias` (cliente, projeto, estado),
  que devolve o registro em texto, com a data e a reunião de cada item.
- Os pedidos prontos `timeline` e `preparar-reuniao` passam a partir do
  registro quando o projeto tem atas conferidas, e das atas quando não tem —
  dizendo qual dos dois usaram.
- **O Claude não fecha nem cria pendência direto.** Tudo o que ele propõe passa
  pela proposta de ata e pela conferência, como no passo 1.

## 8. As reuniões antigas

Não há migração automática: o registro só conhece atas conferidas, e as antigas
não têm citação. Para um projeto começar com o que já está aberto, dois
caminhos, e nenhum é obrigatório:

- conferir as últimas reuniões dele (gerando a formalização de novo, pelo Claude
  ou pelo local);
- acrescentar à mão, na página do projeto, o que está aberto hoje — como item
  "fora de reunião", sem fala citada.

## 9. O que não pode quebrar

- **Refazer uma ata conferida que já alimenta o registro** troca os itens dela.
  O app avisa antes, e os acontecimentos de reuniões posteriores que apontavam
  para itens que sumiram aparecem como ligação perdida, para a pessoa religar —
  e não somem em silêncio.
- **Apagar uma reunião** tira os itens dela do registro; o aviso de apagar passa
  a dizer isso quando ela tiver itens abertos.
- **Mudar o cliente ou o projeto de uma reunião** move os itens dela junto.
- O que o passo 1 garante — nada sem conferência, prazo só se foi dito — vale
  aqui sem exceção.
- A contagem de pendências da lista de reuniões (`EstadoDaAta`) continua
  contando caixinhas, para os outros tipos; para a formalização conferida, ela
  passa a contar as abertas do registro que nasceram naquela reunião.

## 10. Perguntas para o dono, antes do plano

1. **Onde mora a página do projeto?** Um destino novo no trilho ("Projetos"),
   ou dentro de Reuniões, ao filtrar por um projeto?
2. **Vale semear os projetos em andamento** (§8), ou o registro começa da
   próxima reunião de cada um?
3. **Pendência interna existe?** Algo que é nosso e o cliente não deve ver. Se
   existe, ela não pode nascer da ata de formalização, que vai inteira ao
   cliente — precisaria de um caminho só da página do projeto.
4. **"Entregue" é só a pendência concluída**, ou uma entrega pode existir sem
   ter sido pendência (algo que se mostrou na reunião sem ter sido prometido)?
5. **Quem pode concluir?** Só a pessoa, no app — ou uma pendência do lado do
   cliente pode ser fechada por ele, de algum jeito (isso é o passo 4)?

## 11. Critérios de pronto

1. Conferir a ata de uma segunda reunião de um projeto propõe ligações com os
   itens da primeira, e nenhuma vale sem confirmação.
2. A página do projeto mostra as abertas por lado, e a história de um item toca
   o áudio da reunião em que ele nasceu.
3. Uma pendência cobrada em três reuniões aparece **uma vez**, com três
   menções.
4. Refazer uma ata conferida avisa, e não perde ligação em silêncio.
5. `listar_pendencias` devolve o mesmo que a página.
6. O dono usa a página de dois projetos reais por duas semanas, e o que ele
   teve que consertar à mão fica registrado no RASTREABILIDADE.
