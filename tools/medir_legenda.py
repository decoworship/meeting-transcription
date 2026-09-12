#!/usr/bin/env python3
"""A legenda ao vivo em streaming de verdade — o passo R1.

**O que este teste decide.** Se existe a camada 1 da
``docs/FASE7-ROTA.md`` §3: texto sub-segundo durante a reunião, contra os 0 a 3
minutos do bloco que já está em RC. O candidato é o
``nemotron-3.5-asr-streaming-0.6b``, que roda no **mesmo ``transcribe.cpp`` que o
MOSS já empacota** — custo marginal de 0,75 GB e nenhuma dependência nova — e que
**declara ``pt-BR``**, o que o MOSS não faz.

**Por que ele merece ser remedido.** O ``0,99×`` que o rebaixou na
``docs/FASE7-RESULTADOS.md`` §9.1 é **em CPU, num Ryzen 5 3400G de 4 núcleos**, e
reprova o *bloco* — que precisa de margem para drenar fila. Uma legenda em
streaming na GPU não tem fila para drenar. E a queda de 8,46× para 3,77× entre
blocos (§9.2) está marcada lá como *"ressalva de medição, não propriedade
confirmada"*: a causa provável é estado acumulado na sessão, e existe um
``Stream.reset()`` que a medição não usou. **Este teste usa.**

As três perguntas, e nenhuma delas é sobre qualidade de texto — isso a §9.2 já
mediu (empate técnico com o MOSS):

1. **a velocidade se sustenta por 60 minutos?** Medida em baldes de 10 min, com e
   sem ``reset``, para a decadência aparecer se existir;
2. **quanto o texto demora a firmar?** Em ``stable_prefix``, o que separa
   ``committed`` de ``tentative`` é o LocalAgreement. O atraso é
   ``input_received_ms - audio_committed_ms``: o áudio que já entrou e ainda não
   virou texto firme;
3. **quanto sobra da placa?** O ciclo de trabalho no ritmo do relógio — e é a
   única das três que precisa do **Meet aberto** para valer.

**Os critérios de morte, escritos antes de medir** (ROTA §4). Se qualquer um
falhar, a saída está no §5 de lá:

* velocidade sustentada **abaixo de 1,5×**, mesmo com ``reset`` → saída ``B1``;
* ``committed`` demorando **mais de 3 s** → não é legenda, vira ``B3``;
* o Meet aberto derrubando a margem → saída ``B2``.

.. warning::
   **Esta ferramenta nunca rodou.** Ela foi escrita contra a ABI do
   ``transcribe_cpp`` 0.2.3 lida do pacote instalado, mas a máquina onde foi
   escrita não tem GPU nem o GGUF do Nemotron. A primeira execução é na máquina
   com placa, e é esperado que algo precise de ajuste — o que ela **não** pode
   fazer é dar número errado em silêncio, e por isso ela confere
   ``supports_streaming`` e o idioma declarado antes de medir.

**Onde ela roda.** No venv de medição do MOSS, e não no ``.venv`` do projeto —
é lá que vive o ``transcribe_cpp`` com o nativo de CUDA. Ver o cabeçalho de
``tools/medir_moss.py`` para como ele foi montado::

    V=~/.cache/pulsemeet-medicoes/venv-moss
    G=~/.cache/huggingface/hub/models--handy-computer--nemotron-3.5-asr-streaming-0.6b-gguf/\
snapshots/6d44e540bc31b0de1dbe174a3cea87f53a7f22fb/nemotron-3.5-asr-streaming-0.6b-Q8_0.gguf

Uso::

    # o de sempre: 60 min, ritmo do relógio, com o Meet aberto
    VIRTUAL_ENV=$V $V/bin/python tools/medir_legenda.py --gguf $G --gravacao 2026-08-12_09-59-50

    # o braço do reset, para a decadência da §9.2
    VIRTUAL_ENV=$V $V/bin/python tools/medir_legenda.py --gguf $G --gravacao ... --reset-a-cada 10

    # só a vazão, sem esperar o relógio
    VIRTUAL_ENV=$V $V/bin/python tools/medir_legenda.py --gguf $G --gravacao ... --ritmo cheio
"""

from __future__ import annotations

import argparse
import json
import statistics
import sys
import time
from pathlib import Path

import numpy as np

ACERVO = Path("/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings")
TAXA = 16000

#: O pedaço de áudio por ``feed``. 200 ms é o compromisso: menor faz o custo por
#: chamada dominar a medição, maior põe um piso artificial no atraso do
#: ``committed``, que é justamente o que se quer medir.
PEDACO_MS = 200

#: Os critérios de morte da ROTA §4, em código para não virarem lembrança.
MIN_XRT = 1.5
MAX_ATRASO_S = 3.0


def carregar(gguf: str, backend: str):
    """Sobe o modelo e confere que ele faz o que este teste supõe."""
    import transcribe_cpp as t

    t0 = time.perf_counter()
    modelo = t.Model(gguf, backend=backend)
    print(f"modelo carregado em {backend} · {time.perf_counter()-t0:.1f}s")

    caps = modelo.capabilities
    if not caps.supports_streaming:
        print("ERRO: este modelo não declara supports_streaming — "
              "não é candidato à camada 1.", file=sys.stderr)
        raise SystemExit(2)

    linguas = tuple(getattr(caps, "languages", ()) or ())
    tem_pt = any(str(x).lower().startswith("pt") for x in linguas)
    print(f"streaming: sim · línguas declaradas: {len(linguas)}"
          f" · português: {'sim' if tem_pt else 'NÃO'}")
    if not tem_pt:
        # O MOSS transcreve português sem declará-lo, então "não declara" não é
        # prova de que não serve — mas é o tipo de coisa que precisa aparecer no
        # relatório, e não ser descoberta depois.
        print("  aviso: o idioma não está declarado; passe --idioma '' para omitir "
              "o parâmetro, como o motor do MOSS faz.")
    return modelo, tem_pt


def ler_audio(pasta: Path) -> np.ndarray:
    import soundfile as sf

    mix = pasta / "mix.wav"
    fonte = mix if mix.exists() else pasta / "system.wav"
    onda, taxa = sf.read(str(fonte), dtype="float32")
    if onda.ndim > 1:
        onda = onda[:, 0]
    if taxa != TAXA:
        raise SystemExit(f"{fonte.name}: esperado {TAXA} Hz, veio {taxa}")
    print(f"áudio: {fonte.name} · {len(onda)/TAXA/60:.1f} min")
    return onda


def medir(modelo, onda, *, idioma, ritmo, minutos, reset_a_cada, tem_pt):
    """Alimenta o áudio e devolve as três medidas."""
    import transcribe_cpp as t

    passo = TAXA * PEDACO_MS // 1000
    limite = min(len(onda), int(minutos * 60 * TAXA))

    sessao = modelo.session()
    # minuto//10 -> [áudio_s somado, processamento_s somado].
    # **Somado, e não a mediana do xRT por pedaço.** A maioria dos ``feed`` só
    # enfileira e volta em microssegundos; poucos decodificam e custam caro. A
    # mediana daquilo dava 53x onde o agregado dá 3,7x — um número bonito e
    # falso. Só a razão das somas responde "acompanha a reunião?".
    baldes: dict[int, list[float]] = {}
    atrasos: list[float] = []
    processando = 0.0
    resets = 0

    kw = {"commit_policy": "stable_prefix", "timestamps": "none"}
    if idioma:
        kw["language"] = idioma

    inicio = time.perf_counter()
    fluxo = sessao.stream(**kw)
    try:
        for i in range(0, limite, passo):
            pedaco = onda[i:i + passo]
            if len(pedaco) == 0:
                break
            audio_s = len(pedaco) / TAXA

            t0 = time.perf_counter()
            u = fluxo.feed(pedaco)
            gasto = time.perf_counter() - t0
            processando += gasto

            b = baldes.setdefault(int(i / TAXA // 600), [0.0, 0.0])
            b[0] += audio_s
            b[1] += gasto

            # O atraso em tempo de áudio: o que já entrou e ainda não firmou.
            # É a medida honesta do "quanto demora a aparecer", e não depende do
            # relógio de parede nem do ritmo de alimentação.
            if u.committed_changed:
                atrasos.append(
                    max(0.0, (u.input_received_ms - u.audio_committed_ms) / 1000.0))

            # A hipótese da §9.2: o estado acumula, e o reset o devolve.
            if reset_a_cada and (i / TAXA) >= (resets + 1) * reset_a_cada * 60:
                fluxo.reset()
                fluxo = sessao.stream(**kw)
                resets += 1

            if ritmo == "relogio":
                # Ritmo do relógio: dormir o que sobrou do pedaço. Se não sobrou,
                # o motor não está acompanhando — e isso aparece no ciclo.
                sobra = audio_s - gasto
                if sobra > 0:
                    time.sleep(sobra)

        final = fluxo.finalize()
        texto = fluxo.text()
    finally:
        try:
            fluxo.reset()
        except Exception:
            pass

    parede = time.perf_counter() - inicio
    audio_total = limite / TAXA
    return {
        "audio_s": audio_total,
        "parede_s": parede,
        "processando_s": processando,
        "ciclo": processando / max(parede, 1e-9),
        "xrt_global": audio_total / max(processando, 1e-9),
        "baldes": {str(k * 10): (v[0] / v[1] if v[1] > 0 else 0.0)
                   for k, v in sorted(baldes.items())},
        "atraso_mediano_s": float(statistics.median(atrasos)) if atrasos else None,
        "atraso_p90_s": (float(np.percentile(atrasos, 90)) if atrasos else None),
        "atraso_max_s": max(atrasos) if atrasos else None,
        "commits": len(atrasos),
        "resets": resets,
        "revisao_final": getattr(final, "revision", None),
        "palavras": len(texto.committed.split()),
        "tentative_no_fim": len(texto.tentative.split()),
        "portugues_declarado": tem_pt,
    }


def relatar(r: dict) -> bool:
    print(f"\n{'='*64}")
    print(f"áudio {r['audio_s']/60:.1f} min · parede {r['parede_s']/60:.1f} min "
          f"· {r['resets']} reset(s)")
    print(f"\n1. VELOCIDADE\n   global {r['xrt_global']:.2f}x o tempo real")
    print("   por balde de 10 min (áudio ÷ processamento):")
    for minuto, x in r["baldes"].items():
        print(f"      min {minuto:>3}–{int(minuto)+10:<3} {x:6.2f}x")

    print(f"\n2. ATRASO ATÉ FIRMAR  ({r['commits']} commits)")
    if r["atraso_mediano_s"] is None:
        print("   nenhum commit — stable_prefix não emitiu nada")
    else:
        print(f"   mediana {r['atraso_mediano_s']:.2f}s · "
              f"p90 {r['atraso_p90_s']:.2f}s · máx {r['atraso_max_s']:.2f}s")

    # Só quer dizer alguma coisa no ritmo do relógio: em "cheio" ele mede
    # apenas que o laço não dorme, e dá ~95% qualquer que seja o motor.
    print(f"\n3. CICLO DE TRABALHO\n   {r['ciclo']:.1%} da parede"
          + ("" if r.get("ritmo") == "relogio"
             else "   (sem sentido no ritmo 'cheio' — rode com --ritmo relogio)"))

    print(f"\n{'='*64}\nCRITÉRIOS DE MORTE (docs/FASE7-ROTA.md §4)")
    baldes = list(r["baldes"].values())
    lento = min(baldes) if baldes else 0.0
    ok_v = lento >= MIN_XRT
    ok_a = r["atraso_mediano_s"] is not None and r["atraso_mediano_s"] <= MAX_ATRASO_S
    print(f"   velocidade ≥ {MIN_XRT}x sustentado   "
          f"{'OK' if ok_v else 'FALHOU → saída B1'}  (pior balde {lento:.2f}x)")
    print(f"   committed ≤ {MAX_ATRASO_S}s            "
          f"{'OK' if ok_a else 'FALHOU → saída B3'}")
    print("   margem com o Meet aberto        "
          "julgue pelo ciclo acima — se passou de ~50%, é a saída B2")
    return ok_v and ok_a


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--gguf", required=True, help="o GGUF do Nemotron streaming")
    p.add_argument("--gravacao", required=True, help="a pasta em MeetingRecordings")
    p.add_argument("--backend", default="cuda")
    p.add_argument("--idioma", default="pt-BR",
                   help="vazio ('') omite o parâmetro, como o motor do MOSS faz")
    p.add_argument("--ritmo", choices=("relogio", "cheio"), default="relogio",
                   help="relogio = como a reunião; cheio = só vazão")
    p.add_argument("--minutos", type=float, default=60.0)
    p.add_argument("--reset-a-cada", type=float, default=0.0,
                   help="minutos entre Stream.reset(); 0 = nunca")
    p.add_argument("--json", type=Path)
    a = p.parse_args()

    pasta = ACERVO / a.gravacao
    if not pasta.is_dir():
        print(f"gravação não encontrada: {pasta}", file=sys.stderr)
        return 2

    modelo, tem_pt = carregar(a.gguf, a.backend)
    onda = ler_audio(pasta)
    r = medir(modelo, onda, idioma=a.idioma, ritmo=a.ritmo, minutos=a.minutos,
              reset_a_cada=a.reset_a_cada, tem_pt=tem_pt)
    r |= {"gravacao": a.gravacao, "ritmo": a.ritmo, "backend": a.backend,
          "idioma": a.idioma, "reset_a_cada_min": a.reset_a_cada}
    ok = relatar(r)

    if a.json:
        a.json.write_text(json.dumps(r, indent=2, ensure_ascii=False), encoding="utf-8")
        print(f"\nescrito em {a.json}")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
