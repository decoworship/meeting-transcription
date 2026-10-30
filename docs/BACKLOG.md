# Backlog — features e bugs, por tema

Aberto em 27/08/2026, ao fechar a Fase 6.

**Por que deixou de ser fase.** As fases 0 a 5 tinham um objetivo cada, e por
isso cabiam numa carta: gravar, transcrever, empacotar, instalar, pintar. A
Fase 6 não tinha objetivo — era "tudo o que ficou" —, e uma carta sem objetivo
cresce até não se conseguir mais responder à pergunta que importa, que é *o que
faço agora*. Ela chegou a 1167 linhas e onze subitens espalhados por quatro
seções, com o mesmo assunto aparecendo em três lugares.

**O que este documento é.** A lista do que está vivo, agrupada pelo que a
pessoa está tentando fazer, e não pela fase em que o item nasceu. Cada item tem
um id estável, um tipo e um estado. A ordem dentro do tema é a do incômodo.

**A régua que sobrevive das fases.** Todo item diz se **já dói** ou se **espera
gatilho**. Foi a única defesa que a Fase 6 tinha contra virar lista de desejos,
e ela funcionou — executar item sem gatilho é criar código que ninguém sabe por
que existe. Item sem gatilho fica escrito e não é feito.

**O que este documento não é.** Ele não guarda história. O que foi feito, por
quê, e o que foi deprecado está nas cartas de fase, que continuam sendo o
registro — [FASE6.md](FASE6.md) abre com a disposição de cada seção dela.

---

## Como se lê

| campo | valores |
|---|---|
| **tipo** | `feature` · `bug` · `débito` |
| **estado** | `aberto` — dói hoje · `espera` — escrito, sem gatilho · `bloqueado` — depende de alguém de fora |

O **tema 1 é a prioridade corrente**, por decisão do dono do produto em
27/08/2026.

---

## 1. Interface e uso — **prioridade**

O tema tem uma dívida de origem que vale dizer em voz alta: **os itens 2 a 9
abaixo são sobras de fase, não um plano de UI/UX.** Cada um nasceu de um
tropeço isolado. Nenhum nasceu de alguém percorrer o app do começo ao fim
perguntando onde ele custa tempo. É o que o UI-1 existe para consertar, e é por
isso que ele vem primeiro.

### UI-1 · Uma passada de percurso, ponta a ponta — `feature` · feito pelo redesenho da 0.8.0

Percorrer os quatro destinos com uma reunião real na mão — gravar, transcrever,
revisar, gerar ata, exportar — e anotar onde o app faz esperar, faz procurar, ou
não diz o que fez. A saída do item são **itens novos neste tema**, com o passo
em que doeu; não é um redesenho.

**Por que agora:** a Fase 5 pintou o app e mediu contraste, mas nunca mediu
percurso ([FASE5-HANDOFF.md](FASE5-HANDOFF.md) §2). O acervo passou de 44
gravações, e o uso deixou de ser "uma reunião por vez" — que é o regime em que
todas as telas foram desenhadas.

**Coberto pelo redesenho de UI/UX** (planos 1 a 6 em `superpowers/plans/`,
lançado na 0.8.0): cada tela foi repassada com o acervo real. Falta o plano 4b.
Um percurso novo vale depois de a 0.8 rodar na máquina de outra pessoa.

### UI-2 · A tela de vozes — `feature` · feito em 25/09/2026

Fila de revisão, play por amostra, ações por amostra (remover, mover, tirar da
quarentena) e indicador de saúde por perfil. O desenho está escrito em
[VOZES.md](VOZES.md) §6, e o modelo de dados já é o certo — falta a tela.

**Feito em 25/09/2026** no plano 6 do redesenho
([plano](superpowers/plans/2026-09-25-ui-06-vozes.md)): Ajustes › Vozes em
mestre-detalhe, com "Para revisar" (play, semelhança, "É X" / "É outra pessoa"
/ "Descartar"), a saúde por perfil ("boa" com quatro amostras em uso, "pouca
voz" abaixo), "Fora de uso" dobrado e a sugestão de juntar do `VOZ-1`. O "Não
são" da sugestão **não é guardado** — vale até fechar o app; guardar pede um
campo novo no `vozes.json`. A "confusão entre perfis" do VOZES.md §4b não
entrou: o núcleo não a calcula.

**Por que subiu de prioridade sozinha:** a decisão de 20/08/2026 pôs a geração 1
dos vetores fora de circulação. Hoje a biblioteca tem **três estados** para
explicar — quarentena, modelo antigo e geração antiga — e **só o primeiro pede
ação**. A tela mostra 48 amostras apagadas com um motivo em texto, e não há por
onde reagir a nenhuma delas.

### UI-3 · Busca e filtro nas listas — `feature` · feito em 24/09/2026

Reuniões e Atas desenham **um cartão por gravação, sem corte, sem busca e sem
filtro** ([app.js:73](../app-net/App/web/app.js#L73),
[atas.js:52](../app-net/App/web/atas.js#L52)). A única busca do app está na tela
de revisão, dentro de uma transcrição
([revisao.js:117](../app-net/App/web/revisao.js#L117)).

**Já dói:** são 44 gravações no acervo, e o gatilho escrito era "umas trinta".
Achar a reunião de duas semanas atrás é rolar. Filtrar por cliente é o corte
óbvio — cliente e projeto já estão no cartão, justamente porque é por eles que
se procura.

**Feito**, pelo plano
[2026-09-23-ui-01-reunioes-lista.md](superpowers/plans/2026-09-23-ui-01-reunioes-lista.md),
e conferido pelo dono no acervo real em 24/09/2026: busca sem acento em título,
cliente, projeto, convidados, data e estado; filtros de cliente, data (atalhos,
um dia ou uma semana, de segunda a domingo) e estado; grupos por dia; o estado
da ata na linha e o próximo passo num painel. **Ficou de fora, de propósito:** a
busca no **conteúdo** das transcrições (pede op nova no núcleo), a vista de
semana com calendário, e a lista de Atas, que continua como está até o plano 2
trazer a ata para dentro da reunião.

### UI-4 · O tempo marcado na nota não leva ao áudio — `feature` · `feito`

`[00:12:34]` numa nota é texto. Na revisão, cada trecho já toca o áudio a partir
dele; a nota podia fazer o mesmo, e o dado já está lá.

**Gatilho:** usar "marcar momento" e querer ouvir aquele trecho.

**Feito em 25/09/2026** (plano 2b): na aba Notas da reunião, cada linha com
`[hh:mm:ss]` vira um botão que toca dali, pelo tocador fixo do pé.

### UI-5 · Atalhos de teclado — `feature` · `espera`

Há **um** no app inteiro: `Escape` fecha as gavetas
([app.js:796](../app-net/App/web/app.js#L796)). O que tem custo de tempo medido
em segundos é começar e parar a gravação — e para isso hoje é preciso achar a
janela ou o menu da bandeja.

**Gatilho:** perder o começo de uma reunião procurando o botão. Cuidado: atalho
global do Windows é outra coisa, e mais cara, do que atalho de janela.

### UI-6 · Nada é anunciado — `bug` · feito

**Não existe uma única região `aria-live` no app** — conferido em 27/08/2026 nos
`.js`, no `.css` e no `index.html`. Progresso da transcrição, erro da ponte e
fim de etapa são escritos no DOM em silêncio. O `aria-busy` das telas existe e
está certo, mas ele diz *"estou carregando"*, não *"terminou"* nem *"falhou"*.

Uma região só, no `index.html`, com o texto que a barra de progresso já compõe,
fecha a maior parte disto.

**Feito:** o `#anuncio` (`role="status"`, `aria-live="polite"`) mora no
`index.html`, e o `anunciar()` do `pecas.js` diz a troca de tela e o começo e o
fim da gravação.

### UI-7 · A moldura do Windows não repinta ao trocar de tema — `bug` · `espera`

Escolher outro tema vira a página na hora; a barra de título só acompanha na
próxima abertura. Repintar exige refazer a chamada do DWM a partir da ponte.
Origem: [FASE5-HANDOFF.md](FASE5-HANDOFF.md) §6.1 — a fase decidiu que não valia
para um caso que acontece uma vez por instalação.

### UI-8 · O `.aa-pagina` é largo demais para tela de app — `bug` · `espera`

Os 64px do design system são de página de documento. A revisão já precisou de
`padding-top` próprio; provavelmente vale para as outras telas. A correção certa
é no design system, não aqui. Origem: [FASE5-HANDOFF.md](FASE5-HANDOFF.md) §6.3.

### UI-9 · Os estados de gravação em curso nunca foram conferidos no escuro — `débito` · `aberto`

Medidores de nível, aviso de mute prolongado e barra de progresso não entraram
na varredura de contraste da Fase 5: todos exigem o app gravando ou
transcrevendo, e o destino de teste não tem os motores. Nenhum usa cor fora de
token, mas a conferência é de olho e nunca foi feita.
Origem: [FASE5-HANDOFF.md](FASE5-HANDOFF.md) §6.2.

### UI-10 · Renomear projeto não religa as reuniões — `bug` · `espera`

Renomear um cliente ou projeto em Ajustes leva o vocabulário e as
preferências, mas cada reunião guarda o vínculo com o nome antigo
(`DadosDaReuniao`). Elas somem das contagens do projeto novo, do "Ver as
reuniões deste projeto", e a correção da legenda passa a não achar
vocabulário para elas. Hoje a tela só avisa, no pedido do nome novo.
Gatilho: a primeira vez que alguém renomear e estranhar a contagem; aí,
religar as reuniões do nome antigo na mesma operação, com a lista delas na
confirmação.
Origem: revisão final do plano 4a (`superpowers/plans/2026-09-25-ui-04a-clientes.md`).

---

### UI-11 · Imagens nas notas: print da tela e imagem colada — `feature` · feito em 21/10/2026

Pedido de uso real: guardar o que estava na tela durante a reunião, marcado no
tempo dela como o "Marcar momento", e poder pôr nas notas um print tirado por
fora.

**Feito em 21/10/2026**, sem instalar:

- **Capturar tela** — botão na faixa do Gravador e o atalho global
  **Ctrl+Alt+P**, este só enquanto grava (`JanelaDeMensagens`). O print é do
  monitor da janela que está na frente; com o próprio PulseMeet na frente, ele
  some do print por um instante (`WDA_EXCLUDEFROMCAPTURE`), e sai a reunião
  que está atrás. GDI puro (`App/Nativo/CapturaDeTela.cs`) e PNG escrito à mão
  (`Nucleo/Png.cs`) — nem WinRT, nem `System.Drawing`.
- **Colar, arrastar ou "Inserir imagem"** nas notas, na reunião e no Gravador
  (op `salvar-imagem`). Cobre o recorte do Win+Shift+S.
- Tudo vai para `capturas/` na pasta da gravação, e para as notas como
  `[00:12:34] ![captura de tela](capturas/tela-00-12-34.png)`. As notas viram
  miniaturas que ampliam, e o momento com imagem mostra a miniatura e toca dali.
- **O núcleo escreve a linha no `notas.md` antes de avisar a tela** (evento
  `captura`): o print sobrevive com a janela fechada, e o campo aberto põe a
  mesma linha nele para o próximo salvamento não apagá-la.
- O prompt da ata, o vocabulário sugerido e o começo das notas na lista
  recebem o texto **sem** as imagens (`Notas.SemImagens`): o modelo de ata não
  as vê, e um `[00:12:34]` sozinho seria um momento sem nada dito.

**A conferir na máquina** antes de dar como fechado: o print sair do monitor
certo com dois monitores e escala de 125%/150%; o app sumir do próprio print
(com WebView2 dentro); e o Ctrl+Alt+P não colidir com nada no teclado do
usuário. **Não captura** a reunião do Teams com "impedir captura de tela" —
sai preta, de propósito.

### UI-12 · Os nomes dos participantes pelo print — `feature` · feito em 21/10/2026, sem instalar

Ideia do dono, 21/10/2026: um print da lista de participantes do Teams/Meet
guarda quem estava presente e já oferece os nomes para batizar os falantes —
hoje essas sugestões vêm só dos convidados da agenda (`ConvidadosDaAgenda`), que
não diz quem entrou (ver `Presenca`).

Depende do UI-11 (o print já existe e já está na pasta) e de **OCR**.

**A rota está decidida (21/10/2026): motor de OCR separado, no sidecar Python**,
e não o `Windows.Media.Ocr` — pensando em levar o app para macOS e Linux, onde
o sidecar vai junto e a API WinRT teria de ser refeita. Candidato: **RapidOCR**
(modelos PP-OCRv5 em ONNX, com reconhecedor latino que cobre o português). O
`onnxruntime` já está no Python embarcado; o que entra de novo é o
`opencv-python-headless`, `shapely`, `pyclipper` e `pillow` — medir os MB antes.
Roda na CPU, para não disputar a GPU com a legenda ao vivo.

**Medido em 21/10/2026** com oito prints reais do dono — quatro do Teams, quatro
de apresentações. O **PP-OCRv6 small** leu 34 de 34 nomes; o PP-OCRv5 latino,
33, partindo um nome em dois e grudando o ícone de sinal em outro. ~2 s por
print na CPU, 31 MB de modelos. O que entra no Python embarcado com o RapidOCR
(OpenCV headless, shapely, pyclipper, pillow) passa de 100 MB — **medir no
Windows** quando o `empacotar_motores.sh` rodar.

**Como ficou:**

- Na imagem ampliada das notas, **"Ler nomes desta imagem"**. O núcleo sobe o
  `motores/ocr/` (não fica quente), e o `Nucleo/NomesDoPrint.cs` separa os
  nomes: tira a barra do Teams/Meet em inglês e português, o ícone grudado
  ("Il Fulano"), o sufixo da empresa quando ele se repete ou vem com espaço
  ("Fulano - Acme"), normaliza nome em maiúsculas e usuário com ponto, e casa
  com os convidados da agenda.
- **Nada é gravado sem a pessoa confirmar**, numa caixa com uma caixinha por
  nome. Nos prints do Teams elas vêm marcadas; num slide (a tabela de
  vendedores do print 5 dá 16 "nomes") vêm desmarcadas, com a frase dizendo
  por quê — `PareceReuniao`, que acertou os oito prints.
- Os confirmados vão para `participantes.json` na pasta da gravação, e viram
  as **sugestões do campo Nome** na gaveta de falantes da revisão, antes dos
  convidados da agenda.

**Para funcionar na máquina** falta empacotar: `empacotar_motores.sh` (agora
instala o `rapidocr`, com régua) e `empacotar_modelos_de_ocr.sh` (os dois ONNX,
conferidos por SHA256, e a atribuição Apache-2.0). O `montar_instalador.sh` leva
`ocr/modelos` no pacote de motores — **o que muda a impressão dos motores**, e o
próximo update volta a ter o download pesado.

**Fica para depois:** os presentes pelo print na ata (`Presenca` hoje só cruza
agenda e falas) — pergunta feita ao dono e ainda sem resposta.

### UI-13 · O Claude lê as reuniões — `feature` · feito em 22/10/2026, sai na 0.11.1

Pedido do dono, 22/10/2026: usar a ata ou a transcrição numa tarefa **sem
copiar à mão**, tudo local, inclusive tarefas sobre várias reuniões (a linha do
tempo de um cliente foi o exemplo).

**Como ficou:** `PulseMeet.exe --mcp` é um servidor MCP que o Claude Desktop
abre e com quem conversa por stdio. Seis ferramentas só de leitura (listar
clientes e reuniões, ler ata e transcrição, buscar, a reunião aberta), a ata e a
transcrição de cada reunião como recursos no **+** do Claude, e três prompts
(`timeline`, `follow-up`, `preparar-reuniao`). Ajustes › Geral › **Claude**
põe a entrada no `claude_desktop_config.json`, mexendo numa chave só. Tudo em
[INTEGRACAO-CLAUDE.md](INTEGRACAO-CLAUDE.md).

**Medido** com o binário publicado, no Windows, sobre 187 gravações: oito
pedidos em 3,4 s, contando uma busca no acervo inteiro.

**Instalado e conectado** na máquina do dono em 22/10/2026, e sai na 0.11.1.
Os `PulseMeet.exe --mcp` que o Claude Desktop deixa abertos **travam o
`publicar.sh`** como se o app estivesse aberto: a linha de comando diz qual é
qual, e encerrá-los é seguro — só leem —, mas o Claude precisa ser reaberto.

**Fica para depois**, sem gatilho: escrever de volta (um dossiê do cliente que
o app mostra), filtrar por pessoa, paginação dos recursos se o acervo crescer, e
a extensão `.mcpb` no lugar do botão.

## 2. Ata: formato e saída

**Qualidade da ata não está aqui** — omissão, cobertura e escolha de modelo
saíram deste backlog em 27/08/2026 e são tratadas fora dele. O que fica é o que
a ata *é* e por onde ela *sai*.

### ATA-1 · Exportar em DOCX — `feature` · feito em 29/09/2026 (0.8.1)

O botão Exportar copia o `.md`
([atas.js:196](../app-net/App/web/atas.js#L196)). A transcrição já sai em
TXT/SRT/VTT/DOCX, o [`Exportacao`](../app-net/Nucleo/Exportacao.cs) já escreve
DOCX à mão, e a ata já é estruturada — o caminho é curto.

**Já dói pela assimetria:** a ata é justamente o que sai para o cliente, e é a
única das duas saídas que não tem formato de escritório.

**Feito na 0.8.1:** o Exportar da ata escreve o `.docx` ao lado do `.md` na
pasta das atas (`Exportacao.DocxDaAta`), com títulos, negrito, listas, caixas
`- [ ]` como ☐ e listas numeradas. Uma linha do `ata.md` é um parágrafo, porque
ali linhas seguidas são campos distintos.

### ATA-2 · A ata anterior como contexto — `feature` · `espera`

*"O que ficou pendente da última vez"* é a pergunta que mais se faz numa série
de reuniões, e o app é o único que sabe respondê-la: ele tem as atas anteriores
do mesmo cliente e projeto. Estava previsto como feature 9 do motor e não entrou
na v1.

**Gatilho:** a segunda ou terceira reunião da mesma série.

### ATA-3 · As seções estruturadas viram prosa — `feature` · `espera`

O esquema é universal, e o "Por pessoa" da sprint e da daily cai em
`secoes[].texto` como parágrafo em vez de virar lista. Foi escolha consciente
([ATA.md](ATA.md) §3) — é o que permite customizar um tipo escrevendo Markdown,
sem escrever JSON Schema. Quando incomodar, `secoes[]` ganha um campo opcional
de itens, e só os tipos que precisarem o usam.

**Gatilho:** usar ata de sprint ou daily de verdade e sentir falta.

### ATA-4 · Reunião acima de ~2h15 não cabe numa passada — `bug` · `espera`

Hoje 2 h cabem. O caminho é map-reduce, com o custo registrado em
[ATA.md](ATA.md) §7: perde-se a visão do todo. **Não construir antes de
precisar** — um caminho de blocos existindo é um caminho de blocos usado por
engano.

**Gatilho:** aparecer uma reunião que não cabe.

### ATA-5 · A ata por tema — `feature` · feito em 08/10/2026, só no update

A ata de update de 22/09/2026 voltou do cliente com "ficou extensa e
repetitiva": organizada pelo tipo de informação, ela repetia cada tema em até
sete lugares. Agora cada decisão, ação, ponto em aberto e risco mora dentro do
tema, e as observações do verificador ficam fora do arquivo exportado. A chave
`formato_da_ata` em Ajustes › Atas volta ao formato de antes, byte a byte.

Desenho e critérios: [spec](superpowers/specs/2026-10-08-ata-por-tema-design.md).

**Validado em 08/10/2026; o padrão é "por tema", e só o update agrupa.** Sete reuniões nos
dois formatos, com o `gemma-4-e4b` (`tools/validar_ata_por_tema.py`): 92% dos
itens caíram num tema, nenhuma seção com subtítulos, a daily não mudou — mas a
ata por tema saiu só **19% menor na mediana**, e o critério era 25%. Lendo as
atas, o veredito é por tipo:

- **update com cliente:** organizado como a cliente pediu; dois defeitos do
  modelo — `responsavel` do tema com "nosso"/"cliente", e grupo com o mesmo
  título do tema;
- **kickoff:** a estrutura ficou, mas cada seção ganhou uma pessoa como dona,
  deduzida;
- **sessão de trabalho:** **pior** — perde "Por quê", "Alternativas
  descartadas" e "Implicações", e repete o mesmo tema por pessoa.

O dono aprovou o critério 3 com os 81% e escolheu **agrupar só o update**: a
sessão de trabalho e o kickoff perderam a marca e saem como antes, nos dois
formatos. Voltar a marcá-los pede adaptar o esqueleto deles ao agrupamento —
o "Por quê" e as "Alternativas descartadas" não têm lugar num item de decisão.

---

## 3. Vozes e falantes

A tela é o **UI-2**. O que sobra aqui é o que não é tela.

### VOZ-1 · Sugerir fusão de perfis parecidos — `feature` · feito em 25/09/2026 (com o UI-2)

*Fundir "Élio" ↔ "Elio" (0,93)?* — a mesma pessoa inscrita duas vezes com
grafias diferentes é o defeito que a biblioteca acumula sozinha, e a distância
entre os vetores já sabe apontá-lo ([VOZES.md](VOZES.md) §6).

**Gatilho:** a mesma pessoa aparecer duas vezes na lista.

---

## 4. Gravação e agenda

### GRA-1 · O `calendar_event_id` é escrito e nunca lido — `bug` · `feito`

Ele existe no `meta.json` desde sempre
([Meta.cs:163](../app-net/Gravacao/Meta.cs#L163),
[Evento.cs:76](../app-net/Agenda/Evento.cs#L76)) e **nada em código o lê** —
conferido em 27/08/2026. Uma migração que releia os eventos no Google preenche
os `attendee_emails` que faltam.

**Medido no acervo em 26/08/2026:** das 44 gravações, **14 não têm
`attendee_emails`**, e **13 dessas têm `calendar_event_id`** — a migração
recupera todas menos uma.

**Fica como bug de dado, e não de qualidade:** a consequência mais visível dele
era a ata escolher o lado errado da pendência, e isso saiu deste backlog. O
item se sustenta sozinho — é dado que o app tem, guardou, e não usa.

**Feito em 30/09/2026** (`Agenda/ReleituraDaAgenda.cs`): meio minuto depois de
abrir, o app relê no Google cada id sem `attendee_emails` e completa o
`meta.json`. **Só quando os nomes do evento batem exatamente com os
`attendees` gravados** — convite mudado depois, ou participante sem e-mail,
desalinharia as listas paralelas (`GRA-2`), e aí a gravação fica como estava.
Cada id com resposta definitiva vai para `~/.meeting-recorder/releitura_da_agenda.json`
e não é perguntado de novo; erro de rede volta na próxima abertura. O resultado
sai numa linha `agenda` do `registro.log`.

### GRA-2 · Nome e e-mail casados por posição — `bug` · `espera`

`attendees` e `attendee_emails` são listas paralelas com deduplicações
independentes; em teoria desalinham. Hoje o casamento por posição só acontece
quando o e-mail existe e o classificador usa o e-mail direto, então o risco é
baixo. **Vira problema no dia em que alguém cruzar as duas listas.**

**Gatilho:** um nome aparecer atribuído ao domínio errado.

### GRA-3 · O mute junto com a chamada — `feature` · feito em 22/10/2026, Teams no navegador validado em 28/10/2026

Pedido do dono, 22/10/2026: não mutar em dois lugares — na chamada e no
gravador — quando não vai falar, e não desmutar em dois.

**Como ficou:** Ajustes › Gravador › Chamadas, uma chave por chamada, as duas
desligadas por padrão. A decisão do que muta onde é uma só,
`Gravacao/SincroniaDoMute.cs`, com testes, e roda uma por chamada:

- **só a mudança do mute da chamada conta** — o Teams manda o estado inteiro a
  cada câmera ou mão, e sair da reunião não desmuta a gravação;
- **o eco do próprio comando não volta**: dois segundos de espera, e dois
  cliques rápidos são dois comandos;
- começar a gravar no meio da reunião segue o mute dela, e vence o
  `start_muted`.

**Meet e Teams no navegador** — uma extensão de Chrome/Edge em
`extensao/meet/`, instalada em `extensao-meet/` e carregada pela pessoa "sem
compactação". Ela lê o botão do microfone na página e o clica quando pedido.
O PulseMeet escuta em `127.0.0.1:47821` (`Gravacao/ServidorDaExtensao.cs`) e
só aceita a origem da extensão, cujo id é fixo pela chave do `manifest.json` —
um teste confere os dois. **Frágil por natureza**: quando a página mudar, é o
`pagina.js` que se conserta, e o diagnóstico que ela manda (rótulo
`extensao` no `registro.log`) diz o que ela achou.

- **Meet**: o `data-is-muted` do botão, separado da câmera pelo atalho
  ctrl + d no rótulo, que vale em qualquer idioma. **Funcionou numa reunião
  real do dono em 22/10/2026.**
- **Teams no navegador** (pedido do dono no mesmo dia: com o mute junto, ele
  troca o app desktop pelo navegador): o `#microphone-button` com
  `data-state` "mic"/"mic-off", e o atalho ctrl + shift + m no rótulo como
  alternativa; a extensão roda em todos os quadros da página caso a reunião
  esteja num iframe. **A reunião real (27 e 28/10/2026) mostrou duas telas**:
  numa, o `#microphone-button` com `data-state` "mic-volume-renderer"/"mic-off";
  na outra, um `#mic-button` sem `data-state` nem atalho, cujo estado só está
  no rótulo — "Ativar mudo do microfone" com som, "Desativar mudo do microfone"
  mudo. A 0.12.0 não reconhecia a segunda; a extensão 1.1.1 (0.12.1) reconhece
  as duas, **e o sync funcionou nas reuniões do dono.**

**Teams desktop** — a API local em `ws://localhost:8124` ([PLANO.md](PLANO.md)
§2.1), `Gravacao/ConexaoComOTeams.cs`. O pareamento é pedido com
`query-state`, e não com a reação de "curtir" que os clientes de código aberto
mandam — ela aparece para a reunião inteira. **Não medido.** **Na máquina do
dono ele não existe:** a seção "Third-party app API" não aparece em
Configurações › Privacidade (print de 22/10/2026), e ninguém escuta na 8124 —
é política da organização. Fica para quem tiver.

**O registro só vê o lado da extensão**: o rótulo `extensao` diz o botão que
ela achou e o estado dele, mas o PulseMeet não anota quando muta a gravação
por causa da chamada. Para conferir o gravador, o silêncio está no WAV do mic.

**Fora, por enquanto:** o Zoom (o Plugin SDK de junho de 2026, que exige app no
Marketplace e conta). O MuteDeck faz os
três, mas é fechado — o caminho é o mesmo mecanismo público, escrito aqui.

### GRA-4 · "Gravar sem reunião da agenda" não tem como ser exato — `feature` · `espera`

O `fixar-evento` só conhece "esta reunião" e "nenhuma escolhida", e "nenhuma
escolhida" devolve a escolha ao automático do núcleo, que rotula a gravação
com a reunião do momento (`pre_definido`). Por isso o Gravador **esconde** o
botão sempre que há um `pre_definido` (plano 3, revisão final de 25/09/2026):
ele só aparece quando gravar sem reunião é o que de fato acontece. A versão
exata é um valor "nenhuma" no `fixar-evento` (`Bandeja/Gravador.Fixar`) que
desliga o automático até a gravação terminar.

**Gatilho:** querer gravar sem rótulo com uma reunião da agenda em curso — uma
conversa de corredor no horário de uma reunião que não aconteceu.

### GRA-5 · A tela gravada em vídeo, com a transcrição de legenda — `feature` · saiu na 0.13.0 (29/10/2026)

Pedido de uso real, 28/10/2026: gravar a tela para quem acompanha a reunião
depois poder vê-la **na íntegra**, e não só ler e ouvir.

**Como ficou:** Ajustes › Gravador › Dispositivos ganhou o seletor **Tela em
vídeo** — "Não gravar" (o padrão) ou um dos monitores. Escolher é ligar
(`screen_monitor` no `settings.json`). Gravando, sai um `tela.mp4` sem som ao
lado das faixas, e um `tela.json` com onde o primeiro quadro cai no áudio
(`Nucleo/VideoDaTela.cs`). A reunião aberta ganha a aba **Vídeo**: o
`<audio>` do tocador manda, o vídeo é arrastado até `t − inicio_s`, e a
legenda é desenhada por cima com quem fala — liga e desliga, tamanho, e tela
cheia (`web/video.js`).

- **Nada é juntado depois.** O vídeo é carimbado com a mesma origem QPC das
  duas faixas, e os tempos da transcrição servem de legenda sem conversão;
- **GDI + Media Foundation** (`Bandeja/GravadorDeTela.cs`,
  `Nativo/CodificadorH264.cs`): o `BitBlt` do print, em laço a 10 qps, e o
  encoder H.264 do Windows, da placa quando há. Sem FFmpeg. O MF é chamado
  pela vtable — o `ComImport` exigiria redeclarar o `IMFAttributes` em cada
  interface, e devolver interface de P/Invoke é IL2050;
- **medido na máquina do dono (28/10/2026, 1920×1080, reunião do Teams):**
  ~750 kb/s com um slide parado — perto de 350 MB por hora; o teto do
  bitrate é 1,7 Mb/s, ~750 MB por hora —, texto de slide legível. O
  codificador leva **~2,2 s para abrir**, e o vídeo começa esse tanto depois
  do áudio: o `tela.json` registra, e a aba diz.

**O que a prova de conceito não resolve:**

- **queda do processo perde o vídeo.** O MP4 só tem índice depois do
  `Finalize`; o áudio continua à prova de queda. O caminho é cortar em
  segmentos de alguns minutos — e é o `SUP-2` que torna isso mais que teoria;
- **um monitor só, inteiro.** Nem janela, nem região, nem os dois monitores;
- **janela protegida sai preta** (Teams com "impedir captura"), e é de
  propósito;
- **não medido**: telas 4K, e máquina com uma placa só — onde o encoder
  divide a placa com a legenda ao vivo.

**Medido numa reunião real do dono (29/10/2026)**, com a legenda ao vivo
ligada: ~600 kb/s (~270 MB por hora); o PulseMeet inteiro em 5,5% da CPU
(8 lógicos) e 249 MB de memória. **O Windows escolheu o encoder da Radeon Vega
11 integrada** (motor `codec` a ~16%), e não o NVENC da RTX 2060, que ficou em
0% — o vídeo não disputa a placa com a legenda. A sincronia da legenda sobre o
vídeo foi aprovada pelo dono no mesmo dia.

**Gatilho do próximo passo:** o primeiro uso real dizer o que falta — o
segmento contra queda é o candidato natural.

**A janela da reunião, no navegador (30/10/2026, ramo `janela-da-reuniao`).**
Pedido do dono: gravar só a reunião, e não o monitor. O seletor ganhou "A
janela da reunião (Meet ou Teams no navegador)" (`screen_monitor` =
`janela-da-reuniao`). A extensão 1.2.0 manda, no estado, a aba da reunião —
título e se está na frente —, e o PulseMeet acha a janela do Chrome ou do Edge
cujo título contém o da aba (`Gravacao/JanelaDaReuniao.cs`, com testes) e a
copia por `PrintWindow` com `PW_RENDERFULLCONTENT`
(`FonteDaJanelaDaReuniao`, em `Bandeja/GravadorDeTela.cs`).

- **Só grava com a aba na frente**, e o título da janela é a segunda trava: ele
  muda no instante da troca de aba, antes de a extensão avisar. Fora da frente
  ou minimizada, o quadro vira um aviso escrito — o vídeo não abre buraco, e a
  sincronia continua sendo uma subtração;
- a porta da extensão abre com o mute junto **ou** com esta fonte; o mute só
  anda junto com a chave dele;
- **medido em 30/10/2026** com uma página de relógio numa janela do Chrome,
  coberta 4 s por outra janela: o vídeo continuou andando, sem a janela de
  cima. O cálculo de oclusão do Chrome não congelou a janela nesse teste.
  **Não medido** numa chamada real, nem coberta por minutos;
- o picture-in-picture do Meet foi gravado por ~10 s por acaso (tem o mesmo
  título da reunião, e a extensão ainda não tinha avisado a troca). Gravá-lo de
  propósito, com a aba fora da frente, é uma mudança pequena — fica para quando
  o dono pedir.

**O Zoom desktop (30/10/2026, mesmo ramo).** "A janela da reunião (Zoom)" no
seletor (`janela-do-zoom`), sem extensão: as janelas do `Zoom.exe` pela
classe (`Gravacao/ReuniaoDoZoom.cs`, com testes; `FonteDoZoom`). **Medido
numa reunião de um só, com câmera:** a reunião é a
`ConfMultiTabContentWndClass`, e o `PrintWindow` traz a câmera perfeita;
**compartilhando a tela, ela some** e surge a `ZoomAnnoWindowWndClass`, do
tamanho exato do que se compartilha — é ela que se grava, por `BitBlt` da
tela, porque é transparente. Minimizada, a miniatura sai preta no
`PrintWindow`, e o quadro vira aviso. **Não medido:** outra pessoa
compartilhando, e o modo "lado a lado".

**Automático e o Zoom na web (30/10/2026, mesmo ramo).** "Automático: a
reunião que estiver aberta" é a primeira opção do seletor (`automatico`): grava
a reunião do navegador que a extensão aponta ou a do Zoom desktop, e troca se
ela mudar de lugar (`Gravacao/FonteAutomatica.cs`, com testes). **O que está
aberto vale mais que a agenda**; o link do evento — `hangoutLink`,
`conferenceData`, local e descrição, lidos agora pelo `ClienteDaAgenda` — só
desempata duas abertas e diz qual esperar. Sem nenhuma, aviso de espera, e não
o monitor (escolha minha, pela privacidade). A extensão 1.3.0 roda também no
`*.zoom.us`: só diz que há reunião (`#wc-footer` e o botão de sair, **não
medidos**), e o PulseMeet não segue o mute do Zoom. A aba do Zoom se chama
"Zoom", e título curto passou a casar como começo do título da janela.

---

## 5. Diagnóstico e suporte

O tema existe porque o app roda na máquina de outra pessoa, e **software que
roda na máquina de outra pessoa precisa deixar rastro**. O bloco de diagnóstico
da Fase 4 dá a *foto* — versão, placa, modelos. A pergunta que faltou responder
era outra: **o que o app fez**.

### TRA-2 · Motores em paralelo, medidos no uso real — `feature` · `espera`

> **Sem objeto desde 17/09/2026:** o MOSS saiu, e o `MotorAceito` devolve
> sempre `classico`. Não há segundo motor a medir. **Gatilho:** entrar um motor
> novo de transcrição — aí o primeiro passo abaixo (lista em vez de binário)
> vale de novo.

**A estratégia, decidida pelo dono do produto em 11/09/2026:** motor novo entra
**ao lado** do que existe, nunca no lugar; a comparação que decide é o **uso
diário**, não a bancada; e o que deixar de ser usado **se desliga**. Iteração
conservadora.

**Por que isto e não trocar o padrão.** O `large-v3` tem 70 gravações de
histórico nesta máquina. Trocar o padrão muda a transcrição de todo mundo de uma
vez, e a bancada mede quatro gravações — as únicas com gabarito. O uso real mede
todas, com o áudio de verdade e o julgamento de quem estava na reunião.

**O que já existe e serve:**

* o `transcricao.json` **já grava o campo `engine`** — dá para saber depois qual
  motor produziu cada transcrição. Nulo é `classico`, por convenção escrita;
* a `Nucleo/Retomada.cs` já confere o motor antes de reaproveitar parcial;
* a `Nucleo/Vozes.cs` já carimba a origem do vetor de voz por motor;
* o seletor em Ajustes › Transcrição já existe, e desde 10/09 governa também a
  prévia ao vivo.

**O que trava, e é o primeiro passo:** `ConfiguracoesDoApp.MotorAceito` e
`Vozes.MotorAceitoNaAmostra` são **binários** — tudo o que não é `"moss"` vira
`"classico"`. Um terceiro motor entraria **em silêncio** como clássico, gravaria
`engine: "classico"` no arquivo, e contaminaria justamente a medição que esta
estratégia depende. Precisa virar lista, com o desconhecido caindo no padrão
**e deixando rastro no registro**.

**Desligar tem de ser barato**, e hoje quase é: o GGUF de cada motor é pacote do
`Nucleo/Catalogo.cs` e se apaga por Ajustes › Modelos. O que falta é a tela dizer
**quanto cada um está sendo usado** — sem isso, "o que não está sendo usado" é
memória de alguém.

### TRA-1 · A prévia ao vivo é jogada fora — `feature` · `espera` · medido em 30/09/2026

**Gatilho: já dói, e foi relatado em uso em 11/09/2026.** O dono do produto
gravou uma reunião com a prévia ligada, pediu a transcrição ao encerrar, e
esperou o pipeline inteiro — **o app tinha acabado de transcrever aquela reunião
inteira e descartou tudo**.

Hoje o `Nucleo/SessaoAoVivo.cs` diz, no próprio comentário, que *"a prévia é
descartável: ela não vira o `transcricao.json` e não substitui nada"*. Isso veio
da regra do §7 da [FASE7.md](FASE7.md), que existia contra o defeito da 0.4.0 —
um parcial de um motor voltando rotulado como de outro, em silêncio.

**A medição já derrubou a regra, e ninguém aplicou a consequência.** A
[FASE7-RESULTADOS.md](FASE7-RESULTADOS.md) §4 é explícita: o texto por blocos
empata com o da passada inteira, e *"não é mais a mesma situação do defeito de
0.4.0 — lá o parcial era de outro modelo, aqui é do mesmo modelo com os mesmos
parâmetros"*. **Vale só para o texto**: a diarização por bloco tem 4% de erro
contra a inteira, e continua tendo que rodar do começo.

**O desenho:**

```
encerrar  →  o texto dos blocos já está em disco, menos o rabo de ≤3 min
          →  transcrever só o que falta + diarizar a gravação inteira
          →  a ata começa numa fração do tempo de hoje
```

**A máquina já existe inteira.** O `Nucleo/Retomada.cs` grava parcial marcado com
o que falta, e confere **modelo, idioma, vocabulário e motor** antes de
reaproveitar — na dúvida, roda o ASR de novo. Falta a prévia escrever nela.

**Duas coisas para não esquecer:** o último bloco nunca é processado ao vivo, e
trocar de motor entre a reunião e a transcrição faz a `Retomada` recusar o
parcial — corretamente.

**Medido em 30/09/2026, pelo `registro.log`, e o item mudou de figura.** Duas
coisas:

- **A prévia em blocos não roda para quem usa a legenda.** O `SessaoAoVivo`
  (mesmo motor da passada final, o único cujo texto dá para reaproveitar) só
  liga com a legenda desligada — a ponte dá prioridade à legenda e retorna
  antes. O texto da legenda é o Nemotron ASR, outro modelo, sem pontuação: a
  `Retomada` o recusaria, e com razão;
- **reaproveitar os falantes da legenda (`VIVO-7`) não compensa.** Com o
  nemotron-3 na passada final, a diarização leva **13 a 15 s** (30/09: 10h19 e
  14h29, reuniões de 20 e 30 min); com o community-1, 1,5 a 3 min. O ASR leva 5
  a 8 min e é **~90% do tempo**. A legenda com nomes economizaria no máximo os
  15 s — e é streaming, 0,4 ponto pior que o offline.

**O que sobra dele é só o texto, e só sem legenda:** escrever os blocos da
prévia na `Retomada` corta o ASR para o rabo de ≤3 min. **Gatilho novo:**
alguém usar a prévia em vez da legenda — ou a placa comportar as duas ao
mesmo tempo, o que na 2060 com o Meet ninguém mediu e o `MOD-1` sugere que não.

### SUP-1 · O app não sabe dizer o que aconteceu — `feature` · `aberto` (4 de 5 na 0.8.1)

Prometido para a 0.4.1 e **nunca entregue** — conferido no código em
27/08/2026, com a versão em 0.6.1. São cinco instrumentos:

| instrumento | estado hoje |
|---|---|
| assinar o `AoRegistrar` em `AprendizadoDeVozes.ExtrairAsync` | ✅ **existe desde 04/09/2026** — a terceira carga de GPU escreve no registro como `vozes` |
| `Registro.Ultimas()` no bloco de diagnóstico | ✅ **na 0.8.1** — Ajustes › Sobre ganhou "Copiar com o registro", que leva o bloco e as últimas 40 linhas. O bloco curto continua curto |
| marcador de transcrição em andamento, apagado ao terminar | ✅ **existe desde 10/09/2026** — `Nucleo/MarcaDeEtapa.cs`, escrito com `WriteThrough` a cada etapa e apagado no `finally` do `ExecutarAsync`. A órfã entra no log e no **bloco colável** do diagnóstico |
| ler o Event Log (`6008`, `Kernel-Power 41`, `BugCheck 1001`, `Kernel-Boot 27`) | **não existe** — nenhum `EventLogReader` na árvore |
| amostrar o `nvidia-smi` a cada ~15 s durante a transcrição | **não existe** — [Diagnostico.cs](../app-net/Nucleo/Diagnostico.cs) faz uma chamada só, com *"não chame em laço"* escrito nela |

**Quatro dos cinco existem.** Falta só o que é Windows-only e caro: o Event Log e a amostragem do `nvidia-smi`. Os dois primeiros eram de uma linha cada, e valiam mais que os outros: é
o registro que separa um desligamento na diarização de um desligamento vinte
minutos depois. Os dois últimos são Windows-only e usam reflexão — cuidado com o
`PublishTrimmed` e com os testes `net8.0` portáteis.

**Já dói, e o gatilho é o SUP-2:** pedir a informação ao usuário já custou duas
idas e voltas com respostas parciais — ele mandou o bloco de diagnóstico no
lugar do log, e depois o evento errado do Visualizador. **Este item é o que
torna a reavaliação do SUP-2 barata**, e por isso vem antes dela.

### SUP-2 · O computador do segundo usuário desliga na transcrição — `bug` · `bloqueado`

RTX 4050 Laptop, 16 GB, Windows 10.0.26200. Não trava e não dá tela azul: **a
máquina desliga sozinha**. O `registro.log` da 0.2.1 mostrou que o **ASR termina
bem, na GPU**, e que o corte vem da **diarização em diante** — o que derrubou as
quatro hipóteses anteriores (queda para CPU, VRAM, memória do sistema,
driver/TDR). Desligamento seco sob carga de GPU é **corte de energia**, térmica
ou de entrega, e provavelmente não é conserto nosso. O histórico inteiro está em
[FASE6.md](FASE6.md) §3.0 e continua válido.

**Bloqueado em:** reavaliar na máquina dele, com a versão de hoje. A versão
relatada era a **0.1.0**, e de lá para cá mudaram o pipeline, a retomada e o
registro. **O pedido de uma linha continua sendo o mesmo:** transcrever com a
separação de falantes desligada — se sobreviver, confirma que é a segunda carga
de GPU que derruba, e ele sai com a reunião transcrita de qualquer forma.

### SUP-3 · O mix carrega 1,4 GB em pico — `feature` · `espera`

O passo do mix mantém três `float[]` de 460 MB para uma reunião de 2 h
([Faixas.cs](../app-net/Nucleo/Faixas.cs)). Fazê-lo em blocos derruba o pico
para alguns MB. A hipótese de memória caiu no caso do SUP-2, então isto deixou
de ser urgente — **vale por si, quando incomodar**.

---

## 6. Distribuição e atualização

### DIST-1 · Separar os motores do instalador — `feature` · `feito`

> **Feito em 30/09/2026.** O instalador tem **10 MB** e leva o app e o código
> dos sidecars; os motores — `python/` e `diarizacao/modelos/`, 5.518 arquivos,
> 3,87 GB — viajam num `.7z` de **1,68 GB** publicado num release próprio,
> `motores-<impressão>`, que o Inno baixa, confere por SHA256 e extrai **só
> quando a impressão instalada (`motores/versao-dos-motores.txt`) é outra**. A
> impressão é o sha256 da lista "caminho tamanho" do que viaja, mais o formato
> do pacote. Testado de ponta a ponta numa instalação isolada: primeira vez
> 4 min 35 s (download 1 min 53 s), segunda vez **2 s e nenhum download**,
> Nemotron carregando no CUDA pelo Python do pacote, desinstalação limpa.
> **O pacote não pode ser sólido:** com blocos de 64 MB o Inno levaria ~2 h
> para extrair (medido: 369 arquivos em 8 min 44 s). O `--completo` ainda gera
> o instalador de antes, para instalar sem internet. Quem tem a 0.9.0 ou
> anterior baixa os motores uma vez na primeira atualização, porque não tem a
> marca. Falta o que é do dono: submeter ao `microsoft/winget-pkgs`.

> **Estado em 29/09/2026:** o MOSS saiu (17/09) e as DLLs de CUDA que nenhum
> motor abre foram cortadas (PR #18, −56 MB). Os números das notas abaixo são
> de antes disso. A separação em si continua por fazer.

**É o único item deste backlog com prazo.** Enquanto os manifestos não forem
submetidos ao `microsoft/winget-pkgs`, o `PackageIdentifier` ainda pode ser
trocado de graça. E é isto que torna 1,59 GB submissível e um update de 18 MB
possível — sem ele, o `winget upgrade` não funciona.
Ver [ATUALIZACAO.md](ATUALIZACAO.md) e
[instalador/winget/LEIAME.md](../instalador/winget/LEIAME.md).

> **Ficou mais urgente em 04/09/2026, e há 900 MB de gordura identificada.** O
> motor MOSS levou o instalador de 1,59 GB para **2,31 GB** — quase o dobro do
> que a [FASE7-BACKEND.md](FASE7-BACKEND.md) A1 estimava (~200 MB). O excedente
> **não é o motor**: é o `transcribe-cpp-native-cu12` trazendo os wheels
> `nvidia-cublas-cu12`, `nvidia-cuda-nvrtc-cu12` e `nvidia-cuda-runtime-cu12` —
> **927 MB de bibliotecas de CUDA que o `torch/lib` já traz**. Medido: o torch
> 2.6.0+cu124 empacota `cublas64_12.dll`, `cublasLt64_12.dll`, `cudart64_12.dll`
> e `nvrtc64_120_0.dll`; os wheels trazem os mesmos, na 12.9.
>
> **Não foi cortado, e o motivo é honesto:** saber se o `transcribe.cpp` aceita
> as DLLs do torch em vez das dele é pergunta empírica, e só se responde numa
> máquina Windows com placa. Cortar às cegas trocaria 900 MB por um motor que
> não carrega. Fica escrito com número, que é o que faltava para decidir.

> **Medido de novo em 10/09/2026, e a duplicação é maior do que 927 MB.** A
> instalação inteira tem **17 GB** em `motores/`, e os mesmos arquivos de CUDA
> aparecem em **três** lugares — `torch/lib`, os wheels `nvidia/*` e o
> `ata/bin` do llama.cpp, que este item não olhava:
>
> ```
> cublasLt64_12.dll  3x      ggml-cuda.dll   2x
> cublas64_12.dll    3x      nvrtc64_120_0.dll  2x
> ```
>
> **São 1,44 GB de bytes idênticos.** E há mais 5,24 GB em dois GGUF de ata que
> ficaram das comparações e não estão em uso. O plano de corte, com a ordem por
> risco e a régua de cada degrau, está na
> [CONVERGENCIA.md](CONVERGENCIA.md) §3 — e o corte grande não é este: é o
> `torch`, 4,8 GB, preso por uma operação só (§2 de lá).

### DIST-5 · Varredura de modelos, recorrente — `feature` · `aberto`

**Gatilho: 30/09/2026**, e depois a cada mês. Definido pelo dono do produto em
10/09/2026.

**Por que ele existe.** A varredura de 10/09 achou, numa tarde, duas coisas que
não estavam no radar de uma semana antes: o **`Qwen3-ASR` com GGUF oficial do
`ggml-org`** — que pode apagar o `transcribe.cpp` inteiro do instalador, já que
o llama.cpp está embarcado — e o **`Voxtral Mini 4B Realtime`** da Mistral,
Apache 2.0 e com português declarado. Nenhum dos dois foi procurado; os dois
apareceram porque alguém olhou.

**A régua, e ela é curta.** Um candidato só interessa se passar nos quatro:

1. **declara português** (ou transcreve bem sem declarar, como o MOSS);
2. **roda em ggml ou ONNX** — não há NeMo nem vLLM dentro de um Python
   embarcado de 4,3 GB que precisa **encolher**;
3. **cabe na 2060 de 6 GB** compartilhada com o Meet, com a folga intermitente
   medida na [CONVERGENCIA.md](CONVERGENCIA.md) §5;
4. **muda alguma decisão nossa.** Modelo melhor que não troca nada é notícia,
   não tarefa.

**A saída são linhas escritas**, uma por candidato, com licença, tamanho,
línguas e runtime — e nada mais. Ver o `R0` da [FASE7-ROTA.md](FASE7-ROTA.md), que
é a mesma varredura na primeira execução.

**Varredura de 29/09/2026:**

- `nvidia/Nemotron-3-Diarization` (23/09, ONNX, até 8 falantes) — já medido e
  portado; é o `MOD-1`. Não é notícia nova;
- `ibm-granite/granite-speech-5.0-470m-turboctc` (Apache 2.0, 470M) — **só
  inglês**; reprova no 1;
- `audio.cpp` (runtime ggml de áudio, 62 famílias na 0.7 de 26/08) — não é
  modelo; é candidato a runtime se o `transcribe.cpp` travar. Não muda nada hoje.

Nenhum muda decisão. Próxima: 30/10/2026.

**Medição de 29/09/2026**, pedida pelo dono do produto, contra os nossos
números: nas quatro reuniões com Gemini, pela régua do `wer_contra_gemini.py`.
**Os dois saíram, e a decisão do dono é não seguir com modelo novo**. O que vale
é ajustar o Nemotron que já temos (`VIVO-8`).

- `oruk/orukeet` (Parakeet TDT v3 com filtros de Gabor, CC BY-SA 4.0, GGUF e
  ONNX) — **fora.** Exportado para fp32 (`tools/exportar_orukeet_onnx.py`) e
  medido pela `medir_parakeet.py`, contra o Parakeet v3 fp32 de 11/09. Ele
  diverge mais do Gemini em 3 das 4 reuniões (29,9 contra 26,9; 29,6 contra 28,1;
  24,6 contra 24,2) e só ganha na de 14,6 min, por meio ponto. Perde do
  `large-v3` nas mesmas três, e não tem vocabulário. O ganho do card é no
  FLEURS, que é fala lida;
- `ibm-granite/granite-speech-4.1-2b` (Apache 2.0, 2B, vocabulário no pedido) —
  **fora, e pelo pior motivo: traduz em vez de transcrever.** Em reunião em pt
  ele alterna entre transcrever, traduzir para o inglês (*"customers have with
  him, all the links that they send to the teams"*) e inventar *"thank you very
  much"* em trechos com fala. Nenhuma combinação corrigiu isso: blocos de 30 s,
  60 s e 3 min, com três pedidos diferentes, um deles em português. A melhor
  cobriu 11,5% da reunião de 7,7 min, contra 77,2% do app. Em fp16 ele entra em
  laço, e só roda em bf16, com 5 GB de pico. O F1 de termo em pt que o card
  anuncia é medido em frases do Common Voice, e não se transfere para reunião
  (`tools/medir_granite.py`);
- `granite-speech-4.1-2b-plus`, `VibeVoice-ASR-BitNet`, `desert-ant-labs/align`
  e o porte Core ML do Nemotron 3.5 (`smdesai`) — **não medidos**, reprovados
  pelo card. O `-plus` é o Granite sem pontuação. O BitNet é CPU, medido em
  20 s, sem falante nem vocabulário. O `align` só refina carimbo, e tem licença
  comercial em escala. O `smdesai` é só Apple, mas deu a pista do `VIVO-8`: o
  quadro e o contexto de atenção mexem sem quebrar o modelo.

**Varredura de 26/10/2026** (pedida pelo dono do produto; janela 29/09 → 26/10,
pela API do HuggingFace). Pela régua de arquitetura da
[CONVERGENCIA.md](CONVERGENCIA.md) — AED e LLM não dão carimbo de tempo:

- `moondream/parakeet-ultra` (CC BY 4.0, Parakeet TDT v3 retreinado, GGUF no
  `transcribe.cpp` ≥ 0.3) — **passou e foi medido**: ganha do `large-v3` nas
  seis reuniões com Gemini. É o `MOD-3`;
- `ibm-granite/granite-speech-5.0-2b` (20/10, Apache 2.0, GGUF oficial no
  llama.cpp, vocabulário até 2.000 termos, prompt de idioma que pode consertar o
  "traduz em vez de transcrever" do 4.1) — **LLM, sem carimbo**;
- `CohereLabs/cohere-transcribe-03-2026` (março, Apache 2.0, roda no
  `transcribe.cpp`) — **AED, sem carimbo**. Nunca tinha entrado aqui;
- `microsoft/VibeVoice-ASR-Streaming` 1.5B e 7B (MIT, texto + falante +
  hotword, GGUF no `audio.cpp`) — **o artigo diz "no timestamps are emitted"**,
  e o 1.5B tem cpWER de 45 no AMI-IHM;
- `netease-youdao/Confucius4-R2T2` (Qwen3-ASR em streaming, licença NetEase) —
  pt não otimizado, LLM;
- Phonon-2.5 e `desert-ant-labs/voz` — Parakeet v3 comprimido; o Phonon é pior
  que o v3 em pt;
- diarização: `desert-ant-labs/who` (exige crédito visível, 4 falantes por
  5 min) e DiariZen (CC BY-NC, torch) — nenhum muda o `MOD-2`;
- só inglês ou chinês, reprovam no 1: Whistle, Underdog-Bark-3.0,
  Ming-3.0-asr-tiny, tyger, AuroraASR, fluid-speech-mini, Audio8-ASR-Infinite.

Próxima: 26/11/2026.

### DIST-2 · Motores como pacotes por acelerador — `feature` · `espera`

CUDA, Vulkan e CPU como variantes baixáveis, em vez de tudo embutido
([PLANO.md](PLANO.md) §5). O motor de ata já nasceu com o desenho certo — é um
binário à parte, trocável sem tocar no app —; os motores Python não. **É a forma
madura do DIST-1**, e provavelmente sai dele.

**Gatilho:** o app precisar rodar em máquina sem NVIDIA.

### DIST-3 · Baixar e trocar o binário sozinho — `feature` · `bloqueado`

Hoje o app avisa que saiu versão nova, lendo o `versao.json` do próprio
repositório, sem servidor. Atualizar sozinho fica **depois da assinatura de
código**. O winget faz esse degrau sem que o app aprenda nada, e é por isso que
o DIST-1 vem antes.

### DIST-4 · Linux e Mac — `feature` · `espera`

Os motores são multiplataforma; o núcleo não. Continua sendo decisão de ordem,
não dívida.

---

## 7. Ao vivo: legenda e pergunta

**O tema nasceu em 17/09/2026**, quando a pergunta ao vivo saiu do estudo e
entrou no app. Os itens abaixo vieram todos de usar a coisa numa reunião de
verdade, e cada um tem medição atrás — não são ideias.

O que já existe e **não** está aqui: a legenda ao vivo (Fase 7), a caixa de
perguntar com o botão de resumo, a janela de uma hora, as chaves de ligar e
desligar. Ver [ESTUDO-RESUMO-AO-VIVO.md](ESTUDO-RESUMO-AO-VIVO.md).

**Dois itens já vêm medidos** — o `VIVO-4` (a legenda contra o `large-v3`, sete
reuniões) e o `VIVO-6` (a legenda sem placa) —, e os dois deram resultado
negativo para a esperança que os motivou. Ficam escritos porque um número
negativo economiza a próxima tentativa.

### VIVO-1 · A legenda não carimba o turno — `feature` · feito em 17/09/2026, pelo trecho

> **Feito por outro caminho:** o `legenda.json` guarda `TrechoDaLegenda` com
> `inicio_ms`/`fim_ms` (o `audio_committed_ms`, 1,1 s de mediana). O
> `timestamps="word"` foi abandonado porque o `snapshot()` devolve zero palavras
> em streaming. **Sobra uma coisa:** a janela da pergunta ainda é medida em
> caracteres (`CaracteresPorMinuto = 800`) — pode passar a usar o tempo dos
> trechos quando incomodar.

**Gatilho: ele bloqueia outras três coisas, e custa zero.** O `TurnoDaLegenda`
guarda `dono` e `texto`, e nada de tempo. Sem tempo:

- a **diarização não pode rodar sobre a legenda** (`VIVO-2`) — a atribuição de
  falante é por sobreposição temporal, e não há o que sobrepor;
- **pergunta com recorte de tempo não existe** — *"o que rolou nos últimos 10
  minutos"* não tem como ser respondida, e o prompt precisa avisar o modelo
  disso para ele não inventar horário;
- a **janela de uma hora é medida em caracteres**, não em minutos:
  `PerguntaDaReuniao.CaracteresPorMinuto = 800`, aproximado de três reuniões.

**E o preço é zero, medido em 17/09/2026** — 10 min de reunião real, na 2060:

```
timestamps=none   4,78x   475 commits   1º aos 1,2 s
timestamps=word   5,04x   475 commits   1º aos 1,2 s
```

Mesmo texto, mesmos commits, mesma latência de partida. A diferença está dentro
do ruído entre rodadas. O motor pede `"none"` hoje por herança, não por medição.

**Havia dois caminhos, e o barato NÃO basta — medido em 17/09/2026.** A ideia
era carimbar o **turno** com o que o motor já sabe
(`_ms_de_antes + audio_committed_ms`), sem tocar no modelo. Contando os turnos
das nove gravações do acervo:

```
gravação              turnos   palavras   donos
2026-09-15_14-01-30      161      5.116   ambos
2026-09-15_14-58-08       87      1.911   ambos
2026-09-15_15-29-32        2      2.907   ambos
2026-09-16_10-30-08        1      1.673   só "outros"
2026-09-16_15-29-33      153      2.774   ambos
2026-09-17_08-59-06        1      4.246   só "outros"
2026-09-17_10-29-47        1      3.539   só "outros"
2026-09-17_11-00-45       31      2.385   ambos
2026-09-17_13-59-57       19        876   ambos
```

**Em três das nove a reunião inteira é UM turno.** São aquelas em que o dono do
produto ficou com o microfone mudo: o `dono` nunca vira, e o turno nunca quebra.
O comportamento está certo — o turno quebra por troca de dono, e não houve
troca —, mas um turno de 4.246 palavras com um carimbo só não serve para
sobrepor nada.

**Então o caminho é o `timestamps="word"`**, que já se sabe custar zero. O
barato não era mais barato: era insuficiente em um terço do acervo.

### VIVO-2 · Diarizar a legenda depois da reunião, em segundo plano — `feature` · feito

> **Feito:** `Nucleo/FalantesDaLegenda.cs` separa os falantes sobre os
> trechos no fim da reunião e grava `falantes_prontos` no `legenda.json`; a
> correção de termos do `VIVO-3` roda junto.

**Depende do `VIVO-1`.** A ideia é entregar *quem falou* no rascunho logo depois
da reunião, sem esperar a passada final.

**A diarização é barata: 32× o tempo real** — uma reunião de uma hora sai em
~2 min, cinco vezes menos que o ASR
([FASE7-RESULTADOS.md](FASE7-RESULTADOS.md) §5).

**Metade do valor já vem de graça:** a legenda separa "você" dos outros pela
faixa do microfone, sem GPU. O que a diarização acrescenta é separar **os outros
entre si**.

**O que isto NÃO é:** substituto da passada final. O texto da legenda é
Nemotron; a passada final é `large-v3` com correção fonética e vocabulário. Isto
entrega falante no rascunho, e nada além.

**O que falta além do `VIVO-1`:** não há nada rodando em segundo plano depois
que a gravação para — não existe `transcrever_ao_parar`. O gancho existe
(`Ponte.EncerrarAPrevia`), a máquina de progresso não.

### VIVO-3 · O vocabulário não chega à legenda — `bug` · **feito em 23/09/2026**

**Relatado em uso em 17/09/2026:** *"durante a reunião a legenda parece ser boa,
errando mais em termos específicos"*. Estava certo, e não tinha conserto por
configuração — na entrada.

**O gancho não existe para esta família, e isso continua verdadeiro.** O
`initial_prompt` do `transcribe_cpp` é do `WhisperRunOptions` — família Whisper,
que é a da passada final. O Nemotron é da família Parakeet, e o
`ParakeetStreamOptions` expõe **uma única** opção: `att_context_right`. Não há
onde pôr termo na entrada, e por isso o conserto é **depois**.

**Fechado corrigindo depois, não trocando modelo.** A correção de termos da
passada final — fonética, regra e grafia, agora em `Nucleo/CorrecaoDeTermos.cs`
— passou a rodar também sobre a legenda ao vivo, no fim da reunião, junto da
separação de falantes (`Nucleo/CorrecaoDaLegenda.cs`). Ela corrige por **fala**
(trechos seguidos do mesmo falante, onde a troca se enxerga) e funde só os
trechos que uma troca atravessa — o trecho tem 1,1 s, e três das quatro
propostas medidas eram de duas palavras.

**A medição, sobre a reunião real de 23/09/2026** (`2026-09-23_10-00-17`,
101 min, projeto *Agentes (Interno)*):

| termo | Whisper | legenda crua | legenda + pós |
|---|---:|---:|---:|
| Wifi | 14 | 2 | **11** |
| Tânia | 2 | 0 | **2** |
| V.TAL | 2 | 0 | **2** |
| Webhook | 4 | 3 | **4** |
| Prada | 13 | 7 | 8 |
| API · Cloud | 11 · 6 | 6 · 1 | 6 · 1 |

**15 trocas, 4 propostas validadas** (`Wi Fi→Wifi`, `Vital→V.TAL`,
`pra da→Prada`, `web hook→Webhook`) + `Tania→Tânia` da fonética. O teste de
acervo `CorrecaoDaLegendaNoAcervoTests` reproduz esta reunião e confirma:
`15 trocas, 1 fundidos, Wifi = 11`.

**Recupera o que o motor ouviu e escreveu diferente; o que ele não ouviu
continua faltando, e só a passada final recupera.**

**Duas coisas que a medição ensinou, fora do que já se sabia:**

- **A fonética por trecho perdia contexto de frase.** O desenho original (plano
  `2026-09-23-pos-processamento-da-legenda`, passo 4) recalculava a fonética
  depois de recortar cada trecho, e `CorrecaoFonetica.Corrigir` só aceita
  maiúscula como sinal de nome próprio quando há texto **antes** dela na mesma
  frase — recortar apaga esse "antes" sempre que a palavra corrigida abre o
  trecho, e o nome não era corrigido, em silêncio. O conserto roda a fonética
  uma vez por fala inteira, e mapeia cada troca para o trecho a que pertence
  pelo deslocamento de caractere, não por recorte.
- **A contagem é sem diferenciar caixa, como a medição original.** Das onze
  ocorrências de "Wifi" nesta reunião, duas já estavam certas na legenda crua —
  uma `Wifi` e uma `wifi`, em minúscula — e as outras nove eram `Wi Fi`, que a
  cadeia converteu: 2 + 9 = 11. Ela não normaliza a caixa de uma palavra já bem
  grafada, e não tenta: `RevisaoDeTermos.Propor` só considera candidato uma
  palavra que **começa maiúscula**, e `RevisaoDeTermos.ProporGrafia` exige um
  lado com espaço ou hífen. Nenhuma das duas foi feita para consertar caixa
  isolada, só nome/sigla por distância e espaçamento errado — o `wifi` entra na
  contagem, mas não é obra da correção.

**Depende do vínculo da reunião com o projeto**, porque é dele que vem o
vocabulário. E o vínculo muitas vezes chega **depois** da separação de falantes:
em 5 de 9 reuniões recentes o `reuniao.json` foi escrito depois de ela
terminar, e quase sempre quando ela foi recusada. A correção rodava então sem
vocabulário, com 0 trocas, em silêncio — e não rodava de novo, porque
`falantes_prontos` já era `true`. **Por isso ela roda outra vez quando o vínculo
é salvo** (`salvar-reuniao` e o início de uma transcrição que muda o vínculo),
se a separação já terminou; a segunda passada sobre o mesmo texto não acha o
que trocar, então repetir é seguro. E o `registro.log` diz quando faltou
vocabulário (`termos: sem vocabulário (reunião sem projeto)`), em vez de um
zero mudo. **O que não se reaplica:** editar o vocabulário do projeto depois
não corrige as legendas de reuniões passadas — isso pediria varrer todas as
reuniões do projeto, e fica até alguém salvar o vínculo daquela reunião de
novo.

**Ainda falta:** a tela não mostra as trocas da legenda — o `swaps` fica só no
`legenda.json`, para quem abrir o arquivo.

**Faz parte da convergência para o Nemotron alcançar paridade com o
`large-v3`** no eixo texto — ver [docs/CONVERGENCIA.md](CONVERGENCIA.md), `S4`.

**Não confundir com defeito:** a passada final continua recebendo o vocabulário
normalmente. O que errava era só o rascunho, ao vivo, e agora a legenda gravada
também recebe a correção depois que a reunião termina.

### VIVO-4 · A legenda contra a passada final — `débito` · **medido em 17/09/2026**

**Sete reuniões com `legenda.json` e `transcricao.json` lado a lado**, o
`large-v3` como referência, a normalização do `tools/benchmark_wer.py`:

```
reunião              legenda   final   cobertura    WER      CER
2026-09-15_14-01-30     5116    4733       108%    25,6%   13,0%
2026-09-15_14-58-08     1911    1742       110%    27,2%   15,1%
2026-09-15_15-29-32     2907    2791       104%    22,7%   12,8%
2026-09-16_10-30-08     1673    1761        95%    30,7%   20,8%
2026-09-16_15-29-33     2776    2534       110%    25,6%   14,7%
2026-09-17_08-59-06     4248    4153       102%    23,7%   14,5%
2026-09-17_10-29-47     3539    3186       111%    26,9%   14,9%
média                                      106%    26,1%   15,1%
```

**O CER é metade do WER.** A primeira leitura disto — escrita aqui e depois
derrubada — foi que o erro seria de **fronteira**, o motor acertando os sons e
errando onde a palavra começa. **Medido, é falso:** fronteira pura é **2,0%** do
erro. A inferência a partir do CER não se sustentou, e o que a classificação
achou no lugar é mais útil.

**De que tipo são as 6.271 palavras divergentes**, alinhando as duas sequências
e classificando cada bloco:

```
fronteira   as letras são as MESMAS, só o corte muda      123     2,0%
quase       parecidas (>=0,7 de similaridade)             926    14,8%
conteúdo    palavra de verdade diferente                2.527    40,3%
a mais      a legenda escreveu, a final não tem         1.920    30,6%
a menos     a final tem, a legenda não escreveu           775    12,4%
```

**Os 30,6% "a mais" são disfluência, e isso relativiza o WER inteiro.** Lidos à
mão, são muleta, gagueira e repetição — `'ne a gente ta ta a'`, `'o o ne'`,
`'ele ele e'`, `'viu viu da'`. **A legenda transcreve o que foi literalmente
dito; o `large-v3` limpa.** Não é alucinação nem perda: é estilo. Para legenda
ao vivo, transcrever o "né" está certo; para documento, limpá-lo está certo.

**E os 2% de fronteira que existem são quase todos termo técnico** —
`'tecni co'`, `'wi fi'`, `'a p i'`, `'diagno stico'`. É a mesma população de
palavras do `VIVO-3`, e a mesma cura serviria às duas.

> **Ressalva do método:** o alinhamento por `difflib` sobre duas sequências
> longas e ruidosas produz blocos grandes espúrios — pares como
> `'c a boa tarde boa tarde gente tudo'` contra `'chido'` são artefato de
> alinhamento, não um erro só. O número de "conteúdo" é o mais afetado por isso
> e deve ser lido como **teto**, não como medida.

> Os números saíram de duas implementações independentes — a
> `taxa_de_erro` do `tools/benchmark_wer.py` e uma versão vetorizada escrita
> para o acervo caber no tempo — e bateram decimal por decimal.

**A cobertura é o achado, e ele contraria o que se esperava: 106%.** A legenda
produz **mais** palavras que a passada final, não menos. Então os 26% **não são
omissão** — o `R6` não está comendo fala nestas sete. É o Nemotron escrevendo
palavra diferente do `large-v3`, e às vezes escrevendo demais.

**26% não é erro absoluto.** A passada final entrou como referência e ela não é
a verdade — é o melhor que o app produz hoje. O número mede **distância entre
dois motores**. Saber quem erra exige gabarito humano, e isso é outro trabalho.

**O que isto decide sobre o `TRA-1`:** nada ainda. 26% de distância é longe
demais para a legenda poupar a passada final como os blocos do MOSS poupam — lá
o texto **empatava**. Para reabrir, seria preciso o gabarito.

**Relacionado:** o `VIVO-3` fecha uma fatia pequena e específica desta
distância — a que é "ouviu certo, escreveu diferente" —, não os 26% inteiros.

### VIVO-5 · O Sortformer está no pacote, e o `T4.1` não sabe — `feature` · `espera`

**O que mudou:** a [FASE7-FILA.md](FASE7-FILA.md) arquivou o `T4.1` — *"exportar
o Sortformer para ONNX e rodá-lo offline"* — como adiado e nunca começado. **O
`transcribe_cpp` que o app já empacota traz o Sortformer pronto**, com
`SortformerStreamOptions` e quatro pontos de operação (`very_high_latency` a
`low_latency`, ~30 s a ~1 s de antecipação). O `stream()` tem um parâmetro
`diarize`.

O trabalho que arquivou o item — exportar para ONNX — **não precisa ser feito**.
Nada foi medido: nem custo de GPU, nem qualidade, nem se ele convive com a
legenda na 2060. É candidato ao `VIVO-2` sem passada de pyannote, e é a única
rota conhecida para falante **durante** a reunião.

---

### VIVO-6 · Legenda sem placa: medido, e não fecha nesta CPU — `feature` · `espera`

**Gatilho: perguntado pelo dono do produto em 17/09/2026** — dá para usar a
legenda em PC sem GPU? Medido no mesmo áudio, 5 min, `n_threads=4`,
`timestamps=none`, num Ryzen 5 3400G (4 núcleos, 2019):

```
                velocidade   núcleos   WER contra o Q8
Q8_0 · CUDA        6,57x       2,00          —
Q4_K_M · CUDA      4,69x       1,75         6,5%
Q8_0 · CPU         0,81x       3,82          —
Q4_K_M · CPU       0,38x       2,92         6,5%
```

**O critério de morte da fase é 1,5× sustentado**, e o melhor arranjo em CPU dá
**0,81×** — com a máquina ociosa. Abaixo de 1× a fila cresce para sempre.

**E a quantização menor piora, nos dois backends.** É contraintuitivo e é real:
o `Q8_0` desempacota quase como leitura de byte, enquanto o `Q4_K_M` paga
trabalho por peso. Num modelo de 0,6B, que já é barato em memória, o custo de
desempacotar domina. **O Q4 é estritamente pior: mais lento E 6,5% de WER.** A
rota "quantizar para caber na CPU" está fechada.

**O que a medição NÃO diz:** que máquina nenhuma sem placa serve. Diz que
**esta** não serve. Como o `n_threads` já está escolhido e a quantização não
ajuda, o que falta é CPU: seria preciso ~2× este processador para chegar a
1,5×, o que coloca a fronteira em torno de um 8 núcleos moderno. Medir num
desses é o item — não há mais o que otimizar deste lado.

---

### VIVO-7 · Falante durante a reunião, com o Nemotron-3 — `feature` · `feito`

> **Versão de teste em 30/09/2026**, a pedido do dono do produto, atrás da chave
> "Nomes ao vivo na legenda (teste)" em Ajustes › Transcrição
> (`falantes_ao_vivo`, desligada por padrão, só com a legenda ligada). Um
> segundo sidecar (`AoVivo` em `motores/diarizacao/motor.py`) recebe o
> `system.wav` e roda o Nemotron em `low_latency` (`FluxoAoVivo`, que decide
> 100% igual ao bloco a bloco); cada vaga manda o vetor de voz aos 6 s e aos
> 20 s de fala limpa, e o núcleo (`Nucleo/FalantesAoVivo.cs`) põe o nome com o
> `Vozes.Reconhecer` — ou "Pessoa N". Ele é encerrado antes da separação de
> falantes do fim. **Medido em 30/09** com o Meet e a pergunta (§4c): cabe, e
> 95,2% dos trechos dos outros saem com o nome certo; o erro fica na troca de
> pessoa. **Falta:** decidir se sai do teste.

**Gatilho: o `MOD-1` fechar com folga ao lado da legenda.** Medido em
25/09/2026 ([NEMOTRON-DIARIZACAO.md](NEMOTRON-DIARIZACAO.md)): o
`nvidia/Nemotron-3-Diarization` em streaming acerta o falante quase como no
offline — 0,4 ponto a menos no pior caso, a 0,32 s de latência —, e ambos acima
do pyannote de hoje. É a rota para o que o `VIVO-5` procurava, com 8 falantes
em vez de 4, e sem o `transcribe_cpp`: roda em onnxruntime, no sidecar.

**E nomeia desde o primeiro segundo**, por matrícula: 15 s de cada pessoa
conhecida postos antes do áudio fazem dos primeiros slots essas pessoas (95,8% e
100% das palavras no slot certo, ao vivo). O que isso pede — de onde vem a lista
de quem matricular, e se os 4 s que o banco guarda bastam — está na §5 do
documento.

**Relacionado:** o `VIVO-2` (diarizar a legenda depois) e o `VIVO-1` (carimbar o
turno) — sem tempo no turno, não há onde pôr o falante.

---

### VIVO-8 · Os parâmetros do Nemotron da legenda nunca foram medidos — `débito` · fechado em 26/10/2026

> **Medido em 26/10/2026, e a hipótese caiu duas vezes** (`tools/medir_asr_final.py
> legenda`, quatro reuniões com Gemini, 58 min). **O padrão de produção já é o
> `att_context_right=13`**, o maior lookahead do modelo (o menu é 0, 3, 6, 13):
> o padrão e o r13 saíram idênticos número por número. Menos lookahead só troca
> atraso por texto pior e por velocidade menor:
>
> | r | lookahead | divergência | velocidade | atraso do firme | recomeços |
> |---|---|---|---|---|---|
> | 0 | 0 ms | 37,8% | 1,1× | 0,04 s | 7 |
> | 3 | 240 ms | 33,9% | 4,6× | 0,17 s | 2 |
> | 6 | 480 ms | 33,4% | 6,5× | 0,28 s | 2 |
> | **13 = produção** | 1.040 ms | **32,8%** | 8,8× | 0,57 s | 2 |
>
> **A pontuação não volta com lookahead nenhum, porque o modelo não pontua fala
> de reunião em português**: 0 a 2 pontos por mil palavras em todos os r, e
> 1,8 também em bloco, com o contexto inteiro. **A referência oficial pelo
> Transformers dá o mesmo texto, palavra por palavra, sem um ponto** — o porte
> está fiel, e o card ("built-in punctuation") vale para a fala lida do FLEURS.
> O `VIVO-1` não tem o ponto final para quebrar o turno: ele vem de outro lugar
> (Smart-Turn, ou pontuar o texto depois). O r6 fica anotado para o dia em que
> meio segundo de atraso incomodar: corta o atraso pela metade por 0,6 ponto.

**Gatilho: pedido pelo dono do produto em 29/09/2026**, depois do spike de 23/09
ter levantado a hipótese. O `motores/legenda/motor.py` abre o streaming só com
`commit_policy`, `timestamps` e `language`: **o lookahead (`att_context_right`)
e o quadro são os padrões da família**, e nenhum dos dois foi escolhido por
medição.

**O sintoma que motiva: a legenda quase não pontua.** O card do
`nemotron-3.5-asr-streaming-0.6b` diz que ele pontua e capitaliza em pt, e as
`legenda.json` do acervo têm maiúsculas mas ~0 pontuação (até 10.504 palavras
com 6 pontos). A hipótese é que o lookahead padrão seja curto demais para o
modelo decidir o ponto. **É hipótese, não medição.**

**O que varrer:**

- `att_context_right` — o único ajuste que o `ParakeetStreamOptions` expõe
  (ver `VIVO-3`);
- o quadro — o modelo foi treinado com 80, 160, 320, 560 e 1120 ms. O porte
  para Apple do `smdesai` usa 560 a 4480 ms e reduz o contexto de atenção de
  [56,13] para [42,13], o que mostra que esses dois números mexem e o modelo
  aguenta.

**A régua, nos quatro eixos de uma vez**, sobre o mesmo áudio (o
`tools/reproduzir_legenda.py` repassa uma gravação como se fosse ao vivo):

1. pontos por mil palavras;
2. texto contra a passada final, como no `VIVO-4`;
3. latência até o texto firmar;
4. ciclo e VRAM ao lado do Meet, com o teto de ~33% da
   [FASE7-ROTA.md](FASE7-ROTA.md) §4.

Um lookahead maior compra pontuação com latência; **o item fecha escolhendo o
ponto dessa troca**, não maximizando um eixo só.

**O que destrava:** se a pontuação voltar, o `VIVO-1` pode quebrar o turno no
ponto final sem o Smart-Turn, e a ata que parte da legenda recebe frases.

---

## 8. Melhoria de modelos

Aberto em 25/09/2026 pelo dono do produto. **O `DIST-5` acha candidatos; este
tema é o que se faz com o que passou na régua dele** — medir contra o que temos,
portar para o formato do app, e trocar. A qualidade que o §10 deixa de fora é a
de *ajuste* (limiar, prompt, pós-processamento); trocar o modelo por um que
comprovadamente erra menos entra aqui, com medição antes.

### MOD-1 · O Nemotron-3 ao lado da legenda, na 2060 — `feature` · feito em 30/09/2026

> **Fechado com o Meet:** 3,3 GB com legenda, Nemotron ao vivo e Meet; pico de
> 5,7 GB com uma pergunta, sem a legenda atrasar
> ([NEMOTRON-DIARIZACAO.md](NEMOTRON-DIARIZACAO.md) §4c).

> **Medido em 29/09/2026, sem o Meet** ([NEMOTRON-DIARIZACAO.md](NEMOTRON-DIARIZACAO.md) §4b):
> o **`low_latency` cabe** — a legenda vai de 3,17× a 2,90×, o Nemotron fica
> no relógio, 3,7 GB somados. O **`ultra_low_latency` não**: a legenda cai a
> 0,78×. O que falta para fechar é **repetir o `low_latency` numa reunião de
> verdade, com o Meet aberto** — é a VRAM (3,7 GB + o Meet em 6 GB) que pode
> reprovar. Os cortes do passo 2 só voltam se ela reprovar.

**Gatilho: é o que falta para decidir o `MOD-2` e o `VIVO-7`.** O
`nvidia/Nemotron-3-Diarization` já está medido, portado e documentado
([NEMOTRON-DIARIZACAO.md](NEMOTRON-DIARIZACAO.md)): nas **quatro** reuniões com
Gemini ele acerta mais o falante que o pyannote de hoje: o erro cai de 41% a
56% no offline, e o streaming fica a até 0,4 ponto dele. Roda em onnxruntime
sem torch, que é o formato do sidecar, e o port decide 100% igual ao
`transformers`.

**O que não se sabe, e decide:** quanto ele custa **com a legenda rodando**. A
2060 é dividida com o Meet e com a legenda, e sozinho o streaming já ocupa ~10%
da placa em `low_latency` e ~25% em `ultra_low_latency`, com ~1,2 GB de VRAM
(a ferramenta chega a 2 GB em 49 min porque embute o arquivo inteiro de uma vez;
por bloco, não cresce).
Na passada final a pergunta é mais fácil — nada roda junto —, mas é a mesma
medição que diz se ele pode ficar residente.

**O item:**

1. medir a legenda e o Nemotron em streaming juntos, numa gravação do acervo:
   atraso da legenda, quadros perdidos, VRAM somada;
2. cortar o custo do passo antes de desistir de um modo: fp16 (o conversor
   quebra no RoPE — §2 do documento), IO binding, grafo CUDA;
3. repetir as quatro reuniões com Gemini, que é a régua (`tools/medir_nemotron3.py medir`).

**Critério de saída:** um modo de streaming em que a legenda não perde tempo
real, ou a conclusão escrita de que só a passada final cabe.

### MOD-2 · O Nemotron-3 ao lado do pyannote, na passada final — `feature` · `aberto` · medido

> **Medido em 29/09/2026** ([NEMOTRON-DIARIZACAO.md](NEMOTRON-DIARIZACAO.md) §3b),
> com o `system.wav` e o reconhecimento de vozes do app nos dois braços: o erro
> de separação cai de 3,5% para 1,7%, o nome certo sobe de 80,2% para 81,5%,
> e ele roda 18× mais rápido com ~0,55 GB contra ~4,4 GB. **O desenho decidido
> pelo dono do produto:** ele entra **ao lado** do pyannote, com duas escolhas
> independentes — na legenda (`VIVO-7`) e na passada final. O teto de 8
> falantes não pesa (6 de 111 reuniões, contagem inflada).
>
> **Passada final implementada em 29/09/2026:** o Nemotron entra como **um
> modelo a mais** em `motores/diarizacao/modelos/nemotron-3`, e não como motor
> — o seletor de Ajustes › Transcrição e o de cada projeto o oferecem sozinhos
> (`Motores.ModelosDeDiarizacao`). O `motor.py` o reconhece pelos quatro
> artefatos; o código é `motores/diarizacao/pipeline/nemotron3.py`, com o embed
> por bloco. O reconhecimento de vozes não muda: continua o wespeaker sobre os
> trechos de cada rótulo. Padrão continua `community-1`. No Python embarcado,
> 49 min em 25 s na GPU. **Falta:** a escolha independente na legenda
> (`VIVO-7`, depois da medição com o Meet do `MOD-1`).

**Gatilho: o `MOD-1` fechar.** O caminho é curto porque o reconhecimento de vozes
não depende do diarizador: o `vetor_de_voz` do sidecar recebe trechos, e com os
falantes do Nemotron ele reconheceu 7 de 8 pessoas contra o banco real (§5 do
documento). Nada muda no banco, no formato, nem nas vozes aprendidas.

**O que custa:** os 380 MB dos grafos no pacote de modelos, e a pergunta que o
pyannote não tinha — o teto de **8 falantes** por reunião. O acervo tem 99,7%
dos blocos de 5 min dentro dele ([FASE7-RESULTADOS.md](FASE7-RESULTADOS.md) §2);
por reunião inteira, falta contar.

### MOD-3 · O Parakeet Ultra como opção de transcrição — `feature` · feito em 27/10/2026 · opção, não padrão

> **Entrou como opção em Ajustes › Modelos › Transcrição**, pedida pelo dono do
> produto depois da medição. O padrão continua o `large-v3`.

**Por que ele.** Saiu na varredura do `DIST-5` de 26/10 e foi o único que passou
na régua de arquitetura: é um Parakeet TDT v3 retreinado (`moondream`), e
transducer dá carimbo de tempo. Medido nas seis reuniões com Gemini
(`tools/medir_asr_final.py`, 140 min, sem vocabulário nos dois, que é o padrão do
app), **pelo próprio `motores/asr/motor.py`**:

| | divergência | cobertura | trocadas | faltando | velocidade | VRAM |
|---|---|---|---|---|---|---|
| `large-v3` | 27,9% | 74,7% | 1.340 | 4.118 | 4,6× | 4,7 GB |
| **`parakeet-ultra`** | **25,9%** | **76,9%** | 1.434 | 3.558 | **7,9×** | **2,3 GB** |

Ganha nas seis, e o ganho é **perder menos fala**. Troca um pouco mais, e boa
parte disso é grafia formal ("está", "para" onde o Gemini escreveu "tá", "pra").
Nos nomes os dois erram — ele escreveu "lugar/hugar/hogar" para Algar —, e é o
que a `CorrecaoFonetica` com o vocabulário do projeto conserta, depois de
qualquer motor. Ele **aguenta reunião que mistura português com termo em
inglês**: na de 21/10 o Parakeet v3 original derivou para inglês e espanhol
(79% de divergência), e o ultra não.

**O falante não piora.** Com o pyannote de produção sobre o `system.wav` e o
trecho repartido pela palavra como o app faz (`tools/medir_mod2.py
--transcricoes`), as seis reuniões dão separação de 96,3% contra 96,2% e nome
certo de 68,7% contra 68,9% — sobre 351 palavras a mais, que são as que o
`large-v3` perde. **A régua tem de repartir pela palavra**: com um rótulo por
trecho, o ultra parecia perder até 4 pontos, porque devolve trechos maiores
(mediana de 3,3 s contra 1,8 s), e é exatamente o que o
`Montagem.RepartirPorFalante` desfaz.

**Como entrou.** No mesmo sidecar de ASR, e não como motor novo: o `motor.py`
reconhece o id pelo `GGUF` dele e roda o `transcribe.cpp` em vez do
faster-whisper, com o mesmo contrato de fio. O áudio vai em blocos de 5 min
cortados no quadro mais quieto (progresso, e memória que não cresce com a
duração), e os trechos são montados pela palavra, com o espaço na frente que o
C# espera. O GGUF (F16, 1,26 GB) é avulso em `motores/asr/modelos`, como os de
ata. O `transcribe.cpp` do app subiu da 0.2.3 para a 0.4.1, que é de onde ele
existe — e que deixou a legenda 18% mais rápida com o mesmo texto.

**O que ele não tem:** vocabulário na entrada (o `transcribe.cpp` não o expõe
para o Parakeet) e dica de idioma (ele detecta sozinho; a dica não muda uma
palavra). O vocabulário segue para a correção. Licença CC BY 4.0.

**O que falta para virar padrão:**

1. reunião de 2 h — a mais longa medida é de 49 min (o corte em blocos de
   5 min existe para que a duração não importe, mas não foi provado);
2. a régua de vocabulário com a correção aplicada, nos dois;
3. uso de verdade, pela tela.

**De quebra, um defeito antigo saiu:** remover um modelo de ata em Ajustes ›
Modelos apagava a pasta `motores/ata/modelos` inteira — todos os GGUF de ata,
inclusive o em uso. O `RemoverPacote` agora apaga só o arquivo do pacote nas
famílias de arquivo avulso.

---

## 9. Débito técnico e testes

### DEB-1 · O caminho da ata na ponte não tem teste — `débito` · `feito`

O núcleo tem testes de sobra e o caminho inteiro foi exercitado em reuniões
reais pela linha de comando, mas a `Ponte` é interna ao executável e a suíte não
a alcança — a mesma fronteira que a Fase 1 desenhou. O
[`Cli/GeradorDeAta.cs`](../app-net/Cli/GeradorDeAta.cs) faz o mesmo caminho e
dava para rodá-lo num teste com um motor falso.

**Já dói:** mexer no `GerarAta` hoje é mexer sem rede, e a ata mudou em três das
últimas quatro versões.

**Feito em 30/09/2026**, e não com um teste do CLI: o caminho saiu da ponte para
[`Nucleo/Atas/GeracaoDeAta.cs`](../app-net/Nucleo/Atas/GeracaoDeAta.cs), com o
motor como função, e a ponte e o CLI chamam o mesmo código
(`Tests/GeracaoDeAtaTests.cs`). **As duas cópias já tinham divergido**: a do app
punha o título e o vocabulário do projeto no prompt, e a do CLI — a que as
ferramentas de medição usam — não. E o vocabulário só era achado com
`vinculo.json`; agora cai no cliente e projeto da transcrição, como o resto do
contexto.

---

## 10. O que **não** entra

Para a lista não virar depósito de novo:

- **qualidade de transcrição, de diarização e de ata** — saiu deste backlog em
  27/08/2026 por decisão do dono do produto, e é tratada fora dele. A disposição
  item a item está em [FASE6.md](FASE6.md), no bloco de fechamento. **A troca de
  modelo, com medição antes, voltou em 25/09/2026 como tema 8**;
- **o que está na lista de "não se reabre"** do CLAUDE.md — a âncora de relógio,
  o resampler, o mute que escreve silêncio. Custaram caro para acertar, são
  invisíveis quando certos, e "melhorar de passagem" já perdeu em campo;
- **tool calling de verdade** no motor de ata. O protocolo reserva o campo, e num
  4B ele troca um problema resolvido por um não resolvido;
- **keep-alive do motor de ata.** O modelo carrega em 5 s; manter o processo vivo
  economiza isso e prende 2,5 GB que a próxima transcrição vai querer. Só faz
  sentido se gerar várias atas em sequência virar rotina;
- **o tempo real** — é estudo, tem carta própria e não manda fazer nada
  ([FASE7.md](FASE7.md)). A fila dos testes dela e a definição dos três produtos
  estão na [FASE7-FILA.md](FASE7-FILA.md); o que **saiu** do estudo e virou
  código foi a legenda ao vivo e a pergunta (tema 7). O motor MOSS opcional
  ([FASE7-BACKEND.md](FASE7-BACKEND.md)) saiu em 17/09/2026.
