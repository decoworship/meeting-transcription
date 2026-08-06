"""Aplica o AA Design System sobre o Gradio.

O design system é CSS puro com custom properties; o Gradio também expõe o
próprio tema por variáveis CSS. A ponte é mapear as variáveis do Gradio para os
tokens semânticos do design system, e não reescrever componente por componente.

Três problemas específicos que este módulo resolve:

1. **Fontes.** Vão embutidas como data URI. O Gradio serve estáticos por rotas
   próprias que mudam entre versões; embutir elimina a classe inteira de
   problemas de caminho, e as fontes variáveis somam menos de 500 KB.

2. **Modo escuro.** O Gradio marca ``.dark`` no elemento raiz; o design system
   usa ``[data-tema="escuro"]``. Sem ponte, alternar o tema do Gradio deixaria
   o texto do design system claro sobre fundo claro. A ponte replica o seletor.

3. **Especificidade.** O CSS do Gradio é carregado depois do nosso, então as
   sobrescritas precisam alcançar as variáveis dele, não os elementos.
"""

from __future__ import annotations

import base64
import functools
import logging
import re
from pathlib import Path

logger = logging.getLogger(__name__)

DS_DIR = Path(__file__).parent / "assets" / "ds"

# Mapa: variável do Gradio -> token semântico do design system. Só o que muda a
# leitura da interface; o resto do Gradio herda por cascata.
GRADIO_TO_DS = {
    "body-background-fill": "--cor-fundo",
    "body-text-color": "--cor-texto",
    "body-text-color-subdued": "--cor-texto-suave",
    "background-fill-primary": "--cor-superficie",
    "background-fill-secondary": "--cor-superficie-2",
    "block-background-fill": "--cor-superficie",
    "block-border-color": "--cor-borda",
    "block-label-background-fill": "--cor-superficie-2",
    "block-label-text-color": "--cor-texto-suave",
    "block-title-text-color": "--cor-texto-forte",
    "border-color-primary": "--cor-borda",
    "border-color-accent": "--cor-acao",
    "panel-background-fill": "--cor-superficie",
    "input-background-fill": "--cor-superficie",
    "input-border-color": "--cor-borda-controle",
    "input-border-color-focus": "--cor-acao",
    "input-placeholder-color": "--cor-texto-suave",
    "button-primary-background-fill": "--cor-acao",
    "button-primary-background-fill-hover": "--cor-acao-hover",
    "button-primary-text-color": "--cor-texto-invertido",
    "button-secondary-background-fill": "--cor-superficie-2",
    "button-secondary-text-color": "--cor-texto",
    "button-secondary-border-color": "--cor-borda-forte",
    "color-accent": "--cor-acao",
    "color-accent-soft": "--cor-acao-suave",
    "link-text-color": "--cor-acao",
    "link-text-color-hover": "--cor-acao-hover",
    "table-border-color": "--cor-borda",
    "table-even-background-fill": "--cor-superficie",
    "table-odd-background-fill": "--cor-fundo",
    "table-row-focus": "--cor-linha-hover",
    "accordion-text-color": "--cor-texto",
    "checkbox-background-color-selected": "--cor-acao",
    "checkbox-border-color-focus": "--cor-acao",
    "slider-color": "--cor-acao",
    "error-background-fill": "--cor-erro-bg",
    "error-border-color": "--cor-erro",
    "stat-background-fill": "--cor-acao-suave",
}

# Escalares que não são cor.
GRADIO_SCALARS = {
    "radius-sm": "var(--raio-pequeno)",
    "radius-md": "var(--raio-medio)",
    "radius-lg": "var(--raio-grande)",
    "text-sm": "var(--texto-pequeno)",
    "text-md": "var(--texto-corpo)",
    "text-lg": "var(--texto-titulo-m)",
    "spacing-sm": "var(--espaco-2)",
    "spacing-md": "var(--espaco-3)",
    "spacing-lg": "var(--espaco-4)",
    "shadow-drop": "var(--sombra-pequena)",
    "block-shadow": "var(--sombra-pequena)",
}

_FONT_MIME = "font/ttf"


def _inline_fonts(css: str) -> str:
    """Troca ``url('fonts/...')`` por data URI, com o arquivo embutido.

    Duas economias deliberadas, porque isto viaja em cada carregamento:

    * cada ``@font-face`` do design system lista o mesmo arquivo duas vezes (um
      ``format()`` para cada sintaxe). Embutir os dois dobraria o peso à toa, então
      só o primeiro vira data URI e o segundo é descartado.
    * as itálicas ficam de fora. O navegador sintetiza o oblíquo, e a interface
      quase não usa itálico -- não vale 400 KB.
    """
    faces = re.split(r"(?=@font-face)", css)
    saida = []
    for face in faces:
        if "@font-face" in face and "font-style: italic" in face:
            continue
        vistos: set[str] = set()

        def repl(m: re.Match) -> str:
            rel = m.group(1)
            if rel in vistos:
                return ""                      # duplicata do mesmo arquivo
            vistos.add(rel)
            path = DS_DIR / rel
            if not path.is_file():
                logger.warning(f"fonte do design system ausente: {rel}")
                return m.group(0)
            b64 = base64.b64encode(path.read_bytes()).decode("ascii")
            return f"url('data:{_FONT_MIME};base64,{b64}')"

        face = re.sub(
            r"url\(['\"]?(fonts/[^'\")]+)['\"]?\)\s*format\(['\"][^'\"]+['\"]\),?\s*",
            lambda m: (r if (r := repl(m)) == "" else r + " format('truetype-variations'),"),
            face,
        )
        # Limpa vírgula pendente deixada pela remoção da duplicata.
        face = re.sub(r",\s*;", ";", face)
        saida.append(face)
    return "".join(saida)


def _dark_bridge(tokens_css: str) -> str:
    """Reaplica o bloco escuro do design system sob os seletores do Gradio.

    O Gradio marca ``.dark`` na raiz. Sem isto, alternar o tema dele trocaria os
    fundos mas manteria os tokens do design system no claro -- texto claro sobre
    fundo claro em metade da tela.
    """
    m = re.search(r'\[data-tema="escuro"\]\s*\{(.*?)\n\}', tokens_css, re.S)
    if not m:
        logger.warning("bloco [data-tema=escuro] não encontrado nos tokens")
        return ""
    corpo = m.group(1)
    return (
        "/* ponte: tema escuro do Gradio -> tokens escuros do design system */\n"
        ".dark, .dark :root, html.dark {\n" + corpo + "\n}\n"
    )


def build_theme():
    """Tema base do Gradio, ajustado para não brigar com o design system.

    ``Base`` em vez de ``Soft``: os temas prontos do Gradio trazem paleta
    própria e disputam as mesmas variáveis que vamos sobrescrever. Base é quase
    sem estilo, então quem decide é o CSS dos tokens.

    As famílias vão aqui além do CSS porque o Gradio as injeta inline em alguns
    componentes, onde a nossa folha não alcança.
    """
    import gradio as gr

    # Nomes soltos, nunca GoogleFont: as famílias já vêm embutidas por
    # @font-face em build_css(). GoogleFont buscaria da CDN, o que quebraria o
    # uso offline e contradiz a auto-hospedagem do design system.
    return gr.themes.Base(
        font=["Hanken Grotesk", "system-ui", "sans-serif"],
        font_mono=["ui-monospace", "SF Mono", "Menlo", "monospace"],
    )


@functools.lru_cache(maxsize=1)
def build_css() -> str:
    """CSS completo para passar ao ``launch(css=...)``.

    Em cache: as fontes viram base64 e reprocessar a cada chamada seria
    desperdício puro.
    """
    partes = []

    tokens = (DS_DIR / "tokens" / "tokens.css").read_text(encoding="utf-8")
    tipo = (DS_DIR / "colors_and_type.css").read_text(encoding="utf-8")
    componentes = (DS_DIR / "componentes.css").read_text(encoding="utf-8")

    partes.append(_inline_fonts(tipo))
    partes.append(tokens)
    partes.append(componentes)
    partes.append(_dark_bridge(tokens))

    # Mapeamento das variáveis do Gradio. Vai por último e em :root para
    # alcançar tudo que o Gradio desenha.
    linhas = [f"  --{g}: var({ds});" for g, ds in GRADIO_TO_DS.items()]
    linhas += [f"  --{g}: {v};" for g, v in GRADIO_SCALARS.items()]
    linhas.append("  --font: var(--fonte-ui);")
    linhas.append("  --font-mono: var(--fonte-mono);")
    partes.append(
        "/* Gradio -> AA Design System */\n:root, .gradio-container {\n"
        + "\n".join(linhas) + "\n}\n"
    )

    partes.append(_APP_CSS)
    return "\n\n".join(partes)


# CSS próprio do app, agora escrito em cima dos tokens em vez de valores soltos.
_APP_CSS = """
/* ---------- estrutura ---------- */
.gradio-container {
    max-width: 1100px !important;
    margin-left: auto !important;
    margin-right: auto !important;
    font-family: var(--fonte-ui) !important;
    background: var(--cor-fundo) !important;
}

/* Títulos no serif de display -- é o que dá a cara do design system. */
.gradio-container h1,
.gradio-container h2,
.gradio-container h3 {
    font-family: var(--fonte-display) !important;
    color: var(--cor-texto-forte) !important;
    letter-spacing: -0.01em;
}

/* ---------- painel de tempos ---------- */
.timing-box textarea {
    font-family: var(--fonte-mono) !important;
    font-size: var(--texto-rotulo) !important;
    background: var(--cor-superficie-2) !important;
    color: var(--cor-texto-suave) !important;
    border-radius: var(--raio-pequeno) !important;
}

/* ---------- transcrição ---------- */
.mt-segment {
    border-radius: var(--raio-pequeno);
    transition: background var(--transicao-rapida);
}
.mt-segment:hover {
    background: var(--cor-linha-hover) !important;
}
.mt-segment.mt-active {
    background: var(--cor-acao-suave) !important;
    box-shadow: inset 3px 0 0 var(--cor-acao);
}

/* ---------- campo auxiliar oculto ----------
   Fica no DOM e continua alvo de evento; só não é visível. */
.mt-hidden-input {
    position: absolute !important;
    width: 1px !important;
    height: 1px !important;
    overflow: hidden !important;
    clip: rect(0 0 0 0) !important;
    white-space: nowrap !important;
    pointer-events: none !important;
    opacity: 0 !important;
}
"""
