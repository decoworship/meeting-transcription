"""Motor MOSS falso, para exercitar a orquestração em blocos sem GPU nenhuma.

Irmão do ``motor_de_teste.py`` e pelo mesmo motivo: o que se quer verificar aqui
é o **recorte em blocos, o deslocamento dos carimbos e o rótulo local** — tudo
aritmética do núcleo —, e amarrar isso a um GGUF de 0,70 GB numa placa trocaria
uma verificação rápida e determinística por uma lenta que falha por motivos
alheios. O motor de verdade está em ``motores/moss/motor.py``, e o que se
verifica dele é o protocolo, com este mesmo arquivo de fora.

Cada resposta devolve **dois** segmentos por janela pedida, com os carimbos
relativos à janela — que é como o modelo real responde, e é justamente o que o
``MossEmBlocos`` tem de deslocar.

O modo vem em ``MOSS_TESTE_MODO``, e **não** como argumento, ao contrário do
``motor_de_teste.py``: quem sobe este motor é o ``MossEmBlocos``, que monta a
linha de comando sozinho a partir do ``Motores.ScriptMoss``. O ambiente é o
único canal que o teste controla — e é o mesmo canal por onde o app já desliga a
telemetria do pyannote.
"""

import json
import os
import sys
import time

_protocolo = os.fdopen(os.dup(1), "w", encoding="utf-8", newline="\n")
os.dup2(2, 1)

modo = os.environ.get("MOSS_TESTE_MODO", "feliz")


def enviar(**campos):
    _protocolo.write(json.dumps(campos, ensure_ascii=False) + "\n")
    _protocolo.flush()


enviar(tipo="pronto", motor="moss", versao="1")

bloco = 0
for linha in sys.stdin:
    linha = linha.strip()
    if not linha:
        continue
    req = json.loads(linha)
    id_req = req.get("id")

    if modo == "demorado":
        # Longo o bastante para o cancelamento chegar no meio: cancelar é matar
        # o processo, e é isso que se está medindo (docs/SIDECAR.md).
        time.sleep(60)

    if modo == "cpu":
        # O caso que o núcleo tem de pegar no PRIMEIRO bloco: o backend nativo
        # com CUDA não subiu e o modelo caiu para a CPU. Descobrir isso no fim
        # custa a tarde.
        enviar(id=id_req, tipo="resultado", duracao=1.0, dispositivo="cpu",
               motivo="o backend de CUDA do transcribe.cpp não subiu",
               segmentos=[{"inicio": 0.0, "fim": 1.0, "texto": " oi", "falante": "S0"}])
        continue

    if modo == "erra-o-segundo" and bloco == 1:
        # Um bloco custa um bloco, não a reunião: é o teto de geração estourado,
        # que a biblioteca real levanta como exceção.
        bloco += 1
        enviar(id=id_req, tipo="erro", mensagem="o MOSS estourou o teto de geração")
        continue

    inicio = req.get("inicio") or 0.0
    fim = req.get("fim") or 0.0

    # Dois falantes por bloco, com o rótulo REINICIANDO a cada janela — é a
    # propriedade que obriga a costura a existir: o S0 daqui não é o S0 do
    # vizinho, e o modelo não tem como saber que são.
    enviar(id=id_req, tipo="resultado", duracao=fim - inicio, dispositivo="cuda",
           segmentos=[
               {"inicio": 0.5, "fim": 1.5, "texto": f" bloco {bloco} um", "falante": "S0"},
               {"inicio": 2.0, "fim": 3.0, "texto": f" bloco {bloco} dois", "falante": "S1"},
           ])
    bloco += 1
