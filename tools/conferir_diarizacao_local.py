#!/usr/bin/env python3
"""Confere que a diarização por pasta local dá o mesmo que a por HuggingFace.

É o critério E da Fase 4 ([docs/FASE4.md](../docs/FASE4.md) §9), e ele é o que
autoriza — ou reprova — tirar o token do HuggingFace do binário.

A pergunta não é "roda?". É "roda **igual**?". Trocar a origem dos pesos não
pode mexer em quem falou quando: os vetores de voz já aprendidos vivem no espaço
do modelo de hoje, e uma diferença aqui os invalidaria em silêncio, sem nenhum
erro na tela.

Como ele mede: sobe o motor de diarização duas vezes sobre o **mesmo**
``system.wav`` — uma com a pasta local, outra forçando o caminho do HuggingFace
— e compara segmento a segmento.

Uso::

    tools/conferir_diarizacao_local.py <pasta-da-gravacao>
    tools/conferir_diarizacao_local.py <pasta> --python /caminho/python.exe
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import time
from pathlib import Path

PYTHON_PADRAO = "/mnt/c/Users/andre/MeetingApp/motores/python/python.exe"
MOTOR_PADRAO = "/mnt/c/Users/andre/MeetingApp/motores/diarizacao/motor.py"

# Quanto os instantes podem divergir e ainda contar como o mesmo turno. Não é
# tolerância a erro: é a granularidade da própria janela do pyannote. Zero exigiria
# determinismo bit a bit numa GPU, que ninguém promete.
TOLERANCIA_S = 0.05


def _para_windows(caminho: str) -> str:
    """O caminho como o python.exe do Windows o entende."""
    if not caminho.startswith("/mnt/"):
        return caminho
    saida = subprocess.run(["wslpath", "-w", caminho], capture_output=True, text=True)
    return saida.stdout.strip() or caminho


def diarizar(python: str, motor: str, audio: str, *, local: bool) -> list[dict]:
    """Roda o motor uma vez e devolve os segmentos crus.

    ``local=False`` esconde a pasta de modelos do motor apontando ``_LOCAIS``
    para um lugar que não existe — é como se pede o caminho antigo sem manter
    duas versões do motor.
    """
    ambiente = dict(os.environ)
    ambiente["PYANNOTE_METRICS_ENABLED"] = "false"

    # O motor decide pela existência da pasta, e quem a esconde é o main. O que
    # cabe aqui é o token: sem ele o caminho remoto nem sai do lugar, porque o
    # community-1 tem portão. Quem injeta no app é o C# (Motores.Ambiente); aqui
    # ele é lido do mesmo arquivo que o publicar.sh usa.
    if not local and "HF_TOKEN" not in ambiente:
        arquivo = Path("/mnt/c/Users/andre/.meeting-recorder/hf_token.txt")
        if arquivo.is_file():
            ambiente["HF_TOKEN"] = arquivo.read_text().strip()

    # **WSLENV, senão nada disto chega lá.** Variável definida no WSL não cruza
    # para um processo Windows a menos que esteja listada aqui — o env= do
    # Popen monta o ambiente do lado Linux, e o interop só repassa o que WSLENV
    # nomeia. Sem esta linha o motor sobe sem HF_TOKEN e o erro que aparece
    # culpa o token, não o encanamento.
    #
    # É armadilha desta ferramenta, e só dela: no app instalado quem inicia o
    # sidecar é um processo Windows, e aí o ambiente passa inteiro.
    ambiente["WSLENV"] = "HF_TOKEN:PYANNOTE_METRICS_ENABLED"

    proc = subprocess.Popen(
        [python, _para_windows(motor)],
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        text=True, encoding="utf-8", env=ambiente, bufsize=1,
    )

    origem = "?"
    try:
        pronto = proc.stdout.readline()
        if not pronto:
            raise RuntimeError(f"o motor morreu ao subir: {proc.stderr.read()[-2000:]}")

        proc.stdin.write(json.dumps(
            {"id": 1, "op": "diarizar", "audio": _para_windows(audio)}) + "\n")
        proc.stdin.flush()

        while True:
            linha = proc.stdout.readline()
            if not linha:
                raise RuntimeError(f"o motor morreu: {proc.stderr.read()[-2000:]}")
            msg = json.loads(linha)
            if msg.get("tipo") == "progresso":
                print(f"      {msg.get('pct', 0):.0%} {msg.get('texto', '')}",
                      end="\r", file=sys.stderr)
                continue
            if msg.get("tipo") == "erro":
                raise RuntimeError(msg.get("mensagem", "erro sem mensagem"))
            print(" " * 60, end="\r", file=sys.stderr)
            return msg["segmentos"]
    finally:
        try:
            proc.stdin.close()
            proc.wait(timeout=10)
        except Exception:
            proc.kill()
        del origem


def comparar(a: list[dict], b: list[dict]) -> tuple[bool, list[str]]:
    """Iguais? E, se não, o que difere — dito em linhas legíveis."""
    queixas: list[str] = []

    if len(a) != len(b):
        queixas.append(f"contagem de segmentos: {len(a)} local × {len(b)} remoto")

    # A comparação é posicional de propósito. Os dois vêm do mesmo pipeline
    # determinístico sobre o mesmo áudio; se a ordem mudou, isso já é a
    # divergência que se está procurando.
    for i, (x, y) in enumerate(zip(a, b)):
        if abs(x["inicio"] - y["inicio"]) > TOLERANCIA_S:
            queixas.append(f"segmento {i}: início {x['inicio']:.2f} × {y['inicio']:.2f}")
        if abs(x["fim"] - y["fim"]) > TOLERANCIA_S:
            queixas.append(f"segmento {i}: fim {x['fim']:.2f} × {y['fim']:.2f}")
        if x["falante"] != y["falante"]:
            queixas.append(f"segmento {i} ({x['inicio']:.1f}s): "
                           f"falante {x['falante']} × {y['falante']}")

    # Vinte linhas de queixa não ajudam mais que cinco: o que importa é se
    # divergiu, e um exemplo de como.
    return not queixas, queixas[:10]


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("gravacao", help="a pasta da gravação, com system.wav dentro")
    ap.add_argument("--python", default=PYTHON_PADRAO)
    ap.add_argument("--motor", default=MOTOR_PADRAO)
    args = ap.parse_args()

    audio = Path(args.gravacao) / "system.wav"
    if not audio.is_file():
        print(f"não achei {audio}", file=sys.stderr)
        return 2

    modelos = Path(args.motor).parent / "modelos"
    if not (modelos / "community-1" / "config.yaml").is_file():
        print(f"não achei o pipeline local em {modelos}.\n"
              "Rode tools/empacotar_modelos_de_diarizacao.sh antes.", file=sys.stderr)
        return 2

    print(f"áudio: {audio}")
    print(f"pesos locais: {modelos}")

    print("\n==> 1/2 — com a pasta local")
    t0 = time.time()
    local = diarizar(args.python, args.motor, str(audio), local=True)
    print(f"    {len(local)} segmentos em {time.time() - t0:.0f}s")

    print("\n==> 2/2 — pelo HuggingFace (a pasta local sai de cena por um minuto)")
    escondido = modelos.with_name("modelos.escondido")
    modelos.rename(escondido)
    try:
        t0 = time.time()
        remoto = diarizar(args.python, args.motor, str(audio), local=False)
        print(f"    {len(remoto)} segmentos em {time.time() - t0:.0f}s")
    finally:
        # Sempre, inclusive se o motor morreu no meio: deixar a instalação sem os
        # pesos seria um estrago maior que o teste que falhou.
        escondido.rename(modelos)

    igual, queixas = comparar(local, remoto)

    print("\n" + "=" * 60)
    if igual:
        print("IGUAL — os mesmos falantes nos mesmos instantes.")
        print("O critério E passa: o token pode sair do binário.")
        return 0

    print("DIVERGIU. O critério E reprova; o token fica.")
    for q in queixas:
        print(f"  {q}")
    return 1


if __name__ == "__main__":
    sys.exit(main())
