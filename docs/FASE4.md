# Fase 4 — o instalador: carta de execução

Escrita em 14/08/2026, no dia em que a Fase 3 fechou. As fases 0 a 3 entregaram
**um app que grava, transcreve e escreve a ata**. Esta entrega o que falta para
ele existir fora desta máquina: um instalador, uma versão com nome, e a primeira
release.

As cartas anteriores são [FASE1.md](FASE1.md), [FASE2.md](FASE2.md),
[FASE2.5.md](FASE2.5.md) e [FASE3.md](FASE3.md). O que a Fase 3 deixou
explicitamente para cá está em [FASE3-HANDOFF.md](FASE3-HANDOFF.md) §4.

**Esta fase não muda o que o app faz.** Ela muda como ele chega. É a régua para
tudo o que segue: se um item exige mexer no pipeline, na gravação ou na
interface, ele não é desta fase.

---

## 1. O que se instala hoje, medido

Medido na instalação real, em 14/08/2026:

| peça | tamanho | de onde vem |
|---|---|---|
| `MeetingApp.exe` | 18,5 MB | `tools/publicar.sh` |
| `WebView2Loader.dll` | 166 KB | idem — não entra no single-file |
| `motores/python` | **4,3 GB** | `tools/empacotar_motores.sh` |
| `motores/{asr,diarizacao,modelos}/motor.py` | 20 KB | `publicar.sh` |
| `motores/ata/bin` | **1,1 GB** | `tools/empacotar_motor_de_ata.sh` |
| `motores/ata/modelos` | 3,4 GB | idem, e a tela de Modelos |
| cache do HuggingFace (`%USERPROFILE%\.cache`) | ~3,0 GB | baixado na 1ª transcrição |
| **total** | **~8,7 GB** | |

Dentro do `motores/python`, o `torch` sozinho é **3,6 GB**, e dentro dele as DLLs
de CUDA são quase tudo:

```
torch_cuda.dll                     913 MB
cudnn_engines_precompiled64_9.dll  562 MB
cublasLt64_12.dll                  451 MB
cufft64_11.dll                     279 MB
cusparse64_12.dll                  263 MB
torch_cpu.dll                      240 MB
cudnn_adv64_9.dll                  231 MB
```

Em `motores/ata/modelos` há **dois** GGUF — o 4B (2,5 GB) e o 1.7B (1,1 GB). Só
o 4B é o padrão; o outro está lá porque foi baixado para comparar.

---

## 2. As quatro decisões do dono do produto, tomadas em 14/08/2026

| # | pergunta | resposta |
|---|---|---|
| 1 | quem recebe? | **release local, entregue direto a alguns amigos** para testar |
| 2 | o que vai dentro? | **binários dentro, modelos na 1ª execução** |
| 3 | onde instala? | **`%LOCALAPPDATA%\Programs\MeetingApp`, por usuário** |
| 4 | máquina sem NVIDIA? | **só CUDA**, como hoje |

Cada uma fecha uma pendência que estava aberta desde o [PLANO.md](PLANO.md) §5.

**(1) A audiência.** A primeira ideia era publicar no GitHub Releases, e ela foi
descartada no mesmo dia — corretamente. Público significa que o token do
HuggingFace e o segredo OAuth do Google, hoje embutidos no binário, vazam para
qualquer um que rode `strings`. Entregar a poucos amigos conhecidos mantém os
dois num círculo pequeno, e ainda assim §6 tira o do HuggingFace de circulação,
porque ele passou a ser barato de remover.

Uma consequência que ajuda: **sem GitHub Releases, some o limite de 2 GiB por
arquivo.** Ele obrigaria a partir o instalador em fatias (`DiskSpanning` do Inno)
ou a hospedar o payload fora. Entregue por link direto, o instalador pode ser um
arquivo só, do tamanho que for.

**(2) O payload.** Os binários — app, Python dos motores, llama.cpp — vão dentro.
Os **modelos** não: eles são 5,9 GB dos 8,7, e a tela de Modelos já sabe baixá-los
com barra de progresso e verificação de tamanho ([Catalogo.cs](../app-net/Nucleo/Catalogo.cs)).
É o que o PLANO §5 sempre disse, e é o que mantém o instalador em algo que se
manda por link.

O que sobra para dentro do instalador: **~5,4 GB brutos**. É esse número que a §5
ataca.

**(3) O local.** Por usuário, em `%LOCALAPPDATA%\Programs\MeetingApp`, e não em
`Program Files`. Dois motivos, e o segundo é o que decide:

- não pede UAC, e um app de bandeja que exige elevação para instalar assusta mais
  do que precisa;
- **a pasta continua gravável.** O GGUF da ata é baixado para
  `motores/ata/modelos`, ao lado do `llama-server` que o abre por caminho. Em
  `Program Files` esse download falharia com acesso negado, e consertar isso
  significaria mexer em `Catalogo.PastaDosModelosDeAta()` e em
  `MotorDeAta.cs` — pipeline, que esta fase não toca.

**(4) O acelerador.** Só CUDA. Vulkan custaria um segundo build do llama.cpp,
o runtime Vulkan do ASR e uma detecção de placa que escolhe o pacote — e os
amigos que vão testar têm placa NVIDIA. Fica registrado como pendência viva:
**a primeira máquina sem NVIDIA reabre esta decisão.** O que a fase entrega no
lugar é honestidade: §7 exige que o app diga que caiu para CPU, em vez de parecer
travado por vinte minutos.

---

## 3. Item 1 — a versão

Hoje o app não tem versão. O `.csproj` não declara `<Version>`, então o binário
sai como `1.0.0.0`, e nenhuma tela diz qual build está rodando. Isso é tolerável
enquanto só existe uma máquina e ela é a de quem compila; deixa de ser no dia em
que um amigo escreve "está dando erro" e não há como saber sobre o quê.

- `<Version>` no `MeetingApp.App.csproj`, e o esquema **0.x** — `0.1.0` na
  primeira release. Zero-major é a verdade: o formato em disco ainda pode mudar;
- a versão aparece **na tela de Ajustes**, junto de um botão que copia um bloco
  de diagnóstico (versão, GPU detectada, modelos instalados, pasta das gravações).
  É o que transforma um relato vago em um relato acionável;
- o instalador carrega a mesma versão, para que "Aplicativos Instalados" do
  Windows mostre o número certo;
- `CHANGELOG.md` na raiz, em português, escrito para quem usa e não para quem
  compila.

---

## 4. Item 2 — o fim do token do HuggingFace

Hoje o binário publicado carrega um token de leitura da conta HuggingFace de quem
publica, embutido como recurso ([Transcritor.cs](../app-net/Nucleo/Transcritor.cs) `TokenDoHuggingFace`).
Ele existe por um motivo bom: exigir conta no HuggingFace, aceite de termos e
geração de token é trabalho de desenvolvedor, e o dono do produto decidiu, com
razão, que quem grava reunião não deve passar por isso.

**Medido em 14/08/2026, o motivo encolheu.** Dos quatro modelos que o app baixa,
só **um** tem portão:

| modelo | portão | licença |
|---|---|---|
| `Systran/faster-whisper-large-v3` | não | MIT |
| `pyannote/wespeaker-voxceleb-resnet34-LM` | **não** | CC-BY-4.0 |
| `unsloth/Qwen3-4B-Instruct-2507-GGUF` | não | Apache-2.0 |
| `pyannote/speaker-diarization-community-1` | **sim** (`gated: auto`) | **CC-BY-4.0** |

O que está com portão são **32 MB**, sob uma licença que permite redistribuir com
atribuição. E o `config.yaml` do pipeline referencia os pesos por `$model/...` —
caminho relativo à própria pasta, não repositório remoto:

```yaml
segmentation: $model/segmentation
embedding:    $model/embedding
plda:         $model/plda
```

Então o desenho é: **o pipeline de diarização vai dentro do instalador**, em
`motores/diarizacao/modelos/community-1/`, e o motor o carrega por caminho local
em vez de por nome de repositório. O token sai do `.csproj`, do `publicar.sh` e
do `Transcritor.cs`.

Três ganhos, e o terceiro não é sobre segredo nenhum:

1. nada secreto viaja no binário entregue;
2. some a dependência de portão — se o HuggingFace mudar as condições de acesso
   do `community-1`, as instalações já entregues não param;
3. **a diarização deixa de precisar de rede na primeira execução.** Hoje ela
   baixa 32 MB de um repositório com portão no meio do primeiro pipeline; passa a
   estar em disco desde a instalação.

**O risco, e como ele foi medido — ✅ aprovado em 14/08/2026.**
`Pipeline.from_pretrained` com caminho local é suportado, mas não era o caminho
que este projeto exercitava. O critério era objetivo: a mesma gravação, diarizada
pelos dois caminhos, tinha que produzir **os mesmos falantes nos mesmos
instantes**, sob pena de o item ser revertido e o token ficar.

`tools/conferir_diarizacao_local.py` sobe o motor duas vezes sobre o mesmo
`system.wav` — uma com a pasta local, outra escondendo-a para forçar o
HuggingFace — e compara segmento a segmento. Na gravação de 13/08 (29 min):

| | segmentos | tempo |
|---|---|---|
| pasta local | 602 | 120 s |
| HuggingFace | 602 | 184 s |

**Idênticos**: mesmos instantes dentro de 50 ms, mesmos rótulos de falante, na
mesma ordem. Os 64 segundos a menos são o download que deixou de acontecer.

O `wespeaker` (26 MB, sem portão) entra junto pelo mesmo caminho, porque o ganho
3 só vale inteiro se nenhum dos dois precisar de rede.

O segredo do Google **fica** embutido, pela decisão (1). Mas há uma armadilha a
verificar antes de entregar, e ela não é de empacotamento: um app OAuth em modo
*Testing* só autoriza contas cadastradas como testadoras, e os refresh tokens
expiram em 7 dias. **Confirmar isso no console do Google antes de mandar o
instalador**, e, se for o caso, entregar com a agenda desligada e um recado
dizendo por quê — melhor que um amigo descobrir sozinho que reautoriza toda
semana.

---

## 5. Item 3 — emagrecer o payload

> **Executado em 14/08/2026**, a pedido do dono do produto, depois que o
> primeiro instalador saiu com 2,03 GB. O que segue é o que foi medido, e não o
> que se supunha.

A régua de cada corte: `tools/conferir_motores_curto.py`, que recorta 60 s de uma
gravação real, esconde as DLLs candidatas, sobe os dois motores e exige que os
dois **subam, achem a GPU e produzam saída**. Um minuto por experimento, contra
quinze de uma reunião inteira — e o que um trecho curto não pega (degradação de
qualidade ao longo do tempo) não é o modo de falha em questão: uma DLL ausente
não piora a transcrição, ela impede o processo de subir.

O "achou a GPU" é metade da régua. Sem ela um corte errado não quebra nada: o
torch cai para CPU em silêncio, tudo "funciona", e o app fica vinte vezes mais
lento na máquina de quem instalou.

### O que saiu

| corte | bruto | como se soube |
|---|---|---|
| **`motores/ata/bin` inteiro** | **1,1 GB** | decisão de desenho, §5.1 |
| `curand64_10.dll` | 63 MB | nada em `site-packages` a referencia; medido |
| `cusolverMg64_11.dll` | 78 MB | idem |
| `tests/`, `test/` | 63 MB | fixtures de pacote, não rodam na máquina de ninguém |
| `*.pyi` | 7 MB | stubs de tipo, só úteis a quem edita código |
| os dois `.gguf` | 3,6 GB | já eram excluídos: modelos baixam sob demanda |

### O que **não** sai, e por quê

- **`cudnn_engines_precompiled64_9.dll` — 589 MB.** Era o maior candidato
  isolado, e reprovou: sem ele a diarização morre com *"Could not locate
  cudnn_engines_precompiled64_9.dll"*. O `cudnn_engines_runtime_compiled` (8 MB)
  está ao lado, mas o cuDNN **não cai para ele** — falha e pronto;
- **`cufft` (279 MB), `cusparse` (263 MB), `cusolver` (110 MB), `cublas`
  (547 MB).** Não precisou de experimento: os quatro estão na **tabela de
  importações do `torch_cuda.dll`**, que é carregada quando o torch carrega. Sem
  qualquer um deles, `import torch` falha;
- **`sympy`, `pandas`, `matplotlib`.** Pareciam órfãos e não são: 86, 7 e 14
  arquivos do torch e do pyannote os importam. `scipy` e `sklearn` idem — a
  clusterização usa os dois;
- as DLLs **`ggml-cpu-*`** do llama.cpp, uma por arquitetura de CPU: 15 MB que
  são exatamente o que faz o motor rodar na máquina do amigo cujo processador
  não é este.

### 5.1 O motor de ata deixa de viajar

É o maior corte isolado, e o único que é decisão de desenho e não medição.

`motores/ata/bin` são **1,1 GB** — `ggml-cuda.dll` (513 MB) mais o cuBLAS que o
llama.cpp traz (547 MB) — para uma funcionalidade que nem toda instalação vai
usar. Ele passa a ser baixado sob demanda, pela mesma tela e pelo mesmo gesto que
os modelos já usavam: **641 MB** de duas releases oficiais do llama.cpp no
GitHub, uma vez só, quando a pessoa quiser a primeira ata.

Três coisas que isso resolve de uma vez:

- **–400 MB no instalador**, que é o arquivo que se manda por link;
- **nada é hospedado por nós.** A origem é a release oficial, conferível por
  quem quiser — o mesmo lugar de onde o `empacotar_motor_de_ata.sh` já baixava;
- **o cuBLAS duplicado deixou de ser um problema.** Havia um experimento previsto
  — apontar o `llama-server` para o cuBLAS do torch e poupar 547 MB — e ele saiu
  de cena: o que não viaja não precisa ser deduplicado. Fica anotado para o dia
  em que o tamanho do *download* incomodar.

O que ele custa, e está registrado: quem gerar a primeira ata espera 641 MB. A
tela diz isso antes, e `CaminhosDoMotorDeAta.OQueFalta()` passou de constatação
("o motor não está em `C:\...`") para instrução ("abra Ajustes › Modelos e
baixe-o — são 641 MB").

### O corte que não é desta fase

O `torch` inteiro — 3,6 GB, dois terços do que sobrou — existe **só para a
diarização**. Trocá-lo por ONNX (`sherpa-onnx`, 33 MB), que é o que o
[PLANO.md](PLANO.md) §5 sempre previu e a [FASE6.md](FASE6.md) §3.1 mantém em
aberto, levaria o instalador para perto de **300 MB**.

Não é uma poda, é a troca de stack que a Fase 0 mediu antes de fazer — e mexe
justamente na qualidade que o critério E acabou de proteger. Fica onde está.

---

## 6. Item 4 — o instalador

**Inno Setup 6**, um script em `instalador/MeetingApp.iss`. Não está instalado
nesta máquina; entra como pré-requisito de build, via `winget install JRSoftware.InnoSetup`.

O que ele tem que acertar, e cada linha aqui é um jeito conhecido de errar:

- **modo por usuário**: `PrivilegesRequired=lowest`,
  `DefaultDirName={localappdata}\Programs\MeetingApp`;
- **o app aberto**: `AppMutex=Global\MeetingApp` — o mutex já existe
  ([Program.cs:32](../app-net/App/Program.cs#L32)). Sem isso, instalar por cima de
  um app que está **gravando uma reunião** falha no meio da cópia. É a mesma
  lição que o `publicar.sh` aprendeu doendo, e a mesma frase vale: não matar o
  processo, pedir para fechar;
- **atalhos**: Menu Iniciar sempre; área de trabalho como opção;
- **iniciar com o Windows**: uma caixa marcada por padrão. Um gravador de reunião
  que não está aberto quando a reunião começa não grava nada;
- **desinstalar não apaga dado**: nem as gravações, nem
  `%USERPROFILE%\.meeting-transcription`, nem o cache de modelos. O desinstalador
  remove o que instalou e nada mais, e diz na tela o que está deixando para trás,
  com o caminho — quem quiser apagar, apaga sabendo;
- **atualizar por cima** de uma instalação anterior, preservando os modelos já
  baixados em `motores/ata/modelos`: eles não são arquivos do instalador e não
  podem ser removidos por ele;
- **WebView2**: embutir o bootstrapper e rodá-lo só se a runtime não estiver
  presente. Windows 11 já tem; Windows 10 quase sempre tem, pelo Edge — mas
  "quase sempre" na máquina de outra pessoa é uma tela em branco sem explicação.

### Três armadilhas medidas ao escrever o `.iss`

Nenhuma delas aparece na documentação do Inno com o sintoma que ela produz:

- **`const` dentro de função não existe** no Pascal Script. O erro é
  `'BEGIN' expected` na linha do `const`, que manda procurar no lugar errado;
- **`#` na primeira coluna vira diretiva de pré-processador**, mesmo dentro de
  uma string do `[Code]`. Uma linha continuada que começa com `#13#10` aborta a
  compilação com *Unknown preprocessor directive*. O `#13#10` fica grudado no fim
  da linha anterior;
- **o interop do WSL escapa as aspas** ao montar a linha de comando de um
  processo Windows. Como o `ISCC.exe` mora em `Inno Setup 6` — com espaço —, ele
  precisa de aspas, e o `cmd.exe` recebe `\"C:\...\ISCC.exe\"` e responde que não
  reconhece o comando. A saída é escrever um `.cmd` do lado de cá e executar o
  arquivo: as aspas nascem do lado Windows e ninguém as toca no caminho.

E o que fica **de fora, de propósito**: assinatura de código. Sem certificado, o
SmartScreen mostra o aviso de editor desconhecido. Com a audiência sendo amigos a
quem se entrega o arquivo pessoalmente, o custo certo é uma linha no recado
("vai aparecer este aviso; clique em Mais informações → Executar assim mesmo"),
e não um certificado anual. Reabre quando a audiência abrir.

### A instalação manual que já existe

Esta máquina tem o app em `C:\Users\andre\MeetingApp`, montado à mão pelo
`publicar.sh`, com os 8,7 GB de motores. O instalador aponta para outro lugar, e
instalar sem mais nada deixaria **duas** instalações, com dois `motores/` e dois
lugares para o ícone da bandeja sair.

O instalador detecta a pasta antiga e oferece **mover** os modelos já baixados —
5,9 GB que não faz sentido baixar de novo — antes de a antiga ser apagada. É um
caminho de código que roda uma vez na vida de uma máquina, então ele é
conservador: **move o que reconhece, não apaga o que não reconhece**, e o que
sobrar fica lá para o usuário conferir.

---

## 7. Item 5 — a primeira execução

Um instalador sem modelos entrega um app que abre bonito e falha na primeira
coisa que se pede. O que a fase acrescenta é o mínimo para que essa falha seja
uma instrução:

- ao abrir pela primeira vez sem modelo nenhum, o app leva para a tela de
  **Modelos**, com o que falta marcado e o tamanho de cada download ao lado. A
  tela já existe e já sabe fazer isso; o que falta é o app decidir mandar para
  lá;
- **transcrever sem o modelo baixado não pode morrer com erro de motor.** Hoje
  `Motores.OQueFalta()` cobre Python e scripts; ele passa a cobrir também o
  modelo, com a frase dizendo qual falta e onde baixá-lo;
- **a GPU dita em voz alta.** Ao subir, o app registra se achou CUDA. Sem placa,
  a tela de Modelos avisa que vai rodar em CPU e que uma reunião de uma hora leva
  horas — é a mitigação honesta da decisão (4);
- **espaço em disco**: pedir 6 GB de download sem conferir se cabem termina em
  download parcial marcado como "instalado". A verificação por tamanho já existe
  no `Catalogo`; falta perguntar ao disco antes de começar.

---

## 8. Item 6 — montar e entregar

`tools/montar_instalador.sh`, irmão do `publicar.sh` e com a mesma filosofia:
**réguas objetivas antes de produzir o artefato**, porque cada uma delas
corresponde a um defeito que já foi entregue.

O que ele faz, em ordem: roda os testes → `publicar.sh --so-build` → monta a
árvore de payload numa pasta de estágio (app + Python enxuto + llama.cpp +
modelos de diarização) → confere as réguas → chama o `ISCC.exe` → imprime o
tamanho final.

As réguas, que param antes de gerar:

| régua | o defeito que ela pega |
|---|---|
| `MeetingApp.exe` > 10 MB | as três flags de publicação não pegaram |
| ícones da bandeja embutidos | o app sobe sem menu e não dá para sair |
| `python.exe` + os três `motor.py` presentes | o app abre e não transcreve |
| `llama-server.exe` + `ggml-cuda.dll` presentes | a ata roda em CPU sem avisar |
| `community-1/config.yaml` presente | a diarização falha na primeira reunião |
| **zero ocorrências de `hf_token`** | o inverso da régua de hoje: agora a presença é o defeito |
| nenhum `.gguf` no payload | 2,5 GB entrando por engano no instalador |

**Os artefatos, medidos:**

| | primeiro (14/08) | depois dos cortes (15/08) |
|---|---|---|
| payload bruto | 5,4 GB | 4,1 GB |
| **instalador** | **2,03 GB** | **1,59 GB** |
| compressão | 24,5 min | 6,8 min |

O corte de 1,3 GB brutos rendeu **440 MB** no instalador — menos do que a razão
de 2,7× sugeria, e a diferença tem explicação: o que saiu (o `ggml-cuda.dll` e o
cuBLAS do llama.cpp) já era conteúdo que comprime pior que a média do payload.
Registrado porque a conta de guardanapo errou para o lado otimista, e a próxima
estimativa deve descontar isso.

A razão de 2,7× vem de o payload ser dominado por DLLs de CUDA, que comprimem
bem. Os 24 minutos são o preço de `lzma2/max`, e ele se paga: o instalador é
entregue por link, e cada 100 MB pesam mais na paciência de quem baixa do que um
build mais longo na de quem compila. É também por isso que o `--pular-build`
existe — ajustar o `.iss` não deve custar uma republicação.

O que sai junto do instalador, e é metade da entrega:

- **`docs/INSTALAR.md`** — para quem vai receber, não para quem compila: o que a
  máquina precisa ter, o aviso do SmartScreen, os downloads da primeira execução
  e quanto tempo levam, e o que fazer quando não houver placa NVIDIA;
- **o que reportar de volta.** Três amigos testando são três chances de descobrir
  o que esta máquina não descobre; sem pedir nada específico, o retorno vem como
  "achei legal". O recado pede o bloco de diagnóstico do item 1 e três perguntas:
  instalou sem travar? gravou a primeira reunião inteira? a ata fez sentido?

---

## 9. Critérios de aceite

Nenhum deles se verifica nesta pasta de build — todos exigem instalar.

**A. Máquina limpa.** Uma conta de usuário do Windows recém-criada, sem nada do
projeto: instalar, abrir, gravar 5 minutos, transcrever, gerar a ata. Sem etapa
manual, sem variável de ambiente, sem arquivo copiado à mão.

**B. Nada secreto no artefato.** `strings` no `.exe` e no instalador não acha
`hf_token`. O segredo do Google está lá por decisão, e isso está escrito.

**C. Atualizar não perde nada.** Instalar 0.1.0, gravar uma reunião, transcrever,
instalar 0.1.1 por cima: a gravação, a transcrição, as notas, os projetos, as
vozes e os modelos baixados continuam lá.

**D. Desinstalar não apaga reunião.** Depois de desinstalar, as gravações e
`%USERPROFILE%\.meeting-transcription` continuam intactos, e o desinstalador
disse onde estão.

**E. A diarização não regrediu.** A mesma gravação, com o pipeline vindo de
pasta local em vez do HuggingFace, produz os mesmos falantes nos mesmos
instantes. É o critério que autoriza o item 2 — se falhar, o token fica.

**F. O app aberto está protegido.** Instalar com o app gravando é recusado com
uma frase que diz o que fazer, e a gravação sobrevive.

**G. Um amigo instalou.** O critério que fecha a fase não é técnico: alguém que
não é quem escreveu o código instalou, gravou uma reunião de verdade e mandou o
retorno.

---

## 10. O que fica de fora, com o custo registrado

- **assinatura de código** — §6. Reabre quando a audiência abrir;
- **Vulkan e máquina sem NVIDIA** — decisão (4). Reabre na primeira máquina sem
  placa; o que existe até lá é o aviso do item 5;
- **atualização automática** — não há servidor de update, e não vai haver por
  causa de três amigos. Atualizar é receber o instalador novo e rodá-lo, o que o
  critério C já cobre;
- **ffmpeg embutido** (pendência 3 do [PLANO.md](PLANO.md)) — **encerrada por
  medição, não por decisão**: não há uma única referência a ffmpeg no código C#.
  O app nativo não importa vídeo; ele grava o próprio WAV. Os 80 MB não entram
  porque não servem a nada;
- **instalador para máquina de 32 bits, ARM ou Windows 10 antigo** — o alvo é
  Windows 10/11 x64, e é o que o `INSTALAR.md` vai dizer.

---

## 11. A ordem de execução

Cada item entrega algo sozinho, e a ordem é de risco crescente — o que pode
reprovar vem cedo, para reprovar barato.

1. **a versão** (item 1) — pequeno, e é o que torna qualquer relato utilizável;
2. **o token** (item 2) — o critério E pode reprová-lo, e é melhor descobrir
   antes de o instalador estar montado em cima dele;
3. **o emagrecimento** (item 3) — cada corte é medido sozinho e revertido sozinho;
4. **a primeira execução** (item 5) — antes do instalador, porque é ele que
   produz a máquina limpa onde isso aparece;
5. **o instalador** (item 4) e o **montar_instalador.sh** (item 6), juntos;
6. **`INSTALAR.md` e a entrega** — e aí a fase depende de um amigo, não de mim.
