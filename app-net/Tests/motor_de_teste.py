"""Motor falso, para exercitar o contrato do sidecar sem carregar o pyannote.

Cada modo reproduz uma forma de o motor real se comportar — inclusive as feias.
O modo vem como primeiro argumento. Ver docs/SIDECAR.md.
"""

import json
import os
import sys
import time

_protocolo = os.fdopen(os.dup(1), "w", encoding="utf-8", newline="\n")
os.dup2(2, 1)

modo = sys.argv[1] if len(sys.argv) > 1 else "feliz"


def enviar(**campos):
    _protocolo.write(json.dumps(campos, ensure_ascii=False) + "\n")
    _protocolo.flush()


if modo == "morre-no-handshake":
    print("faltou o modelo", file=sys.stderr, flush=True)
    sys.exit(1)

if modo == "lixo-no-stdout":
    # Exatamente o que torch e pyannote fazem: escrever no stdout sem pedir.
    _protocolo.write("Downloading model: 42%\n")
    _protocolo.flush()

enviar(tipo="pronto", motor="teste", versao="1")

for linha in sys.stdin:
    linha = linha.strip()
    if not linha:
        continue
    req = json.loads(linha)
    id_req = req.get("id")

    if modo == "erro":
        enviar(id=id_req, tipo="erro", mensagem="não foi possível ler o áudio")
        continue

    if modo == "morre-no-meio":
        enviar(id=id_req, tipo="progresso", pct=0.3, texto="analisando falantes")
        os._exit(9)

    if modo == "demorado":
        # Longo o bastante para o cancelamento chegar no meio.
        time.sleep(60)

    enviar(id=id_req, tipo="progresso", pct=0.3, texto="analisando falantes")
    enviar(id=id_req, tipo="resultado", segmentos=[
        {"inicio": 0.5, "fim": 3.25, "falante": "SPEAKER_00"},
        {"inicio": 3.25, "fim": 7.0, "falante": "SPEAKER_01"},
    ])
