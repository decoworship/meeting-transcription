# Porte da diarização para ONNX — plano de implementação

> **Para quem executa:** SUB-SKILL OBRIGATÓRIA — use
> `superpowers:subagent-driven-development` (recomendada) ou
> `superpowers:executing-plans` para implementar tarefa a tarefa. Os passos usam
> caixas (`- [ ]`) para acompanhamento.

**Objetivo:** substituir o `torch` pelo ONNX Runtime com provedor CUDA no
pipeline de diarização, tirando ~4,5 GB dos 18 GB de `motores/`, **sem mudar a
matemática nem re-extrair o banco de vozes**.

**Arquitetura:** o `motor.py` (o sidecar) não muda — o protocolo com o C# é o
contrato, e as ops `diarizar`, `voz` e `modelo_de_voz` continuam iguais. Atrás
delas, um pipeline novo em `motores/diarizacao/pipeline/`: dois modelos em ONNX,
o fbank e o pooling em numpy, e o VBx/PLDA vindo do pyannote já vendorizado. As
duas implementações convivem atrás da chave `motor_de_diarizacao`, com `"torch"`
como padrão até as réguas fecharem.

**Tech stack:** Python 3.12 (o embarcado do app), numpy, scipy, sklearn,
`onnxruntime-gpu`, `einops`, `pyannote.core` (livre de torch), `pyannote.pipeline`
(livre de torch no caminho importado).

**Spec:** [`docs/DIARIZACAO-ONNX.md`](../../DIARIZACAO-ONNX.md) — leia antes de
começar. O plano argumenta a partir dela.

---

## Restrições globais

- **Nada de `import torch` em `motores/diarizacao/pipeline/`.** É o ponto
  inteiro. Cada tarefa termina conferindo `"torch" not in sys.modules`.
- **`from pyannote.audio.x import y` é proibido.** O
  `pyannote/audio/__init__.py` importa `core.inference`, `core.io` e
  `core.model`, e os três importam torch. `pyannote.core` e `pyannote.pipeline`
  **são permitidos** — não têm torch no caminho importado.
- **Toda sessão ONNX confere o provedor efetivo** com
  `session.get_providers()`, e falha alto se o CUDA EP não entrou. Pedir o
  provedor não é obtê-lo: em 18/09/2026 o CUDA EP caiu para CPU em silêncio e
  quase entrou no registro como resultado.
- **Os `.bin` do torch ficam onde estão.** Nada é apagado neste plano. A limpeza
  é depois das réguas `V1`–`V4` da spec.
- **O `motor.py` não muda** até a Tarefa 7.
- Comentários e mensagens em **português**, como o resto de `motores/`.

### Constantes medidas — copie exatamente, não deduza

```python
# SEGMENTAÇÃO (community-1/segmentation)
TAXA = 16000
JANELA_SEG = 10.0            # segundos
PASSO_SEG = 1.0              # segundos — o passo real do Inference
QUADROS_SEG = 589            # saída para uma janela de 10 s
CLASSES_POWERSET = 7
FALANTES_LOCAIS = 3
POWERSET_MAX_CLASSES = 2
# receptive field, para a contagem de falantes e a reconstrução
RF_INICIO, RF_DURACAO, RF_PASSO = 0.0, 0.0619375, 0.016875

# EMBEDDING (community-1/embedding — WeSpeakerResNet34)
JANELA_EMB = 5.0             # segundos
DIMENSAO_EMB = 256

# FBANK do Kaldi, com os parâmetros que o pyannote passa
N_MEL = 80
FB_JANELA, FB_PASSO, FB_PAD = 400, 160, 512   # 25 ms, 10 ms, potência de 2
PREENFASE = 0.97
EPS = np.float32(1.1920929e-07)               # torch.finfo(torch.float).eps

# LOTES — vêm do config.yaml do community-1
LOTE_SEG = 32
LOTE_EMB = 32

# CLUSTERING — também do config.yaml
VBX_THRESHOLD, VBX_FA, VBX_FB = 0.6, 0.07, 0.8
```

**A matriz powerset → multirrótulo, `(7, 3)`**, na ordem exata:

```python
MAPA_POWERSET = np.array([
    [0, 0, 0],   # ninguém
    [1, 0, 0],   # só o falante 1
    [0, 1, 0],
    [0, 0, 1],
    [1, 1, 0],   # 1 e 2 juntos
    [1, 0, 1],
    [0, 1, 1],
], dtype=np.float32)
```

### Caminhos

```
MODELOS = .../MeetingApp/motores/diarizacao/modelos/community-1
  segmentation/pytorch_model.bin      os pesos originais (ficam)
  embedding/pytorch_model.bin         idem
  plda/plda.npz  plda/xvec_transform.npz
GRAVACAO_GABARITO = .../MeetingRecordings/2026-08-25_08-59-22/mix.wav
```

---

## ⚠️ Correção que este plano carrega, e ela invalida um artefato de 18/09/2026

**O `embedding.onnx` exportado em 18/09/2026 NÃO SERVE ao pipeline, e o
`segmentacao.onnx` serve.**

O `S1` (e a exportação que eu repeti) exportou `SoResNet(modelo.resnet)`, que
recebe **só o fbank**. Mas o pipeline não chama o embedding assim — ele chama
`self._embedding(waveform_batch, masks=mask_batch)`, que desce até
`self.resnet(fbank, weights=weights)`. **Os pesos entram no pooling
estatístico**, e um embedding sem eles é o vetor do trecho inteiro em vez do
vetor daquele falante naquele trecho.

Usar o ONNX de 18/09 no pipeline **roda e devolve números plausíveis** — e
erra todos os vetores de trecho com mais de um falante. É falha silenciosa, e é
por isso que ela está no topo deste plano.

**A saída é partir o modelo em dois na exportação** (Tarefa 2):

```
fbank → [ONNX codificador] → quadros (B, dim, canal, T)
                                ↓
                       pooling ponderado em numpy      ← os pesos entram AQUI
                                ↓
        estatísticas (B, 2*dim*canal) → [ONNX cabeça] → embedding (B, 256)
```

**O `_pool` do pyannote, que a Tarefa 2 reimplementa em numpy** — copiado de
`models/blocks/pooling.py`, e os dois `1e-8` são parte da matemática:

```
v1   = sum(w) + 1e-8
mean = sum(x * w) / v1
dx2  = (x - mean)²
v2   = sum(w²)
var  = sum(dx2 * w) / (v1 - v2 / v1 + 1e-8)
std  = sqrt(var)
saída = concat([mean, std])
```

> **Nota sobre o `S1`.** A medição dele continua **válida para o que ela mediu**:
> o vetor de voz da op `voz`, que o núcleo monta concatenando trechos limpos de
> uma pessoa — ali não há máscara, e `weights=None`. O que este plano corrige é o
> uso **dentro do pipeline de diarização**, que é outro chamador.

---

## Estrutura de arquivos

| arquivo | responsabilidade |
|---|---|
| `motores/diarizacao/pipeline/vendor/` | **já existe** (commit `9971a28`): `vbx.py`, `plda.py`, `signal.py`, `clustering.py`. Não mexer. A Tarefa 6 acrescenta `diarizacao_utils.py` e `agregacao.py` |
| `motores/diarizacao/pipeline/fbank.py` | o log-mel do Kaldi em numpy. Sem estado |
| `motores/diarizacao/pipeline/sessao.py` | criar sessão ONNX conferindo o provedor efetivo |
| `motores/diarizacao/pipeline/segmentacao.py` | janela deslizante + ONNX + powerset → multirrótulo |
| `motores/diarizacao/pipeline/embedding.py` | fbank + codificador ONNX + pooling ponderado + cabeça ONNX |
| `motores/diarizacao/pipeline/diarizacao.py` | a cola: contagem de falantes, máscaras, clustering, reconstrução |
| `tools/exportar_diarizacao_onnx.py` | exporta os três `.onnx`. Roda no venv do WSL, **não** no Python embarcado |
| `tools/conferir_diarizacao_onnx.py` | a régua `V1`: ONNX contra torch na mesma gravação |
| `motores/diarizacao/motor.py` | **só a Tarefa 7**: a chave `motor_de_diarizacao` |

---

## Tarefa 1: O fbank em numpy

**Arquivos:**
- Criar: `motores/diarizacao/pipeline/fbank.py`
- Teste: `motores/diarizacao/pipeline/testes/test_fbank.py`

**Interfaces:**
- Consome: nada
- Produz: `banco_mel() -> np.ndarray` de forma `(80, 257)`;
  `fbank(onda: np.ndarray, mel: np.ndarray) -> np.ndarray` de forma
  `(quadros, 80)`, com `onda` **já multiplicada por 2¹⁵**;
  `fbank_centrado(onda, mel) -> np.ndarray` de forma `(1, quadros, 80)`

- [ ] **Passo 1: escrever o teste que falha**

O gabarito é o `compute_fbank` do próprio pyannote, com torch — o teste usa
torch **de propósito**, porque é o teste, não o produto.

```python
# motores/diarizacao/pipeline/testes/test_fbank.py
import numpy as np, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

MODELO = ("/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores"
          "/diarizacao/modelos/community-1/embedding/pytorch_model.bin")

def test_fbank_bate_com_o_pyannote():
    """O fbank em numpy contra o compute_fbank do pyannote, no mesmo áudio."""
    import torch
    from pyannote.audio import Model
    from fbank import banco_mel, fbank

    rng = np.random.default_rng(0)
    onda = rng.standard_normal(16000 * 5).astype(np.float32) * 0.1

    m = Model.from_pretrained(MODELO).eval()
    with torch.no_grad():
        esperado = m.compute_fbank(torch.from_numpy(onda)[None, None, :]).numpy()[0]

    obtido = fbank(onda * (1 << 15), banco_mel())
    obtido = obtido - obtido.mean(axis=0, keepdims=True)   # o compute_fbank centra

    assert obtido.shape == esperado.shape, (obtido.shape, esperado.shape)
    assert np.abs(obtido - esperado).max() < 1e-3
```

- [ ] **Passo 2: rodar e ver falhar**

```bash
cd /home/andre/projects/meeting-transcription
uv run python -m pytest motores/diarizacao/pipeline/testes/test_fbank.py -v
```

Esperado: FAIL com `ModuleNotFoundError: No module named 'fbank'`.

- [ ] **Passo 3: implementar**

```python
# motores/diarizacao/pipeline/fbank.py
"""O log-mel do Kaldi em numpy, sem torch.

O ``compute_fbank`` do pyannote embrulha o fbank num ``torch.vmap``, que
nenhum exportador de ONNX atravessa, e o que sobra dele sem o vmap é o
``aten::fft_rfft``, que o opset 17 não tem. Daí este arquivo — que é o mesmo
caminho que o ``infer_onnx.py`` do WeSpeaker já fazia, e que o
``tools/medir_vetor_onnx.py`` (o S1) validou em 10/09/2026.

Ver docs/DIARIZACAO-ONNX.md §3.
"""
import numpy as np

TAXA, N_MEL = 16000, 80
JANELA, PASSO, PAD = 400, 160, 512          # 25 ms, 10 ms, próxima potência de 2
PREENFASE = 0.97
EPS = np.float32(1.1920929e-07)             # torch.finfo(torch.float).eps


def _hamming(n: int) -> np.ndarray:
    """A janela do Kaldi: ``periodic=False``, alpha 0,54."""
    return (0.54 - 0.46 * np.cos(2 * np.pi * np.arange(n) / (n - 1))).astype(np.float32)


def banco_mel() -> np.ndarray:
    """Os filtros mel, com os parâmetros que o pyannote passa ao Kaldi.

    Vem do torchaudio por ser tabela de constantes — é calculado uma vez na
    exportação e guardado em .npy, para o app não importar torchaudio.
    """
    from torchaudio.compliance.kaldi import get_mel_banks
    import torch
    banco = get_mel_banks(N_MEL, PAD, TAXA, 20.0, 0.0, 100.0, -500.0, 1.0)
    banco = banco[0] if isinstance(banco, tuple) else banco
    return torch.nn.functional.pad(banco, (0, 1)).numpy().astype(np.float32)


def fbank(onda: np.ndarray, mel: np.ndarray) -> np.ndarray:
    """O log-mel. ``onda`` já vem na escala do pyannote (×2¹⁵)."""
    m = 1 + (len(onda) - JANELA) // PASSO    # snip_edges=True
    if m <= 0:
        return np.zeros((0, N_MEL), np.float32)

    idx = np.arange(m)[:, None] * PASSO + np.arange(JANELA)[None, :]
    q = onda[idx].astype(np.float32)
    q = q - q.mean(axis=1, keepdims=True)                      # remove_dc_offset
    ant = np.concatenate([q[:, :1], q[:, :-1]], axis=1)        # pad "replicate"
    q = (q - PREENFASE * ant) * _hamming(JANELA)[None, :]
    q = np.pad(q, ((0, 0), (0, PAD - JANELA)))

    pot = np.abs(np.fft.rfft(q, axis=1).astype(np.complex64)) ** 2
    return np.log(np.maximum(pot.astype(np.float32) @ mel.T, EPS))


def fbank_centrado(onda: np.ndarray, mel: np.ndarray) -> np.ndarray:
    """O que o ``compute_fbank`` entrega: escala, fbank e a média global fora."""
    f = fbank(onda.astype(np.float32) * np.float32(1 << 15), mel)
    return (f - f.mean(axis=0, keepdims=True))[None, :, :]
```

- [ ] **Passo 4: rodar e ver passar**

```bash
uv run python -m pytest motores/diarizacao/pipeline/testes/test_fbank.py -v
```

Esperado: PASS.

- [ ] **Passo 5: commitar**

```bash
git add motores/diarizacao/pipeline/fbank.py motores/diarizacao/pipeline/testes/
git commit -m "feat(diarizacao-onnx): o fbank do Kaldi em numpy

O compute_fbank do pyannote não exporta para ONNX — o torch.vmap dele não
é atravessado por nenhum exportador, e o que sobra sem o vmap é o
aten::fft_rfft, ausente do opset 17. Este é o mesmo caminho que o
infer_onnx.py do WeSpeaker já fazia e que o S1 validou.

O teste compara contra o compute_fbank de verdade, e usa torch de
propósito: torch no teste é gabarito, torch no produto é os 4,8 GB."
```

---

## Tarefa 2: Exportar os três ONNX, com o pooling ponderado partido fora

**Arquivos:**
- Criar: `tools/exportar_diarizacao_onnx.py`
- Criar (saída): `modelos/community-1/segmentation/model.onnx`,
  `embedding/codificador.onnx`, `embedding/cabeca.onnx`, `embedding/mel.npy`

**Interfaces:**
- Consome: `fbank.banco_mel` (Tarefa 1)
- Produz: os quatro artefatos acima. Formas:
  `segmentation/model.onnx`: entrada `audio (B,1,160000)` → saída
  `segmentacao (B,589,7)`;
  `embedding/codificador.onnx`: entrada `fbank (B,T,80)` → saída
  `quadros (B,dim,canal,T2)`;
  `embedding/cabeca.onnx`: entrada `estatisticas (B,E)` → saída
  `embedding (B,256)`

- [ ] **Passo 1: escrever o teste que falha**

```python
# motores/diarizacao/pipeline/testes/test_exportacao.py
import numpy as np, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

RAIZ = Path("/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores"
            "/diarizacao/modelos/community-1")

def test_embedding_onnx_respeita_os_pesos():
    """O caminho ONNX com máscara bate com o torch COM a mesma máscara.

    É o teste que pega o defeito do artefato de 18/09/2026: um embedding que
    ignora `weights` passa num teste sem máscara e erra todo trecho com dois
    falantes.
    """
    import torch, onnxruntime as ort
    from pyannote.audio import Model
    from fbank import banco_mel, fbank_centrado
    from embedding import estatisticas_ponderadas

    rng = np.random.default_rng(0)
    onda = rng.standard_normal(16000 * 5).astype(np.float32) * 0.1
    # máscara que liga só a primeira metade — é onde o defeito aparece
    m = Model.from_pretrained(RAIZ / "embedding" / "pytorch_model.bin").eval()
    with torch.no_grad():
        n_quadros = m.compute_fbank(torch.from_numpy(onda)[None, None, :]).shape[1]
    pesos = np.zeros((1, n_quadros), np.float32)
    pesos[0, : n_quadros // 2] = 1.0

    with torch.no_grad():
        esperado = m(torch.from_numpy(onda)[None, None, :],
                     weights=torch.from_numpy(pesos)).numpy()

    cod = ort.InferenceSession(str(RAIZ / "embedding" / "codificador.onnx"),
                               providers=["CPUExecutionProvider"])
    cab = ort.InferenceSession(str(RAIZ / "embedding" / "cabeca.onnx"),
                               providers=["CPUExecutionProvider"])
    fb = fbank_centrado(onda, banco_mel())
    quadros = cod.run(None, {"fbank": fb})[0]
    stats = estatisticas_ponderadas(quadros, pesos)
    obtido = cab.run(None, {"estatisticas": stats})[0]

    assert np.abs(obtido - esperado).max() < 1e-3, np.abs(obtido - esperado).max()
```

- [ ] **Passo 2: rodar e ver falhar**

```bash
uv run --with onnx --with onnxscript --with onnxruntime \
  python -m pytest motores/diarizacao/pipeline/testes/test_exportacao.py -v
```

Esperado: FAIL — os `.onnx` não existem.

- [ ] **Passo 3: escrever o exportador**

```python
#!/usr/bin/env python3
"""Exporta os modelos da diarização para ONNX (docs/DIARIZACAO-ONNX.md §3).

**Roda no venv do WSL, não no Python embarcado do app.** Ele precisa de torch
(para ler os pesos) e de onnx/onnxscript (para exportar) — que é exatamente o
que o app deixa de precisar depois deste porte.

Uso::

    uv run --with onnx --with onnxscript --with onnxruntime \\
      python tools/exportar_diarizacao_onnx.py
"""
import sys, warnings
from pathlib import Path
warnings.filterwarnings("ignore")
import numpy as np, torch

RAIZ = Path("/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores"
            "/diarizacao/modelos/community-1")
sys.path.insert(0, str(Path(__file__).resolve().parents[1]
                       / "motores/diarizacao/pipeline"))
from fbank import banco_mel  # noqa: E402

from pyannote.audio import Model


def exportar_segmentacao():
    """O PyanNet inteiro — este exporta sem truque."""
    m = Model.from_pretrained(RAIZ / "segmentation" / "pytorch_model.bin").eval()
    x = torch.randn(1, 1, 160_000)          # 10 s @ 16 kHz
    alvo = RAIZ / "segmentation" / "model.onnx"
    torch.onnx.export(
        m, (x,), str(alvo),
        input_names=["audio"], output_names=["segmentacao"],
        opset_version=17, dynamo=False,
        dynamic_axes={"audio": {0: "lote"}, "segmentacao": {0: "lote"}},
    )
    print(f"segmentation/model.onnx  {alvo.stat().st_size/1048576:.1f} MB")


def exportar_embedding():
    """Partido em dois, com o pooling ponderado fora.

    **O porquê, e é o achado que motivou este plano.** O pipeline chama
    `resnet(fbank, weights=...)`, e os pesos entram no pooling estatístico.
    Exportar o modelo inteiro sem os pesos produz o vetor do trecho em vez do
    vetor daquele falante — e isso não levanta exceção nenhuma.

    Partir no pooling deixa a matemática ponderada em numpy, onde ela é
    legível e testável, e mantém os dois grafos ONNX de forma fixa.
    """
    m = Model.from_pretrained(RAIZ / "embedding" / "pytorch_model.bin").eval()
    r = m.resnet

    class Codificador(torch.nn.Module):
        """fbank → quadros, tudo antes do pooling."""
        def __init__(self, r):
            super().__init__(); self.r = r
        def forward(self, fbank):
            import torch.nn.functional as F
            x = fbank.permute(0, 2, 1).unsqueeze(1)
            x = F.relu(self.r.bn1(self.r.conv1(x)))
            x = self.r.layer1(x); x = self.r.layer2(x)
            x = self.r.layer3(x); x = self.r.layer4(x)
            return x

    class Cabeca(torch.nn.Module):
        """estatísticas → embedding, tudo depois do pooling."""
        def __init__(self, r):
            super().__init__(); self.r = r
        def forward(self, stats):
            return self.r.seg_1(stats)

    cod = Codificador(r).eval()
    fb = torch.randn(1, 498, 80)
    with torch.no_grad():
        quadros = cod(fb)
    print(f"  quadros: {tuple(quadros.shape)}")

    torch.onnx.export(
        cod, (fb,), str(RAIZ / "embedding" / "codificador.onnx"),
        input_names=["fbank"], output_names=["quadros"],
        opset_version=17, dynamo=False,
        dynamic_axes={"fbank": {0: "lote", 1: "quadros"},
                      "quadros": {0: "lote", 3: "t"}},
    )

    b, dim, canal, t = quadros.shape
    stats = torch.randn(1, 2 * dim * canal)
    torch.onnx.export(
        Cabeca(r).eval(), (stats,), str(RAIZ / "embedding" / "cabeca.onnx"),
        input_names=["estatisticas"], output_names=["embedding"],
        opset_version=17, dynamo=False,
        dynamic_axes={"estatisticas": {0: "lote"}, "embedding": {0: "lote"}},
    )

    # `two_emb_layer` muda a saída do modelo, e o porte só cobre o caso False.
    # Falhar aqui, alto, é melhor que devolver o embedding errado calado.
    if getattr(r, "two_emb_layer", False):
        raise RuntimeError(
            "este modelo tem two_emb_layer=True: a Cabeca precisa de "
            "seg_bn_1 e seg_2 além do seg_1. Ver resnet.py:399-430."
        )

    np.save(RAIZ / "embedding" / "mel.npy", banco_mel())
    for n in ("codificador.onnx", "cabeca.onnx", "mel.npy"):
        print(f"embedding/{n}  "
              f"{(RAIZ/'embedding'/n).stat().st_size/1048576:.2f} MB")


if __name__ == "__main__":
    exportar_segmentacao()
    exportar_embedding()
```

- [ ] **Passo 4: implementar o `estatisticas_ponderadas` que o teste importa**

```python
# motores/diarizacao/pipeline/embedding.py  (primeira parte; o resto é a Tarefa 4)
"""O vetor de voz sem torch: fbank em numpy, ResNet em ONNX, pooling em numpy."""
import numpy as np


def estatisticas_ponderadas(quadros: np.ndarray, pesos: np.ndarray) -> np.ndarray:
    """O pooling estatístico ponderado — o `_pool` do pyannote, em numpy.

    **É aqui que os pesos entram, e é por isso que o modelo foi partido em
    dois na exportação.** Um embedding que ignore `pesos` roda sem erro e
    devolve o vetor do trecho inteiro em vez do vetor daquele falante: num
    trecho com dois falantes, o vetor sai contaminado, e nada avisa.

    Copiado de ``pyannote/audio/models/blocks/pooling.py`` (MIT). Os dois
    ``1e-8`` são parte da matemática, não guarda-chuva contra zero.

    Parameters
    ----------
    quadros : (lote, dim, canal, t) — a saída do codificador ONNX
    pesos : (lote, quadros_mascara) — a máscara por quadro do falante

    Returns
    -------
    (lote, 2 * dim * canal) — média e desvio concatenados
    """
    lote, dim, canal, t = quadros.shape
    x = quadros.reshape(lote, dim * canal, t)

    # o StatsPool interpola a máscara para o número de quadros da sequência,
    # com mode="nearest" — as duas grades têm passos diferentes
    if pesos.shape[1] != t:
        idx = (np.arange(t) * pesos.shape[1] / t).astype(np.int64)
        pesos = pesos[:, np.clip(idx, 0, pesos.shape[1] - 1)]

    w = pesos[:, None, :].astype(np.float32)          # (lote, 1, t)
    v1 = w.sum(axis=2) + 1e-8                         # (lote, 1)
    media = (x * w).sum(axis=2) / v1
    dx2 = np.square(x - media[:, :, None])
    v2 = np.square(w).sum(axis=2)
    var = (dx2 * w).sum(axis=2) / (v1 - v2 / v1 + 1e-8)
    return np.concatenate([media, np.sqrt(var)], axis=1).astype(np.float32)
```

- [ ] **Passo 5: exportar e rodar o teste**

```bash
uv run --with onnx --with onnxscript --with onnxruntime \
  python tools/exportar_diarizacao_onnx.py
uv run --with onnx --with onnxscript --with onnxruntime \
  python -m pytest motores/diarizacao/pipeline/testes/test_exportacao.py -v
```

Esperado: PASS, com `maxabs < 1e-3`.

**Se falhar com diferença grande (>0,1):** o suspeito nº 1 é a interpolação da
máscara. Confira `t` (quadros do codificador) contra `pesos.shape[1]` e compare
com o que o `F.interpolate(..., mode="nearest")` do torch produz para os mesmos
tamanhos — o arredondamento do `nearest` do PyTorch é *floor* sobre a razão,
que é o que a linha do `idx` reproduz.

- [ ] **Passo 6: commitar**

```bash
git add tools/exportar_diarizacao_onnx.py motores/diarizacao/pipeline/embedding.py \
        motores/diarizacao/pipeline/testes/test_exportacao.py
git commit -m "feat(diarizacao-onnx): os modelos exportados, com o pooling partido fora

E este commit corrige um artefato meu de 18/09/2026. O embedding.onnx que
eu tinha exportado seguindo o S1 recebe só o fbank — mas o pipeline chama
resnet(fbank, weights=...), e os pesos entram no pooling estatístico. Um
embedding sem eles devolve o vetor do trecho em vez do vetor daquele
falante, e num trecho com dois falantes o vetor sai contaminado sem nada
avisar.

A saída é partir o modelo no pooling: codificador em ONNX, pooling
ponderado em numpy, cabeça em ONNX. A matemática ponderada fica legível e
testável, e o teste liga a máscara em metade dos quadros — que é onde o
defeito aparece e um teste sem máscara passaria."
```

---

## Tarefa 3: A sessão ONNX que confere o provedor

**Arquivos:**
- Criar: `motores/diarizacao/pipeline/sessao.py`
- Teste: `motores/diarizacao/pipeline/testes/test_sessao.py`

**Interfaces:**
- Produz: `abrir(caminho: str | Path, preferir_gpu: bool = True) -> tuple[ort.InferenceSession, str]`
  — devolve a sessão e o **provedor efetivo**

- [ ] **Passo 1: escrever o teste que falha**

```python
# motores/diarizacao/pipeline/testes/test_sessao.py
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

RAIZ = Path("/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores"
            "/diarizacao/modelos/community-1")

def test_devolve_o_provedor_efetivo_e_nao_o_pedido():
    from sessao import abrir
    s, prov = abrir(RAIZ / "segmentation" / "model.onnx", preferir_gpu=False)
    assert prov == "CPUExecutionProvider"
    assert prov in s.get_providers()

def test_cai_para_cpu_com_aviso_quando_nao_ha_gpu(capsys):
    from sessao import abrir
    s, prov = abrir(RAIZ / "segmentation" / "model.onnx", preferir_gpu=True)
    # numa máquina sem CUDA EP a queda é legítima, mas tem de ser DITA
    if prov != "CUDAExecutionProvider":
        assert "CUDA" in capsys.readouterr().err
```

- [ ] **Passo 2: rodar e ver falhar**

```bash
uv run --with onnxruntime python -m pytest \
  motores/diarizacao/pipeline/testes/test_sessao.py -v
```

Esperado: FAIL com `ModuleNotFoundError: No module named 'sessao'`.

- [ ] **Passo 3: implementar**

```python
# motores/diarizacao/pipeline/sessao.py
"""Abrir sessão ONNX dizendo em voz alta onde ela vai rodar.

**Por que este arquivo existe.** Em 18/09/2026 uma medição de CUDA EP
devolveu 32,11x o tempo real e quase entrou no registro como resultado. O
provedor tinha falhado ao carregar (`libcublasLt.so.13: cannot open shared
object file`) e caído para CPU **em silêncio** — 27 s que pareciam GPU e eram
CPU.

Pedir um provedor não é obtê-lo. Aqui o efetivo é sempre lido de volta com
`get_providers()` e devolvido a quem chamou.
"""
import sys
from pathlib import Path

import onnxruntime as ort


def abrir(caminho: str | Path, preferir_gpu: bool = True):
    """A sessão e o provedor que ela de fato usa.

    Returns
    -------
    (sessao, provedor_efetivo)
    """
    caminho = str(caminho)
    disponiveis = ort.get_available_providers()

    pedidos = []
    if preferir_gpu and "CUDAExecutionProvider" in disponiveis:
        pedidos.append("CUDAExecutionProvider")
    pedidos.append("CPUExecutionProvider")

    sessao = ort.InferenceSession(caminho, providers=pedidos)
    efetivo = sessao.get_providers()[0]

    if preferir_gpu and efetivo != "CUDAExecutionProvider":
        # não é erro — a máquina pode não ter NVIDIA. Mas é caro e tem de
        # aparecer: a diarização em CPU roda a 0,57x o tempo real.
        print(f"[diarizacao] CUDA indisponível, rodando em {efetivo}. "
              f"Disponíveis: {disponiveis}", file=sys.stderr, flush=True)

    return sessao, efetivo
```

- [ ] **Passo 4: rodar e ver passar**

```bash
uv run --with onnxruntime python -m pytest \
  motores/diarizacao/pipeline/testes/test_sessao.py -v
```

Esperado: PASS (os dois).

- [ ] **Passo 5: commitar**

```bash
git add motores/diarizacao/pipeline/sessao.py motores/diarizacao/pipeline/testes/test_sessao.py
git commit -m "feat(diarizacao-onnx): a sessão diz onde ela roda, e não onde foi pedida

Em 18/09/2026 uma medição de CUDA EP deu 32,11x e quase virou resultado.
O provedor tinha falhado ao carregar e caído para CPU em silêncio. Aqui o
provedor efetivo é lido de volta e devolvido a quem chamou, e a queda para
CPU é dita no stderr — ela não é erro, mas custa 46x."
```

---

## Tarefa 4: A segmentação, da onda aos quadros de falante

**Arquivos:**
- Criar: `motores/diarizacao/pipeline/segmentacao.py`
- Teste: `motores/diarizacao/pipeline/testes/test_segmentacao.py`

**Interfaces:**
- Consome: `sessao.abrir` (Tarefa 3)
- Produz: `class Segmentador` com
  `__init__(self, caminho_onnx, preferir_gpu=True)` e
  `__call__(self, onda: np.ndarray, taxa: int = 16000) -> SlidingWindowFeature`
  de dados `(n_janelas, 589, 3)` — **já em multirrótulo**, não em powerset

- [ ] **Passo 1: escrever o teste que falha**

```python
# motores/diarizacao/pipeline/testes/test_segmentacao.py
import numpy as np, sys, wave
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

RAIZ = Path("/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores"
            "/diarizacao/modelos/community-1")
WAV = ("/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings"
       "/2026-08-25_08-59-22/mix.wav")


def _onda(n_segundos):
    with wave.open(WAV, "rb") as w:
        taxa = w.getframerate()
        q = w.readframes(int(n_segundos * taxa))
    return np.frombuffer(q, np.int16).astype(np.float32) / 32768.0, taxa


def test_a_janela_deslizante_bate_com_o_Inference_do_pyannote():
    """60 s de áudio real, contra o Inference com skip_aggregation."""
    import torch
    from pyannote.audio import Model, Inference
    from segmentacao import Segmentador

    onda, taxa = _onda(60)

    m = Model.from_pretrained(RAIZ / "segmentation" / "pytorch_model.bin").eval()
    inf = Inference(m, duration=10.0, step=1.0, skip_aggregation=True, batch_size=32)
    esperado = inf({"waveform": torch.from_numpy(onda)[None], "sample_rate": taxa})

    obtido = Segmentador(RAIZ / "segmentation" / "model.onnx",
                         preferir_gpu=False)(onda, taxa)

    assert obtido.data.shape == esperado.data.shape, \
        (obtido.data.shape, esperado.data.shape)
    # as janelas têm de cair no mesmo lugar, senão tudo depois desalinha
    assert abs(obtido.sliding_window.start - esperado.sliding_window.start) < 1e-9
    assert abs(obtido.sliding_window.step - esperado.sliding_window.step) < 1e-9
    assert np.abs(obtido.data - esperado.data).max() < 1e-2
```

- [ ] **Passo 2: rodar e ver falhar**

```bash
uv run --with onnxruntime python -m pytest \
  motores/diarizacao/pipeline/testes/test_segmentacao.py -v
```

Esperado: FAIL com `ModuleNotFoundError: No module named 'segmentacao'`.

- [ ] **Passo 3: implementar**

```python
# motores/diarizacao/pipeline/segmentacao.py
"""A segmentação: onda → quem fala em cada quadro de cada janela.

O modelo vê 10 s por vez e anda de 1 s em 1 s, então cada instante é visto
por até 10 janelas. Juntar essas vistas é trabalho do clustering, não daqui —
esta classe devolve as janelas cruas, como o `Inference(skip_aggregation=True)`
do pyannote devolve.

**A saída do modelo é powerset**, não multirrótulo: 7 classes que são as
combinações de até 2 falantes entre 3. Converter é um produto de matriz, e a
matriz está abaixo — a ordem dela é a do `Powerset` do pyannote e trocar duas
linhas faz a diarização errar sem levantar exceção.
"""
import numpy as np
from pyannote.core import SlidingWindow, SlidingWindowFeature

from sessao import abrir

TAXA = 16000
JANELA, PASSO = 10.0, 1.0
LOTE = 32

#: powerset → multirrótulo, `(7, 3)`. Extraído do `Powerset(3, 2).mapping` em
#: 18/09/2026. A ordem importa: é ela que diz qual classe é "1 e 2 juntos".
MAPA_POWERSET = np.array([
    [0, 0, 0],
    [1, 0, 0],
    [0, 1, 0],
    [0, 0, 1],
    [1, 1, 0],
    [1, 0, 1],
    [0, 1, 1],
], dtype=np.float32)


class Segmentador:
    def __init__(self, caminho_onnx, preferir_gpu: bool = True):
        self.sessao, self.provedor = abrir(caminho_onnx, preferir_gpu)

    def __call__(self, onda: np.ndarray, taxa: int = TAXA) -> SlidingWindowFeature:
        if taxa != TAXA:
            raise ValueError(f"esperado {TAXA} Hz, veio {taxa}")

        n = int(JANELA * taxa)
        passo = int(PASSO * taxa)

        # `mode="pad"`: a última janela é completada com zeros em vez de
        # descartada. Descartá-la perderia até 10 s do fim da reunião.
        if len(onda) < n:
            onda = np.pad(onda, (0, n - len(onda)))
        inicios = list(range(0, max(1, len(onda) - n + 1), passo))
        if inicios[-1] + n < len(onda):
            inicios.append(len(onda) - n)

        janelas = np.stack([onda[i:i + n] for i in inicios])[:, None, :]

        saidas = []
        for i in range(0, len(janelas), LOTE):
            saidas.append(self.sessao.run(
                None, {"audio": janelas[i:i + LOTE].astype(np.float32)})[0])
        powerset = np.concatenate(saidas)      # (n_janelas, 589, 7)

        # argmax sobre as 7 classes, depois a matriz: é o que o
        # `Powerset.to_multilabel` faz quando o modelo já é powerset.
        vencedora = powerset.argmax(axis=-1)                   # (n_jan, 589)
        multi = MAPA_POWERSET[vencedora]                       # (n_jan, 589, 3)

        return SlidingWindowFeature(
            multi.astype(np.float32),
            SlidingWindow(start=0.0, duration=JANELA, step=PASSO),
        )
```

- [ ] **Passo 4: rodar e ver passar**

```bash
uv run --with onnxruntime python -m pytest \
  motores/diarizacao/pipeline/testes/test_segmentacao.py -v
```

Esperado: PASS.

**Se as formas não baterem:** o suspeito é a última janela. O `Inference` do
pyannote usa `mode="pad"`, e a lista de `inicios` acima o reproduz. Imprima
`obtido.data.shape` e `esperado.data.shape` e compare o número de janelas antes
de mexer em qualquer outra coisa.

- [ ] **Passo 5: commitar**

```bash
git add motores/diarizacao/pipeline/segmentacao.py \
        motores/diarizacao/pipeline/testes/test_segmentacao.py
git commit -m "feat(diarizacao-onnx): a segmentação, com a janela deslizante em numpy

O teste é contra o Inference(skip_aggregation=True) do pyannote sobre 60 s
de áudio real, e confere também onde as janelas caem: um deslocamento de
meia janela passaria no teste de forma e desalinharia tudo depois dele.

A matriz powerset->multirrótulo veio do Powerset(3,2).mapping e está
escrita por extenso. Trocar duas linhas dela faz a diarização errar sem
levantar exceção."
```

---

## Tarefa 5: O embedding por (janela, falante), com as máscaras certas

**Arquivos:**
- Modificar: `motores/diarizacao/pipeline/embedding.py` (acrescenta à Tarefa 2)
- Teste: `motores/diarizacao/pipeline/testes/test_embedding.py`

**Interfaces:**
- Consome: `fbank.banco_mel`, `fbank.fbank_centrado`, `sessao.abrir`,
  `embedding.estatisticas_ponderadas`
- Produz: `class Extrator` com
  `__init__(self, dir_embedding, preferir_gpu=True)` e
  `__call__(self, onda, segmentacao_binaria, excluir_sobreposicao=True) -> np.ndarray`
  de forma `(n_janelas, 3, 256)`

- [ ] **Passo 1: escrever o teste que falha**

```python
# motores/diarizacao/pipeline/testes/test_embedding.py
import numpy as np, sys, wave
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

RAIZ = Path("/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores"
            "/diarizacao/modelos/community-1")
WAV = ("/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings"
       "/2026-08-25_08-59-22/mix.wav")


def test_embeddings_batem_com_o_get_embeddings_do_pyannote():
    """60 s reais: o vetor de cada (janela, falante), contra o pyannote."""
    import torch
    from pyannote.audio import Model
    from pyannote.audio.pipelines import SpeakerDiarization
    from segmentacao import Segmentador
    from embedding import Extrator

    with wave.open(WAV, "rb") as w:
        taxa = w.getframerate(); q = w.readframes(60 * w.getframerate())
    onda = np.frombuffer(q, np.int16).astype(np.float32) / 32768.0

    seg = Segmentador(RAIZ / "segmentation" / "model.onnx", preferir_gpu=False)
    binaria = seg(onda, taxa)

    pipe = SpeakerDiarization(
        segmentation=str(RAIZ / "segmentation" / "pytorch_model.bin"),
        embedding=str(RAIZ / "embedding" / "pytorch_model.bin"),
        embedding_exclude_overlap=True,
    )
    arquivo = {"waveform": torch.from_numpy(onda)[None], "sample_rate": taxa,
               "uri": "teste"}
    esperado = pipe.get_embeddings(arquivo, binaria, exclude_overlap=True)

    obtido = Extrator(RAIZ / "embedding", preferir_gpu=False)(
        onda, binaria, excluir_sobreposicao=True)

    assert obtido.shape == esperado.shape, (obtido.shape, esperado.shape)
    # NaN aparece onde o falante não fala na janela, e tem de aparecer nos dois
    assert np.array_equal(np.isnan(obtido), np.isnan(esperado))
    ok = ~np.isnan(obtido)
    assert np.abs(obtido[ok] - esperado[ok]).max() < 1e-2
```

- [ ] **Passo 2: rodar e ver falhar**

```bash
uv run --with onnxruntime python -m pytest \
  motores/diarizacao/pipeline/testes/test_embedding.py -v
```

Esperado: FAIL com `ImportError: cannot import name 'Extrator'`.

- [ ] **Passo 3: implementar**

Acrescente ao `embedding.py`:

```python
import math
from pathlib import Path

import numpy as np
from einops import rearrange

from fbank import banco_mel, fbank_centrado
from sessao import abrir

TAXA = 16000
JANELA_EMB = 5.0          # a `duration` do modelo de embedding
DIMENSAO = 256
LOTE = 32


class Extrator:
    """O vetor de voz de cada par (janela, falante).

    **A máscara é o ponto.** Cada janela de 10 s tem até 3 falantes locais, e o
    vetor de cada um sai do MESMO áudio com uma máscara por quadro diferente.
    É por isso que o pooling é ponderado, e é por isso que o modelo foi partido
    em dois na exportação (docs/DIARIZACAO-ONNX.md, a correção do §3).
    """

    def __init__(self, dir_embedding, preferir_gpu: bool = True):
        d = Path(dir_embedding)
        self.cod, self.provedor = abrir(d / "codificador.onnx", preferir_gpu)
        self.cab, _ = abrir(d / "cabeca.onnx", preferir_gpu)
        mel = d / "mel.npy"
        self.mel = np.load(mel) if mel.exists() else banco_mel()

    def __call__(self, onda, segmentacao_binaria, excluir_sobreposicao=True):
        dados = segmentacao_binaria.data              # (n_jan, n_quadros, 3)
        n_jan, n_quadros, n_falantes = dados.shape
        janela = segmentacao_binaria.sliding_window

        if excluir_sobreposicao:
            # o mínimo de quadros que o modelo de embedding exige, convertido
            # da duração dele (5 s) para a grade de quadros da segmentação
            n_amostras = janela.duration * TAXA
            min_amostras = JANELA_EMB * TAXA
            min_quadros = math.ceil(n_quadros * min_amostras / n_amostras)
            limpos = 1.0 * (np.sum(dados, axis=2, keepdims=True) < 2)
            limpa = dados * limpos
        else:
            min_quadros = -1
            limpa = dados

        n = int(janela.duration * TAXA)
        ondas, mascaras = [], []
        for j in range(n_jan):
            a = int(j * janela.step * TAXA)
            pedaco = onda[a:a + n]
            if len(pedaco) < n:                       # mode="pad"
                pedaco = np.pad(pedaco, (0, n - len(pedaco)))
            for f in range(n_falantes):
                m_suja = np.nan_to_num(dados[j, :, f], nan=0.0).astype(np.float32)
                m_limpa = np.nan_to_num(limpa[j, :, f], nan=0.0).astype(np.float32)
                ondas.append(pedaco)
                mascaras.append(m_limpa if m_limpa.sum() > min_quadros else m_suja)

        saidas = []
        for i in range(0, len(ondas), LOTE):
            lote_onda = ondas[i:i + LOTE]
            lote_masc = np.stack(mascaras[i:i + LOTE])
            fb = np.concatenate([fbank_centrado(o, self.mel) for o in lote_onda])
            quadros = self.cod.run(None, {"fbank": fb.astype(np.float32)})[0]
            stats = estatisticas_ponderadas(quadros, lote_masc)
            saidas.append(self.cab.run(None, {"estatisticas": stats})[0])

        vetores = np.vstack(saidas)
        # um falante que não fala nesta janela tem máscara toda zero, e o vetor
        # dele não significa nada. O pyannote marca isso com NaN, e o
        # clustering conta com esse NaN — zerar aqui viraria um falante a mais.
        vazias = np.array([m.sum() == 0 for m in mascaras])
        vetores[vazias] = np.nan

        return rearrange(vetores, "(c s) d -> c s d", c=n_jan)
```

- [ ] **Passo 4: rodar e ver passar**

```bash
uv run --with onnxruntime python -m pytest \
  motores/diarizacao/pipeline/testes/test_embedding.py -v
```

Esperado: PASS.

**Se o padrão de NaN divergir:** é a condição de máscara vazia. O pyannote marca
NaN quando o falante não está ativo na janela; confira se `mascaras[i].sum()`
é exatamente 0 nesses casos, e não um valor pequeno.

- [ ] **Passo 5: commitar**

```bash
git add motores/diarizacao/pipeline/embedding.py \
        motores/diarizacao/pipeline/testes/test_embedding.py
git commit -m "feat(diarizacao-onnx): o vetor de cada (janela, falante)

Cada janela de 10 s tem até 3 falantes, e o vetor de cada um sai do mesmo
áudio com uma máscara por quadro diferente. O teste compara contra o
get_embeddings do pyannote sobre 60 s reais, e confere o padrão de NaN
junto com os valores: o NaN marca falante que não fala na janela, e o
clustering conta com ele — zerar viraria um falante a mais."
```

---

## Tarefa 6: A cola — contagem, clustering e reconstrução

**Arquivos:**
- Criar: `motores/diarizacao/pipeline/diarizacao.py`
- Criar: `tools/conferir_diarizacao_onnx.py`
- Teste: `motores/diarizacao/pipeline/testes/test_diarizacao.py`

**Interfaces:**
- Consome: `Segmentador` (T4), `Extrator` (T5), `vendor.clustering.VBxClustering`,
  `vendor.signal.Binarize`
- Produz: `class Diarizador` com
  `__init__(self, dir_modelo, preferir_gpu=True)` e
  `__call__(self, onda, taxa=16000) -> list[dict]` — cada item
  `{"inicio": float, "fim": float, "falante": str}`, **a mesma forma que o
  `motor.py` já devolve hoje**

- [ ] **Passo 1: escrever o teste que falha — e ele é a régua V1**

```python
# motores/diarizacao/pipeline/testes/test_diarizacao.py
import json, numpy as np, sys, wave
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

RAIZ = Path("/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores"
            "/diarizacao/modelos/community-1")
WAV = ("/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings"
       "/2026-08-25_08-59-22/mix.wav")
#: A saída do torch+CUDA medida em 18/09/2026: 404 trechos, 3 falantes.
#: Gere com: python tools/conferir_diarizacao_onnx.py --gravar-gabarito
GABARITO = Path(__file__).parent / "gabarito_14min.json"


def _grade(trechos, dur, passo=0.01):
    """Quem fala em cada centésimo de segundo — a grade que a V1 compara."""
    g = np.full(int(dur / passo), "", dtype=object)
    for t in trechos:
        g[int(t["inicio"] / passo):int(t["fim"] / passo)] = t["falante"]
    return g


def test_V1_mesma_decisao_de_falante_em_99_por_cento_do_tempo():
    from diarizacao import Diarizador

    with wave.open(WAV, "rb") as w:
        taxa = w.getframerate(); q = w.readframes(w.getnframes())
    onda = np.frombuffer(q, np.int16).astype(np.float32) / 32768.0
    dur = len(onda) / taxa

    esperado = json.loads(GABARITO.read_text())["trechos"]
    obtido = Diarizador(RAIZ)(onda, taxa)

    # 1. mesmo número de falantes
    assert len({t["falante"] for t in obtido}) == len({t["falante"] for t in esperado})

    # 2. mesma atribuição em >=99% do tempo falado, com os rótulos casados
    #    pelo melhor pareamento (SPEAKER_00 do ONNX pode ser o _01 do torch)
    from scipy.optimize import linear_sum_assignment
    ge, go = _grade(esperado, dur), _grade(obtido, dur)
    re_ = sorted({x for x in ge if x}); ro = sorted({x for x in go if x})
    custo = np.zeros((len(re_), len(ro)))
    for i, a in enumerate(re_):
        for j, b in enumerate(ro):
            custo[i, j] = -np.sum((ge == a) & (go == b))
    li, lj = linear_sum_assignment(custo)
    mapa = {ro[j]: re_[i] for i, j in zip(li, lj)}
    go_map = np.array([mapa.get(x, x) for x in go], dtype=object)

    falado = ge != ""
    acordo = np.sum((ge == go_map) & falado) / np.sum(falado)
    assert acordo >= 0.99, f"acordo de apenas {acordo:.4f}"
```

- [ ] **Passo 2: gerar o gabarito e rodar o teste**

```bash
cd /home/andre/projects/meeting-transcription
uv run python tools/conferir_diarizacao_onnx.py --gravar-gabarito
uv run --with onnxruntime-gpu python -m pytest \
  motores/diarizacao/pipeline/testes/test_diarizacao.py -v
```

Esperado no primeiro `pytest`: FAIL com `ModuleNotFoundError: No module named
'diarizacao'`.

O `--gravar-gabarito` roda o pipeline do **torch** e grava
`gabarito_14min.json`. Confira que ele traz `404` trechos e `3` falantes antes
de seguir — se não trouxer, a máquina mudou e o resto do plano compara contra a
coisa errada.

- [ ] **Passo 3: implementar o `Diarizador`**

```python
# motores/diarizacao/pipeline/diarizacao.py
"""O pipeline de diarização sem torch (docs/DIARIZACAO-ONNX.md).

A ordem é a do `SpeakerDiarization.apply` do pyannote, e ela não é arbitrária:

    segmentação → contagem de falantes → embeddings → clustering → reconstrução

A contagem vem ANTES dos embeddings porque é ela que permite sair cedo quando
ninguém fala — e sair cedo evita extrair milhares de vetores de silêncio.
"""
import numpy as np
from pyannote.core import Annotation, Segment, SlidingWindow, SlidingWindowFeature

from segmentacao import Segmentador
from embedding import Extrator
from vendor.clustering import VBxClustering

TAXA = 16000
#: do config.yaml do community-1
VBX_THRESHOLD, VBX_FA, VBX_FB = 0.6, 0.07, 0.8
#: o campo receptivo do modelo de segmentação, medido em 18/09/2026
RF_INICIO, RF_DURACAO, RF_PASSO = 0.0, 0.0619375, 0.016875


class Diarizador:
    def __init__(self, dir_modelo, preferir_gpu: bool = True):
        from pathlib import Path
        d = Path(dir_modelo)
        self.seg = Segmentador(d / "segmentation" / "model.onnx", preferir_gpu)
        self.emb = Extrator(d / "embedding", preferir_gpu)
        self.clustering = VBxClustering(plda=str(d / "plda"))
        self.clustering.instantiate({"threshold": VBX_THRESHOLD,
                                     "Fa": VBX_FA, "Fb": VBX_FB})
        self.quadros = SlidingWindow(start=RF_INICIO, duration=RF_DURACAO,
                                     step=RF_PASSO)

    def __call__(self, onda: np.ndarray, taxa: int = TAXA) -> list[dict]:
        binaria = self.seg(onda, taxa)
        n_jan, n_quadros, n_falantes = binaria.data.shape

        contagem = self._contar(binaria)
        if np.nanmax(contagem) == 0.0:
            return []                         # ninguém falou

        vetores = self.emb(onda, binaria, excluir_sobreposicao=True)

        duros, _, _ = self.clustering(
            embeddings=vetores, segmentations=binaria,
            num_clusters=None, min_clusters=1, max_clusters=20,
            file=None, frames=self.quadros,
        )

        anotacao = self._reconstruir(binaria, duros, contagem, len(onda) / taxa)
        return [{"inicio": s.start, "fim": s.end, "falante": f}
                for s, _, f in anotacao.itertracks(yield_label=True)]

    def _contar(self, binaria) -> np.ndarray:
        """Quantos falam em cada quadro da linha do tempo, somando as janelas."""
        raise NotImplementedError("o corpo está no Passo 3c")

    def _reconstruir(self, binaria, duros, contagem, duracao) -> Annotation:
        """Das janelas por falante local para a linha do tempo por pessoa."""
        raise NotImplementedError("o corpo está no Passo 3c")
```

- [ ] **Passo 3b: vendorizar a agregação — e ela é livre de torch**

`speaker_count` chama `Inference.trim` e `Inference.aggregate`. O
`core/inference.py` tem 19 menções a torch, **mas esses dois métodos estáticos
têm zero** — o torch dele está todo na maquinaria de inferência, que o
`Segmentador` da Tarefa 4 substitui. É o mesmo padrão do `permutate`: a
contagem por arquivo diz o custo máximo, não o real.

```bash
cd /home/andre/projects/meeting-transcription
P="/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores/python/Lib/site-packages/pyannote/audio"
cp "$P/pipelines/utils/diarization.py" motores/diarizacao/pipeline/vendor/diarizacao_utils.py
```

Confira que os dois continuam limpos antes de seguir:

```bash
grep -c torch motores/diarizacao/pipeline/vendor/diarizacao_utils.py   # 0
```

Crie `motores/diarizacao/pipeline/vendor/agregacao.py` com **os corpos de
`Inference.trim` e `Inference.aggregate` copiados do `core/inference.py`**, como
funções de módulo (tire o `@staticmethod` e o `Inference.` dos nomes). Depois
corrija os imports de `diarizacao_utils.py` para apontar para elas:

```python
# no topo de vendor/diarizacao_utils.py, no lugar do import de Inference
from .agregacao import trim, aggregate
```

e troque `Inference.trim(` por `trim(` e `Inference.aggregate(` por `aggregate(`.

- [ ] **Passo 3c: implementar `_contar` e `_reconstruir`**

```python
    def _contar(self, binaria) -> np.ndarray:
        """Quantos falam em cada quadro da linha do tempo, somando as janelas.

        `warm_up=(0.0, 0.0)` não é o padrão da função (que é 0,1): é o que o
        `apply` do pyannote passa quando o modelo é powerset. Usar o padrão
        cortaria 1 s de cada ponta de cada janela de 10 s.
        """
        from vendor.diarizacao_utils import SpeakerDiarizationMixin
        contagem = SpeakerDiarizationMixin.speaker_count(
            binaria, self.quadros, warm_up=(0.0, 0.0),
        )
        return contagem

    def _reconstruir(self, binaria, duros, contagem, duracao) -> Annotation:
        """Das janelas por falante local para a linha do tempo por pessoa.

        Copiado do `SpeakerDiarization.reconstruct` (0 menções a torch). O
        `-2` é o rótulo que o clustering dá ao que ele não soube atribuir, e
        pular é o certo: virar cluster próprio criaria um falante fantasma.
        """
        from vendor.diarizacao_utils import SpeakerDiarizationMixin

        n_jan, n_quadros, _ = binaria.data.shape
        n_clusters = np.max(duros) + 1
        agrupada = np.nan * np.zeros((n_jan, n_quadros, n_clusters))

        for c, (cluster, (janela, seg)) in enumerate(zip(duros, binaria)):
            for k in np.unique(cluster):
                if k == -2:
                    continue
                agrupada[c, :, k] = np.max(seg[:, cluster == k], axis=1)

        agrupada = SlidingWindowFeature(agrupada, binaria.sliding_window)
        discreta = SpeakerDiarizationMixin.to_diarization(agrupada, contagem)
        # min_duration_off=0.0 vem do config.yaml do community-1
        return SpeakerDiarizationMixin.to_annotation(
            discreta, min_duration_on=0.0, min_duration_off=0.0,
        )
```

**Régua deste passo:** o teste do Passo 1 passa. Ele é a `V1`, e é ele que diz
se a reconstrução está certa — nenhum teste menor pega um erro de meio quadro
aqui.

- [ ] **Passo 4: rodar a V1**

```bash
uv run --with onnxruntime-gpu python -m pytest \
  motores/diarizacao/pipeline/testes/test_diarizacao.py -v
```

Esperado: PASS, com acordo ≥ 0,99.

**Se o acordo ficar entre 0,90 e 0,99:** o suspeito é a reconstrução, não os
modelos — as Tarefas 4 e 5 já provaram que eles batem. Compare
`contagem` contra o `speaker_count` do pyannote no mesmo áudio antes de olhar
qualquer outra coisa.

**Se ficar abaixo de 0,50:** é alinhamento de janela. Volte ao teste da Tarefa 4
e confira `sliding_window.start` e `.step`.

- [ ] **Passo 5: commitar**

```bash
git add motores/diarizacao/pipeline/diarizacao.py tools/conferir_diarizacao_onnx.py \
        motores/diarizacao/pipeline/testes/
git commit -m "feat(diarizacao-onnx): o pipeline inteiro, e a régua V1 passa

O teste é a V1 da spec: mesmo número de falantes e mesma decisão de
falante em >=99% do tempo falado, contra a saída torch+CUDA de 18/09/2026
(404 trechos, 3 falantes) na gravação de 14,6 min.

Os rótulos são casados por pareamento húngaro antes de comparar: o
SPEAKER_00 do ONNX não é necessariamente o SPEAKER_00 do torch, e comparar
rótulo com rótulo reprovaria um porte correto."
```

---

## Tarefa 7: A chave, e as duas implementações convivendo

**Arquivos:**
- Modificar: `motores/diarizacao/motor.py`
- Teste: `motores/diarizacao/pipeline/testes/test_motor.py`

**Interfaces:**
- Consome: `Diarizador` (T6)
- Produz: o `motor.py` aceita `motor_de_diarizacao` em `{"torch", "onnx"}`,
  com `"torch"` como padrão. **O protocolo do sidecar não muda.**

- [ ] **Passo 1: escrever o teste que falha**

```python
# motores/diarizacao/pipeline/testes/test_motor.py
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

def test_valor_desconhecido_cai_no_padrao_em_silencio():
    """Um app.json com lixo na chave não pode recusar a transcrição.

    Mesma razão pela qual o MotorAceito sobreviveu à saída do MOSS com um
    valor só: recusar a diarização por causa de uma chave é pior que
    ignorá-la (docs/CONVERGENCIA.md, "O MOSS é o primeiro a sair").
    """
    from motor import escolher_motor
    assert escolher_motor(None) == "torch"
    assert escolher_motor("torch") == "torch"
    assert escolher_motor("onnx") == "onnx"
    assert escolher_motor("moss") == "torch"
    assert escolher_motor("") == "torch"
```

- [ ] **Passo 2: rodar e ver falhar**

```bash
uv run python -m pytest motores/diarizacao/pipeline/testes/test_motor.py -v
```

Esperado: FAIL com `ImportError: cannot import name 'escolher_motor'`.

- [ ] **Passo 3: implementar**

Acrescente ao `motor.py`, perto do topo:

```python
#: Os motores de diarização aceitos. O `torch` é o pyannote como sempre foi; o
#: `onnx` é o porte de 18/09/2026 (docs/DIARIZACAO-ONNX.md).
MOTORES = ("torch", "onnx")
PADRAO_DE_MOTOR = "torch"


def escolher_motor(valor: str | None) -> str:
    """O motor pedido, ou o padrão — **nunca um erro**.

    Um `app.json` com valor desconhecido cai no padrão em silêncio. Recusar a
    diarização por causa de uma chave é pior que ignorá-la: a pessoa perde a
    separação de falantes de uma reunião que já aconteceu. É a mesma decisão
    do `MotorAceito` quando o MOSS saiu.
    """
    return valor if valor in MOTORES else PADRAO_DE_MOTOR
```

E no `Pipeline.diarizar`, escolha a implementação pela chave — **sem tocar no
formato de retorno**, que é `list[dict]` com `inicio`, `fim` e `falante`.

- [ ] **Passo 4: rodar e ver passar**

```bash
uv run python -m pytest motores/diarizacao/pipeline/testes/ -v
```

Esperado: todos PASS.

- [ ] **Passo 5: rodar a suíte do C# — a régua V4**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet test app-net/Tests/MeetingApp.Tests.csproj
```

Esperado: **628 testes passam, sem alteração em teste nenhum.** Se um teste
precisar mudar, pare: o corte pegou osso, e isso é assunto para o dono do
produto, não conserto.

- [ ] **Passo 6: commitar**

```bash
git add motores/diarizacao/motor.py motores/diarizacao/pipeline/testes/test_motor.py
git commit -m "feat(diarizacao-onnx): a chave motor_de_diarizacao, com torch de padrão

As duas implementações convivem até as réguas V2 e V3 fecharem. Valor
desconhecido cai no padrão em silêncio, pela mesma razão que o MotorAceito
sobreviveu à saída do MOSS: recusar a diarização por causa de uma chave faz
a pessoa perder a separação de falantes de uma reunião que já aconteceu.

O protocolo do sidecar não mudou."
```

---

## Tarefa 8: As réguas V2 e V3, sobre o acervo

**Arquivos:**
- Modificar: `tools/conferir_diarizacao_onnx.py`

**Interfaces:**
- Consome: `Diarizador` (T6)
- Produz: um relatório com as quatro gravações do acervo e as decisões de voz

- [ ] **Passo 1: rodar a V2 — as quatro gravações com Gemini**

```bash
for g in 2026-08-20_15-59-20 2026-08-21_11-00-33 \
         2026-08-25_08-59-22 2026-08-27_15-28-37; do
  uv run --with onnxruntime-gpu python tools/conferir_diarizacao_onnx.py \
    --gravacao "$g" --motor onnx --contra-gemini
done
```

Esperado: nenhuma das quatro piora contra o resultado de hoje. As gravações são
as que têm `gemini.md` na pasta — 32,1 · 7,7 · 14,6 · 48,5 min.

- [ ] **Passo 2: rodar a V3 — as decisões de voz**

```bash
uv run --with onnxruntime-gpu python tools/medir_vetor_onnx.py \
  --modelo community-1 --json /tmp/v3.json
```

Esperado: **0 decisões diferentes** nos limiares 0,55 · 0,60 · 0,70. É o que
garante que o banco de vozes não precisa ser re-extraído.

**Se houver decisão diferente:** pare e leve ao dono do produto. O `VozExtraida`
carimba o modelo e a casa já fez uma migração de banco de vozes em 20/08/2026,
mas o custo sobe de uma tarde para uma migração — e é decisão dele, não do
plano.

- [ ] **Passo 3: registrar os números na spec**

Acrescente a `docs/DIARIZACAO-ONNX.md` uma seção `§5.1 — as réguas, medidas`
com os quatro resultados e a data. O documento hoje diz o que se pretende
medir; depois desta tarefa ele diz o que se mediu.

- [ ] **Passo 4: commitar**

```bash
git add docs/DIARIZACAO-ONNX.md tools/conferir_diarizacao_onnx.py
git commit -m "docs(diarizacao-onnx): as quatro réguas, medidas

V1 a V4 fechadas sobre o acervo. O documento passa a dizer o que se mediu,
e não o que se pretendia medir."
```

---

## O que este plano NÃO faz, de propósito

**Não apaga o torch.** A limpeza — tirar `torch`, `torchaudio`,
`pytorch_lightning`, `torchmetrics` e os wheels que só eles usam do
`tools/empacotar_motores.sh`, e virar a chave para `"onnx"` — é trabalho
separado, e só começa depois de as quatro réguas fecharem **e** de o dono do
produto ver os números. É a regra do `CONVERGENCIA.md`: *nada é apagado antes de
o substituto ter ganhado na régua, sobre o acervo*.

**Não toca no `large-v3`.** A CUDA fica: o CTranslate2 só tem CUDA e CPU. Ver
`docs/DIARIZACAO-ONNX.md` §6.

**Não mede o `onnxruntime-gpu` no Python embarcado.** Ele não tem `pip`, e a
instalação do wheel na instalação oficial é parte da limpeza, não deste plano.
Até lá tudo roda pelo venv do WSL, que é onde as ferramentas de medição vivem.
