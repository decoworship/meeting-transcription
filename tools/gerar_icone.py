#!/usr/bin/env python3
"""Gera assets/logo.ico a partir de assets/logo.svg.

Por que o ícone tem fundo próprio
---------------------------------
A primeira versão era só a silhueta do logo tingida de #2D2D30, com a ideia de
que um cinza escuro serviria nos dois temas do Explorer. Não serve: no tema
escuro do Windows o fundo é #202020 e a figura praticamente desaparece --
verificado compondo o ícone sobre os dois fundos.

Ícone de aplicativo não pode depender do fundo em que é desenhado. Por isso
aqui ele é uma pastilha: quadrado arredondado escuro com o logo vazado em
quase-branco. A pastilha carrega o próprio contraste e fica legível em tema
claro, escuro e sobre qualquer papel de parede na área de trabalho.

Cada tamanho é composto separadamente, e não reduzido do 256: o traço do logo é
fino, e reduzir a arte inteira de 256 para 16 borra tudo. Compondo por tamanho
dá para dar mais folga proporcional aos pequenos.

Uso:
    uv run python tools/gerar_icone.py
"""

from pathlib import Path

from PIL import Image, ImageDraw

RAIZ = Path(__file__).resolve().parent.parent
ORIGEM = RAIZ / "assets" / "logo-256.png"   # render do logo.svg, silhueta preta
DESTINO = RAIZ / "assets" / "logo.ico"

FUNDO = (45, 45, 48, 255)        # #2D2D30, o cinza escuro da identidade do app
FIGURA = (245, 245, 245)         # quase-branco: contraste alto sem estourar

# 16 e 24 aparecem na barra de tarefas e em listas; 256 no modo "ícones extra
# grandes" do Explorer. Sem os intermediários o Windows improvisa a redução.
TAMANHOS = [16, 24, 32, 48, 64, 128, 256]


def pastilha(tamanho: int) -> Image.Image:
    """Um tamanho do ícone: fundo arredondado + logo vazado."""
    # Escala 4x e reduz no fim: é o que dá borda lisa no cantinho arredondado,
    # que o ImageDraw sozinho não entrega.
    escala = 4
    g = tamanho * escala

    img = Image.new("RGBA", (g, g), (0, 0, 0, 0))
    ImageDraw.Draw(img).rounded_rectangle(
        [0, 0, g - 1, g - 1], radius=int(g * 0.22), fill=FUNDO)

    # Folga maior nos tamanhos pequenos: com pouca margem o traço fino do logo
    # encosta na borda da pastilha e vira um borrão.
    folga = 0.20 if tamanho <= 32 else 0.16
    lado = int(g * (1 - 2 * folga))

    logo = Image.open(ORIGEM).convert("RGBA").resize((lado, lado), Image.LANCZOS)
    # A origem é silhueta preta com alfa; troca-se a cor preservando o alfa,
    # que é o que mantém a forma.
    branco = Image.new("RGBA", logo.size, FIGURA + (0,))
    branco.putalpha(logo.getchannel("A"))

    img.alpha_composite(branco, (int(g * folga), int(g * folga)))
    return img.resize((tamanho, tamanho), Image.LANCZOS)


def escrever_ico(imagens: list[Image.Image], destino: Path) -> None:
    """Monta o .ico à mão, uma arte por tamanho.

    O ``Image.save(..., format="ICO", sizes=...)`` do Pillow regrava todos os
    tamanhos reduzindo a imagem passada, o que jogaria fora a composição feita
    para cada um. O formato é simples o bastante para escrever direto: cabeçalho,
    uma entrada por imagem e os PNGs em sequência.
    """
    import io
    import struct

    corpos = []
    for img in imagens:
        buf = io.BytesIO()
        img.save(buf, format="PNG")     # PNG dentro do ICO: aceito desde o Vista
        corpos.append(buf.getvalue())

    cabecalho = struct.pack("<HHH", 0, 1, len(imagens))     # reservado, tipo=ícone, n
    deslocamento = len(cabecalho) + 16 * len(imagens)

    entradas = b""
    for img, corpo in zip(imagens, corpos):
        lado = img.width
        entradas += struct.pack(
            "<BBBBHHII",
            0 if lado >= 256 else lado,   # 0 significa 256 no formato
            0 if lado >= 256 else lado,
            0,                            # cores da paleta: 0 = sem paleta
            0,                            # reservado
            1,                            # planos
            32,                           # bits por pixel
            len(corpo),
            deslocamento,
        )
        deslocamento += len(corpo)

    destino.write_bytes(cabecalho + entradas + b"".join(corpos))


# ─────────────────────────────────────────── ícones da bandeja

# A cor do ícone é o único aviso que existe durante a gravação: ela distingue
# gravando, mudo por sua escolha e canal sem áudio. O amarelo existe por causa
# da gravação de 06/08, que saiu 95% muda sem ninguém notar.
CORES_DA_BANDEJA = {
    "cinza": (120, 120, 120),      # parado
    "vermelho": (220, 50, 50),     # gravando
    "laranja": (240, 150, 30),     # mudo pela bandeja (decisão sua)
    "amarelo": (230, 200, 30),     # canal sem áudio (ninguém pediu)
}

# Os tamanhos que o Windows pede na área de notificação conforme o DPI.
TAMANHOS_BANDEJA = [16, 20, 24, 32]


def silhueta(tamanho: int, cor: tuple[int, int, int]) -> Image.Image:
    """O logo tingido, sem pastilha: aqui a cor é a informação.

    Diferente do ícone do .exe, que precisa de fundo próprio, o da bandeja é
    desenhado sobre a barra de tarefas e deve ser só a forma -- o contraste vem
    da cor viva do estado, e uma pastilha só roubaria área útil dos 16 px.
    """
    escala = 4
    g = tamanho * escala

    logo = Image.open(ORIGEM).convert("RGBA").resize((g, g), Image.LANCZOS)
    tingido = Image.new("RGBA", (g, g), cor + (0,))
    tingido.putalpha(logo.getchannel("A"))
    return tingido.resize((tamanho, tamanho), Image.LANCZOS)


def main() -> None:
    if not ORIGEM.is_file():
        raise SystemExit(f"falta {ORIGEM} (render do logo.svg em 256 px)")

    escrever_ico([pastilha(t) for t in TAMANHOS], DESTINO)
    print(f"{DESTINO.relative_to(RAIZ)}: {len(TAMANHOS)} tamanhos "
          f"({', '.join(str(t) for t in TAMANHOS)})")

    for nome, cor in CORES_DA_BANDEJA.items():
        destino = RAIZ / "assets" / f"bandeja-{nome}.ico"
        escrever_ico([silhueta(t, cor) for t in TAMANHOS_BANDEJA], destino)
        print(f"{destino.relative_to(RAIZ)}: {len(TAMANHOS_BANDEJA)} tamanhos")


if __name__ == "__main__":
    main()
