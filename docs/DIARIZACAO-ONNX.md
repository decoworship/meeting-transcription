# A diarização fora do torch — o desenho do porte

Escrito em 18/09/2026, depois de um dia de medição. Ele fecha o `C5` do
[CONVERGENCIA.md](CONVERGENCIA.md) e leva o `C2` junto.

**O que este documento é.** O desenho de tirar o `torch` do pipeline de
diarização, trocando-o por ONNX Runtime com o provedor CUDA. Ele não é sobre
qualidade de diarização — a matemática fica **exatamente a mesma** —, é sobre
peso de instalação. O que se ganha são ~4,5 GB dos 18 GB de `motores/`.

**O que ele não é.** Não é o fim da CUDA. Ela fica, e por outro motivo: o
`large-v3` roda em CTranslate2, que só tem CUDA e CPU. Ver §6.

---

## 1. Os números que autorizaram isto

Tudo medido em 18/09/2026, na RTX 2060 do dono do produto, sobre
`2026-08-25_08-59-22` (14,6 min, uma das quatro gravações do acervo com export
do Gemini).

**O pipeline inteiro, como está hoje:**

```
                    tempo      x tempo real    trechos   falantes
torch + CUDA        33,1 s        26,55x         404        3
torch CPU        1.547,4 s         0,57x         404        3
```

**A CPU não é opção, e isso matou o caminho barato.** A ideia de instalar um
torch CPU-only — 300 MB em vez de 3,6 GB, sem tocar em mais nada — morreu aqui:
a 0,57× tempo real, uma reunião de 1 h custaria 1 h 45 de diarização, e a de
2 h do acervo, 3,5 h. As saídas são idênticas; o tempo é que não fecha.

**Os dois modelos, isolados, ONNX contra torch:**

```
SEGMENTAÇÃO — 869 janelas de 10 s, lote 32
  torch-cuda        1,24 s     710,25x
  onnx-cuda-ep      6,24 s     140,85x     5,04x mais lento    maxabs 5,5e-03

EMBEDDING — 4.000 janelas de 5 s
  torch-cuda       16,90 s      51,97x
  onnx-cuda-ep     17,04 s      51,54x     1,01x — EMPATE      maxabs 8,0e-07
```

**O embedding é onde está o custo, e ele empata.** Comparando o pipeline
inteiro com a segmentação isolada, a segmentação responde por ~4% do tempo: na
GPU são 1,24 s de 33,1 s. O resto é embedding e clustering. A segmentação em
ONNX é 5× mais lenta — é o LSTM, e o runtime avisa (*"3 Memcpy nodes are
added"*) —, mas 5 s a mais num pipeline de 33 s.

**Projeção: ~38 s, ou ~23× tempo real.** Uns 15% mais lento, em troca de
4,5 GB.

> **Ressalva de método, para quem repetir.** O pipeline do pyannote roda com
> `segmentation_batch_size: 32` e `embedding_batch_size: 32` (está no
> `config.yaml` do `community-1`), que é o mesmo lote das medições isoladas —
> então elas são comparáveis. O que não foi medido é o pipeline **inteiro** em
> ONNX, porque ele ainda não existe. A projeção de ~38 s é aritmética, e o `V1`
> do §5 é o que a substitui por medição.

### Os dois caminhos que foram medidos e reprovados

**DirectML: 6,3× mais lento que a própria CPU.**

```
CPUExecutionProvider    efetivo=['CPU']          30,8 s    28,52x
DmlExecutionProvider    efetivo=['Dml','CPU']   194,5 s     4,52x
```

Não foi queda silenciosa para CPU — o provedor efetivo traz `DmlExecutionProvider`,
e ainda assim ele perde. Parte do grafo do PyanNet não tem operador em DML e
atravessa a fronteira a cada nó. **E o DirectML está em *maintenance mode* na
Microsoft**: o desenvolvimento migrou para o Windows ML, cujo provedor de NVIDIA
exige RTX 30XX ou superior — a 2060 está fora.

**ONNX em CPU: 2,5× mais rápido que o torch em CPU** (38,01× contra 15,43× na
segmentação), o mesmo fator que o `S1` viu no embedding. Consistente, e
irrelevante: 2,5× de 0,57× ainda não chega ao tempo real.

> **Uma armadilha que custou uma medição.** A primeira tentativa de CUDA EP
> devolveu 32,11× e eu quase a registrei como resultado. O provedor **tinha
> falhado ao carregar** (`libcublasLt.so.13: cannot open shared object file`) e
> caído para CPU em silêncio. **Toda medição de ONNX neste projeto confere o
> provedor efetivo com `session.get_providers()`**, e não o que foi pedido.

---

## 2. Onde o torch realmente está

Esta é a razão de o porte ser viável, e ela não estava escrita em lugar nenhum.

| arquivo do pyannote | linhas | menções a `torch` | o que é |
|---|---:|---:|---|
| `utils/vbx.py` | 218 | **0** | **o VBx em si** |
| `core/plda.py` | 135 | **0** | **o PLDA** |
| `utils/signal.py` | 375 | **0** | `Binarize` |
| `pipelines/clustering.py` | 763 | **0** | a cola do clustering |
| `pipelines/utils/diarization.py` | 274 | **0** | montagem da anotação |
| `pipelines/speaker_diarization.py` | 787 | 9 | a cola do pipeline |
| `core/inference.py` | 667 | 19 | a janela deslizante |
| `utils/powerset.py` | 241 | 25 | powerset → multirrótulo |
| `core/io.py` | 484 | 27 | leitura de áudio |
| `utils/permutation.py` | 275 | **28** | `permutate` — **e o clustering depende dele** |

**A matemática cara não tem uma linha de torch.** O `config.yaml` do
`community-1` pede `VBxClustering` com `threshold: 0.6`, `Fa: 0.07`, `Fb: 0.8` —
clustering variacional bayesiano sobre PLDA. O algoritmo (`utils/vbx.py`, 218
linhas), o PLDA (`core/plda.py`, 135) e a binarização (`utils/signal.py`, 375)
são numpy/scipy puros. É a parte que eu erraria em silêncio se reescrevesse, e
ela não precisa ser reescrita.

> **Duas correções minhas sobre este ponto, e a segunda desfaz a primeira.**
>
> Ao revisar este documento eu vi que o `clustering.py` importa `permutate` de
> `utils/permutation.py` (275 linhas, 28 menções a torch) e escrevi que ele
> teria de ser portado junto.
>
> **Ao implementar, isso caiu.** O `permutate` aparece **uma vez** no
> `clustering.py`, na linha 729, e ela está dentro da `OracleClustering` — uma
> classe de *avaliação*, que compara contra uma anotação de referência que o app
> nunca tem em produção. A `BaseClustering` não o usa (0 ocorrências), e a
> `VBxClustering` tampouco. **Removida a `OracleClustering`, o arquivo fica sem
> torch**, e o `permutation.py` inteiro não precisa ser portado.
>
> A lição vale escrita: contar menções a `torch` por arquivo diz o custo máximo,
> não o custo real. O que decide é **quem chama o quê no caminho de produção**.

As 9 ocorrências de `speaker_diarization.py` são `torch.vstack` e
`torch.from_numpy` empilhando lotes. O `core/io.py` quase todo é caso geral de
decodificação que este projeto não usa: o formato é sempre o do próprio gravador
(16 kHz mono 16 bits), e o `_ler_wav` do `motores/diarizacao/motor.py` já o lê.

**E três pacotes vizinhos não precisam ser tocados:** `pyannote.core`
(`SlidingWindow`, `Annotation`), `pyannote.metrics` e `pyannote.database` **não
têm torch em arquivo nenhum**. Eles são leves e continuam como dependência
normal, o que tira do porte toda a representação de anotação e janela — que é
muito código e nenhum risco.

**O torch mora nas bordas**, e as bordas são: a inferência dos dois modelos
(resolvida), a janela deslizante, o powerset, o `permutate` e o empilhamento de
lotes.

---

## 3. Os dois modelos, exportados e conferidos

```
segmentacao.onnx    5,6 MB    maxabs vs torch = 1,669e-05
embedding.onnx     25,3 MB    maxabs vs torch = 1,602e-07
```

São **~32 MB de ONNX no lugar de 4,8 GB de torch** — a razão de ~150× que o
`S1` já tinha apontado, agora com os dois modelos.

**A segmentação exporta inteira** (`PyanNet`, janela de 10 s → `(1, 589, 7)`),
com `dynamo=False` e opset 17.

**O embedding não exporta inteiro, e o motivo é o mesmo que o `S1` documentou:**
o `compute_fbank` do pyannote embrulha o fbank num `torch.vmap`, que nenhum dos
exportadores atravessa, e o que sobra sem ele é o `aten::fft_rfft`, ausente do
opset 17. **A saída é a mesma do `S1`**: exporta-se só a ResNet
(`WeSpeakerResNet34.resnet`, entrada de fbank `(lote, quadros, 80)`) e o fbank
fica em numpy — que é o que o `infer_onnx.py` do WeSpeaker já fazia.

> **O `S1` exportou o `wespeaker-voxceleb-resnet34-LM`; este porte exporta o
> `community-1/embedding`.** O [CONVERGENCIA.md](CONVERGENCIA.md) §4 registra que
> o `wespeaker` e o `pyannote-3.1/embedding` têm o mesmo MD5 e que **o
> `community-1` é outro arquivo**. O pipeline em produção é o `community-1`, e é
> dele que o vetor de voz sai — então é ele que se exporta, e a régua `V3` do §5
> é sobre ele.

---

## 4. O desenho

**Vendorizar o pipeline e trocar só as bordas.** Os arquivos do pyannote que o
`community-1` usa passam a viver em `motores/diarizacao/pipeline/`, com os
`torch.*` substituídos por numpy e as sessões ONNX injetadas no lugar das
chamadas ao modelo.

```
motores/diarizacao/
  motor.py                  o sidecar — protocolo e ops, INALTERADO
  pipeline/
    __init__.py
    segmentacao.py          janela deslizante + sessão ONNX + powerset
    embedding.py            fbank em numpy + sessão ONNX
    diarizacao.py           a cola: apply, reconstruct, contagem de falantes
    vendor/                 código do pyannote.audio (MIT), copiado e podado
      __init__.py
      vbx.py                sem edição
      plda.py               só os imports
      signal.py             sem edição
      clustering.py         sem a OracleClustering
  modelos/
    community-1/
      segmentation/model.onnx      5,6 MB   ← novo, ao lado do .bin
      embedding/model.onnx        25,3 MB   ← novo, ao lado do .bin
```

**Três decisões que valem estar escritas:**

**O `motor.py` não muda.** O protocolo do sidecar é o contrato com o C#
([SIDECAR.md](SIDECAR.md)), e as ops `diarizar`, `voz` e `modelo_de_voz`
continuam com a mesma forma. Quem troca de runtime é o que está atrás delas. É o
que permite as duas implementações conviverem enquanto a régua não fecha.

**Os `.bin` ficam onde estão, e os `.onnx` entram ao lado.** Apagar os pesos do
torch é o último passo, depois do `V4`, e ele é reversível — os dois formatos
saem do mesmo arquivo e o `tools/empacotar_modelos_de_diarizacao.sh` já sabe
montar a pasta.

**As duas implementações convivem atrás de uma chave**, `motor_de_diarizacao`
na requisição do sidecar (`escolher_motor` em `motor.py`), com `"torch"` como
padrão até a régua fechar. Um valor desconhecido cai no padrão **em
silêncio**, pela mesma razão que o `MotorAceito` do MOSS sobreviveu com um
valor só: recusar a diarização por causa de uma chave é pior que ignorá-la.
**Ela ainda não está ligada ao núcleo**: a chave existe só dentro do sidecar
hoje — `grep -r motor_de_diarizacao` não encontra nada em `.cs`, `app.json`
ou `web/` — e não vive "no `app.json`" até o C# passá-la adiante.

### O que o porte preserva por construção

- **o vetor de voz é o mesmo espaço vetorial.** Mesmos pesos, mesma
  arquitetura, `maxabs` de 1,6e-07 — o `VozExtraida.Modelo` continua carimbando
  `pyannote/wespeaker-voxceleb-resnet34-LM`, e **o banco de vozes não é
  re-extraído**. É o ativo que leva mais tempo para reconstruir, e ele não é
  tocado;
- **o VBx e os limiares.** `threshold: 0.6`, `Fa: 0.07`, `Fb: 0.8` vêm do
  `config.yaml`, não do código;
- **a CC-BY-4.0 exige crédito, e o `ATRIBUICAO.md` já existe** ao lado dos
  pesos, montado pelo `empacotar_modelos_de_diarizacao.sh`. O código vendorizado
  do pyannote.audio entra com a sua própria licença e a sua própria seção.

---

## 5. A régua de saída

Nenhum degrau da limpeza acontece antes de as quatro fecharem. É a regra do
[CONVERGENCIA.md](CONVERGENCIA.md): *nada é apagado antes de o substituto ter
ganhado na régua, sobre o acervo*.

| | o quê | critério |
|---|---|---|
| **V1** | o RTTM da gravação de 14,6 min | **mesmo número de falantes**, e **mesma atribuição de falante em ≥99% do tempo falado** contra a saída torch+CUDA de hoje (404 trechos, 3 falantes) |
| **V2** | as 4 gravações do acervo com Gemini | `tools/comparar_com_gemini.py` **não piora** em nenhuma |
| **V3** | os vetores de voz | `tools/medir_vetor_onnx.py` adaptado ao `community-1`: **0 decisões diferentes** em 0,55 · 0,60 · 0,70 |
| **V4** | a suíte | **os 628 testes passam sem alteração** |

**Por que o `V1` não pede saída idêntica.** A segmentação diverge 5,5e-03 entre
ONNX e torch, e isso desloca fronteira perto do limiar de decisão: dois quadros
podem cair do outro lado e mudar um começo de trecho em alguns milissegundos.
Exigir igualdade byte a byte reprovaria um porte correto. **O que importa é a
decisão de falante**, que é o que o núcleo consome pelo `assign_speakers` por
sobreposição temporal — e é isso que o critério mede.

**Se um teste do `V4` precisar mudar, o corte pegou osso.** É a mesma régua do
`C0`, quando o MOSS saiu: 628 testes passaram sem alteração, e foi o que provou
que a bifurcação inteira era removível.

### Só depois disso é que o peso cai

```
sai   torch/lib                      3,6 GB   torch_cuda 912 MB, cudnn ~975 MB,
                                              cublasLt 450 MB, cufft, cusparse,
                                              cusolver, curand
sai   a árvore científica           ~1,2 GB   pytorch_lightning, torchmetrics,
                                              torchaudio, sympy, scipy, sklearn
entra onnxruntime-gpu               ~250 MB
entra os dois modelos                 32 MB
──────────────────────────────────────────────
                                    ~4,5 GB   dos 18 GB
```

**E o `C2` vem junto, de graça.** O [CONVERGENCIA.md](CONVERGENCIA.md) §1 dizia
que havia 1,44 GB de DLL de CUDA *"duplicada byte por byte"* em três lugares.
**Isso está errado** — conferido em 18/09/2026, elas têm tamanhos diferentes,
porque são versões diferentes de CUDA (12.4 do torch contra 12.9 dos wheels
`nvidia/*`) e builds diferentes de ggml:

```
cublasLt64_12.dll    450,9 MB (torch)   451,6 MB (ata/bin)   637,7 MB (nvidia/)
cublas64_12.dll       95,4 MB            95,5 MB              97,8 MB
ggml-cuda.dll        186,4 MB (transcribe_cpp)   512,9 MB (ata/bin)
nvrtc64_120_0.dll     42,7 MB (torch)    85,7 MB (nvidia/)
```

Deduplicar por link nunca foi possível. **Mas sem o `torch/lib`, a terceira
cópia simplesmente deixa de existir** — e é a maior delas que sai.

---

## 5.1 — as réguas, medidas

Medido em 21/09/2026. A tabela do §5 dizia o que se pretendia medir; esta
seção diz o que se mediu. As quatro fecharam.

**V1 — o RTTM da gravação de 14,6 min, contra a saída torch+CUDA de
18/09/2026.** Acordo **1,0000**, nos dois cálculos: só sobre o falado do
gabarito, e sobre a união que inclui os ~205 s de silêncio. 404 trechos × 404,
3 falantes × 3. O segundo número é o que importa mais: ele diz que o ONNX
também não carimba falante onde o torch não ouve ninguém. 836 s em CPU.

**V2 — as quatro gravações do acervo com `gemini.md`.** Acordo **1,0000 nas
quatro**, contagem de trechos idêntica e tempo por falante idêntico ao décimo
de segundo entre torch e onnx. Contagem de falantes contra o Gemini, igual nas
quatro, nos dois motores.

| gravação | duração | trechos | falantes | torch | onnx |
|---|---:|---:|---:|---:|---:|
| 2026-08-21_11-00-33 | 7,7 min | 137 | 6 | 270 s | 703 s |
| 2026-08-25_08-59-22 | 14,6 min | 404 | 3 | 170 s | 684 s |
| 2026-08-20_15-59-20 | 32,1 min | 889 | 3 | 652 s | 1500 s |
| 2026-08-27_15-28-37 | 48,5 min | 805 | 3 | 1433 s | 2566 s |

**Os tempos desta tabela não são comparáveis entre si.** O torch rodou em
**CUDA** e o ONNX em **CPU** — nesta rodada de 21/09/2026 o `onnxruntime-gpu`
ainda não tinha sido medido (Ruling C5 do pré-voo fixava CPU por cautela), e o
`sessao.py` avisou a queda para CPU no stderr em cada execução. Ler 170 s
contra 684 s como "o porte é 4x mais lento" era uma leitura enganada pela
tabela: os dois lados não estavam na mesma pista. **Isso mudou em 22/09/2026**
— a V5 abaixo mede os dois lados em CUDA, e é essa a comparação honesta de
velocidade hoje; a V3, com os dois lados em CPU, continua valendo para o que
mede (o banco de vozes).

**V3 — as decisões de voz, 49 vozes, 1176 pares.** **0 discordâncias** nos
três limiares 0,55 · 0,60 · 0,70. Cosseno mínimo torch × ONNX
**0,999999992** (mediano 0,999999999984), maior diferença **2,19e-05**.
Velocidade sobre 2664 s de áudio, os dois lados em CPU: torch 573,42 s (4,6x o
tempo real), numpy+onnx 206,64 s (12,9x) — o ONNX é 2,8x mais rápido nesta
comparação, que é justa porque ninguém está em GPU.

Esta é a régua que decide se o banco de vozes precisa ser re-extraído — era o
item que o plano marcava como "pare e leve ao dono do produto" se desse
diferente. **Deu zero, e o banco não precisa ser re-extraído.**

**V4 — a suíte do C#.** **626 passed**, e o número importa menos que o fato
por trás dele: este branch **não toca um único arquivo `.cs`**. O `628` que o
brief e o `CLAUDE.md` citam está desatualizado em relação à base deste branch
— conferido por `git stash` e por `git diff --name-only main...HEAD` (Tarefa
7). Não é regressão: é a mesma régua do `C0`, na forma mais forte possível —
nenhum teste C# mudou porque nenhum arquivo C# mudou.

**As quatro réguas fecharam. Nenhuma reprovou.** O que o §5 pedia como
critério de saída para começar a limpeza está, hoje, satisfeito pelo número
medido — mas a decisão de começar a limpeza continua sendo do dono do
produto, e este documento não a toma (ver "O que este plano NÃO faz, de
propósito", no fim do plano de implementação).

**V5 — o CUDA EP, medido, e a fixação em CPU revogada.** A revisão de ponta a
ponta apontou que `preferir_gpu=True` estava fixo no `motor.py` sem nenhuma
régua ter exercitado o provedor CUDA do onnxruntime, e a resposta cautelosa
foi fixar `preferir_gpu=False`. O dono do produto então trouxe um requisito
que não estava no plano — o pipeline inteiro precisa rodar mais rápido que o
tempo real — e lembrou que o objetivo sempre foi "substituir o torch pelo
ONNX Runtime **com provedor CUDA**": CPU era o caminho de cautela, não a
arquitetura. Medido em 22/09/2026, mesma gravação de 14,6 min e mesmo
gabarito do V1:

| caminho | tempo | x tempo real | acordo |
|---|---:|---:|---:|
| ONNX CPU | 684 s | 1,28x | 1,0000 |
| torch CUDA | 170 s | 5,17x | (é o gabarito) |
| **ONNX CUDA** | **74,4 s** | **11,80x** | **1,0000** |

Carregar o pipeline em GPU levou 1,1 s. 404/404 trechos, 3 falantes, acordo
**1,0000** — a preocupação de reprodutibilidade que motivou a fixação em CPU
(o torch desliga TF32; o onnxruntime não tem o equivalente documentado) não
se confirmou neste modelo. **Como nos outros três, o provedor efetivo foi
conferido com `get_providers()`, não só pedido** — é a mesma disciplina que
evitou registrar o falso 32,11x de 18/09/2026 (§1).

**A armadilha de CUDA 12 contra CUDA 13 continua valendo, e quase repetiu a
de 18/09.** O CUDA EP só carrega com um build **CUDA 12** do
`onnxruntime-gpu` — `1.23.2` funciona. A versão `1.30`, que `uv`/`pip`
resolvem por padrão, exige **CUDA 13**, falha com
`libcublasLt.so.13: cannot open shared object file` e **cai para CPU em
silêncio**. É a mesma armadilha do §1, e é a razão inteira de o `sessao.py`
existir: ele anunciou a queda no stderr em vez de deixar esta medição cronometrar
CPU achando que era GPU.

**O custo de ligar isto é o wheel, não o runtime.** A instalação Windows do
app já embarca as bibliotecas CUDA 12 de que o EP precisa —
`cublas64_12.dll` e `cublasLt64_12.dll` (do motor de ata) e `cudnn64_9.dll`
(do CTranslate2, o ASR). O que falta acrescentar é o `onnxruntime-gpu`
(~250 MB, já contado no orçamento do §5), não ~2 GB de runtime CUDA novo.

Isto revoga a fixação em CPU: `preferir_gpu` volta a `True` no `motor.py`.
`motor_de_diarizacao` continua em `"torch"` por padrão — trocar o padrão para
`"onnx"` é decisão do dono do produto, e depende da limpeza (instalar o
`onnxruntime-gpu` no Python embarcado, fora do escopo desta medição).

**Um risco em aberto: nenhum número desta seção rodou no runtime que o app usa.**
V1, V2 e V3 rodaram todos no venv de desenvolvimento (WSL), não no Python
embarcado que o instalador entrega. Medido em 21/09/2026:

| | venv de dev (onde V1/V2/V3 rodaram) | Python embarcado (o que é distribuído) |
|---|---|---|
| Python | 3.13.12 | 3.12 |
| onnxruntime | 1.23.2 | 1.29.0 |
| numpy | 2.2.6 | 2.5.2 |
| scikit-learn | 1.8.0 | 1.9.0 |
| provedor CUDA | ausente | ausente |
| SO | Linux/WSL | Windows |

Recorrer a V1 no Python embarcado é o item mais barato desta lista que ainda
pode mudar a resposta — ~15 min, sem precisar de torch, com os artefatos já
na pasta do modelo instalado. **Este plano deliberadamente deixou isso fora
do escopo** (a medição foi restrita ao venv do WSL), e por isso fica escrito
aqui: é o risco mais barato que continua aberto antes de aprovar a remoção do
torch.

Uma segunda consequência da mesma tabela: o Python embarcado só tem o
onnxruntime **CPU-only** instalado hoje — o `onnxruntime-gpu` que o V5 mediu
não foi empacotado nele, e empacotá-lo é a limpeza, fora do escopo desta
medição. Até lá, dos números da tabela de V2 acima, os 2566 s da gravação de
48,5 min continuam sendo a figura que uma pessoa realmente experimentaria no
app publicado, mesmo com `preferir_gpu=True` no código — não porque o CUDA EP
não funcione (o V5 mostra que funciona, a 11,80x), mas porque o pacote que o
habilita ainda não está na instalação.

**Achado de 22/09/2026, para quem planejar a limpeza: o CUDA EP do ONNX precisa
de cinco famílias de DLL, e nem todas sobrevivem sem `torch/lib`.** Medido na
instalação real: `cublas64_12.dll`, `cublasLt64_12.dll` e `cudart64_12.dll`
vêm de `motores/ata/bin`, e o `cudnn64_9.dll` principal vem do `ctranslate2` —
essas três famílias sobrevivem à remoção do torch. Mas `cufft64_11.dll` e o
conjunto completo das sublibs do cuDNN (`cudnn_graph64_9.dll`,
`cudnn_cnn64_9.dll`, `cudnn_ops64_9.dll`, ...) **hoje não têm outra fonte na
instalação além de `torch/lib`** — nenhum outro pacote embarcado os traz.
Apagar `torch/lib` na ordem que a tabela do §5 conta com derrubaria de volta
para CPU o motor que este porte inteiro existe para acelerar, a menos que a
limpeza empacote essas duas famílias explicitamente antes de tirar o torch.
**Não é para consertar agora** — é decisão e trabalho do dono do produto, mas
quem planejar a limpeza não pode descobrir isso depois de já ter apagado a
pasta.

**E os 7,91x medidos hoje não saem de uma instalação que os scripts do repo
produzem.** Para medir no runtime real, o `onnxruntime` 1.29.0 (CPU) que o
Python embarcado tinha foi substituído à mão pelo `onnxruntime-gpu` 1.23.2 —
o wheel `onnxruntime_gpu-1.23.2-cp312-cp312-win_amd64.whl`, uma build CUDA 12
(o backup do pacote CPU ficou em `C:\Users\andre\ort-cpu-backup`); e as DLLs
acima (`cublas64_12.dll`, `cublasLt64_12.dll`, `cudart64_12.dll`,
`cufft64_11.dll` e o conjunto de cuDNN) foram copiadas à mão para
`site-packages/onnxruntime/capi/`, vindas de `motores/ata/bin/` e de
`torch/lib`. Nenhum dos dois passos está em `tools/empacotar_motores.sh` nem
em `tools/montar_instalador.sh` hoje — a medição é real, mas um instalador
gerado pelo repo como está não chega a este estado sozinho.

**Armadilha à parte, para quem for empacotar o `onnxruntime-gpu`:** a versão
que `pip`/`uv` resolvem por padrão hoje é a 1.30, que pede CUDA 13. Ela falha
ao carregar (`libcublasLt.so.13: cannot open shared object file`) e cai para
CPU **em silêncio** — é o mesmo tipo de queda muda que motivou este arquivo
existir (ver o topo). A 1.23.2 é uma build CUDA 12 e é a que bate com o que o
app já embarca; fixar a versão no empacotamento não é opcional.

---

## 6. O que este porte NÃO resolve

**A CUDA fica, e o bloqueio é o `large-v3`.** O `faster-whisper` roda em
CTranslate2, que **só tem CUDA e CPU** — não tem Vulkan nem DirectML, e embarca
o seu próprio `cudnn64_9.dll`. Enquanto a passada final for o `large-v3`, os
~926 MB de wheels `nvidia/*` ficam. O `onnxruntime-gpu` deste porte reaproveita
exatamente esses wheels, então ele não acrescenta dependência nova.

**O único candidato sem CUDA é o `whisper.cpp`/Vulkan**, e ele **não foi
medido** — a máquina não tem toolchain (sem Visual Studio, sem `cmake`, sem
Vulkan SDK; o WSL não tem ICD da NVIDIA, só Mesa), e o `whisper.cpp` não publica
binário Vulkan para Windows x64. O que se sabe sem medir, e recomenda ceticismo:

- a issue **#3351** relata o `large-v3` no Vulkan **repetindo o mesmo trecho por
  35 minutos** em RTX no Windows, com os outros modelos funcionando. Fechada
  como *not planned*;
- a **#3047** relata Q8_0/Q5 no Vulkan devolvendo lixo. Fechada como *not
  planned*. Isso obrigaria FP16 (3,1 GB), sem economia no modelo.

Fica adiado com gatilho claro: **medir antes de qualquer decisão sobre tirar a
CUDA**, e a medição custa instalar toolchain num disco que está em 98%.

**O `ggml` do `ata/bin` não tem Vulkan** — só `ggml-cuda` e os `ggml-cpu-*`. O
`transcribe.cpp` da legenda **tem** (`ggml-vulkan.dll`). Então, mesmo com o ASR
resolvido, o motor de ata precisaria de recompilação. É o `S2`/`C6`.

---

## 7. Fontes

- [CONVERGENCIA.md](CONVERGENCIA.md) — §1 (o peso), §2 (o acoplamento do vetor
  de voz), §3 (`C2` e `C5`), §4 `S1` (o vetor fora do torch)
- [SIDECAR.md](SIDECAR.md) — o protocolo que este porte não muda
- [VOZES.md](VOZES.md) §7 — por que o vetor carimba o modelo
- `tools/medir_vetor_onnx.py` — o `S1`, e o molde da exportação
