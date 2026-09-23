#!/usr/bin/env python3
"""Exporta os modelos da diarização para ONNX (docs/DIARIZACAO-ONNX.md §3).

**Roda no venv do WSL, não no Python embarcado do app.** Ele precisa de torch
(para ler os pesos) e de onnx/onnxscript (para exportar) — que é exatamente o
que o app deixa de precisar depois deste porte.

Uso::

    uv run --with onnx --with onnxscript --with onnxruntime \\
      python tools/exportar_diarizacao_onnx.py
"""
import sys, warnings
from pathlib import Path
warnings.filterwarnings("ignore")
import numpy as np, torch
# TracerWarning fica de fora do "ignore" acima: com dynamo=False ela é o
# único sinal de que uma forma ficou cravada no grafo enquanto dynamic_axes
# promete dinâmico. Suprimi-la esconderia esse defeito só no próximo export —
# os artefatos de hoje foram conferidos limpos (22/09/2026), então isto é
# para a próxima exportação, não para esta.
warnings.filterwarnings("default", category=torch.jit.TracerWarning)

MODELOS = Path("/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores"
               "/diarizacao/modelos")
RAIZ = MODELOS / "community-1"
#: O modelo de VOZ, que não é o de embedding da diarização e não se troca:
#: trocá-lo mudaria o espaço vetorial e invalidaria toda voz já aprendida
#: (motor.py, MODELO_DE_VOZ).
RAIZ_VOZ = MODELOS / "wespeaker-voxceleb-resnet34-LM"
sys.path.insert(0, str(Path(__file__).resolve().parents[1]
                       / "motores/diarizacao/pipeline"))
from fbank import banco_mel, TAXA as TAXA_FBANK, N_MEL, JANELA, PASSO  # noqa: E402

from pyannote.audio import Model


def exportar_segmentacao():
    """O PyanNet inteiro — este exporta sem truque."""
    m = Model.from_pretrained(RAIZ / "segmentation" / "pytorch_model.bin").eval()
    x = torch.randn(1, 1, 160_000)          # 10 s @ 16 kHz
    alvo = RAIZ / "segmentation" / "model.onnx"
    torch.onnx.export(
        m, (x,), str(alvo),
        input_names=["audio"], output_names=["segmentacao"],
        opset_version=17, dynamo=False,
        dynamic_axes={"audio": {0: "lote"}, "segmentacao": {0: "lote"}},
    )
    print(f"segmentation/model.onnx  {alvo.stat().st_size/1048576:.1f} MB")


def exportar_embedding():
    """Partido em dois, com o pooling ponderado fora.

    **O porquê, e é o achado que motivou este plano.** O pipeline chama
    `resnet(fbank, weights=...)`, e os pesos entram no pooling estatístico.
    Exportar o modelo inteiro sem os pesos produz o vetor do trecho em vez do
    vetor daquele falante — e isso não levanta exceção nenhuma.

    Partir no pooling deixa a matemática ponderada em numpy, onde ela é
    legível e testável, e mantém os dois grafos ONNX de forma fixa.
    """
    m = Model.from_pretrained(RAIZ / "embedding" / "pytorch_model.bin").eval()
    r = m.resnet

    # `two_emb_layer` muda a saída do modelo, e o porte só cobre o caso False.
    # Falhar aqui, alto, é melhor que exportar uma Cabeca errada calada.
    if getattr(r, "two_emb_layer", False):
        raise RuntimeError(
            "este modelo tem two_emb_layer=True: a Cabeca precisa de "
            "seg_bn_1 e seg_2 além do seg_1. Ver resnet.py:399-430."
        )

    class Codificador(torch.nn.Module):
        """fbank → quadros, tudo antes do pooling."""
        def __init__(self, r):
            super().__init__(); self.r = r
        def forward(self, fbank):
            import torch.nn.functional as F
            x = fbank.permute(0, 2, 1).unsqueeze(1)
            x = F.relu(self.r.bn1(self.r.conv1(x)))
            x = self.r.layer1(x); x = self.r.layer2(x)
            x = self.r.layer3(x); x = self.r.layer4(x)
            return x

    class Cabeca(torch.nn.Module):
        """estatísticas → embedding, tudo depois do pooling."""
        def __init__(self, r):
            super().__init__(); self.r = r
        def forward(self, stats):
            return self.r.seg_1(stats)

    cod = Codificador(r).eval()
    fb = torch.randn(1, 498, 80)
    with torch.no_grad():
        quadros = cod(fb)
    print(f"  quadros: {tuple(quadros.shape)}")

    torch.onnx.export(
        cod, (fb,), str(RAIZ / "embedding" / "codificador.onnx"),
        input_names=["fbank"], output_names=["quadros"],
        opset_version=17, dynamo=False,
        dynamic_axes={"fbank": {0: "lote", 1: "quadros"},
                      "quadros": {0: "lote", 3: "t"}},
    )

    b, dim, canal, t = quadros.shape
    stats = torch.randn(1, 2 * dim * canal)
    torch.onnx.export(
        Cabeca(r).eval(), (stats,), str(RAIZ / "embedding" / "cabeca.onnx"),
        input_names=["estatisticas"], output_names=["embedding"],
        opset_version=17, dynamo=False,
        dynamic_axes={"estatisticas": {0: "lote"}, "embedding": {0: "lote"}},
    )

    np.save(RAIZ / "embedding" / "mel.npy", banco_mel())
    for n in ("codificador.onnx", "cabeca.onnx", "mel.npy"):
        print(f"embedding/{n}  "
              f"{(RAIZ/'embedding'/n).stat().st_size/1048576:.2f} MB")


def exportar_voz():
    """O modelo de voz inteiro — aqui **não** há máscara, e é por isso que ele
    sai numa peça só.

    O embedding da diarização precisou ser partido em dois porque o pipeline
    chama ``resnet(fbank, weights=...)`` e os pesos entram no pooling. O
    caminho da voz (``motor.py:vetor_de_voz``) sempre chamou com
    ``weights=None``: ele concatena os trechos limpos que o núcleo escolheu e
    embeda a onda inteira. Sem máscara, o pooling estatístico é interno ao
    grafo e o modelo exporta em uma peça, como o S1
    (``tools/medir_vetor_onnx.py``, 10/09/2026) já tinha validado.

    O que fica de fora é o ``compute_fbank``: ele embrulha o fbank num
    ``torch.vmap``, que nenhum exportador atravessa, e o que sobra sem o vmap
    é o ``aten::fft_rfft``, ausente do opset 17. Daí o fbank em numpy
    (``pipeline/fbank.py``) e o ``mel.npy`` ao lado do ``.onnx``.
    """
    m = Model.from_pretrained(RAIZ_VOZ / "pytorch_model.bin").eval()

    # O ``fbank_centrado`` de pipeline/fbank.py tira a média GLOBAL dos
    # quadros, que é o que o `compute_fbank` faz quando este hparam é None.
    # Com um span, o pyannote troca a média global por uma média móvel
    # (avg_pool1d) e o fbank em numpy passaria a estar errado — em silêncio,
    # porque o vetor continuaria saindo com a forma certa.
    if m.hparams.get("fbank_centering_span") is not None:
        raise RuntimeError(
            "este modelo de voz tem fbank_centering_span != None: o "
            "fbank_centrado() de pipeline/fbank.py centra pela média global "
            "e não serve. Ver o compute_fbank do pyannote."
        )
    if getattr(m.resnet, "two_emb_layer", False):
        raise RuntimeError("este modelo tem two_emb_layer=True; ver exportar_embedding().")

    # As duas checagens acima cobrem fbank_centering_span e two_emb_layer.
    # Sobram outros oito hparams que pipeline/fbank.py não lê do checkpoint —
    # crava como constante. Hoje batem porque o checkpoint não carrega
    # hyper_parameters nenhum (conferido em 22/09/2026: cada um destes vem do
    # default de BaseWeSpeakerResNet.__init__), e por isso ``.get(nome)`` sem
    # sentinela não serviria — ele não distingue "ausente do checkpoint, usa
    # o default" de "presente e valendo None", que para fbank_centering_span
    # acima é justamente o caso que passa. Aqui um hparam ausente é aceito
    # (o default já é o que fbank.py assume); um hparam presente e diferente
    # do esperado é erro.
    _AUSENTE = object()
    _ESPERADOS = {
        "num_mel_bins": N_MEL,
        "frame_length": JANELA / TAXA_FBANK * 1000,
        "frame_shift": PASSO / TAXA_FBANK * 1000,
        "dither": 0.0,
        "snip_edges": True,
        "window_type": "hamming",
        "round_to_power_of_two": True,
        "sample_rate": TAXA_FBANK,
    }
    for nome, esperado in _ESPERADOS.items():
        visto = m.hparams.get(nome, _AUSENTE)
        if visto is _AUSENTE:
            continue
        if visto != esperado:
            raise RuntimeError(
                f"o checkpoint tem {nome}={visto!r}, mas pipeline/fbank.py "
                f"crava {esperado!r} fixo. O fbank em numpy ficaria errado "
                "em silêncio — ver pipeline/fbank.py."
            )

    class SoResNet(torch.nn.Module):
        """fbank → embedding. A mesma forma do wrapper do S1."""
        def __init__(self, r):
            super().__init__(); self.r = r
        def forward(self, f):
            return self.r(f)[1]

    alvo = RAIZ_VOZ / "voz.onnx"
    torch.onnx.export(
        SoResNet(m.resnet).eval(), (torch.randn(1, 498, 80),), str(alvo),
        input_names=["fbank"], output_names=["embedding"],
        opset_version=17, dynamo=False,
        dynamic_axes={"fbank": {0: "lote", 1: "quadros"}, "embedding": {0: "lote"}},
    )
    np.save(RAIZ_VOZ / "mel.npy", banco_mel())
    for n in ("voz.onnx", "mel.npy"):
        print(f"wespeaker-voxceleb-resnet34-LM/{n}  "
              f"{(RAIZ_VOZ / n).stat().st_size / 1048576:.2f} MB")


if __name__ == "__main__":
    exportar_segmentacao()
    exportar_embedding()
    exportar_voz()
