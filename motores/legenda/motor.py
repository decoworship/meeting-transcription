#!/usr/bin/env python3
"""A legenda ao vivo: ASR em streaming, texto sub-segundo durante a reunião.

**O que ele é, e o que ele não é.** Este motor faz **só texto**, e faz enquanto a
pessoa ainda está falando — 0,11 s de mediana entre a palavra sair da boca e
firmar na tela, medido em 60 minutos contínuos
(``docs/FASE7-ROTA.md`` §4). Ele **não** separa falantes, e isso é desenho, não
falta: ao vivo o app afirma só o que sabe com certeza, e o que ele sabe é o que
veio pela faixa do microfone — decidido pelo núcleo, sem custar GPU.

**Ele não substitui a transcrição.** A passada final roda como sempre rodou,
depois da reunião, e é ela que vale. Esta é a camada 1 das três da
``docs/FASE7-ROTA.md`` §3: rascunho que se reescreve.

Protocolo (``docs/SIDECAR.md``), com uma extensão: **a requisição fica aberta**.

.. code-block:: text

    →  {"id":1,"op":"legendar","idioma":"pt-BR"}
    →  {"id":1,"op":"audio","pcm":"<int16 little-endian em base64>"}   (repetido)
    ←  {"id":1,"tipo":"progresso","firme":"…","tentativo":"…","ate_ms":N}
    →  {"id":1,"op":"encerrar"}
    ←  {"id":1,"tipo":"resultado","texto":"…","duracao":N}

**Por que o áudio vem do núcleo e não é lido daqui.** Seria mais simples este
processo abrir o ``system.wav`` e segui-lo crescendo. Mas ler um WAV **que está
sendo gravado** no Windows depende de semântica de compartilhamento que custou
uma medição para acertar — ``File.OpenRead`` falha e só ``FileShare.ReadWrite``
abre (``Nucleo/Faixas.cs``, o ``T0.5``). Esse conhecimento está no C# e foi
verificado lá; replicá-lo em Python seria refazer a aposta. **Quem já sabe ler,
lê; este motor só recebe.**

**E é o núcleo que mistura as duas faixas**, pela mesma razão de sempre: o
``Faixas.Mix`` soma e normaliza de um jeito que a passada final também usa, e
duas implementações da mesma conta divergem.

Regras que não podem ser esquecidas, e todas já custaram tempo neste projeto:

* **duplicar o fd 1 antes de qualquer import.** O ggml escreve no stdout por
  conta própria, e uma linha dele corrompe o protocolo com um sintoma que não
  aponta para a causa;
* **``commit_policy="stable_prefix"``** é o LocalAgreement, e é o que separa o
  texto firme do volátil. Sem ele a tela treme;
* **o modelo fica quente** enquanto a sessão vive. Recarregar por quadro pagaria
  a carga do GGUF cinco vezes por segundo.
"""

import os
import sys

# ANTES de qualquer import pesado, e pela mesma razão dos outros motores.
_protocolo = os.fdopen(os.dup(1), "w", encoding="utf-8", newline="\n")
os.dup2(2, 1)

import base64          # noqa: E402
import json            # noqa: E402
import time            # noqa: E402

VERSAO = "1"

#: O modelo medido no ``R1``. Streaming de verdade, ``pt-BR`` declarado.
ARQUIVO_GGUF = "nemotron-3.5-asr-streaming-0.6b-Q8_0.gguf"
REPO_GGUF = "handy-computer/nemotron-3.5-asr-streaming-0.6b-gguf"

#: A cada quantos quadros mandar um parcial, quando nada firmou. Serve para a
#: tela saber que o motor está vivo durante um silêncio longo, sem inundá-la.
SINAL_DE_VIDA = 25


def _enviar(**campos) -> None:
    _protocolo.write(json.dumps(campos, ensure_ascii=False) + "\n")
    _protocolo.flush()


def _log(texto: str) -> None:
    print(texto, file=sys.stderr, flush=True)


def _gguf_local() -> str | None:
    aqui = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                        "modelos", ARQUIVO_GGUF)
    return aqui if os.path.exists(aqui) else None


class Legendador:
    """O modelo, quente, e a sessão de streaming aberta sobre ele."""

    def __init__(self) -> None:
        self._modelo = None
        self._sessao = None
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

        # CUDA primeiro, CPU como último recurso — **e dizendo qual foi**. Cair
        # para CPU não é um modo deste app: é o que acontece quando o backend
        # nativo não está registrado, e a diferença precisa chegar ao núcleo.
        # Aqui ela importa mais que nos outros motores: em CPU o Nemotron mediu
        # 0,99× o tempo real (docs/FASE7-RESULTADOS.md §9.1), ou seja, **a
        # legenda não acompanharia a reunião**.
        try:
            self._modelo = t.Model(caminho, backend="cuda")
            self.dispositivo = "cuda"
        except Exception as e:
            self.motivo = (f"o backend de CUDA do transcribe.cpp não subiu: {e!r}"
                           " — em CPU a legenda não acompanha o tempo real")
            _log(f"SEM CUDA: {self.motivo}")
            self._modelo = t.Model(caminho, backend="cpu")
            self.dispositivo = "cpu"

        caps = self._modelo.capabilities
        if not caps.supports_streaming:
            raise RuntimeError(
                f"{ARQUIVO_GGUF} não declara supports_streaming — não serve de legenda")

        _log(f"{ARQUIVO_GGUF} carregado em {self.dispositivo}")

    def abrir(self, id_req: int, idioma: str | None) -> None:
        self.carregar(id_req)
        kw = {"commit_policy": "stable_prefix", "timestamps": "none"}
        if idioma:
            kw["language"] = idioma
        self._sessao = self._modelo.session().stream(**kw)
        _enviar(id=id_req, tipo="progresso", pct=1.0, texto="pronto",
                dispositivo=self.dispositivo, motivo=self.motivo)

    def alimentar(self, pcm) -> object:
        return self._sessao.feed(pcm)

    def encerrar(self):
        if self._sessao is None:
            return ""
        final = self._sessao.finalize()          # noqa: F841 — fecha o prefixo
        texto = self._sessao.text()
        try:
            self._sessao.reset()
        except Exception:
            pass
        self._sessao = None
        return texto.committed


def _pcm_de(b64: str):
    """base64 de int16 little-endian → float32 em [-1, 1], como o motor quer."""
    import numpy as np

    cru = np.frombuffer(base64.b64decode(b64), dtype="<i2")
    return (cru.astype(np.float32) / 32768.0)


def main() -> int:
    L = Legendador()
    aberto: int | None = None
    quadros = 0
    amostras = 0
    t0 = 0.0

    _enviar(tipo="pronto", versao=VERSAO)

    for linha in sys.stdin:
        linha = linha.strip()
        if not linha:
            continue
        try:
            req = json.loads(linha)
        except json.JSONDecodeError:
            continue

        id_req = req.get("id", 0)
        op = req.get("op")

        try:
            if op == "legendar":
                L.abrir(id_req, req.get("idioma"))
                aberto, quadros, amostras, t0 = id_req, 0, 0, time.perf_counter()

            elif op == "audio":
                if aberto is None:
                    _enviar(id=id_req, tipo="erro", erro="não há sessão aberta")
                    continue
                pcm = _pcm_de(req["pcm"])
                amostras += len(pcm)
                quadros += 1
                u = L.alimentar(pcm)
                # **Só quando muda, mais um sinal de vida.** Mandar a cada quadro
                # seria cinco mensagens por segundo para a tela redesenhar; não
                # mandar nunca faria um silêncio longo parecer travamento.
                if (getattr(u, "committed_changed", False)
                        or getattr(u, "tentative_changed", False)
                        or quadros % SINAL_DE_VIDA == 0):
                    txt = L._sessao.text()
                    _enviar(id=aberto, tipo="progresso",
                            firme=txt.committed, tentativo=txt.tentative,
                            ate_ms=int(getattr(u, "audio_committed_ms", 0)))

            elif op == "encerrar":
                texto = L.encerrar()
                d = time.perf_counter() - t0
                _log(f"legenda encerrada: {quadros} quadros, "
                     f"{amostras/16000:.0f}s de áudio em {d:.0f}s")
                _enviar(id=id_req, tipo="resultado", texto=texto,
                        duracao=amostras / 16000.0, dispositivo=L.dispositivo)
                aberto = None

            else:
                _enviar(id=id_req, tipo="erro", erro=f"op desconhecida: {op!r}")

        except Exception as e:
            # **Um erro encerra a requisição, não o motor.** A legenda é um
            # extra; o que não pode acontecer é este processo morrer e levar
            # junto a impressão de que a gravação parou.
            _log(f"erro em {op!r}: {e!r}")
            _enviar(id=id_req, tipo="erro", erro=f"{type(e).__name__}: {e}")
            if op == "legendar":
                aberto = None

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
