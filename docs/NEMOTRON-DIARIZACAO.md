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

<!-- TABELA -->

**Ao vivo custa quase nada em acerto** — no pior caso, 0,4 ponto do offline.

---

## 4. Custo

**Medido com a placa livre** (`nvidia-smi` antes de cada rodada). A primeira
rodada do dia disputou a 2060 com uma reunião de verdade — legenda e pergunta
ligadas, 67–100% de uso, 5,7 GB — e ficou só como piso.

```
                     velocidade      ocupação da placa ao vivo
offline               81–223x            —
low_latency            9–13x           ~10%
very_low_latency        7–8x           ~13%
ultra_low_latency         4x           ~25%

VRAM do processo: ~1,2 GB (pesos fp32 + arena do onnxruntime)
```

A velocidade do offline oscila entre rodadas sem carga aparente de outro
processo; a faixa é o que se viu.

**O streaming é caro por passo, não por modelo.** Cada passo reprocessa o cache
inteiro (264 + 264 quadros) para avançar um bloco de 3 a 9. É por isso que a
velocidade cai de 200x para 4x, e é o que fp16, IO binding e grafo CUDA podem
cortar — nenhum tentado.

**O que não está medido, e decide:** esse custo **ao lado da legenda**. A 2060 é
compartilhada com o Meet e com a legenda, com a folga intermitente da
[CONVERGENCIA.md](CONVERGENCIA.md) §5. É o `MOD-1`.

---

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
