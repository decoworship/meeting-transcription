#!/usr/bin/env python3
"""Compara modelos de ata na mesma reunião, com régua e não com impressão.

Nasceu em 17/08/2026, quando o dono do produto pediu para avaliar o Qwen3.5 4B e
o Gemma 4 contra o Qwen3 4B que está em produção.

**Por que uma ferramenta, e não ler as três atas.** Ler compara o que salta aos
olhos — e o que salta aos olhos é a prosa, que é justamente onde um modelo ruim
engana melhor. O que decide é o que a Fase 3 já tinha medido e nomeado como o
defeito do 4B: **omissão**. Ele não inventa; ele esquece. Isso não se vê lendo,
se vê contando.

As cinco réguas, e cada uma existe por um defeito visto em campo:

  recall de números   quantos dos números ditos na reunião entraram na ata.
                      É a régua principal: a medição da Fase 3 achou 7 de 14.
  donos definidos     ações com responsável, contra "[responsável a definir]".
                      Ata sem dono é lista de desejos.
  nomes inventados    participantes na ata que ninguém ouviu falar. O verificador
                      já barra, mas contar diz se o modelo tentou.
  maior seção         o Qwen3 4B escreveu uma seção de 14 mil caracteres com uma
                      segunda ata dentro, e estourou o limite de saída.
  tempo               na placa de quem usa, não em nuvem.

Uso::

    tools/comparar_modelos_de_ata.py --gravacao 2026-08-14_09-29-44 \\
        --modelo qwen3-4b-instruct-q4km.gguf \\
        --modelo qwen3.5-4b-q4km.gguf \\
        --modelo gemma-4-e4b-q4km.gguf
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path

GRAVACOES = Path("/mnt/c/Users/andre/Documents/MeetingRecordings")
CLI = Path("/mnt/c/Users/andre/cli-teste/Sidecar.exe")
MOTOR = r"C:\Users\andre\MeetingApp\motores\ata"
SAIDA = Path("/mnt/c/Users/andre/ata-comparacao")

# Números que valem contar. Percentual, dinheiro, quantidade, data curta — e não
# o "3" de "às 3 horas", que aparece em qualquer conversa e não é fato.
NUMERO = re.compile(r"\b\d{1,3}(?:[.,]\d+)?\s*(?:%|mil|milhões?|milhão|k\b)"
                    r"|R\$\s*\d[\d.,]*"
                    r"|\b\d{2,}(?:[.,]\d+)?\b")


def _win(p) -> str:
    r = subprocess.run(["wslpath", "-w", str(p)], capture_output=True, text=True)
    return r.stdout.strip() or str(p)


def numeros(texto: str) -> set[str]:
    """Os números citados, normalizados para comparar."""
    achados = set()
    for m in NUMERO.finditer(texto):
        bruto = m.group(0).strip()
        # "1.500" e "1500" são o mesmo número dito de dois jeitos; se a ata
        # escreve de um jeito e a transcrição de outro, contar como falta seria
        # medir formatação e não memória.
        so_digitos = re.sub(r"[^\d]", "", bruto)
        if so_digitos and len(so_digitos) >= 2:
            achados.add(so_digitos)
    return achados


def falantes(transcricao: Path) -> set[str]:
    d = json.loads(transcricao.read_text(encoding="utf-8"))
    segs = d.get("segments") or d.get("segmentos") or []
    return {(s.get("speaker") or "").strip() for s in segs if s.get("speaker")}


def medir_ata(ata: Path, texto_da_reuniao: str, vozes: set[str]) -> dict:
    md = ata.read_text(encoding="utf-8")

    secoes = re.split(r"^## ", md, flags=re.M)[1:]
    maior = max((len(s) for s in secoes), default=0)

    acoes = re.findall(r"^- \[ \] (.+)$", md, flags=re.M)
    sem_dono = sum(1 for a in acoes if "responsável a definir" in a)

    ditos = numeros(texto_da_reuniao)
    na_ata = numeros(md)
    recuperados = ditos & na_ata

    # Nome na linha de participantes que ninguém ouviu falar. Comparação por
    # primeiro nome: a ata escreve "Daniel Prada" e a voz aprendida pode ser
    # "Daniel".
    primeiros = {v.split()[0].lower() for v in vozes if v}
    linha = re.search(r"^\*\*Participantes:\*\* (.+)$", md, flags=re.M)
    inventados = []
    if linha:
        for nome in linha.group(1).split(","):
            n = nome.strip()
            if n and n.split()[0].lower() not in primeiros and n.lower() != "you":
                inventados.append(n)

    return {
        "chars": len(md),
        "secoes": len(secoes),
        "maior_secao": maior,
        "acoes": len(acoes),
        "acoes_sem_dono": sem_dono,
        "numeros_ditos": len(ditos),
        "numeros_na_ata": len(recuperados),
        "recall": (len(recuperados) / len(ditos)) if ditos else 0.0,
        "nomes_inventados": inventados,
    }


def rodar(pasta: Path, tipo: str, modelo: str) -> tuple[dict | None, str]:
    """Roda o pipeline real de ata, pelo CLI, e devolve o que dá para medir."""
    ambiente = {"MEETINGAPP_MOTOR_ATA": MOTOR, "WSLENV": "MEETINGAPP_MOTOR_ATA"}
    import os
    env = dict(os.environ, **ambiente)

    inicio = time.time()
    proc = subprocess.run(
        [str(CLI), "--ata", _win(pasta), "--tipo", tipo, "--modelo", modelo],
        capture_output=True, text=True, encoding="utf-8", errors="replace", env=env)
    duracao = time.time() - inicio

    saida = (proc.stdout or "") + (proc.stderr or "")
    ata = pasta / "ata.md"
    if proc.returncode != 0 or not ata.is_file():
        return None, saida

    SAIDA.mkdir(parents=True, exist_ok=True)
    guardada = SAIDA / f"{pasta.name}__{Path(modelo).stem}.md"
    shutil.copy(ata, guardada)
    return {"segundos": duracao, "arquivo": guardada}, saida


def media(xs: list[float]) -> float:
    return sum(xs) / len(xs) if xs else 0.0


def desvio(xs: list[float]) -> float:
    """Desvio-padrão amostral — é ele que diz se a diferença entre modelos vale."""
    if len(xs) < 2:
        return 0.0
    m = media(xs)
    return (sum((x - m) ** 2 for x in xs) / (len(xs) - 1)) ** 0.5


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--gravacao", action="append", required=True)
    ap.add_argument("--tipo", default="trabalho")
    ap.add_argument("--modelo", action="append", required=True)
    ap.add_argument("--rodadas", type=int, default=2,
                    help="repetições por modelo e reunião — é o que separa "
                         "diferença de ruído")
    args = ap.parse_args()

    # (modelo, reunião, rodada) -> medidas
    tudo: dict[str, list[dict]] = {m: [] for m in args.modelo}
    falhas: dict[str, list[str]] = {m: [] for m in args.modelo}

    for nome in args.gravacao:
        pasta = GRAVACOES / nome
        if not (pasta / "transcricao.json").is_file():
            print(f"pulando {nome}: sem transcrição", file=sys.stderr)
            continue

        d = json.loads((pasta / "transcricao.json").read_text(encoding="utf-8"))
        segs = d.get("segments") or d.get("segmentos") or []
        texto = " ".join((s.get("text") or "") for s in segs)
        vozes = falantes(pasta / "transcricao.json")
        dur = max((s.get("end") or 0) for s in segs) / 60

        print(f"\n{'=' * 78}")
        print(f"{nome} · {dur:.0f} min · {len(segs)} trechos · "
              f"{len(numeros(texto))} números ditos")
        print("=" * 78)

        for rodada in range(1, args.rodadas + 1):
            for modelo in args.modelo:
                tempo, log = rodar(pasta, args.tipo, modelo)
                etiqueta = f"{modelo.split('.gguf')[0][:26]:<26} r{rodada}"

                if tempo is None:
                    ultima = [l for l in log.strip().splitlines() if l.strip()][-1:]
                    motivo = ultima[0][:90] if ultima else "sem saída"
                    print(f"  {etiqueta}  FALHOU — {motivo}")
                    falhas[modelo].append(f"{nome} r{rodada}: {motivo}")
                    continue

                m = medir_ata(tempo["arquivo"], texto, vozes)
                m["segundos"] = tempo["segundos"]
                m["reuniao"] = nome
                tudo[modelo].append(m)
                print(f"  {etiqueta}  {m['segundos']:>4.0f}s · recall {m['recall']:>4.0%} · "
                      f"{m['acoes']:>2} ações ({m['acoes_sem_dono']} s/dono) · "
                      f"maior seção {m['maior_secao']:>6,}")

                # Renomear por rodada, senão a segunda sobrescreve a primeira e
                # a variância — que é o que se está medindo — some.
                novo = tempo["arquivo"].with_name(
                    f"{tempo['arquivo'].stem}__r{rodada}.md")
                tempo["arquivo"].replace(novo)

    print(f"\n\n{'=' * 78}")
    print("RESUMO")
    print("=" * 78)
    print(f"{'modelo':<28} {'n':>3} {'falhas':>7} {'tempo':>7} "
          f"{'recall':>14} {'s/dono':>8} {'pior seção':>11}")
    print("-" * 78)

    for modelo in args.modelo:
        ms = tudo[modelo]
        if not ms:
            print(f"{modelo[:27]:<28} {0:>3} {len(falhas[modelo]):>7}  todas falharam")
            continue

        recalls = [m["recall"] for m in ms]
        acoes = sum(m["acoes"] for m in ms)
        semDono = sum(m["acoes_sem_dono"] for m in ms)
        print(f"{modelo[:27]:<28} {len(ms):>3} {len(falhas[modelo]):>7} "
              f"{media([m['segundos'] for m in ms]):>6.0f}s "
              f"{media(recalls):>7.0%} ±{desvio(recalls):>4.0%} "
              f"{(semDono / acoes if acoes else 0):>7.0%} "
              f"{max(m['maior_secao'] for m in ms):>10,}")

    print("\nO ± é o desvio entre rodadas. Quando ele encosta na diferença entre")
    print("modelos, a diferença não é diferença — é a mesma moeda jogada duas vezes.")

    for modelo in args.modelo:
        for f in falhas[modelo]:
            print(f"\n  falha · {modelo}: {f}")
        inventados = {n for m in tudo[modelo] for n in m["nomes_inventados"]}
        if inventados:
            print(f"\n  {modelo}: participantes que ninguém ouviu — "
                  f"{', '.join(sorted(inventados))}")

    print(f"\nAs atas ficaram em {SAIDA} — a régua diz onde olhar, não substitui olhar.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
