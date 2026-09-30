#!/usr/bin/env python3
"""O Nemotron-3 contra o pyannote na passada final, do jeito que o app monta (MOD-2).

**O que este teste decide.** Se o Nemotron-3 entra **ao lado** do pyannote como
diarizador da passada final — o ``MOD-2`` do docs/BACKLOG.md. A medição de
25/09 (docs/NEMOTRON-DIARIZACAO.md §3) já disse que ele separa melhor, mas com
duas diferenças em relação ao app que este teste tira:

1. **Ela rodou sobre o ``mix.wav``.** O app diariza o ``system.wav`` — os
   outros — e o dono vem da faixa do microfone (``Transcritor``, "A diarização
   roda só no system.wav"). Aqui os dois diarizadores recebem o ``system.wav``;
2. **Os rótulos do Nemotron eram casados com o Gemini por co-ocorrência**, e os
   do app vinham com nome. Aqui os dois passam pelo mesmo reconhecimento de
   vozes — wespeaker sobre 30 s de fala limpa de cada rótulo, e o
   ``Vozes.Reconhecer`` (limiar 0,70) contra o banco real **sem as amostras que
   vieram da própria reunião**, que tornariam o reconhecimento otimista.

As falas do dono (André Yuri no Gemini) saem da conta: elas vêm do microfone e
seriam iguais nos dois braços. O que se mede é **quem dos outros falou**, que é
onde a diarização carrega o peso.

Duas notas por diarizador, por palavra alinhada com o Gemini:

* ``separacao``: os rótulos casados com os nomes do Gemini por co-ocorrência
  (``comparar_com_gemini.casar_nomes``), a mesma régua da §3;
* ``nome``: o nome que o reconhecimento de vozes deu, contra o do Gemini, por
  nome inteiro sem acento. Rótulo não reconhecido conta como erro — é o que a
  pessoa veria antes de editar.

Uso::

    uv run $(cat uvw) --with scipy python tools/medir_mod2.py [--json saida.json]
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
import threading
import time
from collections import Counter
from difflib import SequenceMatcher
from pathlib import Path

import numpy as np
import soundfile as sf

AQUI = Path(__file__).resolve().parent
sys.path.insert(0, str(AQUI))
sys.path.insert(0, str(AQUI.parent / "motores" / "diarizacao" / "pipeline"))
import comparar_com_gemini as cg  # noqa: E402
import conferir_voz_onnx as cvo  # noqa: E402
import nemotron3_onnx as n3  # noqa: E402

ACERVO = Path("/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings")
PYANNOTE = Path("/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores"
                "/diarizacao/modelos/community-1")
REUNIOES = ["2026-08-20_15-59-20", "2026-08-21_11-00-33",
            "2026-08-25_08-59-22", "2026-08-27_15-28-37",
            "2026-09-30_09-58-30"]
DONO = "andre yuri"


def chave(nome: str) -> str:
    return " ".join(cg.sem_acento(nome).lower().split())


def mesma_pessoa(a: str, b: str) -> bool:
    """Nome inteiro, sem acento; "Aline Sena" casa com "Aline Sena de Souza".

    Primeiro nome não basta: André Yuri e André Monlevade são os dois "andre".
    """
    x, y = set(chave(a).split()), set(chave(b).split())
    return x <= y or y <= x
TAXA = 16000


# ------------------------------------------------------------------ custo

class Vigia:
    """Pico de VRAM da placa inteira enquanto um bloco roda."""

    def __init__(self):
        self.pico, self._vivo = 0, True
        threading.Thread(target=self._laco, daemon=True).start()

    @staticmethod
    def agora() -> int:
        r = subprocess.run(["nvidia-smi.exe", "--query-gpu=memory.used",
                            "--format=csv,noheader,nounits"], capture_output=True, text=True)
        return int(r.stdout.split()[0])

    def _laco(self):
        while self._vivo:
            self.pico = max(self.pico, self.agora())
            time.sleep(0.25)

    def parar(self) -> int:
        self._vivo = False
        return self.pico


# ------------------------------------------------------------------ os dois diarizadores

def mascara_pyannote(trechos: list[dict], n: int) -> tuple[np.ndarray, list[str]]:
    """(n, k) a 10 ms, a partir dos trechos {inicio, fim, falante} do pipeline."""
    rotulos = sorted({t["falante"] for t in trechos})
    m = np.zeros((n, max(1, len(rotulos))), bool)
    for t in trechos:
        m[int(t["inicio"] * 100):int(t["fim"] * 100), rotulos.index(t["falante"])] = True
    return m, rotulos


def mascara_nemotron(eng: n3.Nemotron3, onda: np.ndarray) -> tuple[np.ndarray, list[str]]:
    """Offline com o embed por bloco, como o sidecar faria (tools/medir_mod1.py)."""
    import medir_mod1 as m1
    lg, *_ = m1.streaming(eng, onda, "offline", relogio=False)
    ativo = n3.sigmoid(lg) > 0.5
    usados = [f for f in range(n3.NSPK) if ativo[:, f].sum() > 100]  # > 1 s de fala
    return ativo[:, usados], [f"spk{f}" for f in usados]


# ------------------------------------------------------------------ nomes

def banco_sem(gravacao: str) -> dict[str, list[dict]]:
    dados = json.loads((cvo.BANCO / "vozes.json").read_text(encoding="utf-8-sig"))
    pessoas = {}
    for nome, perfil in dados["pessoas"].items():
        lista = [dict(a, _i=-1, _v=np.asarray(a["vetor"], np.float64))
                 for a in perfil.get("amostras") or []
                 if not (a.get("origem") or {}).get("gravacao", "").startswith(gravacao)]
        if lista:
            pessoas[nome] = lista
    return pessoas


def nomear(wes, onda, mascara, rotulos, pessoas) -> dict[str, str | None]:
    limpo = mascara & (mascara.sum(1, keepdims=True) == 1)
    nomes = {}
    for j, r in enumerate(rotulos):
        idx = np.nonzero(limpo[:, j])[0][:3000]  # 30 s
        if len(idx) < 300:  # menos de 3 s de fala limpa: o app também não reconhece
            nomes[r] = None
            continue
        trecho = np.concatenate([onda[i * 160:(i + 1) * 160] for i in idx])
        nomes[r], _ = cvo._reconhecer(np.asarray(wes(trecho), np.float64), pessoas)
    return nomes


# ------------------------------------------------------------------ a régua

def rotular(segs, mascara, rotulos) -> list[str]:
    rot = []
    for s in segs:
        soma = mascara[int(s["start"] * 100):max(int(s["end"] * 100), int(s["start"] * 100) + 1)].sum(0)
        rot.append(rotulos[int(soma.argmax())] if soma.size and soma.max() > 0 else "?")
    return rot


def notas(pasta: Path, rot: list[str], nomes: dict[str, str | None]) -> dict:
    segs = json.loads((pasta / "transcricao.json").read_text(encoding="utf-8"))["segments"]
    nosso = [(str(k), s["text"]) for k, s in enumerate(segs) if (s.get("text") or "").strip()]
    a_txt, a_quem = cg.fila(nosso)
    b_txt, b_quem = cg.fila(cg.ler_gemini(pasta / "gemini.md"))
    pares = []
    for i, j, n in SequenceMatcher(None, a_txt, b_txt, autojunk=False).get_matching_blocks():
        for x in range(n):
            verdade = b_quem[j + x]
            if not mesma_pessoa(verdade, DONO):
                pares.append((rot[int(a_quem[i + x])], verdade))
    casamento = cg.casar_nomes(pares)
    sep = sum(casamento.get(r) == v for r, v in pares)
    nome = sum((nomes.get(r) is not None) and mesma_pessoa(nomes[r], v)
               for r, v in pares)
    return {"palavras": len(pares),
            "separacao": sep / max(len(pares), 1),
            "nome": nome / max(len(pares), 1)}


# ------------------------------------------------------------------ main

def main() -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--json", type=Path)
    a = p.parse_args()

    import onnxruntime as ort
    ort.preload_dlls()
    from diarizacao import Diarizador
    from voz import ExtratorDeVoz

    wes = ExtratorDeVoz(cvo.MODELO, preferir_gpu=True)
    saida = []
    for g in REUNIOES:
        pasta = ACERVO / g
        onda, taxa = sf.read(pasta / "system.wav", dtype="float32")
        assert taxa == TAXA
        onda = onda.mean(1) if onda.ndim > 1 else onda
        n = len(onda) // 160
        segs = json.loads((pasta / "transcricao.json").read_text(encoding="utf-8"))["segments"]
        pessoas = banco_sem(g[:10])
        gemini = Counter(chave(q) for q, _ in cg.ler_gemini(pasta / "gemini.md"))

        for nome_braco in ("pyannote", "nemotron"):
            base = Vigia.agora()
            v = Vigia()
            t0 = time.perf_counter()
            if nome_braco == "pyannote":
                d = Diarizador(PYANNOTE)
                mascara, rotulos = mascara_pyannote(d(onda, TAXA), n)
            else:
                eng = n3.Nemotron3(AQUI / "_nemotron3", gpu=True)
                mascara, rotulos = mascara_nemotron(eng, onda)
            dt = time.perf_counter() - t0
            pico = v.parar()
            if nome_braco == "pyannote":
                del d
            else:
                del eng

            nomes = nomear(wes, onda, mascara, rotulos, pessoas)
            r = {"reuniao": g, "diarizador": nome_braco,
                 "minutos": len(onda) / TAXA / 60, "segundos": dt,
                 "xrt": len(onda) / TAXA / dt, "vram_mb": pico - base,
                 "falantes": len(rotulos),
                 "falantes_gemini_sem_dono": sum(1 for q in gemini if not mesma_pessoa(q, DONO)),
                 "nomes": {k: v for k, v in nomes.items()},
                 **notas(pasta, rotular(segs, mascara, rotulos), nomes)}
            saida.append(r)
            print(f"{g[:10]} {nome_braco:9s} {r['minutos']:5.1f} min  {r['xrt']:6.0f}x  "
                  f"{r['vram_mb']:5d} MB  falantes {r['falantes']} (Gemini {r['falantes_gemini_sem_dono']})  "
                  f"separação {100 * r['separacao']:5.1f}%  nome {100 * r['nome']:5.1f}%  "
                  f"{r['nomes']}", flush=True)

    if a.json:
        a.json.write_text(json.dumps(saida, indent=2, ensure_ascii=False), encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
