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
