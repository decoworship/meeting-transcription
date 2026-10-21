"""Motor de OCR como sidecar: o texto de uma imagem, linha a linha.

Implementa o contrato de ``docs/SIDECAR.md``. Lê requisições JSON do stdin,
responde no canal do protocolo, e loga no stderr.

Por que este motor existe
-------------------------
Para ler os nomes de um print da reunião (UI-12 do ``docs/BACKLOG.md``): o
Teams e o Meet escrevem o nome de cada participante no quadro dele, e um print
da galeria ou da lista de pessoas é a única prova de quem *entrou* — a agenda
diz quem foi convidado, e a transcrição, quem falou.

Por que um motor, e não o OCR do Windows
----------------------------------------
Decisão do dono, 21/10/2026: o ``Windows.Media.Ocr`` é embutido, mas é WinRT e
só existe no Windows. Este motor é Python, ONNX e um modelo — vai junto se o app
for para macOS ou Linux. O que é de cada sistema é tirar o print; ler é igual.

O modelo
--------
RapidOCR com o PP-OCRv6 *small* (detecção e reconhecimento multilíngue), medido
em 21/10/2026 nos prints reais do dono: 34 de 34 nomes do Teams lidos inteiros,
contra 33 do PP-OCRv5 latino, que ainda partia um nome em dois e grudava o
ícone de sinal no nome de quem fala. ~2 s por print na CPU.

**Os pesos vêm da pasta ``modelos/`` ao lado deste arquivo, e nunca da rede.**
Sem o caminho explícito o RapidOCR baixa os modelos de um servidor na China na
primeira chamada — sem progresso, e sem funcionar offline.

**Sempre na CPU.** A GPU pode estar com a legenda ao vivo, e um print não vale
disputá-la: 2 s na CPU é tempo de clique.

Diferente do ASR, este motor **não fica quente**: o núcleo o sobe para ler um
print e o derruba em seguida. A pessoa lê nomes uma vez por reunião.

Uso (é assim que o cliente C# o inicia)::

    python motor.py
"""

from __future__ import annotations

import json
import os
import sys
import time

# ANTES de qualquer import pesado, como nos outros motores: o RapidOCR e o
# onnxruntime escrevem no stdout, e uma linha dessas no meio do fluxo corrompe o
# protocolo. O descritor 1 vira o canal privado; o stdout passa a ser o stderr.
_protocolo = os.fdopen(os.dup(1), "w", encoding="utf-8", newline="\n")
os.dup2(2, 1)

VERSAO = "1"

PASTA_DOS_MODELOS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "modelos")
DETECCAO = "PP-OCRv6_det_small.onnx"
RECONHECIMENTO = "PP-OCRv6_rec_small.onnx"


def _enviar(**campos) -> None:
    _protocolo.write(json.dumps(campos, ensure_ascii=False) + "\n")
    _protocolo.flush()


def _log(texto: str) -> None:
    print(f"[ocr] {texto}", file=sys.stderr, flush=True)


_ocr = None


def _carregar():
    """O leitor, carregado na primeira imagem e não ao subir.

    Subir sem carregar deixa o handshake rápido e o erro de modelo ausente
    chegar como erro da requisição — legível na tela — em vez de como motor que
    morreu antes de dizer que estava pronto.
    """
    global _ocr
    if _ocr is not None:
        return _ocr

    det = os.path.join(PASTA_DOS_MODELOS, DETECCAO)
    rec = os.path.join(PASTA_DOS_MODELOS, RECONHECIMENTO)
    faltam = [os.path.basename(p) for p in (det, rec) if not os.path.isfile(p)]
    if faltam:
        raise FileNotFoundError(
            f"faltam os modelos de OCR em {PASTA_DOS_MODELOS}: {', '.join(faltam)}"
            " (ver tools/empacotar_modelos_de_ocr.sh)")

    from rapidocr import ModelType, OCRVersion, RapidOCR

    inicio = time.time()
    _ocr = RapidOCR(params={
        "Global.log_level": "error",
        # Texto de tela não vem de cabeça para baixo; o classificador de
        # orientação é um modelo a mais para baixar e carregar a troco de nada.
        "Global.use_cls": False,
        "Det.ocr_version": OCRVersion.PPOCRV6,
        "Det.model_type": ModelType.SMALL,
        "Det.model_path": det,
        "Rec.ocr_version": OCRVersion.PPOCRV6,
        "Rec.model_type": ModelType.SMALL,
        "Rec.model_path": rec,
        "EngineConfig.onnxruntime.use_cuda": False,
        "EngineConfig.onnxruntime.use_dml": False,
    })
    _log(f"modelo carregado em {time.time() - inicio:.1f}s")
    return _ocr


def _ler(caminho: str) -> dict:
    if not os.path.isfile(caminho):
        raise FileNotFoundError(f"a imagem não existe: {caminho}")

    inicio = time.time()
    r = _carregar()(caminho)
    linhas = []
    for texto, confianca, caixa in zip(r.txts or (), r.scores or (), r.boxes if r.boxes is not None else ()):
        texto = str(texto).strip()
        if not texto:
            continue
        xs = [float(p[0]) for p in caixa]
        ys = [float(p[1]) for p in caixa]
        linhas.append({
            "texto": texto,
            "confianca": round(float(confianca), 3),
            # Retângulo que contém o quadrilátero: o núcleo só precisa saber
            # onde a linha está, e não de que ângulo.
            "caixa": [round(min(xs)), round(min(ys)), round(max(xs)), round(max(ys))],
        })

    largura = altura = 0
    if r.img is not None:
        altura, largura = int(r.img.shape[0]), int(r.img.shape[1])
    _log(f"{len(linhas)} linhas em {time.time() - inicio:.1f}s ({os.path.basename(caminho)})")
    return {"linhas": linhas, "largura": largura, "altura": altura}


def main() -> None:
    _enviar(tipo="pronto", motor="ocr", versao=VERSAO)
    _log("pronto")

    for linha in sys.stdin:
        linha = linha.strip()
        if not linha:
            continue

        try:
            req = json.loads(linha)
        except json.JSONDecodeError as e:
            _enviar(tipo="erro", mensagem=f"pedido ilegivel: {e}")
            continue

        id_req = req.get("id", 0)
        op = req.get("op")

        try:
            if op == "ler":
                imagem = req.get("imagem")
                if not imagem:
                    raise ValueError("faltou a imagem")
                _enviar(id=id_req, tipo="resultado", **_ler(imagem))
            else:
                _enviar(id=id_req, tipo="erro", mensagem=f"operacao desconhecida: {op}")
        except Exception as e:                       # noqa: BLE001
            # O motor continua vivo: uma imagem ilegível não pode impedir a
            # próxima.
            _log(f"falhou: {type(e).__name__}: {e}")
            _enviar(id=id_req, tipo="erro", mensagem=f"{type(e).__name__}: {e}")


if __name__ == "__main__":
    main()
