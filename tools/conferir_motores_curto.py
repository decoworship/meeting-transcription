#!/usr/bin/env python3
"""Os motores ainda funcionam depois de eu tirar aquela DLL?

É a régua do emagrecimento do payload (docs/FASE4.md §5). Cortar uma DLL de
CUDA é barato; descobrir que ela fazia falta só na próxima reunião de um amigo
é caríssimo. Esta ferramenta fecha esse laço em pouco mais de um minuto.

**Por que um trecho curto.** A verificação honesta seria transcrever uma reunião
inteira, e ela custa 15 minutos de GPU por corte — o que na prática significa
não verificar. Sessenta segundos de áudio real exercitam exatamente o mesmo
caminho de código: carregar o modelo, alocar na placa, rodar as convoluções,
devolver segmentos. O que um trecho curto **não** pega é degradação de
qualidade ao longo do tempo, e não é disso que se trata aqui: uma DLL ausente
não piora a transcrição, ela impede o processo de subir.

O que ele afirma, e é o suficiente:

  - o motor de ASR sobe, **acha a GPU**, e devolve texto;
  - o motor de diarização sobe, acha a GPU, e devolve falantes.

O "acha a GPU" é metade da régua. Sem ela, um corte errado não quebra nada —
o torch cai para CPU em silêncio, tudo "funciona", e o app fica vinte vezes mais
lento na máquina de quem instalou.

Uso::

    tools/conferir_motores_curto.py                       # usa a gravação mais recente
    tools/conferir_motores_curto.py --gravacao <pasta>
    tools/conferir_motores_curto.py --sem <dll> [--sem <outra>]   # esconde e restaura
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import time
import wave
from pathlib import Path

RAIZ_MOTORES = Path("/mnt/c/Users/andre/MeetingApp/motores")
GRAVACOES = Path("/mnt/c/Users/andre/Documents/MeetingRecordings")
SEGUNDOS = 60


def _win(caminho: str | Path) -> str:
    p = str(caminho)
    if not p.startswith("/mnt/"):
        return p
    r = subprocess.run(["wslpath", "-w", p], capture_output=True, text=True)
    return r.stdout.strip() or p


def recortar(origem: Path, destino: Path, segundos: int) -> None:
    """Os primeiros N segundos, no mesmo formato — 16 kHz mono 16 bits."""
    with wave.open(str(origem), "rb") as entrada:
        quadros = entrada.readframes(entrada.getframerate() * segundos)
        with wave.open(str(destino), "wb") as saida:
            saida.setnchannels(entrada.getnchannels())
            saida.setsampwidth(entrada.getsampwidth())
            saida.setframerate(entrada.getframerate())
            saida.writeframes(quadros)


def falar_com_motor(script: Path, pedido: dict) -> tuple[dict, str]:
    """Sobe o motor, manda um pedido, devolve a resposta e o que ele logou."""
    ambiente = dict(os.environ)
    ambiente["PYANNOTE_METRICS_ENABLED"] = "false"
    # Sem WSLENV a variável não cruza para o processo Windows — a mesma
    # armadilha documentada em conferir_diarizacao_local.py.
    ambiente["WSLENV"] = "PYANNOTE_METRICS_ENABLED"

    proc = subprocess.Popen(
        [str(RAIZ_MOTORES / "python" / "python.exe"), _win(script)],
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        # errors="replace" no stderr: o torch e o pyannote logam mensagens do
        # Windows em cp1252, e um único byte 0xE7 ("ç") derruba a leitura com
        # UnicodeDecodeError — matando a ferramenta depois de o motor já ter
        # respondido certo. O canal do protocolo é UTF-8 puro e não é afetado.
        text=True, encoding="utf-8", errors="replace", env=ambiente, bufsize=1,
    )
    try:
        if not proc.stdout.readline():
            return {"tipo": "erro", "mensagem": "o motor morreu ao subir"}, \
                   proc.stderr.read()[-3000:]

        proc.stdin.write(json.dumps(pedido) + "\n")
        proc.stdin.flush()

        while True:
            linha = proc.stdout.readline()
            if not linha:
                return {"tipo": "erro", "mensagem": "o motor morreu no meio"}, \
                       proc.stderr.read()[-3000:]
            msg = json.loads(linha)
            if msg.get("tipo") == "progresso":
                continue
            proc.stdin.close()
            # O stderr é onde os motores logam o dispositivo escolhido, e é de
            # lá que sai a metade "achou a GPU" da régua.
            try:
                proc.wait(timeout=15)
            except subprocess.TimeoutExpired:
                proc.kill()
            return msg, proc.stderr.read()
    finally:
        if proc.poll() is None:
            proc.kill()


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--gravacao", type=Path)
    ap.add_argument("--segundos", type=int, default=SEGUNDOS)
    ap.add_argument("--sem", action="append", default=[],
                    help="DLL a esconder durante o teste (nome ou caminho relativo a motores/)")
    args = ap.parse_args()

    gravacao = args.gravacao
    if gravacao is None:
        candidatas = sorted((p for p in GRAVACOES.iterdir()
                             if (p / "system.wav").is_file()),
                            key=lambda p: p.name)
        if not candidatas:
            print("não achei gravação nenhuma", file=sys.stderr)
            return 2
        gravacao = candidatas[-1]

    print(f"gravação: {gravacao.name}")

    trabalho = Path("/mnt/c/Users/andre/AppData/Local/Temp/meetingapp-corte")
    trabalho.mkdir(parents=True, exist_ok=True)
    recortar(gravacao / "system.wav", trabalho / "curto.wav", args.segundos)
    print(f"trecho:   {args.segundos}s")

    # Esconder é renomear, e o restauro é garantido no finally: deixar a
    # instalação sem uma DLL porque um teste falhou seria um estrago maior que o
    # teste que falhou.
    escondidos: list[tuple[Path, Path]] = []
    for alvo in args.sem:
        achados = list(RAIZ_MOTORES.rglob(alvo)) if "/" not in alvo \
            else [RAIZ_MOTORES / alvo]
        for caminho in achados:
            if caminho.is_file():
                escondidos.append((caminho, caminho.with_suffix(caminho.suffix + ".escondido")))

    if escondidos:
        print("escondendo:")
        for origem, _ in escondidos:
            print(f"  {origem.relative_to(RAIZ_MOTORES)} "
                  f"({origem.stat().st_size / 1e6:.0f} MB)")

    try:
        for origem, fora in escondidos:
            origem.rename(fora)

        problemas: list[str] = []

        print("\n==> ASR")
        t0 = time.time()
        resposta, log = falar_com_motor(
            RAIZ_MOTORES / "asr" / "motor.py",
            {"id": 1, "op": "transcrever", "audio": _win(trabalho / "curto.wav")})
        if resposta.get("tipo") == "erro":
            problemas.append(f"ASR: {resposta.get('mensagem')}")
            print(f"    FALHOU: {resposta.get('mensagem')}")
            print("   ", log[-800:].replace("\n", "\n    "))
        else:
            segs = resposta.get("segmentos") or []
            print(f"    {len(segs)} segmentos em {time.time() - t0:.0f}s")
            if segs:
                print(f"    primeiro: {segs[0].get('texto', '')[:70]!r}")
            if not segs:
                problemas.append("ASR não devolveu segmento nenhum")
            if "cuda" not in log.lower():
                problemas.append("ASR não anunciou CUDA — caiu para CPU?")
            print(f"    dispositivo: {'cuda' if 'cuda' in log.lower() else 'CPU (!)'}")

        print("\n==> diarização")
        t0 = time.time()
        resposta, log = falar_com_motor(
            RAIZ_MOTORES / "diarizacao" / "motor.py",
            {"id": 1, "op": "diarizar", "audio": _win(trabalho / "curto.wav")})
        if resposta.get("tipo") == "erro":
            problemas.append(f"diarização: {resposta.get('mensagem')}")
            print(f"    FALHOU: {resposta.get('mensagem')}")
            print("   ", log[-800:].replace("\n", "\n    "))
        else:
            segs = resposta.get("segmentos") or []
            falantes = {s["falante"] for s in segs}
            print(f"    {len(segs)} turnos, {len(falantes)} falantes, "
                  f"em {time.time() - t0:.0f}s")
            if not segs:
                problemas.append("diarização não devolveu turno nenhum")
            if "cuda" not in log.lower():
                problemas.append("diarização não anunciou CUDA — caiu para CPU?")
            print(f"    dispositivo: {'cuda' if 'cuda' in log.lower() else 'CPU (!)'}")

    finally:
        for origem, fora in escondidos:
            if fora.exists():
                fora.rename(origem)
        if escondidos:
            print("\n(as DLLs escondidas foram restauradas)")

    print()
    if problemas:
        print("REPROVOU:")
        for p in problemas:
            print(f"  {p}")
        return 1

    print("PASSOU — os dois motores sobem, acham a GPU e produzem saída.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
