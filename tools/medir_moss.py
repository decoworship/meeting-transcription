#!/usr/bin/env python3
"""T1.1–1.3: o MOSS-Transcribe-Diarize contra o pipeline de dois motores.

**Por que ele interessa.** O `OpenMOSS-Team/MOSS-Transcribe-Diarize` (0,9 B,
Apache-2.0, aberto em 09/07/2026) faz transcrição **e** falante numa passada só.
Se ele empatar com o que temos, some a atribuição por sobreposição temporal — e
com ela o erro que a `docs/FASE6.md` §4.1 documenta, em que a fala de quem divide
um segmento longo com outra pessoa desaparece.

Ele venceu o 2º MLC-SLM do Interspeech 2026, numa prova que inclui ~200 h de
português. **Mas nada disso foi medido nesta máquina, neste acervo, nesta placa.**

**Os três testes, e o que cada um pode matar:**

* **T1.1** — MOSS na gravação inteira, contra o pipeline de hoje. Se perder feio
  aqui, não há o que discutir;
* **T1.2** — MOSS em bloco de 3 min, que é o regime do produto A da Fase 7. Aqui
  há um risco específico: ele foi construído para **até 90 minutos numa passada**,
  com 128k de contexto. Bloco curto pode estar fora do regime dele — que é
  exatamente o modo de falha que a Fase 0 mediu no `faster-whisper` (14,36% →
  62,78% em áudio muito emendado);
* **T1.3** — VRAM e tempo na RTX 2060 de 6 GB. No teste mínimo, 2 minutos de
  áudio custaram **3,32 GB de pico**. Se isso crescer com a duração, a gravação
  de 48 min não cabe, e o T1.1 morre por memória em vez de por qualidade.

Roda no ``mix.wav``: o MOSS separa falantes por conta própria, então recebe a
conversa inteira — inclusive o dono, que no nosso pipeline vem da faixa do
microfone.

**Roda em GGUF, não em PyTorch — e a diferença é a medição inteira.** A primeira
versão desta ferramenta usava o `transformers`, e ali o MOSS estourava 8,88 GB
aos 8 minutos de áudio numa placa de 6 GB, a menos de 1× o tempo real. Aquilo
**não era o modelo, era o runtime**: o PyTorch codifica o áudio inteiro de uma
vez. O `transcribe.cpp` fatia em blocos de 30 s, codifica cada um e concatena —
a memória para de crescer com a duração. Ver `docs/FASE7-RESULTADOS.md` §7.

Montagem, com binário pronto (não precisa compilar)::

    uv venv ~/.cache/pulsemeet-medicoes/venv-moss --python 3.12
    VIRTUAL_ENV=~/.cache/pulsemeet-medicoes/venv-moss uv pip install \\
        transcribe-cpp huggingface_hub numpy
    VIRTUAL_ENV=~/.cache/pulsemeet-medicoes/venv-moss uv pip install --reinstall \\
        "https://github.com/handy-computer/transcribe.cpp/releases/download/v0.2.3/\\
transcribe_cpp_native_cu12-0.2.3-py3-none-manylinux_2_27_x86_64.manylinux_2_28_x86_64.whl"

**Não passe ``language``.** O build GGUF declara ``languages = ('en', 'zh')`` e
recusa ``pt`` — mas transcreve português corretamente quando o parâmetro é
omitido. A lista de capacidades do port está subdeclarada; o modelo não está.

Uso::

    ~/.cache/pulsemeet-medicoes/venv-moss/bin/python tools/medir_moss.py \\
        --varredura ~/.cache/pulsemeet-medicoes/varredura-moss

Depois, as réguas de sempre (no venv do projeto)::

    uv run python tools/wer_contra_gemini.py <varredura>/<grav>__moss* \\
        --gemini "<acervo>/<grav>/gemini.md"
    uv run python tools/comparar_com_gemini.py <varredura>/<grav>__moss_inteiro \\
        --gemini "<acervo>/<grav>/gemini.md"
"""

from __future__ import annotations

import argparse
import json
import sys
import wave
import time
from pathlib import Path

ACERVO_PADRAO = "/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings"
REPO_GGUF = "handy-computer/moss-transcribe-diarize-gguf"
ARQUIVO_GGUF = "MOSS-Transcribe-Diarize-Q5_K_M.gguf"   # 0,70 GB

COM_GEMINI = [
    "2026-08-21_11-00-33",   # 7,7 min
    "2026-08-25_08-59-22",   # 14,6 min
    "2026-08-20_15-59-20",   # 32,1 min
    "2026-08-27_15-28-37",   # 48,5 min
]

#: Tokens de saída por minuto de áudio. Medido grosseiramente: uma reunião
#: rende ~130 palavras/min, e cada segmento carrega dois carimbos de tempo e um
#: rótulo. Folga de 3× porque ficar sem orçamento **trunca a transcrição em
#: silêncio**, que é o pior jeito de errar esta medição.
TOKENS_POR_MINUTO = 400
TETO_DE_TOKENS = 40_000


def carregar(backend: str):
    import transcribe_cpp as t
    from huggingface_hub import hf_hub_download

    caminho = hf_hub_download(REPO_GGUF, ARQUIVO_GGUF)
    t0 = time.perf_counter()
    modelo = t.Model(caminho, backend=backend)
    print(f"{ARQUIVO_GGUF} em {backend}, carregado em "
          f"{time.perf_counter()-t0:.1f}s", flush=True)
    return modelo


def _ler(caminho: Path):
    import numpy as np

    with wave.open(str(caminho), "rb") as w:
        if w.getsampwidth() != 2 or w.getnchannels() != 1:
            raise SystemExit(f"{caminho.name}: esperava WAV mono de 16 bits")
        taxa = w.getframerate()
        bruto = w.readframes(w.getnframes())
    return np.frombuffer(bruto, dtype=np.int16).astype(np.float32) / 32768.0, taxa


def transcrever(modelo, pcm, taxa: int) -> dict:
    """Uma passada. Sem ``language``: ver a nota no cabeçalho."""
    import transcribe_cpp as t

    t0 = time.perf_counter()
    r = t.transcribe(modelo, pcm, diarize="on", timestamps="segment")
    return {
        "segmentos": [{"inicio": s.t0_ms / 1000, "fim": s.t1_ms / 1000,
                       "falante": f"S{s.speaker_id}", "texto": " " + s.text.strip()}
                      for s in r.segments],
        "segundos": time.perf_counter() - t0,
    }


def por_blocos(modelo, pcm, taxa: int, bloco: float) -> dict:
    passo = int(bloco * taxa)
    n = (len(pcm) + passo - 1) // passo
    segs, gasto = [], 0.0
    for i in range(n):
        pedaco = pcm[i * passo:(i + 1) * passo]
        if len(pedaco) < taxa:
            continue
        r = transcrever(modelo, pedaco, taxa)
        desloc = i * bloco
        # O rótulo é local ao bloco: o S1 daqui não é o S1 do vizinho. Marcar a
        # origem impede a régua de fingir que são a mesma pessoa — costurá-los é
        # trabalho do vetor de voz, e não é medido aqui.
        segs += [{"inicio": s["inicio"] + desloc, "fim": s["fim"] + desloc,
                  "falante": f"b{i}_{s['falante']}", "texto": s["texto"]}
                 for s in r["segmentos"]]
        gasto += r["segundos"]
    return {"segmentos": segs, "segundos": gasto, "blocos": n}


def despejar(destino: Path, saida: dict, duracao: float) -> None:
    destino.mkdir(parents=True, exist_ok=True)
    (destino / "transcricao.json").write_text(json.dumps({
        "language": "pt", "duration": duracao,
        "segments": [{"start": s["inicio"], "end": s["fim"],
                      "text": s["texto"], "speaker": s["falante"]}
                     for s in saida["segmentos"]],
    }, ensure_ascii=False), encoding="utf-8")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--acervo", default=ACERVO_PADRAO)
    ap.add_argument("--gravacoes")
    ap.add_argument("--bloco", type=float, default=180.0)
    ap.add_argument("--backend", default="cuda", help="cuda | cpu")
    ap.add_argument("--varredura", required=True)
    ap.add_argument("--json")
    ap.add_argument("--so-blocos", action="store_true")
    args = ap.parse_args()

    acervo = Path(args.acervo)
    nomes = ([n.strip() for n in args.gravacoes.split(",")] if args.gravacoes
             else list(COM_GEMINI))
    modelo = carregar(args.backend)
    resultados = []

    for nome in nomes:
        mix = acervo / nome / "mix.wav"
        if not mix.is_file():
            print(f"  [pulei] {nome}: sem mix.wav", file=sys.stderr)
            continue
        pcm, taxa = _ler(mix)
        dur = len(pcm) / taxa
        print(f"\n{nome}  ({dur/60:.1f} min)", flush=True)
        linha = {"gravacao": nome, "minutos": round(dur / 60, 1),
                 "backend": args.backend}

        if not args.so_blocos:
            r = transcrever(modelo, pcm, taxa)
            print(f"    inteiro     {r['segundos']:7.1f}s  {dur/r['segundos']:5.2f}x  "
                  f"{len(r['segmentos'])} seg  "
                  f"{len({s['falante'] for s in r['segmentos']})} falantes", flush=True)
            despejar(Path(args.varredura) / f"{nome}__moss_inteiro", r, dur)
            linha["inteiro"] = {"segundos": round(r["segundos"], 1),
                                "rtf": round(dur / r["segundos"], 2),
                                "segmentos": len(r["segmentos"]),
                                "falantes": len({s["falante"] for s in r["segmentos"]})}

        c = por_blocos(modelo, pcm, taxa, args.bloco)
        print(f"    bloco {args.bloco/60:.0f} min {c['segundos']:7.1f}s  "
              f"{dur/c['segundos']:5.2f}x  {len(c['segmentos'])} seg  "
              f"({c['blocos']} blocos)", flush=True)
        despejar(Path(args.varredura) / f"{nome}__moss_bloco{int(args.bloco)}s", c, dur)
        linha["bloco"] = {"segundos": round(c["segundos"], 1),
                          "rtf": round(dur / c["segundos"], 2),
                          "segmentos": len(c["segmentos"]), "blocos": c["blocos"]}
        resultados.append(linha)

    if args.json and resultados:
        Path(args.json).write_text(json.dumps(resultados, ensure_ascii=False,
                                              indent=2), encoding="utf-8")
        print(f"\nbruto em {args.json}")
    print(f"\nvariantes em {args.varredura} — agora rode as réguas no venv do projeto.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
