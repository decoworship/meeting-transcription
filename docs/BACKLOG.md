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

### UI-2 · A tela de vozes — `feature` · `aberto`

Fila de revisão, play por amostra, ações por amostra (remover, mover, tirar da
quarentena) e indicador de saúde por perfil. O desenho está escrito em
[VOZES.md](VOZES.md) §6, e o modelo de dados já é o certo — falta a tela.

**Por que subiu de prioridade sozinha:** a decisão de 20/08/2026 pôs a geração 1
dos vetores fora de circulação. Hoje a biblioteca tem **três estados** para
explicar — quarentena, modelo antigo e geração antiga — e **só o primeiro pede
ação**. A tela mostra 48 amostras apagadas com um motivo em texto, e não há por
onde reagir a nenhuma delas.

### UI-3 · Busca e filtro nas listas — `feature` · `aberto`

Reuniões e Atas desenham **um cartão por gravação, sem corte, sem busca e sem
filtro** ([app.js:73](../app-net/App/web/app.js#L73),
[atas.js:52](../app-net/App/web/atas.js#L52)). A única busca do app está na tela
de revisão, dentro de uma transcrição
([revisao.js:117](../app-net/App/web/revisao.js#L117)).

**Já dói:** são 44 gravações no acervo, e o gatilho escrito era "umas trinta".
Achar a reunião de duas semanas atrás é rolar. Filtrar por cliente é o corte
óbvio — cliente e projeto já estão no cartão, justamente porque é por eles que
se procura.

### UI-4 · O tempo marcado na nota não leva ao áudio — `feature` · `espera`

`[00:12:34]` numa nota é texto. Na revisão, cada trecho já toca o áudio a partir
dele; a nota podia fazer o mesmo, e o dado já está lá.

**Gatilho:** usar "marcar momento" e querer ouvir aquele trecho.

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

### VOZ-1 · Sugerir fusão de perfis parecidos — `feature` · `espera`

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

## 7. Débito técnico e testes

### DEB-1 · O caminho da ata na ponte não tem teste — `débito` · `aberto`

O núcleo tem testes de sobra e o caminho inteiro foi exercitado em reuniões
reais pela linha de comando, mas a `Ponte` é interna ao executável e a suíte não
a alcança — a mesma fronteira que a Fase 1 desenhou. O
[`Cli/GeradorDeAta.cs`](../app-net/Cli/GeradorDeAta.cs) faz o mesmo caminho e
dava para rodá-lo num teste com um motor falso.

**Já dói:** mexer no `GerarAta` hoje é mexer sem rede, e a ata mudou em três das
últimas quatro versões.

---

## 8. O que **não** entra

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
