# Código vendorizado

Estes arquivos **não são deste projeto**. São do `pyannote.audio`, copiados em
18/09/2026 da versão **4.0.7**. Cinco são **MIT**; `vbx.py` é **Apache-2.0** —
são licenças diferentes, com obrigações diferentes, e cada arquivo carrega a
sua própria no cabeçalho.

## Os cinco MIT

- arquivos: `diarizacao_utils.py`, `clustering.py`, `agregacao.py`,
  `signal.py`, `plda.py`
- autoria: Hervé Bredin e colaboradores (CNRS, pyannoteAI)
- origem: https://github.com/pyannote/pyannote-audio
- licença: MIT — o texto viaja no cabeçalho de cada arquivo

## `vbx.py` — Apache-2.0

- linhagem: BUT Speech@FIT / VBx (Lukáš Burget, Pavel Pálka), com uma
  atualização de assinatura de Hervé Bredin ao entrar no pyannote.audio —
  ver o "Revision History" no cabeçalho do arquivo
- origem imediata (copiado deste caminho): `pyannote/audio/utils/vbx.py`, na
  mesma versão 4.0.7
- origem original: https://github.com/BUTSpeechFIT/VBx
- licença: Apache License 2.0 — o texto completo viaja no cabeçalho do
  arquivo; redistribuir é permitido, mas as obrigações (aviso de mudanças,
  licença de patente) são diferentes das do MIT dos outros cinco

## Por que foram copiados

O `pyannote/audio/__init__.py` importa `core.inference`, `core.io` e
`core.model`, e os três importam **torch**. Então `from pyannote.audio.x import y`
arrasta 4,8 GB de framework para executar 32 MB de modelo — que é exatamente o
que este porte existe para evitar (docs/DIARIZACAO-ONNX.md §2).

Copiar é a única forma de usar estes arquivos sem o pacote que os contém.

## O que foi alterado

| arquivo | alteração |
|---|---|
| `vbx.py` | **nenhuma** |
| `signal.py` | **nenhuma** |
| `plda.py` | só imports: `vbx_setup` vem do arquivo ao lado, e o ramo do HuggingFace virou erro — aqui o checkpoint é sempre pasta em disco |
| `clustering.py` | removida a `OracleClustering`, que era a única usuária de `permutate` e `oracle_segmentation` (ambos com torch); imports de `PLDA` e `cluster_vbx` apontam para os arquivos ao lado; `AudioFile` era anotação de tipo vinda de `core/io.py` |
| `diarizacao_utils.py` | só imports: `trim` e `aggregate` vêm do `agregacao.py` ao lado (e perderam o prefixo `Inference.`), `Binarize` vem do `signal.py` ao lado, e o `DiarizationErrorRate` desceu para dentro do `optimal_mapping` |
| `agregacao.py` | **é um recorte**: só os `@staticmethod` `trim` e `aggregate` do `core/inference.py`, virados função de módulo. O resto do arquivo é a maquinaria de inferência em torch, que o `segmentacao.py` substitui |

**A matemática não foi tocada.** O VBx, o PLDA e a binarização estão como
estavam — é por isso que a régua `V1` pode exigir a mesma decisão de falante.
