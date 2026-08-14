"""Mede o motor de ata na máquina de verdade: cabe, quanto demora, o que sai.

Responde as perguntas do §8 do ATA.md com as gravações que já existem no disco,
e não com estimativa. É a Fase 0 do item 3 da Fase 3: medir antes de escrever o
motor, pelo mesmo motivo que a Fase 0 mediu os motores de ASR antes do porte.

O que ela monta é **o prompt que o app vai montar**: as regras comuns recortadas
do SKILL.md, o esqueleto e as notas do tipo escolhido, os dados da reunião, as
notas do humano e a transcrição. Medir com um prompt de brinquedo não diria nada
sobre o que acontece com 20 mil tokens de reunião real.

Uso:
    python tools/medir_motor_de_ata.py --gravacao 2026-08-13_14-30-15 \\
        --tipo cliente-update --ctx 16384

    python tools/medir_motor_de_ata.py --listar     # o que há para medir

Precisa do llama.cpp para Windows e de um GGUF, por padrão em
C:\\Users\\andre\\ata-teste. **O build de CUDA tem que casar com o driver**: o
13.3 falhou nesta máquina com "the provided PTX was compiled with an unsupported
toolchain" (driver 595.97, CUDA 13.2), e o 12.4 funcionou.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import threading
import time
import zipfile
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SKILL_ZIP = REPO / "transcrição para atas" / "transcricao-para-ata.skill"
GRAVACOES = Path("/mnt/c/Users/andre/Documents/MeetingRecordings")
BASE = Path("/mnt/c/Users/andre/ata-teste")

TIPOS = ["cliente-update", "sprint", "trabalho", "kickoff", "resultados", "daily"]


def _da_skill(nome: str) -> str:
    """Lê um arquivo de dentro do .skill, que é a fonte única."""
    with zipfile.ZipFile(SKILL_ZIP) as z:
        return z.read(f"transcricao-para-ata/{nome}").decode("utf-8")


def regras_comuns() -> str:
    """
    O SKILL.md sem o Passo 1.

    A classificação do tipo é do app, não do modelo (ATA.md §1): quem escolheu o
    tipo foi o usuário na tela, e deixar um 4B reclassificar é dar a ele a
    chance de contrariar quem sabe.
    """
    skill = _da_skill("SKILL.md")
    corte = skill.index("## Passo 2: Regras comuns")
    return skill[corte:].replace(
        "## Passo 2: Regras comuns a todos os tipos", "## Regras")


def prompt_da_reuniao(pasta: Path, tipo: str) -> tuple[str, dict]:
    dados = json.loads((pasta / "transcricao.json").read_text(encoding="utf-8"))

    linhas = []
    for s in dados["segments"]:
        t = int(s["start"])
        quem = s.get("speaker") or "?"
        linhas.append(f"[{t // 60:02d}:{t % 60:02d}] {quem}: {s['text'].strip()}")
    transcricao = "\n".join(linhas)

    falantes = sorted({(s.get("speaker") or "?") for s in dados["segments"]})
    notas = ""
    if (pasta / "notas.md").exists():
        notas = (pasta / "notas.md").read_text(encoding="utf-8")

    contexto = [f"- Cliente: {dados.get('client') or '—'}",
                f"- Projeto: {dados.get('project') or '—'}",
                f"- Data: {dados.get('date') or pasta.name}",
                f"- Duração: {(dados.get('duration') or 0) / 60:.0f} minutos",
                f"- Falantes reconhecidos: {', '.join(falantes)}"]

    partes = [regras_comuns(), "\n---\n",
              "# Estrutura desta ata\n",
              f"O tipo desta reunião já foi definido: **{tipo}**. "
              "Use exatamente a estrutura abaixo.\n",
              _da_skill(f"references/{tipo}.md"),
              "\n---\n", "# Dados da reunião\n", "\n".join(contexto)]

    if notas.strip():
        # A precedência é dita ao modelo, não deduzida por ele (ATA.md §6).
        partes += ["\n# Notas escritas por quem estava na reunião\n",
                   "Estas notas têm precedência sobre a transcrição: foram "
                   "escritas por uma pessoa presente. Quando divergirem, siga as "
                   "notas e registre a divergência.\n", notas]

    partes += ["\n# Transcrição\n", transcricao,
               "\n---\n", "Escreva agora a ata, seguindo a estrutura e as regras."]

    return "\n".join(partes), {
        "trechos": len(dados["segments"]),
        "minutos": (dados.get("duration") or 0) / 60,
        "falantes": falantes,
        "com_notas": bool(notas.strip()),
    }


class Vigia(threading.Thread):
    """Amostra a VRAM enquanto o modelo roda. O pico é o que decide se cabe."""

    def __init__(self):
        super().__init__(daemon=True)
        self.pico = 0
        self.parar = threading.Event()

    def run(self):
        while not self.parar.is_set():
            try:
                saida = subprocess.run(
                    ["nvidia-smi", "--query-gpu=memory.used",
                     "--format=csv,noheader,nounits"],
                    capture_output=True, text=True, timeout=10).stdout.strip()
                self.pico = max(self.pico, int(saida.splitlines()[0]))
            except Exception:
                pass
            self.parar.wait(1.0)


def medir(args) -> int:
    pasta = GRAVACOES / args.gravacao
    if not (pasta / "transcricao.json").exists():
        print(f"{pasta} não tem transcricao.json", file=sys.stderr)
        return 2

    prompt, info = prompt_da_reuniao(pasta, args.tipo)
    # O modo entra no nome: sem isto, a rodada com esquema sobrescreve a de
    # Markdown livre da mesma reunião, e a comparação entre as duas some.
    modo = "json" if args.esquema else "md"
    destino = BASE / f"ata-{args.gravacao}-{args.tipo}-c{args.ctx}-{modo}"
    if args.esquema:
        sistema = (
            "Você redige atas de reunião em português do Brasil a partir de "
            "transcrições automáticas. Siga as regras dadas. Responda APENAS com "
            "um objeto JSON no formato pedido. Cada ação precisa de responsável; "
            "se a transcrição não nomear um, escreva \"[responsável a definir]\". "
            "Sem prazo, \"[prazo a definir]\".")
    else:
        sistema = (
            "Você redige atas de reunião em português do Brasil a partir de "
            "transcrições automáticas. Siga as regras e a estrutura dadas. Responda "
            "apenas com a ata em Markdown, sem comentários seus.")
    (BASE / "sistema.txt").write_text(sistema, encoding="utf-8")
    entrada = BASE / "prompt.txt"
    entrada.write_text(prompt, encoding="utf-8")

    print(f"reunião  {args.gravacao}  {info['minutos']:.0f} min, "
          f"{info['trechos']} trechos, {len(info['falantes'])} falantes"
          + ("  (com notas)" if info["com_notas"] else ""))
    print(f"prompt   {len(prompt):,} chars  ~{len(prompt) // 3.2:,.0f} tokens estimados")
    print(f"config   contexto {args.ctx}, KV {args.kv}, modelo {args.modelo}")

    # Caminhos RELATIVOS nos argumentos, com cwd na pasta do teste: o
    # llama-cli.exe é um programa Windows e não sabe abrir "/mnt/c/...". O
    # caminho do próprio executável pode ser do WSL porque quem o resolve é o
    # interop, não o programa. Custou uma execução para descobrir.
    comando = [str(BASE / "bin" / "llama-cli.exe"),
               "-m", args.modelo, "-ngl", "99", "-c", str(args.ctx),
               "-ctk", args.kv, "-ctv", args.kv,
               "--temp", str(args.temp), "-n", str(args.n_predict),
               "-st", "--no-warmup",
               "-sysf", "sistema.txt", "-f", entrada.name]
    if args.gramatica:
        comando += ["--grammar-file", Path(args.gramatica).name]
    if args.esquema:
        # A saída constrangida do ATA.md §3: o decodificador não deixa o modelo
        # sair do formato, em vez de o prompt pedir e o modelo às vezes obedecer.
        comando += ["-jf", Path(args.esquema).name]

    vigia = Vigia()
    vigia.start()
    inicio = time.time()
    r = subprocess.run(comando, cwd=BASE, capture_output=True, text=True,
                       errors="replace", timeout=args.timeout)
    duracao = time.time() - inicio
    vigia.parar.set()
    vigia.join(timeout=3)

    saida = r.stdout
    erro = re.search(r"CUDA error.*|failed to .*|error: .*", r.stderr or "")
    tps = re.search(r"Prompt:\s*([\d.]+) t/s.*?Generation:\s*([\d.]+) t/s", saida, re.S)

    print(f"tempo    {duracao:.0f} s de ponta a ponta")
    print(f"VRAM     pico de {vigia.pico} MiB de 6144")
    if tps:
        print(f"velocidade  prompt {tps.group(1)} t/s, geração {tps.group(2)} t/s")
    if erro:
        print(f"ERRO     {erro.group(0)[:160]}")

    # A ata começa depois do prompt ecoado pela interface do llama-cli.
    marca = "Escreva agora a ata, seguindo a estrutura e as regras."
    ata = saida.split(marca)[-1] if marca in saida else saida
    ata = re.sub(r"\n\[ Prompt:.*?\]\s*$", "", ata.strip(), flags=re.S)
    destino.with_suffix(".md").write_text(ata, encoding="utf-8")
    destino.with_suffix(".log").write_text(r.stderr or "", encoding="utf-8")
    print(f"saída    {len(ata):,} chars em {destino.with_suffix('.md')}")
    return 0 if not erro else 1


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--gravacao")
    p.add_argument("--tipo", default="cliente-update", choices=TIPOS)
    p.add_argument("--ctx", type=int, default=16384)
    p.add_argument("--kv", default="q8_0", choices=["f16", "q8_0", "q4_0"])
    p.add_argument("--temp", type=float, default=0.3)
    p.add_argument("--n-predict", type=int, default=3072)
    p.add_argument("--modelo", default="qwen3-4b-q4km.gguf",
                   help="nome do arquivo dentro da pasta do teste")
    p.add_argument("--gramatica")
    p.add_argument("--esquema", help="JSON Schema que prende a saída (relativo à pasta do teste)")
    p.add_argument("--timeout", type=int, default=1800)
    p.add_argument("--listar", action="store_true",
                   help="mostra as gravações transcritas, da mais longa para a mais curta")
    args = p.parse_args()

    if args.listar:
        linhas = []
        for pasta in GRAVACOES.iterdir():
            arquivo = pasta / "transcricao.json"
            if not arquivo.exists():
                continue
            d = json.loads(arquivo.read_text(encoding="utf-8"))
            chars = sum(len(s["text"]) for s in d["segments"])
            linhas.append(((d.get("duration") or 0) / 60, pasta.name, chars,
                           d.get("client") or "—"))
        for m, nome, chars, cliente in sorted(linhas, reverse=True):
            print(f"{nome}  {m:6.1f} min  {chars:7,} chars  {cliente}")
        return 0

    if not args.gravacao:
        p.error("passe --gravacao ou --listar")
    return medir(args)


if __name__ == "__main__":
    raise SystemExit(main())
