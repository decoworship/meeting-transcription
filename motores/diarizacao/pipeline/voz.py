"""O vetor que identifica uma voz, sem torch: fbank em numpy, ResNet em ONNX.

**Este não é o embedding da diarização.** São dois modelos diferentes no mesmo
motor: o da diarização separa falantes dentro de uma reunião, e o daqui —
``wespeaker-voxceleb-resnet34-LM`` — é a impressão digital que o núcleo guarda
para reconhecer a pessoa na *próxima* reunião (docs/VOZES.md).

**E, ao contrário dele, aqui não há máscara.** O núcleo já escolheu os trechos
limpos e os manda concatenados; o pooling estatístico é `weights=None`, fica
dentro do grafo, e o modelo sai numa peça só (``voz.onnx``). Foi o que o S1
mediu em 10/09/2026 e o que ``testes/test_voz.py`` prende contra o
``Inference(modelo, window="whole")`` que a produção usava.

Ver docs/DIARIZACAO-ONNX.md.
"""
from pathlib import Path

import numpy as np

from fbank import fbank_centrado
from fbank import JANELA as JANELA_FBANK
from sessao import abrir

#: a largura do vetor guardado em cada amostra de voz. Não entra em conta
#: nenhuma aqui — a forma real vem do ONNX —, é o contrato para quem lê.
DIMENSAO = 256


class ExtratorDeVoz:
    """O modelo de voz em ONNX, carregado uma vez e mantido quente."""

    def __init__(self, pasta, preferir_gpu: bool = True):
        d = Path(pasta)
        mel = d / "mel.npy"
        if not (d / "voz.onnx").is_file() or not mel.is_file():
            raise RuntimeError(
                f"o modelo de voz onnx não está em {d} (faltam voz.onnx ou "
                "mel.npy). Rode tools/exportar_diarizacao_onnx.py."
            )
        self.sessao, self.provedor = abrir(d / "voz.onnx", preferir_gpu)
        # NUNCA cair para banco_mel() aqui: ela importa torch e torchaudio, e
        # este é o caminho que existe justamente para não os carregar.
        self.mel = np.load(mel)

    def __call__(self, onda: np.ndarray) -> np.ndarray:
        """O vetor de 256 floats da onda inteira (já concatenada por quem chama)."""
        if len(onda) < JANELA_FBANK:
            # Abaixo da janela de 25 ms do Kaldi o fbank produz zero quadros e
            # a sessão quebraria com uma mensagem sobre formas de tensor, longe
            # da causa. O núcleo só manda segundos de fala, então isto é uma
            # rede de proteção — mas com o nome certo.
            raise RuntimeError(
                f"áudio curto demais para extrair a voz: {len(onda)} amostras, "
                f"mínimo {JANELA_FBANK}"
            )
        f = fbank_centrado(np.asarray(onda, dtype=np.float32), self.mel)
        vetor = self.sessao.run(None, {"fbank": f.astype(np.float32)})[0][0]
        if vetor.shape[-1] != DIMENSAO:
            # A forma real vem do ONNX, não daqui — mas se um export futuro
            # trocar a Cabeca (ex.: two_emb_layer, ou a Cabeca errada do
            # exportar_embedding por engano), o vetor sai com outra largura e
            # fica incomparável com o banco sem erro nenhum: o cosseno entre
            # vetores de tamanhos diferentes nem numpy calcula direito. É
            # melhor estourar aqui do que virar um "ninguém reconhecido"
            # silencioso no Vozes.Reconhecer.
            raise RuntimeError(
                f"a sessão devolveu vetor de {vetor.shape[-1]}, esperado "
                f"{DIMENSAO} (DIMENSAO) — a exportação pode ter mudado a "
                "Cabeca. Ver tools/exportar_diarizacao_onnx.py:exportar_voz."
            )
        return vetor
