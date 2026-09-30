# Nemotron-3-Diarization — medido contra o pyannote, portado para ONNX

Escrito em 25/09/2026, no dia em que o dono do produto perguntou se dava para
usar o [`nvidia/Nemotron-3-Diarization`](https://huggingface.co/nvidia/Nemotron-3-Diarization).
É o `VIVO-7` e o `MOD-1` do [BACKLOG.md](BACKLOG.md).

**O que este documento é.** A medição que responde *"ele é melhor que o que
temos, roda no nosso formato, e dá memória de voz?"*, com o método e o comando
de cada número. **O que ele não é:** a decisão de trocar a diarização. Ela
depende de uma medição que ainda não existe — o custo dele ao lado da legenda
na 2060 —, e é esse o próximo passo (`MOD-1`).

---

## 1. O modelo

100M parâmetros: Transformer de 31 camadas com RoPE, mel de 128 bandas a 10 ms
empilhado para 80 ms, saída de 8 falantes a 10 ms. **Até 8 falantes** — o
Sortformer do `transcribe_cpp` (`VIVO-5`) tem 4, e 4 foi o que o barrou na
[FASE7.md](FASE7.md) §3.1. Um checkpoint só para offline e streaming, com o
cache de falantes em ordem de chegada (AOSC) do Streaming Sortformer. Licença
OpenMDW-1.1. Lançado em 23/09/2026.

**O que o cartão diz e a medição derrubou:**

- *Ampere ou mais nova* — roda na 2060 (Turing, compute 7.5), em fp16 no torch
  e em fp32 no onnxruntime;
- *inglês, mandarim e quatro línguas indianas* — em português acertou mais que
  o pyannote (§3). Diarização quase não depende da língua, e aqui isso se
  confirmou.

**O que o cartão diz e é verdade:** *"not biometric identities"*. O rótulo é por
sessão. Ver §5.

---

## 2. O porte: numpy + onnxruntime, sem torch

**Por que ONNX e não um runtime nativo.** O `transcribe_cpp` que empacotamos,
até a 0.2.4 (25/09), só conhece o Sortformer 4spk v2.1. O **NeMo-Speech.cpp**
ganhou o Nemotron-3 em 24/09 (PR #50, e virou o diarizador padrão no #52), mas o
único release é a v0.1.0, de 19/08 — seria compilar para Windows/CUDA. O ONNX é
o formato que o sidecar de diarização **já usa** desde a
[DIARIZACAO-ONNX.md](DIARIZACAO-ONNX.md): `onnxruntime-gpu` 1.23.2, numpy e
scipy, nada de torch.

**Dois grafos e ~150 linhas de numpy.**

```
embed.onnx      2 MB    mel [1, N, 128]            -> embeds [1, N/8, 512]
step.onnx     377 MB    [cache | FIFO | bloco | contexto] -> logits [1, 8T, 8]
mel.npy                 filtros slaney (o sidecar não precisa de librosa)
silencio.npy            o silence_embeds que o cache comprimido usa
```

A receita dos grafos é a do `NealCaren/Nemotron-3-Diarization-ONNX`. O mel, o
laço de blocos e o cache de falantes ficam em numpy
(`tools/nemotron3_onnx.py`), portados do `transformers`.

**Offline e streaming são o mesmo laço.** O "offline" do modelo também anda em
blocos — 340 quadros de 80 ms (27 s) com 40 de contexto — e passa pelo mesmo
cache. O streaming só encolhe o bloco e aumenta o FIFO:

```
                    bloco   contexto   FIFO   atualização   latência
offline               340        40      40          300     ~30 s
low_latency             9         4     264          222     1,04 s
very_low_latency        6         2     264          222     0,64 s
ultra_low_latency       3         1     264          222     0,32 s
```

**O mel é o da legenda.** O front-end é o `NemotronAsrStreamingFeatureExtractor`,
o mesmo do `nemotron-3.5-asr-streaming` que a legenda roda. E o processor
garante que o mel por blocos sai idêntico ao do arquivo inteiro, então medir o
streaming sobre uma gravação é exato, e não aproximação.

**A régua do porte:** contra o `transformers`, em 3 min de áudio — **100% das
decisões por quadro iguais, offline e `low_latency`**; mel com diferença máxima
de 1,9e-4. O logit diverge até 10 pontos nos valores saturados, onde nenhuma
decisão muda.

**fp16 não fechou.** O `onnxconverter_common` quebra no RoPE (um `Cast`
explícito para float) e, contornado com `op_block_list`, trava na conversão.
Ficou para o `MOD-1`.

---

## 3. Acerto de falante

**A régua** é a de sempre, o `tools/comparar_com_gemini.py`: fração das palavras
alinhadas com o Gemini/Meet que têm o falante certo. O texto é o
`transcricao.json` do app, **intocado**; só o falante de cada segmento é
trocado pelo slot do Nemotron com mais quadros ativos dentro dele, rodado sobre
o `mix.wav`.

**A ressalva, que vale para toda a tabela.** Os rótulos do Nemotron são
anônimos, e a régua os casa com os nomes do Gemini por co-ocorrência — o que o
favorece um pouco. O app de hoje carrega nomes do reconhecimento de vozes e usa
a faixa do mic para o dono. A diferença é grande o bastante para não ser o
casamento, mas é por isso que a §5 existe.

**Nas quatro reuniões do acervo com export do Gemini**, ganhou em todas, em
todos os modos:

```
                     2026-08-20   2026-08-21   2026-08-25   2026-08-27
duração                32 min        8 min       15 min       49 min
app de hoje             92,6%        95,0%        97,9%        96,6%
offline                 96,6%        97,8%        98,8%        98,0%
low_latency     1,04 s  96,5%        97,4%        98,8%        98,0%
very_low_latency 0,64 s 96,4%        97,4%        98,8%        98,0%
ultra_low_latency 0,32 s 96,4%       97,4%        98,6%        98,1%
```

**O erro cai de 41% a 56% no offline** — 7,4 → 3,4 em 20/08, 5,0 → 2,2 em
21/08, 2,1 → 1,2 em 25/08, 3,4 → 2,0 em 27/08. O ganho é maior nas duas em
que o app mais erra.

**Ao vivo custa quase nada em acerto** — no pior caso, 0,4 ponto do offline, e
em 27/08 o `ultra_low_latency` chega a passar o offline por 0,1.

---

## 3b. Na passada final, do jeito que o app monta — o `MOD-2`, 29/09/2026

A §3 tinha duas diferenças em relação ao app, e `tools/medir_mod2.py` tira as
duas: **os dois diarizadores recebem o `system.wav`** (o app não diariza o mix;
o dono vem do microfone), e **os dois passam pelo mesmo reconhecimento de
vozes** — wespeaker sobre 30 s de fala limpa de cada rótulo, `Reconhecer` com
limiar 0,70, contra o banco real **sem as amostras vindas da própria reunião**.
As falas do dono saem da conta. O pyannote é o pipeline ONNX que o app roda
hoje (`motores/diarizacao/pipeline`).

```
                      separação dos outros      nome certo         tempo     VRAM
                      pyannote   Nemotron   pyannote  Nemotron   pyannote  Nemotron
2026-08-20  32 min      93,8%      96,9%      49,5%     51,5%      11x      226x
2026-08-21   8 min      95,6%      98,5%      94,8%     98,0%      12x      142x
2026-08-25  15 min      98,9%      99,3%      98,8%     99,3%      12x      178x
2026-08-27  49 min      98,0%      99,0%      97,2%     98,1%      11x      206x
7.713 palavras          96,5%      98,3%      80,2%     81,5%    564 s     31 s
VRAM de pico                                                      ~4,4 GB  ~0,55 GB
```

**Ganha nas quatro, nas duas notas, e custa uma fração.** O erro de separação
cai de 3,5% para 1,7% no conjunto — a metade, como na §3, agora com a mesma
entrada e o mesmo pós-processamento do app. E roda **18× mais rápido com um
oitavo da VRAM** (o embed por bloco do `tools/medir_mod1.py`).

**O "nome certo" de 20/08 é baixo nos dois pela mesma razão:** o Diego não é
reconhecido por nenhum — sem as amostras de 20/08, o banco não o tem bem o
bastante (melhor 0,644 na §5). É o banco, não o diarizador.

**Os mesmos nomes saem dos dois** em todas as reuniões; o Nemotron abre um
rótulo a mais em 27/08 (dois sem nome, pouca fala), que custa pouco.

**O teto de 8 falantes não pesa:** no acervo, 6 de 111 transcrições têm mais
de 8 rótulos além do dono, contados pelo pyannote — que parte gente em duas e
nunca foi revisado. Decidido pelo dono do produto em 29/09/2026: raro demais
para desenhar em volta.

### Os nomes pelo caminho do app — 29/09/2026

A tabela acima reconhece com 30 s de fala limpa por quadro, e **contra todas as
gerações do banco**. O app faz diferente (`AprendizadoDeVozes.ReconhecerAsync`):
trechos dos segmentos da transcrição com 0,5 s de folga, guarda do microfone,
15 s, e **só amostras `regras = 2`** (`Vozes.Conta`). Refeito desse jeito, sem as
amostras da própria reunião:

```
                         pyannote              Nemotron
27/08  Paloma           ✓ 0,845               ✓ 0,845
       Eduardo          ✓ 0,802               ✓ 0,802
       fala com nome     94,9%                 95,1%
25/08  Diego            ✓ 0,793               ✓ 0,793
       Aline            ✗ 0,682               ✗ 0,682
       fala com nome     90,3%                 90,7%
```

**Os mesmos nomes, com a mesma semelhança.** O que não é reconhecido não o é
pelos dois, e por causa do banco: Aline tem 2 amostras da geração 2, e Roger e
Antonio (21/08), que ficaram sem nome no teste de ponta a ponta com o
`nemotron-3`, têm 2 e 1 — a 0,639 e 0,686, logo abaixo do limiar. Nomeá-los uma
vez grava amostras novas e resolve, com qualquer diarizador.

**O teste de ponta a ponta** (21/08, `Sidecar.exe --diarizacao nemotron-3`, no
Python embarcado): 97,7% das palavras com o falante certo contra o Gemini,
contra 95,0% do pipeline de hoje na mesma reunião.

## 4. Custo

**Medido com a placa livre** (`nvidia-smi` antes de cada rodada). A primeira
rodada do dia disputou a 2060 com uma reunião de verdade — legenda e pergunta
ligadas, 67–100% de uso, 5,7 GB — e ficou só como piso.

```
                     velocidade      ocupação da placa ao vivo
offline              81–226x            —
low_latency            9–13x           ~10%
very_low_latency        7–8x           ~13%
ultra_low_latency         4x           ~25%

VRAM do processo: ~1,2 GB até 32 min de áudio, ~2,0 GB com 49 min
```

A velocidade do offline oscila entre rodadas sem carga aparente de outro
processo; a faixa é o que se viu.

**A VRAM cresce com a duração, e é da ferramenta, não do modelo.** O
`Nemotron3.logits` passa o mel da gravação inteira pelo `embed.onnx` de uma vez,
e 49 min de mel pesam. Os passos do `step.onnx` têm tamanho fixo (cache + FIFO +
bloco). O sidecar tem de embutir por bloco — que é o que o streaming de verdade
faz de qualquer jeito, porque o áudio chega aos poucos —, e aí o pico fica no
dos pesos.

**O streaming é caro por passo, não por modelo.** Cada passo reprocessa o cache
inteiro (264 + 264 quadros) para avançar um bloco de 3 a 9. É por isso que a
velocidade cai de 200x para 4x, e é o que fp16, IO binding e grafo CUDA podem
cortar — nenhum tentado.

**O que não está medido, e decide:** esse custo **ao lado da legenda**. A 2060 é
compartilhada com o Meet e com a legenda, com a folga intermitente da
[CONVERGENCIA.md](CONVERGENCIA.md) §5. É o `MOD-1`.

---

## 4b. Ao lado da legenda — o `MOD-1`, 29/09/2026

**Dois processos, como os dois sidecars rodariam:** a legenda
(`tools/medir_legenda.py`, `transcribe_cpp` CUDA) e o Nemotron em streaming
(`tools/medir_mod1.py`), os dois no ritmo do relógio, 10 min da
`2026-08-20_15-59-20`, com o `nvidia-smi` amostrado a cada 0,5 s. Placa livre
antes (24%, 1,17 GB — só a área de trabalho), **sem o Meet aberto**.

**O embed passou a ser por bloco**, com 8 quadros de contexto à esquerda, que é
o que um sidecar pode fazer. Decide **100% igual** ao embed da gravação inteira
(2 min, `low_latency`), e a VRAM deixa de crescer com a duração.

```
                            legenda              Nemotron              placa
                      xRT   ciclo  parede    passo p50/p90  atraso máx   uso p90  VRAM máx
legenda sozinha       3,17   28%    679 s          —             —          30%    2,2 GB
+ low_latency         2,90   30%    697 s    106 / 207 ms     0,45 s        28%    3,7 GB
+ ultra_low_latency   0,78   61%   1261 s    113 / 237 ms     0,75 s        38%    4,5 GB
```

**`low_latency` cabe.** A legenda perde 8% de vazão (3,17× → 2,90×) e
continua bem acima de 1,5×; o Nemotron acompanha o relógio com folga — atraso
mediano de 0,11 s, que não cresce no último terço. **É o critério de saída do
`MOD-1` cumprido**, e os cortes de custo do passo 2 (fp16, IO binding, grafo
CUDA) deixam de ser pré-requisito.

**`ultra_low_latency` não cabe, e quem paga é a legenda.** O Nemotron se
mantém no relógio (1,76×), mas a legenda cai para **0,78×** — abaixo de 1×, a
fila cresce para sempre. Um passo a cada 240 ms disputa a placa com os feeds de
200 ms da legenda, e ela perde.

**Ressalvas, que decidem o que medir antes de ligar para o usuário:**

- **sem o Meet.** O Meet ocupou até 5,7 GB e 67–100% da placa na reunião de
  25/09; 3,7 GB aqui mais o Meet passa dos 6 GB. **Esta é a medição que falta**;
- a `parede` da legenda passa dos 600 s mesmo sozinha, porque o
  `medir_legenda.py` dorme a sobra de cada feed e não recupera o que um feed
  caro atrasou. O número é pessimista, e igual nos três braços;
- WSL, e não o Python embarcado no Windows; 10 min, e não uma reunião inteira.

## 4c. Em reunião de verdade, com o Meet e a pergunta — 30/09/2026

**Medido pelo dono do produto**, com a chave "Nomes ao vivo na legenda (teste)":
legenda, Nemotron ao vivo e o Meet juntos ficaram em **3,3 GB**; com uma
pergunta sendo respondida, **pico de 5,7 GB**, e a legenda não atrasou aos
24 min de reunião. É o que faltava ao `MOD-1`: o `low_latency` cabe na 2060 com
tudo ligado.

**O nome na legenda, conferido na `2026-09-30_15-30-05`** (26 min, Vanessa,
Monlevade e Carla). O nome ao vivo não é gravado, então foi reproduzido: o
mesmo `FluxoAoVivo` sobre o `system.wav`, a mesma regra de nome por trecho, e os
nomes que o registro diz que cada vaga recebeu — contra a transcrição final.

```
trechos dos outros                  439
com o nome certo                    418   95,2%
errado, logo depois de trocar         6 de  37   16%
errado, com a mesma pessoa           15 de 402   3,7%
```

**O erro mora na troca de pessoa**, como o dono viu nas falas curtas: um trecho
da legenda tem ~1 s e às vezes atravessa a troca. Deslocar a janela (−800 a
+300 ms) ou olhar só o fim do trecho foi medido e **piora o total** — o que se
ganha na troca se perde no resto. Separar dentro do trecho pediria o tempo de
cada palavra, que o streaming não dá (``snapshot()`` devolve zero palavras). A
regra fica como está.

## 5. Memória de vozes

O modelo não guarda voz: a saída é probabilidade por quadro, e o slot é por
sessão. Três caminhos, medidos entre as duas reuniões que têm pessoas em comum
(André Yuri e André Monlevade estão em 20/08 e em 21/08).

### A · o vetor wespeaker do app sobre os falantes do Nemotron — funciona

O mesmo `ExtratorDeVoz` do sidecar, com 30 s de fala limpa de cada slot, e o
`Reconhecer` do app (centroide por dispositivo e faixa, limiar 0,70) contra o
banco real, de 46 pessoas:

```
20/08  Diego Lacerda     não reconhecido   (melhor 0,644)
20/08  André Yuri        André Yuri             0,915
20/08  André Monlevade   André Monlevade        0,807
21/08  André Yuri        André Yuri             0,905
21/08  Eduardo Almeida   Eduardo Almeida        0,843
21/08  Antonio Knauth    Antonio Knauth         0,853
21/08  André Monlevade   André Monlevade        0,794
21/08  Roger Medeiros    Roger Medeiros         0,766
```

**7 de 8.** Entre as reuniões, sem passar pelo banco: a mesma pessoa dá 0,878 e
0,758; pessoas diferentes, no máximo 0,348.

**Ressalva:** 11 amostras do banco vieram destas duas reuniões (7 de 20/08, 4 de
21/08), então o reconhecimento é otimista. A matriz entre reuniões não tem esse
viés.

**O que isso quer dizer:** o reconhecimento de vozes **não depende do
diarizador**. Ele recebe trechos e devolve um vetor; trocar quem corta os trechos
não mexe no banco, no formato, nem nas vozes já aprendidas.

### B · matrícula pela ordem de chegada — funciona, e é o que as demonstrações fazem

Os slots são numerados por quem fala primeiro. Então 15 s de cada pessoa
conhecida, postos **antes** do áudio da reunião, fazem dos slots 0 e 1 essas
pessoas. Tirados de 20/08, postos antes de 21/08:

```
                    offline    low_latency
André Yuri           95,8%        95,8%       das palavras dele no slot dele
André Monlevade     100,0%       100,0%
de outras pessoas     5            5          palavras postas num slot matriculado
```

**Nomeia ao vivo, sem wespeaker, desde o primeiro segundo.** Os custos:

- cada matriculado ocupa **um dos 8 slots**, e matricular quem não veio é slot
  perdido — a lista viria da agenda, não do banco inteiro;
- o banco guarda só **4 s** de áudio por amostra, para auditoria (ver o
  `tools/conferir_voz_onnx.py`), e o teste usou 15 s. Ou se mede se 4 s bastam,
  ou o banco passa a guardar mais áudio.

### C · um vetor das camadas internas do Nemotron — não serve cru

A média das camadas 8, 16, 24 e 31 nos quadros de cada falante: tudo dá
cosseno de 0,90 a 0,998, pessoas diferentes inclusive. A diagonal ainda é a
maior de cada linha, então há sinal. Extraí-lo (centrar, projetar) é pesquisa,
e o A já existe.

### O que isso sugere, sem estar decidido

**B ao vivo** (nomes na legenda desde o começo) e **A na passada final** (o banco
continua sendo o que é).

---

**Uma quinta reunião com Gemini entrou em 30/09/2026:** `2026-09-30_09-58-30`,
*Weekly — Bandeirantes*, 19 min, Diego Lacerda e Hubener Kassio. Já está nas
listas do `medir_nemotron3.py` e do `medir_mod2.py`; as tabelas acima são das
quatro de antes, e ainda não foram refeitas com ela.

## 6. Como reproduzir

As três ferramentas estão em `tools/`, e os grafos em `tools/_nemotron3/`
(derivado, fora do git):

```bash
# os grafos, do checkpoint (precisa do transformers do git)
uv run --with "git+https://github.com/huggingface/transformers" \
    --with onnx --with librosa python tools/exportar_nemotron3_onnx.py

# as três réguas; confira o nvidia-smi antes de "medir"
uv run --with "git+https://github.com/huggingface/transformers" \
    --with onnxruntime-gpu==1.23.2 --with soundfile \
    python tools/medir_nemotron3.py {validar,medir,vozes}
```

`validar` é a régua do porte (§2), `medir` é a §3 e a §4, `vozes` é a §5.

---

## 7. Fontes

- [Cartão do modelo](https://huggingface.co/nvidia/Nemotron-3-Diarization) e o
  `ASR_INTEGRATION_GUIDE.md` do mesmo repositório — a integração com o
  `nemotron-3.5-asr-streaming` por `masked_asr`, e o *"not biometric
  identities"*;
- [NealCaren/Nemotron-3-Diarization-ONNX](https://huggingface.co/NealCaren/Nemotron-3-Diarization-ONNX) — a receita dos dois grafos;
- [NVIDIA/NeMo-Speech.cpp](https://github.com/NVIDIA/NeMo-Speech.cpp), PRs #50 e #52;
- `transformers`, `models/nemotron3_diarization/` — a referência do porte.
