"""Inspeciona a interface num navegador real: capturas + auditoria de tokens.

Existe porque três defeitos do redesign (logo transbordando por cima da página,
campos sem borda, body fora do tema) eram invisíveis lendo o CSS e óbvios na
tela. Ler o CSS diz o que deveria acontecer; isto diz o que acontece.

Uso:
    python tools/ui_check.py                 # sobe o app, audita, captura
    python tools/ui_check.py --url http://localhost:7860
    python tools/ui_check.py --out /tmp/ui

Requer o Chromium do Playwright e as libs do sistema:
    uv pip install playwright && python -m playwright install chromium
    sudo apt-get install -y libnss3 libnspr4
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent

# Contraste mínimo da WCAG AA para texto normal. O design system afirma
# conformidade nos dois temas; aqui é onde isso deixa de ser afirmação.
CONTRASTE_MIN = 4.5


def _luminancia(rgb: tuple[float, float, float]) -> float:
    def canal(c: float) -> float:
        c /= 255.0
        return c / 12.92 if c <= 0.03928 else ((c + 0.055) / 1.055) ** 2.4
    r, g, b = (canal(x) for x in rgb)
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def _parse_rgb(s: str) -> tuple[float, float, float] | None:
    if not s or not s.startswith("rgb"):
        return None
    nums = [float(x) for x in s[s.index("(") + 1:s.index(")")].replace("/", " ")
            .replace(",", " ").split()[:3]]
    return tuple(nums) if len(nums) == 3 else None


def contraste(fg: str, bg: str) -> float | None:
    a, b = _parse_rgb(fg), _parse_rgb(bg)
    if not a or not b:
        return None
    la, lb = _luminancia(a), _luminancia(b)
    claro, escuro = max(la, lb), min(la, lb)
    return (claro + 0.05) / (escuro + 0.05)


AUDITORIA_JS = """() => {
    const cs = el => el ? getComputedStyle(el) : null;
    const body = cs(document.body);
    const cont = cs(document.querySelector('.gradio-container'));
    const svg = document.querySelector('.mt-logo svg');
    // O primeiro `.wrap` da pagina e o rastreador de status oculto do Gradio,
    // que nao tem borda por natureza. O campo de verdade e o wrap que contem
    // um input dentro de um container.
    const wrap = [...document.querySelectorAll('.container > .wrap')]
        .find(w => w.querySelector('input'));

    // Elementos que vazam para fora da largura da página denunciam
    // transbordamento -- foi assim que o logo apareceu.
    const largura = document.documentElement.clientWidth;
    const vazando = [...document.querySelectorAll('.gradio-container *')]
        .map(e => ({e, r: e.getBoundingClientRect()}))
        .filter(({r}) => r.width > 0 && (r.right > largura + 2 || r.left < -2))
        .slice(0, 8)
        .map(({e, r}) => `${e.tagName.toLowerCase()}.${(e.className||'').toString().split(' ')[0]} ${r.width.toFixed(0)}px`);

    return {
        body_bg: body.backgroundColor,
        body_fg: body.color,
        body_font: body.fontFamily.split(',')[0],
        cont_bg: cont?.backgroundColor,
        logo_px: svg ? Math.round(svg.getBoundingClientRect().width) : null,
        campo_bg: cs(wrap)?.backgroundColor,
        campo_borda: cs(wrap)?.borderTopColor,
        campo_borda_px: cs(wrap)?.borderTopWidth,
        vazando,
        altura_pagina: Math.round(document.body.scrollHeight),
    };
}"""


def inspecionar(url: str, out: Path) -> int:
    from playwright.sync_api import sync_playwright

    out.mkdir(parents=True, exist_ok=True)
    problemas = []

    with sync_playwright() as p:
        b = p.chromium.launch()
        for tema in ("claro", "escuro"):
            alvo = url + ("?__theme=dark" if tema == "escuro" else "")
            pg = b.new_page(viewport={"width": 1280, "height": 900})
            erros: list[str] = []
            pg.on("pageerror", lambda e: erros.append(str(e)[:120]))

            # `networkidle` nunca dispara com Gradio: ele mantém conexões
            # abertas. Esperar pelo container é o sinal confiável.
            pg.goto(alvo, wait_until="domcontentloaded", timeout=60000)
            pg.wait_for_selector(".gradio-container", timeout=30000)
            time.sleep(4)

            a = pg.evaluate(AUDITORIA_JS)
            pg.screenshot(path=str(out / f"{tema}_topo.png"))
            pg.screenshot(path=str(out / f"{tema}_completo.png"), full_page=True)

            c_texto = contraste(a["body_fg"], a["body_bg"])
            print(f"\n=== {tema} ===")
            print(f"  fundo {a['body_bg']}  texto {a['body_fg']}  fonte {a['body_font']}")
            print(f"  campo {a['campo_bg']} borda {a['campo_borda']} ({a['campo_borda_px']})")
            print(f"  logo {a['logo_px']}px | pagina {a['altura_pagina']}px")
            print(f"  contraste texto/fundo: {c_texto:.2f}" if c_texto else "  contraste: n/a")

            if c_texto and c_texto < CONTRASTE_MIN:
                problemas.append(f"[{tema}] contraste {c_texto:.2f} < {CONTRASTE_MIN}")
            if a["campo_borda_px"] in ("0px", "medium"):
                problemas.append(f"[{tema}] campo sem borda")
            if a["logo_px"] and a["logo_px"] > 80:
                problemas.append(f"[{tema}] logo com {a['logo_px']}px (transbordando)")
            if a["vazando"]:
                problemas.append(f"[{tema}] elementos fora da pagina: {a['vazando']}")
            if erros:
                problemas.append(f"[{tema}] erros de JS: {erros[:3]}")
            pg.close()
        b.close()

    print("\n=== resultado ===")
    if problemas:
        for x in problemas:
            print("  PROBLEMA:", x)
    else:
        print("  nenhum problema detectado")
    print(f"  capturas em {out}")
    return 1 if problemas else 0


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--url", default=None,
                    help="app ja rodando; se omitido, sobe um temporario")
    ap.add_argument("--out", default="/tmp/ui_check")
    args = ap.parse_args()

    if args.url:
        return inspecionar(args.url, Path(args.out))

    proc = subprocess.Popen(
        [str(REPO / ".venv/bin/python"), "web.py"], cwd=REPO,
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
        env={**__import__("os").environ, "RECORDINGS_DIR": str(REPO / "data/recordings")},
    )
    try:
        time.sleep(28)
        return inspecionar("http://localhost:7860", Path(args.out))
    finally:
        proc.terminate()
        proc.wait(timeout=15)


if __name__ == "__main__":
    sys.exit(main())
