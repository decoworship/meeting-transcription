"""Motor de modelos como sidecar: baixar pacote do HuggingFace sob controle.

Implementa o contrato de ``docs/SIDECAR.md``. Lê requisições JSON do stdin,
responde progresso e resultado no canal do protocolo, e loga no stderr.

Por que este motor existe
-------------------------
O download já acontecia — só que invisível. O ``faster_whisper`` puxa ~3 GB do
HuggingFace na primeira transcrição que precisa do modelo, sem barra de
progresso, sem anunciar o tamanho e sem verificar o que chegou. Quem instala o
app numa máquina nova vê a primeira transcrição travar por minutos, sem
explicação. Este motor é a metade que faltava: o mesmo download, na hora que o
usuário escolher, com progresso na tela.

Por que em Python, e não em C#
------------------------------
O que o app precisa não é "baixar arquivos": é **produzir exatamente o layout
de cache que o faster_whisper e o pyannote vão procurar depois** — a árvore
``blobs``/``refs``/``snapshots`` com os links, que é formato interno da
biblioteca e muda quando ela quer. Reimplementá-lo em C# criaria um segundo
dono de um formato que não é nosso, e o sintoma de errar seria o pior possível:
o modelo baixa, a tela diz "instalado", e o motor baixa tudo de novo por não
reconhecer o que está lá. Chamando a ``huggingface_hub``, o layout é o dela por
construção.

Diferente dos outros dois motores, este **não fica quente**: não há modelo
carregado em memória, e cada requisição é independente.

Uso (é assim que o cliente C# o inicia)::

    python motor.py
"""

from __future__ import annotations

import json
import os
import sys

# ANTES de qualquer import pesado, pelo mesmo motivo dos outros motores: a
# huggingface_hub escreve barra de progresso no stdout sem pedir licença, e uma
# linha dessas no meio do fluxo corrompe o protocolo — com um sintoma que não
# aponta para a causa. O descritor 1 vira nosso canal privado; o stdout do
# processo passa a ser o stderr.
_protocolo = os.fdopen(os.dup(1), "w", encoding="utf-8", newline="\n")
os.dup2(2, 1)

VERSAO = "1"


def _enviar(**campos) -> None:
    _protocolo.write(json.dumps(campos, ensure_ascii=False) + "\n")
    _protocolo.flush()


def _log(texto: str) -> None:
    print(f"[modelos] {texto}", file=sys.stderr, flush=True)


def _bytes_em(pasta: str) -> int:
    """Quanto a pasta ocupa agora, contando os arquivos pela metade."""
    total = 0
    for raiz, _, arquivos in os.walk(pasta):
        for nome in arquivos:
            caminho = os.path.join(raiz, nome)
            try:
                # Os parciais do HuggingFace terminam em .incomplete e contam:
                # é justamente o que está chegando que mede o andamento.
                if not os.path.islink(caminho):
                    total += os.path.getsize(caminho)
            except OSError:
                pass
    return total


def _baixar(repositorio: str, id_req: int, pasta: str | None,
            esperado: int | None) -> dict:
    """Baixa um repositório inteiro para o cache, relatando andamento.

    O andamento é medido **pelo tamanho da pasta em disco**, e não por gancho na
    barra de progresso da biblioteca. Duas razões, uma de robustez e outra de
    honestidade:

    * o ``tqdm_class`` que a ``snapshot_download`` aceita alimenta só a barra
      externa, que conta *arquivos*. Num modelo em que um arquivo é 3 GB dos
      3,09 GB, essa barra fica parada em "1 de 6" o download inteiro. Chegar
      aos bytes exigiria remendar ``huggingface_hub.file_download``, que é
      interno e muda de versão sem aviso;
    * o tamanho esperado já existe no catálogo, do lado C#, e é o mesmo número
      que detecta pacote corrompido. Usá-lo aqui não inventa uma segunda fonte.

    Sem tamanho esperado, o download roda e o andamento vira indeterminado —
    degradar assim é melhor que mentir uma fração.
    """
    import threading

    from huggingface_hub import snapshot_download

    _enviar(id=id_req, tipo="progresso", pct=0.0, texto="conectando ao HuggingFace")

    parar = threading.Event()

    def vigiar() -> None:
        while not parar.wait(1.0):
            if not pasta or not esperado or not os.path.isdir(pasta):
                continue
            feito = _bytes_em(pasta)
            # Trava em 0,99: o 1,0 é do C#, depois de reler o disco. Uma barra
            # que enche antes do fim ensina a não confiar nela.
            fracao = min(feito / esperado, 0.99)
            _enviar(id=id_req, tipo="progresso", pct=fracao,
                    texto=f"{feito / 1e6:.0f} MB de {esperado / 1e6:.0f} MB")

    vigia = threading.Thread(target=vigiar, daemon=True)
    vigia.start()
    try:
        caminho = snapshot_download(
            repo_id=repositorio,
            token=os.environ.get("HF_TOKEN") or None,
        )
    finally:
        parar.set()

    _log(f"{repositorio} em {caminho}")
    return {"caminho": caminho}


def main() -> None:
    _enviar(tipo="pronto", motor="modelos", versao=VERSAO)
    _log("pronto")

    for linha in sys.stdin:
        linha = linha.strip()
        if not linha:
            continue

        try:
            req = json.loads(linha)
        except json.JSONDecodeError as e:
            _enviar(tipo="erro", mensagem=f"pedido ilegivel: {e}")
            continue

        id_req = req.get("id", 0)
        op = req.get("op")

        try:
            if op == "baixar":
                repositorio = req.get("repositorio")
                if not repositorio:
                    raise ValueError("faltou o repositorio")
                _baixar(repositorio, id_req, req.get("pasta"),
                        req.get("tamanho_esperado"))
                _enviar(id=id_req, tipo="resultado")
            else:
                _enviar(id=id_req, tipo="erro", mensagem=f"operacao desconhecida: {op}")
        except Exception as e:                       # noqa: BLE001
            # Qualquer falha vira erro legível para a tela: rede fora, termos do
            # modelo não aceitos, disco cheio. O motor continua vivo para a
            # próxima tentativa — é o que permite "tentar de novo" sem reabrir
            # o app.
            _log(f"falhou: {type(e).__name__}: {e}")
            _enviar(id=id_req, tipo="erro", mensagem=f"{type(e).__name__}: {e}")


if __name__ == "__main__":
    main()
