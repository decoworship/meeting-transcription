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

RAIZ = Path("/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores"
            "/diarizacao/modelos/community-1")
sys.path.insert(0, str(Path(__file__).resolve().parents[1]
                       / "motores/diarizacao/pipeline"))
from fbank import banco_mel  # noqa: E402

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


if __name__ == "__main__":
    exportar_segmentacao()
    exportar_embedding()
