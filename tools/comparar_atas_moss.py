#!/usr/bin/env python3
"""A ata a partir do texto do MOSS, contra a ata de hoje — T3

**É o teste que mais importa, e o último antes de decidir sobre um RC.** A ata é
o produto; a transcrição é insumo. O MOSS produz texto *diferente* — mais
cobertura, mais palavra trocada, e sem os termos do vocabulário na forma canônica
(§12). Se a ata piorar, o resto não importa; se melhorar, o vocabulário vira
detalhe.

**O prompt é o do app**, montado por ``medir_motor_de_ata.prompt_da_reuniao``:
regras comuns do SKILL.md, esqueleto e notas do tipo, dados da reunião, notas do
humano e a transcrição. Só a transcrição muda entre os dois braços — tudo o mais
é idêntico, senão a comparação mediria o prompt e não o texto.

**O motor é o do app**: o ``llama.exe`` da instalação, via ``cmd.exe``, com os
mesmos parâmetros (ctx 16384, KV em q8_0, temp 0,3), e o GGUF que o ``app.json``
desta máquina indica — o **Gemma 4 E4B**, escolhido por produzir o melhor
resultado, e não o ``qwen3-4b`` que é o padrão do código.

**O mesmo modelo nos dois braços.** O que muda entre eles é só a transcrição de
entrada; trocar o modelo junto misturaria as duas variáveis.

Depois, as duas atas vão para o [`auditar_atas.py`](auditar_atas.py), que conta
defeitos internos sem precisar de referência — seção descartada em silêncio,
item duplicado, contradição.

> **Uma nota sobre o critério.** O pedido era "rodar com hotwords se eles
> melhorarem". Eles melhoram (§12), **mas o MOSS não aceita hotwords em nenhum
> runtime ggml**. Então o braço do MOSS roda sem, e a comparação é
> `app com hotwords` × `MOSS sem` — que é a comparação real de produto, não a
> ideal.

Uso::

    uv run python tools/comparar_atas_moss.py
    uv run python tools/comparar_atas_moss.py --gravacoes 2026-08-21_11-00-33
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

sys.path.insert(0, str(Path(__file__).resolve().parent))
import medir_motor_de_ata as M  # noqa: E402

# ── Duas referências obsoletas do medir_motor_de_ata.py, contornadas aqui ────
#
# Ele é de agosto/2026 e aponta para dois lugares que mudaram desde então:
#
#   GRAVACOES  → C:\Users\andre\Documents\MeetingRecordings, que não existe
#                mais: o Documents foi redirecionado para o OneDrive;
#   SKILL_ZIP  → "transcrição para atas/transcricao-para-ata.skill", um zip que
#                virou arquivos soltos em assets/atas/.
#
# Aqui a primeira não incomoda — passamos a pasta explícita a
# `prompt_da_reuniao`. A segunda precisa deste desvio. **A ferramenta original
# está quebrada**, e consertá-la é decisão de quem a mantém; este arquivo só
# não depende do defeito.
ASSETS = Path(__file__).resolve().parent.parent / "assets" / "atas"


def _da_skill(nome: str) -> str:
    alvo = ASSETS / (nome.split("/", 1)[1] if nome.startswith("references/") else nome)
    return alvo.read_text(encoding="utf-8")


M._da_skill = _da_skill
prompt_da_reuniao = M.prompt_da_reuniao

ACERVO = Path("/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings")
#: A saída **costurada** do MOSS (§11), não a crua. A crua traz rótulos locais
#: do bloco (``b4_S1``), e a ata os cita como se fossem pessoas — o produto
#: nunca mostraria isso. O 55 é o limiar recomendado pela §11.2.
COSTURA = Path.home() / ".cache/pulsemeet-medicoes/costura"
LIMIAR_COSTURA = 55
BIN = Path("/mnt/c/Users/andre/MeetingApp/motores/ata/bin")
#: A pasta dos GGUF da instalação oficial.
MODELOS_WIN = r"C:\Users\andre\MeetingApp\motores\ata\modelos"

#: De onde sai o nome do modelo: o **app.json desta máquina**, não o padrão do
#: código. Em 03/09/2026 são coisas diferentes — o
#: ``ConfiguracoesDoApp.ModeloDeAta`` traz ``qwen3-4b-instruct-q4km.gguf`` como
#: padrão, e o app.json diz ``gemma-4-e4b-q4km.gguf``, porque foi o que produziu
#: o melhor resultado na prática. Medir com o padrão do código mediria um
#: produto que ninguém está usando.
#:
#: **Os dois braços usam o mesmo modelo**, sempre: o que muda entre eles é só a
#: transcrição de entrada. Trocar o modelo junto misturaria as duas variáveis.
APP_JSON = Path("/mnt/c/Users/andre/.meeting-transcription/app.json")


def modelo_do_app() -> str:
    try:
        return (json.loads(APP_JSON.read_text(encoding="utf-8"))
                .get("modelo_de_ata") or "qwen3-4b-instruct-q4km.gguf")
    except (OSError, json.JSONDecodeError):
        return "qwen3-4b-instruct-q4km.gguf"

COM_GEMINI = ["2026-08-21_11-00-33", "2026-08-25_08-59-22",
              "2026-08-20_15-59-20", "2026-08-27_15-28-37"]

#: O app **dimensiona o contexto** (``MotorDeAta.Dimensionar``, até 131072), não
#: o fixa. 16384 era pequeno demais: na gravação de 48,5 min o prompt tem ~16k
#: tokens e o modelo não produziu nada — 187 "palavras" que eram o banner.
CTX = 32768
KV = "q8_0"
TEMP = 0.3
PREVER = 3072

#: Arquivos que o montador de prompt lê além da transcrição.
AUXILIARES = ("notas.md", "meta.json", "reuniao.json")


def preparar(origem: Path, transcricao: Path, destino: Path) -> Path:
    """Uma pasta com a transcrição do braço e o resto igual ao original."""
    destino.mkdir(parents=True, exist_ok=True)
    shutil.copy(transcricao, destino / "transcricao.json")
    for a in AUXILIARES:
        if (origem / a).is_file():
            shutil.copy(origem / a, destino / a)
    return destino


def gerar(prompt: str, modelo: str) -> dict:
    """Uma ata. O prompt vai por arquivo — aspas atravessando bash → cmd.exe
    são comidas mesmo com ``/s`` (ver CLAUDE.md, armadilhas medidas)."""
    arq = BIN / "_ata_prompt.txt"
    arq.write_text(prompt, encoding="utf-8")
    cmd = (f'llama.exe cli -m {MODELOS_WIN}\\{modelo} -f _ata_prompt.txt '
           f'-n {PREVER} -ngl 99 --single-turn -c {CTX} '
           f'-ctk {KV} -ctv {KV} --temp {TEMP}')
    t0 = time.perf_counter()
    try:
        p = subprocess.run(["cmd.exe", "/s", "/c", cmd], cwd=BIN,
                           capture_output=True, text=True, errors="replace",
                           timeout=2400)
    except subprocess.TimeoutExpired:
        return {"erro": "estourou o tempo", "segundos": None}
    finally:
        arq.unlink(missing_ok=True)

    saida = (p.stdout or "") + (p.stderr or "")
    corpo = saida.split("[ Prompt:")[0]
    ultima = next((l.strip() for l in reversed(prompt.splitlines()) if l.strip()), "")
    # Onde a resposta começa. O llama ecoa o prompt depois de "> " e, quando ele
    # é longo, **trunca o eco** e imprime "... (truncated)". Ancorar só na
    # última linha do prompt falha exatamente nas reuniões grandes, que são as
    # que interessam — foi o defeito da segunda versão desta ferramenta.
    if "(truncated)" in corpo:
        texto = corpo.rsplit("(truncated)", 1)[-1]
    elif ultima and ultima in corpo:
        texto = corpo.rsplit(ultima, 1)[-1]
    else:
        return {"erro": "não achei onde a resposta começa na saída do llama",
                "segundos": round(time.perf_counter() - t0, 1)}
    # O raciocínio fora da ata. O app manda ``enable_thinking: false`` pelo
    # template do chat (MotorDeAta.cs, medido em 17/08/2026: com o padrão, o
    # modelo gastou os 8.192 tokens de saída inteiros pensando e a ata saiu pela
    # metade). Aqui o prompt vai cru por arquivo, sem template, então o bloco
    # sai depois — o que o produto entrega é o que vem **depois** dele.
    for fim in ("[End thinking]", "</think>"):
        if fim in texto:
            texto = texto.split(fim, 1)[1]
            break

    if len(texto.split()) < 30:
        return {"erro": f"resposta curta demais ({len(texto.split())} palavras) "
                        "— o modelo provavelmente não produziu ata",
                "segundos": round(time.perf_counter() - t0, 1)}
    m = re.search(r"Prompt:\s*([\d.]+)\s*t/s\s*\|\s*Generation:\s*([\d.]+)", saida)
    return {
        "texto": texto.replace("\r", "").strip(),
        "segundos": round(time.perf_counter() - t0, 1),
        "prompt_ts": float(m.group(1)) if m else None,
        "erro": None if p.returncode == 0 else saida[-250:],
    }


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--gravacoes")
    ap.add_argument("--tipo", default="cliente-update")
    ap.add_argument("--saida", default=str(Path.home() / ".cache/pulsemeet-medicoes/atas"))
    ap.add_argument("--modelo", help="padrão: o do app.json desta máquina")
    ap.add_argument("--json")
    args = ap.parse_args()

    if not (BIN / "llama.exe").is_file():
        raise SystemExit(f"llama.exe não está em {BIN}")

    nomes = ([n.strip() for n in args.gravacoes.split(",")] if args.gravacoes
             else list(COM_GEMINI))
    modelo = args.modelo or modelo_do_app()
    print(f"motor de ata: {modelo}  (nos dois braços)", flush=True)
    saida = Path(args.saida)
    palco = saida / "_palco"
    resultados = []

    for nome in nomes:
        origem = ACERVO / nome
        moss = COSTURA / f"{nome}__costurado_{LIMIAR_COSTURA}" / "transcricao.json"
        if not (origem / "transcricao.json").is_file() or not moss.is_file():
            print(f"  [pulei] {nome}", file=sys.stderr)
            continue

        print(f"\n{nome}", flush=True)
        linha = {"gravacao": nome, "tipo": args.tipo}

        for braco, transcricao in (("app", origem / "transcricao.json"),
                                   ("moss", moss)):
            pasta = preparar(origem, transcricao, palco / f"{nome}__{braco}")
            try:
                prompt, meta = prompt_da_reuniao(pasta, args.tipo)
            except Exception as e:
                print(f"    {braco:<5} erro ao montar o prompt: {e}", file=sys.stderr)
                continue

            r = gerar(prompt, modelo)
            if r.get("erro"):
                print(f"    {braco:<5} ERRO: {str(r['erro'])[:110]}", flush=True)
                continue

            destino = saida / f"{nome}__{braco}"
            destino.mkdir(parents=True, exist_ok=True)
            (destino / "ata.md").write_text(r["texto"], encoding="utf-8")
            for a in AUXILIARES:
                if (origem / a).is_file():
                    shutil.copy(origem / a, destino / a)
            shutil.copy(transcricao, destino / "transcricao.json")

            print(f"    {braco:<5} {r['segundos']:>6.1f}s  "
                  f"{len(r['texto'].split()):>5} palavras de ata  "
                  f"(prompt: {len(prompt.split())} palavras)", flush=True)
            linha[braco] = {"segundos": r["segundos"],
                            "palavras_ata": len(r["texto"].split()),
                            "palavras_prompt": len(prompt.split())}
        resultados.append(linha)

    if resultados:
        print(f"\n{'═'*62}\nAtas em {saida}")
        print("Agora audite as duas:")
        print(f"  uv run python tools/auditar_atas.py {saida}")

    if args.json and resultados:
        Path(args.json).write_text(json.dumps(resultados, ensure_ascii=False,
                                              indent=2), encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
