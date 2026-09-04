#!/usr/bin/env python3
"""O T0.2 da rodada 0: o bloco cabe na reunião, com a chamada rodando?

**O que a rodada 0 já sabe.** Transcrever um bloco de 1 minuto custa ~14 s de
RTX 2060 no pior caso medido — 15% a 24% de ciclo de trabalho
(``docs/FASE7-RESULTADOS.md`` §1.3). **Mas isso foi medido com a placa livre.**
Durante uma reunião ela está codificando vídeo para o Meet, e a
[FASE7.md](../docs/FASE7.md) §4.2 aponta o risco: perder essa disputa faz o áudio
do usuário travar para os outros — o pior tipo de defeito, porque quem sofre não
é quem instalou o app.

**As duas metades da pergunta, e por que só uma é automática:**

1. **o bloco continua terminando a tempo?** Um bloco de 1 min precisa ser
   processado em menos de 60 s, senão a fila cresce sem limite e o produto não
   existe. Isto o script mede sozinho;

2. **a gravação sofre?** Esta é objetiva e o app já a responde: o
   ``meta.json`` registra ``dropped_samples`` — amostras perdidas porque a fila
   encheu, que é exatamente o sintoma de a máquina não dar conta do áudio. **Nas
   53 gravações do acervo esse número é zero**, medido em 28/08/2026. Qualquer
   valor acima de zero no teste é sinal inequívoco. Use ``--conferir``.

O que este script **não** mede: se os outros participantes ouviram você picotado.
Isso não tem sensor — pergunte a eles no fim da reunião.

## Como fazer o teste

1. entre numa reunião de verdade, com o app gravando como sempre;
2. **neste terminal**, rode::

       uv run python tools/carga_de_bloco.py

   ele carrega o modelo e passa a transcrever um bloco de 1 min a cada 60 s,
   imitando a carga do produto. Deixe rodando a reunião inteira;
3. no fim, ``Ctrl+C`` aqui e encerre a gravação no app;
4. confira a gravação que acabou de sair::

       uv run python tools/carga_de_bloco.py --conferir "<pasta da gravação>"

5. pergunte aos outros se te ouviram picotado.

O passo 2 usa áudio do acervo, não o da reunião em curso — o que importa é a
carga na placa, não o que está sendo transcrito.
"""

from __future__ import annotations

import argparse
import json
import signal
import statistics
import sys
import time
from pathlib import Path

ACERVO_PADRAO = "/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings"
TAXA = 16_000

# Medido em 28/08/2026 na RTX 2060, placa livre, large-v3 fp16.
# docs/FASE7-RESULTADOS.md §1.3.
RTF_LIVRE_PIOR = 4.24
RTF_LIVRE_MELHOR = 6.85

# Abaixo disto o trecho é silêncio para o VAD, e não serve de carga. O RMS de
# fala nas gravações do acervo fica em 0,03–0,10 (ver Nucleo/VozDoDono.cs).
RMS_MINIMO = 5e-3
TENTATIVAS_POR_BLOCO = 200

# Os mesmos de motores/asr/motor.py.
PARAMETROS = dict(
    language="pt",
    beam_size=5,
    condition_on_previous_text=False,
    word_timestamps=True,
    hallucination_silence_threshold=2.0,
    vad_filter=True,
    vad_parameters=dict(min_silence_duration_ms=500, max_speech_duration_s=25,
                        threshold=0.25),
)

_parar = False


def _sinal(*_):
    global _parar
    if _parar:
        return
    _parar = True
    print("\n  (parando depois deste bloco…)", flush=True)


# ── conferência da gravação ─────────────────────────────────────────────


def linha_de_base(acervo: Path) -> dict:
    """O que é normal no acervo, para o teste ter contra o que comparar."""
    quedas, correcoes = [], []
    for p in sorted(acervo.iterdir()):
        m = p / "meta.json"
        if not m.is_file():
            continue
        try:
            d = json.loads(m.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue
        dur = d.get("duration_s") or 0
        if dur <= 0:
            continue
        faixas = d.get("tracks") or {}
        quedas.append(sum((faixas.get(k) or {}).get("dropped_samples", 0) or 0
                          for k in ("mic", "system")))
        correcoes.append(sum((faixas.get(k) or {}).get("drift_corrections", 0) or 0
                             for k in ("mic", "system")) / (dur / 60))
    correcoes.sort()
    return {
        "n": len(quedas),
        "com_queda": sum(1 for q in quedas if q > 0),
        "correcoes_mediana": correcoes[len(correcoes) // 2] if correcoes else 0.0,
        "correcoes_p90": correcoes[int(0.9 * len(correcoes))] if correcoes else 0.0,
        "correcoes_max": max(correcoes) if correcoes else 0.0,
    }


def conferir(pasta: Path, acervo: Path) -> int:
    m = pasta / "meta.json"
    if not m.is_file():
        print(f"{pasta} não tem meta.json", file=sys.stderr)
        return 1
    d = json.loads(m.read_text(encoding="utf-8"))
    dur = d.get("duration_s") or 0
    faixas = d.get("tracks") or {}

    base = linha_de_base(acervo)
    print(f"\nlinha de base: {base['n']} gravações do acervo")
    print(f"  com dropped_samples > 0 .......... {base['com_queda']}")
    print(f"  correções de deriva por minuto .... mediana {base['correcoes_mediana']:.1f}"
          f"  p90 {base['correcoes_p90']:.1f}  máx {base['correcoes_max']:.1f}")

    print(f"\n{pasta.name}   {dur/60:.1f} min")
    queda_total = 0
    for nome in ("mic", "system"):
        f = faixas.get(nome) or {}
        q = f.get("dropped_samples", 0) or 0
        queda_total += q
        print(f"  {nome:<7} dropped_samples {q:>8}   drift_corrections "
              f"{f.get('drift_corrections', 0):>5}   maior silêncio "
              f"{f.get('longest_silence_s', 0):>6.1f}s")

    cpm = (sum((faixas.get(k) or {}).get("drift_corrections", 0) or 0
               for k in ("mic", "system")) / (dur / 60)) if dur else 0.0
    print(f"\n  correções por minuto: {cpm:.1f}")

    print()
    if queda_total > 0:
        print("  ❌ REPROVOU — houve perda de amostra, e no acervo inteiro isso")
        print("     nunca aconteceu. A carga na GPU atrapalhou a captura.")
        return 2
    if cpm > base["correcoes_max"]:
        print(f"  ⚠️  ATENÇÃO — {cpm:.1f} correções/min está acima do máximo já visto")
        print(f"     no acervo ({base['correcoes_max']:.1f}). Não é perda de áudio, mas")
        print("     é sinal de relógio sob pressão. Repita o teste antes de concluir.")
        return 3
    print("  ✅ PASSOU — nenhuma amostra perdida, e a deriva ficou dentro do")
    print("     que o acervo já mostra sem carga nenhuma.")
    print("\n  Falta a metade que não tem sensor: pergunte aos outros")
    print("  participantes se te ouviram picotado.")
    return 0


# ── a carga ─────────────────────────────────────────────────────────────


def rodar(acervo: Path, bloco: float, modelo_nome: str) -> int:
    import soundfile as sf
    import torch
    from faster_whisper import WhisperModel

    fonte = next((p / "mix.wav" for p in sorted(acervo.iterdir())
                  if (p / "mix.wav").is_file()), None)
    if fonte is None:
        print("nenhum mix.wav no acervo para usar como carga", file=sys.stderr)
        return 1

    audio, taxa = sf.read(str(fonte), dtype="float32", always_2d=False)
    if audio.ndim > 1:
        audio = audio.mean(axis=1)
    if taxa != TAXA:
        print(f"{fonte.name}: {taxa} Hz, esperava {TAXA}", file=sys.stderr)
        return 1

    cuda = torch.cuda.is_available()
    print(f"carga: {modelo_nome} em {'cuda' if cuda else 'cpu'}, "
          f"blocos de {bloco/60:.0f} min, áudio de {fonte.parent.name}")
    modelo = WhisperModel(modelo_nome, device="cuda" if cuda else "cpu",
                          compute_type="float16" if cuda else "int8")

    print(f"\n  a placa livre leva {bloco/RTF_LIVRE_MELHOR:.0f}–{bloco/RTF_LIVRE_PIOR:.0f} s"
          f" por bloco de {bloco/60:.0f} min.")
    print(f"  o limite é {bloco:.0f} s: acima disso a fila cresce e o produto não existe.")
    print(f"\n  Ctrl+C UMA vez para parar. O bloco em curso termina antes"
          f" (~{bloco/RTF_LIVRE_PIOR:.0f} s), e só então ele sai — insistir não acelera.\n")
    print(f"  {'bloco':>5} {'gasto':>8} {'RTF':>7} {'folga':>8}   veredito")
    print(f"  {'-'*5} {'-'*8} {'-'*7} {'-'*8}   {'-'*20}")

    signal.signal(signal.SIGINT, _sinal)
    passo = int(bloco * TAXA)
    tempos, i = [], 0

    while not _parar:
        # Trecho mudo não gera carga: o VAD descarta tudo e a inferência
        # termina em milissegundos. Uma gravação do acervo tem centenas de
        # segundos de silêncio, então é preciso procurar onde há fala — senão
        # este script mede o nada e diz que coube.
        pedaco = None
        for _ in range(TENTATIVAS_POR_BLOCO):
            inicio = (i * passo) % max(1, len(audio) - passo)
            candidato = audio[inicio:inicio + passo]
            i += 1
            if len(candidato) < TAXA:
                i = 0
                continue
            if float((candidato ** 2).mean()) ** 0.5 >= RMS_MINIMO:
                pedaco = candidato
                break
        if pedaco is None:
            print("  nenhum trecho com fala encontrado — troque de gravação com --acervo",
                  file=sys.stderr)
            return 1

        t0 = time.perf_counter()
        list(modelo.transcribe(pedaco, **PARAMETROS)[0])
        gasto = time.perf_counter() - t0
        tempos.append(gasto)

        rtf = bloco / gasto
        folga = bloco - gasto
        marca = "ok" if folga > 0 else "NÃO COUBE"
        if folga > 0 and gasto > bloco * 0.7:
            marca = "apertado"
        print(f"  {len(tempos):>5} {gasto:>7.1f}s {rtf:>6.2f}x {folga:>+7.1f}s   {marca}",
              flush=True)

        # Espera o resto do período, que é o que o produto faria: um bloco por
        # janela, não um atrás do outro. Sem isto mediríamos saturação, não
        # ciclo de trabalho.
        alvo = t0 + bloco
        while not _parar and time.perf_counter() < alvo:
            time.sleep(0.5)

    if not tempos:
        return 1

    print(f"\n  {len(tempos)} blocos   mediana {statistics.median(tempos):.1f}s"
          f"   pior {max(tempos):.1f}s   limite {bloco:.0f}s")
    estourou = sum(1 for t in tempos if t >= bloco)
    apertado = sum(1 for t in tempos if bloco * 0.7 <= t < bloco)
    if estourou:
        print(f"\n  ❌ {estourou} bloco(s) passaram do período. O bloco de "
              f"{bloco/60:.0f} min não cabe nesta máquina com a chamada aberta.")
    elif apertado:
        print(f"\n  ⚠️  {apertado} bloco(s) acima de 70% do período. Cabe, com pouca folga.")
    else:
        print(f"\n  ✅ todos os blocos couberam com folga. Pior caso usou "
              f"{100*max(tempos)/bloco:.0f}% do período.")

    print("\n  Agora encerre a gravação no app e rode:")
    print("    uv run python tools/carga_de_bloco.py --conferir \"<pasta da gravação>\"")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--acervo", default=ACERVO_PADRAO)
    ap.add_argument("--bloco", type=float, default=60.0, help="segundos por bloco")
    ap.add_argument("--modelo", default="large-v3")
    ap.add_argument("--conferir", help="pasta da gravação feita durante o teste")
    args = ap.parse_args()

    acervo = Path(args.acervo)
    if not acervo.is_dir():
        print(f"acervo não encontrado: {acervo}", file=sys.stderr)
        return 1

    if args.conferir:
        return conferir(Path(args.conferir), acervo)
    return rodar(acervo, args.bloco, args.modelo)


if __name__ == "__main__":
    raise SystemExit(main())
