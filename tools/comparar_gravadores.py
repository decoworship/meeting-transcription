"""Critério A da Fase 1: o gravador novo empata ou ganha do Python.

Dispara os dois gravadores em paralelo sobre os mesmos dispositivos, e compara o
resultado no que importa para a transcrição:

* **duração e contagem de amostras** — o novo não pode encolher nem inflar;
* **alinhamento entre as faixas** de cada gravador, que é o que a diarização usa
  para casar quem falou;
* **deslocamento entre os dois gravadores**, por correlação cruzada em janelas ao
  longo da gravação — se ele crescer com o tempo, a âncora de deriva de um dos
  dois não está segurando;
* **meta.json campo a campo**.

A correlação em janelas, e não só no total, é o que distingue "desalinhado desde
o começo" (offset de partida, inofensivo) de "derivando" (o defeito que a âncora
existe para evitar). Foi assim que o Teste B da seção 1 do PLANO previu medir.

Uso::

    python tools/comparar_gravadores.py --segundos 120
    python tools/comparar_gravadores.py --comparar /tmp/novo/xxx /tmp/py/yyy
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import time
from pathlib import Path

import numpy as np

RAIZ = Path(__file__).resolve().parent.parent
EXE = RAIZ / "app-net/CliGravador/bin/Debug/net8.0-windows/win-x64/Capture.exe"
PY_WIN = Path("/mnt/c/Users/andre/.meeting-recorder/.venv/Scripts/python.exe")
TAXA = 16_000


def ler(caminho: Path) -> np.ndarray:
    import soundfile as sf
    sinal, taxa = sf.read(caminho, dtype="float32")
    if taxa != TAXA:
        raise SystemExit(f"{caminho}: esperado {TAXA} Hz, veio {taxa}")
    return sinal


def deslocamento(a: np.ndarray, b: np.ndarray, max_lag: int = TAXA) -> tuple[int, float]:
    """Deslocamento de `b` em relação a `a`, por correlação cruzada.

    Devolve (amostras, correlação normalizada). Correlação baixa significa que a
    medida não é confiável — tipicamente porque a janela é silêncio, e silêncio
    correlaciona com qualquer coisa.
    """
    n = min(len(a), len(b))
    if n < max_lag * 2:
        return 0, 0.0
    a = a[:n] - a[:n].mean()
    b = b[:n] - b[:n].mean()
    if a.std() < 1e-6 or b.std() < 1e-6:
        return 0, 0.0

    # FFT em vez de np.correlate: janelas de 30 s a 16 kHz tornam o cálculo
    # direto lento demais para varrer uma gravação inteira.
    tamanho = 1 << int(np.ceil(np.log2(2 * n)))
    fa = np.fft.rfft(a, tamanho)
    fb = np.fft.rfft(b, tamanho)
    corr = np.fft.irfft(fa * np.conj(fb), tamanho)
    corr = np.concatenate([corr[-max_lag:], corr[:max_lag + 1]])
    pico = int(np.argmax(np.abs(corr)))
    norm = float(np.abs(corr[pico]) / (np.linalg.norm(a) * np.linalg.norm(b) + 1e-12))
    return pico - max_lag, norm


def comparar(dir_novo: Path, dir_py: Path, janela_s: int = 30) -> int:
    problemas = 0
    nao_validado: set[str] = set()
    print(f"novo:   {dir_novo}")
    print(f"python: {dir_py}\n")

    m_novo = json.loads((dir_novo / "meta.json").read_text(encoding="utf-8"))
    m_py = json.loads((dir_py / "meta.json").read_text(encoding="utf-8"))

    print(f"{'':10s} {'novo':>26s} {'python':>26s}")
    print(f"{'duração':10s} {m_novo['duration_s']:>25.2f}s {m_py['duration_s']:>25.2f}s")

    for faixa in ("system", "mic"):
        a_novo = dir_novo / f"{faixa}.wav"
        a_py = dir_py / f"{faixa}.wav"
        if not (a_novo.is_file() and a_py.is_file()):
            continue

        s_novo, s_py = ler(a_novo), ler(a_py)

        # Faixa vazia é falha grave, não "empate". O gravador Python produz zero
        # amostras no loopback quando nada toca — é o requisito 3.6, e foi
        # observado em execução: 90 s pedidos, 0 s gravados.
        for rotulo, sinal in (("novo", s_novo), ("python", s_py)):
            if len(sinal) == 0:
                print(f"\n  ⚠ {faixa}/{rotulo}: FAIXA VAZIA — nada foi gravado")
                problemas += 1
        t_novo, t_py = m_novo["tracks"][faixa], m_py["tracks"][faixa]

        print(f"\n--- {faixa}")
        print(f"  {'amostras':22s} {len(s_novo):>12d} {len(s_py):>12d}"
              f"   dif={len(s_novo)-len(s_py):+d} ({(len(s_novo)-len(s_py))/TAXA:+.2f}s)")
        print(f"  {'correções de deriva':22s} {t_novo['drift_corrections']:>12d} "
              f"{t_py['drift_corrections']:>12d}")
        print(f"  {'deriva líquida':22s} {t_novo['drift_net_samples']:>12d} "
              f"{t_py['drift_net_samples']:>12d}")
        print(f"  {'pico rms':22s} {t_novo['peak_rms']:>12.4f} {t_py['peak_rms']:>12.4f}")

        # Deslocamento por janela: crescimento ao longo do tempo é deriva; um
        # valor constante é só offset de partida.
        passo = janela_s * TAXA
        linhas = []
        for i in range(0, min(len(s_novo), len(s_py)) - passo, passo):
            d, c = deslocamento(s_novo[i:i + passo], s_py[i:i + passo])
            linhas.append((i / TAXA, d, c))

        rms = float(np.sqrt(np.mean(s_novo ** 2))) if len(s_novo) else 0.0
        confiaveis = [(t, d) for t, d, c in linhas if c > 0.3]
        if rms < 1e-3:
            # Silêncio correlaciona com qualquer coisa: qualquer número aqui
            # mediria ruído de fundo, não alinhamento de conteúdo.
            print(f"  ⚠ conteúdo NÃO validado: faixa é silêncio (rms={rms:.5f}). "
                  "Rode com áudio tocando.")
            nao_validado.add(faixa)
        elif not confiaveis:
            print("  deslocamento: janelas sem correlação — não avaliável")
            nao_validado.add(faixa)
        else:
            print(f"  {'deslocamento por janela':22s} " +
                  "  ".join(f"{t:.0f}s:{d:+d}" for t, d in confiaveis[:8]))
            inicio, fim = confiaveis[0][1], confiaveis[-1][1]
            cresc = fim - inicio
            print(f"  crescimento ao longo da gravação: {cresc:+d} amostras "
                  f"({cresc*1000/TAXA:+.1f} ms)")
            if abs(cresc) > TAXA * 0.05:      # 50 ms
                print("    ⚠ o deslocamento cresce — deriva não contida")
                problemas += 1

    # Alinhamento interno de cada gravador: é o que a diarização consome.
    for rotulo, m, d in (("novo", m_novo, dir_novo), ("python", m_py, dir_py)):
        if not all((d / f"{f}.wav").is_file() for f in ("system", "mic")):
            continue
        dif = abs(m["tracks"]["system"]["frames"] - m["tracks"]["mic"]["frames"])
        print(f"\n  alinhamento interno ({rotulo}): {dif} amostras "
              f"({dif*1000/TAXA:.1f} ms)")

    chaves_novo = set(m_novo["tracks"]["system"].keys())
    chaves_py = set(m_py["tracks"]["system"].keys())
    if faltando := chaves_py - chaves_novo:
        print(f"\n  ⚠ meta.json: chaves do Python ausentes no novo: {sorted(faltando)}")
        problemas += 1

    print()
    if problemas:
        print(f"REPROVADO — {problemas} problema(s)")
    elif nao_validado:
        # Verde que não significa nada é pior que amarelo: quem lê conclui que
        # o conteúdo foi conferido quando só a mecânica foi.
        print(f"MECÂNICA OK, CONTEÚDO NÃO VALIDADO ({', '.join(sorted(nao_validado))})")
        print("   duração, deriva e alinhamento conferem; a comparação amostra a")
        print("   amostra exige áudio real tocando durante a gravação.")
    else:
        print("APROVADO — mecânica e conteúdo")
    return problemas


def gravar_em_paralelo(segundos: int, saida: Path) -> tuple[Path, Path]:
    saida.mkdir(parents=True, exist_ok=True)
    dir_novo, dir_py = saida / "novo", saida / "python"

    print(f"disparando os dois gravadores por {segundos}s...\n")
    p_novo = subprocess.Popen(
        [str(EXE), "--seconds", str(segundos), "--track", "both", "--out", str(dir_novo)],
        stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
    p_py = subprocess.Popen(
        [str(PY_WIN), "capture.py", "--seconds", str(segundos), "--out", str(dir_py)],
        cwd=str(RAIZ / "recorder"), stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)

    inicio = time.time()
    while p_novo.poll() is None or p_py.poll() is None:
        time.sleep(2)
        print(f"\r  {time.time()-inicio:.0f}s", end="", flush=True)
        if time.time() - inicio > segundos + 120:
            break
    print()

    def mais_recente(base: Path) -> Path:
        cands = [d for d in base.glob("*") if d.is_dir() and (d / "meta.json").is_file()]
        if not cands:
            raise SystemExit(f"nenhuma gravação em {base}")
        return max(cands, key=lambda d: d.stat().st_mtime)

    return mais_recente(dir_novo), mais_recente(dir_py)


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--segundos", type=int, default=120)
    p.add_argument("--saida", type=Path, default=Path("/tmp/criterioA"))
    p.add_argument("--comparar", type=Path, nargs=2, default=None,
                   help="comparar duas pastas já gravadas, sem gravar de novo")
    p.add_argument("--janela", type=int, default=30)
    args = p.parse_args()

    if args.comparar:
        sys.exit(1 if comparar(*args.comparar, args.janela) else 0)

    if not EXE.is_file():
        raise SystemExit(f"compile primeiro: {EXE} não existe")
    novo, py = gravar_em_paralelo(args.segundos, args.saida)
    sys.exit(1 if comparar(novo, py, args.janela) else 0)


if __name__ == "__main__":
    main()
