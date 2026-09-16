# Convergência — das features ao software enxuto

Escrito em 10/09/2026, a pedido do dono do produto, com o peso da instalação
medido no mesmo dia.

**O que este documento é.** O plano de sair de onde estamos — todos os motores do
passado mantidos vivos para garantir que nada quebre — para **um só caminho, o
mais leve que passar nas réguas**. Ele cobre três coisas, nesta ordem, que é a
ordem que o dono do produto pediu e que é a certa:

1. **as features primeiro** — legenda ao vivo e busca na reunião, funcionando;
2. **o bake-off depois** — comparar com régua, sobre o acervo, o que ganha;
3. **a limpeza por último** — e só do que perdeu, com número.

**A regra que atravessa tudo, e é a única que não se negocia:**

> **Nada é apagado antes de o substituto ter ganhado na régua, sobre o acervo.**
> A instalação pesada é o preço de poder comparar. Cortar antes de medir é trocar
> peso por risco, e o risco cai na reunião de alguém.

O caminho até as features está na [FASE7-ROTA.md](FASE7-ROTA.md). Este documento
começa onde ela termina.

---

## 1. O peso, medido — e ninguém tinha esse número

`motores/` na instalação oficial, em 10/09/2026: **17 GB**.

| pasta | tamanho | o que é |
|---|---:|---|
| `ata/modelos` | **9,6 GB** | três GGUFs, e **só um está em uso** |
| `python/` | **5,5 GB** | o Python embarcado com torch, CUDA e o resto |
| `ata/bin` | 1,1 GB | o llama.cpp com CUDA |
| `moss/` | 0,67 GB | o GGUF do MOSS |
| `diarizacao/` | 88 MB | os modelos que substituíram o token |

Dentro do `python/`, os cinco que mandam:

| pacote | tamanho | existe por causa de |
|---|---:|---|
| `torch` | **3,6 GB** | pyannote — diarização **e vetor de voz** |
| `nvidia/*` | **926 MB** | wheels de CUDA do `transcribe.cpp` |
| `transcribe_cpp_native_cu12` + `_native` | 301 MB | MOSS, e amanhã a legenda |
| `ctranslate2` + `av.libs` | 123 MB | faster-whisper |
| `scipy`+`sklearn`+`sympy`+`pandas`+`matplotlib`+`PIL`+`networkx` | 273 MB | dependências do torch e do pyannote |

**Três gorduras, medidas, em ordem de facilidade:**

**1. 5,24 GB de GGUF de ata que ninguém usa.** O `app.json` aponta para o
`gemma-4-e4b-q4km.gguf` (4,98 GB). Os outros dois — `qwen3.5-4b` (2,74 GB) e
`qwen3-4b-instruct` (2,50 GB) — ficaram das comparações. **É espaço livre hoje,
sem teste nenhum**, e a única razão para guardá-los é poder repetir uma
comparação. Depois do bake-off, não há.

**2. 1,44 GB de DLL de CUDA literalmente duplicada.** O mesmo arquivo, byte por
byte, em três lugares:

```
cublasLt64_12.dll     3x   (torch/lib · nvidia/cublas/bin · ata/bin)
cublas64_12.dll       3x
ggml-cuda.dll         2x   (ata/bin · transcribe_cpp_native_cu12)
nvrtc64_120_0.dll     2x
ggml-vulkan.dll       2x
```

O `DIST-1` do [BACKLOG.md](BACKLOG.md) já tinha identificado 927 MB disso pelo
lado dos wheels. O número real é maior, e **a terceira cópia é do llama.cpp**,
que o `DIST-1` não olhava.

**3. O `torch`, com 3,6 GB, e é o assunto do §2.**

---

## 2. O acoplamento que decide a simplificação inteira

**Este é o achado desta auditoria, e não estava escrito em lugar nenhum.**

A pergunta óbvia é: se o MOSS faz texto e falante numa passada, e a legenda vier
do Nemotron, **por que ainda precisamos do torch?** Ele é 3,6 GB, mais 926 MB de
CUDA, mais ~273 MB de dependências científicas: **4,8 GB, 28% da instalação.**

A resposta é uma operação só, e é fácil de não ver:

```
CosturaDeFalantes  ─┐
                    ├─►  op "voz"  ─►  motores/diarizacao/motor.py
AprendizadoDeVozes ─┘                  └─ pyannote/wespeaker-voxceleb-resnet34-LM
                                          └─ pyannote ─► torch (3,6 GB)
```

**O `vetor_de_voz` é o que transforma rótulo em pessoa**, e ele é a coisa mais
valiosa que o app tem:

- é ele que costura o `S1` do bloco 3 com o `S1` do bloco 7
  ([CosturaDeFalantes.cs](../app-net/Nucleo/CosturaDeFalantes.cs));
- é ele que faz a pessoa **ter nome na próxima reunião**
  ([AprendizadoDeVozes.cs](../app-net/Nucleo/AprendizadoDeVozes.cs)).

Ou seja: **trocar o ASR e a diarização não tira o torch.** Enquanto o vetor de
voz vier do pyannote, os 4,8 GB ficam, mesmo que o `faster-whisper`, o
`ctranslate2` e o pipeline de diarização saiam inteiros.

**Duas coisas boas, que tornam isto tratável:**

- **a costura já recebe o extrator como delegado.** `CosturaDeFalantes` toma um
  `ExtratorDeVoz`, e não um sidecar — a costura foi escrita sem saber de onde o
  vetor vem. **A emenda já existe;**
- **o app já sabe migrar de modelo de voz.** `VozExtraida` guarda `Vetor` **e**
  `Modelo`, com a regra escrita de que vetores de modelos diferentes não se
  comparam — e a casa já fez essa migração uma vez, em 20/08/2026, quando a
  geração 1 saiu de circulação.

**Daí sai o teste mais valioso de toda a simplificação**, e é o `S1` do §4.

---

## 3. A ordem, em três fases

### Fase A — as features funcionando

Nada de otimizar aqui. O objetivo é ter **o produto na tela**, com os motores de
hoje intactos e comparáveis.

| # | o quê | onde está escrito |
|---|---|---|
| **A1** | a legenda ao vivo | [FASE7-ROTA.md](FASE7-ROTA.md) §4, passos `R0`–`R5` |
| **A2** | a busca na reunião, com LLM pequeno | §5 deste documento |
| **A3** | pôr tudo no git e soltar a `0.7.0` | [FASE7-ROTA.md](FASE7-ROTA.md) §2 |

**Termina quando:** uma reunião real é gravada com legenda na tela, blocos por
baixo, uma pergunta respondida no meio, e a passada final por cima — tudo numa
sessão só, sem reiniciar nada.

### Fase B — o bake-off

**As réguas já existem e já mediram a 0.6.1**, o que é o que torna esta fase
barata: `tools/comparar_com_gemini.py` (falante), `tools/wer_contra_gemini.py`
(texto), `tools/auditar_atas.py` (a ata), e o acervo com as quatro gravações que
têm Gemini em paralelo.

**O que se compara, e é a tabela que decide a limpeza:**

| eixo | competidores |
|---|---|
| texto | `faster-whisper large-v3` · MOSS em bloco · Nemotron streaming |
| falante | `pyannote` (janela cumulativa) · MOSS + costura |
| vetor de voz | `wespeaker` via torch · o candidato do `S1` |
| ata e busca | `gemma-4-e4b` · `qwen3-1.7b` |

**Duas disciplinas que fazem a diferença entre um bake-off e uma opinião:**

- **o mesmo áudio, as quatro gravações com Gemini, sempre.** As ressalvas da
  [FASE7-RESULTADOS.md](FASE7-RESULTADOS.md) §0.5 continuam valendo inteiras — o
  Gemini não é gabarito, e só a comparação entre colunas do mesmo áudio tem
  sentido;
- **o custo entra na tabela junto com a qualidade.** VRAM, ciclo de GPU e MB de
  instalação por competidor. Um empate de qualidade é decidido pelo peso, e é
  esse o ponto do exercício.

**Termina quando:** cada linha da tabela tem um vencedor, ou um empate declarado
com o peso como desempate.

### Fase C — a limpeza, em degraus por risco

Só depois da Fase B, e nesta ordem — **do que não pode dar errado para o que
pode:**

| # | corte | ganho | risco |
|---|---|---:|---|
| **C0** | **o MOSS inteiro** — GGUF, sidecar, `MossEmBlocos`, a costura | **0,67 GB** | baixo — **nada nasce ligado nele**, e a decisão já está tomada (abaixo) |
| **C1** | os dois GGUF de ata que perderam | **~5,2 GB** | nenhum — não estão em uso |
| **C2** | deduplicar as DLLs de CUDA | **~1,4 GB** | baixo, mas **empírico**: só se responde em máquina Windows com placa |
| **C3** | o `faster-whisper` + `ctranslate2` + modelos, se o Nemotron ganhar | ~123 MB + os modelos | **médio** — é o motor de todo mundo hoje, e o `C0` tirou metade da premissa deste degrau |
| **C4** | o pipeline de diarização do pyannote, se a costura ganhar | parte dos 88 MB | médio |
| **C5** | **o torch inteiro**, se o `S1` passar | **~4,8 GB** | **alto** — é o §2, e o banco de vozes depende dele |
| **C6** | um ggml só para llama.cpp e transcribe.cpp | resto da duplicação | alto — é recompilar |

**Somados, C1+C2+C5 são ~11,4 GB dos 17 GB.** E o instalador — hoje 2,31 GB,
depois de o MOSS o levar de 1,59 GB — voltaria a caber no que o `DIST-1` precisa
para o winget.

**A régua de saída de cada degrau é a mesma:** os testes passam sem alteração, e
a régua do eixo correspondente não piora sobre o acervo. Se um teste precisar
mudar, o corte pegou osso.

### O MOSS é o primeiro a sair — decidido em 15/09/2026

**Quem decidiu foi o dono do produto, depois de ver a legenda funcionando em
reunião de verdade.** A tabela acima tinha sido escrita supondo o contrário — o
`C3` esperava o MOSS *ganhar* e o `faster-whisper` sair. Duas reuniões com
legenda na tela trocaram a pergunta: não é mais *qual motor de bloco é melhor*, e
sim **para que serve um motor de bloco**.

**O nicho dele ficou espremido dos dois lados:**

- **por cima**, a legenda ao vivo faz o "durante a reunião" melhor — quadro de
  200 ms contra 3 minutos de espera, e ela acompanhou o relógio em duas reuniões
  reais na 2060 (1,007× e 1,005×, `registro.log` de 15/09/2026);
- **por baixo**, a passada final continua sendo a verdade, e é ela que tem
  hotword, `RevisaoDeTermos` e o dono de graça pela faixa do microfone;
- **e os dois não rodam juntos.** Legenda + MOSS mede 0,45×, abaixo do tempo
  real ([FASE7-ROTA.md](FASE7-ROTA.md) §4). Ligar o MOSS *é* desligar a legenda,
  e o núcleo recusa em vez de atrasar em silêncio.

**O que já estava medido e pesa contra ele** — nada disto é novidade deste dia,
só mudou de peso:

- **ele não tem hotword, e não é configuração: é ausência.** Nenhum runtime ggml
  expõe o `initial_prompt` do MOSS ([FASE7-RESULTADOS.md](FASE7-RESULTADOS.md)
  §12.1). Sobre os 19 termos que o Gemini confirma terem sido ditos, o app com
  hotwords escreve 16/19 na forma canônica e o MOSS 11/19 — e `Cloud`,
  `versionamento` e `must-have` **não aparecem de forma nenhuma** na saída dele
  (§12.2). Palavra não transcrita não volta por pós-processamento;
- **os rótulos dele são locais ao bloco.** Quem os transforma em pessoa é a
  `Nucleo/CosturaDeFalantes.cs`: ela custa ~1 ponto de acerto (§11.1), e na
  gravação de 2 h divide **8 pessoas em 15 identidades** no limiar de 0,55
  (§11.2).

**O que se está abrindo mão, e convém dizer em voz alta:** o MOSS ganha do
pipeline atual no texto em 3 das 4 gravações **e** no falante (§7.4), fazendo os
dois numa passada só e 2,6× mais rápido (§7.3). Cortá-lo é escolher **hotword e
a passada final** em vez de **texto cru melhor em bloco**.

**O que reabriria a decisão**, e é por isso que o `C0` ainda espera a Fase B:

1. **um runtime ggml passar a expor o `initial_prompt`** — some o argumento
   principal;
2. **a legenda se mostrar insuficiente como camada 1 no uso diário.** Se o texto
   ao vivo não for legível o bastante para decidir sobre a passada final, o bloco
   de 3 min volta a ter função;
3. **a `CosturaDeFalantes` achar outro consumidor.** Hoje ela existe só para o
   MOSS e **sai junto** — é o único ativo medido que morre neste corte.

**O que o corte rende:** os 0,67 GB do GGUF, o `motores/moss/motor.py`, a
`Nucleo/MossEmBlocos.cs`, a `Nucleo/CosturaDeFalantes.cs` e a bifurcação do
`Transcritor` — a chave `motor_de_transcricao` volta a ter um valor só, e o
`TRA-2` do [BACKLOG.md](BACKLOG.md) deixa de precisar de um terceiro rótulo.
**O `transcribe_cpp_native_cu12` fica**, com seus 301 MB: é o runtime da legenda,
e sempre foi compartilhado.

---

## 4. As alternativas que valem testar depois do Nemotron

Em ordem de valor, e o primeiro vale mais que os outros quatro juntos.

### S1 · O vetor de voz fora do torch — ✅ **medido em 10/09/2026: passou**

> **O resultado, com `tools/medir_vetor_onnx.py` sobre o acervo:** 129 vozes reais
> de 25 gravações, extraídas pelos dois caminhos.
>
> ```
> cosseno torch vs ONNX     mínimo 0,999999992   mediana 1,000000000
> 8.256 pares de vozes      0 decisões diferentes em 0,55 · 0,60 · 0,70
> maior diferença de cosseno entre os dois caminhos      2,31e-05
>
> velocidade, 7.196 s de áudio, os dois em CPU
>   torch (pyannote)     1.167 s     6,2x o tempo real
>   numpy + onnx           463 s    15,6x o tempo real
> ```
>
> **E o ONNX é 2,5× mais rápido que o torch, em CPU.** Isso não era o que se
> procurava e muda uma conta: a costura extrai muitos vetores durante a reunião,
> e a 15,6× em CPU ela **não disputa a placa com a legenda nem com o bloco**.
> O orçamento de GPU do §3 da [FASE7-ROTA.md](FASE7-ROTA.md) sobra.
>
> **Nenhuma decisão de reconhecimento muda**, em nenhum dos três limiares que o
> app usa. O caminho ONNX é substituição direta, e **o banco de vozes não precisa
> ser re-extraído** — o modo de falha que este documento vigiava não ocorreu.
>
> O que a ferramenta descobriu, e que é o mapa para quem for implementar: o
> modelo **inteiro não exporta** (o `compute_fbank` do pyannote embrulha o fbank
> num `torch.vmap`, e nem o exportador antigo nem o dynamo o atravessam); **sem o
> vmap ele exporta**, e o wrapper de batch 1 é *bit-exato* contra o modelo oficial
> (`maxabs = 0.0`); o que sobra sem exportar é só o `aten::fft_rfft`, que o
> opset 17 não tem — daí o fbank em numpy, que é o que o `infer_onnx.py` do
> WeSpeaker já fazia. **A ResNet em ONNX tem 26,5 MB.**
>
> **O que isto ainda não decide:** o `C5` continua dependendo do `C3` e do `C4`.
> O pyannote também faz a **segmentação** do caminho clássico, e sair do torch
> inteiro exige que esse caminho tenha perdido no bake-off — não só que o vetor
> migre.

> **O MOSS não resolve isto, e não é questão de tempo.** A ABI do
> `transcribe.cpp` 0.2.3 expõe, do falante, exatamente
> `{t0_ms, t1_ms, speaker_id (int32), p (float)}` — **não há campo de vetor**, e a
> palavra *embedding* não aparece na API. E não é lacuna do port: o `S1` do MOSS é
> **relacional dentro da janela de atenção** ("esta é a mesma voz de 40 s atrás"),
> não uma impressão digital absoluta. É por isso que o rótulo é local ao bloco.
> **Reconhecer alguém três semanas depois exige um vetor**, e ele virá sempre de
> um modelo cujo ofício é produzir vetor. Ver §2.

**A pergunta não é qual modelo, e sim qual runtime — conferido em 10/09/2026:**

```
wespeaker-voxceleb-resnet34-LM/pytorch_model.bin     26,6 MB
torch + nvidia/* + scipy/sklearn/sympy/...           ~4,8 GB   ← para rodá-lo
```

**São 4,8 GB de framework para executar 26,6 MB de modelo — uma razão de ~180×.**
O peso nunca foi o modelo de voz. É o torch.

**Isso torna o corte muito mais barato do que parecia**, porque o caminho de menor
risco **não troca de modelo**: exporta-se o **mesmo** `wespeaker-voxceleb-resnet34-LM`
para ONNX. Mesmos pesos, mesma arquitetura — os vetores saem numericamente
equivalentes, e **o banco de vozes não precisa ser re-extraído**. O
`onnxruntime` já está instalado (40 MB).

**Como se mede, e a régua já existe:** extrair os vetores das gravações do acervo
pelos dois caminhos e comparar **reconhecimento**, não similaridade de vetor — é a
taxa de acerto de pessoa que importa. O `tools/medir_costura.py` já faz a
varredura de limiar; é ele com outro extrator.

**Dois modos de falha a vigiar:**

- **se o ONNX divergir numericamente mais do que o esperado**, o banco tem de ser
  **re-extraído** do áudio guardado, não convertido. O app já sabe fazer isso — o
  `VozExtraida` carimba o modelo, e a migração de 20/08/2026 é o precedente —,
  mas o custo sobe de uma tarde para uma migração;
- **o `pyannote` ainda faz a segmentação** do caminho clássico, além do vetor. Sair
  do torch inteiro depende de o caminho clássico ter perdido no bake-off (`C3` e
  `C4` antes do `C5`), e não só de o vetor migrar.

> **De quebra, 53 MB de graça:** dos três modelos de embedding em
> `diarizacao/modelos`, **dois são o mesmo arquivo** — `wespeaker-voxceleb-resnet34-LM`
> e `pyannote-3.1/embedding` têm o mesmo MD5. O `community-1` é outro.

### S1b · ONNX como runtime único de fala — *o candidato que apareceu em 11/09/2026*

**A pergunta que abriu isto:** o `llama.cpp` é obrigatório para rodar um ASR
novo? **Não** — há o caminho ONNX, e ele é melhor por outras razões.

> **Correção de 11/09/2026, e ela desfaz uma acusação injusta.** Este documento
> afirmou que o Qwen3-ASR pelo `llama.cpp` *"ecoou o próprio comando em vez de
> transcrever"*, e concluiu daí que a integração estava quebrada. **Estava
> errado: o erro era da medição.** A janela de áudio escolhida para o teste era
> um trecho quase silencioso da gravação (RMS 0,00006, pico 0,004), e os dois
> runtimes devolviam vazio pelo motivo certo — não havia fala.
>
> Refeito com uma janela de fala real, o **`llama.cpp` transcreve português
> corretamente**, e o `sherpa-onnx` também. A acusação cai.
>
> **O que sobrevive, e é o que de fato reprova o Qwen3-ASR como motor de
> transcrição: ele não devolve carimbo de tempo em runtime nenhum.** Pelo
> `llama.cpp` a API só tem `text`; pelo `sherpa-onnx` o resultado tem campo
> `timestamps`, e ele volta **vazio** para este modelo. A conclusão é a mesma; a
> razão é outra, e a razão importa.

**O candidato: `nvidia/parakeet-tdt-0.6b-v3` em ONNX.** Ele passa nas quatro
réguas do `DIST-5`, e numa quinta que matou os dois anteriores:

| | |
|---|---|
| português | **sim**, entre 25 línguas europeias, com detecção automática |
| **carimbo de tempo** | **palavra, caractere e segmento** — é o que faltou ao Nemotron e ao Qwen3-ASR |
| runtime | **ONNX Runtime**, que este app já embarca (40 MB) — **sem torch, sem transcribe.cpp** |
| tamanho | **~670 MB** em int8 (encoder 652 + decoder 18), contra ~3,1 GB de VRAM do `large-v3` |
| extras | pontuação e maiúsculas na saída |

**E há uma versão afinada para o português do Brasil**:
`alefiury/parakeet-tdt-0.6b-v3-ptBR-TAGARELA-onnx`, derivada da multilíngue.

**Por que isto é maior que trocar de modelo.** O `sherpa-onnx` (Next-gen Kaldi)
roda este modelo **e** faz diarização, VAD, pontuação e **vetor de voz** — tudo
em ONNX Runtime. Somado ao `S1`, que já provou o vetor de voz fora do torch,
aparece a possibilidade de **um runtime só para tudo o que é fala**:

```
hoje:   faster-whisper (CTranslate2) + pyannote (torch 4,8 GB) + transcribe.cpp (301 MB)
talvez: ONNX Runtime (40 MB) + ~670 MB de modelo
```

> **⛔ MEDIDO EM 11/09/2026 — perde do `large-v3` nas quatro.** Com
> `tools/medir_parakeet.py` e a régua `wer_contra_gemini.py`, sobre as quatro
> gravações com export do Gemini:
>
> ```
>                divergência          cobertura
>              parakeet    app     parakeet    app
>  7,7 min       36,6%   24,6%       64,6%   77,2%
> 14,6 min       25,9%   23,1%       77,0%   80,4%
> 32,1 min       34,3%   31,7%       67,8%   70,3%
> 48,5 min       26,3%   23,5%       75,9%   78,8%
> ```
>
> **Perde nas duas medidas, nas quatro.** E o modo de falha aparece nas parcelas:
> ele **perde mais fala** (244 palavras faltando contra 133 na de 7,7 min) e
> **erra mais palavra** (501 trocadas contra 340 na de 32 min). As duas coisas
> que a régua do projeto considera caras.
>
> **As três assimetrias da comparação, conferidas uma a uma** — porque um
> candidato reprovado por medição injusta é pior que um candidato não medido:
>
> | assimetria | veredito |
> |---|---|
> | **pós-processamento** — o texto do app passa por `CorrecaoFonetica` e `RevisaoDeTermos`, o do Parakeet não | **imaterial, conferido**: são **6 trocas em ~15.000 palavras** nas quatro gravações somadas, e 0 em três delas |
> | **bloco contra passada inteira** — o Parakeet em blocos de 3 min, o app numa passada | **a medir** (`--inteira`), em vez de argumentar por ordem de grandeza |
> | **quantização** — int8 contra fp16 | **medida, e era assimetria de verdade** — ver abaixo |
>
> **A quantização int8 era pior nos dois eixos, e o segundo é surpreendente.**
> Mesmo áudio de 7,7 min, mesmo modelo:
>
> ```
> int8  CPU    98,6s    4,69x   1226 tokens
> int8  GPU    75,2s    6,14x   1219 tokens
> fp32  CPU    99,2s    4,65x   1399 tokens
> fp32  GPU    20,4s   22,67x   1399 tokens
> ```
>
> * **o fp32 devolve 14% mais tokens** (1399 contra 1226) — o int8 estava
>   **perdendo conteúdo**, que é exatamente o modo de falha que a régua apontou;
> * **o int8 é 3,7× MAIS LENTO que o fp32 na GPU.** O ONNX Runtime avisa
>   (*"21 Memcpy nodes added"*): a quantização obriga converter na fronteira de
>   cada nó, e come o ganho inteiro. **Na GPU, int8 só atrapalha.**
>
> **E 22,67x contra os ~4,4x do `large-v3`** na mesma máquina: cinco vezes mais
> rápido, se a qualidade se sustentar.
>
> **A lição de método:** a reprovação do int8 estava certa como fato e errada
> como conclusão — eu quase descartei o candidato por uma escolha minha de
> empacotamento, não por propriedade dele. O pedido do dono do produto de
> *"comparações o mais justas possível"* é o que pegou isso.
>
> **O que este teste NÃO fecha, e é honesto dizer:** o Parakeet rodou em
> **int8**, contra o `large-v3` em fp16. Quantização custa acurácia, e o encoder
> em fp32 são 2,44 GB — ainda competitivo contra os ~3,1 GB de VRAM do
> `large-v3`. **Antes de descartá-lo vale uma rodada em fp32**; se ele perder
> também lá, perdeu por qualidade e não por quantização.
>
> O que continua de pé, e é o que motivou o teste: ele é o **único dos três
> candidatos que devolve estrutura de tempo** (409 tokens com carimbo por bloco),
> e roda em ONNX sem torch. Se a fp32 empatar, a troca vale pelo peso.

**O resto disto não foi medido**, e o histórico desta semana recomenda desconfiança: o
Nemotron e o Qwen3-ASR também pareciam ótimos no papel. **A régua é a mesma e
vem primeiro: segmentos por bloco.** A ferramenta é a
[`tools/comparar_asr.py`](../tools/comparar_asr.py).

### A regra que decide entre multilíngue e afinado — 11/09/2026

**O motor padrão tem de aguentar reunião internacional.** Decidido pelo dono do
produto: um modelo dedicado ao português não pode ser o único, porque há reuniões
em inglês e mistas.

Isso não descarta um afinado — **muda o papel dele**:

| | papel |
|---|---|
| **`parakeet-tdt-0.6b-v3`** (25 línguas, detecção automática) | **o padrão.** Tem de dar conta de qualquer reunião |
| **`parakeet-...-ptBR-TAGARELA`** | **opção**, se ganhar o bastante em português para justificar a escolha |
| `nemo-canary-1b-v2` | candidato a padrão, também multilíngue |

**E há um risco específico do afinado que precisa ser medido, não suposto:**
afinar em português pode **piorar o inglês** — e o caso real deste acervo não é
"reunião em inglês", é **reunião em português com termo técnico em inglês**
(*next best*, *lifecycle*, *sales coach*), que é exatamente onde a
[FASE7-RESULTADOS.md](FASE7-RESULTADOS.md) §12 já mediu perda de vocabulário.

Um afinado que escreva *"neksti besti"* onde o multilíngue escreve *"next best"*
teria ganhado na régua de palavra e perdido no que importa. **A régua de
vocabulário (`tools/medir_vocabulario_moss.py`) tem de ser aplicada ao afinado
antes de qualquer conclusão.**

> **Corrigido em 11/09/2026: o acervo TEM uma reunião em inglês** —
> `2026-09-10_08-30-36`, 31 min, apontada pelo dono do produto. A afirmação
> anterior deste documento (*"todas são em português"*, herdada do
> [CLAUDE.md](../CLAUDE.md)) estava errada, e ela teria feito o teste de
> degradação em inglês ser dado como impossível. **Ela é a régua do requisito
> multilíngue**, e o code-switching continua sendo a segunda.

### O veredito do afinado em português — 11/09/2026

**Medido, e é o resultado mais claro da semana: o `ptBR-TAGARELA` está fora.**

**Em português ele não domina.** Divergência contra o Gemini nas quatro:

```
gravação      multilíngue    ptBR      app (large-v3)
 7,7 min         28,1%      32,5%        24,6%
14,6 min         35,6%      19,8%        23,1%
32,1 min         26,9%      27,3%        31,7%
48,5 min         24,2%      23,5%        23,5%
```

Cada um ganha em alguma. **E a anomalia da de 14,6 min ficou explicada**: é a
gravação que o afinado resolve melhor que todos (19,8%) e o multilíngue resolve
pior (35,6%) — há algo naquele áudio que separa os modelos, e não é ruído de
medição.

**Em inglês ele não degrada: quebra.** Sobre a reunião de 31,9 min em inglês
(`2026-09-10_08-30-36`), contra a saída do `large-v3`:

```
                palavras   iguais   divergência
multilíngue        3668     3379       18,5%
ptBR               2220       13      150,4%
```

**Treze palavras em comum de 4.043.** Ele transcreve inglês *por fonética
portuguesa*:

```
app     "Hi Yuri, welcome back. Good morning. Thank you. You wouldn't leave, right?"
multi   "Welcome back. Hi. Good morning. Thank you. You were on leave, right?"
ptBR    "Olá, bem-vindo. Bom, vocês vivem? Ah, só um dia."
```

**Não é transcrição pior, é transcrição inventada** — e plausível, que é o pior
tipo. Numa ata isso vira registro falso de reunião.

**Nem como opção.** O ganho em português é inconsistente (ganha em uma, perde em
duas) e o risco é categórico. Uma chave que o usuário pode esquecer ligada antes
de uma reunião internacional não vale 3 pontos de divergência numa gravação.

> **O requisito do §"multilíngue ou afinado" se confirma com número**, e ele foi
> escrito antes da medição. A reunião em inglês que o tornou medível é de
> 10/09/2026 e foi apontada pelo dono do produto.

### O orçamento de peso é por camada, não global — 11/09/2026

Decidido pelo dono do produto, e corrige um erro de enquadramento deste
documento: eu vinha aplicando **um critério só** — *"cabe na 2060 junto com o
Meet"* — a todos os candidatos. Esse critério vale para o que roda **durante** a
reunião, e só.

| camada | quando roda | o que é caro | o que é barato |
|---|---|---|---|
| **1 · legenda** | durante, com o Meet | VRAM e ciclo. Teto medido: ~33% ([ROTA](FASE7-ROTA.md) §4) | latência |
| **3 · passada final** | **depois, com a placa livre** | **nada** — a pessoa já saiu da reunião | qualidade é tudo |

**Um modelo pesado demais para a camada 1 pode ser exatamente o certo para a
camada 3.** O `Canary 1B` em fp32 são ~4 GB, mais que o `large-v3`; isso o
elimina de rodar ao vivo e **não diz nada** sobre o papel dele na passada final,
onde o único concorrente é o tempo que a pessoa espera pela ata.

**O que isso muda na régua de cada candidato:**

* **para a camada 1**, a ordem é: cabe na placa com o Meet → acompanha o relógio
  → qualidade suficiente para rascunho (a passada final corrige);
* **para a camada 3**, a ordem é: **qualidade** → estrutura de tempo → tempo de
  espera aceitável. Peso em disco entra só no `DIST-1`, e lá ele compete com
  1,44 GB de DLL duplicada, que é dinheiro mais fácil.

> **E os dois papéis podem ser modelos diferentes.** O app já tem a máquina para
> isso: o `motor_de_transcricao` escolhe quem faz a passada final, e a
> `Retomada` confere o motor antes de reaproveitar parcial. Um motor leve ao
> vivo e um pesado no fim não é exceção — é o desenho de três camadas levado a
> sério.

### A regra que prevê o próximo candidato — 11/09/2026

Quatro candidatos testados, três reprovados **pela mesma coisa**: não devolvem
carimbo de tempo. E a causa não é implementação — é **arquitetura**:

| candidato | arquitetura | carimbo | por quê |
|---|---|---|---|
| **Parakeet TDT 0.6B** | transducer | **sim** | emite por quadro; o carimbo sai de graça |
| Canary 1B v2 | AED (atenção) | não | `NemoConformerAED` não herda classe de decodificação com carimbo |
| Qwen3-ASR | LLM decoder | não | vazio nos dois runtimes |
| `large-v3` (hoje) | AED | não **pelo modelo** | o `faster-whisper` alinha por fora, e é por isso que o app tem trechos |

**A regra: transducer e CTC dão carimbo; atenção e LLM não.** Modelo de atenção
só serve a este app se alguém construir o alinhamento — que é trabalho real, e é
o que o CTranslate2 já faz pelo `large-v3`.

**Isto é uma régua de triagem para o `DIST-5`**: antes de baixar 4 GB, olhar a
arquitetura. Se for AED ou LLM, ou vem com alinhador, ou está reprovado.

> **O Canary merece a nota honesta**: o texto dele é **visivelmente melhor** —
> *"atualização de data da Redir. Ela foi para o dia 1 de setembro"*, com numeral
> e nome próprio certos onde o Parakeet escreveu *"redeira"* —, e ele roda a
> 8,3× na 2060, ~2× o `large-v3`. **Ele perde por uma propriedade que não é
> qualidade.** Se um dia houver alinhador, ele volta à mesa.

### S2 · Um ggml só

llama.cpp e transcribe.cpp são **o mesmo motor por baixo**, e hoje cada um traz o
seu `ggml-cuda.dll` (0,73 GB somados). É o `C6`, e é a versão madura do `C2`.

### S3 · O acelerador como pacote, não como embutido

É o `DIST-2` do backlog, e ele **muda de natureza depois do `S1`**: sem torch, o
que precisa de CUDA é só ggml, e ggml tem Vulkan e CPU no mesmo binário. O app
rodando em máquina sem NVIDIA deixa de ser reescrita e vira escolha de pacote.

### S4 · O ASR menor, se a legenda ganhar

Se o Nemotron em streaming empatar com o `large-v3` na régua — e a
[FASE7-RESULTADOS.md](FASE7-RESULTADOS.md) §9.2 já viu empate técnico no texto em
bloco —, o `large-v3` deixa de ser necessário **e a passada final fica mais
barata que a de hoje**. É a inversão que o §6 da carta chamaria de irônica: o
estudo do tempo real barateando o offline.

### S5 · A fala sobreposta

O `multitalker-parakeet-streaming` continua **só inglês**, e continua fora. Fica
escrito com o mesmo gatilho de sempre: sair versão com português.

> **O que eu não pude conferir**, e vale saber ao ler esta lista: a busca na web
> estava fora do ar em 10/09, então não há aqui candidato que tenha aparecido
> depois de 03/09. A varredura é o `R0` da [FASE7-ROTA.md](FASE7-ROTA.md), e ela
> vale para esta lista também.

---

## 5. A busca na reunião, com o modelo pequeno

O pedido foi *"pesquisar a reunião com modelo de llm menor mesmo"*, e ele
**reverte uma decisão que os resultados tinham fechado** — vale dizer por quê,
porque a reversão é correta.

A [FASE7-RESULTADOS.md](FASE7-RESULTADOS.md) §10.2 concluiu *"o 4B serve"* e
descartou o 1.7B. A conclusão estava certa para o que se media: **resposta sob
demanda, com a placa livre.** O que muda agora é que a placa **não está livre** —
há legenda e bloco rodando, e o custo verdadeiro do produto B nunca foi o
relógio: é que **ele para o ASR**.

E aí o modelo menor deixa de ser concessão e vira o mecanismo:

| | `gemma-4-e4b` (hoje) | `gemma-4-e2b` | `qwen3-1.7b` |
|---|---|---|---|
| disco | 4,98 GB | 3,11 GB (Q4_K_M) | 1,1 GB |
| VRAM | 4.512 a 5.727 MiB ([ATA.md](ATA.md) §8) | não medido | não medido |
| contexto | 128K | 128K | **32K** nativo (131K só com YaRN) |
| na 2060 de 6 GB | **enche a placa** | marginal | cabe |

> **Esta tabela envelheceu em 15/09/2026, e a seção abaixo a substitui.** Ela
> escolheu por VRAM estimada e **não olhou o contexto** — que é o que decide.
> Fica aqui porque é o registro de como a decisão foi tomada antes da medição.

> **Correção de 10/09/2026, e ela inverte a recomendação.** Uma fonte secundária
> deu o E2B como *"~1,3 GB em disco"*, e este documento chegou a dizer que ele
> cabia na janela quieta. **O arquivo tem 3,11 GB**, conferido no repositório —
> o `E2B` conta parâmetros **efetivos** (~2 B por token), mas a família Gemma 4 é
> aninhada e o checkpoint carrega o conjunto inteiro.
>
> Com ~2,9 GB livres no desenho leve, **o E2B não cabe junto com a prévia**. Ele
> continua valendo por si — 1,9 GB menos que o E4B para a ata, que roda com a
> reunião encerrada e a placa livre —, mas não resolve a caixa de pergunta ao
> vivo. **Quem cabe é o `qwen3-1.7b`**, com 1,1 GB, e o custo dele é sair da
> família que acerta 8 dos 10 nomes próprios. Os dois estão no catálogo; **a
> medição decide, e ela não foi feita.**

**O E2B é o candidato principal desde 10/09/2026**, por decisão do dono do
produto, e a razão é medida: a [ATA.md](ATA.md) registra que **o Gemma 4 acerta 8
dos 10 casos de nome próprio contra 3 do Qwen3.5**. Trocar de família jogaria
fora essa vantagem para economizar 200 MB. O Qwen fica como segunda opção.

### A VRAM é intermitente, e isso decide o desenho

Observado no Gerenciador de Tarefas em 10/09/2026, com Meet + prévia do MOSS +
legenda do Nemotron rodando juntos na 2060:

```
1,5 GB         Windows + os apps de trabalho   ← o piso, e ele não é nosso
1,45 GB        piso + Meet                     ← medido antes de subir motor nenhum
3,1 – 3,3 GB   + a legenda do Nemotron         ← ~1,75 GB para ele
4,0 GB         + o bloco do MOSS, em picos     ← ~0,85 GB, ~18 s a cada 180 s
```

**O piso é 1,5 GB e não é nosso** — informado pelo dono do produto em 10/09/2026,
e bate com os 1.452 MiB medidos antes de qualquer motor subir. **Sobram ~4,6 GB
dos 6,1** para o app inteiro, e é esse o orçamento verdadeiro.

Com o desenho leve (§0.6 da [FASE7-ROTA.md](FASE7-ROTA.md)) — Meet + legenda, sem
MOSS — o consumo para em **~3,2 GB e sobram ~2,9 GB**. É o que torna a caixa de
pergunta possível: o E2B pede 2 a 3 GB, e cabe **só nesse desenho**. Com o MOSS
junto, sobram ~2,1 GB e ele fica marginal.

**A folga não é constante.** São ~2,8 GB na janela quieta e ~2,0 GB durante o
pico — e o pico dura o que o bloco leva (~18 s a cada 180 s, ou ~10% do tempo).

Para o produto B isso é a diferença entre dois desenhos: o E2B **cabe folgado na
janela quieta**, e a colisão só existe em ~10% do tempo. A fila da decisão `D7`
continua necessária, mas passa a ser **curta e rara** em vez de permanente — e a
tela deve dizer "esperando o bloco terminar" por segundos, não "isto vai pausar a
transcrição por meio minuto".

**Se ele couber junto, a fila vira paralelismo**, e cai com ela a decisão `D7` da
[FASE7-FRONTEND.md](FASE7-FRONTEND.md) §12 — a etiqueta dizendo o que a placa faz
agora — e o aviso *"isto vai pausar a transcrição por meio minuto"*. **Uma
medição de meia hora pode apagar dois requisitos de interface.**

O pacote **já existe no catálogo** (`qwen3-1.7b-instruct`,
[Catalogo.cs:219](../app-net/Nucleo/Catalogo.cs#L219)), marcado
`TamanhoMedido = false` e com a nota *"ainda não medido aqui"*. Baixar e medir é
o trabalho todo.

**Os testes, em ordem, e o primeiro pode dispensar os outros:**

0. **o cache KV com ~32 mil tokens**, para cada candidato, em fp16 e em `q8_0`.
   Se o KV dominar, a escolha deixa de ser entre famílias e passa a ser entre
   quantizações de cache — e talvez nenhum 3B caiba;
1. **VRAM com a legenda rodando.** Cabe junto na 2060, ou não? A parte da legenda
   já está medida (864,8 MiB, acima); falta a do LLM com carga concorrente.
   `tools/medir_llm_ao_vivo.py` já mede a espera;
2. **o cache de prompt do `llama.cpp`.** A [FASE7-FRONTEND.md](FASE7-FRONTEND.md)
   §2.2 já apontava: a transcrição cresce **por acréscimo**, então o prefixo é
   sempre o mesmo — é o caso ideal de reaproveitamento de KV. Se funcionar, a
   segunda pergunta custa uma fração da primeira;
3. **quanto de transcrito basta.** A §10.2 diz que acima de ~60 min o prompt
   cresce demais, e que a saída é *"um resumo corrente mais os últimos N
   minutos"* — **trabalho de desenho, não de modelo, e não foi medido**.

### A régua que faltava: quanto custa uma reunião como prompt — 15/09/2026

Medido no acervo do dono do produto, contando o prompt como ele chegaria ao
modelo — carimbo e falante por trecho, não só o texto:

```
 46 min   1.239 trechos    ~18.000 tokens
 61 min   1.486 trechos    ~19.900 tokens
 84 min     937 trechos    ~21.600 tokens
122 min   2.058 trechos    ~31.900 tokens   ← a maior do acervo
```

**Uma reunião de duas horas custa ~32 mil tokens**, e é esse número que reprova
candidato — antes de qualquer conversa sobre qualidade.

**O titular tem um problema que esta seção não tinha visto.** O `qwen3-1.7b` é
**32K nativo**; a reunião de 122 min ocupa 31.891 desses 32.768. Ele só cobre o
acervo inteiro via **YaRN**, e YaRN degrada justamente recuperação de trecho
longo, que é o que a busca na reunião faz. Foi escolhido por caber na VRAM, com o
contexto nunca conferido.

### E a VRAM, agora medida em reunião de verdade — 15/09/2026

Durante uma reunião em curso, com Meet, legenda e desktop, pelos contadores do
Windows (`GPU Process Memory`, pid do sidecar) e pelo `nvidia-smi`:

```
  864,8 MiB   a legenda (Nemotron Q8_0)   ← plano nas 30 amostras, sem crescer
~1.726 MiB   o resto: desktop, Chrome/Meet, WebView2
2.181–2.591  a placa inteira, de 6.144 MiB
~3.553 MiB   LIVRE no pico da placa
```

**Isto corrige a estimativa de ~2,9 GB** que esta seção usava, e a correção é
para melhor: **sobram ~3,5 GB**, o que abre a classe dos 3B. A legenda ficou em
864,8 MiB dígito por dígito nas trinta amostras — evidência contra vazamento de
VRAM na sessão de streaming, que era a suspeita aberta.

### O primeiro degrau existe, e ele muda a conta — 16/09/2026

**Construído:** a caixa de perguntar, no painel ao vivo do Gravador. Escreve-se
a pergunta, o motor de ata sobe, responde e morre. As peças:

| peça | onde |
|---|---|
| a montagem do texto, o corte e a vez | [PerguntaDaReuniao.cs](../app-net/Nucleo/PerguntaDaReuniao.cs) |
| a instrução, sozinha porque é o que mais vai mudar | [PromptDeReuniao.cs](../app-net/Nucleo/Atas/PromptDeReuniao.cs) |
| a op `perguntar-ao-vivo` | [Ponte.cs](../app-net/App/Ponte.cs) |
| a caixa e a resposta | [aovivo.js](../app-net/App/web/aovivo.js) |

**O motor sobe por pergunta e morre depois dela**, e isso responde ao item 3 do
`tools/medir_llm_ao_vivo.py` pelo lado do risco, não pelo da conta: um 4B quente
a reunião inteira é o terceiro contexto CUDA que derrubou a legenda de 2,46× para
0,45× em 11/09. Custa ~6 s de carga por pergunta, e é o preço de a reunião
continuar sendo transcrita enquanto se pergunta sobre ela.

**Medido em 16/09/2026**, contra o `legenda.json` real da reunião de 15/09 às
14:01, com o `qwen3-4b-instruct-q4km` e os mesmos argumentos que o
`MotorDeAta.Subir` usa (`-ngl 99 -ctk q8_0 -ctv q8_0 -fa on -c 20480`), na 2060
com o desktop rodando:

```
 5,9 s   o modelo carregar
15,8 s   a resposta inteira (7.274 tokens de prompt, 412 de saída)
27.130   caracteres de legenda  →  7.274 tokens   (3,73 char/token)
```

**A conta desta seção supunha o dobro.** A régua de 15/09 mediu o prompt *da
passada final* — carimbo e falante por trecho —, e deu ~18.000 tokens para 46
minutos. **A legenda ao vivo custa menos da metade disso** pelo formato: sem
carimbo, e com os turnos do mesmo lado juntos numa linha só. Isso não reprova
nem aprova candidato sozinho, mas afrouxa o teto que reprovou o `Gemma 3 1B`.

> **A constante de dimensionamento continua em 2,5 char/token, e de propósito.**
> Ela agora erra por ~1,5× **para o lado seguro**: superestimar o contexto custa
> VRAM, subestimar custa a resposta depois de a pessoa já ter esperado.

**A regra de não inventar segura.** Perguntado "qual foi o orçamento aprovado e
em que data o contrato foi assinado?" sobre uma reunião que não fala de nenhum
dos dois, a resposta foi *"Isso não foi falado até agora."*, em 43 tokens.

**O que fica aberto, e é a fase de ajustes:** o bake-off dos quatro candidatos
abaixo (a versão construída usa o modelo de ata configurado, seja ele qual for),
o comprimento da resposta — pediu-se *"direto e curto"* e vieram 412 tokens —, e
**o relógio**: a legenda não carimba turno, então pergunta com recorte de tempo
("os últimos 10 minutos") não tem como ser respondida. O conserto é carimbar o
turno na `LegendaAoVivo`, e é trabalho à parte.

### Os quatro competidores do `T2.1`, com o `Gemma 3 1B` já reprovado

| | contexto | GGUF Q4_K_M | licença | pt-BR |
|---|---|---:|---|---|
| **`Ministral-3-3B-Instruct-2512`** | **256K** | **2,15 GB** | Apache 2.0 | na lista oficial |
| **`SmolLM3-3B`** | 64K (128K YaRN) | ~2,1 GB | Apache 2.0 | 1 dos 6 idiomas do *instruction tuning* |
| `qwen3-1.7b` (titular) | **32K** (131K YaRN) | 1,1 GB | Apache 2.0 | sim, e perde nome próprio 3/10 |
| ~~`Gemma 3 1B`~~ | ⛔ **32K** | ~0,8 GB | Gemma | o elo fraco da família |

**O `Gemma 3 1B` está reprovado por aritmética**, e não por opinião: 32K de
contexto contra os 31.891 tokens da maior reunião do acervo deixa ~900 tokens
para system prompt, pergunta **e** resposta. Não é apertado — é impossível.

**O `Ministral 3` não é o Ministral de 2024.** Aquele era só API, sem pesos; esta
é a família de dezembro de 2025, Apache 2.0 de verdade, com GGUF publicado pela
própria Mistral. Traz duas coisas que o resto não tem e que o `Nucleo/Atas/` usa:
*function calling* nativo com saída JSON, e adesão forte a system prompt — que é
exatamente o mecanismo que o MOSS perdeu (`C0`).

**O `SmolLM3` entra pelo modo duplo.** Numa caixa de pergunta ao vivo a latência
**é** o produto, e `no_think` desliga o raciocínio explícito por chave, em vez de
torcer para o modelo ser breve.

> **O custo que ninguém contou ainda, e que pode inverter esta ordem: o cache
> KV.** Com ~32 mil tokens de prompt, num 3B o KV em fp16 pode **rivalizar com os
> próprios pesos** — 2,15 GB de Ministral podem virar 4 GB em uso, e aí nada
> cabe. O `llama.cpp` corta isso pela metade com `--cache-type-k q8_0
> --cache-type-v q8_0`. **É a primeira coisa a medir**, e por isso a lista de
> testes abaixo ganhou um item zero.

**E o `gemma-4-e2b` continua de fora**, mesmo com os 3,5 GB medidos: 3,11 GB de
pesos mais o KV de uma reunião longa estoura a folga. Ele segue valendo para a
ata, com a reunião encerrada e a placa livre.

**E a qualidade tem régua, ao contrário da primeira vez.** A §10.3 é explícita:
*"isto não é uma medição de qualidade — são quatro respostas lidas por mim, sem
gabarito"*. O `tools/auditar_atas.py` existe e não foi aplicado. Aplicar aos dois
modelos é o que impede a escolha de virar preferência.

> **Uma distinção que vale fixar:** isto é a **pergunta ao vivo**, o produto B.
> Procurar texto nas reuniões passadas é o `UI-3` do [BACKLOG.md](BACKLOG.md), é
> busca literal, não precisa de LLM nenhum, e **já dói hoje** com 44 gravações.
> São duas coisas, e a barata não depende desta.

---

## 6. O que não se corta, em nenhuma hipótese

- **`Gravacao/` e `Captura/` inteiros.** É o que o [CLAUDE.md](../CLAUDE.md) marca
  como o que não se reabre, e nada neste documento toca lá. A simplificação é dos
  **motores**, e a gravação não tem motor;
- **a passada final offline.** As três camadas da [FASE7-ROTA.md](FASE7-ROTA.md)
  §3 existem porque a legenda é rascunho. Enxugar não pode virar "a prévia é o
  resultado";
- **a retomada.** `Nucleo/Retomada.cs` e a marca de motor no parcial são o que
  impede um texto de um motor voltar rotulado como de outro. Trocar de motor
  **aumenta** o valor dela;
- **o banco de vozes**, até o `S1` provar que o substituto reconhece as mesmas
  pessoas. É o ativo que leva mais tempo para reconstruir: ele se forma reunião a
  reunião, e não se baixa de lugar nenhum;
- **os GGUF que ainda estiverem sendo comparados.** Guardar modelo para repetir
  medição é o custo de poder decidir. O `C1` só vale depois de a Fase B fechar.

---

## 7. Fontes

- [FASE7-ROTA.md](FASE7-ROTA.md) — o caminho até as features existirem
- [FASE7-RESULTADOS.md](FASE7-RESULTADOS.md) — §9 (Nemotron), §10 (o LLM ao vivo),
  §11 (a costura), §0.5 (as ressalvas)
- [FASE7-FRONTEND.md](FASE7-FRONTEND.md) — §2.2 (o orçamento do produto B), §12 (as decisões)
- [ATA.md](ATA.md) §8 — a VRAM e a velocidade do motor de ata
- [BACKLOG.md](BACKLOG.md) — `DIST-1` a `DIST-4`, `UI-3`, `VOZ-1`
