#!/usr/bin/env python3
"""O Parakeet TDT v3 em ONNX, contra o motor de hoje, nas quatro com gabarito.

**Por que ele está sendo medido.** Dois candidatos a motor de transcrição caíram
em 11/09/2026 pelo mesmo motivo, e nenhum dos dois pela qualidade do texto: o
Nemotron devolve **1 segmento** para um bloco de 3 min, e o Qwen3-ASR pelo
llama.cpp não tem campo de segmento nenhum. Sem estrutura de tempo o pipeline
inteiro colapsa — a diarização atribui falante por sobreposição com o trecho, o
``VozDoDono`` corta na fronteira, a revisão lista trechos e a ata os lê.

**O Parakeet passa nesse teste**, e é o primeiro que passa: 372 tokens com
carimbo num bloco de 3 min, resolução de 80–160 ms. E há duas coisas a mais que
o tornam interessante além de "mais um modelo":

* **roda em ONNX Runtime**, que este app já embarca (40 MB) — sem torch, sem
  CTranslate2, sem transcribe.cpp;
* **rodou em CPU** nesta máquina, a ~3 a 4× o tempo real, porque o
  ``onnxruntime-gpu`` instalado pede CUDA 13 e aqui há CUDA 12. **Isso é
  acidente, não desenho** — mas um ASR que não disputa a placa muda o orçamento
  inteiro da reunião ao vivo, e por isso o número de CPU é reportado como
  resultado e não como nota de rodapé.

**A régua é a do projeto**, e ela não é WER: ``tools/wer_contra_gemini.py`` mede
**quanto duas transcrições independentes discordam** do export do Gemini/Meet. O
número absoluto não significa nada; só a comparação entre colunas do mesmo áudio.
Ver o cabeçalho daquele arquivo e a ``docs/FASE7-RESULTADOS.md`` §0.5.

**Os segmentos são construídos por silêncio**, e isso é uma escolha desta
ferramenta, não do modelo: o Parakeet devolve token com carimbo, e agrupar por
intervalo de silêncio é o que mais se aproxima do que o ``faster-whisper`` faz.
O limiar está em :data:`PAUSA_S`.

Uso::

    V=~/.cache/pulsemeet-medicoes/venv-parakeet
    VIRTUAL_ENV=$V $V/bin/python tools/medir_parakeet.py
    VIRTUAL_ENV=$V $V/bin/python tools/medir_parakeet.py --gravacoes 2026-08-21_11-00-33

Depois::

    uv run python tools/wer_contra_gemini.py \\
        ~/.cache/pulsemeet-medicoes/varredura-parakeet/*/parakeet \\
        ~/.cache/pulsemeet-medicoes/varredura-parakeet/*/app
"""

from __future__ import annotations

import argparse
import json
import shutil
import time
from pathlib import Path

import numpy as np

ACERVO = Path("/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings")
RAIZ = Path.home() / ".cache" / "pulsemeet-medicoes"
SAIDA = Path.home() / ".cache" / "pulsemeet-medicoes" / "varredura-parakeet"
TAXA = 16000
BLOCO_S = 180.0

#: O silêncio que fecha um trecho. 0,6 s é a pausa entre frases faladas; abaixo
#: disso corta no meio da frase, acima gruda duas falas numa só.
PAUSA_S = 0.6

#: Teto de duração de um trecho, para uma fala corrida não virar um bloco só —
#: que é exatamente o defeito que reprovou o Nemotron.
TETO_S = 30.0

#: As quatro do acervo que têm export do Gemini em paralelo.
COM_GABARITO = ("2026-08-20_15-59-20", "2026-08-21_11-00-33",
                "2026-08-25_08-59-22", "2026-08-27_15-28-37")


def segmentar(tokens, carimbos, deslocamento: float):
    """Token com carimbo → trechos, cortando no silêncio."""
    trechos, atual, inicio, anterior = [], [], None, None
    for tok, ts in zip(tokens, carimbos):
        if inicio is None:
            inicio = ts
        elif (ts - anterior > PAUSA_S) or (ts - inicio > TETO_S):
            trechos.append((inicio + deslocamento, anterior + deslocamento,
                            "".join(atual)))
            atual, inicio = [], ts
        atual.append(tok)
        anterior = ts
    if atual and inicio is not None:
        trechos.append((inicio + deslocamento, anterior + deslocamento,
                        "".join(atual)))
    return [(a, b, t) for a, b, t in trechos if t.strip()]


def escrever(pasta: Path, trechos, gemini: Path | None) -> None:
    pasta.mkdir(parents=True, exist_ok=True)
    (pasta / "transcricao.json").write_text(json.dumps(
        {"segments": [{"start": a, "end": b, "text": t} for a, b, t in trechos]},
        ensure_ascii=False, indent=1), encoding="utf-8")
    if gemini and gemini.exists():
        shutil.copy(gemini, pasta / "gemini.md")


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--gravacoes", nargs="*", default=list(COM_GABARITO))
    #: **A justiça da comparação mora aqui.** O `large-v3` do app roda em fp16;
    #: medir o Parakeet em int8 contra ele mistura "modelo pior" com "quantização
    #: mais agressiva", e as duas coisas têm consertos diferentes.
    p.add_argument("--quantizacao", choices=("int8", "fp32"), default="int8")
    #: A segunda assimetria: o app faz uma passada só, e esta ferramenta corta em
    #: blocos de 3 min. `--inteira` mede o custo disso em vez de argumentá-lo.
    p.add_argument("--inteira", action="store_true",
                   help="uma passada só, sem cortar em blocos")
    p.add_argument("--sufixo", default="", help="nome da variante na saída")
    p.add_argument("--dispositivo", choices=("cpu", "gpu"), default="cpu")
    p.add_argument("--modelo", default="", help="pasta do modelo, para variantes")
    p.add_argument("--json", type=Path)
    a = p.parse_args()

    import onnx_asr
    import soundfile as sf

    pasta_modelo = Path(a.modelo) if a.modelo else RAIZ / (
        "parakeet" if a.quantizacao == "int8" else "parakeet-fp32")
    variante = "parakeet" + (a.sufixo or f"-{a.quantizacao}"
                             + ("-inteira" if a.inteira else ""))
    print(f"carregando o Parakeet ({a.quantizacao}, {a.dispositivo.upper()}) "
          f"de {pasta_modelo}…", flush=True)
    m = onnx_asr.load_model(
        "nemo-parakeet-tdt-0.6b-v3", str(pasta_modelo),
        quantization=None if a.quantizacao == "fp32" else a.quantizacao,
        providers=["CUDAExecutionProvider"] if a.dispositivo == "gpu"
        else ["CPUExecutionProvider"]).with_timestamps()

    resumo = {}
    for g in a.gravacoes:
        origem = ACERVO / g
        if not (origem / "mix.wav").exists():
            print(f"{g}: sem mix.wav"); continue

        onda, taxa = sf.read(str(origem / "mix.wav"), dtype="float32")
        if onda.ndim > 1:
            onda = onda[:, 0]
        if a.inteira:
            blocos = [onda]
        else:
            passo = int(BLOCO_S * taxa)
            blocos = [onda[i:i + passo] for i in range(0, len(onda), passo)]
            blocos = [b for b in blocos if len(b) >= taxa]

        trechos, gasto = [], 0.0
        for i, b in enumerate(blocos):
            t0 = time.perf_counter()
            r = m.recognize(b, sample_rate=taxa)
            gasto += time.perf_counter() - t0
            trechos += segmentar(r.tokens, r.timestamps,
                                 0.0 if a.inteira else i * BLOCO_S)
            print(f"  {g} bloco {i+1}/{len(blocos)}", end="\r", flush=True)

        audio = sum(len(b) for b in blocos) / taxa
        escrever(SAIDA / g / variante, trechos, origem / "gemini.md")

        # O motor de hoje, de graça: é o que o app já escreveu.
        atual = origem / "transcricao.json"
        n_app = 0
        if atual.exists():
            segs = json.loads(atual.read_text(encoding="utf-8")).get("segments") or []
            n_app = len(segs)
            escrever(SAIDA / g / "app",
                     [(s.get("start", 0), s.get("end", 0), s.get("text", ""))
                      for s in segs], origem / "gemini.md")

        palavras = len(" ".join(t[2] for t in trechos).split())
        print(f"  {g:<22} {audio/60:5.1f} min · {variante} {len(trechos):>4} trechos "
              f"({palavras} palavras, {audio/gasto:.2f}x CPU) · app {n_app:>4} trechos")
        resumo[g] = {"minutos": audio / 60, "trechos_parakeet": len(trechos),
                     "palavras_parakeet": palavras, "xrt_cpu": audio / gasto,
                     "trechos_app": n_app}

    print(f"\nescrito em {SAIDA}/<gravacao>/{{parakeet,app}}/")
    print("agora a régua:\n  uv run python tools/wer_contra_gemini.py "
          f"{SAIDA}/*/parakeet {SAIDA}/*/app")
    if a.json:
        a.json.write_text(json.dumps(resumo, indent=2, ensure_ascii=False),
                          encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
