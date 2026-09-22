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
import pathlib
import sys

# ANTES de qualquer import pesado — ver a mesma nota em motores/diarizacao.
# ctranslate2 escreve no stdout, e uma linha dele corrompe o protocolo.
_protocolo = os.fdopen(os.dup(1), "w", encoding="utf-8", newline="\n")
os.dup2(2, 1)


def _pastas_de_cuda() -> list:
    """As pastas do empacotamento que contêm as DLLs de CUDA, em ordem.

    **Quem as tinha era o torch, e ele saiu em 22/09/2026.** Até então tudo
    vivia em ``torch/lib``, e bastava registrar aquela pasta. Agora as mesmas
    DLLs vêm de wheels próprios — ``nvidia-cublas-cu12``, ``nvidia-cudnn-cu12``,
    ``nvidia-cufft-cu12`` e ``nvidia-cuda-runtime-cu12`` —, que o
    ``tools/empacotar_motores.sh`` instala, e cada um põe as suas em
    ``nvidia/<pacote>/bin``.

    As pastas saem da própria instalação, e não de caminho fixo: este arquivo
    está em ``motores/asr/``, e o ``site-packages`` é
    ``motores/python/Lib/site-packages``. Pasta que não existe é só pulada.
    """
    aqui = pathlib.Path(os.path.abspath(__file__)).parent
    site = aqui.parent / "python" / "Lib" / "site-packages"
    candidatas = [site / "ctranslate2", site / "onnxruntime" / "capi"]
    candidatas += sorted((site / "nvidia").glob("*/bin"))
    return [str(c) for c in candidatas if c.is_dir()]


def _achar_cuda() -> None:
    """Deixa o ctranslate2 encontrar as DLLs de CUDA do empacotamento.

    No Windows o ctranslate2 procura ``cublas64_12.dll`` e ``cudnn*.dll`` no
    caminho de busca do processo, e traz consigo só o ``cudnn64_9.dll``
    principal. Sem este registro o faster-whisper cai para CPU **em silêncio**:
    não há erro, só lentidão, que é o pior tipo de falha para diagnosticar.

    **`os.add_dll_directory` sozinho não basta, e a razão está medida** em
    ``motores/diarizacao/pipeline/sessao.py``: o ``cudnn64_9.dll`` carrega as
    próprias sublibs (``cudnn_graph64_9``, ``cudnn_cnn64_9``, ``cudnn_ops64_9``,
    ...) procurando no ``PATH`` do processo, e não nas pastas que o loader
    registrou. Por isso as pastas vão para os dois lugares.
    """
    if sys.platform != "win32":
        return
    try:
        pastas = _pastas_de_cuda()
        for pasta in pastas:
            try:
                os.add_dll_directory(pasta)
            except (OSError, AttributeError):
                pass
        if pastas:
            os.environ["PATH"] = (os.pathsep.join(pastas) + os.pathsep
                                  + os.environ.get("PATH", ""))
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

    @staticmethod
    def _versao_do_cuda() -> str | None:
        """A versão do runtime de CUDA que ESTE processo conseguiu carregar.

        ``None`` quando o ``cudart`` não carrega — o que, no nosso
        empacotamento, quer dizer DLL faltando e não máquina sem placa.
        """
        import ctypes

        nomes = (("cudart64_12.dll",) if sys.platform == "win32"
                 else ("libcudart.so.12", "libcudart.so"))
        for nome in nomes:
            try:
                lib = ctypes.CDLL(nome)
            except OSError:
                continue
            try:
                v = ctypes.c_int()
                if lib.cudaRuntimeGetVersion(ctypes.byref(v)) == 0 and v.value:
                    return f"{v.value // 1000}.{(v.value % 1000) // 10}"
            except AttributeError:
                pass
        return None

    @staticmethod
    def _nome_da_placa() -> str | None:
        """O nome da placa, perguntado ao driver — não ao nvidia-smi.

        É a API de driver do CUDA (``nvcuda.dll``), que é justamente a camada
        que o ctranslate2 usa: se ela responde, a placa existe para quem vai
        transcrever. ``None`` é só falta de nome, nunca falta de placa — quem
        decide isso é o ``diagnostico``.
        """
        import ctypes

        nomes = (("nvcuda.dll",) if sys.platform == "win32"
                 else ("libcuda.so.1", "libcuda.so"))
        for nome in nomes:
            try:
                lib = ctypes.CDLL(nome)
            except OSError:
                continue
            try:
                if lib.cuInit(0) != 0:
                    return None
                dev = ctypes.c_int()
                if lib.cuDeviceGet(ctypes.byref(dev), 0) != 0:
                    return None
                buf = ctypes.create_string_buffer(256)
                if lib.cuDeviceGetName(buf, 256, dev) != 0:
                    return None
                return buf.value.decode("utf-8", "replace") or None
            except (AttributeError, OSError):
                return None
        return None

    def diagnostico(self) -> dict:
        """O que o motor de transcrição enxerga da placa — e, quando não
        enxerga, por quê.

        Existe porque o app tinha duas opiniões sobre a mesma pergunta. O bloco
        de diagnóstico da tela pergunta ao ``nvidia-smi``, que responde pela
        presença do driver; quem decide o dispositivo da transcrição é esta
        função, que depende também das DLLs de CUDA estarem alcançáveis. Os
        dois discordaram na máquina de um usuário em 18/08/2026: a tela dizia
        "RTX 4050" e o modelo rodava na CPU.

        "Rodar na CPU" não é só lento: o ``large-v3`` em CPU come RAM por horas,
        e na máquina dele **derrubou o Windows**. Então a resposta desta função é
        o que permite o app parar antes, em vez de descobrir no fim.

        **Quem responde é o ctranslate2 desde 22/09/2026, e não mais o torch.**
        Era ``torch.cuda.is_available()`` — e o torch saiu do empacotamento com
        a diarização em ONNX (``docs/DIARIZACAO-ONNX.md``). Perguntar ao
        ctranslate2 não é só o que sobrou: é a biblioteca que **de fato roda o
        ASR**, então a resposta dela é sobre o caminho que vai ser usado, e não
        sobre um vizinho que também tem CUDA. A pergunta continua **medida**:
        ``get_cuda_device_count()`` conta placas pelo runtime de CUDA, e
        ``get_supported_compute_types("cuda", 0)`` consulta a placa de verdade —
        e é conferido o ``float16``, que é exatamente o ``compute_type`` que o
        ``carregar`` pede. Pedir e não poder é a falha muda que este app já
        pagou duas vezes.

        A forma do retorno não mudou, porque é contrato de fio com o C#
        (``Sidecar/Protocolo.cs``): ``cuda``, ``nome``, ``cuda_do_torch`` e
        ``motivo``. O ``cuda_do_torch`` guarda o nome antigo e passou a valer a
        versão do runtime de CUDA carregado; renomeá-lo trocaria a chave no
        JSON e o campo chegaria nulo no registro, que é onde ele é lido.
        """
        import ctranslate2

        versao_cuda = self._versao_do_cuda()
        info = {
            "cuda": False,
            "runtime": f"ctranslate2 {getattr(ctranslate2, '__version__', '?')}",
            # None aqui significa que nem o cudart carregou — DLL faltando no
            # empacotamento, e não configuração da máquina de quem instalou.
            "cuda_do_torch": versao_cuda,
            "placas": 0,
        }

        try:
            info["placas"] = ctranslate2.get_cuda_device_count()
        except Exception as e:                               # nunca fatal
            info["motivo"] = (f"o ctranslate2 não conseguiu contar as placas: {e}")
            return info

        if info["placas"] > 0:
            try:
                tipos = ctranslate2.get_supported_compute_types("cuda", 0)
            except Exception as e:
                info["motivo"] = (f"a placa foi contada, mas o ctranslate2 não "
                                  f"conseguiu consultá-la: {e}")
                return info
            if "float16" in tipos:
                info["cuda"] = True
                nome = self._nome_da_placa()
                if nome:
                    info["nome"] = nome
                return info
            info["motivo"] = ("a placa não oferece float16, que é o compute_type "
                              f"da transcrição (oferece: {sorted(tipos)})")
            return info

        # Sem placa contada, a pergunta que importa é qual das causas é a desta
        # máquina — e cada uma tem uma saída diferente.
        if versao_cuda is None:
            info["motivo"] = ("as DLLs de CUDA não foram encontradas ao lado do "
                              "motor; o empacotamento está incompleto e nenhuma "
                              "configuração desta máquina resolveria")
        else:
            info["motivo"] = ("o runtime de CUDA " + versao_cuda
                              + " carregou, mas não encontrou placa nenhuma — "
                                "driver antigo demais para esta versão de CUDA, "
                                "ou não há placa NVIDIA nesta máquina")
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
            # threshold: 0,25, e não 0,35 — medido em 27/08/2026 sobre quatro
            # gravações reais, de 7 a 122 minutos, com três réguas que se
            # cobrem (docs/AUDITORIA-ATAS.md §7).
            #
            #   cobertura, contra a transcrição paralela do Meet: 0,35 é a PIOR
            #   das configurações com VAD nas duas gravações que têm par;
            #
            #   invenção sobre ausência de sinal: zero palavras em 0,25 — só
            #   desligar o VAD inventou;
            #
            #   texto nas pausas longas: na de 122 minutos, 0,25 recuperou 113
            #   palavras dentro das pausas e ficou +92 no total; o 0,15
            #   recuperou as MESMAS 113 e ficou -69 no total, ou seja, mexeu em
            #   outros trechos e saiu no prejuízo.
            #
            # 0,15 ganhou na gravação de 32 minutos e perdeu nas outras três.
            # 0,25 ganha em três de quatro, e é o único que nunca piora.
            #
            # **min_silence_duration_ms é parâmetro morto.** 200 e 500 deram
            # resultado idêntico ao dígito nas duas gravações com referência.
            # Fica em 500 porque mudá-lo não faz nada.
            #
            # **Não desligue o vad_filter.** Foi a pior configuração nas quatro,
            # e contraria o resultado 6 da FASE0 pelo motivo que o sweep_vad.py
            # já previa: lá a medição foi sobre fala concatenada, quase sem
            # silêncio; em gravação real é no silêncio que o VAD ganha o salário.
            vad_parameters=dict(
                min_silence_duration_ms=500,
                max_speech_duration_s=25,
                threshold=0.25,
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
