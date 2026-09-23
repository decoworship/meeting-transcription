"""O pipeline de diarização sem torch (docs/DIARIZACAO-ONNX.md).

A ordem é a do `SpeakerDiarization.apply` do pyannote, e ela não é arbitrária:

    segmentação → contagem de falantes → embeddings → clustering → reconstrução

A contagem vem ANTES dos embeddings porque é ela que permite sair cedo quando
ninguém fala — e sair cedo evita extrair milhares de vetores de silêncio.

**Esta é a cola, e é onde o porte pode errar em silêncio.** As Tarefas 4 e 5
provaram que a segmentação e o embedding batem número a número com o torch; o
que sobra é a montagem — e meio quadro de deslocamento aqui não levanta exceção
nenhuma: vira fala atribuída à pessoa errada. Por isso a régua deste arquivo é
a `V1` (`testes/test_diarizacao.py`), que compara a linha do tempo inteira de
uma gravação de 14,6 min contra a saída do torch.
"""
from pathlib import Path

import numpy as np
from pyannote.core import Annotation, SlidingWindow, SlidingWindowFeature

from segmentacao import Segmentador
from embedding import Extrator
from vendor.clustering import VBxClustering
from vendor.diarizacao_utils import SpeakerDiarizationMixin
from vendor.plda import PLDA

TAXA = 16000
#: do config.yaml do community-1
VBX_THRESHOLD, VBX_FA, VBX_FB = 0.6, 0.07, 0.8
#: também do config.yaml: `segmentation.min_duration_off`
MIN_DURACAO_OFF = 0.0
#: o campo receptivo do modelo de segmentação, medido em 18/09/2026.
#: É a grade de quadros em que a contagem e a reconstrução vivem — errar o
#: passo desloca a linha do tempo inteira, sem erro nenhum.
RF_INICIO, RF_DURACAO, RF_PASSO = 0.0, 0.0619375, 0.016875


class Diarizador:
    """Quem falou quando, em numpy e onnxruntime.

    Devolve `[{"inicio", "fim", "falante"}, ...]` — a mesma forma que o
    `motor.py` já devolve hoje, para a Tarefa 7 poder trocar um pelo outro
    sem mexer em quem consome.
    """

    def __init__(self, dir_modelo, preferir_gpu: bool = True):
        d = Path(dir_modelo)
        self.seg = Segmentador(d / "segmentation" / "model.onnx", preferir_gpu)
        self.emb = Extrator(d / "embedding", preferir_gpu)
        # Divergência do brief: ele passava `plda=str(d / "plda")`, uma string.
        # O `VBxClustering.__call__` chama `self.plda(train_embeddings)`, e uma
        # string não é chamável — o porte quebraria no primeiro áudio real. O
        # pyannote monta o pipeline com `Klustering.value(self._plda, ...)`,
        # onde `_plda` é um `PLDA` já carregado (`get_plda` em
        # `pipelines/utils/getter.py`). É isso que se faz aqui.
        self.clustering = VBxClustering(plda=PLDA.from_pretrained(d / "plda"))
        self.clustering.instantiate({"threshold": VBX_THRESHOLD,
                                     "Fa": VBX_FA, "Fb": VBX_FB})
        self.quadros = SlidingWindow(start=RF_INICIO, duration=RF_DURACAO,
                                     step=RF_PASSO)

    def __call__(self, onda: np.ndarray, taxa: int = TAXA) -> list[dict]:
        binaria = self.seg(onda, taxa)

        contagem = self._contar(binaria)
        if np.nanmax(contagem.data) == 0.0:
            return []                         # ninguém falou

        vetores = self.emb(onda, binaria, excluir_sobreposicao=True)

        # `min_clusters=1, max_clusters=np.inf` é exatamente o que o `apply` do
        # pyannote passa quando ninguém informa quantas pessoas há — é o que o
        # `set_num_speakers` devolve sem argumento nenhum.
        #
        # Divergência do brief, que dizia `max_clusters=20`: o
        # `VBxClustering.__call__` compara `auto_num_clusters > max_clusters`
        # **sem** passar por `set_num_clusters`, então o valor chega cru. Com
        # `None` isso é um TypeError, e com 20 é um teto que o pyannote não
        # tem — ele mudaria a saída numa reunião com mais de 20 pessoas, para
        # pior e calado.
        duros, _, _ = self.clustering(
            embeddings=vetores, segmentations=binaria,
            num_clusters=None, min_clusters=1, max_clusters=np.inf,
            file=None, frames=self.quadros,
        )

        # Divergência do brief: ele não tinha este bloco, e o `apply` do
        # pyannote tem. Um falante local que não abre a boca na janela ainda
        # ganha um cluster do `constrained_argmax`; mandá-lo para o cluster de
        # descarte (-2) é o que impede a janela de "votar" por alguém que ela
        # não ouviu.
        inativos = np.sum(binaria.data, axis=1) == 0     # (n_jan, n_falantes)
        duros[inativos] = -2

        # O mesmo teto que o `apply` aplica à contagem depois do clustering.
        # Sem `max_speakers` informado ele é `np.inf`, ou seja, não corta nada
        # — o que sobra é o `astype`, e ele é o que o `to_diarization` lê.
        contagem.data = contagem.data.astype(np.int8)

        anotacao = self._reconstruir(binaria, duros, contagem)

        # o `apply` renomeia os rótulos inteiros do clustering para
        # SPEAKER_00, SPEAKER_01, … na ordem de `labels()`
        anotacao = anotacao.rename_labels(mapping={
            r: f"SPEAKER_{i:02d}" for i, r in enumerate(anotacao.labels())
        })
        return [{"inicio": float(s.start), "fim": float(s.end), "falante": str(f)}
                for s, _, f in anotacao.itertracks(yield_label=True)]

    def _contar(self, binaria) -> SlidingWindowFeature:
        """Quantos falam em cada quadro da linha do tempo, somando as janelas.

        `warm_up=(0.0, 0.0)` não é o padrão da função (que é 0,1): é o que o
        `apply` do pyannote passa quando o modelo é powerset. Usar o padrão
        cortaria 1 s de cada ponta de cada janela de 10 s.
        """
        return SpeakerDiarizationMixin.speaker_count(
            binaria, self.quadros, warm_up=(0.0, 0.0),
        )

    def _reconstruir(self, binaria, duros, contagem) -> Annotation:
        """Das janelas por falante local para a linha do tempo por pessoa.

        Copiado do `SpeakerDiarization.reconstruct` (0 menções a torch). O
        `-2` é o rótulo que o clustering dá ao que ele não soube atribuir, e
        pular é o certo: virar cluster próprio criaria um falante fantasma.
        """
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
        return SpeakerDiarizationMixin.to_annotation(
            discreta, min_duration_on=0.0, min_duration_off=MIN_DURACAO_OFF,
        )
