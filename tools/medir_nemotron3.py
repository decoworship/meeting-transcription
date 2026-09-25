#!/usr/bin/env python3
"""As três réguas do Nemotron-3-Diarization (25/09/2026). Ver docs/NEMOTRON-DIARIZACAO.md.

``validar``
    O porte decide **o mesmo** que o ``transformers``? Mel, e decisão por quadro
    (logit > 0) no offline e no ``low_latency``, nos primeiros 3 min de uma
    gravação. É a régua do porte: um diarizador que erra não levanta exceção,
    ele só atribui a fala à pessoa errada.

``medir``
    Acerto de falante contra o Gemini, nos quatro modos, e velocidade e VRAM. O
    texto é o ``transcricao.json`` do app, **intocado**; só o falante de cada
    segmento é trocado pelo slot com mais quadros ativos dentro dele. Escreve
    uma pasta por (gravação, modo) em ``--saida`` e a pontua com
    ``comparar_com_gemini.py``. **Confira o nvidia-smi antes**: a mesma 2060
    roda a legenda de reunião de verdade, e velocidade com a placa ocupada é
    piso, não medida.

``vozes``
    Os três caminhos de memória de voz, entre as duas reuniões com Gemini
    (André Yuri e André Monlevade estão nas duas): A — o vetor wespeaker do
    app sobre os falantes do Nemotron, e o ``Reconhecer`` contra o banco real;
    C — a média de camadas internas do próprio Nemotron; B — matrícula: 15 s de
    cada pessoa conhecida **antes** da reunião fixam o slot pela ordem de
    chegada.

``validar`` e o caminho C de ``vozes`` precisam do ``transformers`` do git e
de torch com CUDA; ``medir`` e os caminhos A e B, só de onnxruntime-gpu::

    uv run --with "git+https://github.com/huggingface/transformers" \\
        --with onnxruntime-gpu==1.23.2 --with soundfile \\
        python tools/medir_nemotron3.py {validar,medir,vozes}
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

AQUI = Path(__file__).parent
sys.path.insert(0, str(AQUI))
import comparar_com_gemini as cg  # noqa: E402
import nemotron3_onnx as n3  # noqa: E402

ACERVO = Path("/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings")
#: as quatro gravações do acervo com export do Gemini/Meet (AUDITORIA-ATAS.md §9)
COM_GEMINI_TODAS = ["2026-08-20_15-59-20", "2026-08-21_11-00-33",
                    "2026-08-25_08-59-22", "2026-08-27_15-28-37"]
#: as duas que têm pessoas em comum, que é o que a régua de vozes precisa
COM_GEMINI = {"0820": "2026-08-20_15-59-20", "0821": "2026-08-21_11-00-33"}
MODELO = AQUI / "_nemotron3"
VOZ = Path("/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores/diarizacao/"
           "modelos/wespeaker-voxceleb-resnet34-LM")
MODOS = ["offline", "low_latency", "very_low_latency", "ultra_low_latency"]


def onda(gravacao: str) -> np.ndarray:
    a, _ = sf.read(ACERVO / gravacao / "mix.wav", dtype="float32")
    return a.mean(1) if a.ndim > 1 else a


def motor() -> n3.Nemotron3:
    import onnxruntime as ort
    ort.preload_dlls()  # as DLLs de CUDA/cuDNN que vêm com o torch, se houver
    return n3.Nemotron3(MODELO, gpu=True)


def rotular(segs: list[dict], ativo: np.ndarray) -> list[str]:
    """O slot com mais quadros ativos dentro de cada segmento."""
    rot = []
    for s in segs:
        i0 = int(s["start"] * 100)
        i1 = max(int(s["end"] * 100), i0 + 1)
        soma = ativo[i0:i1].sum(0)
        rot.append(f"spk{int(soma.argmax())}" if soma.max() > 0 else "?")
    return rot


# ------------------------------------------------------------------ a verdade

def _filas(pasta: Path):
    segs = json.loads((pasta / "transcricao.json").read_text(encoding="utf-8"))["segments"]
    nosso = [(str(k), s["text"]) for k, s in enumerate(segs) if (s.get("text") or "").strip()]
    a_txt, a_quem = cg.fila(nosso)
    b_txt, b_quem = cg.fila(cg.ler_gemini(pasta / "gemini.md"))
    for i, j, n in SequenceMatcher(None, a_txt, b_txt, autojunk=False).get_matching_blocks():
        for x in range(n):
            yield int(a_quem[i + x]), b_quem[j + x]


def verdade_por_segmento(pasta: Path) -> list[str | None]:
    """Quem falou cada segmento segundo o Gemini: a maioria das palavras alinhadas."""
    votos: dict[int, Counter] = {}
    for k, quem in _filas(pasta):
        votos.setdefault(k, Counter())[quem] += 1
    n = len(json.loads((pasta / "transcricao.json").read_text(encoding="utf-8"))["segments"])
    return [votos[k].most_common(1)[0][0] if k in votos else None for k in range(n)]


def confusao(pasta: Path, rotulo_de) -> Counter:
    """(rótulo nosso, verdade) por palavra alinhada, com o rótulo do segmento k = rotulo_de(k)."""
    c: Counter = Counter()
    for k, quem in _filas(pasta):
        c[(rotulo_de(k), quem)] += 1
    return c


# ------------------------------------------------------------------ validar

def validar(_args) -> int:
    import torch
    from transformers import AutoModelForAudioFrameClassification, AutoProcessor

    a = onda(COM_GEMINI["0821"])[: 180 * 16000]
    proc = AutoProcessor.from_pretrained("nvidia/Nemotron-3-Diarization")
    m = AutoModelForAudioFrameClassification.from_pretrained(
        "nvidia/Nemotron-3-Diarization").cuda().eval()
    eng = motor()

    inp = proc(a, sampling_rate=16000)
    mel = n3.mel(a, eng.filtros)
    ref_mel = inp.input_features[0].numpy()[: len(mel)]
    print(f"mel: {len(mel)} quadros, diferença máxima {np.abs(mel - ref_mel).max():.2e}")

    def comparar(nome, ref, got):
        k = min(len(ref), len(got))
        igual = ((ref[:k] > 0) == (got[:k] > 0)).all(1).mean()
        print(f"{nome:14s} {k} quadros, decisão igual em {100 * igual:.3f}%")
        return igual == 1.0

    feats = inp.input_features.cuda()
    with torch.inference_mode():
        ref = m(input_features=feats, attention_mask=inp.attention_mask.cuda()).logits
    ok = comparar("offline", ref[0].float().cpu().numpy(), eng.logits(a, "offline"))

    bl, cd, _, _ = n3.MODOS["low_latency"]
    n, cache, saida = feats.shape[1], None, []
    with torch.inference_mode():
        for ini in range(0, n, bl * 8):
            fim = ini + (bl + cd) * 8
            if fim < n:
                o = m(input_features=feats[:, ini:fim], num_lookahead_frames=cd, speaker_cache=cache)
            else:
                o = m(input_features=feats[:, ini:], speaker_cache=cache)
            cache = o.speaker_cache
            saida.append(o.logits[0].float().cpu().numpy())
            if fim >= n:
                break
    ok &= comparar("low_latency", np.concatenate(saida), eng.logits(a, "low_latency"))
    return 0 if ok else 1


# ------------------------------------------------------------------ medir

def _vram() -> int:
    r = subprocess.run(["nvidia-smi", "--query-gpu=memory.used", "--format=csv,noheader,nounits"],
                       capture_output=True, text=True)
    return int(r.stdout.split()[0])


def medir(args) -> int:
    base, pico, vivo = _vram(), [0], [True]

    def vigiar():
        while vivo[0]:
            pico[0] = max(pico[0], _vram())
            time.sleep(0.2)

    threading.Thread(target=vigiar, daemon=True).start()
    eng = motor()
    eng.logits(np.zeros(16000 * 30, np.float32))  # aquece o CUDA antes de cronometrar

    for gravacao in COM_GEMINI_TODAS:
        a = onda(gravacao)
        app = cg.comparar(cg.ler_nosso(ACERVO / gravacao / "transcricao.json"),
                          cg.ler_gemini(ACERVO / gravacao / "gemini.md"))
        print(f"{gravacao[:10]} {'app de hoje':18s} {100 * app['acertos'] / app['alinhadas']:5.1f}% "
              f"falante certo   {len(a) / 16000 / 60:5.1f} min", flush=True)
        for modo in MODOS:
            t = time.time()
            lg = eng.logits(a, modo)
            dt = time.time() - t
            d = json.loads((ACERVO / gravacao / "transcricao.json").read_text(encoding="utf-8"))
            for s, r in zip(d["segments"], rotular(d["segments"], n3.sigmoid(lg) > 0.5)):
                s["speaker"] = r
            dst = args.saida / f"{gravacao[:10]}-{modo}"
            dst.mkdir(parents=True, exist_ok=True)
            (dst / "transcricao.json").write_text(json.dumps(d, ensure_ascii=False), encoding="utf-8")
            (dst / "gemini.md").write_bytes((ACERVO / gravacao / "gemini.md").read_bytes())
            r = cg.comparar(cg.ler_nosso(dst / "transcricao.json"), cg.ler_gemini(dst / "gemini.md"))
            print(f"{gravacao[:10]} {modo:18s} {100 * r['acertos'] / r['alinhadas']:5.1f}% "
                  f"falante certo   {len(a) / 16000 / dt:5.0f}x", flush=True)
    vivo[0] = False
    print(f"VRAM do processo: ~{pico[0] - base} MB (base {base}, pico {pico[0]})")
    return 0


# ------------------------------------------------------------------ vozes

def _falantes(eng, gravacao, a):
    """Por slot do Nemotron (offline): a máscara de fala limpa a 10 ms e o nome verdadeiro dominante."""
    p = n3.sigmoid(eng.logits(a, "offline")) > 0.5
    limpo = p & (p.sum(1, keepdims=True) == 1)
    segs = json.loads((ACERVO / gravacao / "transcricao.json").read_text(encoding="utf-8"))["segments"]
    quem = verdade_por_segmento(ACERVO / gravacao)
    nomes = {}
    for f in range(n3.NSPK):
        votos: Counter = Counter()
        for s, v in zip(segs, quem):
            if v:
                votos[v] += int(limpo[int(s["start"] * 100):int(s["end"] * 100), f].sum())
        if limpo[:, f].sum() > 300 and votos:  # mais de 3 s de fala limpa
            nomes[f] = votos.most_common(1)[0][0]
    return limpo, nomes


def _trechos(a, mascara, teto_s):
    idx = np.nonzero(mascara)[0][: int(teto_s * 100)]
    return np.concatenate([a[i * 160:(i + 1) * 160] for i in idx])


def _cos(x, y):
    return float(np.dot(x, y) / (np.linalg.norm(x) * np.linalg.norm(y)))


def _matriz(dados, vs, titulo):
    print(f"\n  {titulo}: cosseno 20/08 (linhas) x 21/08 (colunas)")
    col = list(dados["0821"]["nomes"].items())
    print(" " * 24 + "".join(f"{n.split()[0][:5] + n.split()[-1][:4]:>11s}" for _, n in col))
    for f0, n0 in dados["0820"]["nomes"].items():
        print(f"  {n0:22s}" + "".join(f"{_cos(vs[('0820', f0)], vs[('0821', f1)]):11.3f}" for f1, _ in col))


def vozes(_args) -> int:
    sys.path.insert(0, str(AQUI.parent / "motores" / "diarizacao" / "pipeline"))
    import conferir_voz_onnx as cvo
    from voz import ExtratorDeVoz

    eng = motor()
    dados = {}
    for k, g in COM_GEMINI.items():
        a = onda(g)
        limpo, nomes = _falantes(eng, g, a)
        dados[k] = {"a": a, "limpo": limpo, "nomes": nomes}
        print(k, nomes)

    print("\n=== A · wespeaker sobre os falantes do Nemotron (30 s de fala limpa)")
    wes = ExtratorDeVoz(VOZ, preferir_gpu=True)
    va = {(k, f): wes(_trechos(d["a"], d["limpo"][:, f], 30))
          for k, d in dados.items() for f in d["nomes"]}
    banco = json.loads((cvo.BANCO / "vozes.json").read_text(encoding="utf-8-sig"))
    pessoas = {}
    for nome, perfil in banco["pessoas"].items():
        pessoas[nome] = [dict(s, _i=i, _v=np.asarray(s["vetor"], np.float32))
                         for i, s in enumerate(perfil["amostras"])]
    vazou = Counter((s.get("origem") or {}).get("gravacao", "")[:10]
                    for lista in pessoas.values() for s in lista)
    print(f"  banco: {len(pessoas)} pessoas; amostras vindas destas reuniões: "
          f"{vazou['2026-08-20']} de 20/08, {vazou['2026-08-21']} de 21/08")
    for (k, f), v in va.items():
        rec, melhor = cvo._reconhecer(v, pessoas)
        print(f"  {k} spk{f} verdade={dados[k]['nomes'][f]:18s} reconhecido={rec!s:18s} melhor={melhor:.3f}")
    _matriz(dados, va, "A wespeaker")

    print("\n=== C · média das camadas internas do Nemotron")
    import torch
    from transformers import AutoModelForAudioFrameClassification, AutoProcessor
    proc = AutoProcessor.from_pretrained("nvidia/Nemotron-3-Diarization")
    m = AutoModelForAudioFrameClassification.from_pretrained(
        "nvidia/Nemotron-3-Diarization").cuda().eval()
    por_bloco = m.config.audio_config.num_hidden_layers + 1
    vc = {L: {} for L in (8, 16, 24, 31)}
    for k, d in dados.items():
        for f in d["nomes"]:
            inp = proc(_trechos(d["a"], d["limpo"][:, f], 30), sampling_rate=16000)
            with torch.inference_mode():
                hs = m(input_features=inp.input_features.cuda(), output_hidden_states=True).hidden_states
            for L in vc:
                blocos = [hs[b * por_bloco + L][0] for b in range(len(hs) // por_bloco)]
                vc[L][(k, f)] = torch.cat(blocos).float().mean(0).cpu().numpy()
    for L, vs in vc.items():
        _matriz(dados, vs, f"C camada {L}")

    print("\n=== B · matrícula: 15 s de cada pessoa de 20/08 antes do áudio de 21/08")
    d0, d1 = dados["0820"], dados["0821"]
    comuns = [(f, n) for f, n in d0["nomes"].items() if n in set(d1["nomes"].values())]
    pausa = np.zeros(16000, np.float32)
    prefixo = np.concatenate([x for f, _ in comuns for x in (_trechos(d0["a"], d0["limpo"][:, f], 15), pausa)])
    slot = {f"spk{i}": n for i, (_, n) in enumerate(comuns)}
    print(f"  slots matriculados: {slot}")
    segs = json.loads((ACERVO / COM_GEMINI["0821"] / "transcricao.json").read_text(encoding="utf-8"))["segments"]
    for modo in ("offline", "low_latency"):
        lg = eng.logits(np.concatenate([prefixo, d1["a"]]), modo)[len(prefixo) // 160:]
        rot = [slot.get(r, r) for r in rotular(segs, n3.sigmoid(lg) > 0.5)]
        c = confusao(ACERVO / COM_GEMINI["0821"], lambda k: rot[k])
        print(f"  {modo}:")
        for n in slot.values():
            dele = sum(v for (r, q), v in c.items() if q == n)
            print(f"    {n:18s} {c[(n, n)]}/{dele} = {100 * c[(n, n)] / max(dele, 1):.1f}% no slot dele")
        print(f"    palavras de outras pessoas num slot matriculado: "
              f"{sum(v for (r, q), v in c.items() if r in slot.values() and r != q)}")
    return 0


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = p.add_subparsers(dest="cmd", required=True)
    sub.add_parser("validar")
    pm = sub.add_parser("medir")
    pm.add_argument("--saida", type=Path, default=Path("/tmp/nemotron3"))
    sub.add_parser("vozes")
    a = p.parse_args()
    return {"validar": validar, "medir": medir, "vozes": vozes}[a.cmd](a)


if __name__ == "__main__":
    sys.exit(main())
