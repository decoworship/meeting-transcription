"""Motor de ASR como sidecar: faster-whisper atrás do protocolo por linha.

Implementa o contrato de ``docs/SIDECAR.md``. Os parâmetros de transcrição são
os mesmos do ``src/transcription/faster_whisper_transcriber.py``, que continua
sendo a referência: mudar qualquer um deles aqui muda o resultado sem que a
comparação com o app antigo perceba.

Uso (é assim que o cliente C# o inicia)::

    python motor.py [--modelo large-v3]
"""

from __future__ import annotations

import json
import os
import sys

# ANTES de qualquer import pesado — ver a mesma nota em motores/diarizacao.
# ctranslate2 e torch escrevem no stdout, e uma linha delas corrompe o protocolo.
_protocolo = os.fdopen(os.dup(1), "w", encoding="utf-8", newline="\n")
os.dup2(2, 1)

VERSAO = "1"


def _enviar(**campos) -> None:
    _protocolo.write(json.dumps(campos, ensure_ascii=False) + "\n")
    _protocolo.flush()


def _log(texto: str) -> None:
    print(f"[asr] {texto}", file=sys.stderr, flush=True)


class Modelo:
    """O faster-whisper, carregado sob demanda e mantido quente."""

    def __init__(self, tamanho: str) -> None:
        self._tamanho = tamanho
        self._modelo = None

    def carregar(self, id_req: int) -> None:
        if self._modelo is not None:
            return

        _enviar(id=id_req, tipo="progresso", pct=0.0, texto="carregando o modelo")
        from faster_whisper import WhisperModel
        import torch

        cuda = torch.cuda.is_available()
        self._modelo = WhisperModel(
            self._tamanho,
            device="cuda" if cuda else "cpu",
            compute_type="float16" if cuda else "int8",
        )
        _log(f"modelo {self._tamanho} carregado em {'cuda' if cuda else 'cpu'}")

    def transcrever(self, caminho: str, id_req: int,
                    vocabulario: str | None, idioma: str | None) -> dict:
        self.carregar(id_req)

        # Os mesmos parâmetros do transcritor do app atual. Cada um tem um
        # motivo registrado lá; repetir os motivos aqui só os faria divergir.
        kwargs = dict(
            language=idioma,
            beam_size=5,
            condition_on_previous_text=False,
            word_timestamps=True,
            hallucination_silence_threshold=2.0,
            vad_filter=True,
            vad_parameters=dict(
                min_silence_duration_ms=500,
                max_speech_duration_s=25,
                threshold=0.35,
            ),
        )
        # hotwords, não initial_prompt: é reinjetado em toda janela de 30 s, em
        # vez de enviesar só a primeira e ser truncado em 223 tokens.
        if vocabulario:
            kwargs["hotwords"] = vocabulario

        geracao, info = self._modelo.transcribe(caminho, **kwargs)

        duracao = info.duration or 0.0
        segmentos = []
        for s in geracao:
            segmentos.append({"inicio": s.start, "fim": s.end, "texto": s.text})
            if duracao > 0:
                _enviar(id=id_req, tipo="progresso",
                        pct=min(s.end / duracao, 0.99), texto="transcrevendo")

        return {"segmentos": segmentos, "idioma": info.language, "duracao": duracao}


def main() -> int:
    tamanho = "large-v3"
    if "--modelo" in sys.argv:
        tamanho = sys.argv[sys.argv.index("--modelo") + 1]

    modelo = Modelo(tamanho)
    _enviar(tipo="pronto", motor="asr", versao=VERSAO)

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
            if req.get("op") != "transcrever":
                raise RuntimeError(f"operação desconhecida: {req.get('op')!r}")

            caminho = req.get("audio") or ""
            if not os.path.isfile(caminho):
                raise RuntimeError(f"áudio não encontrado: {caminho}")

            r = modelo.transcrever(caminho, id_req,
                                   req.get("vocabulario"), req.get("idioma"))
            _enviar(id=id_req, tipo="resultado", **r)

        except Exception as e:
            _log(f"falha na requisição {id_req}: {e!r}")
            _enviar(id=id_req, tipo="erro", mensagem=str(e))

    return 0


if __name__ == "__main__":
    sys.exit(main())
