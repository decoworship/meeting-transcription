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


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--gravacao", required=True)
    ap.add_argument("--tipo", default="trabalho")
    ap.add_argument("--modelo", action="append", required=True)
    args = ap.parse_args()

    pasta = GRAVACOES / args.gravacao
    if not (pasta / "transcricao.json").is_file():
        print(f"não achei transcrição em {pasta}", file=sys.stderr)
        return 2

    d = json.loads((pasta / "transcricao.json").read_text(encoding="utf-8"))
    segs = d.get("segments") or d.get("segmentos") or []
    texto = " ".join((s.get("text") or "") for s in segs)
    vozes = falantes(pasta / "transcricao.json")

    print(f"reunião: {args.gravacao} · {len(segs)} trechos · tipo {args.tipo}")
    print(f"números ditos na reunião: {len(numeros(texto))}\n")

    resultados = []
    for modelo in args.modelo:
        print(f"==> {modelo}")
        tempo, log = rodar(pasta, args.tipo, modelo)
        if tempo is None:
            print("    FALHOU")
            for linha in log.strip().splitlines()[-6:]:
                print(f"    {linha}")
            resultados.append((modelo, None))
            print()
            continue

        m = medir_ata(tempo["arquivo"], texto, vozes)
        m["segundos"] = tempo["segundos"]
        resultados.append((modelo, m))
        print(f"    {m['segundos']:.0f}s · {m['secoes']} seções · {m['acoes']} ações · "
              f"recall {m['recall']:.0%}")
        print()

    print("=" * 78)
    print(f"{'modelo':<32} {'tempo':>6} {'recall':>7} {'ações':>6} "
          f"{'s/dono':>7} {'maior seção':>12}")
    print("-" * 78)
    for modelo, m in resultados:
        if m is None:
            print(f"{modelo:<32} {'—':>6} {'FALHOU':>7}")
            continue
        print(f"{modelo:<32} {m['segundos']:>5.0f}s {m['recall']:>6.0%} "
              f"{m['acoes']:>6} {m['acoes_sem_dono']:>7} {m['maior_secao']:>11,}")

    print()
    for modelo, m in resultados:
        if m and m["nomes_inventados"]:
            print(f"  {modelo}: participantes que ninguém ouviu — "
                  f"{', '.join(m['nomes_inventados'])}")

    print(f"\nAs atas ficaram em {SAIDA} — a régua diz onde olhar, não substitui olhar.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
