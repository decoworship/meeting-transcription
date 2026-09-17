#!/usr/bin/env python3
"""Reproduz o áudio de uma gravação pela legenda, para saber por que ela não firmou.

**Por que existe.** Em 15/09/2026 uma reunião de 46 minutos saiu inteira em
cinza: o Nemotron decodificou o tempo todo, mandou texto tentativo, e **nunca
confirmou uma palavra**. Como o ``legenda.json`` só é escrito quando algo firma,
a reunião não deixou rastro nenhum.

O log não ajuda — não há uma linha entre carregar o modelo e encerrar. A
pergunta que sobra é de que lado está o defeito:

* se este áudio **firma** aqui, o problema está no encanamento do app — janela,
  ritmo, fronteira de quadro — e não no modelo;
* se **não firma**, está no modelo ou no áudio, e a diferença que se via nos
  dados (o microfone mudo) deixa de ser coincidência.

**Ele alimenta o mais rápido que a placa aguentar, de propósito.** O
``LegendaDeFioAFioTests`` toca no ritmo do relógio porque mede o *percurso*;
aqui o que se mede é a **decisão do modelo**, que depende dos quadros e não do
relógio. Alimentar em tempo real custaria 46 minutos para responder o mesmo.

**A mistura é replicada como o app faz**, e isso não é preciosismo: o
``Faixas.Mix`` normaliza **por janela**, então o ganho pode mudar cinco vezes por
segundo. Misturar o arquivo inteiro de uma vez mediria outro áudio.

Uso::

    python tools/reproduzir_legenda.py <pasta-da-gravação> [--minutos 5]
"""

import argparse
import array
import os
import sys
import time
import wave

QUADRO = 3200          # 200 ms a 16 kHz, o quadro da LegendaAoVivo
TAXA = 16000


def quadros(pasta: str, minutos: float, de: float = 0.0):
    """Os quadros da mistura, como o núcleo os monta — janela por janela."""
    m = wave.open(os.path.join(pasta, "mic.wav"))
    s = wave.open(os.path.join(pasta, "system.wav"))
    inicio = int(de * 60 * TAXA)
    if inicio:
        m.setpos(min(inicio, m.getnframes())); s.setpos(min(inicio, s.getnframes()))
    limite = int(minutos * 60 * TAXA) if minutos > 0 else min(m.getnframes(), s.getnframes())
    lidas = 0
    while lidas < limite:
        n = min(QUADRO, limite - lidas)
        am = array.array("h"); am.frombytes(m.readframes(n))
        asy = array.array("h"); asy.frombytes(s.readframes(n))
        if not len(am) and not len(asy):
            break
        k = max(len(am), len(asy))
        am.extend([0] * (k - len(am))); asy.extend([0] * (k - len(asy)))

        mix = [(am[i] + asy[i]) / 32768.0 for i in range(k)]
        pico = max((abs(v) for v in mix), default=0.0)
        if pico > 1.0:                       # a normalização por janela do Mix()
            mix = [v / pico for v in mix]
        # o mesmo ida-e-volta por int16 que a ponte faz ao mandar em base64
        yield [max(-32768, min(32767, int(v * 32768))) / 32768.0 for v in mix]
        lidas += k
    m.close(); s.close()


def pelo_motor(a) -> int:
    """O mesmo áudio, mas pelo sidecar de verdade — com o destravamento dentro.

    **Um fio só para ler**, porque o motor responde quando quer: ele manda
    parcial quando algo muda ou a cada 25 quadros, e ler em linha reta depois de
    cada quadro travaria o alimentador no primeiro quadro silencioso.
    """
    import base64
    import json
    import queue
    import subprocess
    import threading

    aqui = os.path.dirname(os.path.abspath(__file__))
    motor = os.path.join(aqui, "..", "motores", "legenda", "motor.py")
    py = os.environ.get("PYTHON_DA_LEGENDA", sys.executable)
    p = subprocess.Popen([py, motor], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                         text=True, encoding="utf-8", bufsize=1)

    caixa: "queue.Queue[dict]" = queue.Queue()

    def ler():
        for linha in p.stdout:
            linha = linha.strip()
            if linha:
                try:
                    caixa.put(json.loads(linha))
                except json.JSONDecodeError:
                    pass
    threading.Thread(target=ler, daemon=True).start()

    def manda(**c):
        p.stdin.write(json.dumps(c) + "\n"); p.stdin.flush()

    estado = {"firme": "", "recomecos": 0, "texto": None}

    def drenar(ate_resultado=False, prazo=0.0):
        fim = time.perf_counter() + prazo
        while True:
            try:
                m = caixa.get(timeout=max(0.0, fim - time.perf_counter())
                              if ate_resultado else 0)
            except queue.Empty:
                if ate_resultado and time.perf_counter() < fim:
                    continue
                return
            if m.get("tipo") == "progresso" and m.get("firme") is not None:
                estado["firme"] = m["firme"]
                if m.get("recomecou"):
                    estado["recomecos"] += 1
                    print(f"   RECOMEÇO — {len(m['firme'])} chars preservados")
            elif m.get("tipo") == "resultado":
                estado["texto"] = m.get("texto", "")
                return
            elif m.get("tipo") == "erro":
                print("ERRO do motor:", m.get("erro"))
                return

    manda(id=1, op="legendar", idioma=a.idioma)
    print(f"\n{os.path.basename(a.pasta)} · pelo motor · do minuto {a.de:g}")
    print("   (carregando o modelo…)")

    n = 0
    t0 = time.perf_counter()
    for q in quadros(a.pasta, a.minutos, a.de):
        pcm = array.array("h", [max(-32768, min(32767, int(v * 32768))) for v in q])
        manda(id=1, op="audio", pcm=base64.b64encode(pcm.tobytes()).decode())
        n += 1
        drenar()
        if n % 300 == 0:
            print(f"   {n * QUADRO / TAXA:6.0f}s  firme={len(estado['firme'])}")

    manda(id=1, op="encerrar")
    drenar(ate_resultado=True, prazo=120.0)
    try:
        p.stdin.close(); p.wait(timeout=30)
    except Exception:
        p.kill()

    audio = n * QUADRO / TAXA
    d = time.perf_counter() - t0
    texto = estado["texto"] or ""
    print(f"\n{n} quadros · {audio:.0f}s em {d:.0f}s · recomeços: {estado['recomecos']}")
    print(f"firme DURANTE a reunião: {len(estado['firme'])} chars")
    print(f"texto do encerrar:       {len(texto)} chars")
    print(f"   {texto[:200]!r}")
    print("\nVEREDITO:",
          "firmou durante" if estado["firme"].strip() else "só no fim")
    return 0


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("pasta")
    p.add_argument("--minutos", type=float, default=5.0, help="0 = a gravação toda")
    p.add_argument("--de", type=float, default=0.0, help="começar no minuto N")
    p.add_argument("--idioma", default="pt-BR")
    # Forçar CPU serve para medir a máquina sem placa, que é um caso de uso real
    # e nunca foi medido com n_threads escolhido — o 0,99x da FASE7-RESULTADOS
    # §9.1 usou o padrão da biblioteca, que hoje se sabe ser o pior.
    p.add_argument("--backend", default="auto", choices=["auto", "cuda", "cpu"])
    # **O texto inteiro, para comparar entre rodadas.** Backends diferentes
    # somam em ordens diferentes, e ninguém neste projeto conferiu se o CPU e a
    # CUDA produzem o MESMO texto — imprimir 300 caracteres não responde isso.
    p.add_argument("--salvar", default=None,
                   help="arquivo onde escrever o texto firme inteiro")
    # O GGUF não mora no repo: ele vem no pacote de motores, e em desenvolvimento
    # está na instalação oficial. Ver tools/empacotar_motores.sh.
    p.add_argument("--gguf", default=None,
                   help="caminho do .gguf (padrão: ao lado do motor, no repo)")
    # **O que a diarização sobre a legenda precisaria.** A atribuição de falante
    # é por sobreposição temporal, e a legenda hoje não guarda tempo nenhum.
    # Pedir carimbo ao fluxo é o caminho óbvio, e o que falta saber é o preço:
    # a legenda tem 2,46x de margem com o Meet aberto, e pouco a perder.
    p.add_argument("--timestamps", default="none",
                   choices=["none", "segment", "word", "token"],
                   help="o que pedir de carimbo ao fluxo (padrão: none, o de hoje)")
    # O motor chama session() sem n_threads, e o padrão 0 costuma significar
    # "todas as CPUs" no ggml. Na máquina do dono do produto o sidecar come dois
    # núcleos de quatro com o modelo na CUDA.
    p.add_argument("--threads", type=int, default=0,
                   help="n_threads da sessão (0 = o padrão da biblioteca)")
    p.add_argument("--pelo-motor", action="store_true",
                   help="falar com motores/legenda/motor.py pelo protocolo, "
                        "em vez de chamar a biblioteca — é o que exercita o "
                        "recomeço do fluxo travado")
    a = p.parse_args()

    if a.pelo_motor:
        return pelo_motor(a)

    import numpy as np
    import transcribe_cpp as t

    gguf = a.gguf or os.path.join(
        os.path.dirname(os.path.abspath(__file__)), "..",
        "motores", "legenda", "modelos",
        "nemotron-3.5-asr-streaming-0.6b-Q8_0.gguf")
    if a.backend == "cpu":
        modelo = t.Model(gguf, backend="cpu"); onde = "cpu"
    else:
        try:
            modelo = t.Model(gguf, backend="cuda"); onde = "cuda"
        except Exception as e:
            print(f"sem CUDA ({e!r}) — caindo para CPU", file=sys.stderr)
            modelo = t.Model(gguf, backend="cpu"); onde = "cpu"

    sessao = modelo.session(n_threads=a.threads).stream(
        commit_policy="stable_prefix", timestamps=a.timestamps, language=a.idioma)

    print(f"\n{os.path.basename(a.pasta)} · {onde} · idioma={a.idioma} · do minuto {a.de:g}"
          f" · timestamps={a.timestamps} · n_threads={a.threads or 'padrão'}")
    print(f"{'áudio':>8} {'quadro':>7} {'firme':>7} {'tent.':>7}  primeiro firme")
    n = firmes = 0
    primeiro = None
    cpu0 = time.process_time()
    t0 = time.perf_counter()
    for q in quadros(a.pasta, a.minutos, a.de):
        u = sessao.feed(np.asarray(q, dtype=np.float32))
        n += 1
        if getattr(u, "committed_changed", False):
            firmes += 1
            if primeiro is None:
                primeiro = n * QUADRO / TAXA
        if n % 150 == 0:                      # a cada 30 s de áudio
            x = sessao.text()
            print(f"{n*QUADRO/TAXA:7.0f}s {n:7d} {len(x.committed):7d} "
                  f"{len(x.tentative):7d}  {primeiro if primeiro else '—'}")

    final = sessao.finalize()                 # noqa: F841 — fecha o prefixo
    x = sessao.text()
    d = time.perf_counter() - t0
    audio = n * QUADRO / TAXA
    cpu = time.process_time() - cpu0
    print(f"\n{n} quadros · {audio:.0f}s de áudio em {d:.0f}s ({audio/d:.2f}x)")
    print(f"CPU do processo: {cpu:.0f}s em {d:.0f}s de parede "
          f"({cpu/d:.2f} núcleos)")
    print(f"commits: {firmes} · primeiro aos "
          f"{f'{primeiro:.1f}s' if primeiro else 'NUNCA'}")
    print(f"firme  ({len(x.committed)} chars): {x.committed[:300]!r}")
    if a.salvar:
        with open(a.salvar, "w", encoding="utf-8") as f:
            f.write(x.committed)
        print(f"texto firme salvo em {a.salvar}")
    print(f"tent.  ({len(x.tentative)} chars): {x.tentative[:200]!r}")
    print("\nVEREDITO:", "firmou" if x.committed.strip() else "NÃO FIRMOU")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
