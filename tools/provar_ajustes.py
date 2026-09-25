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

  // A biblioteca de vozes do gabarito (aj-vozes.png): sete pessoas, três
  // amostras em quarentena, "Elio" e "Élio", e 48 amostras fora de uso.
  const A = (dia, extra = {}) => ({
    criada_em: `2026-09-${String(dia).padStart(2, "0")}T14:00:00`, duracao_s: 4.2,
    gravacao: `2026-09-${String(dia).padStart(2, "0")}_14-00-00`, faixa: "system", t0: 10, t1: 14,
    dispositivo: "Headset", quarentena: false, outro_modelo: false, regras_antigas: false,
    trecho: "Carol/a.wav", semelhanca: 0.82, ...extra,
  });
  const varias = (n, dia, extra = {}, aparelhos = ["Headset"]) =>
    Array.from({ length: n }, (_, i) => A(dia - (i % 9), { dispositivo: aparelhos[i % aparelhos.length], ...extra }));
  const pessoas = [
    ["Você (André)", [...varias(22, 17, { faixa: "mic" }, ["Microfone (USB)", "Headset"]), ...varias(20, 10, { regras_antigas: true })]],
    ["Carol Souza", [...varias(13, 16, {}, ["Headset", "Sala Conf B", "Notebook"]),
                     A(17, { quarentena: true, duracao_s: 3.1, dispositivo: "Sala Conf B", semelhanca: 0.41 })]],
    ["Marcos", [...varias(8, 15), A(16, { quarentena: true, duracao_s: 2.4, dispositivo: "Alto-falantes (Realtek)", semelhanca: 0.38 })]],
    ["Élio", varias(4, 12)],
    ["Heitor Lima", varias(5, 11)],
    ["Rafael Prado", [...varias(2, 14), A(17, { quarentena: true, duracao_s: 4.0, dispositivo: "Sala Conf B", semelhanca: 0.45, trecho: null })]],
    ["Elio", varias(2, 9)],
    ["Dimi Randel", [...varias(27, 9, { regras_antigas: true }), A(3, { outro_modelo: true, regras_antigas: true })]],
  ];
  let vozes = pessoas.map(([nome, amostras]) => ({ nome, amostras }));
  let parecidos = [{ a: "Elio", b: "Élio", semelhanca: 0.93 }];
  const numerar = () => { for (const p of vozes) p.amostras.forEach((a, i) => { a.indice = i; }); };
  numerar();
  const achar = (q) => {
    const p = vozes.find((x) => x.nome === q.pessoa);
    const a = p?.amostras[q.indice];
    return a && (!q.criada_em || a.criada_em === q.criada_em) ? [p, a] : [];
  };
  const tirar = (p, a) => { p.amostras.splice(p.amostras.indexOf(a), 1); if (!p.amostras.length) vozes.splice(vozes.indexOf(p), 1); };
  const pessoa = (nome) => vozes.find((x) => x.nome === nome) ?? (vozes.push({ nome, amostras: [] }), vozes.at(-1));
  window.__vozes = {
    responder(q) {
      let fez = true;
      if (q.op !== "vozes") {
        const [p, a] = achar(q);
        switch (q.op) {
          case "aprovar-voz": if (a) a.quarentena = false; else fez = false; break;
          case "esquecer-voz": if (a) tirar(p, a); else fez = false; break;
          case "mover-voz": if (a) { tirar(p, a); a.quarentena = false; pessoa(q.nome).amostras.push(a); } else fez = false; break;
          case "apagar-voz": vozes = vozes.filter((x) => x.nome !== q.pessoa); break;
          case "juntar-vozes": {
            const de = vozes.find((x) => x.nome === q.pessoa);
            vozes = vozes.filter((x) => x !== de);
            pessoa(q.nome).amostras.push(...de.amostras);
            vozes.sort((x, y) => x.nome.localeCompare(y.nome));
            break;
          }
        }
        numerar();
        const nomes = vozes.map((x) => x.nome);
        parecidos = parecidos.filter((x) => nomes.includes(x.a) && nomes.includes(x.b));
      }
      return fez ? { vozes, parecidos } : { erro: "a biblioteca de vozes mudou; a tela foi atualizada" };
    },
  };
  window.__responder = (q) => {
    const k = `${q.cliente}::${q.projeto}`;
    switch (q.op) {
      case "clientes": return { clientes };
      case "prefs": return { prefs: prefs[k] ?? null };
      case "modelos-de-ata": return { tipos };
      case "vozes": return window.__vozes.responder(q);
      case "aprovar-voz": case "esquecer-voz": case "mover-voz":
      case "apagar-voz": case "juntar-vozes": return window.__vozes.responder(q);
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


def abrir(navegador, porta: int, largura: int = 1280, altura: int = 800, tema: str | None = None,
          secao: str = "Clientes"):
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
    pagina.click(f".aba >> text={secao}")
    pagina.wait_for_selector(".clientes__projeto" if secao == "Clientes" else ".vozes__item", timeout=5000)
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


# ───────────────────────────────────────────────────────── Vozes (plano 6)

def vozes(prova):
    """A prova abre em Ajustes › Vozes, e não em Clientes."""
    prova.secao = "Vozes"
    return prova


def escolher_pessoa(pagina, nome: str) -> None:
    pagina.click(f".vozes__pessoas .vozes__item[data-pessoa='{nome}']")
    pagina.wait_for_selector(f".vozes__detalhe-nome >> text='{nome}'", timeout=3000)


@vozes
def prova_vozes_mestre(pagina) -> None:
    rev = pagina.locator(".vozes__revisar")
    conferir(rev.get_attribute("aria-current") == "true", "'Para revisar' vem escolhido, em cima")
    conferir(pagina.inner_text(".vozes__revisar .vozes__contagem") == "3", "com o número de amostras a revisar")
    conferir("soaram diferente" in rev.inner_text(), "e o que ele é")
    rotulo = pagina.inner_text(".vozes__rotulo")
    conferir(rotulo.upper() == "PESSOAS · 7", f"o rótulo das pessoas com a contagem ({rotulo!r})")
    nomes = textos(pagina, ".vozes__pessoas .vozes__nome")
    conferir(nomes == ["Você (André)", "Carol Souza", "Marcos", "Heitor Lima", "Élio", "Rafael Prado", "Elio"],
             f"as pessoas, de quem tem mais voz para quem tem menos ({nomes})")
    subs = textos(pagina, ".vozes__pessoas .vozes__sub")
    conferir(subs[0] == "22 amostras · 2 aparelhos" and subs[1] == "14 amostras · 3 aparelhos",
             f"cada uma com amostras e aparelhos ({subs[:2]})")
    saude = textos(pagina, ".vozes__pessoas .vozes__saude")
    conferir(saude == ["boa", "boa", "boa", "boa", "boa", "pouca voz", "pouca voz"], f"e a saúde ({saude})")
    fora = pagina.locator(".vozes__fora")
    conferir(fora.get_attribute("open") is None, "'Fora de uso' vem dobrado")
    conferir(pagina.inner_text(".vozes__fora > summary").strip() == "Fora de uso · 48 amostras",
             f"e diz quantas ({pagina.inner_text('.vozes__fora > summary')!r})")
    depois = pagina.evaluate("""() => {
      const l = document.querySelector('.vozes__pessoas'), f = document.querySelector('.vozes__fora');
      return Boolean(l.compareDocumentPosition(f) & Node.DOCUMENT_POSITION_FOLLOWING);
    }""")
    conferir(depois, "no fim da lista")


@vozes
def prova_vozes_fila(pagina) -> None:
    conferir(pagina.inner_text(".vozes__detalhe-nome") == "Para revisar", "à direita, a fila")
    casos = textos(pagina, ".vozes__caso .vozes__marcada")
    conferir(casos == ["Marcada como Carol Souza", "Marcada como Rafael Prado", "Marcada como Marcos"],
             f"um caso por amostra em quarentena, a mais nova primeiro ({casos})")
    proc = textos(pagina, ".vozes__caso .vozes__proc")[0]
    conferir(proc == "17 set · áudio da reunião · 3,1 s · Sala Conf B", f"com a procedência ({proc!r})")
    sim = textos(pagina, ".vozes__caso .vozes__semelhanca")[0]
    conferir(sim.replace("\n", " ") == "semelhança 0,41", f"e a semelhança ({sim!r})")
    botoes = textos(pagina, ".vozes__caso:first-child .vozes__respostas > * button, .vozes__caso:first-child .vozes__respostas > button")
    conferir(botoes[0] == "É Carol" and "É outra pessoa" in botoes[1] and botoes[-1] == "Descartar",
             f"e as três respostas ({botoes})")
    conferir(pagina.locator(".vozes__caso").nth(1).locator(".tocar").is_disabled(),
             "sem trecho guardado, o play fica desligado")


@vozes
def prova_vozes_tocar(pagina) -> None:
    pagina.evaluate("() => { HTMLMediaElement.prototype.play = function () { this.dispatchEvent(new Event('play')); return Promise.resolve(); }; }")
    pagina.locator(".vozes__caso .tocar").first.click()
    src = pagina.evaluate("() => document.getElementById('audio').getAttribute('src')")
    conferir(src == "https://vozes.local/Carol/a.wav", f"o play toca o trecho no <audio> único ({src})")
    conferir(pagina.evaluate("() => document.querySelectorAll('audio').length") == 1, "e não cria outro <audio>")


@vozes
def prova_vozes_respostas(pagina) -> None:
    caso = lambda n: pagina.locator(".vozes__caso").nth(n)  # noqa: E731
    caso(0).locator("text=É Carol").click()
    pagina.wait_for_timeout(150)
    q = pedidos(pagina, "aprovar-voz")[-1]
    conferir(q["pessoa"] == "Carol Souza" and q["indice"] == 13 and q["criada_em"],
             f"'É Carol' aprova aquela amostra, com o carimbo dela ({q})")
    conferir(pagina.inner_text(".vozes__revisar .vozes__contagem") == "2", "e a fila diminui")

    # Descartar confirma; cancelar não faz nada.
    caso(0).locator("text=Descartar").click()
    pagina.wait_for_selector("dialog[open]", timeout=2000)
    conferir("Rafael Prado" in pagina.inner_text("dialog[open]"), "Descartar pergunta antes, dizendo de quem")
    pagina.click("dialog[open] >> text=Cancelar")
    pagina.wait_for_timeout(100)
    conferir(pedidos(pagina, "esquecer-voz") == [], "cancelar não descarta")
    caso(0).locator("text=Descartar").click()
    pagina.click("dialog[open] button.aa-btn-primario")
    pagina.wait_for_timeout(150)
    q = pedidos(pagina, "esquecer-voz")[-1]
    conferir(q["pessoa"] == "Rafael Prado" and q["indice"] == 2, f"confirmar descarta só aquela ({q})")

    # É outra pessoa: escolhe entre as que existem.
    caso(0).locator(".vozes__outra").click()
    opcoes = textos(pagina, ".popover .vozes__menu button")
    conferir("Marcos" not in opcoes and "Carol Souza" in opcoes and opcoes[-1] == "Pessoa nova…",
             f"'É outra pessoa' lista as outras e uma nova ({opcoes})")
    pagina.click(".popover >> text=Heitor Lima")
    pagina.wait_for_timeout(150)
    q = pedidos(pagina, "mover-voz")[-1]
    conferir(q["pessoa"] == "Marcos" and q["nome"] == "Heitor Lima", f"e move a amostra para ela ({q})")
    conferir(pagina.locator(".vozes__caso").count() == 0 and pagina.is_visible(".vozes__nada"),
             "fila vazia diz que não há nada para revisar")


@vozes
def prova_vozes_sugestao(pagina) -> None:
    sug = pagina.locator(".vozes__sugestao")
    conferir(sug.count() == 1, "a sugestão de juntar aparece abaixo da fila")
    conferir("“Elio” e “Élio” parecem a mesma pessoa" in sug.inner_text(), f"com os dois nomes ({sug.inner_text()!r})")
    conferir("0,93" in sug.inner_text(), "e a semelhança entre eles")
    sug.locator("text=Não são").click()
    pagina.wait_for_timeout(100)
    conferir(pagina.locator(".vozes__sugestao").count() == 0, "'Não são' a esconde")
    conferir(pedidos(pagina, "juntar-vozes") == [], "sem juntar nada")


@vozes
def prova_vozes_juntar_sugerido(pagina) -> None:
    pagina.click(".vozes__sugestao >> text=Juntar")
    pagina.wait_for_selector("dialog[open]", timeout=2000)
    pagina.click("dialog[open] button.aa-btn-primario")
    pagina.wait_for_timeout(150)
    q = pedidos(pagina, "juntar-vozes")[-1]
    conferir(q["pessoa"] == "Elio" and q["nome"] == "Élio",
             f"'Juntar' confirma e leva quem tem menos voz para quem tem mais ({q})")
    conferir("Elio" not in textos(pagina, ".vozes__pessoas .vozes__nome"), "e a lista perde o que sumiu")


@vozes
def prova_vozes_perfil(pagina) -> None:
    escolher_pessoa(pagina, "Carol Souza")
    conferir(pagina.locator(".vozes__item[data-pessoa='Carol Souza']").get_attribute("aria-current") == "true",
             "a pessoa escolhida fica marcada")
    linhas = pagina.locator(".vozes__detalhe .vozes__amostra")
    conferir(linhas.count() == 14, f"à direita, as amostras dela ({linhas.count()})")
    conferir(linhas.first.locator(".tocar").count() == 1 and linhas.first.locator(".vozes__mais-amostra").count() == 1,
             "cada uma com play e ⋯")
    linhas.first.locator(".vozes__mais-amostra").click()
    conferir(textos(pagina, ".popover .vozes__menu > button") == ["Mover para…", "Remover"], "o ⋯ da amostra: mover, remover")
    pagina.click(".popover >> text=Remover")
    pagina.wait_for_selector("dialog[open]", timeout=2000)
    pagina.click("dialog[open] button.aa-btn-primario")
    pagina.wait_for_timeout(150)
    q = pedidos(pagina, "esquecer-voz")[-1]
    conferir(q["pessoa"] == "Carol Souza" and q["indice"] == 0 and q["criada_em"], f"Remover confirma e tira aquela ({q})")
    pagina.locator(".vozes__detalhe .vozes__amostra").first.locator(".vozes__mais-amostra").click()
    pagina.click(".popover >> text=Mover para…")
    pagina.click(".popover >> text=Marcos")
    pagina.wait_for_timeout(150)
    q = pedidos(pagina, "mover-voz")[-1]
    conferir(q["pessoa"] == "Carol Souza" and q["nome"] == "Marcos", f"Mover para… leva a amostra ({q})")
    pagina.click(".vozes__mais-pessoa")
    conferir(textos(pagina, ".popover .vozes__menu > button") == ["Renomear", "Juntar com…", "Apagar perfil"],
             "o ⋯ do cabeçalho: renomear, juntar, apagar")
    pagina.click(".popover >> text=Apagar perfil")
    pagina.wait_for_selector("dialog[open]", timeout=2000)
    pagina.click("dialog[open] button.aa-btn-primario")
    pagina.wait_for_timeout(150)
    conferir(pedidos(pagina, "apagar-voz")[-1]["pessoa"] == "Carol Souza", "Apagar perfil confirma e apaga")
    conferir(pagina.locator(".vozes__revisar").get_attribute("aria-current") == "true", "e volta para a fila")


@vozes
def prova_vozes_renomear(pagina) -> None:
    escolher_pessoa(pagina, "Marcos")
    pagina.click(".vozes__mais-pessoa")
    pagina.click(".popover >> text=Renomear")
    pagina.fill("dialog[open] input", "Marcos Paulo")
    pagina.keyboard.press("Enter")
    pagina.wait_for_selector(".vozes__detalhe-nome >> text='Marcos Paulo'", timeout=3000)
    q = pedidos(pagina, "juntar-vozes")[-1]
    conferir(q["pessoa"] == "Marcos" and q["nome"] == "Marcos Paulo", f"renomear é juntar num nome novo ({q})")


@vozes
def prova_vozes_abaixo(pagina) -> None:
    # D-C: o que a tela tinha e a prancha não mostra vem depois.
    escolher_pessoa(pagina, "Você (André)")
    inertes = pagina.locator(".vozes__inertes .vozes__amostra")
    conferir(inertes.count() == 20, f"as amostras fora de uso da pessoa aparecem, apagadas ({inertes.count()})")
    depois = pagina.evaluate("""() => {
      const a = document.querySelector('.vozes__amostras'), i = document.querySelector('.vozes__inertes');
      return Boolean(a.compareDocumentPosition(i) & Node.DOCUMENT_POSITION_FOLLOWING);
    }""")
    conferir(depois, "abaixo das que estão em uso")
    conferir("antes da guarda de contaminação" in inertes.first.inner_text(), "com o motivo")
    pagina.click(".vozes__fora > summary")
    pagina.click(".vozes__fora .vozes__item[data-pessoa='Dimi Randel']")
    pagina.wait_for_selector(".vozes__detalhe-nome >> text='Dimi Randel'", timeout=3000)
    conferir("modelo de voz antigo" in pagina.inner_text(".vozes__inertes"), "quem só tem voz fora de uso abre do 'Fora de uso'")
    escolher_pessoa(pagina, "Rafael Prado")
    conferir(pagina.locator(".vozes__amostra[data-quarentena='true'] >> text=Aprovar").count() == 1,
             "a amostra em revisão, dentro do perfil, ainda tem o Aprovar")
    conferir(pagina.is_visible(".vozes-obra") or pagina.locator(".painel .obra, .painel .campo__dica").count() > 0,
             "e a nota do que falta comprovar continua embaixo")


@vozes
def prova_vozes_carimbo(pagina) -> None:
    # A biblioteca mudou por baixo: a op volta com erro e a tela relê.
    pagina.evaluate("() => { const c = window.__vozes.responder; window.__vozes.responder = (q) => q.op === 'aprovar-voz' ? { erro: 'a biblioteca de vozes mudou; a tela foi atualizada' } : c(q); }")
    pagina.locator(".vozes__caso").first.locator("text=É Carol").click()
    pagina.wait_for_selector("dialog[open]", timeout=2000)
    conferir("mudou" in pagina.inner_text("dialog[open]"), "a recusa do núcleo aparece numa caixa")
    pagina.click("dialog[open] button")
    pagina.wait_for_timeout(150)
    conferir(len(pedidos(pagina, "vozes")) >= 2, "e a tela relê as vozes")


@vozes
def prova_vozes_estreita(pagina) -> None:
    conferir(pagina.evaluate(R.SEM_ROLAGEM_LATERAL), "a 1000 px não rola de lado")


prova_vozes_estreita.janela = (1000, 700)

PROVAS = [prova_mestre, prova_projeto_aberto, prova_busca, prova_gravar, prova_criar,
          prova_renomear_e_apagar, prova_limpar, prova_renomear_para_nome_existente,
          prova_cliente_existente, prova_acabamento, prova_ver_reunioes, prova_estreita,
          prova_vozes_mestre, prova_vozes_fila, prova_vozes_tocar, prova_vozes_respostas,
          prova_vozes_sugestao, prova_vozes_juntar_sugerido, prova_vozes_perfil, prova_vozes_renomear,
          prova_vozes_abaixo, prova_vozes_carimbo, prova_vozes_estreita]


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
                    pagina, erros = abrir(navegador, porta, largura, altura,
                                          secao=getattr(prova, "secao", "Clientes"))
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
                        pagina, _ = abrir(navegador, porta, largura, altura, tema, secao="Vozes")
                        pagina.mouse.move(0, 0)
                        pagina.wait_for_timeout(300)
                        pagina.screenshot(path=str(args.fotos / f"vozes-{tema}-{largura}.png"))
                        escolher_pessoa(pagina, "Carol Souza")
                        pagina.mouse.move(0, 0)
                        pagina.wait_for_timeout(200)
                        pagina.screenshot(path=str(args.fotos / f"vozes-pessoa-{tema}-{largura}.png"))
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
