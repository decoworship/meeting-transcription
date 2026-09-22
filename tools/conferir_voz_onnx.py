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
   lado, que é onde se vê que a diferença é do áudio e não do runtime;
4. **custo** — quanto tempo cada caminho leva, em CPU;
5. **reconstrução ponta a ponta** — a régua que fecha o que a 1–4 não alcançam:
   elas rodam CPU↔CPU (torch em CPU contra onnxruntime em CPU), enquanto os
   160 vetores do banco saíram de **CUDA torch** e os próximos sairão de
   **CUDA onnxruntime** — nenhum dos dois endpoints reais está em 1–4. Para as
   amostras em que ``duracao_s == origem.t1 - origem.t0`` houve um único
   trecho usado, então o vetor guardado veio exatamente de
   ``<gravacao>/<faixa>.wav[t0:t1]`` — sem a truncagem em 4 s do trecho de
   auditoria (ver acima). Reextrair esse recorte da gravação de origem com
   ``ExtratorDeVoz(..., preferir_gpu=True)`` e comparar contra o vetor já
   guardado testa a mudança de runtime **e** a mudança de GPU ao mesmo tempo,
   sem o confundidor de tamanho que virou o item 3 em "contexto". O provedor
   efetivo (``sessao.abrir`` devolve o que a sessão realmente usou, não o que
   foi pedido) é conferido e impresso — um retorno silencioso para CPU
   mediria de novo exatamente o que o item 3 já mede, e pareceria a régua
   certa sem ser.

O torch aqui é referência, como no S1; o caminho medido é numpy + onnxruntime.

Uso::

    uv run python tools/conferir_voz_onnx.py
    uv run python tools/conferir_voz_onnx.py --json saida.json

O item 5 pede CUDAExecutionProvider de verdade. Neste WSL de desenvolvimento
o ``onnxruntime`` do ``uv.lock`` é a build CPU-only (é o que ``faster-whisper``
pede) — sem o provedor CUDA compilado dentro, nenhum PATH ou LD_LIBRARY_PATH
o traz de volta. Para medir o item 5 aqui, é preciso o pacote GPU e as libs
CUDA/cuDNN que o torch já trouxe como dependências::

    LD_LIBRARY_PATH="/usr/local/lib/ollama/cuda_v13:$(uv run python -c \\
      'import nvidia.cudnn as m,os;print(os.path.dirname(m.__file__))')/lib:\\
      $(uv run python -c 'import nvidia.cufft as m,os;print(os.path.dirname(m.__file__))')/lib:\\
      $(uv run python -c 'import nvidia.curand as m,os;print(os.path.dirname(m.__file__))')/lib" \\
      uv run --with onnxruntime-gpu python tools/conferir_voz_onnx.py

Na instalação real (Windows, ``onnxruntime-gpu`` 1.23.2 fixado à mão, ver
docs/DIARIZACAO-ONNX.md) isto não é necessário — ``sessao.abrir`` já resolve
sozinho.
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
#: onde o núcleo grava as reuniões — a fonte para a reconstrução do item 5.
GRAVACOES = Path("/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings")

#: Vozes.LimiarDeReconhecimento
LIMIAR = 0.70
#: Vozes.ModeloDeVozPadrao — amostras antigas não carimbaram o modelo
MODELO_PADRAO = "pyannote/wespeaker-voxceleb-resnet34-LM"
TAXA = 16000
#: Folga para "duracao_s == t1 - t0" (item 5). T0, T1 e DuracaoS cada um sai
#: de um Math.Round(_, 2) independente no C# (AprendizadoDeVozes.cs), então o
#: erro de arredondamento acumulado no pior caso é ~0,015 s; testado de 0,005
#: a 0,1 no banco real, a contagem de amostras que qualificam não muda (44) —
#: não é um limiar que está cortando no meio de alguma coisa.
TOLERANCIA_DURACAO = 0.02


def _ler_wav(caminho: Path) -> np.ndarray:
    with wave.open(str(caminho), "rb") as w:
        if w.getsampwidth() != 2 or w.getnchannels() != 1:
            raise RuntimeError(f"{caminho}: esperado WAV mono de 16 bits")
        bruto = w.readframes(w.getnframes())
    return np.frombuffer(bruto, np.int16).astype(np.float32) / 32768.0


def _ler_wav_trecho(caminho: Path, t0: float, t1: float) -> np.ndarray:
    """O recorte [t0:t1] de um WAV, sem carregar o arquivo inteiro — algumas
    gravações passam de 3000 s."""
    with wave.open(str(caminho), "rb") as w:
        if w.getsampwidth() != 2 or w.getnchannels() != 1:
            raise RuntimeError(f"{caminho}: esperado WAV mono de 16 bits")
        taxa = w.getframerate()
        ini = max(0, int(round(t0 * taxa)))
        fim = min(w.getnframes(), int(round(t1 * taxa)))
        if fim <= ini:
            return np.zeros(0, np.float32)
        w.setpos(ini)
        bruto = w.readframes(fim - ini)
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

    print(f"\n5. RECONSTRUÇÃO PONTA A PONTA — a gravação de origem, com CUDA, "
          f"contra o vetor guardado\n")
    print("   1–4 rodam CPU↔CPU (torch e onnx em CPU); os vetores do banco "
          "saíram de CUDA torch\n   e os próximos sairão de CUDA onnxruntime "
          "— nenhum dos dois endpoints reais está em 1–4.\n")
    candidatas = []
    for am in todas:
        dur = am.get("duracao_s")
        o = am.get("origem") or {}
        t0, t1 = o.get("t0"), o.get("t1")
        if dur is None or t0 is None or t1 is None:
            continue
        if abs(dur - (t1 - t0)) < TOLERANCIA_DURACAO:
            candidatas.append(am)
    print(f"   {len(candidatas)}/{len(todas)} amostras com duracao_s == t1 - t0 "
          f"(±{TOLERANCIA_DURACAO}s — um único trecho usado, sem a truncagem "
          f"em 4 s do item 3)")

    extrator_gpu = None
    linhas5, sem_gravacao = [], 0
    for am in candidatas:
        o = am["origem"]
        wav = GRAVACOES / o["gravacao"] / f"{o['faixa']}.wav"
        if not wav.is_file():
            sem_gravacao += 1
            continue
        if extrator_gpu is None:
            extrator_gpu = ExtratorDeVoz(MODELO, preferir_gpu=True)
            print(f"   onnx (item 5) pedindo GPU, efetivo: "
                  f"{extrator_gpu.provedor}")
            if extrator_gpu.provedor != "CUDAExecutionProvider":
                print("   ATENÇÃO — caiu para CPU: isto mede de novo o que o "
                      "item 3 já mede (torch-CPU vs onnx-CPU), não fecha o "
                      "Finding 1.")
        onda = _ler_wav_trecho(wav, o["t0"], o["t1"])
        vg = np.asarray(extrator_gpu(onda), dtype=np.float64).ravel()
        linhas5.append((am, vg))

    print(f"\n   {len(linhas5)}/{len(candidatas)} com a gravação de origem em "
          f"disco ({sem_gravacao} sem)")

    resumo5 = None
    if not linhas5:
        print("   nenhuma amostra reconstruível — item 5 fica sem número")
    else:
        cos5 = [_cos(am["_v"], vg) for am, vg in linhas5]
        print(f"\n   cosseno mínimo   {min(cos5):.9f}")
        print(f"   cosseno mediano  {float(np.median(cos5)):.9f}")

        mudou5 = []
        for am, vg in linhas5:
            fora = am["_i"]
            n_guardado, _ = _reconhecer(am["_v"], pessoas, fora)
            n_novo, _ = _reconhecer(vg, pessoas, fora)
            if n_guardado != n_novo:
                mudou5.append((am, n_guardado, n_novo))
        print(f"   decisões do Reconhecer (limiar {LIMIAR:.2f}) que mudariam: "
              f"{len(mudou5)}/{len(linhas5)}")
        for am, ng, nn in mudou5[:10]:
            print(f"     MUDOU {am['_pessoa']}: guardado={ng} novo={nn}")

        if min(cos5) < 0.999:
            print("\n   ATENÇÃO — cosseno mínimo longe de 1. Isto é sobre o "
                  "banco precisar de re-extração, decisão do dono do "
                  "produto — nada aqui foi ajustado por causa disto.")

        resumo5 = {
            "candidatas": len(candidatas), "reconstruidas": len(linhas5),
            "sem_gravacao": sem_gravacao,
            "cos_min": min(cos5), "cos_mediano": float(np.median(cos5)),
            "decisoes_mudariam": len(mudou5),
            "provedor": extrator_gpu.provedor if extrator_gpu else None,
        }

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
            "reconstrucao_e2e": resumo5,
        }, indent=2, ensure_ascii=False), encoding="utf-8")
        print(f"\nescrito em {a.json}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
