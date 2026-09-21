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
import subprocess
import sys
import wave
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
#: `acordo`/`_casar_falantes` vivem em tools/conferir_diarizacao_onnx.py — é a
#: única implementação (F1 da revisão de 21/09/2026): ele gerou o gabarito
#: desta régua, então os dois têm de medir concordância do mesmo jeito.
sys.path.insert(0, str(Path(__file__).resolve().parents[4] / "tools"))

RAIZ = Path("/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores"
            "/diarizacao/modelos/community-1")
WAV = ("/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings"
       "/2026-08-25_08-59-22/mix.wav")
#: A saída do torch+CUDA medida em 18/09/2026: 404 trechos, 3 falantes.
#: Gere com: python tools/conferir_diarizacao_onnx.py --gravar-gabarito
GABARITO = Path(__file__).parent / "gabarito_14min.json"


def test_V1_mesma_decisao_de_falante_em_99_por_cento_do_tempo():
    from diarizacao import Diarizador

    # As mesmas conferências que o `ler_wav` do
    # `tools/conferir_diarizacao_onnx.py` faz — ele é quem gerou o gabarito, e
    # os dois têm de ler o arquivo do mesmo jeito. Hoje o `mix.wav` é mono 16
    # bits; se um dia ele vier estéreo, o gerador faria o downmix e esta régua
    # compararia dois áudios diferentes sem dizer nada.
    with wave.open(WAV, "rb") as w:
        assert w.getsampwidth() == 2, f"{WAV} não é PCM 16 bits"
        taxa, canais = w.getframerate(), w.getnchannels()
        q = w.readframes(w.getnframes())
    onda = np.frombuffer(q, np.int16).astype(np.float32) / 32768.0
    if canais > 1:                                   # mono="downmix"
        onda = onda.reshape(-1, canais).mean(axis=1)
    dur = len(onda) / taxa

    esperado = json.loads(GABARITO.read_text(encoding="utf-8"))["trechos"]
    obtido = Diarizador(RAIZ)(onda, taxa)

    # 1. mesmo número de falantes
    assert len({t["falante"] for t in obtido}) == len({t["falante"] for t in esperado})

    # 2. mesma atribuição em >=99% do tempo falado, com os rótulos casados
    #    pelo melhor pareamento (SPEAKER_00 do ONNX pode ser o _01 do torch).
    #    `acordo` é a UNIÃO, e não só o que o gabarito chama de fala: comparar
    #    apenas sobre o falado do gabarito mede o que o ONNX **deixa de
    #    ouvir** e é cego ao que ele ouve a mais — no gabarito, 672,7 s dos
    #    878,4 s são fala (76,6%), e um pipeline que carimbasse um falante nos
    #    ~205 s de silêncio ainda marcaria 1,0000. Ver a implementação e o
    #    porquê em tools/conferir_diarizacao_onnx.py.
    from conferir_diarizacao_onnx import acordo, acordo_so_falado_do_gabarito
    valor = acordo(esperado, obtido, dur)
    # Só sobre o que o gabarito chama de fala, para o relatório: é o número
    # antigo, e a diferença entre os dois é o alarme falso.
    valor_antigo = acordo_so_falado_do_gabarito(esperado, obtido, dur)

    # os números aparecem mesmo quando passa (`pytest -s`): um acordo que cai
    # de 0,999 para 0,991 continua passando, e é assim que se vê a queda antes
    # de ela virar reprovação
    print(f"\nV1: {len(obtido)} trechos × {len(esperado)} do gabarito, "
          f"acordo (união) {valor:.4f}, "
          f"acordo (só o falado do gabarito) {valor_antigo:.4f}")
    assert valor >= 0.99, f"acordo de apenas {valor:.4f}"

    # 3. e a mesma fragmentação. Duas saídas podem concordar sobre quem fala em
    #    cada quadro e ainda assim partir a linha do tempo de formas muito
    #    diferentes — e é a contagem de trechos que revela isso, porque nenhum
    #    quadro individual revela.
    #
    #    A folga é de 10%: o gabarito tem 404 trechos, e uma diferença dentro
    #    de ~40 é o que a granularidade do `Binarize` pode produzir entre duas
    #    implementações que decidem o mesmo. Acima disso a reconstrução está
    #    agrupando de outro jeito, e isso é notícia mesmo com o acordo alto.
    folga = 0.1 * len(esperado)
    assert abs(len(obtido) - len(esperado)) <= folga, (
        f"{len(obtido)} trechos contra {len(esperado)} do gabarito — "
        f"a fragmentação mudou mais que os {folga:.0f} de folga"
    )


def test_o_Diarizador_nao_importa_torch():
    """Em interpretador limpo, porque só ali a resposta é determinística.

    Ler `sys.modules` dentro da suíte não serve: os outros testes desta pasta
    importam torch de propósito (é contra ele que eles medem), e basta um deles
    rodar antes para a conferência passar a valer nada. A ordem alfabética
    salva hoje, e ordem alfabética não é garantia — `pytest -k`, um arquivo
    passado à mão ou uma rodada do repositório inteiro já a desfazem.

    Um processo novo não tem essa dúvida: se `diarizacao.py` importar torch,
    direta ou indiretamente, ele aparece.
    """
    codigo = (
        "import sys; "
        f"sys.path.insert(0, {str(Path(__file__).resolve().parents[1])!r}); "
        "import diarizacao; "
        "print('torch' in sys.modules)"
    )
    saida = subprocess.run([sys.executable, "-c", codigo],
                           capture_output=True, text=True, check=True)
    assert saida.stdout.strip() == "False", (
        f"importar `diarizacao` puxou torch: {saida.stdout!r} {saida.stderr!r}"
    )
