# Código vendorizado

Estes arquivos **não são deste projeto**. São do `pyannote.audio`, copiados em
18/09/2026 da versão **4.0.7**, sob a licença **MIT**.

- autoria: Hervé Bredin e colaboradores (CNRS, pyannoteAI)
- origem: https://github.com/pyannote/pyannote-audio
- licença: MIT — o texto viaja no cabeçalho de cada arquivo

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

**A matemática não foi tocada.** O VBx, o PLDA e a binarização estão como
estavam — é por isso que a régua `V1` pode exigir a mesma decisão de falante.
