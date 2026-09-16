#!/usr/bin/env python3
"""Compara modelos respondendo perguntas sobre uma reunião em andamento.

Irmão do ``comparar_modelos_de_ata.py``, e a diferença é o que se mede. Lá a
saída é um documento com forma fixa, e a régua principal é omissão. Aqui a
pergunta é **livre** — é a caixa de perguntar que entrou em 16/09/2026
(CONVERGENCIA §T2.1) —, e o que decide é outra coisa: **o modelo respondeu só
do que estava na transcrição, ou completou com o que seria plausível?**

**Prompt e esquema são variáveis, e não dados do problema.** Esta ferramenta
existe justamente porque ainda não sabemos que instrução dar: o estudo de
16/09/2026 (docs/ESTUDO-RESUMO-AO-VIVO.md) achou que a mesma pergunta, com e sem
gramática de esquema, produz modelos diferentes — quatro dos seis escreviam só a
primeira seção **por causa do esquema**, e não por não saberem responder. Então
aqui os dois entram por opção, e o padrão é **sem esquema e sem sistema**: é o
que menos assume.

**As réguas são genéricas de propósito.** Uma pergunta livre não tem gabarito,
mas tem uma coisa falsificável: **o que a resposta afirma e a transcrição não
contém**. Três listas, e nenhuma delas dá veredito — elas apontam onde olhar:

  termos de fora     palavra em maiúscula na resposta que não está na
                     transcrição nem na pergunta. É como se pega nome de pessoa
                     inventado, que é o pior defeito medido neste projeto.
  números de fora    o mesmo, para número que dimensiona algo. Reusa a régua
                     do comparar_modelos_de_ata.py, que já aprendeu a ignorar
                     conta em voz alta e hipótese.
  citações de fora   trecho entre aspas na resposta que não aparece na
                     transcrição. Modelo que "cita" o que ninguém disse.

E o que sempre vale: carga, resposta, tokens, tok/s e a VRAM que o modelo
ocupou na placa — medida pelo nvidia-smi, antes e depois de subir.

**Roda o llama.cpp do próprio app**, em Windows, pelos binários que a instalação
já tem, com CUDA. Medir com outro build mediria outra coisa.

O WSL não alcança o ``127.0.0.1`` do Windows (rede em NAT), então o HTTP sai
pelo ``curl.exe`` do Windows. É feio e é o que funciona.

Uso::

    tools/comparar_modelos_de_pergunta.py \\
        --gravacao 2026-09-15_14-01-30 --ate 1200 \\
        --pergunta "O que já foi discutido nesta reunião?" \\
        --modelo qwen3-4b-instruct-q4km.gguf \\
        --modelo ministral-3-3b-q4km.gguf

    # com uma instrução de sistema para testar, e com esquema, para comparar
    tools/comparar_modelos_de_pergunta.py --sistema instrucao.txt --esquema ...
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from comparar_modelos_de_ata import numeros_materiais  # noqa: E402

ACERVO = Path("/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings")
INSTALACAO = Path("/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores/ata")
BIN = INSTALACAO / "bin" / "llama-server.exe"
CURL = Path("/mnt/c/Windows/System32/curl.exe")
SMI = Path("/mnt/c/Windows/System32/nvidia-smi.exe")
TASKKILL = Path("/mnt/c/Windows/System32/taskkill.exe")

#: Onde procurar um `.gguf` dado pelo nome: a instalação oficial primeiro, e
#: depois a pasta de estudo — que é onde candidato ainda não promovido mora.
PASTAS_DE_MODELO = [INSTALACAO / "modelos", Path("/mnt/c/Users/andre/pulsemeet-estudo/modelos")]

PORTA = 8751

#: O esquema de um campo só, igual ao da produção. Só é usado com `--esquema`.
ESQUEMA = {"type": "object", "properties": {"resposta": {"type": "string"}},
           "required": ["resposta"], "additionalProperties": False}

#: Palavras que começam frase em português e viriam em maiúscula sem serem nome
#: próprio. Sem esta lista, "Ainda", "Nada" e "Como" saem como invenção.
COMUNS = {
    "a", "à", "ainda", "algo", "alguém", "ao", "aos", "apenas", "as", "às", "até",
    "cada", "caso", "com", "como", "da", "das", "de", "decisões", "do", "dos", "e",
    "ela", "ele", "em", "entre", "essa", "esse", "esta", "este", "eu", "foi", "há",
    "isso", "já", "mas", "na", "nada", "nas", "não", "no", "nos", "num", "numa",
    "o", "os", "ou", "para", "pela", "pelo", "por", "porque", "que", "quem", "se",
    "sem", "ser", "seu", "sua", "também", "tem", "um", "uma", "durante", "após",
    "sobre", "todos", "toda", "todo", "quando", "onde", "qual", "quais", "nenhuma",
    "nenhum", "resposta", "pergunta", "reunião", "transcrição", "você",
}

PALAVRA = re.compile(r"\b[A-ZÁÉÍÓÚÂÊÔÃÕÀÇ][\wÁÉÍÓÚÂÊÔÃÕÀÇáéíóúâêôãõàç]{2,}\b")
CITACAO = re.compile(r"[\"“”«»]([^\"“”«»\n]{12,})[\"“”«»]")


# ───────────────────────────────────────────────────────── o texto da reunião

def texto_da_reuniao(pasta: Path, ate: float, fonte: str) -> str:
    """A reunião até o segundo `ate`, no formato que o modelo recebe.

    **Turnos, e não trechos.** Falas seguidas da mesma pessoa viram uma linha
    só: é o formato da legenda ao vivo, e custa bem menos token que uma linha
    por trecho com carimbo — 3,7 caracteres por token contra os 2,74 medidos no
    formato carimbado (docs/ATA.md).
    """
    if fonte == "legenda":
        # A legenda não tem carimbo: o corte por tempo não existe, e pedi-lo
        # em silêncio devolveria a reunião inteira fingindo estar cortada.
        d = json.loads((pasta / "legenda.json").read_text(encoding="utf-8"))
        if ate:
            raise SystemExit("a legenda ao vivo não tem carimbo de tempo — "
                             "use --ate 0, ou --fonte final para cortar.")
        return "\n".join(f"{'Você' if t['dono'] else 'Outra pessoa'}: {t['texto'].strip()}"
                         for t in d["turnos"] if t["texto"].strip())

    segs = json.loads((pasta / "transcricao.json").read_text(encoding="utf-8"))["segments"]
    if ate:
        segs = [s for s in segs if s["start"] < ate]

    linhas, atual, quem = [], [], None
    for s in segs:
        q = s.get("speaker") or "Desconhecido"
        if q != quem:
            if atual:
                linhas.append(f"{quem}: {' '.join(atual)}")
            atual, quem = [], q
        atual.append(s["text"].strip())
    if atual:
        linhas.append(f"{quem}: {' '.join(atual)}")
    return "\n".join(linhas)


# ───────────────────────────────────────────────────────────────── as réguas

def _normalizar(t: str) -> str:
    return re.sub(r"\s+", " ", t).lower()


def de_fora(resposta: str, fonte: str) -> dict:
    """O que a resposta afirma e a transcrição não contém.

    Não é veredito: é onde olhar. A pergunta e a instrução entram na fonte
    porque o modelo pode legitimamente repetir palavra que veio delas.
    """
    fonte_n = _normalizar(fonte)

    termos = sorted({p for p in PALAVRA.findall(resposta)
                     if p.lower() not in COMUNS and p.lower() not in fonte_n})
    numeros = sorted(numeros_materiais(resposta) - numeros_materiais(fonte))
    citacoes = [c for c in CITACAO.findall(resposta) if _normalizar(c) not in fonte_n]
    return {"termos": termos, "numeros": numeros, "citacoes": citacoes}


#: Palavras funcionais do português. Uma resposta em inglês não tem nenhuma —
#: é o jeito mais barato de pegar o modelo que trocou de idioma.
PT = re.compile(r"\b(que|não|para|com|uma|dos|das|foi|pelo|sobre|ainda|então)\b", re.I)


def parece_portugues(t: str) -> bool:
    return len(PT.findall(t)) >= 3


# ─────────────────────────────────────────────────────────────────── o motor

def achar_modelo(nome: str) -> Path:
    p = Path(nome)
    if p.is_absolute() and p.exists():
        return p
    for pasta in PASTAS_DE_MODELO:
        if (pasta / nome).exists():
            return pasta / nome
    raise SystemExit(f"não achei o modelo {nome} em {[str(x) for x in PASTAS_DE_MODELO]}")


def _win(p: Path) -> str:
    r = subprocess.run(["wslpath", "-w", str(p)], capture_output=True, text=True)
    return r.stdout.strip() or str(p)


def vram_mib() -> int:
    try:
        r = subprocess.run([str(SMI), "--query-gpu=memory.used",
                            "--format=csv,noheader,nounits"],
                           capture_output=True, text=True, timeout=15)
        return int(r.stdout.strip().split("\n")[0])
    except Exception:
        return -1


def matar():
    subprocess.run([str(TASKKILL), "/IM", "llama-server.exe", "/F"], capture_output=True)
    time.sleep(2)


def perguntar(modelo: Path, corpo: dict, troca: Path, rotulo: str,
              contexto: int) -> dict:
    """Sobe, pergunta, mata. Uma subida por modelo, como a produção faz."""
    matar()
    base = vram_mib()
    log = troca / f"{rotulo}.log"

    t0 = time.time()
    proc = subprocess.Popen(
        [str(BIN), "-m", _win(modelo), "-ngl", "99", "-c", str(contexto),
         "-ctk", "q8_0", "-ctv", "q8_0", "-fa", "on",
         "--host", "127.0.0.1", "--port", str(PORTA),
         "--jinja", "--no-warmup", "-np", "1"],
        stdout=log.open("wb"), stderr=subprocess.STDOUT)

    subiu = None
    for _ in range(240):
        r = subprocess.run([str(CURL), "-s", "-m", "3",
                            f"http://127.0.0.1:{PORTA}/health"],
                           capture_output=True, text=True)
        if "ok" in r.stdout:
            subiu = time.time() - t0
            break
        if proc.poll() is not None:
            return {"erro": "o servidor morreu ao subir",
                    "log": log.read_text(errors="replace")[-600:]}
        time.sleep(1)
    if subiu is None:
        matar()
        return {"erro": "não respondeu ao /health em 4 min"}

    pico = vram_mib()
    pedido = troca / f"{rotulo}-pedido.json"
    pedido.write_text(json.dumps(corpo, ensure_ascii=False), encoding="utf-8")
    resp = troca / f"{rotulo}-resposta.json"

    t1 = time.time()
    subprocess.run([str(CURL), "-s", "-m", "900", "-X", "POST",
                    f"http://127.0.0.1:{PORTA}/v1/chat/completions",
                    "-H", "Content-Type: application/json",
                    "--data-binary", f"@{pedido.name}", "-o", resp.name],
                   cwd=str(troca), capture_output=True)
    parede = time.time() - t1
    matar()

    try:
        d = json.loads(resp.read_text(encoding="utf-8"))
        esc = d["choices"][0]
    except Exception as e:
        return {"erro": f"resposta ilegível: {e}"}

    cru = esc["message"]["content"] or ""

    # **O pensamento não é resposta, e tem que sair da conta.** O llama-server
    # devolve `reasoning_content` quando consegue separar; quando não consegue,
    # o `<think>...</think>` vem dentro do próprio content. Medir os dois juntos
    # daria um modelo "mais verboso" que na verdade só pensou mais alto.
    pensamento = esc["message"].get("reasoning_content") or ""
    if not pensamento and "<think>" in cru:
        fim = cru.find("</think>")
        if fim >= 0:
            pensamento = cru[cru.find("<think>") + 7:fim]
            cru = cru[fim + 8:].lstrip()
        else:
            pensamento, cru = cru, ""   # pensou e não sobrou resposta

    try:
        texto, preso = json.loads(cru)["resposta"], True
    except Exception:
        texto, preso = cru, False

    t = d.get("timings", {})
    return {
        "texto": texto,
        "esquema_respeitado": preso,
        "carga_s": round(subiu, 1),
        "resposta_s": round(parede, 1),
        "prompt_tokens": d["usage"]["prompt_tokens"],
        "saida_tokens": d["usage"]["completion_tokens"],
        "tok_por_s": round(t.get("predicted_per_second", 0), 1),
        "finish": esc.get("finish_reason"),
        "pensamento_chars": len(pensamento),
        "pensou": bool(pensamento),
        "vram_mib": pico - base if pico > 0 and base > 0 else -1,
    }


# ─────────────────────────────────────────────────────────────────── a volta

def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--gravacao", required=True,
                    help="nome da pasta no acervo, ou caminho completo")
    ap.add_argument("--ate", type=float, default=0,
                    help="segundos: só o que foi dito antes disso. 0 = tudo")
    ap.add_argument("--fonte", choices=["final", "legenda"], default="final",
                    help="a passada final (tem carimbo) ou a legenda ao vivo")
    ap.add_argument("--pergunta", action="append", required=True,
                    help="repetível: cada pergunta roda em cada modelo")
    ap.add_argument("--sistema", default="",
                    help="arquivo com a instrução de sistema. Vazio = nenhuma")
    ap.add_argument("--esquema", action="store_true",
                    help="prender a saída ao esquema JSON, como a ata faz")
    ap.add_argument("--pensar", action="store_true",
                    help="ligar o raciocínio explícito. Só quatro modelos leem a chave: "
                         "qwen3-1.7b e smollm3-3b pensam por padrão SEM ela, e "
                         "qwen3.5-4b e gemma-4-e4b só pensam COM ela. O "
                         "qwen3-4b-instruct e o ministral-3-3b nem a leem")
    ap.add_argument("--saida-max", type=int, default=1024,
                    help="max_tokens. Pensar consome deste mesmo orçamento, "
                         "então com --pensar convém subir")
    ap.add_argument("--modelo", action="append", required=True,
                    help="repetível: nome do .gguf")
    ap.add_argument("--contexto", type=int, default=0,
                    help="tokens. 0 = calcula do tamanho do prompt")
    ap.add_argument("--saida", default="/mnt/c/Users/andre/pulsemeet-estudo",
                    help="onde deixar as respostas e o resultados.json")
    a = ap.parse_args()

    pasta = Path(a.gravacao) if "/" in a.gravacao else ACERVO / a.gravacao
    if not pasta.is_dir():
        raise SystemExit(f"não achei a gravação em {pasta}")

    troca = Path(a.saida)
    troca.mkdir(parents=True, exist_ok=True)

    transcricao = texto_da_reuniao(pasta, a.ate, a.fonte)
    sistema = Path(a.sistema).read_text(encoding="utf-8").strip() if a.sistema else ""

    # 2,5 caracteres por token é a constante conservadora do MotorDeAta, e ela
    # erra para o lado seguro: medido em 16/09/2026, o formato em turnos dá 3,3
    # a 3,8. Mais 1.024 de saída e uma folga para a instrução.
    contexto = a.contexto or max(
        4096, ((int(len(transcricao) / 2.5) + 2048 + 4095) // 4096) * 4096)

    print(f"reunião: {pasta.name} | fonte: {a.fonte}"
          + (f" | corte: {a.ate:.0f}s" if a.ate else " | inteira"))
    print(f"texto: {len(transcricao)} caracteres | contexto: {contexto}")
    print(f"sistema: {len(sistema)} caracteres" if sistema else "sistema: NENHUM")
    print(f"esquema: {'sim' if a.esquema else 'não'}\n")

    resultados = []
    for nome in a.modelo:
        modelo = achar_modelo(nome)
        for i, pergunta in enumerate(a.pergunta, 1):
            rotulo = f"{modelo.stem}--p{i}"
            usuario = (f"=== TRANSCRIÇÃO DA REUNIÃO ===\n{transcricao}\n"
                       f"=== FIM DA TRANSCRIÇÃO ===\n\n{pergunta}")

            mensagens = ([{"role": "system", "content": sistema}] if sistema else []) \
                + [{"role": "user", "content": usuario}]
            corpo = {"messages": mensagens, "temperature": 0.3,
                     "max_tokens": a.saida_max,
                     "chat_template_kwargs": {"enable_thinking": bool(a.pensar)}}
            if a.esquema:
                corpo["response_format"] = {
                    "type": "json_schema",
                    "json_schema": {"name": "resposta", "strict": True, "schema": ESQUEMA}}

            print(f"-- {rotulo}…", flush=True)
            r = perguntar(modelo, corpo, troca, rotulo, contexto)
            if "erro" in r:
                print(f"   ERRO: {r['erro']}", flush=True)
                resultados.append({"modelo": modelo.stem, "pergunta": pergunta, **r})
                continue

            texto = r.pop("texto")
            (troca / f"{rotulo}.md").write_text(texto, encoding="utf-8")
            fora = de_fora(texto, transcricao + " " + sistema + " " + pergunta)

            linha = {"modelo": modelo.stem, "pergunta": pergunta, **r,
                     "chars": len(texto), "portugues": parece_portugues(texto),
                     **{f"fora_{k}": v for k, v in fora.items()}}
            resultados.append(linha)
            print(f"   {r['resposta_s']}s · {r['saida_tokens']} tk · "
                  f"{r['vram_mib']} MiB · pensou {r['pensamento_chars']} chars · "
                  f"resposta {len(texto)} chars", flush=True)

    (troca / "resultados.json").write_text(
        json.dumps(resultados, ensure_ascii=False, indent=2), encoding="utf-8")

    print(f"\n{'modelo':<26}{'resp':>7}{'tk':>6}{'tok/s':>7}{'VRAM':>8}"
          f"{'pensou':>8}{'resp.ch':>9}{'termos':>8}{'citaç':>7}")
    for r in resultados:
        if "erro" in r:
            print(f"{r['modelo']:<26}  {r['erro'][:50]}")
            continue
        print(f"{r['modelo']:<26}{r['resposta_s']:>6.1f}s{r['saida_tokens']:>6}"
              f"{r['tok_por_s']:>7.1f}{r['vram_mib']:>7}M"
              f"{r['pensamento_chars']:>8}{r['chars']:>9}"
              f"{len(r['fora_termos']):>8}{len(r['fora_citacoes']):>7}")

    print(f"\nrespostas e pedidos em {troca}")
    print("As três colunas de 'fora' apontam onde olhar, e não dão veredito:")
    print("  termos  palavra em maiúscula que não está na transcrição")
    print("  núm     número que dimensiona algo e não foi dito")
    print("  citaç   trecho entre aspas que ninguém falou")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
