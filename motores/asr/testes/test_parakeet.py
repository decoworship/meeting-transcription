"""O caminho do transcribe.cpp no motor de ASR: os cortes e os trechos.

O modelo em si é medido por ``tools/medir_asr_final.py``; aqui ficam as duas
regras que o motor acrescenta por conta própria, e que um modelo bom não salva
se estiverem erradas: onde o áudio é cortado em blocos e como as palavras viram
trechos.

    uv run --with pytest python -m pytest motores/asr/testes -v
"""
import sys
from pathlib import Path
from types import SimpleNamespace

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import motor  # noqa: E402

TAXA = motor.TAXA


def palavra(texto, t0, t1):
    return SimpleNamespace(text=texto, t0_ms=int(t0 * 1000), t1_ms=int(t1 * 1000))


def test_audio_curto_e_um_bloco_so():
    onda = np.ones(TAXA * 60, np.float32)
    assert motor.cortes(onda) == [(0, len(onda))]


def test_o_corte_cai_no_silencio_perto_da_fronteira():
    """Fala o tempo todo, menos 300 ms aos 297 s: o corte tem de cair ali."""
    onda = np.full(TAXA * 700, 0.5, np.float32)
    quieto = int(297.0 * TAXA)
    onda[quieto: quieto + int(0.3 * TAXA)] = 0.0
    blocos = motor.cortes(onda)
    primeiro_fim = blocos[0][1] / TAXA
    assert 297.0 <= primeiro_fim <= 297.3
    # os blocos cobrem o áudio inteiro, sem buraco e sem sobreposição
    assert blocos[0][0] == 0 and blocos[-1][1] == len(onda)
    assert all(a[1] == b[0] for a, b in zip(blocos, blocos[1:]))


def test_reuniao_de_duas_horas_vira_blocos_limitados():
    onda = np.random.default_rng(0).normal(0, 0.1, TAXA * 7200).astype(np.float32)
    blocos = motor.cortes(onda)
    # o tamanho de cada bloco é o que limita a memória; a contagem varia com
    # onde o silêncio cai, e por isso não é cravada
    duracoes = [(f - i) / TAXA for i, f in blocos]
    assert max(duracoes) <= motor.BLOCO_S + 2 * motor.JANELA_DE_CORTE_S
    assert min(duracoes[:-1]) >= motor.BLOCO_S - 2 * motor.JANELA_DE_CORTE_S


def test_pausa_fecha_o_trecho_e_as_palavras_levam_o_espaco():
    ws = [palavra("Bom", 0.0, 0.3), palavra("dia", 0.3, 0.6),
          palavra("tudo", 1.5, 1.8), palavra("bem?", 1.8, 2.1)]
    trechos = motor.trechos_por_palavra(ws, deslocamento=100.0)
    assert [t["texto"] for t in trechos] == [" Bom dia", " tudo bem?"]
    assert trechos[1]["inicio"] == 101.5 and trechos[1]["fim"] == 102.1
    # é assim que o C# remonta um trecho dividido: string.Concat das palavras
    for t in trechos:
        assert "".join(p["texto"] for p in t["palavras"]) == t["texto"]


def test_fim_de_frase_fecha_com_pausa_curta():
    ws = [palavra("Certo.", 0.0, 0.4), palavra("Então", 0.65, 0.9)]
    assert len(motor.trechos_por_palavra(ws, 0.0)) == 2
    sem_ponto = [palavra("Certo", 0.0, 0.4), palavra("então", 0.65, 0.9)]
    assert len(motor.trechos_por_palavra(sem_ponto, 0.0)) == 1


def test_fala_corrida_nao_passa_do_teto():
    ws = [palavra(f"p{i}", i * 0.3, i * 0.3 + 0.25) for i in range(200)]
    trechos = motor.trechos_por_palavra(ws, 0.0)
    assert len(trechos) > 1
    assert all(t["fim"] - t["inicio"] <= motor.TETO_S + 0.3 for t in trechos)


def test_palavra_vazia_some():
    """A 0.2.3 devolvia uma palavra vazia no começo; ela não pode virar trecho."""
    ws = [palavra("", 1.2, 1.28), palavra("A", 1.2, 1.28), palavra("gente", 1.3, 1.6)]
    trechos = motor.trechos_por_palavra(ws, 0.0)
    assert [t["texto"] for t in trechos] == [" A gente"]


def test_id_desconhecido_continua_no_faster_whisper():
    assert motor.Modelo("large-v3")._gguf is None
    assert motor.Modelo("parakeet-ultra")._gguf == "parakeet-ultra-F16.gguf"
