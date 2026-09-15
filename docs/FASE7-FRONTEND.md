# Fase 7 — a interface do tempo real, e o que ela precisa antes

Escrito em 03/09/2026, a pedido do dono do produto, depois de a rodada 0 fechar
com número ([FASE7-RESULTADOS.md](FASE7-RESULTADOS.md)).

**Este documento não manda fazer nada**, pela mesma razão da
[FASE7.md](FASE7.md): o tempo real ainda é estudo. Ele faz três coisas —
**revisa o planejamento** contra o que a medição descobriu, **audita a interface
que existe** contra o que ela vai ter de aguentar, e **escreve o que muda**, tela
por tela e arquivo por arquivo, para o dia em que o backend for definido.

A conclusão que atravessa as três: **a interface é hoje o item mais atrasado da
Fase 7, e é o único que dá para adiantar sem GPU, sem C# e sem risco à
gravação.** O §11 mostra como — com uma ferramenta que já existe no repositório.

> **As dez decisões de interface foram tomadas em 04/09/2026** e estão no
> **§12**. Daqui em diante o que este documento chamava de recomendação é
> desenho — menos a `D3`, que voltou decidida ao contrário e por isso tem
> parágrafo próprio.

---

## 1. Revisão do planejamento

### 1.1 O que a rodada 0 fez com a carta

A carta foi escrita em 27/08; a medição rodou de 28/08 a 03/09. **Quatro das suas
seis posições mudaram de estado**, e **nenhuma delas está corrigida no texto da carta**
— quem ler a [FASE7.md](FASE7.md) hoje, sozinha, decide errado em três pontos.

| na carta | o que a medição fez | estado |
|---|---|---|
| **§3.1** — Sortformer, e o teto de 4 falantes como risco principal (43% das gravações) | a janela cumulativa (RESULTADOS §5) tirou o teto da conta: rediarizar tudo desde o começo **não tem teto de falantes**, e o pyannote de hoje custa 32× o tempo real | **esvaziado** — a §2.1 dos resultados já diz isso, a carta não |
| **§3.2** — Whisper em janela deslizante com LocalAgreement | não foi medido, e **deixou de ser necessário para o produto A**: o bloco de 3 min usa o mesmo `faster-whisper`, com os mesmos parâmetros, sem política de emissão nenhuma | **adiado** para a rodada 4 |
| **§3.3** — Multitalker Parakeet, só inglês | nada mudou | **válido** |
| **§5** — a ordem de medir: (1) exportar Sortformer para ONNX … (5) só então decidir | o teste que decidiu tudo — *o pipeline de hoje, em blocos, entrega texto pior?* — **não estava na lista**. O passo 1 nunca foi feito e já não é caminho crítico | **a ordem estava errada** |
| **§7** — "não deixar a prévia ao vivo escrever no `transcricao.json`" | os resultados §4 abriram a exceção: o parcial por bloco é **do mesmo modelo com os mesmos parâmetros**, então não é o defeito de 0.4.0 de volta. Vale **só para o texto** | **corrigido nos resultados, não na carta** |
| **§4.1** — o desligamento do segundo usuário (`SUP-2`) | nada. O T0.2 mediu a integridade da gravação sob carga **na 2060 do dono**, não na RTX 4050 que desliga | **intacto, e continua mandando** |

Duas leituras que valem ser ditas em voz alta:

**O gatilho que a carta julgou mais provável não disparou, e o motivo é irônico.**
A §6 apostava que trocar a diarização offline pelo Sortformer era o mais provável
de acontecer. O que aconteceu foi o contrário: medir o pyannote de perto
(32× o tempo real, linear, sem termo quadrático) foi justamente o que **tornou
desnecessário trocá-lo**.

**O argumento mais forte contra a fase segue de pé, e não é técnico.** Hoje o
desligamento custa uma transcrição, e a retomada devolve. Ao vivo custaria a
reunião. Nada na rodada 0 tocou nisso, e o `SUP-1` — o registro que diria *o que
o app estava fazendo quando a máquina caiu* — continua não existindo. **Enquanto
o `SUP-1` não existir, ligar qualquer coisa ao vivo na máquina do segundo usuário
é apostar sem instrumento.**

### 1.2 Os cinco buracos do planejamento

**1. A fila não existe escrita.** `T0.1`, `T0.2`, `T0.3`, `T0.3b`, `T0.4`,
`T1.1`–`T1.3`, `T2.1`, `T3.1` e `T4.1`–`T4.5` aparecem numa tabela de *estado*
nos resultados e em docstrings de `tools/`. **Nenhum documento os define.** Não
há como saber o que `T4.3` é senão perguntando a quem escreveu.

É, em escala menor, o defeito que matou a Fase 6 e que o
[BACKLOG.md](BACKLOG.md) abre explicando: uma lista que mora onde não se procura
por ela cresce até ninguém conseguir responder *o que faço agora*. A diferença é
que aqui ainda são onze itens — cabe numa página, hoje, de graça.

**2. "Produto A" é citado como se estivesse definido.** Os resultados dizem *"o
que a fila da Fase 7 chama de produto A"* (§4) e *"O produto A — a consciência da
reunião, com o LLM respondendo sobre o que já foi dito e o falante ganhando nome
cedo"* (§4). É a única definição que existe, e ela está numa frase de passagem.
Não há produto B nem C escritos, embora `T2.1` e `T3.1` claramente sejam outros
produtos — este documento os separa no §6 e pode estar separando errado.

**3. Três dias de medição não estão no git.** `docs/FASE7-RESULTADOS.md` e sete
ferramentas (`simular_blocos.py`, `medir_bloco_asr.py`,
`medir_bloco_diarizacao.py`, `medir_janela_cumulativa.py`, `carga_de_bloco.py`,
`medir_moss.py`, `medir_nomear_cedo.py`) estão **untracked**. São ~85 min de GPU
no T0.1, ~25 no T0.3b, ~40 no T0.4, ~40 no T3.1 e a rodada inteira do MOSS. Um
`git clean` distraído apaga a semana.

**4. O documento se chama "rodada 0" e já contém as rodadas 1 e 3.** O §7 é
`T1.x` (MOSS) e o §8 é `T3.1` (nomear cedo). O título mente sobre o próprio
conteúdo, e a tabela do §6 é a única coisa que orienta.

**5. Nenhuma das onze perguntas é sobre a tela.** O estudo mediu texto, falante,
custo, VRAM, deriva e cobertura — e não perguntou uma vez o que a pessoa vê, nem
quanto custa desenhá-lo. É o buraco que este documento existe para tapar.

### 1.3 O que os números já decidem, sem medir mais nada

Cruzando a rodada 0 com o que a [ATA.md](ATA.md) §8 já tinha medido, três
restrições saem prontas — e as três são de desenho de interface, não de motor.

**A placa serializa, e a tela vai ter de contar isso.** Numa 2060 de 6.144 MiB:

| carga | VRAM medida | onde |
|---|---|---|
| ASR `large-v3` fp16 | ~3,1 GB | RESULTADOS §7.3 |
| motor de ata (Qwen3-4B Q4_K_M + KV) | 4.512 a 5.727 MiB, conforme o contexto | ATA.md §8 |
| pyannote (janela cumulativa) | soma-se ao ASR | — |
| o Meet codificando vídeo | não medido, e é real | FASE7 §4.2 |

**O motor de ata sozinho quase enche a placa.** ASR e ata **não cabem juntos**, e
isso não é um detalhe de implementação: é a razão pela qual a tela do tempo real
precisa mostrar uma **fila**, e não três indicadores girando em paralelo. Uma
pergunta feita ao vivo **para o ASR do bloco seguinte**. Quem desenhar isso como
se fossem coisas independentes vai desenhar uma mentira.

**O orçamento de GPU já está quase todo comprometido.** ASR em bloco de 3 min
ocupa 15–24% (RESULTADOS §1.3); a diarização cumulativa com cadência de 9 min
ocupa ~21% aos 60 minutos (§5.2). São **~41% da placa** para o produto A sozinho,
antes de qualquer LLM e com o Meet já rodando.

**A latência da consciência é de 3 minutos, mais o bloco.** O texto de uma frase
dita no minuto 10 aparece na tela entre o minuto 12 e o 13 — o bloco fecha aos 12
e leva 14 a 40 s para ser transcrito. **Isso não é "tempo real"** no sentido em
que a palavra é usada na demonstração da NVIDIA, e a tela não pode sugerir que
seja. É *consciência da reunião*, e o nome importa: quem espera legenda e recebe
bloco de três minutos acha que está quebrado.

---

## 2. As três perguntas que a carta não fez, e a tela obriga

### 2.1 O texto se reescreve debaixo do olho — e ninguém desenhou esse momento

Os resultados §1.1 mediram a divergência entre o texto por blocos e o da passada
inteira: **11,53% no bloco de 3 minutos**. A §1.2 provou que isso não é perda de
qualidade — as duas variantes divergem do Gemini na mesma medida. Para o
*pipeline*, o assunto está encerrado.

Para a *tela*, começa aqui. Quando a reunião acaba e a passada offline roda,
**uma palavra em cada nove muda** no texto que a pessoa passou uma hora vendo. Se
ela leu, procurou, ou anotou em cima daquele texto, a substituição é um evento de
interface que hoje não tem desenho, não tem aviso e não tem nome.

Três saídas, e a escolha não é minha:

1. **substituir em silêncio.** É o mais fácil e o mais desonesto: quem viu uma
   frase e não a acha mais conclui que o app perdeu;
2. **substituir e dizer**, com uma linha ("o texto final substituiu a prévia; 148
   trechos mudaram") e a possibilidade de ver o quê. A revisão já tem a máquina
   para isso — o filtro `✎ N correções` de [revisao.js:233](../app-net/App/web/revisao.js#L233)
   marca e conta trocas feitas por cima do texto do modelo;
3. **não substituir**: a prévia é riscada e some, e o texto final entra como
   coisa nova. Honesto, e joga fora a continuidade de leitura.

**A (2) é a que o app já sabe fazer**, e é a única que respeita a regra que o
projeto aplicou à correção fonética: *palpite tem que poder ser conferido*.

### 2.2 A pergunta ao vivo custa a placa duas vezes, e o backlog já recusou o conserto

O `T2.1` — "perguntar ao LLM sobre a reunião" — está na fila como um teste. Os
números para orçá-lo **já existem**, na [ATA.md](ATA.md) §8:

- o modelo **carrega em 5 s**;
- processa prompt a **1.500–1.600 t/s** em contexto curto, **866 t/s** em 49k;
- gera a **44 t/s** em contexto curto, **17 t/s** em 49k;
- uma reunião de 1 h são **14–17,5k tokens** de transcrição.

Uma pergunta no minuto 45 de uma reunião de 1 h, com o transcrito inteiro no
prompt: **5 s de carga + ~10 s de prompt + 5 a 20 s de resposta**. Vinte a
quarenta segundos, com a placa inteira ocupada — o que **para o bloco de ASR** que
estaria rodando.

E aqui o estudo esbarra numa decisão já tomada: o [BACKLOG.md](BACKLOG.md) §8
lista **"keep-alive do motor de ata"** entre o que *não entra*, com o motivo
escrito — "manter o processo vivo economiza [5 s] e prende 2,5 GB que a próxima
transcrição vai querer" — e o gatilho — "só faz sentido se gerar várias atas em
sequência virar rotina".

**Perguntar ao vivo é exatamente um gatilho novo para o keep-alive**, e ele não
está registrado. Só que agora os 2,5 GB não competem com "a próxima transcrição":
competem com o bloco de ASR de daqui a três minutos, na mesma placa, durante a
reunião. **A conclusão do backlog continua certa e o motivo dela virou outro.**

Duas medições baratas que ninguém fez e que decidem o produto B:

- **o cache de prompt do `llama.cpp`.** O transcrito cresce por acréscimo, então o
  prefixo do prompt é sempre o mesmo — é o caso ideal de reaproveitamento de KV.
  Se funcionar, a segunda pergunta custa uma fração da primeira, e o produto B
  muda de categoria;
- **quanto de transcrito basta.** Perguntar sobre os últimos 15 minutos não
  precisa dos 45 anteriores. Um recorte muda o custo por um fator de três.

### 2.3 O bloco que sobra, o mute, e a reunião que acaba no meio

Três casos de borda que a medição não tocou porque são de produto:

- **o último bloco nunca é processado ao vivo.** Uma reunião de 50 min tem 16
  blocos de 3 min e sobram 2 min que só a passada final vê. A tela vai ter um
  rabo de até 3 minutos sem texto no momento exato em que a pessoa clica em
  encerrar — e é o momento em que ela mais olha;
- **mute escreve silêncio, não interrompe a escrita** (CLAUDE.md, "o que não se
  reabre"). Um bloco inteiro mudo é um bloco de silêncio válido, e o ASR devolve
  vazio. A tela precisa saber a diferença entre *"não falaram"* e *"ainda não
  processei"*, e hoje ela não tem como;
- **a reunião que acaba durante um bloco** deixa um arquivo parcial. Descartá-lo
  perde até 3 minutos de fala da prévia; processá-lo cria um bloco curto cuja
  diarização é a pior possível (§3 dos resultados: o bloco curto funde pessoas).
  A resposta provável é *descartar a prévia e deixar a passada final resolver* —
  mas é uma decisão, e ela some se ninguém a escrever.

---

## 3. A auditoria do frontend — o que existe

### 3.1 Inventário

`app-net/App/web/`, ~216 KB em 13 arquivos (10 deles `.js`), sem framework, sem build, sem
dependência externa. Servido de dentro do executável (`App/Conteudo.cs`).

| arquivo | tamanho | papel |
|---|---:|---|
| [configuracoes.js](../app-net/App/web/configuracoes.js) | 60 KB | Ajustes, seis abas |
| [app.js](../app-net/App/web/app.js) | 37 KB | moldura, roteador, lista, preparo, acompanhamento |
| [app.css](../app-net/App/web/app.css) | 36 KB | tudo o que não é do design system |
| [revisao.js](../app-net/App/web/revisao.js) | 25 KB | ler, corrigir, nomear, exportar |
| [gravador.js](../app-net/App/web/gravador.js) | 21 KB | o gravador visto de dentro da janela |
| [atas.js](../app-net/App/web/atas.js) | 13 KB | escolher, gerar e ler ata |
| [index.html](../app-net/App/web/index.html) | 7 KB | casca: sprite, trilho, barra, `<main>`, 3 gavetas, 1 `<audio>`, 1 `<dialog>` |
| [notas.js](../app-net/App/web/notas.js) | 6 KB | o editor de notas, usado em dois lugares |
| [pecas.js](../app-net/App/web/pecas.js) | 6 KB | alerta, campo, seção, gaveta, paleta, parar áudio |
| [transcricoes.js](../app-net/App/web/transcricoes.js) | 4 KB | o registro do núcleo, espelhado |
| [ponte.js](../app-net/App/web/ponte.js) | 2,4 KB | pedido/resposta + eventos `id: 0` |
| [trilho.js](../app-net/App/web/trilho.js) | 2,6 KB | as bolinhas de "está acontecendo" |
| [ds.css](../app-net/App/web/ds.css) | 0,6 KB | os três `@import` do AA Design System |

### 3.2 O que está certo — e é o que torna o tempo real barato

Vale registrar antes das falhas, porque **quatro decisões já tomadas são
exatamente o substrato de que o tempo real precisa**, e nenhuma delas foi tomada
pensando nisso:

- **o canal de eventos empurrados existe e funciona.** `id: 0` + `assinar(tipo)`
  em [ponte.js:23](../app-net/App/web/ponte.js#L23) já carrega o nível de áudio
  cinco vezes por segundo. Um bloco transcrito vai pelo mesmo cano, sem inventar
  nada;
- **a disciplina de assinatura já está resolvida.** Todo assinante devolve o
  cancelador, e todo desenho guarda `if (!raiz.isConnected) { cancelar(); return; }`.
  É o defeito clássico de tela ao vivo — medidor que "volta a se mexer sozinho" —
  e ele já foi pago;
- **o registro do núcleo espelhado num módulo só** ([transcricoes.js](../app-net/App/web/transcricoes.js))
  é o padrão certo para uma sessão ao vivo, e existe pelo mesmo motivo: até a
  Fase 3, a transcrição morava na tela que a começou;
- **zero `innerHTML` no app inteiro** — conferido em 03/09/2026 nos dez `.js`.
  Tudo é nó construído. Com CSP `default-src 'none'`, `script-src 'self'`, sem
  `'unsafe-inline'` e `connect-src 'none'`, a superfície é honestamente pequena.

Some-se a isso o que o núcleo já entrega e que o tempo real precisa: o
`CrashSafeWavWriter` abre com `FileShare.Read` e mantém o header válido a cada
10 s ([CrashSafeWavWriter.cs:59](../app-net/Gravacao/CrashSafeWavWriter.cs#L59)).
**Um segundo processo já pode ler o áudio enquanto ele é gravado.** É a peça mais
difícil do tempo real, e ela está pronta desde a Fase 1 por outro motivo.

---

## 4. O que está errado hoje — independente de tempo real

Doze itens, na ordem do que custa mais. Os que já têm id no
[BACKLOG.md](BACKLOG.md) aparecem apontando para lá; os outros são novos.

| id | o quê | tipo | ao vivo fica |
|---|---|---|---|
| ~~**F-1**~~ | as cores de falante falham no tema escuro | `resolvido` | **fechado em 04/09** — paleta nova de 8, §12.3 |
| **F-2** | a revisão redesenha a transcrição inteira a cada tecla | `bug` | **impeditivo** |
| **F-3** | nada é anunciado, e o foco nunca se move (= `UI-6`) | `bug` | pior |
| **F-4** | as gavetas não são modais | `bug` | igual |
| **F-5** | nove `alert()`/`confirm()` nativos | `débito` | igual |
| **F-6** | `pedir()` não tem prazo nem cancelamento | `bug` | pior |
| **F-7** | dois `await` sem guarda em `abrirGravacao` | `bug` | igual |
| **F-8** | o aviso de versão cai na tela errada | `bug` | igual |
| **F-9** | ids de campo são globais e colidem entre telas | `débito` | pior |
| **F-10** | o roteador de `hash` é de mão única | `débito` | pior |
| **F-11** | `configuracoes.js` com 60 KB num módulo | `débito` | pior |
| **F-12** | o padrão da ponte é "empurrar o estado inteiro" | `débito` | **impeditivo** |

### F-1 · As cores de falante falham no tema escuro — `bug` · novo

A paleta de [pecas.js:125](../app-net/App/web/pecas.js#L125) tem seis cores. **A
primeira é token (`var(--cor-acao)`) e as outras cinco são hexadecimais fixos** —
e o app tem tema claro e escuro, aplicado pelo núcleo em
[Conteudo.cs:117](../app-net/App/Conteudo.cs#L117).

Contraste medido contra os dois fundos do design system:

```
                      claro (#FAF7F1)   escuro (#1A1714)
--cor-acao  ("You")        6,01:1            7,74:1   ← token, acompanha o tema
#8a6d3b     marrom         4,53:1            3,68:1
#4a7c59     verde          4,55:1            3,67:1
#8c4a5f     vinho          6,02:1            2,77:1
#3d6d8a     azul           5,23:1            3,19:1
#7a5c9e     roxo           5,10:1            3,27:1
```

**No claro as seis passam em AA (4,5:1). No escuro, as cinco fixas falham** — e o
vinho fica em 2,77:1, abaixo até do 3:1 de texto grande. O `--cor-acao`, que é
token, sobe de 6,01 para 7,74 sozinho.

O efeito prático: **no tema escuro, o seu nome é legível e o de todo mundo não.**
Vale no nome do trecho, no filtro por falante e na barra de participação da
gaveta — os três lugares em que o falante é a informação.

Isto **não** é o `UI-9` do backlog, que é sobre os medidores de gravação nunca
terem entrado na varredura de contraste. Estes escaparam por outro motivo: são
`style.color` posto por JavaScript, e a varredura da Fase 5 leu CSS.

A paleta também **cicla com cinco cores**, então da sexta pessoa em diante duas
dividem a mesma — cinco gravações do acervo têm 9 falantes ou mais (FASE7 §3.1).

**E o design system já tem uma paleta categórica, com oito cores e valor nos dois
temas** (`--dados-1` … `--dados-8`, em `assets/ds/colors_and_type.css`, com
*"use NESTA ORDEM"* escrito nela). O `pecas.js` inventou a sua em vez de usá-la.

**Só que trocar uma pela outra não conserta — conferido em 03/09/2026.** A
`--dados-*` é paleta de **gráfico**, para preenchimento e marca, e o próprio
comentário do arquivo diz isso. Como **texto**, ela reprova no tema claro, e pior
que a atual:

```
como texto        claro (#FAF7F1)   escuro (#221F1A)
--dados-1              6,01:1            5,57:1
--dados-2              3,10:1            6,94:1
--dados-3              3,42:1            6,81:1
--dados-4              2,19:1            8,97:1   ← ocre sobre areia
--dados-5 a -8    4,07 a 4,22:1     6,00 a 6,39:1
```

Os valores escuros passam com folga; os claros não. Ou seja: **a `--dados-*` é o
inverso exato do problema de hoje** — a paleta do `pecas.js` foi escurecida à mão
para o tema claro e nunca ganhou a versão escura; a do design system foi feita
para preencher, não para escrever.

Daí saíram dois consertos possíveis — tokens próprios `--falante-1` … `--falante-8`
com valor por tema, ou **a cor virando marca**: pastilha colorida antes do nome,
e o nome em `--cor-texto`, que faria a `--dados-*` servir como está nos dois temas.

> **Decidido em 04/09/2026** (§12.1 e §12.3), em dois tempos. Primeiro: **o nome
> continua colorido** — *"separa do texto da transcrição"* —, então a alternativa
> da pastilha foi recusada. Depois: **a paleta foi refeita a partir da
> `--dados-*`**, com oito cores e valor nos dois temas, e as dezesseis passam AA.
> **O F-1 está fechado**, e o parágrafo acima vale como registro do que havia.

### F-2 · A revisão redesenha a transcrição inteira a cada tecla — `bug` · novo

[revisao.js:121](../app-net/App/web/revisao.js#L121): cada `input` na busca chama
`redesenhar()`, que chama `desenharFiltros()` **e** `desenharTrechos()`. O segundo
faz `corpo.replaceChildren()` e reconstrói **todos** os trechos, quatro nós cada.

A reunião de 122 min do acervo tem **2.058 trechos** ([ATA.md](ATA.md) §8) — algo
como 8.000 nós, destruídos e recriados **a cada tecla digitada**, sem
virtualização e sem espera. `ordemDosFalantes` roda três vezes por redesenho e é
`O(n·m)`, com `includes` num vetor por segmento. E `desenharFiltros` reconstrói
os botões de filtro, então o foco de quem estiver navegando por Tab entre eles
cai no chão a cada letra.

**Isso já dói hoje**, na maior reunião do acervo. Ao vivo é impeditivo, e por um
motivo que não é o mesmo: a lista ao vivo **cresce sem teto durante a reunião**, e
qualquer redesenho total num bloco de 3 minutos é um engasgo visível na tela do
app que está gravando a reunião.

**É o item que mais paga**, porque o conserto é uma peça só — uma lista
virtualizada com acréscimo incremental — e ela serve às **duas** telas. Ver §7.

### F-3 · Nada é anunciado, e o foco nunca se move — `bug` · = `UI-6`

Conferido em 03/09/2026: **zero ocorrências de `aria-live`** nos dez `.js`, no
`.css` e no `index.html`. O backlog já registra isto como `UI-6`; a auditoria
acrescenta a metade que falta.

Além de nada ser anunciado, **o foco nunca se move ao trocar de tela**.
`cabecalho()` em [app.js:51](../app-net/App/web/app.js#L51) troca o `<h1>` e o
subtítulo, e o foco continua no botão do trilho. Para quem navega por teclado ou
lê por leitor de tela, clicar em "Atas" não produz nenhum sinal de que a tela
mudou.

Ao vivo isso vira um problema com dois lados opostos, e é por isso que ele não é
"acrescentar uma região e pronto": **anunciar cada bloco que chega é pior que não
anunciar nada.** Um `aria-live="polite"` sobre a transcrição ao vivo leria a
reunião inteira em voz alta, por cima da reunião. A política tem de ser
desenhada: anunciar mudança de *estado* (bloco processando, prévia substituída,
falante reconhecido), nunca o *conteúdo*.

### F-4 · As gavetas não são modais — `bug` · novo

`abrirGaveta` ([pecas.js:113](../app-net/App/web/pecas.js#L113)) faz
`hidden = false` no `<aside>` e no véu. **Não move o foco para dentro, não põe
`aria-modal`, não prende o Tab, não devolve o foco ao fechar, e não torna o fundo
inerte.** O Tab de dentro da gaveta caminha para a página atrás dela, que está
visualmente coberta pelo véu.

`Escape` fecha — e é o **único atalho de teclado do app inteiro** (`UI-5`).

O `<dialog id="modal-segmento">` do `index.html`, esse sim, é modal de verdade,
porque usa `showModal()`. A casa já tem o elemento certo; as gavetas não o usam.

### F-5 · Nove `alert()`/`confirm()` nativos — `débito` · novo

Dois em [app.js](../app-net/App/web/app.js), dois em
[revisao.js](../app-net/App/web/revisao.js), cinco em
[configuracoes.js](../app-net/App/web/configuracoes.js). No WebView2 eles
aparecem como diálogos do Edge, com o título do executável, fora do design
system, e **bloqueiam o laço de mensagens** — que neste app é o mesmo laço da
bandeja (`Aplicacao.cs`).

As três confirmações que destroem trabalho — apagar gravação, transcrever de
novo, desfazer correções — são justamente as que mereciam a caixa do app. O
`<dialog>` já está no `index.html`; falta uma função `confirmar()` em `pecas.js`.

### F-6 · `pedir()` não tem prazo nem cancelamento — `bug` · novo

[ponte.js:61](../app-net/App/web/ponte.js#L61): a promessa fica em `pendentes`
até chegar resposta. **Se o núcleo não responder, ela nunca resolve** — e a tela
fica em "gerando…" ou "salvando…" para sempre, sem erro e sem saída. Não há
`AbortController`, então um pedido caro não pode ser abandonado.

Hoje isso é raro porque o núcleo responde tudo. Ao vivo deixa de ser: uma
pergunta ao LLM que o usuário desistiu de esperar precisa poder ser abandonada, e
uma sessão ao vivo que morreu precisa ser detectável pela ausência.

### F-7 · Dois `await` sem guarda — `bug` · novo

`abrirGravacao` ([app.js:762](../app-net/App/web/app.js#L762)) tem
`await pedir("config")` e `await pedir("transcricao")` **fora de qualquer
`try`**. Uma rejeição da ponte vira `unhandled rejection`, e a tela fica parada
em "carregando a transcrição…" sem dizer nada. O resto do arquivo trata erro com
cuidado; estes dois escaparam.

### F-8 · O aviso de versão cai na tela errada — `bug` · novo

`avisarDeVersaoNova` ([app.js:160](../app-net/App/web/app.js#L160)) pede
`atualizacao` e, quando a resposta chega, faz `tela.prepend(linha)`. `tela` é o
`<main>` permanente — **sempre conectado**. Trocar de destino enquanto a
consulta corre faz o aviso "Saiu a versão X" aparecer no Gravador ou nos
Ajustes. `avisoDeVersaoDispensado` protege contra dispensa, não contra navegação.

### F-9 · Ids de campo são globais e colidem — `débito` · novo

`campo(..., { id })` e `campoComSugestoes(rotulo, id, ...)` escrevem `id` no
documento, e as telas leem por `getElementById`. A tela de preparo usa `cliente`
e `projeto`; a gaveta de exportação usa `exp-cliente` e `exp-projeto` —
**prefixadas exatamente para não colidir**, o que mostra que o autor já bateu
nisto. Continua sendo convenção, não mecanismo, e uma tela ao vivo coexistindo
com outras aumenta a chance de repetir o acidente.

### F-10 · O roteador de `hash` é de mão única — `débito` · novo

`inicio()` lê `location.hash` **uma vez, na subida**
([app.js:874](../app-net/App/web/app.js#L874)). Navegar depois não atualiza o
hash, e não há ouvinte de `hashchange`. Não há voltar, não há endereço para uma
tela, e o único consumidor é o `--tela` das capturas. "Voltar para a reunião ao
vivo" quer ser um endereço.

### F-11 · `configuracoes.js` tem 60 KB num módulo — `débito` · novo

Seis abas, ~1.500 linhas, um arquivo. Uma aba "Tempo real" — que o §6 mostra ser
necessária, e que nasce desligada — piora isso.

### F-12 · O padrão da ponte é "empurrar o estado inteiro" — `débito` · novo

`EmpurrarTranscricoes` ([Ponte.cs:1074](../app-net/App/Ponte.cs#L1074)) e
`EventoDoGravador` ([Ponte.cs:934](../app-net/App/Ponte.cs#L934)) serializam o
**estado completo** a cada evento. Está certo hoje: os dois estados são pequenos
e cabem em centenas de bytes, cinco vezes por segundo.

**Uma transcrição não é pequena.** Empurrar o transcrito inteiro a cada bloco é
`O(n²)` ao longo da reunião, com `JSON.parse` na thread que desenha. O evento ao
vivo precisa ser **incremental por construção** — "chegou o bloco 12, aqui estão
seus 41 trechos" —, e isso é uma decisão de contrato, não de otimização: ela tem
de estar certa antes da primeira linha de C#.

---

## 5. O que o tempo real quebra

Quatro invariantes escritas no código de hoje deixam de valer. Cada uma está
comentada em português no arquivo, o que torna a quebra fácil de achar e fácil de
esquecer.

**1. "Gravar não disputa nada com os motores".**
[trilho.js:15](../app-net/App/web/trilho.js#L15) diz, literalmente: *"**Gravar não
disputa nada com os motores** (capturar áudio não usa GPU), então a bolinha do
Gravador pode conviver com qualquer uma das outras duas."* Com o produto A, gravar
**é** transcrever. A premissa some, e com ela a regra de composição das bolinhas.

**2. "As outras duas nunca convivem entre si — o núcleo recusa".** Mesmo arquivo.
Reuniões e Atas nunca acendem juntas porque os modelos não cabem na placa. Ao
vivo há três consumidores de GPU (bloco de ASR, janela de diarização, LLM da
pergunta) e uma placa. **A recusa vira fila**, e fila é coisa que se mostra.

**3. Um `<audio>` para o app inteiro, e `estado` de módulo em `revisao.js`.**
O elemento é único de propósito ([index.html](../app-net/App/web/index.html)), e
`estado` é uma variável de módulo lida pelo `close` do `<dialog>`
([revisao.js:421](../app-net/App/web/revisao.js#L421)). Hoje só existe uma
transcrição aberta por vez. O tempo real cria a segunda: rever a reunião de ontem
enquanto a de hoje corre é o caso normal, não o exótico.

**4. A prévia e a retomada.** Os resultados §4 abriram a porta para o texto por
bloco virar o parcial de `Nucleo/Retomada.cs`. Se isso for feito, a regra da
retomada — *modelo, idioma e vocabulário conferidos, e na dúvida roda de novo* —
passa a valer também para blocos, e **a diarização por bloco continua sendo
rascunho** (4% de erro contra a passada inteira). São dois graus de confiança
diferentes no mesmo arquivo, e a tela precisa dos dois.

---

## 6. O desenho, por produto

Os três produtos que a fila implica. **Os nomes A, B e C são deste documento** —
só o A tem definição anterior, e ela é uma frase de passagem (§1.2).

### 6.1 Produto A — a consciência da reunião

**O que é.** Durante a reunião, o texto do que já foi dito aparece na tela, em
blocos de 3 minutos, com o falante identificado. É o que a rodada 0 mediu e
liberou: **não depende de modelo novo nenhum**.

**Onde mora.** No Gravador, e não num destino novo. É a tela que já está aberta
durante a reunião, já mostra os medidores, já tem as notas e já sabe qual reunião
da agenda está sendo gravada. Um quinto destino no trilho seria um lugar a mais
para procurar durante a única hora em que não se pode procurar nada.

**O que ela precisa, e que não existe:**

- **uma lista virtualizada com acréscimo.** É a mesma peça do F-2, e essa é a
  razão de o F-2 vir primeiro: escrever a lista ao vivo separada da lista da
  revisão cria duas listas que divergem. Uma peça, dois usuários;
- **o "seguir" que se solta.** A lista rola sozinha enquanto a pessoa está no fim;
  no instante em que ela rola para trás para reler, o seguimento para e aparece um
  "voltar ao vivo". Sem isso, ler o que foi dito há dez minutos é impossível
  durante a reunião — que é **o motivo pelo qual a carta diz que este produto vale
  a pena** (FASE7 §1);
- **três estados por bloco, visíveis:** *aguardando* (áudio gravado, ainda não
  processado), *provisório* (texto por bloco, sujeito à passada final) e *firme*
  (depois da reconciliação). Sem eles não há como distinguir "não falaram" de
  "ainda não processei" (§2.3);
- **o nome do falante segurado até os ~9 minutos.** Regra que sai dos resultados
  §5.3: até a janela esquentar, a rotatividade do rótulo chega a 22,8%. Antes
  disso a tela mostra "Falante 2", e não um nome. **Um nome que troca sozinho é
  pior que nenhum**;
- **a marca da reconciliação no fim** (§2.1).

**O que ela não faz.** Não edita, não renomeia, não exporta, não gera ata. Tudo
isso já tem lugar e continua acontecendo depois — e cada um deles, ao vivo,
compete com a atenção da reunião.

### 6.2 Produto B — perguntar sobre a reunião, ao vivo

**O que é.** Uma caixa de pergunta sobre o que já foi dito. É o `T2.1`, e é o
produto mais caro dos três: §2.2 orça 20 a 40 s por pergunta, com a placa inteira
e parando o ASR.

**O que a tela precisa dizer, e que é incomum:**

- **o custo, antes.** "Isto vai pausar a transcrição por cerca de meio minuto" é
  informação que o usuário precisa ter *antes* de perguntar, no meio de uma
  reunião. É o oposto do que se faz numa caixa de chat, e é honesto;
- **a fila, visível.** Perguntar entra na fila da GPU. A tela mostra o que está
  na frente;
- **a resposta é rascunho, e cita.** Cada afirmação da resposta apontando para o
  minuto de onde saiu, clicável — a revisão já toca o áudio a partir de um
  instante ([revisao.js:31](../app-net/App/web/revisao.js#L31)). Um 4B local
  **omite** (ATA.md §8), e resposta sem citação num app que grava reunião é a
  pior combinação possível;
- **nunca parecer ata.** A ata tem destino, formato e verificador
  ([Nucleo/Atas/VerificadorDeAta.cs](../app-net/Nucleo/Atas/VerificadorDeAta.cs)).
  Uma resposta ao vivo não passou por nada disso.

**O que decide se ele existe** é a medição do §2.2 (cache de prompt e recorte de
janela), não desenho.

### 6.3 Produto C — o falante ganha nome cedo

**O que é.** O `T3.1`, e o mais bem medido dos três (resultados §8, 40 gravações):
aprende-se a voz nos primeiros minutos e nomeia-se o resto da reunião.

**Os números que mandam no desenho:**

- **zero erros de pessoa** no corte de 10 min, em 42 mil palavras julgadas;
- **cobertura de ~45%** no limiar de hoje (0,70), e **78,5% no de 0,60 — ainda com
  zero erro**. O penhasco está em 0,55;
- o teto é quem nunca falou antes do corte: aos 10 minutos, 92,8% do que ainda
  será dito é de alguém já conhecido (§6 dos resultados).

**O que a tela faz com isso.** Metade dos trechos tem nome e metade não, e essa
proporção é o produto, não uma falha. A tela precisa de **duas classes visuais
distintas** — nomeado e não nomeado — e de um caminho de um clique para nomear os
não nomeados, aproveitando que o não nomeado *já está agrupado por rótulo*.

**A ressalva do §8.3 dos resultados é de interface, não de motor.** O limiar de
0,60 vale para casar voz **dentro da mesma reunião**; o banco de vozes entre
reuniões é outro problema. Se o app passar a ter dois limiares, **os Ajustes
passam a ter de explicar dois**, e hoje explicam um.

---

## 7. O que muda, arquivo por arquivo

Nenhum destes é para fazer agora. É o mapa do custo.

| arquivo | o que muda | por quê | risco |
|---|---|---|---|
| **novo** `lista-de-trechos.js` | lista virtualizada com acréscimo incremental, âncora de rolagem e "seguir" | conserta o F-2 **e** é a peça central do produto A | médio — é a peça nova mais difícil |
| **novo** `aovivo.js` | o espelho do estado da sessão ao vivo, no molde de `transcricoes.js` | uma tela não pode ser dona de um estado que dura a reunião inteira | baixo — o padrão já existe |
| [revisao.js](../app-net/App/web/revisao.js) | passa a usar `lista-de-trechos.js`; `estado` deixa de ser variável de módulo | duas listas que divergem é o defeito mais provável deste projeto | **alto** — é a tela em que o usuário passa mais tempo |
| [gravador.js](../app-net/App/web/gravador.js) | ganha o painel ao vivo — **fora** do `aplicar()`, que roda a 5 Hz | pôr a lista dentro do `aplicar` é reconstruir a transcrição cinco vezes por segundo | médio |
| [ponte.js](../app-net/App/web/ponte.js) | prazo, cancelamento, e um segundo tipo de evento incremental | F-6 e F-12 | baixo |
| [trilho.js](../app-net/App/web/trilho.js) | a composição das bolinhas deixa de assumir exclusão mútua | §5, itens 1 e 2 | baixo, mas o comentário do arquivo precisa ser reescrito junto |
| [pecas.js](../app-net/App/web/pecas.js) | paleta em tokens (F-1); `confirmar()` em `<dialog>` (F-5); gaveta modal de verdade (F-4) | três consertos independentes na mesma peça | baixo |
| [index.html](../app-net/App/web/index.html) | uma região de anúncio; nada mais | F-3 — e a política de anúncio é o trabalho, não a região | baixo |
| [app.css](../app-net/App/web/app.css) | tokens de falante por tema; a lista ao vivo | F-1 | baixo |
| [configuracoes.js](../app-net/App/web/configuracoes.js) | aba "Tempo real", **nascendo desligada** | FASE7 §4.1 e §7 | baixo, e piora o F-11 |
| [Ponte.cs](../app-net/App/Ponte.cs) | ops e eventos novos (§8) | — | médio |
| [SIDECAR.md](SIDECAR.md) | **nada** | ver abaixo | — |

**O achado que barateia tudo: o produto A não precisa de mudança no protocolo do
sidecar.**

O motor de ASR já aceita `{"id":N,"op":"transcrever","audio":"<caminho>"}` sobre
um arquivo, **fica quente entre requisições** por decisão da Fase 2, e devolve
segmentos com palavras. Um bloco de 3 minutos é *mais uma chamada dessas*, num
arquivo menor. A diarização por janela cumulativa é *uma chamada de `diarizar` no
`system.wav` que está crescendo* — e o `CrashSafeWavWriter` garante header válido
a cada 10 s, com `FileShare.Read`.

O que falta é do **núcleo**, não do protocolo: mixar as duas faixas por trecho
enquanto a gravação corre (hoje `Nucleo/Faixas.cs` faz o mix uma vez, no início da
transcrição, e carrega 1,4 GB em pico — o `SUP-3` do backlog **muda de prioridade
por causa disto**, porque mixar em blocos é exatamente o que o `SUP-3` propõe).

**E uma pergunta que ninguém mediu, que é barata e pode derrubar o desenho:**

> **T0.5 — o motor de diarização lê corretamente um WAV que está crescendo?**
> A janela cumulativa manda o `system.wav` vivo ao pyannote. O motor lê pelo
> `soundfile`/`torchaudio`, que confiam no header — e o header é reescrito a cada
> 10 s com os tamanhos *daquele instante*. Ler enquanto o flush acontece é um caso
> que o `CrashSafeWavWriter` nunca teve de aguentar, porque até hoje ninguém leu o
> arquivo antes de a gravação acabar. **Custa meia hora e nenhuma GPU**: gravar,
> ler em paralelo, comparar com a leitura depois de fechado.

---

## 8. O contrato: o que a ponte precisaria aprender

Escrito como proposta, para ser criticado antes de existir. Segue a forma do que
já está lá (`{id, op, ...}` / `{id, ...}`, `id: 0` para evento).

**Ops novas** — três, e nenhuma delas liga nada sozinha:

```
{"op": "aovivo",        "gravacao": "<pasta>"}   → o estado da sessão agora
{"op": "aovivo-ligar",  "ligado": true|false}    → liga/desliga para ESTA gravação
{"op": "perguntar",     "texto": "…", "ate_s": N} → produto B; responde por progresso
```

**Eventos** — `id: 0`, tipo novo, **incremental por construção** (F-12):

```
{"id":0, "tipo":"aovivo", "evento":"bloco",     "n":12, "inicio_s":2160, "fim_s":2340,
 "trechos":[{"inicio":2162.4,"fim":2165.1,"texto":" …","falante":"SPEAKER_01"}]}
{"id":0, "tipo":"aovivo", "evento":"falantes",  "de":{"SPEAKER_01":"Ana"}}
{"id":0, "tipo":"aovivo", "evento":"fila",      "agora":"asr", "esperando":["pergunta"]}
{"id":0, "tipo":"aovivo", "evento":"reconciliado","trechos_mudados":148}
```

Quatro decisões embutidas aí que valem discussão antes de código:

1. **o bloco chega inteiro, e uma vez.** Nada de trecho a trecho — o produto A é
   de blocos de 3 minutos, e fingir granularidade menor é fingir tempo real;
2. **os nomes chegam separados dos trechos**, porque eles mudam *para trás*: o
   falante do minuto 5 ganha nome no minuto 12. Mandá-los junto do bloco
   obrigaria a reenviar blocos antigos;
3. **a fila é evento, não dedução.** A tela não pode adivinhar quem está com a
   placa;
4. **`aovivo-ligar` é por gravação, não global.** É o "nasce desligado" da carta
   com a granularidade certa: quem liga está aceitando a troca *nesta* reunião.

---

## 9. O que não fazer

Estendendo o §7 da carta ao que é de tela:

- **não pôr a lista ao vivo dentro do `aplicar()` do gravador.** Ele roda cinco
  vezes por segundo e já reconstrói os avisos a cada volta;
- **não anunciar o conteúdo em `aria-live`.** Anunciar estado, nunca texto (§F-3);
- **não mostrar nome de falante antes de a janela ter ~9 minutos** (resultados
  §5.3). Um nome que troca sozinho é pior que "Falante 2";
- **não deixar a resposta do produto B parecer ata**, nem sair da tela ao vivo;
- **não escrever a lista ao vivo separada da lista da revisão.** Duas listas
  divergem, e a que diverge é sempre a que ninguém está olhando;
- **não nascer ligado**, enquanto o `SUP-2` estiver aberto e o `SUP-1` não
  existir;
- **não chamar isto de "tempo real" na tela.** São três minutos, e a palavra
  promete três segundos.

---

## 10. A fila do frontend, com gatilho

> **Estado em 10/09/2026:** cinco dos doze itens saíram — ver
> [FASE7-ROTA.md](FASE7-ROTA.md) §1. As dez decisões do §12 continuam valendo
> inteiras; é esta tabela que envelheceu.

Mesma régua do [BACKLOG.md](BACKLOG.md): item sem gatilho fica escrito e não é
feito. **Nenhum destes depende do tempo real acontecer** — os cinco primeiros
valem por si, hoje.

| id | item | tipo | gatilho | dependência |
|---|---|---|---|---|
| ~~**FE-1**~~ | os oito `--falante-N` no `app.css` e na `PALETA` do `pecas.js` | `bug` | ✅ **feito** | — |
| ~~**FE-2**~~ | a lista de trechos virtualizada, usada pela revisão | `bug` | ✅ **feito** — `lista-de-trechos.js`, usada pela revisão **e** pelo painel ao vivo | — |
| **FE-3** | região de anúncio + foco ao trocar de tela | `bug` | **já dói** (`UI-6`) | nenhuma |
| **FE-4** | `confirmar()` em `<dialog>`, e as gavetas modais | `débito` | as três confirmações destrutivas | nenhuma |
| **FE-5** | prazo e cancelamento na `pedir()` | `bug` | uma tela travada em "gerando…" | nenhuma |
| **FE-6** | escrever a fila da Fase 7 (T0.1…T4.5) e definir A, B e C | `débito` | **já dói** — o §1.2 deste documento | nenhuma |
| **FE-7** | commitar os resultados e as sete ferramentas | `débito` | **já dói** — três dias de GPU fora do git | nenhuma |
| ~~**FE-8**~~ | o ensaio sem backend (§11) | `feature` | ⏭ **pulado** — o painel de verdade foi construído direto | — |
| ~~**FE-9**~~ | o painel ao vivo no Gravador | `feature` | ✅ **feito** — `aovivo.js`, em RC na `0.7.0-rc3` | — |
| **FE-10** | a caixa de pergunta | `feature` | o `T2.1` medir cache de prompt e dar número | FE-9 |
| **FE-11** | as duas classes visuais de falante nomeado | `feature` | o produto C ser decidido | FE-1 |
| ~~**FE-12**~~ | aba "Tempo real" nos Ajustes, desligada | `feature` | ✅ **feito**, como bloco em Ajustes › Transcrição e não como aba própria | — |

---

## 11. Como ensaiar o produto A sem backend nenhum

A parte boa, e é a mesma da carta: **o acervo responde de novo, e desta vez sem
GPU.**

`tools/medir_layout.py` já serve a página do app num Chromium do Playwright com
uma **ponte falsa** — `window.chrome.webview` dublado, com `_ouvintes` guardando
os assinantes. Empurrar um evento `id: 0` de dentro do Playwright é uma linha:

```python
page.evaluate("""(ev) => window.chrome.webview._ouvintes
                   .forEach(f => f({ data: JSON.stringify(ev) }))""", evento)
```

E os eventos não precisam ser inventados: **as 49 gravações transcritas do acervo
já são a fonte**. `tools/simular_blocos.py` já corta transcrição em blocos de N
segundos — é o que ele fez para o T0.3. Alimentar a tela com os blocos reais de
uma reunião real, na velocidade que se quiser, dá:

- **o custo de desenho medido**, e não estimado: quanto custa acrescentar o bloco
  12 numa lista que já tem 700 trechos;
- **o comportamento da rolagem** com texto chegando enquanto se lê;
- **a reconciliação do §2.1 encenada** — basta empurrar a transcrição final
  depois dos blocos e ver o que acontece com a tela;
- **o teste do nome que troca**: os resultados §5.3 dão a rotatividade real por
  passada, e ela pode ser reproduzida.

Custa **zero GPU, zero C# e zero risco à gravação** — e responde a pergunta que a
Fase 7 não fez em onze testes: *isto é bom de olhar durante uma reunião?* Se a
resposta for não, ela chega antes de qualquer linha de backend, que é o único
momento em que ela é barata.

---

## 12. As decisões tomadas — 04/09/2026

As dez decisões do §6 e do §7 foram para o dono do produto como pranchas
desenhadas, e **nove voltaram aprovadas como recomendadas; a D3 voltou decidida
ao contrário.** Este é o registro delas. **A partir daqui elas não são mais
opinião deste documento: são o desenho.**

A D3 teve um segundo tempo no mesmo dia — mantido o nome colorido, a paleta foi
refeita a partir da maior lista categórica do design system. O resultado está no
**§12.3**, e o que quem for implementar recebe está no **§12.4**.

| # | decisão | resolvida como |
|---|---|---|
| **D1** | onde o painel ao vivo mora | **duas colunas** enquanto grava — controle e notas à esquerda, transcrição à direita, rolagens separadas. Parada a gravação, volta a ser uma coluna |
| **D2** | o que aparece antes de o falante ter nome | **"Falante N"**, e o nome entra aos ~9 min, quando a janela esquentou |
| **D3** | a cor do falante: texto ou marca | **texto** — e a paleta refeita com os oito matizes da `--dados-*`, §12.3 |
| **D4** | como o bloco diz em que estado está | **separador nomeando bloco e estado** (`36:00 – 39:00 · provisório`) mais hachura e texto acinzentado no provisório |
| **D5** | seguir ao vivo, e como se solta | **solta ao rolar para cima**, e aparece "voltar ao vivo" |
| **D6** | a reconciliação no fim | **substituir, dizer quanto mudou, e deixar conferir** — pela mesma máquina do filtro `✎ N correções` da revisão |
| **D7** | a fila da GPU | **uma etiqueta dizendo o que a placa faz agora**, no topo do painel |
| **D8** | a caixa de pergunta | **abaixo da transcrição**, avisando o custo antes, e com **toda** afirmação citando o minuto |
| **D9** | o rabo de 3 minutos | **dizer que os últimos minutos entram na passada final** |
| **D10** | como isto se liga | **Ajustes › Tempo real, global**, nascendo desligado, com a troca escrita em português |

### 12.1 A D3, e por que ela fica registrada mesmo aprovada ao contrário

A recomendação era trocar a cor de **texto** por **marca**. O dono do produto
decidiu manter os nomes coloridos: *"eu gosto dos nomes coloridos porque separa
do texto da transcrição, o contraste está abaixo do esperado mas eu consigo
utilizar"*.

**A decisão é dele e está tomada.** O que fica escrito é só o fato medido, para
não voltar como surpresa: no tema escuro, cinco dos seis nomes de falante ficam
entre **2,77:1 e 3,68:1**, e o mínimo AA para texto normal é 4,5:1. Quem usa o
app hoje é quem decidiu, e para ele funciona.

O que a decisão **não** fechava era outra coisa: a paleta tinha **cinco cores** e
**nenhum valor para o tema escuro**. Isso foi resolvido em 04/09 sem tocar no
desenho aprovado — ver §12.3.

### 12.3 A paleta de falante, fechada — `--falante-1` a `--falante-8`

Pedido do dono do produto em 04/09: *"vê onde temos uma lista maior de cores no
design system e usa ela"*. Varri o `assets/ds/` inteiro. Há três paletas de
gráfico, e **só uma é categórica**:

| paleta | cores | serve? |
|---|---|---|
| **`--dados-1` … `--dados-8`** | **8** | **sim** — é categórica, e é a maior que existe |
| `--seq-1` … `--seq-5` | 5 | não — sequencial, codifica *magnitude*. Um falante não é maior que outro |
| `--div-1` … `--div-5` | 5 | não — divergente, codifica *direção a partir de um centro* |

As escalas `--areia-*`, `--primaria-*` e `--acento-*` são rampas de um matiz só:
não separam identidade. **Oito é o teto do design system**, e é o que vamos usar.

**O que impedia usá-la direto já estava medido no F-1:** a `--dados-*` é paleta de
*preenchimento*. Os valores do **tema escuro passam como texto** — conferido, 4,83
a 9,75:1 nos quatro fundos reais. Os do **tema claro não** — `--dados-4` fica em
2,19:1 sobre areia.

**A solução: mesmos matizes, luminosidade de texto no tema claro.** Cada cor foi
escurecida em OKLCH — matiz e croma preservados, só a luminosidade desce — até
passar 4,5:1 no **pior** dos quatro fundos em que o nome do falante aparece
(`--cor-fundo`, o `--cor-superficie-2` do `:hover`, o `--cor-acao-suave` do trecho
tocando, e o `--cor-superficie` das etiquetas de filtro).

**E foram reordenadas.** As oito do design system têm três pares de matiz vizinho
— dois azuis, dois verdes, dois marrons —, o que é normal numa categórica de oito
e é por isso que o arquivo dela diz *"use NESTA ORDEM"*. Reordenar não cria
separação onde não há, mas **adia o encontro dos pares**: com 4 falantes a
separação mínima sobe de 0,055 para 0,082, e com 5, de 0,055 para 0,061.

| token | claro | escuro | vem de | matiz |
|---|---|---|---|---|
| `--falante-1` | `#3D6189` | `#6E9AC8` | `--dados-1` | azul — é o **Você**, e no claro é o próprio `--cor-acao` |
| `--falante-2` | `#865B00` | `#E0BB68` | `--dados-4` | ocre |
| `--falante-3` | `#4C6B4A` | `#8AB185` | `--dados-3` | verde |
| `--falante-4` | `#755A79` | `#B292B6` | `--dados-5` | roxo |
| `--falante-5` | `#875844` | `#C4917A` | `--dados-7` | marrom |
| `--falante-6` | `#4A6875` | `#82A5B4` | `--dados-6` | azul-cinza |
| `--falante-7` | `#96512D` | `#DB9A72` | `--dados-2` | terracota |
| `--falante-8` | `#566949` | `#93A883` | `--dados-8` | oliva |

**Contraste conferido: as dezesseis passam AA (4,5:1)** nos quatro fundos, nos
dois temas. O pior caso é `--falante-6` no claro sobre o `:hover`, em 4,58:1.

**O que isto resolve, e o que não resolve — dito com número:**

```
falantes na reunião   separação mínima (OKLab)   leitura
        2                    0,184               confortável
        3                    0,099               confortável
        4                    0,082               aceitável
        5                    0,061               aceitável
        6                    0,047               dois tons vizinhos
        7                    0,035               dois tons vizinhos
        8                    0,014               dois pares quase iguais
```

**Até cinco falantes, as cores se distinguem com folga; de seis em diante, não
mais.** Isso não é defeito da derivação — é propriedade dos oito matizes do
design system, e a paleta de hoje é pior: ela tem **seis** cores e o primeiro par
ruim aparece já na **quinta** (Δ=0,036 entre o azul do "Você" e o `#3d6d8a`).

O ganho concreto: **o ciclo deixa de começar na sexta pessoa e passa a começar na
nona.** Como o "Você" fica sempre no `--falante-1`, os outros sete rodam entre
`--falante-2` e `--falante-8`. Das 44 gravações com falante atribuído, as de 9+
são cinco.

**Se um dia seis cores distintas de verdade forem necessárias**, o caminho é
afastar os matizes — e aí são cores novas, que não estão no design system. Fica
escrito, sem gatilho.

### 12.4 O que quem for implementar recebe

**1. Os tokens, em `app-net/App/web/app.css`.** A forma do bloco escuro copia a do
design system — `[data-tema="escuro"]` explícito, e `[data-tema="auto"]` dentro do
`@media`, exatamente como o `assets/ds/colors_and_type.css` faz. Trocar isso faz o
tema `auto` parar de funcionar em silêncio.

```css
/* A cor do falante, legível como TEXTO nos dois temas.
 * Matizes da paleta categórica do design system (--dados-*), reordenados para
 * separar melhor as primeiras, e escurecidos no tema claro — a --dados-* é de
 * preenchimento e reprova como texto sobre areia. Ver docs/FASE7-FRONTEND.md §12.3.
 * Os dezesseis valores passam AA (4,5:1) nos quatro fundos em que o nome aparece. */
:root {
  --falante-1: #3D6189;  --falante-2: #865B00;
  --falante-3: #4C6B4A;  --falante-4: #755A79;
  --falante-5: #875844;  --falante-6: #4A6875;
  --falante-7: #96512D;  --falante-8: #566949;
}
[data-tema="escuro"] {
  --falante-1: #6E9AC8;  --falante-2: #E0BB68;
  --falante-3: #8AB185;  --falante-4: #B292B6;
  --falante-5: #C4917A;  --falante-6: #82A5B4;
  --falante-7: #DB9A72;  --falante-8: #93A883;
}
@media (prefers-color-scheme: dark) {
  [data-tema="auto"] {
    --falante-1: #6E9AC8;  --falante-2: #E0BB68;
    --falante-3: #8AB185;  --falante-4: #B292B6;
    --falante-5: #C4917A;  --falante-6: #82A5B4;
    --falante-7: #DB9A72;  --falante-8: #93A883;
  }
}
```

**2. A paleta, em `pecas.js`.** Substitui os seis literais; o resto da função não
muda, e a regra do "Você" no primeiro tom continua valendo.

```js
/* Os matizes vêm da paleta categórica do design system, em tokens porque o app
 * tem dois temas — cinco hexadecimais fixos reprovavam em AA no escuro
 * (docs/FASE7-FRONTEND.md §12.3). São oito: o ciclo começa na nona pessoa. */
const PALETA = [
  "var(--falante-1)", "var(--falante-2)", "var(--falante-3)", "var(--falante-4)",
  "var(--falante-5)", "var(--falante-6)", "var(--falante-7)", "var(--falante-8)",
];
```

**3. Nada mais.** `corDoFalante` já faz `PALETA[1 + (ordem % (PALETA.length - 1))]`,
então passar de 6 para 8 cores é automático. Os três lugares que usam a cor — o
nome do trecho, o filtro por falante e a barra de participação da gaveta — leem
todos dessa função.

**4. Um teste que valeria.** O `app.css` e o `pecas.js` podem discordar em
silêncio: um token que não existe vira `color: ` inválido, e o nome sai na cor
herdada, sem erro. Um teste que confira que todo `var(--falante-N)` da `PALETA`
tem definição nos dois blocos do `app.css` custa poucas linhas — é o mesmo
raciocínio do teste que já existe para o `Marca.Nome` e o `.iss`.

### 12.2 O que as nove decisões liberam, e o que continua bloqueado

**Liberado para construir hoje, sem depender de o tempo real acontecer:**

| item | por quê |
|---|---|
| **FE-2** — a lista de trechos virtualizada | conserta o F-2 na revisão, que **já dói** na maior reunião do acervo, e é a peça central do painel ao vivo. Paga duas vezes, e é a primeira |
| **FE-3, FE-4, FE-5** | os consertos de anúncio, gaveta modal, `confirmar()` e prazo na `pedir()` valem por si |
| **FE-6, FE-7** | escrever a fila da Fase 7 e commitar os resultados. Nenhum código |
| **FE-8** — o ensaio sem backend (§11) | agora tem desenho para ensaiar, e as nove decisões dizem exatamente o que desenhar |

**Continua bloqueado, e a decisão D10 não desbloqueia:**

O `SUP-1` — o registro que diria o que o app estava fazendo quando a máquina do
segundo usuário caiu — **não existe**, e o `SUP-2` continua aberto. A D10 decide
*como* o interruptor aparece e que ele nasce desligado; ela não decide que se
pode ligá-lo. **Ligar a transcrição ao vivo numa máquina que desliga sozinha sob
carga de GPU, sem o `SUP-1`, é apostar sem instrumento** — e os dois instrumentos
mais baratos do `SUP-1` são de uma linha cada.

---

## 13. Fontes

- [FASE7.md](FASE7.md) — a carta de estudo
- [FASE7-RESULTADOS.md](FASE7-RESULTADOS.md) — a rodada 0
- [ATA.md](ATA.md) §8 — a medição do motor de ata, de onde sai o orçamento do §2.2
- [BACKLOG.md](BACKLOG.md) — `UI-1` a `UI-9`, `SUP-1`, `SUP-2`, `SUP-3`
- [FASE5-HANDOFF.md](FASE5-HANDOFF.md) — a varredura de contraste que não pegou o F-1
- [SIDECAR.md](SIDECAR.md) — o contrato que o produto A **não** precisa mudar
