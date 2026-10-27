"""Motor de ASR como sidecar: faster-whisper atrás do protocolo por linha.

Implementa o contrato de ``docs/SIDECAR.md``. Os parâmetros de transcrição são
os mesmos do ``src/transcription/faster_whisper_transcriber.py``, que continua
sendo a referência: mudar qualquer um deles aqui muda o resultado sem que a
comparação com o app antigo perceba.

**Desde 27/10/2026 há um segundo runtime atrás do mesmo protocolo**: os modelos
do :data:`GGUF` rodam no ``transcribe.cpp``, o mesmo da legenda ao vivo. O
contrato de fio não muda — trechos com ``inicio``, ``fim``, ``texto`` e
``palavras`` —, e por isso o C# não sabe qual dos dois respondeu.

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

VERSAO = "3"

#: Os modelos que rodam no ``transcribe.cpp``, e não no faster-whisper: o id que
#: o app manda em ``--modelo`` → o arquivo em :data:`PASTA_DOS_MODELOS`.
#:
#: **O mesmo par está no** ``Nucleo/Catalogo.cs`` (o ``Arquivo`` do pacote),
#: escrito nos dois lugares de propósito, como o GGUF da legenda: quem baixa é o
#: C#, quem abre é este arquivo. O GGUF não viaja no instalador (o ``.iss``
#: exclui ``*.gguf``); entra por Ajustes › Modelos.
#:
#: O ``parakeet-ultra`` entrou em 27/10/2026, depois de ganhar do ``large-v3``
#: nas seis reuniões com Gemini: 26,1% contra 27,9% de divergência, 7,4× contra
#: 4,6× o tempo real, 2,2 GB contra 4,7 GB de VRAM
#: (``tools/medir_asr_final.py``). **F16, e não Q8**, porque foi o F16 que se
#: mediu — e em setembro a quantização mais pesada do Parakeet perdia conteúdo.
GGUF = {"parakeet-ultra": "parakeet-ultra-F16.gguf"}

PASTA_DOS_MODELOS = pathlib.Path(os.path.abspath(__file__)).parent / "modelos"

TAXA = 16000

#: O áudio vai ao ``transcribe.cpp`` em blocos, cortados no silêncio, por duas
#: razões. A primeira é o **progresso**: uma chamada só não diz onde está, e a
#: tela ficaria parada a reunião inteira. A segunda é a **memória**: o Parakeet
#: v3, numa passada única de 48 min, morreu com ``CUDA error: invalid
#: configuration argument`` (26/10/2026). O ultra passou nos 48 min, mas uma
#: reunião de duas horas nunca foi medida, e bloco limitado é o que torna o
#: custo independente da duração.
BLOCO_S = 300

#: Onde procurar o silêncio, para cada lado da fronteira de cada bloco. Cortar
#: no meio de uma palavra a perderia nos dois blocos; 10 s é mais que a maior
#: fala corrida sem pausa de 100 ms que se espera de uma reunião.
JANELA_DE_CORTE_S = 10

#: O corte dos trechos pela palavra, a mesma regra da medição. O ``segment`` do
#: Parakeet devolve os trechos da cabeça de VAD dele, de até 30 s — 15 trechos
#: em 7,7 min, contra 124 do ``large-v3`` —, e trecho longo mistura falantes.
PAUSA_S = 0.5           # silêncio entre palavras que fecha o trecho
PAUSA_NO_PONTO_S = 0.2  # basta isto quando a palavra fecha frase
TETO_S = 25.0           # o max_speech_duration_s do VAD do faster-whisper


def _threads() -> int:
    """Os núcleos físicos, como na legenda (``motores/legenda/motor.py``).

    O padrão da biblioteca (0) sobre-inscreve threads nos núcleos lógicos: no
    ``parakeet-ultra`` isso custou 6,5× contra 7,9× o tempo real, com o texto
    idêntico palavra por palavra (27/10/2026).
    """
    return max(2, min(8, (os.cpu_count() or 4) // 2))


def cortes(onda, taxa: int = TAXA) -> list:
    """Os blocos de :data:`BLOCO_S`, cada fronteira no quadro mais quieto.

    Devolve pares ``(início, fim)`` em amostras. Procura, em quadros de 100 ms,
    o de menor energia dentro de :data:`JANELA_DE_CORTE_S` em torno de cada
    fronteira, e corta no meio dele. Áudio curto é um bloco só.
    """
    import numpy as np

    n = len(onda)
    passo, janela, quadro = BLOCO_S * taxa, JANELA_DE_CORTE_S * taxa, taxa // 10
    limites = [0]
    alvo = passo
    while alvo + janela < n:
        ini, fim = max(limites[-1] + quadro, alvo - janela), alvo + janela
        k = (fim - ini) // quadro
        energia = (np.asarray(onda[ini: ini + k * quadro], dtype=np.float32)
                   .reshape(k, quadro) ** 2).mean(axis=1)
        corte = ini + int(energia.argmin()) * quadro + quadro // 2
        limites.append(corte)
        alvo = corte + passo
    limites.append(n)
    return list(zip(limites[:-1], limites[1:]))


def trechos_por_palavra(palavras, deslocamento: float) -> list:
    """Palavras com tempo → os trechos do protocolo, cortando no silêncio.

    As palavras saem **com o espaço na frente**, como as do faster-whisper:
    o C# remonta o texto de um trecho dividido por ``string.Concat`` delas
    (``Nucleo/Transcricao.cs``), e sem o espaço o texto sairia colado.
    """
    palavras = [w for w in palavras if w.text.strip()]
    trechos, atual = [], []
    for i, w in enumerate(palavras):
        atual.append(w)
        prox = palavras[i + 1] if i + 1 < len(palavras) else None
        pausa = (prox.t0_ms - w.t1_ms) / 1000 if prox else float("inf")
        fecha = w.text.rstrip().endswith((".", "?", "!"))
        longo = (w.t1_ms - atual[0].t0_ms) / 1000 > TETO_S
        if pausa >= PAUSA_S or (fecha and pausa >= PAUSA_NO_PONTO_S) or longo:
            ditas = [{"inicio": deslocamento + x.t0_ms / 1000,
                      "fim": deslocamento + x.t1_ms / 1000,
                      "texto": " " + x.text.strip()} for x in atual]
            trechos.append({"inicio": ditas[0]["inicio"], "fim": ditas[-1]["fim"],
                            "texto": "".join(d["texto"] for d in ditas),
                            "palavras": ditas})
            atual = []
    return trechos


def ler_wav(caminho: str):
    """O ``mix.wav`` que o núcleo escreve, em float32 mono a 16 kHz.

    O faster-whisper reamostra sozinho; o ``transcribe.cpp`` não. O núcleo
    sempre escreve 16 kHz (``Faixas.EscreverMix``), e outra taxa aqui é defeito
    de quem chamou — melhor dizer que transcrever outro áudio.
    """
    import wave

    import numpy as np

    with wave.open(caminho, "rb") as w:
        if w.getframerate() != TAXA or w.getsampwidth() != 2:
            raise RuntimeError(
                f"o áudio precisa ser WAV de 16 bits a {TAXA} Hz, e veio "
                f"{w.getsampwidth() * 8} bits a {w.getframerate()} Hz")
        a = np.frombuffer(w.readframes(w.getnframes()), "<i2").astype(np.float32) / 32768
        return a.reshape(-1, w.getnchannels()).mean(axis=1)


def _enviar(**campos) -> None:
    _protocolo.write(json.dumps(campos, ensure_ascii=False) + "\n")
    _protocolo.flush()


def _log(texto: str) -> None:
    print(f"[asr] {texto}", file=sys.stderr, flush=True)


class Modelo:
    """O modelo, carregado sob demanda e mantido quente.

    O faster-whisper, ou o ``transcribe.cpp`` quando o id está em :data:`GGUF`.
    """

    def __init__(self, tamanho: str) -> None:
        self._tamanho = tamanho
        self._modelo = None
        self._gguf = GGUF.get(tamanho)
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
        if self._gguf:
            self._carregar_gguf()
            return

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

    def _carregar_gguf(self) -> None:
        """O ``transcribe.cpp`` na CUDA, e na CPU se ela não subir.

        Mesmo arranjo da legenda: a falha de CUDA não é fatal aqui, porque quem
        decide se a CPU é aceitável é o núcleo, pelo ``dispositivo`` e pelo
        ``motivo`` que voltam na resposta.
        """
        import transcribe_cpp as t

        caminho = PASTA_DOS_MODELOS / self._gguf
        if not caminho.is_file():
            raise RuntimeError(
                f"o modelo {self._tamanho} não está em {caminho}. "
                "Abra Ajustes → Modelos e baixe-o antes de transcrever.")
        try:
            self._modelo = t.Model(str(caminho), backend="cuda")
            self.dispositivo = "cuda"
        except Exception as e:
            self.motivo = f"o backend de CUDA do transcribe.cpp não subiu: {e!r}"
            _log(f"SEM CUDA: {self.motivo}")
            self._modelo = t.Model(str(caminho), backend="cpu")
            self.dispositivo = "cpu"
        _log(f"modelo {self._tamanho} ({self._gguf}) carregado em {self.dispositivo}, "
             f"transcribe.cpp {getattr(t, '__version__', '?')}")

    def _transcrever_gguf(self, caminho: str, id_req: int,
                          vocabulario: str | None, idioma: str | None) -> dict:
        import transcribe_cpp as t

        # O Parakeet não tem entrada de vocabulário nem de idioma: ele detecta
        # o idioma sozinho, e a dica não muda uma palavra (medido em 26/10/2026).
        # O vocabulário não se perde — ele segue para a correção fonética, que
        # roda depois de qualquer motor.
        if vocabulario:
            _log(f"{self._tamanho} não aceita vocabulário; ele vai só para a correção")

        onda = ler_wav(caminho)
        duracao = len(onda) / TAXA
        segmentos, idioma_visto = [], None
        for ini, fim in cortes(onda):
            if fim - ini < TAXA // 2:   # o transcribe.cpp recusa entrada curta demais
                continue
            r = t.transcribe(self._modelo, onda[ini:fim], timestamps="word",
                             n_threads=_threads())
            segmentos += trechos_por_palavra(r.words, ini / TAXA)
            idioma_visto = idioma_visto or (r.language or None)
            _enviar(id=id_req, tipo="progresso", pct=min(fim / len(onda), 0.99),
                    texto="transcrevendo")

        return {"segmentos": segmentos, "idioma": idioma or idioma_visto,
                "duracao": duracao, "dispositivo": self.dispositivo,
                "motivo": self.motivo}

    def transcrever(self, caminho: str, id_req: int,
                    vocabulario: str | None, idioma: str | None) -> dict:
        self.carregar(id_req)
        if self._gguf:
            return self._transcrever_gguf(caminho, id_req, vocabulario, idioma)

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
