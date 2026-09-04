#!/usr/bin/env python3
"""T2.1: o LLM respondendo sobre a reunião enquanto ela acontece.

**O pedido.** Poder perguntar, no meio da reunião, o que já foi dito — o que
ficou decidido, que pendências surgiram, se a pauta está sendo seguida. É a
funcionalidade que o produto A da Fase 7 destrava, porque ela **só precisa da
transcrição parcial**, que a §1 mostrou sair sem custo de qualidade.

**O que decide se isso existe não é a qualidade da resposta, é a conta.** O
prompt *é* a transcrição corrente, e ela só cresce. O KV custa 62 KiB por token
(``docs/ATA.md`` §8), e a placa tem ~5 GB úteis com o desktop rodando. Então as
perguntas são:

1. **quanto tempo o usuário espera** por uma resposta, aos 10, 30, 60 minutos de
   reunião? Uma resposta que leva um minuto não serve para uso ao vivo;
2. **cabe na VRAM** junto com o que mais estiver rodando?
3. **o modelo residente ou sob demanda?** Se responder sob demanda for rápido o
   bastante, some a disputa de memória com o ASR e a diarização.

**Roda o llama.cpp do próprio app**, em Windows via ``cmd.exe`` — os binários
que a instalação já tem, com CUDA. Medir com outro build mediria outra coisa.

Uso::

    uv run python tools/medir_llm_ao_vivo.py
    uv run python tools/medir_llm_ao_vivo.py --cortes 10,30,60 --modelo qwen3-4b-instruct-q4km
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import time
from pathlib import Path

ACERVO_PADRAO = "/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings"

#: A instalação oficial. É de lá que saem os binários e os modelos que o app
#: usa de verdade — ver docs/FASE4.md §1.
BIN = Path("/mnt/c/Users/andre/MeetingApp/motores/ata/bin")
MODELOS_WIN = r"C:\Users\andre\MeetingApp\motores\ata\modelos"

PERGUNTAS = [
    ("decisoes", "Liste em tópicos o que ficou decidido nesta reunião até agora. "
                 "Se nada foi decidido, diga apenas 'nada decidido ainda'."),
    ("pendencias", "Liste as pendências e tarefas que apareceram, com o responsável "
                   "quando ele for dito. Se não houver, diga 'nenhuma'."),
]

#: A régua de tempo. Acima disto a resposta deixa de ser "ao vivo" e vira
#: "abri um relatório" — número escolhido, não medido.
SEGUNDOS_ACEITAVEIS = 20.0


def transcricao_ate(dados: dict, ate_s: float) -> str:
    """A reunião como texto, até o instante dado, no formato que a ata usa."""
    linhas, atual, buffer = [], None, []
    for s in dados.get("segments") or []:
        if not isinstance(s.get("start"), (int, float)) or s["start"] >= ate_s:
            continue
        texto = (s.get("text") or "").strip()
        if not texto:
            continue
        quem = (s.get("speaker") or "").strip() or "Desconhecido"
        if quem != atual:
            if atual and buffer:
                linhas.append(f"{atual}: {' '.join(buffer)}")
            atual, buffer = quem, []
        buffer.append(texto)
    if atual and buffer:
        linhas.append(f"{atual}: {' '.join(buffer)}")
    return "\n".join(linhas)


def perguntar(modelo: str, prompt: str, n_prever: int, camadas: int,
              contexto: int) -> dict | None:
    """Uma pergunta ao llama.cpp do app. Devolve tempos e a resposta.

    O prompt vai por **arquivo**, não por argumento: aspas atravessando
    bash → cmd.exe são comidas mesmo com ``/s``, e o sintoma é um argumento
    partido no meio de uma palavra (ver CLAUDE.md, armadilhas medidas).
    """
    arq = BIN / "_prompt.txt"
    arq.write_text(prompt, encoding="utf-8")
    # -c explícito: o contexto nativo do Qwen3-4B é 262k, e o KV disso não cabe
    # em 6 GB — o sintoma é "failed to create_context", não um erro de memória.
    # -ctk/-ctv em q8_0: é o que a docs/ATA.md §8 usa para chegar aos 62 KiB
    # por token.
    cmd = (f'llama.exe cli -m {MODELOS_WIN}\\{modelo}.gguf -f _prompt.txt '
           f'-n {n_prever} -ngl {camadas} --single-turn '
           f'-c {contexto} -ctk q8_0 -ctv q8_0')
    t0 = time.perf_counter()
    try:
        p = subprocess.run(["cmd.exe", "/s", "/c", cmd], cwd=BIN,
                           capture_output=True, text=True, errors="replace",
                           timeout=900)
    except subprocess.TimeoutExpired:
        return None
    finally:
        arq.unlink(missing_ok=True)
    parede = time.perf_counter() - t0

    saida = (p.stdout or "") + (p.stderr or "")
    m = re.search(r"Prompt:\s*([\d.]+)\s*t/s\s*\|\s*Generation:\s*([\d.]+)\s*t/s", saida)

    # A saída é: banner, "> " + o prompt ecoado, a resposta, e a linha de
    # estatística. Recortar entre a ÚLTIMA linha do prompt e o "[ Prompt:" é o
    # que sobra sendo só o texto gerado — cortar pelo começo do prompt pegaria
    # o banner junto, que foi o defeito da primeira versão desta ferramenta.
    corpo = saida.split("[ Prompt:")[0]
    ultima = next((l.strip() for l in reversed(prompt.splitlines()) if l.strip()), "")
    resposta = corpo.rsplit(ultima, 1)[-1] if ultima and ultima in corpo else corpo[-1200:]

    return {
        "parede_s": round(parede, 1),
        "prompt_ts": float(m.group(1)) if m else None,
        "geracao_ts": float(m.group(2)) if m else None,
        "resposta": resposta.replace("\r", "").strip()[:900],
        "erro": None if p.returncode == 0 else saida[-300:],
    }


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--acervo", default=ACERVO_PADRAO)
    ap.add_argument("--gravacao", default="2026-08-10_15-00-15",
                    help="padrão: a de 122 min, a única que cobre uma hora de reunião")
    ap.add_argument("--cortes", default="10,30,60",
                    help="minutos de reunião decorridos")
    ap.add_argument("--modelo", default="qwen3-4b-instruct-q4km")
    ap.add_argument("--prever", type=int, default=256)
    ap.add_argument("--camadas", type=int, default=99)
    ap.add_argument("--contexto", type=int, default=32768,
                    help="janela de contexto; o nativo do 4B (262k) não cabe em 6 GB")
    ap.add_argument("--json")
    args = ap.parse_args()

    if not shutil.which("cmd.exe"):
        raise SystemExit("cmd.exe não encontrado — esta medição precisa do Windows")
    if not (BIN / "llama.exe").is_file():
        raise SystemExit(f"llama.exe não está em {BIN}")

    alvo = Path(args.acervo) / args.gravacao / "transcricao.json"
    dados = json.loads(alvo.read_text(encoding="utf-8"))
    dur = dados.get("duration") or 0
    print(f"{args.gravacao}  ({dur/60:.1f} min)  modelo {args.modelo}\n")

    resultados = []
    for corte in [float(c) * 60 for c in args.cortes.split(",")]:
        if corte > dur:
            continue
        texto = transcricao_ate(dados, corte)
        palavras = len(texto.split())
        print(f"── aos {corte/60:.0f} min de reunião  ({palavras} palavras de contexto)",
              flush=True)
        for nome, pergunta in PERGUNTAS:
            prompt = (f"Você está acompanhando uma reunião em andamento. Abaixo está "
                      f"a transcrição do que foi dito até agora.\n\n"
                      f"--- TRANSCRIÇÃO ---\n{texto}\n--- FIM ---\n\n{pergunta}\n")
            r = perguntar(args.modelo, prompt, args.prever, args.camadas,
                          args.contexto)
            if r is None:
                print(f"   {nome:<12} ESTOUROU O TEMPO", flush=True)
                continue
            if r["erro"]:
                print(f"   {nome:<12} ERRO: {r['erro'][:120]}", flush=True)
                continue
            veredito = "ok" if r["parede_s"] <= SEGUNDOS_ACEITAVEIS else "LENTO"
            print(f"   {nome:<12} {r['parede_s']:>6.1f}s parede   "
                  f"prompt {r['prompt_ts'] or 0:>6.1f} t/s   "
                  f"geração {r['geracao_ts'] or 0:>5.1f} t/s   {veredito}", flush=True)
            resultados.append({"corte_min": corte / 60, "palavras": palavras,
                               "pergunta": nome, **r})
        print(flush=True)

    if resultados:
        print("─" * 66)
        print("A resposta mais lenta por corte (é ela que o usuário sente):")
        for c in sorted({r["corte_min"] for r in resultados}):
            pior = max((r for r in resultados if r["corte_min"] == c),
                       key=lambda r: r["parede_s"])
            print(f"  {c:>4.0f} min de reunião → {pior['parede_s']:>6.1f}s"
                  f"   ({pior['palavras']} palavras de contexto)")
        print(f"\n  régua: {SEGUNDOS_ACEITAVEIS:.0f}s. Acima disso deixa de ser "
              "resposta ao vivo.")

    if args.json and resultados:
        Path(args.json).write_text(json.dumps(resultados, ensure_ascii=False,
                                              indent=2), encoding="utf-8")
        print(f"\nbruto em {args.json}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
