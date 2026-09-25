#!/usr/bin/env python3
"""Prova Ajustes › Clientes e projetos num Chromium de verdade.

**Por que existe.** A seção é mestre-detalhe (plano 4a do redesenho de UI,
spec 2026-09-23-ui-ux.md §3.4): um cliente escolhido que some depois de salvar,
uma contagem que não bate ou uma busca que tira o cursor de quem digita não dão
erro nenhum — só aparecem rodando.

Usa a ponte falsa e o servidor de tools/provar_reunioes.py, e responde antes
dela, por ``window.__responder``, o que esta tela lê e escreve: clientes,
preferências por projeto e os tipos de ata, com estado que muda ao salvar.

Uso::

    uv run --with playwright python tools/provar_ajustes.py [--so prova_x] [--fotos PASTA]
"""

from __future__ import annotations

import argparse
import json
import socketserver
import sys
import threading
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import provar_reunioes as R  # noqa: E402 — a ponte falsa e o servidor são os de lá

conferir = R.conferir

ESTADO = r"""
(() => {
  const clientes = {
    "Algar": ["Agentes", "Agente de Crédito"],
    "Beegol (interno)": ["App", "Gestão", "Marca"],
    "Coca-Cola — CCIL": ["Pedido Sugerido"],
    "Vivo": ["Sherlock", "Portabilidade", "URA", "Ofertas"],
  };
  const prefs = {
    "Algar::Agentes": { language: "pt", model_size: "large-v3", diarization: true,
      initial_prompt: "Agentes, Algar, Beegol, cobrança, Rafael, URA, Carol", tipo_de_ata: "cliente" },
    "Algar::Agente de Crédito": { tipo_de_ata: "cliente" },
  };
  const tipos = [{ id: "geral", nome: "Reunião geral" }, { id: "cliente", nome: "Reunião com cliente" }];
  window.__estado = { clientes, prefs };
  window.__responder = (q) => {
    const k = `${q.cliente}::${q.projeto}`;
    switch (q.op) {
      case "clientes": return { clientes };
      case "prefs": return { prefs: prefs[k] ?? null };
      case "modelos-de-ata": return { tipos };
      case "vozes": return { vozes: [] };
      case "gravador": return { gravador: {} };
      case "diagnostico": return { diagnostico: { marca: "PulseMeet", versao: "0.7.1", texto: "" } };
      case "salvar-projeto":
        (clientes[q.cliente] ??= []);
        if (!clientes[q.cliente].includes(q.projeto)) clientes[q.cliente].push(q.projeto);
        prefs[k] = { ...(prefs[k] ?? {}), ...q.prefs };
        return { clientes };
      // Como o núcleo: nome em uso é erro, e não um "ok" que não fez nada.
      case "renomear-projeto": {
        if (clientes[q.cliente].includes(q.nome)) return { erro: "já existe um projeto com esse nome" };
        const l = clientes[q.cliente]; l[l.indexOf(q.projeto)] = q.nome;
        prefs[`${q.cliente}::${q.nome}`] = prefs[k]; delete prefs[k];
        return { clientes };
      }
      case "apagar-projeto":
        clientes[q.cliente] = clientes[q.cliente].filter((p) => p !== q.projeto);
        return { clientes };
      case "renomear-cliente":
        if (clientes[q.nome]) return { erro: "já existe um cliente com esse nome" };
        clientes[q.nome] = clientes[q.cliente]; delete clientes[q.cliente]; return { clientes };
      case "apagar-cliente": delete clientes[q.cliente]; return { clientes };
      default: return null;
    }
  };
})();
"""


def abrir(navegador, porta: int, largura: int = 1280, altura: int = 800, tema: str | None = None):
    pagina = navegador.new_page(viewport={"width": largura, "height": altura})
    erros: list[str] = []
    pagina.on("pageerror", lambda e: erros.append(str(e)))
    pagina.add_init_script(R.PONTE_FALSA)
    pagina.add_init_script(ESTADO)
    if tema:
        pagina.add_init_script(
            "document.addEventListener('DOMContentLoaded', () => "
            f"document.documentElement.dataset.tema = '{tema}')")
    pagina.goto(f"http://127.0.0.1:{porta}/index.html", wait_until="load")
    pagina.wait_for_selector(".reuniao-linha", timeout=5000)
    pagina.click("#ir-config")
    pagina.click(".aba >> text=Clientes")
    pagina.wait_for_selector(".clientes__projeto", timeout=5000)
    pagina.wait_for_timeout(100)
    return pagina, erros


def textos(pagina, seletor: str) -> list[str]:
    return pagina.eval_on_selector_all(seletor, "els => els.map((e) => e.textContent.trim())")


def pedidos(pagina, op: str) -> list[dict]:
    return pagina.evaluate(f"() => window.__pedidos.filter((q) => q.op === {json.dumps(op)})")


def escolher_cliente(pagina, nome: str) -> None:
    pagina.click(f".clientes__cliente >> text={nome}")
    pagina.wait_for_timeout(100)


def abrir_projeto(pagina, nome: str) -> None:
    topo = pagina.locator(".clientes__projeto-topo", has=pagina.locator(f"text='{nome}'"))
    if topo.get_attribute("aria-expanded") != "true":
        topo.click()
    pagina.wait_for_selector(".clientes__projeto--aberto .clientes__campos", timeout=3000)


# ─────────────────────────────────────────────────────────────── as provas

def prova_mestre(pagina) -> None:
    nomes = textos(pagina, ".clientes__cliente .clientes__nome")
    conferir(nomes == ["Algar", "Beegol (interno)", "Coca-Cola — CCIL", "Vivo"],
             f"os clientes em ordem, à esquerda ({nomes})")
    subs = textos(pagina, ".clientes__cliente .clientes__sub")
    conferir(subs[0] == "2 projetos · 2 reuniões" and subs[3] == "4 projetos · 1 reunião",
             f"cada um com projetos e reuniões ({subs})")
    conferir(pagina.locator(".clientes__cliente[aria-current='true']").inner_text().startswith("Algar"),
             "o primeiro cliente vem escolhido")
    conferir(pagina.is_visible(".clientes__cabeca >> text=+ Cliente"), "o '+ Cliente' está no cabeçalho")
    conferir(pagina.inner_text(".clientes__detalhe-nome") == "Algar", "à direita, o nome do escolhido")
    conferir(pagina.is_visible(".clientes__detalhe >> text=+ Projeto"), "e o '+ Projeto'")
    projetos = textos(pagina, ".clientes__projeto .clientes__nome")
    conferir(projetos == ["Agente de Crédito", "Agentes"], f"os projetos do escolhido ({projetos})")
    escolher_cliente(pagina, "Vivo")
    conferir(len(textos(pagina, ".clientes__projeto")) == 4, "escolher outro cliente mostra os projetos dele")


def prova_projeto_aberto(pagina) -> None:
    abrir_projeto(pagina, "Agentes")
    aberto = ".clientes__projeto--aberto"
    sub = pagina.inner_text(f"{aberto} .clientes__sub")
    conferir(sub.startswith("1 reunião · última em"), f"o projeto diz reuniões e a última ({sub!r})")
    conferir("Reunião com cliente" in pagina.inner_text(f"{aberto} .clientes__tipo"),
             "e o tipo de ata à direita")
    termos = textos(pagina, f"{aberto} .etiquetas__termo")
    conferir(len(termos) == 7 and termos[0].startswith("Agentes"), f"o vocabulário em etiquetas ({termos})")
    rotulos = textos(pagina, f"{aberto} .clientes__campos .campo > span")
    conferir(rotulos == ["Idioma", "Modelo", "Falantes", "Tipo de ata"], f"os quatro campos na ordem ({rotulos})")
    valores = pagina.eval_on_selector_all(
        f"{aberto} .clientes__campos select", "els => els.map((s) => s.selectedOptions[0].textContent)")
    conferir(valores == ["Português", "Large v3", "Separar", "Reunião com cliente"], f"com os valores do projeto ({valores})")
    botoes = textos(pagina, f"{aberto} .clientes__acoes button")
    conferir(botoes == ["Ver as reuniões deste projeto", "Renomear", "Apagar projeto"], f"e as três ações ({botoes})")
    # D-C: o que a prancha não mostra vem depois do que ela mostra.
    depois = pagina.evaluate(f"""() => {{
      const a = document.querySelector('{aberto} .clientes__acoes');
      const d = document.querySelector('{aberto} .clientes__mais');
      return Boolean(d) && Boolean(a.compareDocumentPosition(d) & Node.DOCUMENT_POSITION_FOLLOWING);
    }}""")
    conferir(depois, "o modelo de diarização e a dica ficam abaixo das ações")


def prova_busca(pagina) -> None:
    pagina.click("#busca-clientes")
    pagina.locator("#busca-clientes").press_sequentially("coca")
    conferir(textos(pagina, ".clientes__cliente .clientes__nome") == ["Coca-Cola — CCIL"], "a busca filtra os clientes")
    conferir(pagina.evaluate("() => document.activeElement.id") == "busca-clientes", "sem tirar o cursor do campo")
    pagina.fill("#busca-clientes", "zzz")
    conferir(pagina.is_visible(".clientes__nada"), "sem resultado, diz que não achou")


def prova_gravar(pagina) -> None:
    abrir_projeto(pagina, "Agentes")
    aberto = ".clientes__projeto--aberto"
    pagina.fill(f"{aberto} .etiquetas__novo", "NOC")
    pagina.keyboard.press("Enter")
    ultimo = lambda: pedidos(pagina, "salvar-projeto")[-1]  # noqa: E731
    pagina.wait_for_timeout(100)
    conferir(ultimo()["prefs"]["initial_prompt"].endswith(", NOC"), "pôr etiqueta salva o vocabulário")
    conferir(pagina.evaluate("() => document.activeElement.classList.contains('etiquetas__novo')"),
             "e o cursor continua no campo de termo")
    pagina.select_option(f"{aberto} #projeto-tipo", "")
    pagina.wait_for_timeout(100)
    conferir(ultimo()["prefs"]["tipo_de_ata"] == "", "voltar o tipo ao padrão grava vazio, e não nulo")
    conferir("padrão" in pagina.inner_text(f"{aberto} .clientes__tipo"), "e a linha passa a dizer o padrão")
    pagina.select_option(f"{aberto} #projeto-falantes", "nao")
    pagina.wait_for_timeout(100)
    conferir(ultimo()["prefs"]["diarization"] is False, "Falantes: Não separar")
    pagina.select_option(f"{aberto} #projeto-idioma", "en")
    pagina.wait_for_timeout(100)
    p = ultimo()
    conferir(p["cliente"] == "Algar" and p["projeto"] == "Agentes" and p["prefs"]["language"] == "en",
             "Idioma vai para o projeto certo")


def prova_criar(pagina) -> None:
    pagina.click(".clientes__detalhe >> text=+ Projeto")
    pagina.fill("dialog[open] input", "Cobrança")
    pagina.keyboard.press("Enter")
    pagina.wait_for_selector(".clientes__projeto--aberto >> text=Cobrança", timeout=3000)
    p = pedidos(pagina, "salvar-projeto")[-1]
    conferir(p["cliente"] == "Algar" and p["projeto"] == "Cobrança", "'+ Projeto' cria no cliente escolhido, e o abre")
    pagina.click(".clientes__cabeca >> text=+ Cliente")
    pagina.fill("dialog[open] input", "TIM")
    pagina.keyboard.press("Enter")
    pagina.wait_for_timeout(100)
    pagina.fill("dialog[open] input", "Geral")
    pagina.keyboard.press("Enter")
    pagina.wait_for_selector(".clientes__cliente[aria-current='true'] >> text=TIM", timeout=3000)
    p = pedidos(pagina, "salvar-projeto")[-1]
    conferir(p["cliente"] == "TIM" and p["projeto"] == "Geral", "'+ Cliente' cria com o primeiro projeto, e o escolhe")


def prova_renomear_e_apagar(pagina) -> None:
    escolher_cliente(pagina, "Vivo")
    abrir_projeto(pagina, "URA")
    pagina.click(".clientes__projeto--aberto >> text=Renomear")
    pagina.fill("dialog[open] input", "URA nova")
    pagina.keyboard.press("Enter")
    pagina.wait_for_selector(".clientes__projeto--aberto >> text=URA nova", timeout=3000)
    conferir(pagina.locator(".clientes__cliente[aria-current='true']").inner_text().startswith("Vivo"),
             "renomear o projeto mantém o cliente e o projeto escolhidos")
    pagina.click(".clientes__projeto--aberto >> text=Apagar projeto")
    pagina.click("dialog[open] button.aa-btn-primario")
    pagina.wait_for_timeout(200)
    conferir("URA nova" not in textos(pagina, ".clientes__projeto .clientes__nome"), "apagar o projeto o tira da lista")
    pagina.click(".clientes__mais-cliente")
    conferir(pagina.is_visible(".popover >> text=Renomear cliente") and pagina.is_visible(".popover >> text=Apagar cliente"),
             "o ⋯ do cliente guarda renomear e apagar o cliente")


def prova_limpar(pagina) -> None:
    # I-1: limpar manda "" (o núcleo tira a chave), e nunca nulo, que o
    # WhenWritingNull descarta e deixava o valor antigo no disco.
    abrir_projeto(pagina, "Agentes")
    aberto = ".clientes__projeto--aberto"
    for _ in range(7):
        pagina.click(f"{aberto} .etiquetas__novo")
        pagina.keyboard.press("Backspace")
    pagina.wait_for_timeout(100)
    pagina.select_option(f"{aberto} #projeto-idioma", "")
    pagina.select_option(f"{aberto} #projeto-modelo", "")
    pagina.wait_for_timeout(150)
    p = pedidos(pagina, "salvar-projeto")[-1]["prefs"]
    conferir(p["initial_prompt"] == "" and p["language"] == "" and p["model_size"] == "",
             f"limpar vocabulário, idioma e modelo manda vazio ({p})")
    conferir(all(v is not None for v in p.values()), "e nenhum nulo")


def prova_renomear_para_nome_existente(pagina) -> None:
    abrir_projeto(pagina, "Agentes")
    pagina.click(".clientes__projeto--aberto >> text=Renomear")
    dica = pagina.inner_text("dialog[open]")
    conferir("reuniões já feitas" in dica, f"o pedido de nome avisa que as reuniões guardam o nome antigo ({dica!r})")
    pagina.fill("dialog[open] input", "Agente de Crédito")
    pagina.keyboard.press("Enter")
    pagina.wait_for_selector("dialog[open] >> text=já existe", timeout=3000)
    conferir(True, "renomear para um projeto que existe diz que já existe")
    pagina.click("dialog[open] button")
    pagina.wait_for_timeout(100)
    conferir(pedidos(pagina, "renomear-projeto") == [], "e não pede a renomeação")
    conferir("Agentes" in pagina.inner_text(".clientes__projeto--aberto .clientes__nome"),
             "e continua no projeto em que estava")
    pagina.click(".clientes__mais-cliente")
    pagina.click(".popover >> text=Renomear cliente")
    pagina.fill("dialog[open] input", "Vivo")
    pagina.keyboard.press("Enter")
    pagina.wait_for_selector("dialog[open] >> text=já existe", timeout=3000)
    pagina.click("dialog[open] button")
    conferir(pagina.locator(".clientes__cliente[aria-current='true']").inner_text().startswith("Algar"),
             "renomear o cliente para um que existe avisa e fica no mesmo")


def prova_cliente_existente(pagina) -> None:
    pagina.click(".clientes__cabeca >> text=+ Cliente")
    pagina.fill("dialog[open] input", "vivo")
    pagina.keyboard.press("Enter")
    pagina.wait_for_selector("dialog[open] >> text=já existe", timeout=3000)
    conferir(True, "'+ Cliente' com nome que existe (sem olhar caixa) diz que já existe")
    pagina.click("dialog[open] button")
    conferir(pedidos(pagina, "salvar-projeto") == [], "e não cria nada")


def prova_acabamento(pagina) -> None:
    ap = pagina.eval_on_selector(".clientes select.aa-entrada", "s => getComputedStyle(s).appearance")
    conferir(ap == "none", f"os seletores de Clientes têm uma seta só ({ap})")
    pe = pagina.inner_text(".abas__versao") if pagina.locator(".abas__versao").count() else ""
    conferir(pe == "PulseMeet 0.7.1", f"o pé do menu de Ajustes diz a marca e a versão ({pe!r})")


def prova_ver_reunioes(pagina) -> None:
    abrir_projeto(pagina, "Agentes")
    pagina.click("text=Ver as reuniões deste projeto")
    pagina.wait_for_selector(".reuniao-linha", timeout=5000)
    titulos = R.titulos(pagina)
    conferir(titulos == ["Comunicação Beegol + App"], f"leva a Reuniões só com as do projeto ({titulos})")
    conferir(pagina.input_value("#filtro-cliente") == "Algar", "com o cliente no filtro")


def prova_estreita(pagina) -> None:
    abrir_projeto(pagina, "Agentes")
    conferir(pagina.evaluate(R.SEM_ROLAGEM_LATERAL), "a 1000 px não rola de lado")


prova_estreita.janela = (1000, 700)

PROVAS = [prova_mestre, prova_projeto_aberto, prova_busca, prova_gravar, prova_criar,
          prova_renomear_e_apagar, prova_limpar, prova_renomear_para_nome_existente,
          prova_cliente_existente, prova_acabamento, prova_ver_reunioes, prova_estreita]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--fotos", type=Path, help="pasta onde deixar as fotos da seção")
    ap.add_argument("--so", help="só estas provas, separadas por vírgula")
    args = ap.parse_args()
    provas = PROVAS
    if args.so:
        pedidas = set(args.so.split(","))
        provas = [p for p in PROVAS if p.__name__ in pedidas]
        if len(provas) != len(pedidas):
            print(f"prova desconhecida: {sorted(pedidas - {p.__name__ for p in PROVAS})}", file=sys.stderr)
            return 2

    from playwright.sync_api import sync_playwright

    socketserver.TCPServer.allow_reuse_address = True
    with socketserver.TCPServer(("127.0.0.1", 0), R.Servidor) as srv:
        porta = srv.server_address[1]
        threading.Thread(target=srv.serve_forever, daemon=True).start()
        with sync_playwright() as pw:
            navegador = pw.chromium.launch()
            for prova in provas:
                print(f"── {prova.__name__}")
                largura, altura = getattr(prova, "janela", (1280, 800))
                try:
                    pagina, erros = abrir(navegador, porta, largura, altura)
                except Exception as e:  # noqa: BLE001
                    conferir(False, f"{prova.__name__} não abriu: {str(e).splitlines()[0]}")
                    continue
                try:
                    prova(pagina)
                except Exception as e:  # noqa: BLE001
                    conferir(False, f"{prova.__name__} estourou: {str(e).splitlines()[0]}")
                conferir(not erros, f"sem erro de JavaScript {erros[:2] if erros else ''}")
                pagina.close()

            if args.fotos:
                args.fotos.mkdir(parents=True, exist_ok=True)
                for tema in ["escuro", "claro"]:
                    for largura, altura in [(1280, 800), (1000, 700)]:
                        pagina, _ = abrir(navegador, porta, largura, altura, tema)
                        abrir_projeto(pagina, "Agentes")
                        pagina.mouse.move(0, 0)
                        pagina.evaluate("() => document.querySelector('.conteudo').scrollTo(0, 0)")
                        pagina.wait_for_timeout(300)
                        pagina.screenshot(path=str(args.fotos / f"clientes-{tema}-{largura}.png"))
                        pagina.close()
            navegador.close()
        srv.shutdown()

    if R.falhas:
        print(f"\n{len(R.falhas)} falha(s).", file=sys.stderr)
        return 1
    print("\ntudo certo.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
