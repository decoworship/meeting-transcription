"""Motor MOSS como sidecar: texto **e** falante numa passada só.

Implementa o contrato de ``docs/SIDECAR.md``, com uma operação a mais —
``transcrever_e_separar`` —, e é o único motor deste app que devolve
``texto`` e ``falante`` no mesmo segmento. Os outros dois preenchem um campo
cada, e é o núcleo que os cruza por sobreposição temporal.

**De onde ele veio.** O ``OpenMOSS-Team/MOSS-Transcribe-Diarize`` (0,9 B,
Apache-2.0) foi medido no acervo deste projeto em 02–03/09/2026 e ganhou do
pipeline de dois motores no texto e no falante — ver
``docs/FASE7-RESULTADOS.md`` §7.4. A implementação de referência é
``tools/medir_moss.py``, e **é dela que este arquivo saiu**: mudar um parâmetro
aqui sem mudar lá faz o app deixar de reproduzir a medição que o aprovou.

Uso (é assim que o cliente C# o inicia)::

    python motor.py
"""

from __future__ import annotations

import json
import os
import sys

# ANTES de qualquer import pesado, e pela mesma razão dos outros dois motores: o
# ggml escreve no stdout por conta própria — tamanho de tensor, backend
# escolhido, avisos de quantização — e **uma linha dessas corrompe o
# protocolo**, com um sintoma (JSON inválido em ponto imprevisível) que não
# aponta para a causa. O descritor 1 vira o canal privado; o stdout do processo
# passa a ser o stderr.
_protocolo = os.fdopen(os.dup(1), "w", encoding="utf-8", newline="\n")
os.dup2(2, 1)

VERSAO = "1"

#: O GGUF que a Fase 7 mediu. Q5_K_M, 0,70 GB.
#:
#: O nome é fixo e não escolhível, ao contrário do pipeline de diarização:
#: trocar a quantização muda o texto e a separação ao mesmo tempo, e não haveria
#: como saber qual das duas mudou. Quando houver uma segunda opção, ela entra
#: como escolha explícita, medida — não como arquivo que alguém largou na pasta.
ARQUIVO_GGUF = "MOSS-Transcribe-Diarize-Q5_K_M.gguf"
REPO_GGUF = "handy-computer/moss-transcribe-diarize-gguf"

# O modelo ao lado deste arquivo, como os pesos de diarização (docs/FASE4.md §4).
# Estar aqui é o que faz a primeira transcrição de uma instalação nova não
# depender de rede nem de token; o caminho do HuggingFace fica para a máquina de
# quem desenvolve e ainda não rodou o empacotador.
_LOCAIS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "modelos")


def _enviar(**campos) -> None:
    _protocolo.write(json.dumps(campos, ensure_ascii=False) + "\n")
    _protocolo.flush()


def _log(texto: str) -> None:
    print(f"[moss] {texto}", file=sys.stderr, flush=True)


def _gguf_local() -> str | None:
    """O GGUF embarcado, ou ``None`` quando o empacotador ainda não rodou."""
    caminho = os.path.join(_LOCAIS, ARQUIVO_GGUF)
    return caminho if os.path.isfile(caminho) else None


def _e_truncado(e: BaseException) -> bool:
    """Se a exceção é o estouro do teto de geração do ``transcribe.cpp``.

    Comparada pelo **nome da classe**, e não por ``isinstance``: a
    ``OutputTruncated`` mora no pacote nativo, que pode não estar importável no
    momento em que o erro chega (é ele mesmo que está falhando), e um
    ``ImportError`` dentro do tratador de erro trocaria uma falha legível por
    uma ilegível.
    """
    return type(e).__name__ == "OutputTruncated"


class Modelo:
    """O MOSS em GGUF, carregado sob demanda e mantido quente.

    Quente entre requisições porque o regime deste motor é **um bloco de 3
    minutos por requisição** (docs/FASE7-BACKEND.md B2): carregar o modelo a
    cada bloco pagaria ~1,3 s por bloco à toa, e numa reunião de duas horas
    são 40 blocos.
    """

    def __init__(self) -> None:
        self._modelo = None
        self.dispositivo = "?"
        self.motivo: str | None = None

    def carregar(self, id_req: int) -> None:
        if self._modelo is not None:
            return

        _enviar(id=id_req, tipo="progresso", pct=0.0, texto="carregando o modelo")
        import transcribe_cpp as t

        caminho = _gguf_local()
        if caminho:
            _log(f"GGUF local: {caminho}")
        else:
            from huggingface_hub import hf_hub_download

            _log(f"GGUF do HuggingFace: {REPO_GGUF}/{ARQUIVO_GGUF}")
            caminho = hf_hub_download(REPO_GGUF, ARQUIVO_GGUF)

        # CUDA primeiro, CPU como último recurso — e **dizendo qual foi**.
        # Rodar em CPU não é um modo deste app: é o que acontece quando o
        # backend nativo com CUDA não está registrado, e a diferença entre as
        # duas coisas precisa chegar ao núcleo, que decide se segue (a chave
        # "Transcrever sem placa") ou para. É o mesmo desenho do motor de ASR,
        # onde a queda silenciosa para CPU comeu a RAM de um usuário por horas.
        try:
            self._modelo = t.Model(caminho, backend="cuda")
            self.dispositivo = "cuda"
        except Exception as e:
            # O wheel do PyPI instala um stub 0.0.0 que **não registra o backend
            # de CUDA**; o nativo vem do release do GitHub (docs/FASE7-BACKEND.md
            # A1). É o modo de falha mais provável desta linha, e ele é mudo.
            self.motivo = (f"o backend de CUDA do transcribe.cpp não subiu: {e!r}"
                           " — o wheel nativo pode ser o stub do PyPI")
            _log(f"SEM CUDA: {self.motivo}")
            self._modelo = t.Model(caminho, backend="cpu")
            self.dispositivo = "cpu"

        _log(f"{ARQUIVO_GGUF} carregado em {self.dispositivo}")

    def transcrever(self, caminho: str, id_req: int,
                    inicio: float | None, fim: float | None) -> dict:
        self.carregar(id_req)
        import transcribe_cpp as t

        pcm, taxa = _ler_wav(caminho, inicio, fim)
        _enviar(id=id_req, tipo="progresso", pct=0.1, texto="transcrevendo")

        try:
            # **Sem `language`.** O build GGUF declara `languages = ('en','zh')`
            # e recusa `pt` com UnsupportedRequest — mas transcreve português
            # corretamente quando o parâmetro é omitido: a lista de capacidades
            # do port está subdeclarada, o modelo não está. Passar o idioma aqui
            # derruba todas as transcrições em português deste app.
            #
            # `diarize="on"` é a razão de o motor existir, e
            # `timestamps="segment"` é o que a medição usou — não há alinhamento
            # por palavra, e o núcleo trata a ausência como "não dá para cortar"
            # (docs/SIDECAR.md).
            r = t.transcribe(self._modelo, pcm, diarize="on", timestamps="segment")
        except Exception as e:
            if not _e_truncado(e):
                raise
            # O modelo já estourou o teto de geração uma vez, no acervo, e a
            # biblioteca **levanta exceção** em vez de devolver o que gerou.
            # Num sidecar sem este tratamento isso derrubaria a requisição com
            # uma mensagem do ggml; aqui vira um erro de bloco, que a
            # orquestração decide o que fazer com (hoje: registra e segue, para
            # um bloco não custar a reunião).
            raise RuntimeError(
                "o MOSS estourou o teto de geração neste bloco "
                f"({(fim or 0) - (inicio or 0):.0f} s) e não devolveu texto"
            ) from e

        segmentos = [
            {"inicio": s.t0_ms / 1000, "fim": s.t1_ms / 1000,
             # O rótulo sai cru, como o modelo o produziu (`S0`, `S1`), e é
             # **local ao bloco**: o S1 daqui não é o S1 do vizinho. Quem os
             # costura em identidades é o núcleo, por vetor de voz
             # (Nucleo/CosturaDeFalantes.cs). Traduzir aqui seria mentir.
             "falante": f"S{s.speaker_id}",
             # O espaço à esquerda, como no motor de ASR: aparar é apresentação,
             # e concatenar segmentos sem ele juntaria palavras.
             "texto": " " + s.text.strip()}
            for s in r.segments
        ]
        return {"segmentos": segmentos, "duracao": len(pcm) / taxa,
                "dispositivo": self.dispositivo, "motivo": self.motivo}


def _ler_wav(caminho: str, inicio: float | None, fim: float | None):
    """O trecho pedido do WAV, já como float32 em [-1, 1].

    **A janela vem do cliente, e o arquivo é sempre o mix inteiro.** O núcleo
    corta a reunião em blocos de 3 minutos (docs/FASE7-BACKEND.md B2) porque a
    passada inteira não escala nesta placa — foi interrompida depois de 1h36
    numa gravação de 32 min (§7.2). Mandar cada bloco como arquivo próprio
    escreveria 20 WAVs temporários numa reunião de uma hora; mandar o mesmo
    arquivo com ``inicio``/``fim`` não escreve nenhum, e o que o modelo recebe é
    byte a byte o mesmo PCM que o ``tools/medir_moss.py`` mediu.

    Sem a janela, lê o arquivo inteiro — que é a forma documentada em
    ``docs/SIDECAR.md`` e a que uma ferramenta de medição usaria.
    """
    import numpy as np
    import wave

    with wave.open(caminho, "rb") as w:
        if w.getsampwidth() != 2 or w.getnchannels() != 1:
            raise RuntimeError(
                f"esperado WAV mono de 16 bits, veio {w.getnchannels()} canais "
                f"de {8 * w.getsampwidth()} bits")
        taxa = w.getframerate()
        total = w.getnframes()

        a = 0 if inicio is None else max(0, min(total, int(inicio * taxa)))
        b = total if fim is None else max(a, min(total, int(fim * taxa)))
        w.setpos(a)
        bruto = w.readframes(b - a)

    if b <= a:
        raise RuntimeError(f"janela vazia: {inicio}–{fim} s em {taxa} Hz")
    return np.frombuffer(bruto, dtype=np.int16).astype(np.float32) / 32768.0, taxa


def main() -> int:
    modelo = Modelo()
    _enviar(tipo="pronto", motor="moss", versao=VERSAO)

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
            if op != "transcrever_e_separar":
                raise RuntimeError(f"operação desconhecida: {op!r}")

            caminho = req.get("audio") or ""
            if not os.path.isfile(caminho):
                raise RuntimeError(f"áudio não encontrado: {caminho}")

            r = modelo.transcrever(caminho, id_req, req.get("inicio"), req.get("fim"))
            _enviar(id=id_req, tipo="resultado", **r)

        except Exception as e:
            # Erro encerra a **requisição**, não o motor: o processo continua
            # vivo e pronto para o próximo bloco. É o que faz um bloco truncado
            # custar um bloco, e não a reunião inteira. Ver docs/SIDECAR.md.
            _log(f"falha na requisição {id_req}: {e!r}")
            _enviar(id=id_req, tipo="erro", mensagem=str(e))

    return 0


if __name__ == "__main__":
    sys.exit(main())
