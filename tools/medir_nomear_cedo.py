#!/usr/bin/env python3
"""T3.1: nomear o falante cedo resolve o resto da reunião?

**A ideia, do dono do produto.** Numa transcrição que aparece durante a reunião,
nomear quem está falando é muito mais fácil do que depois: a pessoa acabou de
falar, e o convite da agenda já dá a lista de quem está na sala. E o nome dado
ao vivo **ancora o vetor de voz**, que vale para as próximas reuniões.

**O teto já foi medido** (``docs/FASE7-RESULTADOS.md`` §6): quem falou nos
primeiros 10 minutos diz 92,8% das palavras do resto da reunião. Isso é o
máximo que a estratégia poderia alcançar.

**O que falta é saber se o casamento funciona.** Aprender a voz de alguém em 3
minutos de reunião e reconhecê-la 40 minutos depois é outra coisa — o vetor sai
de pouca fala, e a régua do app exige cosseno de 0,70 para dizer que é a mesma
pessoa (``Vozes.LimiarDeReconhecimento``).

**A operação medida é a do produto, não uma idealizada.** Depois do corte, para
cada bloco e cada falante daquele bloco, monta-se um vetor com a fala limpa do
bloco e compara-se com as vozes aprendidas. É exatamente o que o app faria ao
vivo, e é o que a §2.3 disse ser possível em ~88% dos pares.

**O dono do microfone fica de fora**, de propósito: ele já é conhecido de graça
pela faixa separada (``Nucleo/VozDoDono.cs``). O problema é nomear os outros.

Constantes, todas do app — divergir mediria outra coisa::

    SegundosMinimos       3.0   piso de fala limpa para aprender uma voz
    SegundosDoTrecho      4.0   cada amostra é truncada aqui
    FolgaEntreTurnos      0.5   vizinho de outra pessoa mais perto contamina
    LimiarDeReconhecimento 0.70 cosseno abaixo disto é "não sei quem é"

Uso::

    uv run python tools/medir_nomear_cedo.py
    uv run python tools/medir_nomear_cedo.py --cortes 3,5,10 --bloco 180
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import wave
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from simular_blocos import _falante_util  # noqa: E402

ACERVO_PADRAO = "/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings"

SEGUNDOS_MINIMOS = 3.0
SEGUNDOS_DO_TRECHO = 4.0
FOLGA_ENTRE_TURNOS = 0.5
LIMIAR = 0.70

LOCAIS_VOZ = [
    "/mnt/c/Users/andre/MeetingApp/motores/diarizacao/modelos/wespeaker-voxceleb-resnet34-LM",
    str(Path(__file__).resolve().parent.parent
        / "motores/diarizacao/modelos/wespeaker-voxceleb-resnet34-LM"),
]
MODELO_DE_VOZ = "pyannote/wespeaker-voxceleb-resnet34-LM"


def ler_wav(caminho: Path):
    """O mesmo leitor do motor: passar o caminho faria o pyannote 4 exigir o
    torchcodec, que não casa com o torch do índice do PyTorch."""
    import numpy as np
    import torch

    with wave.open(str(caminho), "rb") as w:
        if w.getsampwidth() != 2 or w.getnchannels() != 1:
            raise RuntimeError(f"{caminho.name}: esperava WAV mono de 16 bits")
        taxa = w.getframerate()
        bruto = w.readframes(w.getnframes())
    sinal = np.frombuffer(bruto, dtype=np.int16).astype(np.float32) / 32768.0
    return torch.from_numpy(sinal).unsqueeze(0), taxa


def carregar_voz():
    import torch
    from pyannote.audio import Inference, Model

    local = next((p for p in LOCAIS_VOZ
                  if os.path.isfile(os.path.join(p, "pytorch_model.bin"))), None)
    m = (Model.from_pretrained(local) if local
         else Model.from_pretrained(MODELO_DE_VOZ, token=os.environ.get("HF_TOKEN")))
    if torch.cuda.is_available():
        m = m.to(torch.device("cuda"))
    print(f"modelo de voz: {local or MODELO_DE_VOZ}")
    return Inference(m, window="whole")


def limpos_de(segmentos: list[dict], falante: str) -> list[tuple[float, float]]:
    """Trechos de fala limpa do falante, pela régua do ``AprendizadoDeVozes``."""
    ordenados = sorted(segmentos, key=lambda s: s["start"])
    saida = []
    for i, s in enumerate(ordenados):
        if _falante_util(s) != falante:
            continue
        sujo_antes = (i > 0 and _falante_util(ordenados[i - 1]) != falante
                      and s["start"] - ordenados[i - 1]["end"] < FOLGA_ENTRE_TURNOS)
        sujo_depois = (i + 1 < len(ordenados)
                       and _falante_util(ordenados[i + 1]) != falante
                       and ordenados[i + 1]["start"] - s["end"] < FOLGA_ENTRE_TURNOS)
        if sujo_antes or sujo_depois:
            continue
        saida.append((s["start"], min(s["end"], s["start"] + SEGUNDOS_DO_TRECHO)))
    return sorted(saida, key=lambda t: t[1] - t[0], reverse=True)


def vetor(inferencia, onda, taxa: int, trechos: list[tuple[float, float]]):
    """Concatena os trechos e embeda o conjunto — como faz o motor."""
    import numpy as np
    import torch

    total, pedacos = 0.0, []
    for a, b in trechos:
        i, j = int(a * taxa), int(b * taxa)
        if j <= i:
            continue
        pedacos.append(onda[:, i:j])
        total += (j - i) / taxa
        if total >= SEGUNDOS_MINIMOS * 1.5:
            break
    if total < SEGUNDOS_MINIMOS or not pedacos:
        return None
    v = inferencia({"waveform": torch.cat(pedacos, dim=1), "sample_rate": taxa})
    v = np.asarray(v).astype(float).ravel()
    n = float(np.linalg.norm(v))
    return v / n if n else None


def dono_do_microfone(segmentos: list[dict], mic, taxa: int) -> str | None:
    """Quem tem energia no microfone é o dono — não é estimativa, é o canal."""
    import numpy as np

    por_falante: dict[str, list[float]] = {}
    for s in segmentos:
        f = _falante_util(s)
        if not f:
            continue
        i, j = int(s["start"] * taxa), int(min(s["end"], s["start"] + 4) * taxa)
        if j <= i or j > mic.shape[1]:
            continue
        por_falante.setdefault(f, []).append(
            float(np.sqrt(np.mean(mic[0, i:j].numpy() ** 2))))
    medias = {f: sum(v) / len(v) for f, v in por_falante.items() if v}
    return max(medias, key=medias.get) if medias else None


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--acervo", default=ACERVO_PADRAO)
    ap.add_argument("--cortes", default="3,5,10", help="minutos de aprendizado")
    ap.add_argument("--bloco", type=float, default=180.0)
    ap.add_argument("--minimo-min", type=float, default=12.0,
                    help="ignora gravações curtas demais para a pergunta")
    ap.add_argument("--json")
    args = ap.parse_args()

    acervo = Path(args.acervo)
    cortes = [float(c) * 60 for c in args.cortes.split(",") if c.strip()]
    inferencia = carregar_voz()

    total = {c: {"acerto": 0, "erro": 0, "nao_reconhecido": 0,
                 "sem_voz": 0, "fora": 0} for c in cortes}
    por_gravacao = []
    # Cada decisão guardada com a similaridade que a produziu: é o que permite
    # varrer o limiar depois sem reprocessar áudio nenhum.
    decisoes: list[dict] = []

    for pasta in sorted(p for p in acervo.iterdir() if p.is_dir()):
        alvo = pasta / "transcricao.json"
        sistema = pasta / "system.wav"
        mic = pasta / "mic.wav"
        if not (alvo.is_file() and sistema.is_file() and mic.is_file()):
            continue
        try:
            dados = json.loads(alvo.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue
        segs = [s for s in (dados.get("segments") or [])
                if isinstance(s.get("start"), (int, float))
                and isinstance(s.get("end"), (int, float))]
        dur = dados.get("duration") or 0
        if dur < args.minimo_min * 60 or len(segs) < 20:
            continue

        onda_s, taxa = ler_wav(sistema)
        onda_m, _ = ler_wav(mic)
        dono = dono_do_microfone(segs, onda_m, taxa)
        # Os outros — o dono sai porque a faixa dele já o entrega de graça.
        outros = {f for s in segs if (f := _falante_util(s)) and f != dono}
        if len(outros) < 2:
            continue

        linha = {"gravacao": pasta.name, "minutos": round(dur / 60, 1),
                 "dono": dono, "outros": len(outros), "cortes": {}}

        for corte in cortes:
            antes = [s for s in segs if s["end"] <= corte]
            depois = [s for s in segs if s["start"] >= corte]
            if not depois:
                continue

            aprendidas = {}
            for f in outros:
                v = vetor(inferencia, onda_s, taxa, limpos_de(antes, f))
                if v is not None:
                    aprendidas[f] = v

            c = {"acerto": 0, "erro": 0, "nao_reconhecido": 0,
                 "sem_voz": 0, "fora": 0}
            n = int((dur - corte) // args.bloco) + 1
            for i in range(n):
                a, b = corte + i * args.bloco, corte + (i + 1) * args.bloco
                dentro = [s for s in depois if s["end"] > a and s["start"] < b]
                presentes = {f for s in dentro if (f := _falante_util(s)) and f != dono}
                for f in presentes:
                    palavras = sum(len((s.get("text") or "").split())
                                   for s in dentro if _falante_util(s) == f)
                    if f not in aprendidas:
                        c["fora"] += palavras          # nunca falou antes do corte
                        continue
                    v = vetor(inferencia, onda_s, taxa, limpos_de(dentro, f))
                    if v is None:
                        c["sem_voz"] += palavras       # bloco sem fala limpa
                        continue
                    melhor, sim = None, -1.0
                    for nome, alvo_v in aprendidas.items():
                        s_ = float(v @ alvo_v)
                        if s_ > sim:
                            melhor, sim = nome, s_
                    decisoes.append({"sim": round(sim, 4), "certo": melhor == f,
                                     "palavras": palavras, "corte": int(corte/60)})
                    if sim < LIMIAR:
                        c["nao_reconhecido"] += palavras
                    elif melhor == f:
                        c["acerto"] += palavras
                    else:
                        c["erro"] += palavras

            linha["cortes"][int(corte / 60)] = c | {"vozes_aprendidas": len(aprendidas)}
            for k in c:
                total[corte][k] += c[k]
            print(f"  {pasta.name}  corte {corte/60:.0f}min  "
                  f"{len(aprendidas)}/{len(outros)} vozes  "
                  f"acerto {c['acerto']} erro {c['erro']} "
                  f"não-rec {c['nao_reconhecido']} fora {c['fora']}", flush=True)
        por_gravacao.append(linha)

    print(f"\n{'═'*72}\nT3.1 — nomear cedo, {len(por_gravacao)} gravações\n{'═'*72}")
    print(f"{'corte':>7} {'acerto':>9} {'erro':>8} {'não-rec':>9} {'sem voz':>9}"
          f" {'fora':>8}   {'precisão':>9}")
    for corte in cortes:
        t = total[corte]
        julgados = t["acerto"] + t["erro"]
        base = julgados + t["nao_reconhecido"] + t["sem_voz"]
        print(f"{corte/60:>5.0f}min {t['acerto']:>9} {t['erro']:>8} "
              f"{t['nao_reconhecido']:>9} {t['sem_voz']:>9} {t['fora']:>8}   "
              f"{100*t['acerto']/julgados if julgados else 0:>8.1f}%"
              f"   (cobertura {100*julgados/base if base else 0:.1f}%)")
    print("\n  precisão = acertos entre os que o app se arriscou a nomear.")
    print("  cobertura = quanto ele se arriscou, do que tinha voz aprendida.")
    print("  'fora' = palavras de quem nunca falou antes do corte — o teto da §6.")

    print(f"\n{'═'*72}\nA VARREDURA DO LIMIAR — o que se compra baixando 0,70\n{'═'*72}")
    for corte in cortes:
        d = [x for x in decisoes if x["corte"] == int(corte/60)]
        if not d:
            continue
        base = sum(x["palavras"] for x in d)
        print(f"\n  corte de {corte/60:.0f} min  ({base} palavras julgáveis)")
        print(f"    {'limiar':>7} {'cobertura':>11} {'precisão':>10} {'palavras erradas':>18}")
        for lim in (0.75, 0.70, 0.65, 0.60, 0.55, 0.50, 0.45, 0.40):
            aceitos = [x for x in d if x["sim"] >= lim]
            cert = sum(x["palavras"] for x in aceitos if x["certo"])
            err = sum(x["palavras"] for x in aceitos if not x["certo"])
            tot = cert + err
            marca = "  ← hoje" if abs(lim - LIMIAR) < 1e-9 else ""
            print(f"    {lim:>7.2f} {100*tot/base if base else 0:>10.1f}%"
                  f" {100*cert/tot if tot else 0:>9.1f}% {err:>18}{marca}")

    if args.json:
        Path(args.json).write_text(json.dumps(
            {"total": {str(int(c/60)): total[c] for c in cortes},
             "gravacoes": por_gravacao, "decisoes": decisoes}, ensure_ascii=False, indent=2),
            encoding="utf-8")
        print(f"\nbruto em {args.json}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
