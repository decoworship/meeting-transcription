#!/usr/bin/env python3
"""A régua do porte da diarização para ONNX: ONNX contra torch, na mesma onda.

O porte (docs/DIARIZACAO-ONNX.md) troca torch+pyannote por onnxruntime+numpy
dentro do pipeline de diarização. A pergunta não é "roda?" — é "decide **o
mesmo**?". Uma diarização que erre quem falou quando não levanta exceção
nenhuma: ela só passa a atribuir a fala à pessoa errada, e o sintoma aparece na
ata, longe da causa.

Dois usos, e eles servem a réguas diferentes:

``--gravar-gabarito``
    Roda o pipeline **torch** sobre a gravação de 14,6 min e grava
    ``motores/diarizacao/pipeline/testes/gabarito_14min.json``. É o gabarito da
    régua `V1`, que o ``test_diarizacao.py`` compara centésimo a centésimo. Ele
    é gerado, e não versionado por acaso: refazê-lo é a única forma honesta de
    conferir que a máquina não mudou debaixo do teste.

``--gravacao <id> --motor {torch,onnx,ambos}``
    Roda uma gravação real e imprime o que ela decidiu. Com ``--contra-gemini``,
    confronta a contagem de falantes e o tempo de fala de cada um com o export
    do Gemini/Meet da mesma reunião (``gemini.md`` na pasta da gravação) — é a
    superfície da régua `V2`.

Uso::

    python tools/conferir_diarizacao_onnx.py --gravar-gabarito
    python tools/conferir_diarizacao_onnx.py --gravacao 2026-08-25_08-59-22 \\
        --motor ambos --contra-gemini
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import time
import wave
from pathlib import Path

import numpy as np

RAIZ = Path(__file__).resolve().parents[1]

MODELOS = Path(
    "/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores"
    "/diarizacao/modelos/community-1"
)
GRAVACOES = Path("/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings")

#: A gravação do gabarito: 14,6 min, três pessoas. Ver o cabeçalho do
#: test_diarizacao.py.
GRAVACAO_GABARITO = "2026-08-25_08-59-22"
GABARITO = RAIZ / "motores/diarizacao/pipeline/testes/gabarito_14min.json"

#: como o export do Gemini marca um turno: "**Fulano:** texto". As duas
#: expressões são as do `tools/comparar_com_gemini.py` — o mesmo arquivo lido
#: do mesmo jeito, para as duas ferramentas não discordarem sobre quem falou.
FALA = re.compile(r"^\*\*(?P<quem>[^*]{2,60}?):?\*\*:?\s*(?P<texto>.*)$")
CABECALHO = re.compile(r"^\s*(#|>|\*[^*])")


# ------------------------------------------------------------------- áudio

def ler_wav(caminho: Path) -> tuple[np.ndarray, int]:
    """A onda mono em float32 [-1, 1], como os dois pipelines a esperam."""
    with wave.open(str(caminho), "rb") as w:
        if w.getsampwidth() != 2:
            raise ValueError(f"{caminho} não é PCM 16 bits")
        taxa, canais = w.getframerate(), w.getnchannels()
        cru = w.readframes(w.getnframes())
    onda = np.frombuffer(cru, np.int16).astype(np.float32) / 32768.0
    if canais > 1:                                   # mono="downmix"
        onda = onda.reshape(-1, canais).mean(axis=1)
    return onda, taxa


# ------------------------------------------------------------------ motores

def diarizar_torch(caminho: Path) -> list[dict]:
    """O pipeline do pyannote, com torch. É ele que define o gabarito.

    Usa CUDA quando há — o gabarito é o mesmo, e na CPU custa dezenas de
    minutos.
    """
    import torch
    from pyannote.audio import Pipeline

    pipeline = Pipeline.from_pretrained(str(MODELOS))
    dispositivo = "cuda" if torch.cuda.is_available() else "cpu"
    pipeline.to(torch.device(dispositivo))
    print(f"  torch em {dispositivo}", file=sys.stderr, flush=True)

    saida = pipeline(str(caminho))
    # o pyannote 4 devolve um DiarizeOutput; o 3 devolvia a Annotation crua
    anotacao = getattr(saida, "speaker_diarization", saida)
    return [
        {"inicio": float(s.start), "fim": float(s.end), "falante": str(f)}
        for s, _, f in anotacao.itertracks(yield_label=True)
    ]


def diarizar_onnx(caminho: Path) -> list[dict]:
    """O pipeline portado, sem torch."""
    sys.path.insert(0, str(RAIZ / "motores/diarizacao/pipeline"))
    from diarizacao import Diarizador

    onda, taxa = ler_wav(caminho)
    return Diarizador(MODELOS)(onda, taxa)


MOTORES = {"torch": diarizar_torch, "onnx": diarizar_onnx}


# ------------------------------------------------------------------ medidas

def tempo_por_falante(trechos: list[dict]) -> dict[str, float]:
    tempos: dict[str, float] = {}
    for t in trechos:
        tempos[t["falante"]] = tempos.get(t["falante"], 0.0) + (t["fim"] - t["inicio"])
    return dict(sorted(tempos.items(), key=lambda kv: -kv[1]))


def grade(trechos: list[dict], duracao: float, passo: float = 0.01) -> np.ndarray:
    """Quem fala em cada centésimo de segundo — a mesma grade da `V1`."""
    g = np.full(int(duracao / passo), "", dtype=object)
    for t in trechos:
        g[int(t["inicio"] / passo):int(t["fim"] / passo)] = t["falante"]
    return g


def acordo(esperado: list[dict], obtido: list[dict], duracao: float) -> float:
    """A fração do tempo falado em que os dois concordam, com rótulos casados.

    O casamento é húngaro: o SPEAKER_00 do ONNX não é necessariamente o
    SPEAKER_00 do torch, e comparar rótulo com rótulo reprovaria um porte
    correto.
    """
    from scipy.optimize import linear_sum_assignment

    ge, go = grade(esperado, duracao), grade(obtido, duracao)
    re_ = sorted({x for x in ge if x})
    ro = sorted({x for x in go if x})
    if not re_ or not ro:
        return 0.0
    custo = np.zeros((len(re_), len(ro)))
    for i, a in enumerate(re_):
        for j, b in enumerate(ro):
            custo[i, j] = -np.sum((ge == a) & (go == b))
    li, lj = linear_sum_assignment(custo)
    mapa = {ro[j]: re_[i] for i, j in zip(li, lj)}
    go_map = np.array([mapa.get(x, x) for x in go], dtype=object)
    falado = ge != ""
    return float(np.sum((ge == go_map) & falado) / np.sum(falado))


def falantes_do_gemini(caminho: Path) -> dict[str, tuple[int, int]]:
    """`{pessoa: (palavras, turnos)}`, segundo o export do Gemini.

    O Gemini não dá tempos por turno, só a ordem — então a comparação possível
    é de **proporção**: quem falou mais, e quantas pessoas falaram.

    Os turnos vêm junto porque o export mistura duas coisas na mesma marcação
    `**...**`: o nome de quem fala e os títulos em negrito que o resumo usa.
    Medido nas quatro gravações com `gemini.md`: duas delas trazem um falante
    fantasma chamado `Atualizamos a seção "Decisões"`, com um turno só. Quem
    conta os falantes decide o que fazer com isso — esta função não esconde
    nada, só entrega o que permite decidir.
    """
    palavras: dict[str, int] = {}
    turnos: dict[str, int] = {}
    for linha in caminho.read_text(encoding="utf-8", errors="replace").splitlines():
        linha = linha.strip()
        if not linha or CABECALHO.match(linha):
            continue
        if (m := FALA.match(linha)) and m.group("texto").strip():
            quem = m.group("quem").strip()
            palavras[quem] = palavras.get(quem, 0) + len(
                re.findall(r"\w+", m.group("texto"))
            )
            turnos[quem] = turnos.get(quem, 0) + 1
    return {q: (n, turnos[q])
            for q, n in sorted(palavras.items(), key=lambda kv: -kv[1])}


# -------------------------------------------------------------------- ações

def gravar_gabarito(args) -> int:
    audio = GRAVACOES / args.gravacao / "mix.wav"
    if not audio.exists():
        print(f"não achei {audio}", file=sys.stderr)
        return 1

    print(f"diarizando {audio} com torch…", file=sys.stderr, flush=True)
    t0 = time.monotonic()
    trechos = diarizar_torch(audio)
    gasto = time.monotonic() - t0

    falantes = sorted({t["falante"] for t in trechos})
    GABARITO.parent.mkdir(parents=True, exist_ok=True)
    GABARITO.write_text(
        json.dumps(
            {
                "gravacao": args.gravacao,
                "audio": str(audio),
                "motor": "torch",
                "trechos": trechos,
            },
            ensure_ascii=False,
            indent=1,
        ),
        encoding="utf-8",
    )
    print(f"{GABARITO}: {len(trechos)} trechos, {len(falantes)} falantes "
          f"({gasto:.0f} s)")
    for quem, s in tempo_por_falante(trechos).items():
        print(f"  {quem:<12} {s:7.1f} s")
    return 0


def conferir(args) -> int:
    pasta = Path(args.gravacao)
    if not pasta.is_dir():
        pasta = GRAVACOES / args.gravacao
    audio = pasta / "mix.wav"
    if not audio.exists():
        print(f"não achei {audio}", file=sys.stderr)
        return 1

    onda, taxa = ler_wav(audio)
    duracao = len(onda) / taxa
    print(f"{pasta.name}: {duracao / 60:.1f} min")

    motores = ["torch", "onnx"] if args.motor == "ambos" else [args.motor]
    saidas: dict[str, list[dict]] = {}
    for nome in motores:
        t0 = time.monotonic()
        saidas[nome] = MOTORES[nome](audio)
        gasto = time.monotonic() - t0
        tempos = tempo_por_falante(saidas[nome])
        print(f"\n[{nome}] {len(saidas[nome])} trechos, {len(tempos)} falantes, "
              f"{gasto:.0f} s")
        for quem, s in tempos.items():
            print(f"  {quem:<12} {s:7.1f} s  ({100 * s / duracao:4.1f}% do áudio)")

    if len(saidas) == 2:
        a = acordo(saidas["torch"], saidas["onnx"], duracao)
        print(f"\nacordo torch×onnx: {a:.4f}")

    if args.contra_gemini:
        arquivo = pasta / "gemini.md"
        if not arquivo.exists():
            print(f"\n{arquivo} não existe — sem comparação com o Gemini",
                  file=sys.stderr)
            return 1
        deles = falantes_do_gemini(arquivo)
        total = sum(n for n, _ in deles.values()) or 1
        # Um turno só é um título em negrito do resumo, não uma pessoa
        # (ver `falantes_do_gemini`). Os descartados aparecem marcados: o
        # corte é uma decisão, e uma decisão escondida não se confere.
        pessoas = [q for q, (_, t) in deles.items() if t >= 2]
        print(f"\n[gemini] {len(pessoas)} falantes")
        for quem, (n, t) in deles.items():
            marca = "" if t >= 2 else "   <- 1 turno, descartado"
            print(f"  {quem:<32} {n:6d} palavras ({100 * n / total:4.1f}%)"
                  f"{marca}")
        for nome, trechos in saidas.items():
            nossos = len({t["falante"] for t in trechos})
            veredito = "igual" if nossos == len(pessoas) else "DIFERENTE"
            print(f"  contagem de falantes {nome} × gemini: "
                  f"{nossos} × {len(pessoas)} — {veredito}")
    return 0


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--gravar-gabarito", action="store_true",
                   help="roda o torch e grava o gabarito da régua V1")
    p.add_argument("--gravacao", default=GRAVACAO_GABARITO,
                   help="o id da pasta em MeetingRecordings, ou um caminho")
    p.add_argument("--motor", choices=["torch", "onnx", "ambos"], default="ambos")
    p.add_argument("--contra-gemini", action="store_true",
                   help="confronta com o gemini.md da mesma pasta")
    args = p.parse_args()
    return gravar_gabarito(args) if args.gravar_gabarito else conferir(args)


if __name__ == "__main__":
    raise SystemExit(main())
