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
