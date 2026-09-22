#!/usr/bin/env python3
"""A régua do porte do vetor de voz, sobre o banco de vozes real (22/09/2026).

**Por que esta régua e não a do S1.** O ``tools/medir_vetor_onnx.py`` comparou
os dois caminhos sobre áudio montado na hora a partir do acervo de gravações, e
comparou o ONNX contra uma chamada direta ao modelo. Aqui o áudio é o do banco
de vozes de verdade — os trechos que o app guardou junto de cada amostra — e a
referência é o caminho de produção que o porte substitui,
``Inference(modelo, window="whole")``.

**O que se descobriu montando isto, e muda o que dá para medir.** O trecho
guardado em cada amostra **não é o áudio que produziu o vetor guardado**: o
``AprendizadoDeVozes.RecortarTrecho`` grava só o *primeiro* trecho usado,
truncado em ``SegundosDoTrecho`` (4 s), enquanto o vetor sai da concatenação de
todos os trechos usados (``duracao_s``, tipicamente 6 a 26 s). Os 4 s são para
auditar de ouvido, não para reproduzir a extração. Então **o cosseno contra o
vetor guardado não mede o porte** — ele mede quanto 4 s se parecem com os 26 s
da mesma pessoa, e com o torch daria o mesmo número. Ele fica no relatório como
contexto, e a régua é outra:

1. **equivalência** — o mesmo trecho pelos dois caminhos, torch e ONNX. É o
   porte, e é a régua;
2. **decisão** — o ``Reconhecer`` do app (``app-net/Nucleo/Vozes.cs``:
   centroide por ``dispositivo|faixa``, o melhor grupo, limiar 0,70) rodado
   sobre o banco de vetores *torch* com o vetor de cada caminho. Se nenhuma
   decisão muda, um app recém-atualizado reconhece exatamente quem reconhecia;
3. **contexto** — o cosseno contra o vetor guardado, pelos dois caminhos lado a
   lado, que é onde se vê que a diferença é do áudio e não do runtime.

O torch aqui é referência, como no S1; o caminho medido é numpy + onnxruntime.

Uso::

    uv run python tools/conferir_voz_onnx.py
    uv run python tools/conferir_voz_onnx.py --json saida.json
"""

from __future__ import annotations

import argparse
import json
import sys
import time
import warnings
import wave
from pathlib import Path

import numpy as np

warnings.filterwarnings("ignore")

sys.path.insert(0, str(Path(__file__).resolve().parents[1]
                       / "motores/diarizacao/pipeline"))
from voz import ExtratorDeVoz  # noqa: E402

BANCO = Path("/mnt/c/Users/andre/.meeting-transcription/vozes")
MODELO = Path("/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores"
              "/diarizacao/modelos/wespeaker-voxceleb-resnet34-LM")

#: Vozes.LimiarDeReconhecimento
LIMIAR = 0.70
#: Vozes.ModeloDeVozPadrao — amostras antigas não carimbaram o modelo
MODELO_PADRAO = "pyannote/wespeaker-voxceleb-resnet34-LM"
TAXA = 16000


def _ler_wav(caminho: Path) -> np.ndarray:
    with wave.open(str(caminho), "rb") as w:
        if w.getsampwidth() != 2 or w.getnchannels() != 1:
            raise RuntimeError(f"{caminho}: esperado WAV mono de 16 bits")
        bruto = w.readframes(w.getnframes())
    return np.frombuffer(bruto, np.int16).astype(np.float32) / 32768.0


def _normalizado(v: np.ndarray) -> np.ndarray:
    n = np.linalg.norm(v)
    return v if n == 0 else v / n


def _centroide(vetores: list[np.ndarray]) -> np.ndarray:
    """Vozes.Centroide: média dos normalizados, normalizada de novo."""
    return _normalizado(np.mean([_normalizado(v) for v in vetores], axis=0))


def _cos(a: np.ndarray, b: np.ndarray) -> float:
    return float(np.dot(a, b) / (np.linalg.norm(a) * np.linalg.norm(b)))


def _grupos(amostras: list[dict], sem: int | None = None) -> list[np.ndarray]:
    """Os sub-perfis de uma pessoa, como o ``Vozes.Semelhanca`` os monta."""
    por: dict[str, list[np.ndarray]] = {}
    for a in amostras:
        if a["_i"] == sem or a.get("quarentena") in (True, "True"):
            continue
        if (a.get("modelo") or MODELO_PADRAO) != MODELO_PADRAO:
            continue
        o = a.get("origem") or {}
        por.setdefault(f"{o.get('dispositivo')}|{o.get('faixa')}", []).append(a["_v"])
    return [_centroide(v) for v in por.values()]


def _reconhecer(vetor: np.ndarray, pessoas: dict[str, list[dict]],
                sem: int | None = None) -> tuple[str | None, float]:
    """Vozes.Reconhecer: o melhor grupo de cada pessoa, acima do limiar.

    Devolve também a melhor semelhança vista, **acima ou abaixo do limiar** —
    o app descarta esse número, mas sem ele um "ninguém" não se distingue de
    outro na hora de ler o relatório.
    """
    nome_acima, acima, melhor = None, -1.0, -1.0
    for nome, amostras in pessoas.items():
        s = max((_cos(vetor, c) for c in _grupos(amostras, sem)), default=-1.0)
        melhor = max(melhor, s)
        if s >= LIMIAR and s > acima:
            nome_acima, acima = nome, s
    return nome_acima, melhor


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--json", type=Path)
    a = p.parse_args()

    dados = json.loads((BANCO / "vozes.json").read_text(encoding="utf-8-sig"))
    pessoas: dict[str, list[dict]] = {}
    i = 0
    for nome, perfil in dados["pessoas"].items():
        lista = []
        for am in perfil.get("amostras") or []:
            am = dict(am)
            am["_i"] = i
            am["_v"] = np.asarray(am["vetor"], dtype=np.float64)
            am["_pessoa"] = nome
            lista.append(am)
            i += 1
        pessoas[nome] = lista

    todas = [am for lista in pessoas.values() for am in lista]
    em_disco = len(list((BANCO / "trechos").glob("*.wav")))
    print(f"banco: {len(pessoas)} pessoas, {len(todas)} amostras, "
          f"{em_disco} trechos em disco\n")

    import torch
    from pyannote.audio import Model, Inference

    modelo = Model.from_pretrained(MODELO / "pytorch_model.bin")
    referencia = Inference(modelo, window="whole")
    extrator = ExtratorDeVoz(MODELO, preferir_gpu=False)
    print(f"onnx em {extrator.provedor}, torch em cpu\n")

    linhas, sem_trecho, segundos = [], 0, 0.0
    gasto = {"torch": 0.0, "onnx": 0.0}
    for am in todas:
        rel = (am.get("trecho") or "").replace("\\", "/")
        wav = BANCO / rel
        if not rel or not wav.is_file():
            sem_trecho += 1
            continue
        onda = _ler_wav(wav)
        t0 = time.perf_counter()
        vt = np.asarray(referencia({"waveform": torch.from_numpy(onda).unsqueeze(0),
                                    "sample_rate": TAXA}),
                        dtype=np.float64).ravel()
        t1 = time.perf_counter()
        vo = np.asarray(extrator(onda), dtype=np.float64).ravel()
        t2 = time.perf_counter()
        gasto["torch"] += t1 - t0
        gasto["onnx"] += t2 - t1
        segundos += len(onda) / TAXA
        linhas.append((am, vt, vo))

    if not linhas:
        print("nenhuma amostra com trecho em disco", file=sys.stderr)
        return 2

    prop = [_cos(vt, vo) for _, vt, vo in linhas]
    print("=" * 72)
    print(f"1. EQUIVALÊNCIA — o mesmo trecho, torch vs ONNX ({len(linhas)} amostras)\n")
    print(f"   cosseno mínimo   {min(prop):.9f}")
    print(f"   cosseno mediano  {float(np.median(prop)):.9f}")
    print(f"   maior diferença absoluta  "
          f"{max(float(np.abs(vt - vo).max()) for _, vt, vo in linhas):.2e}")

    print(f"\n2. DECISÃO — o Reconhecer do app contra o banco torch, limiar {LIMIAR:.2f}\n")
    mudou, resumo = [], {}
    for rotulo, sem_si in (("com a própria amostra no banco", False),
                           ("deixando a própria de fora", True)):
        iguais = 0
        conta = {"torch": {}, "onnx": {}}
        for am, vt, vo in linhas:
            fora = am["_i"] if sem_si else None
            nt, st = _reconhecer(vt, pessoas, fora)
            no, so = _reconhecer(vo, pessoas, fora)
            iguais += nt == no
            if nt != no:
                mudou.append((rotulo, am, nt, st, no, so))
            for k, n in (("torch", nt), ("onnx", no)):
                if n is None:
                    conta[k]["ninguém"] = conta[k].get("ninguém", 0) + 1
                elif n == am["_pessoa"]:
                    conta[k]["a pessoa certa"] = conta[k].get("a pessoa certa", 0) + 1
                else:
                    conta[k]["outra pessoa"] = conta[k].get("outra pessoa", 0) + 1
        resumo[rotulo] = {"decisoes_iguais": iguais, "de": len(linhas),
                          "torch": conta["torch"], "onnx": conta["onnx"]}
        print(f"   {rotulo}:")
        print(f"     decisões iguais  {iguais}/{len(linhas)}")
        for k in ("torch", "onnx"):
            print(f"     {k:<6} {conta[k]}")
    for rotulo, am, nt, st, no, so in mudou[:10]:
        print(f"     MUDOU ({rotulo}) {am['_pessoa']}: torch={nt} ({st:.4f}) "
              f"onnx={no} ({so:.4f})")

    ct = [_cos(vt, am["_v"]) for am, vt, _ in linhas]
    co = [_cos(vo, am["_v"]) for am, _, vo in linhas]
    print("\n3. CONTEXTO — o trecho de 4 s contra o vetor guardado (que veio de mais áudio)\n")
    print(f"   {'':<7} {'mínimo':>12} {'mediano':>12}")
    print(f"   {'torch':<7} {min(ct):>12.6f} {float(np.median(ct)):>12.6f}")
    print(f"   {'onnx':<7} {min(co):>12.6f} {float(np.median(co)):>12.6f}")
    print("   Os dois caem juntos: a distância é do áudio (4 s de auditoria "
          "vs. a\n   concatenação que gerou o vetor), não do runtime.")

    print(f"\n4. CUSTO — {segundos:.0f}s de áudio, tudo em CPU\n")
    for k in ("torch", "onnx"):
        print(f"   {k:<6} {gasto[k]:7.1f}s   {segundos / gasto[k]:6.1f}x o tempo real")
    if sem_trecho:
        print(f"\n   {sem_trecho} amostras sem trecho em disco, fora da conta")

    ok = min(prop) > 0.9999 and not mudou
    print("\n" + "=" * 72)
    print("VEREDITO:", "equivalente — nenhuma decisão muda, nada a re-extrair" if ok
          else "ATENÇÃO — ver os números acima, é decisão do dono do produto")

    if a.json:
        a.json.write_text(json.dumps({
            "amostras": len(linhas), "sem_trecho": sem_trecho,
            "trechos_em_disco": em_disco,
            "cos_min": min(prop), "cos_mediano": float(np.median(prop)),
            "limiar": LIMIAR, "decisao": resumo,
            "contexto_guardado": {
                "torch_min": min(ct), "torch_mediano": float(np.median(ct)),
                "onnx_min": min(co), "onnx_mediano": float(np.median(co))},
            "audio_s": segundos, "seg": gasto,
            "provedor": extrator.provedor, "ok": ok,
        }, indent=2, ensure_ascii=False), encoding="utf-8")
        print(f"\nescrito em {a.json}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
