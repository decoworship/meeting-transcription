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

#: forma esperada da saída do ONNX por janela: 589 quadros, 7 classes
#: powerset (combinações de até 2 entre 3 falantes locais). Conferida antes
#: do argmax — um `.onnx` velho ou mal exportado com outra contagem de
#: classes indexaria `MAPA_POWERSET` fora da linha certa (ou estouraria)
#: sem levantar exceção nenhuma até aqui.
QUADROS_SEG = 589
CLASSES_POWERSET = 7
FALANTES_LOCAIS = 3

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
        #
        # O início dela é o próximo passo natural (`inicios[-1] + passo`),
        # **não** `len(onda) - n`. O `Inference.slide` do pyannote não
        # desliza a última janela para terminar exatamente no fim do áudio —
        # ele mantém o passo regular e completa com zeros o que sobra depois
        # da última janela cheia. Alinhar pelo fim, em vez disso, desalinha
        # os quadros da janela final com o gabarito (medido: só a última
        # janela diverge, com diferença até 1.0 nos rótulos).
        if len(onda) < n:
            onda = np.pad(onda, (0, n - len(onda)))
        inicios = list(range(0, max(1, len(onda) - n + 1), passo))
        if inicios[-1] + n < len(onda):
            inicios.append(inicios[-1] + passo)

        # a última janela pode ir além do fim do áudio agora que o início
        # dela segue o passo regular em vez do fim; o resto é zero.
        fim = inicios[-1] + n
        if fim > len(onda):
            onda = np.pad(onda, (0, fim - len(onda)))

        janelas = np.stack([onda[i:i + n] for i in inicios])[:, None, :]

        saidas = []
        for i in range(0, len(janelas), LOTE):
            saidas.append(self.sessao.run(
                None, {"audio": janelas[i:i + LOTE].astype(np.float32)})[0])
        powerset = np.concatenate(saidas)      # (n_janelas, 589, 7)

        if powerset.shape[1:] != (QUADROS_SEG, CLASSES_POWERSET):
            raise ValueError(
                f"saída do ONNX na forma errada: esperado (*, {QUADROS_SEG}, "
                f"{CLASSES_POWERSET}), veio {powerset.shape} — modelo trocado "
                f"ou exportado com outra contagem de classes/quadros"
            )
        assert MAPA_POWERSET.shape == (CLASSES_POWERSET, FALANTES_LOCAIS)

        # argmax sobre as 7 classes, depois a matriz: é o que o
        # `Powerset.to_multilabel` faz quando o modelo já é powerset.
        vencedora = powerset.argmax(axis=-1)                   # (n_jan, 589)
        multi = MAPA_POWERSET[vencedora]                       # (n_jan, 589, 3)

        return SlidingWindowFeature(
            multi.astype(np.float32),
            SlidingWindow(start=0.0, duration=JANELA, step=PASSO),
        )
