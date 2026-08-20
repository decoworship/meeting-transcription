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


def _achar_cuda() -> None:
    """Deixa o ctranslate2 encontrar as DLLs de CUDA que vêm com o torch.

    No Windows o ctranslate2 procura ``cublas64_12.dll`` e ``cudnn*.dll`` no
    caminho de busca do processo, e não as traz consigo. Quem as tem, no nosso
    empacotamento, é o torch — que instala tudo em ``torch/lib``. Sem este
    registro o faster-whisper cai para CPU **em silêncio**: não há erro, só
    lentidão, que é o pior tipo de falha para diagnosticar.
    """
    if sys.platform != "win32":
        return
    try:
        import importlib.util

        spec = importlib.util.find_spec("torch")
        if spec is None or not spec.submodule_search_locations:
            return
        lib = os.path.join(list(spec.submodule_search_locations)[0], "lib")
        if os.path.isdir(lib):
            os.add_dll_directory(lib)
    except Exception as e:                                   # nunca fatal
        print(f"[asr] não foi possível registrar as DLLs de CUDA: {e!r}",
              file=sys.stderr, flush=True)


_achar_cuda()

VERSAO = "2"


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
        self.dispositivo = "?"
        self.motivo: str | None = None

    def diagnostico(self) -> dict:
        """O que o torch enxerga da placa — e, quando não enxerga, por quê.

        Existe porque o app tinha duas opiniões sobre a mesma pergunta. O bloco
        de diagnóstico da tela pergunta ao ``nvidia-smi``, que responde pela
        presença do driver; quem decide o dispositivo da transcrição é o
        ``torch.cuda.is_available()``, que depende também das DLLs de CUDA
        estarem alcançáveis. Os dois discordaram na máquina de um usuário em
        18/08/2026: a tela dizia "RTX 4050" e o modelo rodava na CPU.

        "Rodar na CPU" não é só lento: o ``large-v3`` em CPU come RAM por horas,
        e na máquina dele **derrubou o Windows**. Então a resposta desta função é
        o que permite o app parar antes, em vez de descobrir no fim.
        """
        import torch

        cuda = torch.cuda.is_available()
        info = {
            "cuda": cuda,
            "torch": getattr(torch, "__version__", "?"),
            # None aqui significa build de CPU do torch — é o caso em que nenhuma
            # configuração da máquina do usuário resolveria.
            "cuda_do_torch": torch.version.cuda,
            "placas": torch.cuda.device_count() if cuda else 0,
        }
        if cuda:
            try:
                info["nome"] = torch.cuda.get_device_name(0)
            except Exception:                                # nunca fatal
                pass
            return info

        # Sem CUDA, a pergunta que importa é qual das três causas é a desta
        # máquina — e cada uma tem uma saída diferente.
        if torch.version.cuda is None:
            info["motivo"] = ("o torch empacotado é a versão de CPU; nenhuma "
                              "configuração desta máquina faria a placa funcionar")
        elif torch.cuda.device_count() == 0:
            info["motivo"] = ("o torch tem CUDA " + str(torch.version.cuda)
                              + ", mas não encontrou placa nenhuma — driver antigo "
                                "demais para esta versão de CUDA, ou DLL de CUDA "
                                "faltando ao lado do torch")
        else:
            info["motivo"] = "o torch não conseguiu iniciar o CUDA nesta máquina"
        return info

    def carregar(self, id_req: int) -> None:
        if self._modelo is not None:
            return

        _enviar(id=id_req, tipo="progresso", pct=0.0, texto="carregando o modelo")
        from faster_whisper import WhisperModel

        placa = self.diagnostico()
        cuda = placa["cuda"]
        self.dispositivo = "cuda" if cuda else "cpu"
        self.motivo = placa.get("motivo")

        if not cuda:
            _log(f"SEM CUDA: {self.motivo}")

        self._modelo = WhisperModel(
            self._tamanho,
            device="cuda" if cuda else "cpu",
            compute_type="float16" if cuda else "int8",
        )
        _log(f"modelo {self._tamanho} carregado em {self.dispositivo}")

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
            # As palavras vão junto. O alinhamento por palavra já era calculado
            # (word_timestamps=True acima) e jogado fora aqui — e é exatamente o
            # insumo que permite cortar um segmento na troca de falante, que é o
            # defeito da FASE6 §4.1: um rótulo por segmento do ASR faz sumir
            # quem falou dentro de um segmento longo.
            segmentos.append({
                "inicio": s.start, "fim": s.end, "texto": s.text,
                "palavras": [{"inicio": p.start, "fim": p.end, "texto": p.word}
                             for p in (s.words or [])],
            })
            if duracao > 0:
                _enviar(id=id_req, tipo="progresso",
                        pct=min(s.end / duracao, 0.99), texto="transcrevendo")

        return {"segmentos": segmentos, "idioma": info.language, "duracao": duracao,
                "dispositivo": self.dispositivo, "motivo": self.motivo}


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
            op = req.get("op")

            # Responder "que placa você usaria?" sem carregar o modelo: é o que
            # deixa a tela perguntar de graça, e o que o usuário manda de volta
            # quando a transcrição sai lenta.
            if op == "dispositivo":
                _enviar(id=id_req, tipo="resultado", **modelo.diagnostico())
                continue

            if op != "transcrever":
                raise RuntimeError(f"operação desconhecida: {op!r}")

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
