"""O vetor de voz sem torch: fbank em numpy, ResNet em ONNX, pooling em numpy."""
import math
from pathlib import Path

import numpy as np
from einops import rearrange

from fbank import banco_mel, fbank_centrado
from fbank import JANELA as JANELA_FBANK
from sessao import abrir

TAXA = 16000
#: a largura do vetor que `cabeca.onnx` devolve por (janela, falante) — não
#: usada em nenhuma conta aqui (a forma real vem do ONNX), é a documentação
#: do contrato de saída que a Tarefa 6 (e quem mais consumir `Extrator`)
#: pode citar sem abrir o `.onnx`.
DIMENSAO = 256
LOTE = 32

#: o mínimo de amostras de onda que o codificador aceita sem erro — **não**
#: é `duration` do modelo (5 s), como uma primeira leitura sugere. É o
#: mesmo `min_num_samples` que o `PyannoteAudioPretrainedSpeakerEmbedding`
#: do pyannote acha por busca binária (medido: 400 amostras, 25 ms), e o
#: motivo é o fbank: com menos que `JANELA_FBANK` amostras (400, a janela
#: de 25 ms do Kaldi em `fbank.py`), `fbank()` produz zero quadros e tudo
#: depois quebra. Usar 5 s aqui (a duração da JANELA do modelo, não o
#: mínimo real) fazia o limiar de "máscara limpa suficiente" ficar ~150x
#: maior que o do pyannote, e trocava a máscara escolhida (limpa vs. suja)
#: em boa parte das janelas — medido: 37 de 153 pares (janela, falante)
#: com diferença > 1e-2 antes desta correção, zero depois.
#:
#: **Se o modelo de embedding for trocado, remeça esta constante.** Ela é o
#: `min_num_samples` do modelo de embedding — hoje coincide com
#: `JANELA_FBANK` porque o gargalo é o fbank, não o modelo, mas isso é uma
#: coincidência deste modelo, não uma garantia geral. Para medir de novo:
#: instancie o pipeline do pyannote com o modelo novo e leia
#: `pipe._embedding.min_num_samples` (a busca binária está em
#: `PyannoteAudioPretrainedSpeakerEmbedding.min_num_samples`, em
#: `pyannote/audio/pipelines/speaker_verification.py`).
MIN_AMOSTRAS_EMBEDDING = JANELA_FBANK


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
        if not mel.exists():
            # NUNCA cair para banco_mel() aqui: ela importa torchaudio e
            # torch (fbank.py), e este é o caminho de produção que existe
            # para não carregar torch. `mel.npy` é gerado uma vez por
            # tools/exportar_diarizacao_onnx.py e guardado ao lado dos
            # outros três artefatos onnx — banco_mel() é como o exportador
            # constrói essa tabela, não um substituto em tempo de execução.
            raise RuntimeError(
                f"{mel} não existe. Rode tools/exportar_diarizacao_onnx.py "
                "para gerar os artefatos onnx completos."
            )
        self.mel = np.load(mel)

    def __call__(self, onda, segmentacao_binaria, excluir_sobreposicao=True):
        dados = segmentacao_binaria.data              # (n_jan, n_quadros, 3)
        n_jan, n_quadros, n_falantes = dados.shape
        janela = segmentacao_binaria.sliding_window

        if excluir_sobreposicao:
            # o mínimo de quadros que o codificador exige, convertido do
            # mínimo de amostras (MIN_AMOSTRAS_EMBEDDING) para a grade de
            # quadros da segmentação
            n_amostras = janela.duration * TAXA
            min_quadros = math.ceil(n_quadros * MIN_AMOSTRAS_EMBEDDING / n_amostras)
            limpos = 1.0 * (np.sum(dados, axis=2, keepdims=True) < 2)
            limpa = dados * limpos
        else:
            min_quadros = -1
            limpa = dados

        n = int(janela.duration * TAXA)
        ondas, mascaras = [], []
        for j in range(n_jan):
            # `a = j * passo` assume que TODAS as janelas seguem o mesmo
            # passo regular, inclusive a última — reconstruímos o áudio a
            # partir só do índice `j`, sem olhar onde a janela realmente
            # começa. Isso só é verdade porque a Tarefa 4 (`e4bb503`, "a
            # janela final segue o passo, não o fim do áudio") corrigiu o
            # `Segmentador` para nunca alinhar a última janela pelo fim do
            # áudio. Se o `Segmentador` voltar a fazer isso, ou se algum
            # chamador passar uma `SlidingWindowFeature` montada à mão com
            # passo irregular, esta linha fatia o trecho de áudio errado
            # para a última janela — o mesmo tipo de contaminação silenciosa
            # que a máscara (acima) e o NaN (abaixo) existem para evitar,
            # só que na fatia de áudio em vez da máscara.
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
        # Divergência do brief: NÃO forçamos NaN aqui. Medido contra
        # `SpeakerDiarization.get_embeddings` (60 s reais): quando um
        # falante não fala na janela, a máscara é toda zero, mas o `_pool`
        # do pyannote (o mesmo formato que `estatisticas_ponderadas`
        # implementa, com os dois `1e-8`) não produz NaN nesse caso — produz
        # média 0 e desvio 0 de forma finita, porque o denominador nunca
        # zera. O `get_embeddings` do pyannote em si também não insere NaN
        # em lugar nenhum (conferido lendo o código-fonte instalado). Um
        # `vazias → NaN` manual aqui inventaria NaN onde o gabarito não tem
        # nenhum: no teste de 60 s, `esperado` tem zero NaN no total.

        return rearrange(vetores, "(c s) d -> c s d", c=n_jan)
