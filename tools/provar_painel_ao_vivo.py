#!/usr/bin/env python3
"""Prova que o painel ao vivo desenha — num Chromium de verdade.

**Por que esta ferramenta existe.** O painel nasceu em 04/09/2026 e **nunca
funcionou** até 10/09: dois defeitos em sequência, um escondendo o outro, e
nenhum dos dois dava erro visível. O primeiro — a sentinela da lista fora do DOM
— eu achei lendo. **O segundo só apareceu rodando**, e se eu tivesse publicado o
conserto raciocinado, a tela teria continuado vazia.

Daí a regra que esta ferramenta aplica: **tela se prova no navegador, não no
raciocínio.**

Ela monta o `painelAoVivo()` sozinho, com a ponte falsa do molde do
``tools/medir_layout.py``, e confere as três coisas que a tela promete:

1. **o bloco de 3 minutos** desenha, com separador e falas nos dois lados;
2. **a legenda ao vivo** firma em pedaços, **agrupa por dono** — texto novo do
   mesmo lado cresce no mesmo balão, dono diferente abre outro — e mantém **um**
   balão volátil com o que o motor ainda pode reescrever;
3. **a grade de duas colunas** do Gravador não deixa nada além da prévia
   escorregar para a direita (o ``grid-row: 1 / -1`` **não** atravessa linhas
   implícitas, e sem a regra da coluna 1 a agenda ia parar debaixo da legenda).
4. **a caixa de perguntar ao modelo** faz a volta inteira — espera dizendo por
   quê, resposta na tela, aviso quando o modelo não viu a reunião inteira — e a
   resposta **não entra na lista**: a lista é o que foi dito, e a resposta é o
   que um modelo deduziu do que foi dito.

Uso::

    uv run --with playwright python tools/provar_painel_ao_vivo.py
"""

import functools
import http.server
import socketserver
import sys
import threading
from pathlib import Path

WEB = Path(__file__).resolve().parent.parent / "app-net" / "App" / "web"
PORTA = 8761

PAINEL = """<!doctype html><meta charset="utf-8">
<script>
window.chrome = { webview: {
  _ouvintes: [],
  addEventListener(_, f) { this._ouvintes.push(f); },
  postMessage(txt) {
    const p = JSON.parse(txt);
    if (p.op === "aovivo") this._empurrar({ id: p.id, aovivo_ate: [] });
    if (p.op === "perguntar-ao-vivo") {
      window.__perguntado = p.pergunta;
      this._empurrar({ id: p.id, tipo: "progresso",
                       texto: "carregando o modelo e lendo a reunião\u2026" });
      setTimeout(() => this._empurrar(
        { id: p.id, resposta: "Falaram do instalador e ficou de medir o Parakeet.",
          cortado: true }), 400);
    }
  },
  _empurrar(ev) { for (const f of this._ouvintes) f({ data: JSON.stringify(ev) }); },
}};
</script>
<link rel="stylesheet" href="/app.css">
<div id="alvo"></div>
<script type="module">
import { painelAoVivo } from "/aovivo.js";
document.getElementById("alvo").appendChild(painelAoVivo().raiz);
window.__pronto = true;
</script>
"""

COLUNAS = """<!doctype html><meta charset="utf-8">
<link rel="stylesheet" href="/app.css">
<div class="painel" id="p" data-aovivo="true">
  <div class="bloco" id="cartao">cartao</div>
  <div class="bloco" id="reuniao">reuniao</div>
  <div class="bloco" id="agenda">a agenda</div>
  <div class="bloco" id="notas">notas</div>
  <div class="bloco" id="disp">dispositivos</div>
  <div class="bloco" id="pasta">pasta</div>
  <section class="bloco aovivo" id="previa">a prévia</section>
</div>"""

BLOCO = {
    "n": 0, "inicio_s": 0, "fim_s": 180, "estado": "provisorio",
    "trechos": [
        {"start": 12.5, "end": 15.0, "text": " bom dia pessoal", "speaker": "You"},
        {"start": 15.2, "end": 18.9, "text": " bom dia, tudo certo", "speaker": "b0_S1"},
    ],
}

#: A legenda firma aos poucos. Os dois primeiros são do dono e têm de cair no
#: mesmo balão; o terceiro troca de lado e abre outro; o quarto só mexe no
#: volátil.
LEGENDA = [
    {"novo": "bom dia pessoal", "tentativo": "tudo", "dono": True},
    {"novo": ", tudo bem?", "tentativo": "", "dono": True},
    {"novo": "tudo ótimo", "tentativo": "e vo", "dono": False},
    {"novo": "", "tentativo": "e você, como", "dono": False},
]


def servir():
    h = functools.partial(http.server.SimpleHTTPRequestHandler, directory=str(WEB))
    h.log_message = lambda *a: None
    socketserver.TCPServer.allow_reuse_address = True
    s = socketserver.TCPServer(("127.0.0.1", PORTA), h)
    threading.Thread(target=s.serve_forever, daemon=True).start()
    return s


def main() -> int:
    from playwright.sync_api import sync_playwright

    servidor = servir()
    erros: list[str] = []
    try:
        with sync_playwright() as pw:
            nav = pw.chromium.launch()
            pg = nav.new_page(viewport={"width": 1400, "height": 900})
            pg.on("pageerror", lambda e: erros.append(str(e)))
            # Só erro de JavaScript conta. Os 404 são o design system, que mora
            # fora da pasta web e é embutido à parte pelo App/Conteudo.cs.
            pg.on("console", lambda m: erros.append(m.text)
                  if m.type == "error" and "404" not in m.text else None)

            # ── 1 e 2: o painel ────────────────────────────────────────────
            pg.route("**/painel", lambda r: r.fulfill(content_type="text/html", body=PAINEL))
            pg.goto(f"http://127.0.0.1:{PORTA}/painel")
            pg.wait_for_function("window.__pronto === true", timeout=5000)
            vazio = pg.inner_text(".aovivo__corpo").strip()

            pg.evaluate("(b) => window.chrome.webview._empurrar("
                        "{id:0, tipo:'aovivo', aovivo:b})", BLOCO)
            pg.wait_for_timeout(300)
            for g in LEGENDA:
                pg.evaluate("(g) => window.chrome.webview._empurrar("
                            "{id:0, tipo:'aovivo', legenda:g})", g)
                pg.wait_for_timeout(120)

            falas = pg.evaluate("""() => [...document.querySelectorAll('.fala')].map(e => ({
                dono: e.dataset.dono,
                vol: e.classList.contains('fala--volatil'),
                x: Math.round(e.getBoundingClientRect().left),
                txt: e.textContent.trim().replace(/\\s+/g, ' ') }))""")
            separadores = pg.locator(".aovivo__bloco").count()

            # ── 4: perguntar ao modelo ─────────────────────────────────────
            pg.fill(".aovivo__perguntar .aa-entrada", "o que ficou decidido?")
            pg.click(".aovivo__perguntar button")

            # A espera é dita de frente: dez segundos de silêncio parecem
            # defeito, e o defeito é o que este projeto evita parecer quando
            # está funcionando.
            pg.wait_for_timeout(150)
            esperando = {
                "estado": pg.get_attribute(".aovivo__resposta", "data-estado"),
                "texto": pg.inner_text(".aovivo__resposta").strip(),
                "botao": pg.is_disabled(".aovivo__perguntar button"),
            }

            pg.wait_for_selector(".aovivo__resposta[data-estado='pronta']", timeout=5000)
            pergunta = pg.evaluate("""() => ({
                echo: document.querySelector('.aovivo__resposta-pergunta').textContent,
                resposta: document.querySelector('.aovivo__resposta-texto').textContent,
                aviso: !!document.querySelector('.aovivo__resposta .aa-alerta'),
                campo: document.querySelector('.aovivo__perguntar .aa-entrada').value,
                naLista: !!document.querySelector('.aovivo__corpo .aovivo__resposta-texto'),
                chegouAoNucleo: window.__perguntado,
            })""")

            # ── 3: as duas colunas ─────────────────────────────────────────
            pg.route("**/colunas", lambda r: r.fulfill(content_type="text/html", body=COLUNAS))
            pg.goto(f"http://127.0.0.1:{PORTA}/colunas")
            caixas = pg.evaluate("""() => [...document.querySelectorAll('#p > *')]
                .map(e => ({ id: e.id, x: Math.round(e.getBoundingClientRect().left) }))""")
            nav.close()
    finally:
        servidor.shutdown()

    print(f"\n1. estado vazio: {vazio!r}")
    print(f"2. separadores de bloco: {separadores}")
    print("3. falas na tela:")
    for f in falas:
        print(f"   dono={f['dono']:<5} x={f['x']:>5} "
              f"{'VOLÁTIL' if f['vol'] else 'firme  '} {f['txt']!r}")

    firmes = [f for f in falas if not f["vol"]]
    volateis = [f for f in falas if f["vol"]]
    donos = [f for f in firmes if f["dono"] == "true"]
    outros = [f for f in firmes if f["dono"] == "false"]

    # O bloco deu 2 falas; a legenda deve ter agrupado em mais 2, não em 3.
    agrupou = any(f["txt"] == "bom dia pessoal, tudo bem?" for f in firmes)
    um_volatil = len(volateis) == 1
    lados = bool(donos and outros
                 and min(f["x"] for f in donos) > max(f["x"] for f in outros))

    print(f"\n   agrupou o texto do mesmo dono num balão só: {agrupou}")
    print(f"   manteve exatamente um balão volátil:          {um_volatil}")
    print(f"   dono à direita, os outros à esquerda:         {lados}")

    print("\n4. perguntar ao modelo:")
    print(f"   enquanto espera:  estado={esperando['estado']!r} botão travado="
          f"{esperando['botao']}")
    print(f"   {esperando['texto']!r}")
    print(f"   a pergunta chegou ao núcleo:   {pergunta['chegouAoNucleo']!r}")
    print(f"   a resposta na tela:            {pergunta['resposta']!r}")

    espera_honesta = (esperando["estado"] == "esperando" and esperando["botao"]
                      and "carregando" in esperando["texto"])
    respondeu = pergunta["resposta"].startswith("Falaram do instalador")
    # O eco da pergunta é o que permite ler a resposta depois de a tela ter
    # rolado — sem ele, "sim" na tela não quer dizer nada.
    ecoou = "o que ficou decidido?" in pergunta["echo"]
    avisou_do_corte = pergunta["aviso"]
    limpou = pergunta["campo"] == ""
    fora_da_lista = not pergunta["naLista"]

    print(f"\n   a espera diz por que está esperando:          {espera_honesta}")
    print(f"   respondeu, com o eco da pergunta:            {respondeu and ecoou}")
    print(f"   avisou que só viu a parte final da reunião:  {avisou_do_corte}")
    print(f"   limpou o campo para a próxima:               {limpou}")
    print(f"   a resposta ficou FORA da lista de falas:     {fora_da_lista}")

    esq = [c for c in caixas if c["id"] != "previa"]
    previa = next(c for c in caixas if c["id"] == "previa")
    colunas = len({c["x"] for c in esq}) == 1 and previa["x"] > max(c["x"] for c in esq)
    print(f"   duas colunas, nada vazando para a direita:    {colunas}")

    if erros:
        print("\nERROS DE JAVASCRIPT:\n  " + "\n  ".join(erros))

    ok = (separadores == 1 and agrupou and um_volatil and lados
          and colunas and espera_honesta and respondeu and ecoou
          and avisou_do_corte and limpou and fora_da_lista and not erros)
    print("\nVEREDITO:", "o painel ao vivo desenha" if ok else "QUEBRADO")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
