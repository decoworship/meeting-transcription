"""Motor de diarização como sidecar: ONNX Runtime atrás do protocolo por linha.

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

# ANTES de qualquer import pesado. onnxruntime e huggingface_hub escrevem no
# stdout sem pedir licença — barra de progresso de download, avisos de versão,
# mensagens de provedor — e uma linha dessas no meio do fluxo corrompe o
# protocolo, com um sintoma que não aponta para a causa. O descritor 1 vira
# nosso canal privado; o stdout do processo passa a ser o stderr.
_protocolo = os.fdopen(os.dup(1), "w", encoding="utf-8", newline="\n")
os.dup2(2, 1)

VERSAO = "5"

# O mesmo modelo que o app Python usa. Trocar mudaria o espaço vetorial e
# invalidaria toda voz já aprendida — os vetores de modelos diferentes não são
# comparáveis, e a comparação não falha: ela só passa a errar.
# ATENÇÃO: este é o modelo de VOZ, e ele não é escolhível. A escolha de modelo
# de diarização (`modelo` na requisição) troca o *pipeline* que separa os
# falantes; trocar o de voz invalidaria toda voz já aprendida, porque vetores de
# modelos diferentes não são comparáveis — e a comparação não falha, ela só passa
# a errar em silêncio. São duas coisas no mesmo motor, e só uma delas se troca.
MODELO_DE_VOZ = "pyannote/wespeaker-voxceleb-resnet34-LM"
#: A taxa em que o modelo de voz foi treinado, e a única que o fbank em numpy
#: de pipeline/fbank.py sabe calcular.
TAXA_DA_VOZ = 16000

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


#: O pipeline usado quando ninguém pede outro.
PADRAO = "community-1"

#: Os motores de diarização aceitos — **um só desde 22/09/2026**.
#:
#: O `torch` era o pyannote como sempre foi, e o `onnx` o porte de 18/09/2026
#: (docs/DIARIZACAO-ONNX.md). O porte fechou com acordo 1,0000 na gravação de
#: referência e nas quatro do acervo, e mais rápido: 7,91x o tempo real na GPU
#: contra 5,17x do torch. Com isso o torch saiu do empacotamento — são 3,6 GB
#: carregados para não serem usados —, e o ramo `torch` deixou de ter como
#: rodar. A chave fica, com um valor só.
MOTORES = ("onnx",)
PADRAO_DE_MOTOR = "onnx"


def escolher_motor(valor: str | None) -> str:
    """O motor pedido, ou o padrão — **nunca um erro**.

    Um `app.json` com valor desconhecido cai no padrão em silêncio, e isso
    inclui um `"torch"` deixado para trás por quem testou o porte. Recusar a
    diarização por causa de uma chave é pior que ignorá-la: a pessoa perde a
    separação de falantes de uma reunião que já aconteceu. É a mesma decisão
    do `MotorAceito` quando o MOSS saiu (docs/CONVERGENCIA.md).
    """
    return valor if valor in MOTORES else PADRAO_DE_MOTOR


def _modelo_onnx_local(nome: str = PADRAO) -> str | None:
    """A pasta do modelo onnx embarcado, ou ``None`` quando algum dos quatro
    artefatos não está lá.

    É a mesma pasta em que o pipeline pyannote vivia (``modelos/<nome>``):
    os dois formatos ficaram lado a lado enquanto a chave teve dois valores,
    como o docs/DIARIZACAO-ONNX.md §4 desenha, e os pesos ``.bin`` continuam
    lá porque são eles que o exportador lê.

    Os quatro são o que ``tools/exportar_diarizacao_onnx.py`` produz, e os
    quatro são exigidos: faltar só o ``mel.npy`` não impede o pipeline de
    carregar, porque ``Extrator`` cairia para ``banco_mel()`` — que importa
    torch e torchaudio (``fbank.py``) e derrotaria em silêncio o motivo de o
    motor onnx existir. Conferir os quatro aqui, antes de entrar no caminho
    onnx, é o que torna esse import impossível.
    """
    if not nome or os.path.basename(nome) != nome or nome in (".", ".."):
        return None
    pasta = os.path.join(_LOCAIS, nome)
    artefatos = (
        os.path.join(pasta, "segmentation", "model.onnx"),
        os.path.join(pasta, "embedding", "codificador.onnx"),
        os.path.join(pasta, "embedding", "cabeca.onnx"),
        os.path.join(pasta, "embedding", "mel.npy"),
    )
    return pasta if all(os.path.isfile(a) for a in artefatos) else None


def _voz_local() -> str | None:
    """A pasta do modelo de voz, ou ``None`` quando um dos dois artefatos falta.

    São os artefatos **onnx** (``voz.onnx`` e ``mel.npy``) desde 22/09/2026, e
    não mais o ``pytorch_model.bin``: o caminho da voz é o último que carregava
    torch, e é o que faz os 3,6 GB saírem (docs/DIARIZACAO-ONNX.md). Os pesos
    do torch continuam ao lado — são eles que o exportador lê —, mas ninguém
    em produção os abre.

    Ao contrário do modelo de diarização, este **não é escolhível**: uma pasta
    só, sem parâmetro, porque trocá-lo invalidaria toda voz já aprendida.
    """
    pasta = os.path.join(_LOCAIS, "wespeaker-voxceleb-resnet34-LM")
    artefatos = (os.path.join(pasta, "voz.onnx"), os.path.join(pasta, "mel.npy"))
    return pasta if all(os.path.isfile(a) for a in artefatos) else None


def _enviar(**campos) -> None:
    _protocolo.write(json.dumps(campos, ensure_ascii=False) + "\n")
    _protocolo.flush()


def _log(texto: str) -> None:
    print(f"[diarizacao] {texto}", file=sys.stderr, flush=True)


class Pipeline:
    """O pipeline ONNX, carregado sob demanda e mantido quente."""

    def __init__(self) -> None:
        self._onnx = None
        self._modelo = None
        self._motor = None
        self._voz = None
        self.dispositivo = "?"

    def _carregado(self) -> bool:
        return self._onnx is not None

    def carregar(self, id_req: int, modelo: str | None = None,
                 motor: str = PADRAO_DE_MOTOR) -> None:
        modelo = modelo or PADRAO

        # Duas dimensões decidem se recarrega agora: o modelo (community-1,
        # ...) e o motor. O motor tem um valor só hoje, e a dimensão fica:
        # manter o pipeline quente é o que faz a segunda reunião não pagar o
        # carregamento de novo, e um motor parado respondendo por outro é
        # exatamente a classe de bug que este porte já produziu três vezes.
        if self._motor == motor and self._modelo == modelo and self._carregado():
            return
        if self._carregado():
            _log(f"trocando de motor={self._motor}/modelo={self._modelo} "
                 f"para motor={motor}/modelo={modelo}")
        self._onnx = None

        _enviar(id=id_req, tipo="progresso", pct=0.0, texto="carregando o modelo")

        self._carregar_onnx(modelo)

        self._modelo = modelo
        self._motor = motor

    def _carregar_onnx(self, modelo: str) -> None:
        # NUNCA importa torch neste caminho — é o ponto inteiro do porte
        # (docs/DIARIZACAO-ONNX.md): carregar 4,5 GB de torch para não usá-los
        # derrotaria a razão de a chave existir.
        pasta = _modelo_onnx_local(modelo)
        if pasta is None:
            raise RuntimeError(
                f"o modelo onnx {modelo!r} não está completo em {_LOCAIS} "
                "(faltam segmentation/model.onnx, embedding/codificador.onnx, "
                "embedding/cabeca.onnx ou embedding/mel.npy). Rode "
                "tools/exportar_diarizacao_onnx.py."
            )
        # pipeline/ tem imports próprios sem prefixo de pacote
        # (`from segmentacao import ...`), então ele entra no sys.path em vez
        # de ser importado como submódulo de motores.diarizacao.
        pipeline_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "pipeline")
        if pipeline_dir not in sys.path:
            sys.path.append(pipeline_dir)
        from diarizacao import Diarizador

        # preferir_gpu=True: o CUDA EP do onnxruntime foi medido (V5 —
        # docs/DIARIZACAO-ONNX.md §5.1) na mesma gravação e gabarito do V1,
        # com o provedor efetivo conferido (não só pedido): 11,80x o tempo
        # real, acordo 1,0000 — a preocupação de reprodutibilidade que
        # justificava fixar CPU não se confirmou neste modelo. O
        # `sessao.abrir` cai para CPU sozinho quando o provedor CUDA não
        # está disponível ou falha ao carregar, avisando alto no stderr; o
        # caminho CPU continua utilizável, a ~1,28x o tempo real.
        self._onnx = Diarizador(pasta, preferir_gpu=True)
        _log(f"pipeline onnx carregado: {pasta} "
             f"(segmentação em {self._onnx.seg.provedor}, "
             f"embedding em {self._onnx.emb.provedor})")

    #: Como o modelo de voz se identifica nas amostras guardadas.
    #:
    #: São os **pesos**, e não o caminho: local ou do HuggingFace, são os
    #: mesmos bytes e produzem o mesmo espaço vetorial. É esta identidade que o
    #: núcleo carimba em cada voz aprendida, para nunca comparar vetores de
    #: modelos diferentes (docs/VOZES.md §7).
    #:
    #: **O runtime mudou em 22/09/2026 e esta string não.** É de propósito: o
    #: que o carimbo protege é o espaço vetorial, e ele é dos pesos — o ONNX
    #: executa os mesmos. Renomear aqui para "…-onnx" marcaria todo vetor novo
    #: como incomparável com os 160 já guardados e apagaria na prática o banco
    #: de vozes inteiro, por uma diferença que a régua mostra não existir.
    def modelo_de_voz(self) -> str:
        return MODELO_DE_VOZ

    def vetor_de_voz(self, caminho: str, trechos: list[dict], id_req: int) -> list[float]:
        """O vetor que identifica uma voz, extraído dos trechos indicados.

        Recebe intervalos e não um arquivo recortado porque quem escolhe os
        trechos é o núcleo, que sabe quais são limpos: fala sem sobreposição,
        na faixa certa, somando o mínimo de segundos. Ver VOZES.md §2.

        **Sem torch desde 22/09/2026.** Até então este era o único caminho do
        app que ainda subia o pyannote (``Inference(modelo, window="whole")``),
        e era ele que segurava os 3,6 GB. Os vetores continuam os mesmos —
        ``pipeline/testes/test_voz.py`` mede este caminho contra aquele
        ``Inference``, e ``tools/conferir_voz_onnx.py`` o mede contra o banco
        de vozes já gravado —, e por isso ``modelo_de_voz()`` não mudou: mudar
        a identidade jogaria fora toda voz aprendida por uma diferença que não
        existe.
        """
        _enviar(id=id_req, tipo="progresso", pct=0.1, texto="carregando o modelo de voz")

        import numpy as np

        if self._voz is None:
            local = _voz_local()
            if local is None:
                raise RuntimeError(
                    f"o modelo de voz onnx não está completo em {_LOCAIS} "
                    "(faltam wespeaker-voxceleb-resnet34-LM/voz.onnx ou mel.npy). "
                    "Rode tools/exportar_diarizacao_onnx.py."
                )
            # pipeline/ tem imports próprios sem prefixo de pacote, então ele
            # entra no sys.path — mesma razão que em `_carregar_onnx`.
            pipeline_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "pipeline")
            if pipeline_dir not in sys.path:
                sys.path.append(pipeline_dir)
            from voz import ExtratorDeVoz

            self._voz = ExtratorDeVoz(local, preferir_gpu=True)
            # Só quando ninguém disse ainda: `dispositivo` é o rótulo do motor
            # de diarização, e a voz não pode sobrescrever o dele — os dois
            # rodam em sessões independentes e podem cair para provedores
            # diferentes.
            if self.dispositivo == "?":
                self.dispositivo = ("cuda" if self._voz.provedor == "CUDAExecutionProvider"
                                    else "cpu")
            _log(f"modelo de voz onnx carregado: {local} ({self._voz.provedor})")

        onda, taxa = self._ler_onda(caminho)
        # O fbank de pipeline/fbank.py tem 16 kHz na tabela mel e nos passos de
        # janela; o caminho antigo reamostrava sozinho dentro do `model.audio`
        # do pyannote, e este não. O nosso gravador só produz 16 kHz, então
        # isto é uma rede de proteção — mas sem ela um WAV de outra taxa sairia
        # com um vetor errado e nenhum aviso, que é o modo de falha que este
        # porte mais teme.
        if taxa != TAXA_DA_VOZ:
            raise RuntimeError(
                f"o modelo de voz espera {TAXA_DA_VOZ} Hz, o áudio veio a {taxa} Hz"
            )

        # Concatenar os trechos limpos em vez de embedar o mais longo: o piso
        # de duração é sobre o total de fala da pessoa, e um único trecho curto
        # produz vetor ruidoso — que contamina em silêncio.
        pedacos = []
        for t in trechos:
            a, b = int(t["inicio"] * taxa), int(t["fim"] * taxa)
            if b > a:
                pedacos.append(onda[a:b])
        if not pedacos:
            raise RuntimeError("nenhum trecho utilizável para extrair a voz")

        junto = np.concatenate(pedacos)
        _enviar(id=id_req, tipo="progresso", pct=0.6, texto="extraindo a voz")

        vetor = self._voz(junto)
        return np.asarray(vetor).astype(float).ravel().tolist()

    def diarizar(self, caminho: str, id_req: int, modelo: str | None = None,
                 motor: str | None = None) -> list[dict]:
        motor = escolher_motor(motor)
        self.carregar(id_req, modelo, motor)
        _enviar(id=id_req, tipo="progresso", pct=0.3, texto="analisando falantes")

        onda, taxa = self._ler_onda(caminho)
        # list[dict] com inicio/fim/falante, que é a forma que o núcleo espera
        # — a mesma que o ramo pyannote produzia —, por construção do
        # Diarizador (T6). Rótulos crus (SPEAKER_00): nomear é apresentação e
        # vive no núcleo.
        return self._onnx(onda, taxa)

    @staticmethod
    def _ler_onda(caminho: str) -> tuple:
        """O áudio do nosso gravador, em numpy puro — sem torch.

        O formato é o do próprio gravador (16 kHz mono 16 bits), então não há
        caso geral a tratar. Até 22/09/2026 havia ao lado um `_ler_wav` que
        devolvia o dict `{"waveform", "sample_rate"}` do pyannote, e ele
        importava torch; saiu junto com o ramo que o chamava.
        """
        import numpy as np
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
        return sinal, taxa


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
                # O modelo vai junto do vetor: sem ele o núcleo não teria como
                # saber que dois vetores não são comparáveis.
                _enviar(id=id_req, tipo="resultado", vetor=vetor,
                        modelo=pipeline.modelo_de_voz())
            else:
                segmentos = pipeline.diarizar(caminho, id_req, req.get("modelo"),
                                              req.get("motor_de_diarizacao"))
                _enviar(id=id_req, tipo="resultado", segmentos=segmentos)

        except Exception as e:
            # Erro encerra a requisição, não o motor: o processo continua vivo
            # e pronto para a próxima. Ver docs/SIDECAR.md.
            _log(f"falha na requisição {id_req}: {e!r}")
            _enviar(id=id_req, tipo="erro", mensagem=str(e))

    return 0


if __name__ == "__main__":
    sys.exit(main())
