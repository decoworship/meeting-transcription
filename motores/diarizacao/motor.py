"""Motor de diarização como sidecar: pyannote atrás do protocolo por linha.

Implementa o contrato de ``docs/SIDECAR.md``. Lê requisições JSON do stdin,
responde progresso e resultado no canal do protocolo, e loga no stderr.

Fica quente entre requisições — é a razão de o processo ser separado: carregar
o pipeline custa mais que diarizar uma reunião curta.

Uso (é assim que o cliente C# o inicia)::

    python motor.py
"""

from __future__ import annotations

import json
import os
import sys

# ANTES de qualquer import pesado. torch, pyannote e transformers escrevem no
# stdout sem pedir licença — barra de progresso de download, avisos de versão,
# mensagens de device — e uma linha dessas no meio do fluxo corrompe o
# protocolo, com um sintoma que não aponta para a causa. O descritor 1 vira
# nosso canal privado; o stdout do processo passa a ser o stderr.
_protocolo = os.fdopen(os.dup(1), "w", encoding="utf-8", newline="\n")
os.dup2(2, 1)

VERSAO = "3"

# O mesmo modelo que o app Python usa. Trocar mudaria o espaço vetorial e
# invalidaria toda voz já aprendida — os vetores de modelos diferentes não são
# comparáveis, e a comparação não falha: ela só passa a errar.
MODELO_DE_VOZ = "pyannote/wespeaker-voxceleb-resnet34-LM"
PIPELINE_DE_DIARIZACAO = "pyannote/speaker-diarization-community-1"

# Os pesos ao lado deste arquivo, montados por
# tools/empacotar_modelos_de_diarizacao.sh. Ver docs/FASE4.md §4.
#
# São 57 MB, CC-BY-4.0, redistribuídos com atribuição (ATRIBUICAO.md fica junto
# deles). Estarem aqui é o que permite o binário do app não carregar um token do
# HuggingFace — e, de quebra, é o que faz a primeira diarização de uma instalação
# nova não depender de rede nem de portão.
#
# Os nomes das pastas casam com os do empacotador. Mudar um sem o outro faz o
# motor cair silenciosamente no caminho do HuggingFace, que é justamente o que
# não se quer: ele funcionaria nesta máquina (que tem token e cache) e falharia
# na de quem instalou.
_LOCAIS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "modelos")


def _pipeline_local() -> str | None:
    """A pasta do pipeline embarcado, ou ``None`` quando ele não veio junto."""
    pasta = os.path.join(_LOCAIS, "community-1")
    return pasta if os.path.isfile(os.path.join(pasta, "config.yaml")) else None


def _voz_local() -> str | None:
    """A pasta do modelo de voz embarcado, ou ``None``."""
    pasta = os.path.join(_LOCAIS, "wespeaker-voxceleb-resnet34-LM")
    return pasta if os.path.isfile(os.path.join(pasta, "pytorch_model.bin")) else None


def _enviar(**campos) -> None:
    _protocolo.write(json.dumps(campos, ensure_ascii=False) + "\n")
    _protocolo.flush()


def _log(texto: str) -> None:
    print(f"[diarizacao] {texto}", file=sys.stderr, flush=True)


class Pipeline:
    """O pyannote, carregado sob demanda e mantido quente."""

    def __init__(self) -> None:
        self._pipeline = None
        self._voz = None
        self.dispositivo = "?"

    def carregar(self, id_req: int) -> None:
        if self._pipeline is not None:
            return

        _enviar(id=id_req, tipo="progresso", pct=0.0, texto="carregando o modelo")
        from pyannote.audio import Pipeline as PyannotePipeline
        import torch

        # community-1: 6,7 pontos de DER melhor que o 3.1 na medição da Fase 0.
        #
        # De onde ele vem, nesta ordem: a pasta ao lado (o app instalado), e só
        # então o HuggingFace (a máquina de quem desenvolve, que pode não ter
        # rodado o empacotador). Os pesos são os mesmos nos dois casos — o que
        # muda é precisar ou não de token e de rede.
        local = _pipeline_local()
        if local:
            _log(f"pipeline local: {local}")
            self._pipeline = PyannotePipeline.from_pretrained(local)
        else:
            token = os.environ.get("HF_TOKEN")
            if not token:
                raise RuntimeError(
                    f"o pipeline de diarização não está em {_LOCAIS} e não há "
                    "HF_TOKEN no ambiente para baixá-lo. Rode "
                    "tools/empacotar_modelos_de_diarizacao.sh."
                )
            self._pipeline = PyannotePipeline.from_pretrained(
                PIPELINE_DE_DIARIZACAO, token=token
            )
        self.dispositivo = "cuda" if torch.cuda.is_available() else "cpu"
        self._pipeline.to(torch.device(self.dispositivo))
        _log(f"pipeline carregado em {self.dispositivo}")

    def vetor_de_voz(self, caminho: str, trechos: list[dict], id_req: int) -> list[float]:
        """O vetor que identifica uma voz, extraído dos trechos indicados.

        Recebe intervalos e não um arquivo recortado porque quem escolhe os
        trechos é o núcleo, que sabe quais são limpos: fala sem sobreposição,
        na faixa certa, somando o mínimo de segundos. Ver VOZES.md §2.
        """
        _enviar(id=id_req, tipo="progresso", pct=0.1, texto="carregando o modelo de voz")

        from pyannote.audio import Model, Inference
        import numpy as np
        import torch

        if self._voz is None:
            import torch as _t
            if self.dispositivo == "?":
                self.dispositivo = "cuda" if _t.cuda.is_available() else "cpu"
            # Mesma ordem do pipeline: a pasta ao lado primeiro. O modelo de voz
            # não tem portão no HuggingFace, mas ele viaja junto pelo ganho que
            # não é de segredo — a primeira reunião de uma instalação nova não
            # depende de rede.
            local = _voz_local()
            modelo = (Model.from_pretrained(local) if local
                      else Model.from_pretrained(MODELO_DE_VOZ,
                                                 token=os.environ.get("HF_TOKEN")))
            if torch.cuda.is_available():
                modelo = modelo.to(torch.device("cuda"))
            self._voz = Inference(modelo, window="whole")
            _log(f"modelo de voz carregado em {self.dispositivo}")

        audio = self._ler_wav(caminho)
        onda, taxa = audio["waveform"], audio["sample_rate"]

        # Concatenar os trechos limpos em vez de embedar o mais longo: o piso
        # de duração é sobre o total de fala da pessoa, e um único trecho curto
        # produz vetor ruidoso — que contamina em silêncio.
        pedacos = []
        for t in trechos:
            a, b = int(t["inicio"] * taxa), int(t["fim"] * taxa)
            if b > a:
                pedacos.append(onda[:, a:b])
        if not pedacos:
            raise RuntimeError("nenhum trecho utilizável para extrair a voz")

        junto = torch.cat(pedacos, dim=1)
        _enviar(id=id_req, tipo="progresso", pct=0.6, texto="extraindo a voz")

        vetor = self._voz({"waveform": junto, "sample_rate": taxa})
        return np.asarray(vetor).astype(float).ravel().tolist()

    def diarizar(self, caminho: str, id_req: int) -> list[dict]:
        self.carregar(id_req)
        _enviar(id=id_req, tipo="progresso", pct=0.3, texto="analisando falantes")

        saida = self._pipeline(self._ler_wav(caminho))
        # O pyannote 3.1+ devolve um objeto com a anotação dentro; versões
        # antigas devolvem a anotação direto. Mesmo tratamento do
        # src/diarization/speaker_diarizer.py, que continua sendo a referência.
        anotacao = getattr(saida, "speaker_diarization", saida)

        # Rótulos crus (SPEAKER_00): nomear é apresentação e vive no núcleo.
        return [
            {"inicio": trecho.start, "fim": trecho.end, "falante": falante}
            for trecho, _, falante in anotacao.itertracks(yield_label=True)
        ]

    @staticmethod
    def _ler_wav(caminho: str) -> dict:
        """O áudio já decodificado, do jeito que o pyannote aceita.

        Passar o caminho faria o pyannote 4 procurar o ``torchcodec``, que é
        compilado contra uma versão específica do torch — e o nosso torch vem do
        índice do PyTorch, para ter CUDA. As duas versões não casam, e o sintoma
        é ``torchcodec is not available`` no meio da diarização, depois de a
        transcrição inteira já ter rodado.

        Ler aqui elimina a dependência: o formato é o do nosso próprio gravador
        (16 kHz mono 16 bits), então não há caso geral a tratar.
        """
        import numpy as np
        import torch
        import wave

        with wave.open(caminho, "rb") as w:
            if w.getsampwidth() != 2 or w.getnchannels() != 1:
                raise RuntimeError(
                    f"esperado WAV mono de 16 bits, veio {w.getnchannels()} canais "
                    f"de {8 * w.getsampwidth()} bits"
                )
            taxa = w.getframerate()
            bruto = w.readframes(w.getnframes())

        sinal = np.frombuffer(bruto, dtype=np.int16).astype(np.float32) / 32768.0
        # (canal, tempo), que é a forma que o pyannote espera.
        return {"waveform": torch.from_numpy(sinal).unsqueeze(0), "sample_rate": taxa}


def main() -> int:
    pipeline = Pipeline()
    _enviar(tipo="pronto", motor="diarizacao", versao=VERSAO)

    for linha in sys.stdin:
        linha = linha.strip()
        if not linha:
            continue

        try:
            req = json.loads(linha)
        except json.JSONDecodeError as e:
            _enviar(tipo="erro", mensagem=f"requisição ilegível: {e}")
            continue

        id_req = req.get("id")
        try:
            op = req.get("op")
            if op not in ("diarizar", "voz"):
                raise RuntimeError(f"operação desconhecida: {op!r}")

            caminho = req.get("audio") or ""
            if not os.path.isfile(caminho):
                raise RuntimeError(f"áudio não encontrado: {caminho}")

            if op == "voz":
                vetor = pipeline.vetor_de_voz(caminho, req.get("trechos") or [], id_req)
                _enviar(id=id_req, tipo="resultado", vetor=vetor)
            else:
                segmentos = pipeline.diarizar(caminho, id_req)
                _enviar(id=id_req, tipo="resultado", segmentos=segmentos)

        except Exception as e:
            # Erro encerra a requisição, não o motor: o processo continua vivo
            # e pronto para a próxima. Ver docs/SIDECAR.md.
            _log(f"falha na requisição {id_req}: {e!r}")
            _enviar(id=id_req, tipo="erro", mensagem=str(e))

    return 0


if __name__ == "__main__":
    sys.exit(main())
