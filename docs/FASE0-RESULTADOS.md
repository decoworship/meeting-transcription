# Fase 0 — o motor nativo custa qualidade?

Resultados da medição prevista na seção 5 do [PLANO.md](PLANO.md). A pergunta:
**trocar faster-whisper + pyannote por whisper.cpp + sherpa-onnx degrada a
transcrição?**

Nada aqui foi escrito em C# ou Rust. Só binários de linha de comando rodando
sobre gravações que já existiam.

## Montagem

| | |
|---|---|
| gravação | `2026-08-06_09-03-05`, 11,3 min, duas faixas |
| trecho usado | **240s–660s** (7 min) — ver "o trecho descartado" abaixo |
| áudio da transcrição | mix das duas faixas, gerado pelo `mix_tracks` do próprio app |
| áudio da diarização | só `system.wav`, como o app faz |
| baseline | `history/1786024581252.json` — faster-whisper `large-v3` + pyannote `community-1` |
| candidato | whisper.cpp `large-v3-turbo` q8_0 + sherpa-onnx `segmentation-3.0` + `wespeaker-resnet34-LM` |

> ⚠️ **Falha de desenho na primeira rodada.** O baseline usa `large-v3` e o
> candidato usou `large-v3-turbo` — arquiteturas diferentes (32 camadas de
> decoder contra 4). Corrigido no **resultado 1-A**, com `ggml-large-v3-q5_0`.
| hardware | Ryzen 5 3400G, 8 threads, **CPU apenas** (sem CUDA no WSL) |

Ferramentas: [`tools/compare_engines.py`](../tools/compare_engines.py) e
[`tools/compare_diarization.py`](../tools/compare_diarization.py).

### Parâmetros equivalentes

Mapeados a partir de `src/transcription/faster_whisper_transcriber.py`, para a
comparação ser justa:

| app (faster-whisper) | whisper.cpp | |
|---|---|---|
| `beam_size=5` | `-bs 5` | ✅ |
| `vad_filter=True` | `--vad -vm ggml-silero-v5.1.2` | ✅ |
| `threshold=0.35` | `-vt 0.35` | ✅ |
| `min_silence_duration_ms=500` | `-vsd 500` | ✅ |
| `max_speech_duration_s=25` | `-vmsd 25` | ✅ |
| `hotwords=<prompt>` | `--prompt` + `--carry-initial-prompt` | ✅ |
| `word_timestamps=True` | `-dtw <modelo>` | ⚠️ **não testado — ver risco 1** |
| `hallucination_silence_threshold=2.0` | — | ❌ não existe |

O `--carry-initial-prompt` **parecia** resolver: o app usa `hotwords` justamente
porque `initial_prompt` só influencia a primeira janela, e esse flag reinjeta o
prompt em todas. O prompt atual (440 caracteres, ~137 tokens) cabe folgado no
limite de 224.

> ⚠️ **Medido depois, o equivalente não se sustenta.** Reinjetar contexto não é
> o mesmo que enviesar a decodificação: o `hotwords` leva "Dimi" de 1 para 9
> acertos, o `--carry-initial-prompt` leva para 5 — e não aumenta o total de
> termos acertados. Ver **resultado 5**, que reabre a decisão de migrar o ASR.
> A linha da tabela acima deveria ser ⚠️, não ✅.

## Resultado 1 — transcrição: empate técnico

```
                          baseline      turbo q8_0      turbo q5_0
                          large-v3        (874 MB)        (548 MB)
duração coberta (s)          420.0           420.1           420.1
palavras                       827             825             866
segmentos                      162              27              69
palavras/segmento              5.1            30.6            12.6
marcadores de inglês             0               0               0

similaridade vs baseline                     0.833           0.855
```

**825 contra 827 palavras.** O modelo quantizado e 4× menor recupera a mesma
quantidade de fala. O risco de "quantização estraga o português" **não se
confirmou** neste teste.

### E o q5_0 saiu melhor que o q8_0

Contra a expectativa — a recomendação corrente é preferir q8_0 em trabalho
multilíngue — o **q5_0 ganhou em todas as métricas**: mais palavras (866), mais
segmentos (69 contra 27) e maior concordância com o baseline (0,855 contra
0,833). Com 548 MB contra 874 MB.

Duas ressalvas antes de tratar isso como conclusão:

- **uma amostra de 7 minutos, sem verdade de referência.** A diferença entre
  0,833 e 0,855 é pequena e pode ser ruído de uma reunião só;
- a diferença de segmentação (27 contra 69) é grande e provavelmente **causa**
  as outras: caminhos de decodificação diferentes produzem cortes diferentes, e
  a contagem de palavras acompanha.

O que este resultado autoriza a dizer não é "q5_0 é melhor", e sim: **não há
evidência de que q5_0 seja pior em português**, e portanto os 326 MB a mais do
q8_0 precisam ser justificados por uma medição, não por precaução.

Lendo lado a lado, o whisper.cpp sai até melhor em pontuação:

> **baseline:** `mas onde é que a gente acertou aqui, esse aumento da aderência
> aconteceu em todas as frentes que a gente analisa a adesão`
>
> **whisper.cpp:** `Isso que aconteceu. Mas onde é que a gente acertou aqui, né?
> Esse aumento da aderência, ele aconteceu em todas as frentes...`

O whisper.cpp preserva os marcadores de discurso ("né?") que o baseline engole,
e pontua frases. Em um trecho ele capta um nome a mais: `conversado com o
Silvio, né, Lúcio?` onde o baseline só ouviu `conversa com o Lúcio`.

Em compensação, produziu uma alucinação curta (`Tata-tata-tata-tata...`) num
trecho de silêncio — exatamente o que o `hallucination_silence_threshold` do
faster-whisper existe para cortar, e que o whisper.cpp não tem.

## Resultado 1-A — mesmo modelo: o whisper.cpp transcreve **mais**, não menos

Rodando o `ggml-large-v3-q5_0`, mesma arquitetura do baseline:

```
                     baseline    large-v3-q5_0    turbo-q8_0    turbo-q5_0
palavras                  827             1028           825           866
segmentos                 162              124            27            69
similaridade                —            0.773         0.833         0.855
```

A similaridade **caiu** com o modelo certo, o que parece absurdo — até se olhar
a contagem de palavras. O `large-v3` produziu **1028 palavras contra 827**, 24%
a mais. A similaridade simétrica pune conteúdo extra: com essa diferença de
tamanho, o **teto matemático da métrica é 0,892**, não 1,0.

Separando as duas perguntas que a métrica simétrica mistura:

```
candidato         palavras   casadas   cobertura do baseline   excesso   teto
large-v3-q5           1028       717                   86.7%     30.3%  0.892
turbo-q5               866       724                   87.5%     16.4%  0.977
turbo-q8               825       688                   83.2%     16.6%  0.999
```

Os três preservam ~85% do baseline. O `large-v3` se distingue por **311
palavras a mais**. A pergunta que decide tudo: fala recuperada ou alucinação?

### Provavelmente é fala recuperada — mas a afirmação forte não se sustenta

> **Revisão posterior.** A leitura abaixo foi feita numa janela de 30 s e
> conclui mais do que os dados permitem. Três coisas a considerar antes de
> aceitar "+24% de cobertura":
>
> - **o baseline filtra de propósito.** O `hallucination_silence_threshold=2.0`
>   suprime conteúdo de baixa confiança; o whisper.cpp não tem o parâmetro.
>   Parte do "acha mais" pode ser simplesmente "filtra menos";
> - **o próprio resultado mostra 47 segmentos terminando em `...`** e 5 com ≤2
>   palavras em mais de 5 s — padrão clássico de enchimento. Parte das +201
>   palavras não casadas pode ser isso;
> - a verificação qualitativa cobriu **um trecho**, escolhido depois de ver os
>   números.
>
> Leitura correta: **não há evidência de perda de conteúdo**. "Recupera 24% mais
> fala" é hipótese, não resultado. O resultado 1-C, com WER contra referência
> humana, é quem responde isso de fato — e lá a resposta é *empate em áudio bem
> formado, vantagem clara em áudio degradado*.

Num trecho denso (300s–330s), lado a lado:

> **baseline:** `ajudar o Thiago, né, ele tá é, porque a gente já marcou eles
> ainda, tipo assim, não tem um modelo de rtm que seja o melhor pra digital`
>
> **whisper.cpp:** `É ajudar do lado deles mesmo. Ajudar o Thiago, né? Ele tá
> precisando de... É porque a gente já marcou um tanto de RTM e eles ainda, tipo
> assim, não tem um modelo podem de RTM que seja o melhor pra digital, né?`

O whisper.cpp captou:

- **uma frase inteira que o baseline perdeu** (`É ajudar do lado deles mesmo.`);
- `Ele tá precisando de...` onde o baseline entregou `ele tá é,`;
- `a gente já marcou um tanto de RTM`, onde o baseline embaralhou para
  `a gente já marcou eles ainda`;
- **`RTM` em caixa alta**, contra `rtm` do baseline — melhor tratamento de sigla;
- pontuação e fronteiras de frase.

O baseline também tem uma **lacuna de 14,4s** que o whisper.cpp não tem.

### O custo: fragmentação

```
                        segmentos com ≤2 palavras e >5s    com reticências
baseline                                             0                  4
large-v3 (whisper.cpp)                               5                 47
```

47 segmentos terminando em `...` contra 4. O whisper.cpp produz muito mais
fragmento pendurado, e chega a gastar 15 segundos num único `Consegue...`.

**Isto é a mesma doença do resultado 2**, vista por outro ângulo: o problema do
whisper.cpp aqui não é ouvir, é **cortar**. E a solução é a mesma — cortar por
VAD antes do ASR, como [PARIDADE.md](PARIDADE.md) detalha.

### Sobre a expectativa de "paridade quase 100%"

Não se confirmou, e não era realista: mesmo com o mesmo modelo, os dois caminhos
diferem em **VAD** (silero contra o do faster-whisper, com limiares próprios),
**quantização** (q5_0 contra fp16), **decodificação** (fallback de temperatura,
limiar de entropia) e **segmentação** (o baseline usa `word_timestamps`, que
corta em palavras).

O que se confirmou é melhor do que paridade: **o whisper.cpp não perde conteúdo
— ele acha mais.** O que ele perde é organização do texto, e isso se conserta
com engenharia, não com modelo maior.

## Resultado 1-B — WER contra referência humana (CORAA)

Todos os resultados acima comparam **dois palpites entre si**: quando os motores
divergem, não há como saber qual errou. Este mede contra transcrição feita por
gente.

**Corpus:** [CORAA ASR](https://github.com/nilc-nlp/CORAA), split de
desenvolvimento — 87 enunciados, **5,0 min**, fala **espontânea** em pt-BR
(SP2010 49, C-ORAL-BRASIL I 25, NURC-Recife 13), transcrição validada à mão.
Filtro: `pt_br`, estilo espontâneo, ao menos um voto de "nenhum problema
identificado", mínimo de 5 palavras. Ruído **não** foi filtrado — reunião tem
ruído, e a referência continua confiável.

```
motor                                       WER      CER     tempo     xRT
faster-whisper large-v3 fp16 (GPU)       20.67%   12.33%      179s    1.7x
whisper.cpp large-v3-q5_0 (CPU)          18.61%   10.44%     5941s    0.1x
```

O modelo quantizado errou **19 palavras a menos** em 919. Mas o número sozinho
engana, e o teste de reamostragem mostra por quê:

```
bootstrap, 2000 reamostragens por enunciado:
  whisper.cpp melhor em 91,7% das reamostragens
  IC 95% da diferença de WER: [-0,74, +5,51] pontos   ← cruza o zero
```

**A diferença não é significativa.** Os dados são compatíveis com o whisper.cpp
sendo desde um pouco pior até bem melhor. O que este teste estabelece com
firmeza é o lado que interessa para a decisão: **não há evidência de perda de
qualidade ao migrar o ASR** — nem mesmo com quantização q5_0 contra fp16.

Para estreitar o intervalo seria preciso ~4× mais áudio (≈20 min), já que a
largura do IC cai com a raiz do tamanho da amostra.

### Por que o WER absoluto parece alto

20% num modelo que publica 8–12% em português tem duas causas, e nenhuma
distorce a comparação, porque atingem os dois motores igualmente:

- **CORAA é fala espontânea**, com hesitação, truncamento e sobreposição. Os
  números publicados costumam vir de fala lida (Common Voice, FLEURS);
- **o normalizador é simples de propósito** — não expande número por extenso
  ("2" contra "dois") nem desfaz abreviação. Isso infla o WER de todo mundo.

Os valores aqui não são comparáveis com WER de artigo. A **diferença entre os
motores** é.

### Sobre o tempo

`179s` contra `5941s` mede GPU contra CPU, não motor contra motor. O
faster-whisper rodou numa RTX 2060; o whisper.cpp, num Ryzen 5 sem CUDA.
Velocidade só se mede no alvo.

## Resultado 1-C — o teste definitivo: fala longa, referência humana, tudo em GPU

Corpus: **6 passagens do CORAA, 14,1 min, 2363 palavras**, montadas concatenando
clipes consecutivos da mesma gravação (o CORAA vem picotado em enunciados de 2-4
segundos, que é o regime errado — ver resultado 1-B). Todos os motores na
**RTX 2060**, mesmo áudio, mesmo VAD silero com os mesmos limiares.

```
motor                                    WER      CER    tempo    xRT
faster-whisper large-v3 fp16          29.12%   23.33%    156s   5.4x
whisper.cpp large-v3-q5_0 (CUDA)      21.33%   13.89%    183s   4.6x
whisper.cpp large-v3-turbo-q5_0       23.19%   15.97%     67s  12.7x
```

O agregado engana, porque duas passagens são um regime diferente. Separando:

```
                        4 passagens bem formadas    2 do NURC (muito emendadas)
                             (1643 palavras)              (720 palavras)
faster-whisper                     14.36%                     62.78%
whisper.cpp large-v3               14.79%                     36.25%
whisper.cpp turbo                  18.69%                     33.47%
```

### Em áudio bem formado, o large-v3 empata — e o turbo não

Bootstrap com 4000 reamostragens sobre as 4 passagens boas:

```
wcpp large-v3 vs faster-whisper: melhor em 23% | IC95 [-1.39, +0.64] pts  -> EMPATE
wcpp turbo    vs faster-whisper: melhor em  0% | IC95 [-9.28, -1.83] pts  -> PIOR
```

**O `large-v3-q5_0` tem paridade estatística com o `large-v3` fp16 do
faster-whisper.** O intervalo cruza o zero e é estreito (±1 ponto): não é
"não sabemos", é "são equivalentes". A quantização q5_0, que era o risco nº 2 do
plano, **não custa qualidade**.

**O turbo é genuinamente pior**: 4,3 pontos de WER, com intervalo inteiramente
abaixo de zero e nenhuma reamostragem a favor. Não é ruído. É uma troca real de
qualidade por velocidade, e agora está quantificada.

### Em áudio degradado, o whisper.cpp é muito mais robusto

Nas duas passagens muito emendadas — uma junção a cada 1,6 s — o faster-whisper
descarta o áudio (62,78% de WER, chegando a produzir 111 palavras onde há 386),
enquanto o whisper.cpp entrega 36,25%. **Uma diferença de 26 pontos.**

Isto não é curiosidade acadêmica: é o regime das gravações reais deste projeto.
A gravação de 06/08 tinha o microfone morto por 95% do tempo e o áudio do
sistema com 94% de zeros nos dois primeiros minutos (seção 0 do plano). A
robustez a áudio ruim vale tanto quanto o WER em áudio bom.

Hipótese sobre a causa: o `vad_filter` do faster-whisper é mais agressivo em
descartar trechos que não reconhece como fala contínua. Confirmar isso é parte
do ajuste de VAD (ver próximos passos).

### Velocidade

O `large-v3-q5_0` é **17% mais lento** que o faster-whisper (183s contra 156s) —
esperado, o CTranslate2 é muito otimizado. O turbo é **2,3× mais rápido** (67s).

Mas o número que mais importa na RTX 2060 de 6 GB não é o relógio, é a memória:
o modelo quantizado ocupa ~1,1 GB contra ~4,7 GB do fp16. É a diferença entre
apertar e ter espaço para a diarização rodar junto.

### Veredito

**A migração do ASR está aprovada com medição.** `large-v3-q5_0` entrega a mesma
qualidade, com 1/4 da VRAM e mais robustez a áudio ruim, ao custo de 17% de
tempo.

O turbo fica como opção explícita de "rápido e um pouco pior", não como padrão.

## Resultado 2 — a segmentação é o problema real

```
                        segmentos   mediana    máximo   acima de 20s
baseline (word ts)            162      1.7s     14.8s              0
whisper.cpp q8_0               27      7.0s     73.5s              6
whisper.cpp q5_0               69      4.2s     42.6s              3
```

**Um segmento de 73,5 segundos atravessa vários turnos de fala.** O q5_0
segmenta melhor (máximo de 42,6s, 3 acima de 20s) mas não resolve — o baseline
não tem nenhum. O comentário no próprio código do app explica por que isso
importa:

> *"Word-level timestamps split long utterances into short segments. Essential
> for diarization: assign_speakers() matches by temporal overlap, so a segment
> spanning several speaker turns is guaranteed to be misattributed."*

Com segmentos assim, a atribuição de falante quebra **mesmo que a diarização
esteja perfeita**. Este é o achado mais acionável do exercício: não é uma
questão de qualidade do modelo, é um parâmetro que faltou.

Duas saídas, nenhuma testada ainda:

1. `-dtw large-v3-turbo` — timestamps por palavra no whisper.cpp, que permite
   recortar como o app faz hoje;
2. recortar os segmentos usando as fronteiras que a **diarização** já devolve,
   em vez de pedir ao ASR que segmente. Provavelmente melhor, porque usa a
   informação certa para a tarefa certa.

## Resultado 3 — diarização: aqui está o risco

Não existe conversão ONNX do `community-1`. Migrar significa cair para
`segmentation-3.0`. O quanto isso custa:

```
                                    falantes   fala     segmentos   concordância
baseline (pyannote community-1)            4   320.5s         162            —
sherpa-onnx, threshold 0.5 (padrão)        9   280.8s          90        71.0%
sherpa-onnx, num_speakers=4                4   279.2s          69        55.1%
sherpa-onnx, threshold 0.8                 3   279.3s          69        55.1%
sherpa-onnx, threshold 0.9                 2   279.9s          69        54.4%
```

"Concordância" = porcentagem do tempo sobreposto em que os dois atribuem a fala
à mesma pessoa, com os rótulos pareados de forma ótima.

Dois problemas visíveis:

- **29% a 45% de discordância.** É muito.
- **Fixar o número de falantes piorou** (71% → 55%), o que é contraintuitivo e
  merece explicação: com 9 clusters, os 4 melhores absorvem 170s de fala; com 4
  clusters, um único cluster domina e os outros três ficam com 1,3s, 4,5s e
  nada. O agrupamento está colapsando, não refinando.
- O sherpa também encontra **40s a menos de fala** que o pyannote (280s vs 320s).

### Ressalva metodológica, importante

A comparação é entre coisas de naturezas diferentes: o baseline são *segmentos
do Whisper com rótulo de falante colado por sobreposição*, enquanto o sherpa
devolve *turnos de fala crus*. A contagem de segmentos (162 vs 69) não é
comparável por isso. A concordância temporal continua significativa — ambos
respondem "quem fala quando" —, mas o número exato deve ser lido como ordem de
grandeza.

E, principalmente: **não há verdade de referência**. 29% de discordância não
prova que o sherpa erra; prova que os dois discordam. Como o `community-1` é o
modelo mais novo e é o que produz a saída com que se está satisfeito hoje, o
ônus da prova é do candidato — mas para decidir de fato seria preciso rotular à
mão uma janela de 2 ou 3 minutos.

## Resultado 3-A — diarização com verdade de referência: o risco era outro

O resultado 3 comparou dois palpites entre si, sobre gravação nossa sem
anotação. Não respondia "quem erra". Este responde.

**Corpus:** `diarizers-community/ami`, configuração `ihm` (headset mix — cada
pessoa no próprio microfone, misturado depois, que é o análogo do nosso
`system.wav`). 2 reuniões, 20 min, 4 falantes cada, **13% de fala sobreposta**,
turnos anotados à mão. Em inglês, e isso é aceitável: diarização opera sobre
características acústicas de locutor, não sobre fonemas — e não existe corpus de
diarização anotado em português.

**Métrica:** DER com collar de 0,25 s, casamento ótimo de rótulos (Hungarian) e
sobreposição contada corretamente. Ferramenta:
[`tools/benchmark_der.py`](../tools/benchmark_der.py).

```
motor                        DER    perdida    falso   confusão   falantes achados
pyannote community-1       17.14%    11.91%    1.49%      3.74%          4 e 2
sherpa thr=0.5             62.63%    10.90%    1.96%     49.77%         63 e 20
sherpa thr=0.7             53.32%    10.84%    1.78%     40.70%         29 e 13
sherpa thr=0.8             39.58%    10.89%    1.77%     26.93%         18 e 7
sherpa thr=0.9             32.44%    10.90%    1.78%     19.75%         10 e 5
sherpa, nº falantes dado   18.18%    10.69%    1.66%      5.82%          4 e 3
```

*(17,14% para o `community-1` no AMI é coerente com a literatura — pyannote 3.1
publica ~20-22% no mesmo corpus. Isso valida a implementação de DER.)*

### O modelo de segmentação não tem problema nenhum

**A fala perdida é constante em ~10,8% em todas as configurações do sherpa — e é
menor que os 11,91% do pyannote.** O `segmentation-3.0` detecta fala um pouco
melhor que o `community-1`. O falso alarme também empata.

Isso **derruba a hipótese estrutural** registrada em [PARIDADE.md](PARIDADE.md):
se a decodificação simplificada (argmax por frame, sem powerset) estivesse
custando a fala sobreposta, ela apareceria como fala perdida. Não aparece, e o
corpus tem 13% de sobreposição — teria aparecido.

### O problema é contar quantas pessoas existem

Toda a variação está em **confusão**, e ela acompanha exatamente o número de
falantes encontrados: 63 pessoas → 49,77%; 29 → 40,70%; 10 → 19,75%; 4 → 5,82%.

### Resultado 3-B — o threshold resolve quase tudo, e eu tinha parado cedo demais

A primeira varredura foi truncada em 0,9 **enquanto a contagem ainda caía**
(63 → 29 → 18 → 10). O parâmetro é distância, não probabilidade: a documentação
do `FastClusteringConfig` diz *"smaller value → more clusters, larger value →
fewer"*, e o `Validate()` só exige `>= 0`. Não há teto em 1,0. Estendendo:

```
                              DER   perdida    falso  confusão   falantes achados
pyannote community-1       17.14%    11.91%    1.49%     3.74%          4 e 2
sherpa thr=0.9             32.44%    10.90%    1.78%    19.75%         10 e 5
sherpa thr=1.0             20.65%    10.68%    1.70%     8.27%          5 e 3   ← ótimo
sherpa thr=1.2             38.04%    19.04%    1.25%    17.75%          1 e 1   ← colapsa
sherpa thr=1.5             38.04%    (idêntico a 1.2)
sherpa thr=2.0             38.04%    (idêntico a 1.2)
sherpa, k=4 informado      18.18%    10.69%    1.66%     5.82%          4 e 3
```

**Com threshold 1,0 e sem saber quantas pessoas há, o sherpa faz 20,65% contra
17,14% do pyannote.** A distância é de 3,5 pontos, não os 15 que a versão
anterior deste documento reportava. Informar *k* economiza 2,5 pontos a mais —
útil, mas longe de ser a alavanca de 14 pontos que eu havia estimado.

A afirmação **"ajustar o threshold não resolve" estava errada**, e a causa foi
metodológica: parei a varredura numa fronteira arbitrária em vez de continuar
até a contagem cruzar o alvo.

> ⚠️ **O ótimo é estreito, e foi escolhido olhando o conjunto de avaliação.**
> 0,9 → 32%; 1,0 → 20,65%; 1,2 → 38%. Um pico agudo, ajustado sobre as **mesmas
> 2 reuniões** em que se reporta o resultado. O 3-C valida isso — e mostra que
> era sobreajuste mesmo.

### Resultado 3-C — validação em dados retidos: a distância é o dobro

10 reuniões do AMI (5 min cada, 50 min), com as **2 usadas para escolher o
threshold separadas das 8 que nunca participaram da escolha**.

```
                     ajuste (2)   teste (8)
pyannote community-1    17.54%      11.29%
sherpa thr=0.95         24.45%      21.97%   ← melhor no teste
sherpa thr=1.00         22.88%      23.13%   ← melhor no ajuste
sherpa thr=1.05         25.66%      28.83%
sherpa thr=1.10         33.38%      35.38%
```

Agregado nas 10 reuniões:

```
                          DER   perdida    falso  confusão
pyannote community-1   12.67%     7.34%    3.26%     2.07%
sherpa thr=0.95        22.52%     6.67%    3.53%    12.31%
sherpa thr=1.00        23.08%     6.68%    3.51%    12.89%
```

Três correções ao que este documento afirmava antes:

1. **O 1,0 era sobreajuste.** Ganha no conjunto onde foi escolhido, perde nos
   dados retidos. O 0,95 é melhor no teste — e o ótimo se moveu entre corpora,
   o que indica que **o threshold do `FastClustering` não é estável**.
2. **A distância é de ~10,7 pontos, não 3,5.** A estimativa em 2 reuniões
   subestimou o problema pela metade.
3. **Ajustar o threshold não fecha a diferença.** A afirmação corrigida no 3-B
   ("o threshold resolve quase tudo") também estava otimista — resolve o
   colapso em 63 falantes, não a distância para o pyannote.

**O que permanece firme através de todas as revisões:** a fala perdida do sherpa
é igual ou menor que a do pyannote (6,67% contra 7,34%). O modelo de segmentação
não é o problema. **Toda a distância está em confusão de falante: 2,07% contra
12,31% — seis vezes maior.**

### Resultado 3-D — com *k* informado nas 10 reuniões: não é contagem

O diagnóstico "falta estimar *k*" repousava no 3-A, medido nas 2 reuniões de
ajuste e nunca revalidado. Revalidando (sherpa na GPU, ver nota de hardware):

```
                              DER   perdida    falso  confusão   tempo
pyannote community-1       12.67%     7.34%    3.26%     2.07%    512s
sherpa thr=0.95            22.52%     6.67%    3.53%    12.31%   4034s (CPU)
sherpa k=4 informado       19.39%     6.45%    3.80%     9.14%    507s (GPU)
```

**Informar *k* vale 3,1 pontos, não os 14 estimados em n=2.** E o que importa
mais: com o número certo de falantes, a confusão cai de 12,31% para 9,14% —
**ainda 4,4× a do pyannote (2,07%)**.

Isso decide entre dois diagnósticos, e é o segundo:

- ~~o problema é contar quantas pessoas há~~ — contribui 3,1 pontos, não é o
  principal;
- **o problema é a atribuição de segmentos a falantes.** Mesmo sabendo que há
  exatamente 4 pessoas, o sherpa erra a quem pertence cada trecho.

**Consequência para o desenho:** um estimador de *k* (AHC com eigengap, como
estava proposto) **atacaria o alvo errado**. O alvo é qualidade de atribuição —
o que aponta para o embedding (`wespeaker` contra o interno do pyannote), para o
algoritmo de agrupamento, ou para a ausência da resegmentação com consciência de
sobreposição que o pipeline do pyannote faz.

### Onde isso deixa o risco nº 1

Nem "estrutural e bloqueante" (1ª leitura), nem "calibrável por parâmetro" (2ª),
nem "falta estimar *k*" (3ª). O que sobreviveu a todas as revisões:

> O `segmentation-3.0` **detecta fala melhor** que o `community-1` (perdida
> 6,45% contra 7,34%). Toda a distância está em **atribuir a fala à pessoa
> certa**, e ela não se fecha por parâmetro nem por saber quantas pessoas há.

Caminhos, em ordem de custo:

1. **Manter a diarização em Python** como motor separado (sidecar). A
   arquitetura de motores como pacotes permite isso sem comprometer "app sem
   Python para o usuário". **É o caminho da v1** — um gap de 6,7 pontos com *k*
   conhecido não se migra agora;
2. **Exportar o `community-1` para ONNX** com o token que já existe. O embedding
   (wespeaker) já é o mesmo; o que falta é o pós-processamento — que é
   justamente onde o 3-D localizou o problema;
3. **Agrupamento próprio sobre os embeddings** — agora com alvo corrigido: não
   estimar *k*, e sim melhorar a atribuição (resegmentação com sobreposição,
   atribuição conjunta em vez de gulosa). Mais difícil do que parecia no 3-A.

### Resultado 3-F — a perda em fala sobreposta é igual nos dois

O 3-A concluiu que a hipótese estrutural (decodificação sem *powerset*, que não
representaria fala simultânea) estava derrubada, mas por **inferência**: a fala
perdida total era parecida. Uma perda concentrada nos trechos sobrepostos podia
estar mascarada por um detector melhor no resto. Estratificando:

```
motor                    perda em fala simples   perda em sobreposição
pyannote community-1              4.48%                 27.25%
sherpa thr=0.95                   3.63%                 27.90%
sherpa k=4                        3.62%                 26.15%
```

*(29,2 min de fala simples e 4,2 min sobrepostos, ou 12,5% de sobreposição.)*

**Idênticos onde a hipótese previa diferença.** Se o sherpa não representasse
sobreposição, perderia perto de 50% dessas unidades — um de cada dois falantes
simultâneos. Perde 27,90% contra 27,25% do pyannote. E em fala simples perde
*menos*.

Somado ao 3-A(a) — o sherpa emite segmentos simultâneos em 31–34% dos casos,
contra 19,8% do pyannote —, a hipótese estrutural está encerrada por dois
caminhos independentes.

**Achado lateral que vale para o produto:** os dois motores perdem ~27% da fala
sobreposta. É limitação da classe de modelo, não de implementação. Numa reunião
com muito cross-talk, **trocar de motor não resolve** — o que resolve é o desenho
de duas faixas, que separa a sua fala da dos outros antes de qualquer diarização.

### Resultado 3-E — o mesmo threshold dá resultado diferente em CPU e GPU

Rodando os mesmos thresholds nas mesmas 10 reuniões, mudando só o provider do
onnxruntime:

```
 thr    DER cpu   DER gpu    delta   confusão cpu   confusão gpu   k cpu  k gpu
1.00     23.08%    27.91%   +4.83         12.89%         17.53%      23     21
1.05     28.13%    28.33%   +0.20         17.98%         18.05%      17     19
1.10     34.94%    34.78%   -0.16         21.76%         21.46%      12     12
1.15     38.22%    34.78%   -3.44         25.07%         21.46%      11     12
```

**Até 4,8 pontos de diferença só por trocar CPU por GPU**, com o mesmo modelo,
o mesmo áudio e o mesmo parâmetro. E o sinal não é sistemático: em 1,0 a GPU é
pior, em 1,15 é melhor, no meio empata. É o padrão de diferenças de ponto
flutuante empurrando decisões de agrupamento que estão em cima do limiar.

O número de falantes encontrados também muda (23 contra 21 em 1,0; 17 contra 19
em 1,05).

**Consequência para produção:** um threshold calibrado num backend **não
transfere** para o outro. Somado à instabilidade entre corpora já medida no 3-C
(o ótimo andou de 1,0 para 0,95), isto encerra o assunto:

> O threshold do `FastClustering` não serve como mecanismo de produção. Não é
> questão de achar o valor certo — não existe um valor que se sustente entre
> conjuntos de dados e entre backends.

É mais um argumento, independente do 3-D, para o caminho 2 (exportar o
`community-1`) ou 3 (manter Python) em vez de calibrar o que está aí.

*(Pendência: não medi se a GPU é determinística entre execuções do mesmo
parâmetro. Chama atenção que 1,10 e 1,15 deram DER idêntico em GPU — provável
saturação, mas vale confirmar com uma repetição antes de tratar como ruído
puramente numérico.)*

### Nota de hardware

O sherpa passou a rodar em **GPU** (`--provider cuda`, padrão em
`benchmark_der.py`): 507s contra 4034s em CPU para os mesmos 50 min — **8×**.

A wheel CUDA do sherpa exige CUDA 11.8 + cuDNN 8, mais antigo que o driver desta
máquina; resolvido com bibliotecas `cu11` via pip no venv e `LD_LIBRARY_PATH`
(ver `sherpa_gpu.sh`). **No Windows, que é o alvo, o sherpa publica build para
CUDA 12.x + cuDNN 9** — o descompasso é da wheel Linux, não do projeto.

### O que isso muda

> ⚠️ **Esta seção afirmava que "o `FastClustering` não sabe estimar quantas
> pessoas há — a separação em si funciona", com base em 18,18% contra 17,14% nas
> 2 reuniões de ajuste.** O 3-D revalidou nas 10 e superou as duas coisas:
> informar *k* vale 3,1 pontos (não 14), e a confusão continua 4,4× maior mesmo
> com *k* certo. **O problema é atribuição, não contagem.** A leitura válida está
> no 3-D e no 3-E.

> ⚠️ **Este parágrafo dizia "estimar *k* vale ~14 pontos".** Era aritmética das
> mesmas 2 reuniões (32,44 − 18,18) e **não se sustentou**: revalidado nas 10, o
> ganho é de **3,1 pontos** (22,52% → 19,39%). Ver 3-D, que também muda o
> diagnóstico.

> ⚠️ **O calendário não resolve isto, e não deve.** A lista de convidados não é
> teto nem piso: entra gente sem convite e falta convidado. A decisão registrada
> em [QUALIDADE.md](QUALIDADE.md) §4 e [VOZES.md](VOZES.md) §5 é que **o número
> de falantes é do modelo, sem hint**, e o calendário atua só a jusante — no
> vocabulário do ASR e como reforço de confiança ao *nomear*. Uma versão anterior
> deste documento listava o calendário como solução; era contradição com a
> decisão de produto e está corrigida.
>
> Nota sobre a evidência: o QUALIDADE.md justifica a decisão citando "fixar
> `num_speakers` colapsou o clustering (71% → 55%)", número que vem do resultado
> 3 — medição sem verdade de referência e hoje superada. **O 3-A mostra o
> contrário**: com *k* conhecido o DER despenca. A decisão de produto continua
> certa (a lista de convidados é fonte ruim de *k*), mas o argumento empírico
> que a acompanhava não se sustenta e não deve ser reusado.

Risco nº 1: ver a leitura final em 3-D. Em resumo, **solucionável por
engenharia, mas não por calibração** — a detecção de fala é boa, e o que falta é
qualidade de atribuição.

## Resultado 6 — há 12 pontos de WER de graça no app de hoje

O 1-C atribuiu ao whisper.cpp uma "vantagem de robustez em áudio degradado" (36%
contra 63% nas passagens emendadas). A hipótese levantada ali — o `vad_filter`
do faster-whisper descartando o que não reconhece como fala contínua — nunca
tinha sido testada. Testando, sobre as mesmas 6 passagens:

```
configuração                              WER      CER    nas 2 emendadas
app hoje (vad 0.35, filtro aluc. 2.0)  29.12%   23.33%    75.9% e 47.6%
vad 0.2                                26.28%   19.79%    67.4% e 35.0%
SEM VAD                                17.05%    9.36%    20.2% e 27.8%
sem filtro de alucinação (vad 0.35)    29.12%   23.33%    idêntico ao app
SEM VAD + sem filtro                   17.05%    9.36%    idêntico a sem-vad
whisper.cpp large-v3-q5_0              21.33%   13.89%    40.4% e 31.4%
```

Três conclusões:

1. **A hipótese estava certa.** O VAD era a causa. Desligado, o WER cai de
   29,12% para 17,05% — **12 pontos, sem trocar motor nenhum**;
2. **A vantagem do whisper.cpp era artefato da nossa configuração.** Sem VAD, o
   faster-whisper faz 17,05% contra 21,33% do whisper.cpp — **o titular ganha**.
   O 1-C deve ser lido com essa correção;
3. **O `hallucination_silence_threshold=2.0` é inerte aqui.** Ligado ou
   desligado, WER idêntico até a segunda casa. É um parâmetro que o app carrega
   sem efeito medido.

### Resultado 6-A — repetido sobre gravação real: a conclusão se inverte

O resultado acima veio de fala concatenada, sem silêncio. Repetindo sobre
`2026-08-06_09-03-05/system.wav` — **11,3 min com 32,2% de silêncio digital e
38,9% de fala**, o único áudio do acervo com as duas coisas em quantidade.

Sem transcrição de referência, a avaliação usa o próprio áudio como verdade
([`tools/sweep_vad.py`](../tools/sweep_vad.py)): onde há energia, o texto deve
aparecer; onde há **zeros exatos**, qualquer palavra é invenção. Zeros exatos não
são fala baixa — são ausência de sinal, e não admitem interpretação.

```
config              palavras   em fala   em silêncio   % inventado
0.35:500 (app hoje)      885       617          50.3         5.69%
0.25:500                 875       621          47.2         5.40%
0.15:500                 930       642          48.8         5.24%   ← melhor
0.1:300                  894       620          48.9         5.47%
sem-vad                  934       632          67.0         7.18%   ← pior
```

**Desligar o VAD aumenta a invenção em 33%** (67 contra 50 palavras). Os 12
pontos do resultado 6 **não transferem** para áudio com silêncio — eram artefato
do corpus, como a ressalva previa.

Mas há um ganho real: **threshold 0,15 domina o 0,35 nos dois eixos** —
recupera 4% mais fala (642 contra 617) e inventa proporcionalmente menos (5,24%
contra 5,69%). É melhoria disponível no app de hoje, medida, sem trocar motor.

### O número que ninguém tinha visto

Mesmo na melhor configuração, **~5% das palavras são produzidas sobre silêncio
digital**. Uma em cada vinte palavras da transcrição atual é invenção sobre
ausência de sinal.

Isso não é problema de VAD — nenhum ajuste testado desce disso. É o modelo
preenchendo vazio, e reforça o que o resultado 4 já mostrava. O conserto
provavelmente é a jusante: **descartar segmentos cuja janela de áudio é
silêncio digital**, que é uma checagem barata e determinística no núcleo, não no
motor.

*(Ressalva: uma gravação de 11 min. E a métrica rateia por tempo os segmentos
que atravessam fala e silêncio, então os valores absolutos são aproximados — o
ordenamento é que importa.)*

### ⚠️ Não desligue o VAD por causa deste resultado

O corpus é **fala concatenada**: clipes do CORAA emendados, praticamente sem
silêncio. Aí o VAD não tem o que fazer de útil e só remove fala boa.

Numa gravação real a situação se inverte. O resultado 4 mostra os dois motores
alucinando sobre silêncio digital, e a gravação de 06/08 tem 95% de microfone
mudo. **É para isso que o VAD existe.**

O que este resultado autoriza:

- o limiar de 0,35 é **agressivo demais** e nunca foi medido contra referência;
- há ganho grande disponível ajustando-o;
- **a varredura precisa ser refeita sobre gravação real**, com silêncio de
  verdade, antes de mudar o padrão do app.

É a mesma disciplina de ajuste/teste do 3-C: um parâmetro calibrado no corpus
errado é pior que o parâmetro velho, porque vem com falsa confiança.

## Resultado 5 — o vocabulário funciona, e cobra um preço

A pergunta que originou o projeto: um nome conhecido ("Dimi") sair transcrito
como outra coisa ("Jimmy"). Nenhuma tabela desta fase reportava isso até agora.

**Áudio:** `2026-08-06_10-31-03`, 36,7 min, a gravação com mais vocabulário
disponível (37 ocorrências, incluindo **"Dimi" 9 vezes**).
**Ferramenta:** [`tools/benchmark_vocab.py`](../tools/benchmark_vocab.py).
**Desenho 2×2** — cada motor com e sem vocabulário, porque sem o braço "sem
prompt" não dá para separar "o motor já acertaria sozinho" de "o mecanismo
funcionou".

```
motor                        mecanismo   total  André  Avanç.  Carla  Dimi  Ellen  Felipe  Vanessa
referência (histórico)       —              37      3      0       2     9      4      1       18
faster-whisper com-prompt    hotwords       36      3      4       1     9      4      1       14
faster-whisper sem-prompt    —              25      3      0       2     1      3      1       15
```

### O mecanismo funciona, e o efeito é grande

**"Dimi": 9 ocorrências com `hotwords`, 1 sem.** Sem o vocabulário, o nome se
perde em 8 de 9 vezes. No total, 36 contra 25 acertos — 44% a mais.

E o braço com prompt reproduz a referência do app (36 contra 37), o que valida
a montagem: é a mesma configuração que roda em produção.

### O preço: a decodificação fica pior

```
                    palavras  segmentos  tempo
com hotwords            3409        279    684s
sem prompt              3692        752    274s
```

Menos palavras, **menos da metade dos segmentos**, e **2,5× mais lento** — o
tempo extra é assinatura de *fallback* de temperatura, que o faster-whisper
aciona quando a decodificação degenera.

E aparecem trechos que são o prompt remontado, não fala:

> `Avançados.com.br  Avançados.com.br  Nossa, é horrível...`
>
> `Sistemas e bases de faturamento ciclo Avançados. Sistemas e bases de
> faturamento ciclo Avançados.`

"Sistemas e bases", "faturamento ciclo" e "Avançados" são fragmentos do
`initial_prompt` recombinados. Regurgitação **literal** é mínima (1 n-grama de 4
palavras em 3409), mas a influência temática é visível — e explica o termo
"Avançados" aparecer 4 vezes onde a referência tem 0.

### Ressalva importante

Esta é **a gravação com microfone morto em 95% do tempo** (seção 0). Silêncio
digital é justamente onde o modelo se apoia no prompt por falta de sinal. O
efeito colateral medido aqui é provavelmente **pior do que seria em áudio
saudável** — mas o benefício ("Dimi" 1 → 9) também foi medido aqui, e vem de
trechos com fala real.

Repetir numa gravação saudável separaria as duas coisas. Fica como pendência.

### O whisper.cpp não tem mecanismo equivalente — e isto bloqueia a migração

Braços do whisper.cpp (GPU no Windows, mesmo áudio, mesmos parâmetros):

```
motor                  mecanismo                total  Dimi  Vanessa  André  Carla  segmentos
referência (app)       —                           37     9       18      3      2          —
faster-whisper com     hotwords                    36     9       14      3      1        279
faster-whisper sem     —                           25     1       15      3      2        752
whisper.cpp com        --carry-initial-prompt      28     5       14      2      1        618
whisper.cpp sem        —                           28     1       17      3      2       1081
```

Dois fatos, e o segundo é o que decide:

1. **"Dimi": o faster-whisper recupera 9 de 9; o whisper.cpp, 5 de 9.**
2. **O whisper.cpp não ganha nada no total: 28 sem prompt, 28 com prompt.** Ele
   *troca* acertos — Dimi sobe de 1 para 5, mas Vanessa cai de 17 para 14, André
   de 3 para 2, Carla de 2 para 1. O faster-whisper vai de 25 para 36.

O `--carry-initial-prompt` reinjeta texto de contexto; o `hotwords` do
faster-whisper enviesa a decodificação. São mecanismos diferentes, e a diferença
aparece exatamente no caso que originou o projeto.

Nota: sem prompt, o whisper.cpp é **melhor** que o faster-whisper (28 contra 25).
A desvantagem não é do modelo — é da falta do mecanismo.

### Resultado 5-A — a correção a jusante resolve, e desfaz o bloqueio

Duas descobertas ao olhar **o que** o motor escreveu em vez de "Dimi":

```
                   Dimi  Jimmy  Dimmy
referência            9      0      0
faster-whisper com    9      0      0
faster-whisper sem    1     10      0
whisper.cpp com       5      0      3   ← 8 de 9, foneticamente
whisper.cpp sem       1     10      0
```

1. **Falha na minha métrica.** Contar por string exata marcou 5/9 para o
   whisper.cpp com prompt; o desempenho fonético era **8 de 9** (5 "Dimi" + 3
   "Dimmy"). O modelo ouviu certo e escreveu diferente.
2. **Os dois motores produzem "Jimmy" exatamente 10 vezes sem prompt.** O viés
   é do modelo Whisper, não do motor — o que explica por que o problema apareceu
   antes de qualquer migração.

Aplicando correção fonética a jusante
([`tools/correcao_fonetica.py`](../tools/correcao_fonetica.py)):

```
motor                       antes  depois  ganho   trocas
faster-whisper com-prompt      36      36     +0   —
faster-whisper sem-prompt      25      36    +11   Jimmy→Dimi×10, Helen→Ellen×1
whisper.cpp com-prompt         28      31     +3   Dimmy→Dimi×3
whisper.cpp sem-prompt         28      38    +10   Jimmy→Dimi×10
```

**O whisper.cpp sem prompt, com correção a jusante, faz 38 — mais que os 36 do
faster-whisper com `hotwords`.** A referência tem 37.

Três consequências:

- **o bloqueio do resultado 5 cai.** O mecanismo de vocabulário do motor deixa
  de ser critério de escolha;
- **melhor ainda, o prompt fica dispensável** — e isso é ganho, porque o prompt
  custa 2,5× mais tempo, menos da metade dos segmentos e regurgitação temática
  (resultado 5);
- **vale igual para os dois motores**, no núcleo, o que casa com o que o
  [PARIDADE.md](PARIDADE.md) defende sobre não deixar filtro de qualidade dentro
  do motor.

#### O falso positivo que quase passou

A primeira versão tinha uma regra de "remover vogal final" que fazia `fixo` e
`Fixa` colidirem — e o corretor reescrevia **"Do IP fixo"** (português legítimo
numa reunião de telecom) como "Fixa". Removida: os casos verdadeiros não
precisam dela, porque "Dimi", "Dimmy" e "Jimmy" já convergem pelas outras regras.

Vale como princípio de desenho: **falso positivo aqui reescreve o que a pessoa
disse, e quem lê a ata não tem como desconfiar.** O custo é assimétrico, então o
casamento exige código fonético igual **e** distância de edição pequena.

> ### ~~Isto reabre a decisão de migrar o ASR~~ — resolvido pelo 5-A
>
> O 1-C aprovou a migração por paridade de WER. Mas WER trata todas as palavras
> como iguais, e **um nome próprio errado custa uma palavra no WER e custa a
> utilidade do parágrafo inteiro** para quem lê a ata.
>
> Com o vocabulário no quadro, o placar muda: empate em WER, **derrota clara no
> mecanismo que resolve o problema real**.
>
> Caminhos, nenhum testado ainda:
>
> - `--suppress-regex` do whisper.cpp para suprimir as grafias erradas
>   conhecidas ("Jimmy") — ataca o sintoma, exige manutenção manual por termo;
> - correção a jusante no núcleo, por dicionário de substituição com
>   similaridade fonética. **Vale para os dois motores** e independe da
>   migração — provavelmente o melhor investimento;
> - manter o faster-whisper como motor de transcrição, já que a arquitetura de
>   motores como pacotes permite escolher por qualidade e não por stack.

## Resultado 4 — o trecho descartado ensina mais que o teste

A primeira rodada rodou sobre a gravação inteira, sem VAD, e degenerou:

```
sem VAD, gravação inteira:  1812 palavras (contra 950 do baseline)
                            similaridade 0.584
com VAD, trecho saudável:    825 palavras (contra 827)
                            similaridade 0.833
```

O texto era a mesma frase repetida dezenas de vezes — `"Eu acho que é um pouco
mais difícil de fazer."` por minutos a fio. **VAD não é otimização, é
requisito.**

Mas a causa não era o motor. Medindo as faixas:

```
mic     rms 0.00005 até 480s  (morto)  -> vivo só a partir dos 8 minutos
system  94% de zeros nos 2 primeiros minutos
```

E o baseline atual **alucina no mesmo trecho**: produziu
`[13,8s → 41,1s] "Eu acho que tem"` e `[55,3s → 168,9s] "Aí tipo"` — duas
palavras em 113 segundos. Os dois motores inventam texto sobre silêncio
digital; o faster-whisper apenas inventa menos.

Isso confirma o diagnóstico da seção 0 do plano por um caminho independente, e
reforça que a instrumentação de silêncio no `meta.json` é prioritária: **a
qualidade da gravação domina a escolha de motor.**

## Sobre velocidade: este teste não mede nada

CPU apenas, sem CUDA no WSL, e com os processos disputando os 8 threads em
graus diferentes:

```
sherpa-onnx (420s de áudio)     349s   1.2x tempo real
whisper.cpp q8_0                1188s  0.35x
whisper.cpp q5_0                 740s  0.57x
```

**Não comparar esses números entre si**: o q8_0 rodou junto com o sweep de
diarização e o q5_0 quase sozinho, então a diferença mede contenção de CPU, não
os modelos. E nenhum deles diz o que acontece numa **RTX 2060 no Windows**, que
é o alvo. Velocidade só se mede lá.

## Veredito

| risco do plano | veredito |
|---|---|
| 1. Diarização pode piorar | ⚠️ **confirmado, com diagnóstico** (3-C, 3-D) — em 8 reuniões retidas do AMI o sherpa fica ~6,7 pontos atrás do pyannote **mesmo com o número de falantes informado** (19,39% contra 12,67%). A detecção de fala é melhor (perdida 6,45% vs 7,34%); a distância está toda em atribuir a fala à pessoa certa. **Não se migra a diarização na v1** — segue como motor Python separado |
| 2. Quantização e português | ✅ **afastado, agora com medição contra referência humana** — no CORAA, `large-v3-q5_0` deu WER 18,61% contra 20,67% do faster-whisper fp16. A diferença não é significativa (IC 95% cruza o zero), mas a direção do risco se inverteu: não há sinal de perda |
| 3. Alinhamento por palavra | ⚠️ **vira o item bloqueante** — sem word timestamps, segmentos de 73s inviabilizam a atribuição de falante. **Solução identificada:** cortar por VAD antes do ASR (ver [PARIDADE.md](PARIDADE.md)) |

**A migração do ASR estava liberada por WER — o resultado 5 a reabriu.** O
whisper.cpp empata em WER e é melhor sem prompt, mas **não tem equivalente ao
`hotwords`**, e é o `hotwords` que resolve o caso "Dimi/Jimmy" que originou o
projeto. Ver a leitura final no resultado 5.

Nada impede começar pela Fase 1 (gravador nativo), que é independente das duas.

## Próximos passos, na ordem

A Fase 0 está encerrada. A Fase 1 (gravador nativo, ver
[FASE1.md](FASE1.md)) não depende de nada aqui. O que segue são **trilhas
paralelas**, na ordem de valor.

### Gate único que resta da migração do ASR

1. **Segmentação do whisper.cpp.** O único teste do resultado 2 nunca feito, e
   ele volta a ser bloqueante porque o 5-A reabriu o ASR a favor do whisper.cpp:
   `-dtw`, corte por VAD antes do ASR, ou corte pelas fronteiras da diarização.
   Sem isso, migrar troca um problema resolvido (vocabulário, pelo 5-A) por um
   conhecido e não resolvido (atribuição quebrada por segmento de 73 s).

### Melhorias no app de hoje — independem de migração

2. **VAD em 0,15** (resultado 6-A), confirmando em 1–2 gravações reais antes de
   mudar o padrão.
3. **Filtro de segmentos sobre silêncio digital** no núcleo. ~5% das palavras
   hoje são invenção sobre zeros, e nenhum ajuste de VAD desce disso.
4. **Correção fonética** ([`tools/correcao_fonetica.py`](../tools/correcao_fonetica.py))
   como componente do núcleo, com o guarda de léxico pt-BR que falta e as trocas
   visíveis no transcript. Libera o vocabulário do teto de 224 tokens: a lista
   por projeto passa a poder ser ilimitada.

### Refinamentos de medição — mudam confiança, não direção

5. **Repetir o resultado 5 em gravação saudável.** O efeito colateral do prompt
   foi medido na gravação com 95% de microfone morto, onde ele é maximizado.
6. **Arbitrar as divergências** do 5-A por escuta: as 10 correções Jimmy→Dimi
   caem sobre fala (0% de silêncio digital) e coincidem com os "Dimi" da
   referência, mas 2 excedem a contagem dela.
7. **q5_0 contra q8_0 em mais gravações** antes de fixar o default do instalador.
8. **Bootstrap por clipe** em vez de por passagem (exige rebaixar o CORAA).

### Superados, registrados para não voltarem

- ~~rotular à mão 2–3 min para saber "quem erra" na diarização~~ — resolvido
  pelo AMI com RTTM (3-A a 3-F), que é referência humana de verdade;
- ~~`reverb-diarization-v2` como alternativa~~ — perdeu urgência: o 3-D mostrou
  que o gargalo é atribuição, não o modelo de segmentação, e a diarização fica
  em Python na v1.
