# Fase 7 — a rota

Escrito em 10/09/2026, depois de uma auditoria do código contra os cinco
documentos da fase.

**Este é o único documento da Fase 7 que se lê primeiro.** Ele existe porque a
fase passou de 2.850 linhas em cinco arquivos, e o mais novo deles — a
[FASE7-FILA.md](FASE7-FILA.md), escrita em 04/09 justamente para resolver isso —
**já estava desatualizado seis dias depois**. Não é falha de quem escreveu: é o
que acontece quando o registro cresce mais rápido do que se lê.

A regra que este documento aplica a si mesmo: **se ele passar de duas páginas,
ele falhou.** O detalhe mora nos outros; aqui fica só o que decide o que fazer
agora.

---

## 0. Os cinco documentos, e para que cada um serve hoje

| documento | serve para | ainda vale? |
|---|---|---|
| **esta rota** | o que fazer agora | — |
| [CONVERGENCIA.md](CONVERGENCIA.md) | **o depois**: o bake-off e a limpeza dos motores que perderem | — |
| [FASE7-RESULTADOS.md](FASE7-RESULTADOS.md) | **o acervo de medição.** Todo número citado em qualquer lugar sai daqui | **sim, inteiro.** É o ativo mais valioso da fase |
| [FASE7-BACKEND.md](FASE7-BACKEND.md) | o plano do MOSS como motor | **cumprido** — ver §1 |
| [FASE7-FRONTEND.md](FASE7-FRONTEND.md) | a auditoria de interface e as dez decisões de desenho (§12) | **o §12 vale; a fila do §10 está vencida** |
| [FASE7-FILA.md](FASE7-FILA.md) | o que cada id `T*` significa | **sim como glossário; o estado do Produto A está vencido** |
| [FASE7.md](FASE7.md) | a carta de estudo, como registro histórico | **quatro das seis posições estão erradas** — a própria FRONTEND §1.1 as cataloga, e ninguém corrigiu a carta |

---

## 0.5 Onde parou — 10/09/2026, fim do dia

**Medido hoje, e cada um está detalhado adiante:**

| | resultado |
|---|---|
| **`S1`** — vetor de voz em ONNX | ✅ **passou**: 129 vozes, 8.256 pares, **0 decisões diferentes**, e **2,5× mais rápido** que o torch em CPU ([CONVERGENCIA](CONVERGENCIA.md) §4) |
| **`R1`** — legenda, placa livre | ✅ **passou**: 3,37x sustentado por 60 min, atraso de **0,11 s**, ciclo 26,7%. **Sem decadência** — o braço do `reset` ficou sem objeto |
| **`R1`** — legenda, Meet + MOSS | ⚠️ **0,45x** — os três não cabem na 2060 |
| **`R1`** — legenda, Meet **sem** MOSS | ✅ **2,46x, ciclo 33,3%, atraso 0,11 s.** Passa nos três critérios — **a camada 1 está liberada** |
| **Parakeet TDT v3 (ONNX)** | ✅ **409 tokens com carimbo por bloco**, ~3–4x **em CPU**. O primeiro candidato a passar no teste de estrutura de tempo |
| **`SUP-1`** | 3 dos 5 instrumentos existem — `Nucleo/MarcaDeEtapa.cs` entrou hoje |
| **o painel ao vivo** | ✅ **funciona pela primeira vez**, em produção, numa reunião real |

**Respondida em 11/09/2026: sim, acompanha.** O desenho leve do §0.6 está
liberado, e a decisão de produto de 10/09 — tirar o falante da tela ao vivo — é
o que o tornou possível: ela remove exatamente a carga que derrubava a legenda.

**A pergunta aberta agora é outra, e é de qualidade:** o Parakeet em ONNX
substitui o `large-v3` na passada final? Se substituir, a transcrição sai da
placa e o orçamento da reunião muda de novo. A régua está rodando
(`tools/medir_parakeet.py` + `wer_contra_gemini.py`, as quatro com gabarito).

## 0.6 O desenho que o dia produziu, e ele é mais leve que o planejado

Decidido pelo dono do produto em 10/09, olhando o painel numa reunião real:
**ao vivo não é preciso saber quem falou — basta separar a sua voz da dos
outros.**

| | GPU | o que dá |
|---|---|---|
| Nemotron em streaming | 26,7% | o texto, sub-segundo |
| **faixa do microfone** | **zero** | "Você" contra "outra pessoa" |
| passada final | depois | quem é cada um, de verdade |

**Sem MOSS e sem pyannote durante a reunião.** O dono vem do canal, não de
modelo — é o que o `Montagem.AtribuirDono` já faz, e está escrito no
`SessaoAoVivo` como *"sem custar GPU nenhuma"*. Some o pico de 4,0 GB, some a
costura que divide 8 pessoas em 15 identidades, e some a instabilidade que o `D2`
mandava esperar 9 minutos — o dono é certeza desde o segundo um.

**A separação de falantes vira chave**, ligada por quem quiser pagar ~10% de
placa e 3 minutos de atraso naquela parte. Ligar e desligar **não toca na
legenda**: as duas coisas são independentes por construção.

---

## 1. O que está pronto — e o que os documentos ainda não sabem

Conferido no código em 10/09/2026, com a suíte rodada.

**A versão é `0.7.0-rc3` e os testes são 574**, não os 504 que o
[CLAUDE.md](../CLAUDE.md) e o [FASE7-BACKEND.md](FASE7-BACKEND.md) usam como
régua. Os 70 novos são desta fase.

**O Produto A existe, ponta a ponta, e está em RC:**

| peça | onde |
|---|---|
| o sidecar do MOSS | `motores/moss/motor.py` |
| o corte em blocos de 3 min | [MossEmBlocos.cs](../app-net/Nucleo/MossEmBlocos.cs) |
| a costura de falante por vetor de voz | [CosturaDeFalantes.cs](../app-net/Nucleo/CosturaDeFalantes.cs) |
| a sessão que acompanha a gravação | [SessaoAoVivo.cs](../app-net/Nucleo/SessaoAoVivo.cs) |
| o evento incremental na ponte | [Ponte.cs](../app-net/App/Ponte.cs) — `id: 0`, `tipo: "aovivo"` |
| o painel no Gravador | [aovivo.js](../app-net/App/web/aovivo.js) |
| a lista virtualizada, usada pelas **duas** telas | [lista-de-trechos.js](../app-net/App/web/lista-de-trechos.js) |
| as duas chaves, nascendo desligadas | [configuracoes.js](../app-net/App/web/configuracoes.js) |

**Três coisas que os documentos dão como pendentes e não são:**

- **o `T0.5` foi medido e passou**, em 09/09/2026. Era o teste que a
  [FASE7-FILA.md](FASE7-FILA.md) marca como *"⬜ nunca feito, e pode derrubar o
  desenho"*. A resposta está no comentário de
  [Faixas.cs:109](../app-net/Nucleo/Faixas.cs#L109): `File.OpenRead` falha num WAV
  que está sendo gravado, e `FileShare.ReadWrite` abre e lê. **O desenho não
  caiu;**
- **o `FE-1` e o `FE-2` estão feitos** — a paleta de oito falantes está no
  `app.css`, e a revisão e o painel ao vivo usam a mesma lista virtualizada, que
  era a condição que a FRONTEND §7 punha para não haver duas listas divergentes;
- **o `FE-8` — o ensaio sem backend — foi pulado**, e o painel de verdade foi
  construído direto. Não é um problema, mas é uma dívida honesta: a pergunta
  *"isto é bom de olhar durante uma reunião?"* nunca foi feita com uma reunião
  real na frente. O RC responde por ela.

**O que continua não feito, e é do plano do backend:** o `D1` (o MOSS competindo
com a gravação — o `T0.2` refeito com o perfil do MOSS) e o `D2` (a auditoria
estrutural do `ata.json`). Os dois são de depois do RC, e continuam sendo.

---

## 2. O risco que vem antes de qualquer decisão técnica

**Nada disto está no git.** São 36 arquivos modificados e 19 novos, e entre eles
o motor inteiro, a sessão ao vivo, o sidecar, os dois documentos novos da fase e
dez arquivos de teste.

A [FASE7-FRONTEND.md](FASE7-FRONTEND.md) §1.2 já tinha registrado isto como o
buraco nº 3 — *"três dias de medição não estão no git; um `git clean` distraído
apaga a semana"*. De lá para cá o que está fora do git deixou de ser três dias
de medição e passou a ser **uma release candidate inteira**.

**É a primeira coisa a fazer, custa um comando, e não depende de decidir mais
nada.** Todo o resto desta rota supõe que já foi feita.

---

## 3. O alinhamento: "tempo real" são três camadas, não uma

Aqui está a única divergência real entre o que foi construído e o que o dono do
produto pediu, e ela vale ser dita de frente.

**O que está pronto não é tempo real, e os documentos proíbem chamá-lo assim** —
com razão: são 0 a 3 minutos de espera mais o processamento, e quem espera
legenda e recebe bloco de três minutos acha que o app quebrou.

**Mas a restrição que levou a esse desenho acabou de ser levantada.** A carta e a
frontend assumem que a prévia precisa ser confiável o bastante para se olhar. O
pedido de 10/09 diz o contrário, com todas as letras: *"não precisa ser perfeita
porque ainda podemos fazer passes posteriores para revisar tudo"*.

Isso reabre a faixa que a fila arquivou como `T4.1`–`T4.3` (⏸ adiado) e que o
`T4.4` rebaixou. E o desenho que sai disso **não substitui o que foi construído —
soma-se a ele**, porque as três camadas medem a mesma coisa com confianças
diferentes:

| | camada 1 — **legenda** | camada 2 — **consciência** | camada 3 — **verdade** |
|---|---|---|---|
| **latência** | sub-segundo | 0 a 3 min | no fim da reunião |
| **motor** | `nemotron-3.5-asr-streaming-0.6b` | MOSS em bloco | `faster-whisper` + `pyannote` |
| **estado** | **a construir** | **pronto** (§1) | pronto desde a Fase 2 |
| **entrega** | texto, sem falante | texto + falante local | tudo |
| **GPU na 2060** | ~12% | ~10% | a placa, depois |
| **confiança** | rascunho que se reescreve | provisório | firme |

Somadas, as camadas 1 e 2 custam **~22% da placa** — ainda abaixo dos ~41% que o
caminho clássico em bloco custaria sozinho (RESULTADOS §1.3 e §5.2).

**A tela já modela isto.** O `BlocoAoVivo` já carrega um campo `Estado`
(`"provisorio"` / `"mudo"`), e a decisão `D4` da FRONTEND §12 já desenhou o
separador que nomeia bloco e estado. Uma terceira classe — *tentativo* — é a
mesma máquina com mais um valor.

### O que o Nemotron **não** pode ser — medido em 11/09/2026

Pedido o Nemotron como terceiro motor de transcrição, ao lado do clássico e do
MOSS. **Ele não serve para isso**, por duas razões que só apareceram rodando:

```
passada inteira, 2060, language=pt-BR
   5 min  →  41,5 s   7,23x   ok
  15 min  →  o processo morre
  20 / 25 / 30 min  →  idem
```

**1. A passada inteira quebrou — mas a medição não vale.** Acima de ~5 min o
processo morreu, com *core dump* numa das tentativas. **A máquina estava em uso
pelo dono do produto**, e contenção de VRAM produz exatamente esse sintoma.
**Refazer com a placa livre**; até lá isto não é resultado, é suspeita.

**2. E o que mata de vez — este sim vale, e não depende de placa livre.** A
rodada que produziu isto **completou**, nos dois modos de timestamp, e estrutura
de saída não muda por falta de recurso. Num bloco de
3 minutos, com `timestamps` em `segment` **ou** em `token`:

```
1 segmento · 412 palavras · [2,32 s – 180,08 s]
```

**Um segmento cobrindo o bloco inteiro.** O app inteiro é construído sobre
trechos: a diarização atribui falante **por sobreposição temporal com o trecho**,
o `VozDoDono` corta na fronteira, a revisão lista trechos, e a ata os lê. Um
trecho de três minutos faz tudo isso colapsar — é a doença do `hotwords`
(207 trechos em vez de 787, [FASE6.md](FASE6.md) §4.1) elevada a outra ordem.

**Isto não atinge a legenda ao vivo**, e a distinção é a chave: ali o texto vem
da **API de streaming**, que emite `committed`/`tentative` em pedaços — foram
**2.864 commits** em 60 minutos no `R1`. A granularidade vem do fluxo, não dos
segmentos do modo offline. O Nemotron continua sendo o candidato da camada 1 e
**está descartado como motor de transcrição.**

### Por que o Nemotron, e não outro

Não é preferência: é o que a medição desta casa já disse, e é o mais barato de
tentar.

- **roda no mesmo `transcribe.cpp` que o MOSS**, que o
  `tools/empacotar_motores.sh` **já empacota** para o RC. Custo marginal: 0,75 GB
  de modelo e **zero dependência nova**;
- **declara `pt-BR` corretamente** — o MOSS nem declara `pt`, e só funciona
  porque se omite o parâmetro (BACKEND A2);
- **`CommitPolicy: stable_prefix` é o LocalAgreement** que a carta §3.2 propunha
  implementar à mão, e a separação `committed`/`tentative` é o que impede o texto
  de tremer na tela. RESULTADOS §9.3: *"os dois problemas que eu havia listado
  como trabalho em aberto, resolvidos por configuração"*;
- **8,46× o tempo real em CUDA**, e empate técnico no texto contra o MOSS (§9.2).

**O número que o reprovou não reprova este uso.** O `0,99×` da §9.1 é **em CPU,
num Ryzen 5 3400G de 4 núcleos**, e reprova o *bloco* — que precisa de margem
para drenar a fila. Uma legenda em streaming na GPU não tem fila para drenar.

> **Ressalva honesta:** não consegui conferir se saiu coisa melhor que o Nemotron
> desde 03/09 — a busca na web estava fora do ar em 10/09. O que sustenta a
> escolha é medição feita **nesta placa, com este áudio**, que vale mais do que
> um modelo mais novo que ninguém aqui mediu. **Refazer a varredura de
> candidatos é o passo `R0` do §4**, e é barato.

---

## 4. O caminho principal, especificado

Cada passo diz o que é, onde mora, e **como se sabe que acabou**. Um passo que
não pode ser verificado não está pronto para ser executado.

**Antes de tudo:** o §2 (pôr no git), e **soltar o `0.7.0` com o que já está
medido**. A camada 1 entra depois do RC, e não dentro dele — misturar a coisa
nova com a coisa em teste é como se perde a leitura das duas.

### R0 · Refazer a varredura de candidatos — *meia hora, sem GPU*

Conferir se, entre 03/09 e hoje, apareceu motor de streaming melhor que o
Nemotron **com português declarado** e que rode em `transcribe.cpp` ou em ggml.

**Pronto quando:** há uma linha escrita por candidato, com licença, tamanho,
línguas declaradas e se faz streaming — e a escolha do R1 está confirmada ou
trocada. **Não é para adiar o R1**: se nada aparecer em meia hora, segue-se com o
Nemotron.

### R1 · Medir o Nemotron em streaming de verdade — *o passo que pode matar a camada 1*

**A ferramenta existe desde 10/09/2026:**
[`tools/medir_legenda.py`](../tools/medir_legenda.py). Ela mede as três coisas
abaixo, tem os critérios de morte em código (não em lembrança), e confere
`supports_streaming` e o idioma declarado antes de medir. **Ela nunca rodou** —
foi escrita contra a ABI do `transcribe_cpp` 0.2.3 numa máquina sem GPU e sem o
GGUF, e a primeira execução é na máquina com placa.

**O quê:** alimentar uma gravação do acervo **na velocidade do relógio**, por
60 minutos seguidos, com o Meet aberto, e medir três coisas:

1. **a velocidade se sustenta?** A §9.2 viu cair de 8,46× para 3,77× entre
   blocos, e a própria seção marca isso como *"ressalva de medição, não
   propriedade confirmada"* — a causa provável é estado acumulado na sessão, e
   existe um `Stream.reset()` que a medição não usou. **Medir com e sem o
   `reset`** responde a pergunta e o conserto de uma vez;
2. **quanto demora o texto a firmar?** O atraso entre a palavra ser dita e sair
   do `tentative` para o `committed`, em `stable_prefix`;
3. **quanto sobra da placa** com o Meet codificando vídeo.

> **✅ MEDIDO EM 10/09/2026 — passou.** 60 min contínuos, ritmo do relógio, na
> RTX 2060, sobre a reunião de 62 min de 17/08. Sem o Meet aberto.
>
> ```
> velocidade   global 3,37x · por balde de 10 min:
>              3,61 · 3,10 · 2,75 · 3,70 · 3,73 · 3,58
> atraso       mediana 0,11s · p90 0,19s · máx 0,19s   (2.864 commits)
> ciclo        26,7% da parede
> ```
>
> **Não há decadência.** O balde final (3,58x) é maior que o terceiro (2,75x) —
> é ruído, não tendência. **A queda de 8,46x para 3,77x da
> [FASE7-RESULTADOS.md](FASE7-RESULTADOS.md) §9.2 não se reproduz em streaming**,
> o que confirma a leitura de lá de que era ressalva de medição: aquilo era
> propriedade do reuso do mesmo `Model` entre blocos, e não do modelo. **O braço
> do `Stream.reset()` fica sem objeto** — não há o que consertar.
>
> **O atraso é o resultado principal**: 0,11 s de mediana, idêntico ao que o
> teste de 2 min tinha dado, sobre 2.864 commits. Isso é legenda, e não bloco.
>
> **Uma ressalva do arnês, e ela não muda a conclusão:** a parede deu 66,7 min
> para 60 min de áudio. É artefato do ritmo — o laço dorme o que sobra de cada
> pedaço e **não sabe guardar folga**, então um pedaço caro atrasa o total sem
> nunca ser recuperado. Ao vivo o áudio chega a um buffer e o consumidor drena:
> com 3,37x de vazão e 26,7% de ciclo, a folga é de três vezes. Os números que
> mandam são esses dois.

> **⚠️ COM O MEET ABERTO, EM 10/09/2026 — a velocidade desaba.** Mesmo áudio,
> 10 min, durante uma reunião de verdade com a prévia do MOSS também ligada:
>
> ```
>                    sozinho      com Meet + MOSS
> velocidade         3,37x            0,45x        ← 7,5x mais lento
> ciclo              26,7%            73,7%
> atraso             0,11s            0,11s        ← ver a ressalva
> ```
>
> **0,45x é abaixo do tempo real**: a legenda não acompanha, e a fila cresceria
> sem parar. Falha o critério do §4.
>
> **Mas o teste não separa as duas cargas**, e isso decide o que fazer. Havia
> **três** consumidores de GPU: o Meet, o bloco do MOSS a cada 3 min, e a
> legenda. O MOSS sozinho é ~10% de ciclo — pouco para explicar 7,5x —, então a
> causa provável é pressão de VRAM com três contextos CUDA na placa de 6 GB.
> **A medição que falta é Nemotron + Meet, com o MOSS desligado**, que é
> exatamente o desenho proposto em 10/09: legenda pelo Nemotron e "Você" pela
> faixa do microfone, sem modelo de falante ao vivo.
>
> **✅ E SEM O MOSS, PASSA — 11/09/2026.** A medição que faltava, feita durante
> uma reunião real com a prévia desligada:
>
> ```
>                 sozinho    Meet + MOSS    Meet sem MOSS
> velocidade       3,37x        0,45x          2,46x
> ciclo            26,7%        73,7%          33,3%
> atraso           0,11s        0,11s          0,11s
> ```
>
> **O culpado era o MOSS, não o Meet.** O Meet sozinho custa ~27% da velocidade
> (3,37 → 2,46), o que é modesto e esperado. O terceiro contexto CUDA é que
> derruba por mais 5,5×. E isso é desproporcional ao ciclo do MOSS (~10%), o que
> reforça a leitura de pressão de contexto na placa de 6 GB, e não de disputa de
> cálculo.
>
> **Os três critérios passam**, e a saída `B2` não se aplica. **A camada 1 está
> liberada no desenho leve do §0.6** — legenda pelo Nemotron, "Você" pela faixa
> do microfone, sem modelo de falante ao vivo. A decisão de produto de 10/09, de
> tirar o falante da tela ao vivo, é o que torna isto possível: ela remove
> exatamente a carga que derrubava a legenda.

> **Ressalva do medidor, e ela é importante:** o atraso de 0,11s **não desmente**
> a velocidade. Ele mede `input_received_ms - audio_committed_ms` — a defasagem
> interna do modelo —, e não a profundidade da fila. A 0,45x o áudio se acumula,
> e o que a pessoa veria é o atraso da fila, que este número não enxerga. **A
> métrica de atraso só vale quando a velocidade está acima de 1x.**

**Pronto quando:** os três números existem sobre 60 min contínuos.

**Critérios de morte, escritos antes de medir** — se qualquer um falhar, vai-se
para o §5:

- a velocidade cai abaixo de **1,5×** sustentado, mesmo com `reset` → **B1**;
- o `committed` demora mais de **3 s** → a camada 1 não é legenda, e vira o
  **B3**;
- o Meet aberto derruba a margem → **B2**.

### R2 · O sidecar da legenda

**Onde:** `motores/legenda/motor.py`, ao lado do `motores/moss/motor.py`, e no
molde dele.

**O quê:** uma sessão que **fica aberta** — é a extensão do protocolo que a carta
§2 previa: abre, recebe áudio, e **cospe resultado sem ser perguntado**, com
`id: 0`, no mesmo espírito do evento da ponte.

**As armadilhas já são conhecidas e estão escritas:** duplicar o fd 1 antes de
qualquer import (o ggml escreve no stdout e uma linha corrompe o protocolo), e o
modelo fica quente pela sessão inteira.

**Pronto quando:** sobe, recebe uma gravação do acervo em tempo de relógio, e o
texto `committed` que ele emite bate com o que o R1 mediu.

### R3 · `Nucleo/LegendaAoVivo.cs`

**O quê:** o irmão da `SessaoAoVivo`, com a mesma disciplina — **lê** as faixas
pelo `Faixas.LerJanela`, que o `T0.5` já provou funcionar num WAV que cresce, e
**não toca em `Gravacao/` nem em `Captura/`**.

**A regra que não se negocia é a mesma:** todo caminho protegido, e o pior
desfecho de um erro é uma linha a menos na tela — nunca uma reunião perdida.

**Pronto quando:** os **574 testes passam sem alteração nenhuma**, mais os novos.
É a mesma régua que a fase B do backend usou, e pelo mesmo motivo: se um teste
existente precisar mudar, a coisa nova vazou para onde não devia.

### R4 · A linha na tela

**Onde:** [aovivo.js](../app-net/App/web/aovivo.js), acima da lista de blocos.

**O quê:** **uma linha que se reescreve no lugar** — nunca um item que entra na
lista. O texto `tentative` em cinza, o `committed` firme, e o bloco de 3 minutos
continua chegando por baixo e substituindo o que a linha já mostrou.

**Por que não entra na lista:** a lista é de blocos, e um bloco chega inteiro e
uma vez (FRONTEND §8, decisão 1). Uma legenda que empilhasse linhas faria a lista
crescer sem teto durante a reunião, que é exatamente o `F-12`.

**Pronto quando:** a linha se reescreve sem redesenhar a lista — conferido pelo
ensaio do §11 da FRONTEND, que empurra evento falso de dentro do Playwright, sem
GPU e sem C#.

### R5 · A chave

**Onde:** o bloco *"Ver a transcrição durante a reunião"* que já existe em
Ajustes › Transcrição.

**O quê:** um segundo nível na chave que já está lá — **nascendo desligado**, e
dizendo em português que a legenda é rascunho que se reescreve.

**Pronto quando:** o `app.json` sem a chave continua se comportando como hoje.

---

## 5. As saídas, se o caminho principal travar

Nenhuma delas é hipotética: as quatro saem de número já medido.

### B1 · O Nemotron decai ao longo da hora

**Se** a velocidade cair mesmo com `Stream.reset()` por segmento: **reciclar a
sessão** a cada N minutos, como o `MossEmBlocos` recicla o bloco. Perde-se o
contexto da fronteira, que num rascunho não custa nada.

### B2 · A placa não aguenta legenda + bloco + Meet

Três degraus, do mais barato ao mais caro:

1. **espaçar a camada 2.** O bloco do MOSS vai de 3 para 6 minutos, e a legenda
   cobre o intervalo. O `BlocoS` é **uma constante só**
   ([MossEmBlocos.cs:49](../app-net/Nucleo/MossEmBlocos.cs#L49));
2. **desligar a camada 2** quando a 1 estiver ligada. A passada final continua
   entregando falante, que é o que a camada 2 adiantava;
3. **a legenda em CPU.** O `0,99×` da §9.1 é de um Ryzen 5 3400G de 4 núcleos, e
   a própria seção diz que *"num processador moderno a margem provavelmente
   existe"*. Se existir, a legenda custa **zero GPU** — e dissolve a §4.2 da
   carta, que é a disputa com o Meet.

### B3 · O texto ao vivo do Nemotron não serve em português

Cair para o que a carta §3.2 propunha e que **nunca foi medido**: o
`faster-whisper` em janela deslizante com LocalAgreement, num modelo pequeno.
Mesma família de motor que o app já usa, mesma língua já validada. É mais
trabalho — a política de emissão passa a ser nossa —, mas não tem risco de
modelo.

### B4 · Nenhum streaming funciona — a saída mais barata que existe

**Encurtar o bloco de 3 para 1 minuto.** A RESULTADOS §4 já mediu os três
tamanhos, e o que o bloco de 1 min perde é *diarização* (7,3% contra 4,0%) e
fronteira (3,9% dos segmentos cortados contra 1,2%). **O texto empata em todos os
tamanhos.**

O bloco de 3 min ganhou porque a prévia precisava ser confiável. **Sob a
restrição nova — rascunho, com passe posterior — o bloco de 1 min passa a ser a
escolha certa**, e a latência cai de 0–3 min para 0–1 min por uma constante.

Não é legenda, e não resolve o pedido. Mas é o degrau que existe **hoje**, custa
uma linha, e nenhuma das outras três saídas é pré-requisito dele.

---

## 6. O que continua bloqueado, e não é técnico

O argumento mais forte contra a fase inteira não mudou, e nenhuma medição o
tocou: **o computador do segundo usuário desliga sozinho sob carga de GPU**
(`SUP-2`). Hoje isso custa uma transcrição, e a retomada devolve. **Ao vivo
custaria a reunião.**

O `T0.2` — a gravação sobrevive à carga — passou com **zero amostra perdida**,
mas na **RTX 2060 do dono**, não na RTX 4050 que desliga.

Do que isto decide:

- **na máquina do dono**, ligar é decisão dele, e as chaves nascem desligadas
  exatamente para que seja;
- **na máquina do segundo usuário**, não se liga nada ao vivo enquanto o `SUP-1`
  não existir. Ele é o registro que diria *o que o app estava fazendo quando a
  máquina caiu*. **Dois dos cinco instrumentos entraram em 04/09**; três não. Sem
  eles, ligar lá é apostar sem instrumento.

---

## 7. O que não fazer

Herdado da carta §7 e da FRONTEND §9, e o que a camada 1 acrescenta:

- **não trocar o motor offline.** A camada 3 é a verdade, e é ela que a ata lê;
- **não deixar a legenda tocar o `transcricao.json`.** A exceção que os
  RESULTADOS §4 abriram vale para o **bloco**, que é do mesmo modelo com os
  mesmos parâmetros. A legenda é de **outro modelo**, e é o defeito da 0.4.0 de
  volta;
- **não pôr a legenda na lista de blocos.** Linha que se reescreve, sempre;
- **não anunciar o conteúdo em `aria-live`** — anunciar estado, nunca texto. Uma
  legenda em região viva leria a reunião em voz alta por cima da reunião;
- **não nascer ligado**, enquanto o `SUP-2` estiver aberto;
- **não escrever um sexto documento de Fase 7.** O que mudar de estado se corrige
  aqui e no documento de origem, no mesmo dia.

---

## 7.1 E depois das features

Quando a legenda e a busca existirem, o assunto passa a ser **qual motor fica** —
hoje todos são mantidos vivos para que dê para comparar, e isso custa **17 GB de
instalação**, dos quais ~11,4 GB são cortáveis com medição.

A ordem é: **features → bake-off com régua → limpeza**, e nada é apagado antes de
o substituto ter ganhado sobre o acervo. O plano está na
[CONVERGENCIA.md](CONVERGENCIA.md), e o achado que manda nele é que **trocar o
ASR e a diarização não tira o `torch`**: ele está preso por uma operação só, o
vetor de voz, que é o que dá nome à pessoa na reunião seguinte.

---

## 8. Fontes

- [FASE7-RESULTADOS.md](FASE7-RESULTADOS.md) — §4 (o tamanho do bloco), §9 (o
  Nemotron), §5 (a janela cumulativa), §0.5 (as ressalvas)
- [FASE7-FRONTEND.md](FASE7-FRONTEND.md) — §8 (o contrato da ponte), §11 (o
  ensaio sem backend), §12 (as dez decisões)
- [FASE7-BACKEND.md](FASE7-BACKEND.md) — o molde do sidecar e as armadilhas do
  `transcribe.cpp`
- [FASE7-FILA.md](FASE7-FILA.md) — o que cada `T*` significa
- [CONVERGENCIA.md](CONVERGENCIA.md) — o bake-off e a limpeza, depois das features
- [BACKLOG.md](BACKLOG.md) — `SUP-1`, `SUP-2`, `VOZ-1`, `DIST-1`
