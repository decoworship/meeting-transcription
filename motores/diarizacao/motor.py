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

VERSAO = "1"


def _enviar(**campos) -> None:
    _protocolo.write(json.dumps(campos, ensure_ascii=False) + "\n")
    _protocolo.flush()


def _log(texto: str) -> None:
    print(f"[diarizacao] {texto}", file=sys.stderr, flush=True)


class Pipeline:
    """O pyannote, carregado sob demanda e mantido quente."""

    def __init__(self) -> None:
        self._pipeline = None

    def carregar(self, id_req: int) -> None:
        if self._pipeline is not None:
            return

        _enviar(id=id_req, tipo="progresso", pct=0.0, texto="carregando o modelo")
        from pyannote.audio import Pipeline as PyannotePipeline
        import torch

        token = os.environ.get("HF_TOKEN")
        if not token:
            raise RuntimeError(
                "HF_TOKEN não está no ambiente; o pyannote precisa dele para "
                "baixar o modelo na primeira execução."
            )

        # community-1: 6,7 pontos de DER melhor que o 3.1 na medição da Fase 0.
        self._pipeline = PyannotePipeline.from_pretrained(
            "pyannote/speaker-diarization-community-1", token=token
        )
        self._pipeline.to(torch.device("cuda" if torch.cuda.is_available() else "cpu"))
        _log(f"pipeline carregado em {'cuda' if torch.cuda.is_available() else 'cpu'}")

    def diarizar(self, caminho: str, id_req: int) -> list[dict]:
        self.carregar(id_req)
        _enviar(id=id_req, tipo="progresso", pct=0.3, texto="analisando falantes")

        saida = self._pipeline(caminho)
        # O pyannote 3.1+ devolve um objeto com a anotação dentro; versões
        # antigas devolvem a anotação direto. Mesmo tratamento do
        # src/diarization/speaker_diarizer.py, que continua sendo a referência.
        anotacao = getattr(saida, "speaker_diarization", saida)

        # Rótulos crus (SPEAKER_00): nomear é apresentação e vive no núcleo.
        return [
            {"inicio": trecho.start, "fim": trecho.end, "falante": falante}
            for trecho, _, falante in anotacao.itertracks(yield_label=True)
        ]


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
            if req.get("op") != "diarizar":
                raise RuntimeError(f"operação desconhecida: {req.get('op')!r}")

            caminho = req.get("audio") or ""
            if not os.path.isfile(caminho):
                raise RuntimeError(f"áudio não encontrado: {caminho}")

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
