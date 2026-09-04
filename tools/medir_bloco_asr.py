#!/usr/bin/env python3
"""Mede o que o troceamento em blocos custa ao ASR — o T0.1 da rodada 0.

**A pergunta.** A Fase 7 propõe rodar o pipeline em blocos de poucos minutos
durante a reunião, em vez de uma passada só no fim. Isso muda o texto? E
quanto?

**Por que a resposta não é adivinhável.** A Fase 0 mediu o ``faster-whisper``
indo de 14,36% para **62,78%** de WER em áudio muito emendado (uma junção a
cada 1,6 s): regime curto já quebrou um motor neste projeto. Por outro lado o
app roda com ``condition_on_previous_text=False``, então o modelo **já não**
carrega contexto entre as janelas de 30 s dele — o que sugere que a perda seja
pequena e concentrada nas bordas. As duas hipóteses são plausíveis; só medindo.

**A régua.** A passada inteira é a referência; cada tamanho de bloco é a
hipótese. Os dois lados saem do **mesmo modelo, com os mesmos parâmetros de
produção** (``motores/asr/motor.py``), no mesmo áudio — então a diferença é o
troceamento e nada mais. Sem pós-processamento dos dois lados: filtro de
silêncio e correção fonética entrariam como confundidor.

O corte é **ingênuo, sem sobreposição**: é o piso honesto. Emenda com janela
sobreposta só melhora, e se o ingênuo já passar, não precisa existir.

Uso::

    uv run python tools/medir_bloco_asr.py --gravacoes 2026-08-21_11-00-33
    uv run python tools/medir_bloco_asr.py --blocos 60,180,300 --json saida.json
    uv run python tools/medir_bloco_asr.py --modelo medium --limite 2
"""

from __future__ import annotations

import argparse
import json
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from benchmark_wer import normalizar, taxa_de_erro  # noqa: E402

ACERVO_PADRAO = "/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings"

# As quatro gravações que têm transcrição paralela do Gemini. São elas que
# servem a todas as réguas deste projeto (docs/AUDITORIA-ATAS.md §9).
COM_GEMINI = [
    "2026-08-21_11-00-33",   # 7,7 min
    "2026-08-25_08-59-22",   # 14,6 min
    "2026-08-20_15-59-20",   # 32,1 min
    "2026-08-27_15-28-37",   # 48,5 min
]

TAXA = 16_000

# Exatamente os de motores/asr/motor.py. Divergir aqui mediria outro motor.
PARAMETROS = dict(
    language="pt",
    beam_size=5,
    condition_on_previous_text=False,
    word_timestamps=True,
    hallucination_silence_threshold=2.0,
    vad_filter=True,
    vad_parameters=dict(
        min_silence_duration_ms=500,
        max_speech_duration_s=25,
        threshold=0.25,
    ),
)


def ler_audio(caminho: Path):
    import soundfile as sf

    dados, taxa = sf.read(str(caminho), dtype="float32", always_2d=False)
    if dados.ndim > 1:
        dados = dados.mean(axis=1)
    if taxa != TAXA:
        raise SystemExit(f"{caminho.name}: esperava {TAXA} Hz, veio {taxa} Hz")
    return dados


def transcrever(modelo, audio, rotulo: str) -> dict:
    """Uma passada, e o que ela custou."""
    t0 = time.perf_counter()
    geracao, _ = modelo.transcribe(audio, **PARAMETROS)
    segmentos = [{"inicio": s.start, "fim": s.end, "texto": s.text} for s in geracao]
    gasto = time.perf_counter() - t0
    texto = "".join(s["texto"] for s in segmentos)
    print(f"    {rotulo:<16} {gasto:6.1f}s  {len(segmentos):4d} seg  "
          f"{len(normalizar(texto).split()):5d} palavras", flush=True)
    return {"segmentos": segmentos, "texto": texto, "segundos": gasto}


def por_blocos(modelo, audio, tamanho: float) -> dict:
    """Corta o áudio em blocos exatos e transcreve um por um.

    O modelo fica carregado entre os blocos — é o que o app faria, e medir a
    recarga aqui mediria uma decisão de engenharia que ninguém tomaria.
    """
    passo = int(tamanho * TAXA)
    n = (len(audio) + passo - 1) // passo
    t0 = time.perf_counter()
    segmentos = []
    for i in range(n):
        pedaco = audio[i * passo:(i + 1) * passo]
        if len(pedaco) < TAXA // 10:      # menos de 100 ms de sobra: nada a ouvir
            continue
        geracao, _ = modelo.transcribe(pedaco, **PARAMETROS)
        deslocamento = i * tamanho
        for s in geracao:
            segmentos.append({
                "inicio": s.start + deslocamento,
                "fim": s.end + deslocamento,
                "texto": s.text,
            })
    gasto = time.perf_counter() - t0
    texto = "".join(s["texto"] for s in segmentos)
    print(f"    bloco {tamanho/60:>4.0f} min    {gasto:6.1f}s  {len(segmentos):4d} seg  "
          f"{len(normalizar(texto).split()):5d} palavras  ({n} blocos)", flush=True)
    return {"segmentos": segmentos, "texto": texto, "segundos": gasto, "blocos": n}


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--acervo", default=ACERVO_PADRAO)
    ap.add_argument("--gravacoes", help="nomes separados por vírgula; padrão: as com Gemini")
    ap.add_argument("--blocos", default="60,180,300")
    ap.add_argument("--modelo", default="large-v3")
    ap.add_argument("--limite", type=int, help="para depois de N gravações")
    ap.add_argument("--json", help="grava o resultado bruto")
    ap.add_argument("--varredura", help=(
        "despeja cada variante como <dir>/<gravacao>__<variante>/transcricao.json, "
        "no formato que tools/wer_contra_gemini.py consome. É o que permite "
        "perguntar se a divergência do bloco é degradação ou só outra escolha: "
        "se as duas variantes divergem do Gemini na mesma medida, o bloco não "
        "piorou — mudou"))
    args = ap.parse_args()

    acervo = Path(args.acervo)
    nomes = ([n.strip() for n in args.gravacoes.split(",")] if args.gravacoes
             else list(COM_GEMINI))
    if args.limite:
        nomes = nomes[:args.limite]
    tamanhos = [float(t) for t in args.blocos.split(",") if t.strip()]

    from faster_whisper import WhisperModel

    import torch
    cuda = torch.cuda.is_available()
    print(f"modelo {args.modelo} em {'cuda' if cuda else 'cpu'}", flush=True)
    modelo = WhisperModel(args.modelo, device="cuda" if cuda else "cpu",
                          compute_type="float16" if cuda else "int8")

    resultados = []
    for nome in nomes:
        mix = acervo / nome / "mix.wav"
        if not mix.is_file():
            print(f"  [pulei] {nome}: sem mix.wav", file=sys.stderr)
            continue

        audio = ler_audio(mix)
        minutos = len(audio) / TAXA / 60
        print(f"\n{nome}  ({minutos:.1f} min)", flush=True)

        def despejar(variante: str, saida: dict) -> None:
            if not args.varredura:
                return
            destino = Path(args.varredura) / f"{nome}__{variante}"
            destino.mkdir(parents=True, exist_ok=True)
            (destino / "transcricao.json").write_text(json.dumps({
                "language": "pt",
                "duration": len(audio) / TAXA,
                "segments": [{"start": s["inicio"], "end": s["fim"],
                              "text": s["texto"], "speaker": None}
                             for s in saida["segmentos"]],
            }, ensure_ascii=False), encoding="utf-8")

        inteiro = transcrever(modelo, audio, "inteiro")
        despejar("inteiro", inteiro)
        linha = {
            "gravacao": nome,
            "minutos": round(minutos, 1),
            "inteiro": {k: inteiro[k] for k in ("segundos",)}
                       | {"palavras": len(normalizar(inteiro["texto"]).split()),
                          "segmentos": len(inteiro["segmentos"])},
            "blocos": [],
        }

        for t in tamanhos:
            corte = por_blocos(modelo, audio, t)
            despejar(f"bloco{int(t)}s", corte)
            wer = taxa_de_erro([inteiro["texto"]], [corte["texto"]])
            perdidas = (len(normalizar(inteiro["texto"]).split())
                        - len(normalizar(corte["texto"]).split()))
            print(f"      → WER contra a inteira: {100*wer['taxa']:5.2f}%"
                  f"   ({wer['erros']} erros em {wer['unidades']} palavras,"
                  f" saldo {perdidas:+d})", flush=True)
            linha["blocos"].append({
                "bloco_s": t,
                "n_blocos": corte["blocos"],
                "segundos": round(corte["segundos"], 1),
                "segmentos": len(corte["segmentos"]),
                "palavras": len(normalizar(corte["texto"]).split()),
                "wer": round(wer["taxa"], 5),
                "erros": wer["erros"],
                "unidades": wer["unidades"],
                "saldo_palavras": perdidas,
            })
        resultados.append(linha)

    if resultados:
        print(f"\n{'═' * 66}\nRESUMO — WER do bloco contra a passada inteira\n{'═' * 66}")
        cab = "  gravação              min  " + "".join(
            f"{t/60:>7.0f} min" for t in tamanhos)
        print(cab)
        for r in resultados:
            celulas = "".join(f"{100*b['wer']:>10.2f}%" for b in r["blocos"])
            print(f"  {r['gravacao']:<20} {r['minutos']:>5.1f}{celulas}")

        print("\n  agregado (soma dos erros / soma das palavras)")
        for i, t in enumerate(tamanhos):
            erros = sum(r["blocos"][i]["erros"] for r in resultados)
            unid = sum(r["blocos"][i]["unidades"] for r in resultados)
            saldo = sum(r["blocos"][i]["saldo_palavras"] for r in resultados)
            print(f"    bloco {t/60:>4.0f} min   WER {100*erros/unid:5.2f}%"
                  f"   ({erros} em {unid}, saldo de palavras {saldo:+d})")

    if args.json and resultados:
        Path(args.json).write_text(
            json.dumps({"modelo": args.modelo, "gravacoes": resultados},
                       ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"\nbruto em {args.json}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
