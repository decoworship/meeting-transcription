#!/usr/bin/env python3
"""Qwen3-ASR contra o Nemotron, e os dois contra o motor de hoje.

**A pergunta que decide, e ela não é a WER.** Um motor só serve a este app se
devolver **trechos com tempo**. A diarização atribui falante por sobreposição
temporal com o trecho, o ``VozDoDono`` corta na fronteira, a revisão lista
trechos e a ata os lê. Medido em 11/09/2026, o **Nemotron devolve 1 segmento
para um bloco de 3 minutos** — 412 palavras num trecho de [2,32 s – 180,08 s] —,
e isso o desqualifica como motor de transcrição por mais que o texto empate.

Então a primeira coluna do relatório é **segmentos por bloco**, e ela vem antes
de qualquer medida de qualidade. Um motor que devolve um trecho por bloco está
reprovado, e as outras colunas dele são curiosidade.

**Por que o Qwen3-ASR interessa**, apesar disso: o GGUF é oficial do
``ggml-org`` — a organização do próprio llama.cpp — e roda no
``llama-server.exe`` que **este app já embarca** para o motor de ata (build
10427, com ``mtmd.dll``). Se ele passar, é motor novo com **zero dependência
nova**, e abre a porta para dispensar o ``transcribe.cpp`` inteiro
(``docs/CONVERGENCIA.md`` §4, ``S2``).

**O que se sabe contra, antes de medir** — e as duas coisas são para conferir,
não para supor: o llama.cpp tem defeito aberto de *"no content for longer audio
files"*, e a saída vem poluída com ``language XXXX<asr_text>``, que esta
ferramenta tira.

**O motor de hoje entra de graça.** A referência de `large-v3` é o
``transcricao.json`` que o app já escreveu para a mesma gravação: é a saída real
do pipeline, sem custar GPU nenhuma para reproduzir.

Uso::

    V=~/.cache/pulsemeet-medicoes/venv-moss
    VIRTUAL_ENV=$V $V/bin/python tools/comparar_asr.py --gravacao 2026-08-21_11-00-33

    # só um motor, para iterar
    ... tools/comparar_asr.py --gravacao ... --motores qwen3asr

As quatro gravações com gabarito do Gemini são ``2026-08-20_15-59-20`` (32 min),
``2026-08-21_11-00-33`` (7 min), ``2026-08-25_08-59-22`` (14 min) e
``2026-08-27_15-28-37`` (48 min). O texto de cada motor é gravado em disco para
o ``tools/wer_contra_gemini.py`` pontuar depois.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid
import wave
from pathlib import Path

import numpy as np

ACERVO = Path("/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings")
SAIDA = Path.home() / ".cache" / "pulsemeet-medicoes" / "comparar-asr"
TAXA = 16000

LLAMA = Path("/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores/ata/bin"
             "/llama-server.exe")
#: **No disco do Windows, e não no do WSL.** O ``llama-server.exe`` é binário
#: Windows: passar a ele ``/home/andre/...`` devolve *"No such file or
#: directory"*, porque aquele caminho não existe para quem o executa. Os pesos
#: moram aqui e os argumentos passam pelo ``wslpath -w``.
QWEN = Path("/mnt/c/Users/andre/pulsemeet-modelos")

NEMOTRON = (Path.home() / ".cache/huggingface/hub"
            / "models--handy-computer--nemotron-3.5-asr-streaming-0.6b-gguf/snapshots"
            / "6d44e540bc31b0de1dbe174a3cea87f53a7f22fb"
            / "nemotron-3.5-asr-streaming-0.6b-Q8_0.gguf")
MOSS = (Path.home() / ".cache/huggingface/hub"
        / "models--handy-computer--moss-transcribe-diarize-gguf")

#: A poluição conhecida do Qwen3-ASR no llama.cpp (issue #26749).
LIXO = re.compile(r"language\s+\w+\s*<asr_text>|</?asr_text>", re.I)


def escrever_wav(caminho: Path, pcm: np.ndarray) -> None:
    with wave.open(str(caminho), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(TAXA)
        w.writeframes((np.clip(pcm, -1, 1) * 32767).astype("<i2").tobytes())


def ler_audio(pasta: Path) -> np.ndarray:
    import soundfile as sf

    fonte = pasta / "mix.wav"
    if not fonte.exists():
        fonte = pasta / "system.wav"
    onda, taxa = sf.read(str(fonte), dtype="float32")
    if onda.ndim > 1:
        onda = onda[:, 0]
    if taxa != TAXA:
        raise SystemExit(f"{fonte.name}: esperado {TAXA} Hz, veio {taxa}")
    return onda


# ── os motores de transcribe.cpp ───────────────────────────────────────────
def rodar_transcribe_cpp(gguf: Path, blocos, *, idioma, diarize):
    """Devolve [(segundos, [(ini, fim, texto)])] por bloco."""
    import transcribe_cpp as t

    modelo = t.Model(str(gguf), backend="cuda")
    saida = []
    for pcm in blocos:
        kw = {"timestamps": "segment"}
        if idioma:
            kw["language"] = idioma
        if diarize:
            kw["diarize"] = "on"
        t0 = time.perf_counter()
        try:
            r = t.transcribe(modelo, pcm, **kw)
            gasto = time.perf_counter() - t0
            saida.append((gasto, [(s.t0_ms / 1000, s.t1_ms / 1000, s.text)
                                  for s in r.segments]))
        except Exception as e:
            saida.append((time.perf_counter() - t0, None))
            print(f"    bloco falhou: {type(e).__name__}: {str(e)[:90]}", flush=True)
    return saida


# ── o Qwen3-ASR, pelo llama-server que o app já embarca ────────────────────
def para_windows(p: Path) -> str:
    """O caminho como o executável Windows o enxerga."""
    return subprocess.run(["wslpath", "-w", str(p)], capture_output=True,
                          text=True, check=True).stdout.strip()


def _multipart(campos: dict[str, str], arquivo: Path) -> tuple[bytes, str]:
    lim = uuid.uuid4().hex
    corpo = b""
    for k, v in campos.items():
        corpo += (f"--{lim}\r\nContent-Disposition: form-data; name=\"{k}\"\r\n\r\n"
                  f"{v}\r\n").encode()
    corpo += (f"--{lim}\r\nContent-Disposition: form-data; name=\"file\"; "
              f"filename=\"{arquivo.name}\"\r\n"
              "Content-Type: audio/wav\r\n\r\n").encode()
    corpo += arquivo.read_bytes() + f"\r\n--{lim}--\r\n".encode()
    return corpo, f"multipart/form-data; boundary={lim}"


def rodar_qwen(blocos, tmp: Path, porta: int = 8791):
    modelo = QWEN / "Qwen3-ASR-0.6B-Q8_0.gguf"
    mmproj = QWEN / "mmproj-Qwen3-ASR-0.6B-Q8_0.gguf"
    for f in (LLAMA, modelo, mmproj):
        if not f.exists():
            print(f"    falta {f}", file=sys.stderr)
            return None

    proc = subprocess.Popen(
        [str(LLAMA), "-m", para_windows(modelo), "--mmproj", para_windows(mmproj),
         "--port", str(porta), "--host", "127.0.0.1", "-ngl", "99"],
        stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
    try:
        # Espera o servidor subir. Sem isso o primeiro bloco mede a carga do
        # modelo junto com a transcrição, e o número sai três vezes maior.
        for _ in range(120):
            try:
                urllib.request.urlopen(f"http://127.0.0.1:{porta}/health", timeout=2)
                break
            except Exception:
                time.sleep(1)
        else:
            proc.terminate()
            erro = (proc.stderr.read() or b"").decode(errors="replace")
            linhas = [l for l in erro.splitlines() if " E " in l][-3:]
            print("    llama-server não subiu:", file=sys.stderr)
            for l in linhas:
                print(f"      {l.strip()[:140]}", file=sys.stderr)
            return None

        saida = []
        for i, pcm in enumerate(blocos):
            w = tmp / f"bloco-{i}.wav"
            escrever_wav(w, pcm)
            corpo, tipo = _multipart(
                {"model": "qwen3-asr", "response_format": "verbose_json"}, w)
            req = urllib.request.Request(
                f"http://127.0.0.1:{porta}/v1/audio/transcriptions",
                data=corpo, headers={"Content-Type": tipo})
            t0 = time.perf_counter()
            try:
                with urllib.request.urlopen(req, timeout=900) as r:
                    dados = json.loads(r.read())
                gasto = time.perf_counter() - t0
                # `verbose_json` pode ou não trazer segmentos — é exatamente o
                # que se quer descobrir, então não se assume nenhum dos dois.
                segs = dados.get("segments")
                if segs:
                    trechos = [(s.get("start", 0.0), s.get("end", 0.0),
                                LIXO.sub("", s.get("text", ""))) for s in segs]
                else:
                    trechos = [(0.0, len(pcm) / TAXA,
                                LIXO.sub("", dados.get("text", "")))]
                saida.append((gasto, trechos))
            except urllib.error.HTTPError as e:
                saida.append((time.perf_counter() - t0, None))
                print(f"    bloco {i} falhou: HTTP {e.code} {e.read()[:120]!r}",
                      flush=True)
            finally:
                w.unlink(missing_ok=True)
        return saida
    finally:
        proc.terminate()
        try:
            proc.wait(timeout=20)
        except subprocess.TimeoutExpired:
            proc.kill()


# ── a referência: o que o app já escreveu ──────────────────────────────────
def do_app(pasta: Path, bloco_s: float, n_blocos: int):
    arq = pasta / "transcricao.json"
    if not arq.exists():
        return None
    segs = json.loads(arq.read_text(encoding="utf-8")).get("segments") or []
    por_bloco = []
    for n in range(n_blocos):
        de, ate = n * bloco_s, (n + 1) * bloco_s
        dentro = [(s["start"], s["end"], s.get("text", ""))
                  for s in segs if de <= s.get("start", 0) < ate]
        por_bloco.append((0.0, dentro))
    return por_bloco


def relatar(nome: str, resultado, bloco_s: float, destino: Path) -> dict:
    ok = [r for r in resultado if r[1] is not None]
    quebrados = len(resultado) - len(ok)
    segs = sum(len(r[1]) for r in ok)
    palavras = sum(len(" ".join(t[2] for t in r[1]).split()) for r in ok)
    tempo = sum(r[0] for r in ok)
    audio = len(ok) * bloco_s

    texto = "\n".join(t[2].strip() for r in ok for t in r[1] if t[2].strip())
    destino.write_text(texto, encoding="utf-8")

    por_bloco = segs / len(ok) if ok else 0
    print(f"  {nome:<12} {len(ok):>3} blocos  {segs:>5} segs  "
          f"{por_bloco:>6.1f}/bloco  {palavras:>6} palavras  "
          + (f"{audio/tempo:>6.2f}x" if tempo > 0 else "     —")
          + (f"  ({quebrados} falharam)" if quebrados else ""))
    return {"blocos_ok": len(ok), "blocos_quebrados": quebrados,
            "segmentos": segs, "segs_por_bloco": por_bloco,
            "palavras": palavras, "segundos": tempo,
            "xrt": audio / tempo if tempo > 0 else None}


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--gravacao", required=True)
    p.add_argument("--bloco", type=float, default=3.0, help="minutos por bloco")
    p.add_argument("--max-blocos", type=int, default=0, help="0 = a gravação toda")
    p.add_argument("--motores", default="app,nemotron,qwen3asr",
                   help="app,nemotron,moss,qwen3asr")
    p.add_argument("--json", type=Path)
    a = p.parse_args()

    pasta = ACERVO / a.gravacao
    if not pasta.is_dir():
        print(f"não achei {pasta}", file=sys.stderr)
        return 2

    SAIDA.mkdir(parents=True, exist_ok=True)
    tmp = SAIDA / "tmp"
    tmp.mkdir(exist_ok=True)

    onda = ler_audio(pasta)
    bloco_s = a.bloco * 60
    passo = int(bloco_s * TAXA)
    blocos = [onda[i:i + passo] for i in range(0, len(onda), passo)]
    blocos = [b for b in blocos if len(b) >= TAXA]          # nada abaixo de 1 s
    if a.max_blocos:
        blocos = blocos[:a.max_blocos]

    print(f"{a.gravacao} · {len(onda)/TAXA/60:.1f} min · "
          f"{len(blocos)} blocos de {a.bloco:.0f} min"
          + ("  · com gabarito do Gemini" if (pasta / "gemini.md").exists() else ""))
    print(f"\n  {'motor':<12} {'blocos':>9}  {'segs':>5}  {'por bloco':>9}  "
          f"{'palavras':>8}  {'vel':>6}\n  " + "-" * 62)

    quais = [m.strip() for m in a.motores.split(",") if m.strip()]
    resumo = {}
    for motor in quais:
        destino = SAIDA / f"{a.gravacao}.{motor}.txt"
        if motor == "app":
            r = do_app(pasta, bloco_s, len(blocos))
        elif motor == "nemotron":
            r = rodar_transcribe_cpp(NEMOTRON, blocos, idioma="pt-BR", diarize=False)
        elif motor == "moss":
            g = next(MOSS.rglob("*.gguf"), None)
            r = rodar_transcribe_cpp(g, blocos, idioma=None, diarize=True) if g else None
        elif motor == "qwen3asr":
            r = rodar_qwen(blocos, tmp)
        else:
            print(f"  motor desconhecido: {motor}", file=sys.stderr)
            continue
        if r is None:
            print(f"  {motor:<12} indisponível")
            continue
        resumo[motor] = relatar(motor, r, bloco_s, destino)

    print("\n  O que decide: **segs por bloco**. Um motor que devolve ~1 não tem")
    print("  estrutura de tempo, e o pipeline inteiro depende dela.")
    print(f"\n  textos em {SAIDA}/  — pontuar com tools/wer_contra_gemini.py")

    if a.json:
        a.json.write_text(json.dumps(resumo, indent=2, ensure_ascii=False),
                          encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
