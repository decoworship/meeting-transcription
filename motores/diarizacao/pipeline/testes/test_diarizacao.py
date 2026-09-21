"""A régua `V1` do porte: o pipeline ONNX decide o mesmo que o torch.

Nenhum teste menor pega o que este pega. As Tarefas 4 e 5 já provaram que a
segmentação e o embedding batem número a número; o que sobra é a **cola** —
contagem de falantes, clustering e reconstrução —, e um erro de meio quadro
ali não aparece em nenhuma comparação de tensor: aparece como fala atribuída à
pessoa errada, calada, na ata.

Por isso a comparação é sobre a linha do tempo inteira, e é sobre a gravação
inteira (14,6 min). Encurtar o áudio para o teste ficar rápido é justamente
desligar a régua.
"""
import json
import sys
import wave
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

RAIZ = Path("/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores"
            "/diarizacao/modelos/community-1")
WAV = ("/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings"
       "/2026-08-25_08-59-22/mix.wav")
#: A saída do torch+CUDA medida em 18/09/2026: 404 trechos, 3 falantes.
#: Gere com: python tools/conferir_diarizacao_onnx.py --gravar-gabarito
GABARITO = Path(__file__).parent / "gabarito_14min.json"


def _grade(trechos, dur, passo=0.01):
    """Quem fala em cada centésimo de segundo — a grade que a V1 compara."""
    g = np.full(int(dur / passo), "", dtype=object)
    for t in trechos:
        g[int(t["inicio"] / passo):int(t["fim"] / passo)] = t["falante"]
    return g


def test_V1_mesma_decisao_de_falante_em_99_por_cento_do_tempo():
    # antes de tudo: os outros testes desta pasta importam torch de
    # propósito (é contra ele que eles medem), e a ordem em que o pytest
    # os roda não é garantia nenhuma. O que se afirma aqui é que **este**
    # caminho não importa torch, e isso é a diferença entre antes e depois.
    tinha_torch = "torch" in sys.modules

    from diarizacao import Diarizador

    with wave.open(WAV, "rb") as w:
        taxa = w.getframerate()
        q = w.readframes(w.getnframes())
    onda = np.frombuffer(q, np.int16).astype(np.float32) / 32768.0
    dur = len(onda) / taxa

    esperado = json.loads(GABARITO.read_text(encoding="utf-8"))["trechos"]
    obtido = Diarizador(RAIZ)(onda, taxa)

    # 1. mesmo número de falantes
    assert len({t["falante"] for t in obtido}) == len({t["falante"] for t in esperado})

    # 2. mesma atribuição em >=99% do tempo falado, com os rótulos casados
    #    pelo melhor pareamento (SPEAKER_00 do ONNX pode ser o _01 do torch)
    from scipy.optimize import linear_sum_assignment
    ge, go = _grade(esperado, dur), _grade(obtido, dur)
    re_ = sorted({x for x in ge if x})
    ro = sorted({x for x in go if x})
    custo = np.zeros((len(re_), len(ro)))
    for i, a in enumerate(re_):
        for j, b in enumerate(ro):
            custo[i, j] = -np.sum((ge == a) & (go == b))
    li, lj = linear_sum_assignment(custo)
    mapa = {ro[j]: re_[i] for i, j in zip(li, lj)}
    go_map = np.array([mapa.get(x, x) for x in go], dtype=object)

    falado = ge != ""
    acordo = np.sum((ge == go_map) & falado) / np.sum(falado)
    # o número aparece mesmo quando passa (`pytest -s`): um acordo que cai de
    # 0,999 para 0,991 continua passando, e é assim que se vê a queda antes de
    # ela virar reprovação
    print(f"\nV1: {len(obtido)} trechos × {len(esperado)} do gabarito, "
          f"acordo {acordo:.4f}")
    assert acordo >= 0.99, f"acordo de apenas {acordo:.4f}"

    # 3. e nada de torch no caminho — é o ponto inteiro do porte
    assert ("torch" in sys.modules) == tinha_torch
