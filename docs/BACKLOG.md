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

### UI-1 · Uma passada de percurso, ponta a ponta — `feature` · `aberto`

Percorrer os quatro destinos com uma reunião real na mão — gravar, transcrever,
revisar, gerar ata, exportar — e anotar onde o app faz esperar, faz procurar, ou
não diz o que fez. A saída do item são **itens novos neste tema**, com o passo
em que doeu; não é um redesenho.

**Por que agora:** a Fase 5 pintou o app e mediu contraste, mas nunca mediu
percurso ([FASE5-HANDOFF.md](FASE5-HANDOFF.md) §2). O acervo passou de 44
gravações, e o uso deixou de ser "uma reunião por vez" — que é o regime em que
todas as telas foram desenhadas.

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

### UI-6 · Nada é anunciado — `bug` · `aberto`

**Não existe uma única região `aria-live` no app** — conferido em 27/08/2026 nos
`.js`, no `.css` e no `index.html`. Progresso da transcrição, erro da ponte e
fim de etapa são escritos no DOM em silêncio. O `aria-busy` das telas existe e
está certo, mas ele diz *"estou carregando"*, não *"terminou"* nem *"falhou"*.

Uma região só, no `index.html`, com o texto que a barra de progresso já compõe,
fecha a maior parte disto.

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

## 2. Ata: formato e saída

**Qualidade da ata não está aqui** — omissão, cobertura e escolha de modelo
saíram deste backlog em 27/08/2026 e são tratadas fora dele. O que fica é o que
a ata *é* e por onde ela *sai*.

### ATA-1 · Exportar em DOCX — `feature` · `aberto`

O botão Exportar copia o `.md`
([atas.js:196](../app-net/App/web/atas.js#L196)). A transcrição já sai em
TXT/SRT/VTT/DOCX, o [`Exportacao`](../app-net/Nucleo/Exportacao.cs) já escreve
DOCX à mão, e a ata já é estruturada — o caminho é curto.

**Já dói pela assimetria:** a ata é justamente o que sai para o cliente, e é a
única das duas saídas que não tem formato de escritório.

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

### GRA-1 · O `calendar_event_id` é escrito e nunca lido — `bug` · `aberto`

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

### GRA-2 · Nome e e-mail casados por posição — `bug` · `espera`

`attendees` e `attendee_emails` são listas paralelas com deduplicações
independentes; em teoria desalinham. Hoje o casamento por posição só acontece
quando o e-mail existe e o classificador usa o e-mail direto, então o risco é
baixo. **Vira problema no dia em que alguém cruzar as duas listas.**

**Gatilho:** um nome aparecer atribuído ao domínio errado.

### GRA-3 · Espelhar o mute do Teams — `feature` · `espera`

Por WebSocket local ([PLANO.md](PLANO.md) §2.1). Não depende de nada que as
fases recentes mudaram.

**Gatilho:** esquecer o mute do gravador de novo por causa do mute do Teams.

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

---

## 5. Diagnóstico e suporte

O tema existe porque o app roda na máquina de outra pessoa, e **software que
roda na máquina de outra pessoa precisa deixar rastro**. O bloco de diagnóstico
da Fase 4 dá a *foto* — versão, placa, modelos. A pergunta que faltou responder
era outra: **o que o app fez**.

### TRA-2 · Motores em paralelo, medidos no uso real — `feature` · `aberto`

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

### TRA-1 · A prévia ao vivo é jogada fora — `feature` · `aberto`

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

### SUP-1 · O app não sabe dizer o que aconteceu — `feature` · `aberto`

Prometido para a 0.4.1 e **nunca entregue** — conferido no código em
27/08/2026, com a versão em 0.6.1. São cinco instrumentos:

| instrumento | estado hoje |
|---|---|
| assinar o `AoRegistrar` em `AprendizadoDeVozes.ExtrairAsync` | **não existe** — é a única das **três** cargas de GPU do pipeline que não escreve uma linha, nem do motor |
| `Registro.Ultimas()` no bloco de diagnóstico | existe e **ninguém a chama** fora do teste ([Registro.cs:69](../app-net/Nucleo/Registro.cs#L69)) |
| marcador de transcrição em andamento, apagado ao terminar | ✅ **existe desde 10/09/2026** — `Nucleo/MarcaDeEtapa.cs`, escrito com `WriteThrough` a cada etapa e apagado no `finally` do `ExecutarAsync`. A órfã entra no log e no **bloco colável** do diagnóstico |
| ler o Event Log (`6008`, `Kernel-Power 41`, `BugCheck 1001`, `Kernel-Boot 27`) | **não existe** — nenhum `EventLogReader` na árvore |
| amostrar o `nvidia-smi` a cada ~15 s durante a transcrição | **não existe** — [Diagnostico.cs](../app-net/Nucleo/Diagnostico.cs) faz uma chamada só, com *"não chame em laço"* escrito nela |

**Três dos cinco existem.** Os dois primeiros eram de uma linha cada, e valem mais que os outros dois: é
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

### DIST-1 · Separar os motores do instalador — `feature` · `aberto`

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

### VIVO-1 · A legenda não carimba o turno — `feature` · `aberto`

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

### VIVO-2 · Diarizar a legenda depois da reunião, em segundo plano — `feature` · `espera`

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

## 8. Débito técnico e testes

### DEB-1 · O caminho da ata na ponte não tem teste — `débito` · `aberto`

O núcleo tem testes de sobra e o caminho inteiro foi exercitado em reuniões
reais pela linha de comando, mas a `Ponte` é interna ao executável e a suíte não
a alcança — a mesma fronteira que a Fase 1 desenhou. O
[`Cli/GeradorDeAta.cs`](../app-net/Cli/GeradorDeAta.cs) faz o mesmo caminho e
dava para rodá-lo num teste com um motor falso.

**Já dói:** mexer no `GerarAta` hoje é mexer sem rede, e a ata mudou em três das
últimas quatro versões.

---

## 9. O que **não** entra

Para a lista não virar depósito de novo:

- **qualidade de transcrição, de diarização e de ata** — saiu deste backlog em
  27/08/2026 por decisão do dono do produto, e é tratada fora dele. A disposição
  item a item está em [FASE6.md](FASE6.md), no bloco de fechamento;
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
  código é o motor MOSS opcional ([FASE7-BACKEND.md](FASE7-BACKEND.md)), que é
  transcrição depois da reunião como sempre foi — não ao vivo.
