#!/usr/bin/env python3
"""Prova a tela de Reuniões num Chromium de verdade.

**Por que existe.** Um filtro que esconde uma reunião não dá erro nenhum: a
lista só fica menor. E redesenhar a barra de busca a cada tecla tira o cursor
de quem digita — o F-2 de docs/FASE7-FRONTEND.md, que só aparece rodando.

Monta o app inteiro com uma ponte falsa, no molde de
tools/provar_painel_ao_vivo.py, e confere o que a tela promete
(docs/superpowers/specs/2026-09-23-ui-ux.md §3.3). Cada prova abre uma página
nova: os critérios moram no módulo, e uma prova não pode herdar os da outra.

Uso::

    uv run --with playwright python tools/provar_reunioes.py [--fotos PASTA]
"""

from __future__ import annotations

import argparse
import http.server
import socketserver
import sys
import threading
from datetime import date, timedelta
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
WEB = RAIZ / "app-net" / "App" / "web"
DS = RAIZ / "assets" / "ds"

# A ponte falsa. As datas nascem de "hoje", porque a lista agrupa por "Hoje" e
# "Ontem" — com datas fixas, a prova quebraria no dia seguinte.
PONTE_FALSA = r"""
(() => {
  const agora = new Date();
  const p = (n) => String(n).padStart(2, "0");
  const nome = (dias, h, m) => {
    const d = new Date(agora.getFullYear(), agora.getMonth(), agora.getDate() - dias);
    return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}_${p(h)}-${p(m)}-00`;
  };
  const G = (n, extra) => Object.assign({
    nome: n, caminho: "C:\\Rec\\" + n, duracao_s: 1800, titulo: null, convidados: 0,
    transcrita: true, cliente: null, projeto: null, com_notas: false, avisos: [],
    tem_ata: false, ata_velha: false, pendencias: 0, pendencias_inicio: [],
    resumo: null, nomes: [], notas_inicio: null,
  }, extra);

  // Na ordem do núcleo: nome da pasta, decrescente — o "reuniao-…" vem primeiro.
  const gravacoes = [
    G("reuniao-importada", { titulo: "Reunião importada", duracao_s: 600 }),
    G(nome(0, 11, 2), { titulo: "Reunião de lideranças", cliente: "Beegol (interno)",
                        projeto: "Gestão", convidados: 9, nomes: ["Heitor Lima"] }),
    G(nome(0, 10, 30), { titulo: "Semanal — Beegol · Uberlândia", transcrita: false, convidados: 6 }),
    G(nome(0, 9, 0), { titulo: "Sherlock Diário — Status e Ações", cliente: "Vivo", projeto: "Sherlock" }),
    G(nome(6, 13, 59), { titulo: "Comunicação Beegol + App", cliente: "Algar", projeto: "Agentes",
      convidados: 7, nomes: ["Carol Souza", "Rafael Prado"], tem_ata: true, pendencias: 3,
      resumo: "O tom da comunicação passa a ser separado por canal.",
      pendencias_inicio: ["Consolidar as referências — Carol — sexta",
                          "Marcar a validação — André — [prazo a definir]",
                          "Revisar os textos de cobrança — Jurídico — [prazo a definir]"],
      notas_inicio: "Definir o tom antes do novo desenho." }),
    G(nome(6, 8, 59), { titulo: "Pedido Sugerido — alinhamento", cliente: "Coca-Cola — CCIL",
                        projeto: "Pedido Sugerido", tem_ata: true, ata_velha: true }),
    G(nome(7, 15, 29), { titulo: "Agente de Crédito — kickoff", cliente: "Algar",
                         projeto: "Agente de Crédito", tem_ata: true }),
    G(nome(7, 10, 30), { avisos: ["O microfone não teve áudio nenhum."] }),
    G(nome(45, 14, 0), { titulo: "Planejamento trimestral", cliente: "Beegol (interno)", projeto: "App" }),
  ];

  const transcricao = JSON.stringify({ language: "pt", segments: [
    { start: 0, end: 4, text: " Bom dia.", speaker: "André" },
    { start: 4, end: 9, text: " Vamos começar.", speaker: "Carol" },
  ] });
  const ata = "# Ata — Comunicação Beegol + App\n\n## Resumo\n\nO tom passa a ser separado por canal.\n\n"
            + "## Pendências\n\n- [ ] Consolidar as referências — **Carol** — sexta\n";

  const responder = (q) => {
    switch (q.op) {
      case "gravacoes": return { gravacoes };
      case "transcricoes": return { transcricoes: { atual: null, ultimo: null } };
      case "catalogo": return { catalogo: [{ pacote: { id: "large-v3", nome: "Large v3", familia: "asr" },
                                             estado: "instalado", em_uso: true }], diarizadores: [] };
      case "atualizacao": return { atualizacao: { nova: null, versao_instalada: "0.7.1" } };
      case "config": return { config: window.__config ?? {} };
      case "transcricao": return { transcricao: window.__transcricao ?? transcricao };
      case "reuniao": return window.__vinculo ?? { cliente: "", projeto: "" };
      case "legenda-gravada": return { legenda_gravada: [] };
      case "clientes": return { clientes: {} };
      case "prefs": return { prefs: window.__prefs ?? null };
      case "notas": return { notas: window.__notas ?? "" };
      case "modelos-de-ata": return { tipos: [{ id: "geral", nome: "Reunião geral" }] };
      case "ata": return window.__atas[q.gravacao] ? { ata: window.__atas[q.gravacao], ata_velha: false }
        : q.gravacao.includes("13-59") ? { ata, ata_velha: false } : { ata: null };
      default: return {};
    }
  };

  window.chrome = { webview: {
    _ouvintes: [],
    addEventListener(_, f) { this._ouvintes.push(f); },
    postMessage(cru) {
      const q = JSON.parse(cru);
      window.__pedidos.push(q);
      const r = Object.assign({ id: q.id }, responder(q));
      setTimeout(() => { for (const f of this._ouvintes) f({ data: JSON.stringify(r) }); }, 0);
    },
  } };
  window.__pedidos = [];
  // O Chromium sem janela não toca o mix (o endereço gravacoes.local não
  // existe aqui). A prova quer saber o que se pediu para tocar: de onde, e a
  // partir de quando. Os eventos são os que o <audio> de verdade dispara.
  HTMLMediaElement.prototype.play = function () {
    window.__tocou = { src: this.getAttribute("src"), t: this.currentTime };
    this.dispatchEvent(new Event("play"));
    return Promise.resolve();
  };
  HTMLMediaElement.prototype.pause = function () { this.dispatchEvent(new Event("pause")); };
  window.__gravacoes = gravacoes;
  window.__atas = {};
  window.__emitir = (ev) => {
    for (const f of window.chrome.webview._ouvintes)
      f({ data: JSON.stringify(Object.assign({ id: 0 }, ev)) });
  };
})();
"""

# Acha uma linha pelo começo do título e a guarda em window.__linha, para
# conferir depois se ela é o MESMO nó — é assim que se prova que a lista não
# foi redesenhada.
LINHA_POR_TITULO = """(inicio) => {
  const l = [...document.querySelectorAll('.reuniao-linha')]
    .find((b) => b.querySelector('.reuniao-linha__titulo').textContent.startsWith(inicio));
  window.__linha = l || null;
  return Boolean(l);
}"""


class Servidor(http.server.SimpleHTTPRequestHandler):
    """Mapeia /ds/ para o design system, como o app faz por recurso embutido."""

    def log_message(self, *args):
        pass

    def translate_path(self, path):
        path = path.split("?")[0]
        if path.startswith("/ds/"):
            return str(DS / path[4:])
        return str(WEB / path.lstrip("/"))


falhas: list[str] = []


def conferir(condicao: bool, o_que: str) -> None:
    print(("  ok     " if condicao else "  FALHA  ") + o_que)
    if not condicao:
        falhas.append(o_que)


def abrir(navegador, porta: int, largura: int = 1280, altura: int = 800, tema: str | None = None,
          relogio: str | None = None):
    pagina = navegador.new_page(viewport={"width": largura, "height": altura})
    erros: list[str] = []
    pagina.on("pageerror", lambda e: erros.append(str(e)))
    # O relógio falso antes da ponte falsa: ela monta o acervo a partir de
    # "hoje", e instalado depois o acervo nasce da data de verdade.
    if relogio:
        pagina.clock.install(time=relogio)
    pagina.add_init_script(PONTE_FALSA)
    # Antes da primeira pintura, como o núcleo faz ao servir a página. Trocar
    # depois fotografa os botões no meio da transição de cor do design system.
    if tema:
        pagina.add_init_script(
            "document.addEventListener('DOMContentLoaded', () => "
            f"document.documentElement.dataset.tema = '{tema}')")
    pagina.goto(f"http://127.0.0.1:{porta}/index.html", wait_until="load")
    pagina.wait_for_selector(".reuniao-linha", timeout=5000)
    return pagina, erros


def titulos(pagina) -> list[str]:
    return pagina.eval_on_selector_all(
        ".reuniao-linha__titulo", "els => els.map((e) => e.textContent)")


def linha_por_titulo(pagina, inicio: str) -> bool:
    return pagina.evaluate(LINHA_POR_TITULO, inicio)


def buscar(pagina, texto: str) -> None:
    """Digita letra por letra, como gente — é o que pega o cursor saindo do campo."""
    pagina.fill("#busca-reunioes", "")
    pagina.locator("#busca-reunioes").press_sequentially(texto)
    pagina.wait_for_timeout(50)


# ─────────────────────────────────────────────────────────────── as provas

SEM_ROLAGEM_LATERAL = """() => {
  const c = document.querySelector('.conteudo');
  return c.scrollWidth <= c.clientWidth && document.documentElement.scrollWidth <= innerWidth;
}"""


def prova_grupos(pagina) -> None:
    conferir(pagina.evaluate(SEM_ROLAGEM_LATERAL), "a lista cabe na largura da janela, sem rolagem lateral")
    dias = pagina.eval_on_selector_all(".reunioes__dia", "els => els.map((e) => e.textContent)")
    conferir(dias[0].startswith("Hoje · "), f"o primeiro grupo é o de hoje ({dias[0]!r})")
    conferir(dias[-1] == "Sem data", f"o último grupo é 'Sem data' ({dias[-1]!r})")
    conferir(titulos(pagina)[-1] == "Reunião importada",
             "a gravação sem data vem no fim, embora o núcleo a mande primeiro")
    conferir(len(titulos(pagina)) == 9, "as nove gravações estão na lista")


def prova_busca(pagina) -> None:
    buscar(pagina, "credito")
    conferir(titulos(pagina) == ["Agente de Crédito — kickoff"], "'credito' acha 'Crédito'")
    ativo = pagina.evaluate("() => document.activeElement && document.activeElement.id")
    conferir(ativo == "busca-reunioes", f"o cursor ficou no campo enquanto se digitava ({ativo!r})")
    valor = pagina.input_value("#busca-reunioes")
    conferir(valor == "credito", f"as letras todas chegaram ao campo ({valor!r})")
    buscar(pagina, "algar rafael")
    conferir(titulos(pagina) == ["Comunicação Beegol + App"],
             "'algar rafael' casa cliente e convidado ao mesmo tempo")
    sub = pagina.text_content("#subtitulo")
    conferir(sub == "1 de 9 gravações", f"o subtítulo diz quantas sobraram ({sub!r})")


def prova_filtros(pagina) -> None:
    pagina.select_option("#filtro-cliente", "Algar")
    metas = pagina.eval_on_selector_all(".reuniao-linha__meta", "els => els.map((e) => e.textContent)")
    conferir(len(metas) == 2 and all(m.startswith("Algar") for m in metas),
             f"o filtro de cliente deixa só a Algar ({metas})")
    pagina.select_option("#filtro-cliente", "")
    pagina.select_option("#filtro-estado", "com-pendencias")
    conferir(titulos(pagina) == ["Comunicação Beegol + App"], "'Com pendências' deixa só a que tem item aberto")
    pagina.select_option("#filtro-estado", "nao-transcrita")
    conferir(titulos(pagina) == ["Semanal — Beegol · Uberlândia"], "'Não transcritas' deixa só a não transcrita")
    pagina.select_option("#filtro-estado", "")


def abrir_data(pagina) -> None:
    pagina.click("#filtro-data")
    pagina.wait_for_selector(".popover .calendario", timeout=2000)


def dia_no_calendario(pagina, dia: date) -> None:
    """Leva o calendário aberto até o mês do dia, e clica nele."""
    alvo = f".popover .calendario__dia[data-dia='{dia.isoformat()}']"
    for _ in range(3):
        if pagina.locator(alvo).count():
            break
        pagina.click(".popover .calendario__anterior")
    pagina.click(alvo)
    pagina.wait_for_timeout(50)


# O acervo da ponte falsa, em dias antes de hoje — para contar o que cada
# período deve deixar, sem copiar a regra que se está provando.
DIAS_DO_ACERVO = [0, 0, 0, 6, 6, 7, 7, 45]


def prova_filtro_de_data(pagina) -> None:
    rotulo = lambda: pagina.text_content("#filtro-data").strip()  # noqa: E731
    conferir(rotulo() == "Qualquer data", f"sem filtro, o botão diz 'Qualquer data' ({rotulo()!r})")
    conferir(not pagina.is_visible("#filtro-data-limpar"), "e não oferece tirar o filtro")

    abrir_data(pagina)
    conferir(pagina.get_attribute("#filtro-data", "aria-expanded") == "true", "o botão diz que abriu")
    ha_seis_dias = date.today() - timedelta(days=6)
    dia_no_calendario(pagina, ha_seis_dias)
    conferir(titulos(pagina) == ["Comunicação Beegol + App", "Pedido Sugerido — alinhamento"],
             f"um dia escolhido no calendário deixa só as reuniões dele ({titulos(pagina)})")
    conferir(not pagina.is_visible(".popover"), "e o calendário fecha ao escolher")
    ativo = pagina.evaluate("() => document.activeElement.id")
    conferir(ativo == "filtro-data", f"com o foco de volta no botão ({ativo!r})")
    conferir(rotulo().startswith(f"{ha_seis_dias.day} de "), f"que diz o dia escolhido ({rotulo()!r})")
    conferir(pagina.is_visible("#filtro-data-limpar"), "e oferece tirar o filtro")
    pagina.click("#filtro-data-limpar")
    pagina.wait_for_timeout(50)
    conferir(len(titulos(pagina)) == 9 and rotulo() == "Qualquer data", "o ✕ tira o filtro de data")

    abrir_data(pagina)
    pagina.click(".popover .atalho >> text=Hoje")
    pagina.wait_for_timeout(50)
    conferir(len(titulos(pagina)) == DIAS_DO_ACERVO.count(0) and rotulo() == "Hoje",
             f"o atalho 'Hoje' deixa só as de hoje ({titulos(pagina)})")

    abrir_data(pagina)
    pagina.click(".popover >> text=uma semana")
    dia_no_calendario(pagina, ha_seis_dias)
    segunda = ha_seis_dias - timedelta(days=ha_seis_dias.weekday())
    na_semana = sum(1 for d in DIAS_DO_ACERVO
                    if segunda <= date.today() - timedelta(days=d) <= segunda + timedelta(days=6))
    conferir(len(titulos(pagina)) == na_semana,
             f"com 'uma semana', o dia escolhido filtra a semana dele, de segunda a domingo "
             f"({len(titulos(pagina))} de {na_semana})")
    conferir(rotulo().startswith("Semana de "), f"e o botão diz a semana ({rotulo()!r})")


def prova_calendario(pagina) -> None:
    hoje = date.today()
    abrir_data(pagina)
    dia = lambda d: f".popover .calendario__dia[data-dia='{d.isoformat()}']"  # noqa: E731
    conferir(pagina.get_attribute(dia(hoje), "aria-current") == "date", "hoje vem marcado")
    conferir("com gravação" in (pagina.get_attribute(dia(hoje), "aria-label") or ""),
             "e o dia com gravação diz isso, além do ponto")
    ontem = hoje - timedelta(days=1)
    if pagina.locator(dia(ontem)).count():
        conferir("com gravação" not in (pagina.get_attribute(dia(ontem), "aria-label") or ""),
                 "o dia sem gravação não diz que tem")
    focado = lambda: pagina.evaluate("() => document.activeElement.dataset.dia || document.activeElement.className")  # noqa: E731
    conferir(focado() == hoje.isoformat(), f"abrir leva o foco ao dia de hoje ({focado()!r})")
    pagina.keyboard.press("ArrowLeft")
    pagina.keyboard.press("ArrowUp")
    esperado = hoje - timedelta(days=8)
    conferir(focado() == esperado.isoformat(), f"as setas andam um dia e uma semana ({focado()!r})")
    pagina.keyboard.press("PageUp")
    ano, mes = (esperado.year, esperado.month - 1) if esperado.month > 1 else (esperado.year - 1, 12)
    import calendar
    esperado = date(ano, mes, min(esperado.day, calendar.monthrange(ano, mes)[1]))
    conferir(focado() == esperado.isoformat(), f"PageUp volta um mês, no mesmo dia ({focado()!r})")
    pagina.keyboard.press("Enter")
    pagina.wait_for_timeout(50)
    conferir(not pagina.is_visible(".popover") and pagina.text_content("#filtro-data").strip()
             .startswith(f"{esperado.day} de "), "Enter escolhe o dia")

    abrir_data(pagina)
    pagina.keyboard.press("Escape")
    conferir(not pagina.is_visible(".popover"), "Esc fecha o calendário")
    conferir(pagina.evaluate("() => document.activeElement.id") == "filtro-data", "e devolve o foco ao botão")
    abrir_data(pagina)
    pagina.mouse.click(5, 790)
    conferir(not pagina.is_visible(".popover"), "clicar fora fecha")
    abrir_data(pagina)
    pagina.keyboard.press("Tab")
    conferir(not pagina.is_visible(".popover"), "e sair dele com Tab também")



def prova_sem_resultado(pagina) -> None:
    buscar(pagina, "zzzz")
    conferir(pagina.is_visible(".reunioes__vazio"), "sem resultado, a tela diz isso")
    pagina.click(".reunioes__vazio button")
    pagina.wait_for_timeout(50)
    conferir(len(titulos(pagina)) == 9, "'Limpar filtros' devolve as nove")
    conferir(pagina.input_value("#busca-reunioes") == "", "e esvazia o campo de busca")


def prova_criterios_sobrevivem(pagina) -> None:
    pagina.select_option("#filtro-cliente", "Vivo")
    pagina.click(".reuniao-linha")
    pagina.click('.reunioes__painel >> text="Abrir"')
    pagina.wait_for_selector(".revisao", timeout=5000)
    pagina.click("#voltar")
    pagina.wait_for_selector(".reuniao-linha", timeout=5000)
    conferir(pagina.input_value("#filtro-cliente") == "Vivo", "o filtro voltou como estava")
    conferir(titulos(pagina) == ["Sherlock Diário — Status e Ações"], "e a lista voltou filtrada")
    conferir(pagina.get_attribute(".reuniao-linha", "aria-pressed") == "true",
             "e a mesma reunião continua escolhida")



def prova_painel(pagina) -> None:
    # A primeira da TELA: o núcleo manda a "Reunião importada" na frente, e ela
    # é desenhada no fim, em "Sem data".
    conferir(pagina.get_attribute(".reuniao-linha", "aria-pressed") == "true",
             "sem escolha anterior, a escolhida é a primeira linha da tela")
    conferir(pagina.text_content(".reunioes__painel-titulo") == "Reunião de lideranças",
             "e o painel fala dela")
    linha_por_titulo(pagina, "Sherlock")
    pagina.evaluate("() => window.__linha.click()")
    pagina.wait_for_timeout(50)
    conferir(pagina.evaluate("() => window.__linha.getAttribute('aria-pressed')") == "true",
             "o clique escolhe a linha, e não abre a reunião")
    conferir(pagina.text_content(".reunioes__painel-titulo") == "Sherlock Diário — Status e Ações",
             "o painel mostra a escolhida")
    for inicio, acao in [("Sherlock", "gerar-ata"), ("Semanal", "transcrever"),
                         ("Pedido Sugerido", "refazer-ata"), ("Comunicação", "abrir-ata")]:
        linha_por_titulo(pagina, inicio)
        pagina.evaluate("() => window.__linha.click()")
        pagina.wait_for_timeout(30)
        achada = pagina.get_attribute(".reunioes__painel [data-acao]", "data-acao")
        conferir(achada == acao, f"'{inicio}' oferece {acao} ({achada})")
    pendencias = pagina.eval_on_selector_all(".reunioes__pendencias li", "els => els.length")
    conferir(pendencias == 3, f"o painel mostra as três primeiras pendências ({pendencias})")
    linha_por_titulo(pagina, "Reunião de lideranças")
    pagina.evaluate("() => window.__linha.dispatchEvent(new MouseEvent('dblclick', { bubbles: true }))")
    pagina.wait_for_selector(".revisao", timeout=5000)
    conferir(True, "o duplo clique abre a reunião")


def prova_transcricao_em_curso(pagina) -> None:
    linha_por_titulo(pagina, "Semanal")
    pagina.evaluate("() => window.__linha.click()")
    pagina.wait_for_timeout(30)
    pagina.evaluate("() => { window.__botao = document.querySelector('.reunioes__painel [data-acao]'); "
                    "window.__botao.focus(); }")
    caminho = pagina.evaluate("() => window.__linha.dataset.gravacao")
    em_curso = ("(c, f) => window.__emitir({ tipo: 'transcricoes', transcricoes: { atual: "
                "{ gravacao: c, nome: 'x', tarefa: 'transcricao', etapa: 'asr', fracao: f, texto: '', "
                "comecou_em: '', terminou: false, erro: null, cancelada: false }, ultimo: null } })")
    pagina.evaluate(f"([c, f]) => ({em_curso})(c, f)", [caminho, 0.2])
    pagina.wait_for_timeout(30)
    conferir(pagina.evaluate("() => window.__linha.isConnected"), "a lista não foi redesenhada")
    conferir(pagina.evaluate("() => window.__linha.lastElementChild.textContent") == "Transcrevendo…",
             "a etiqueta passou a 'Transcrevendo…'")
    conferir(pagina.get_attribute(".reunioes__painel [data-acao]", "data-acao") == "acompanhar-transcricao",
             "o painel passou a oferecer acompanhar")
    pagina.evaluate("() => { window.__botao = document.querySelector('.reunioes__painel [data-acao]'); "
                    "window.__botao.focus(); }")
    pagina.evaluate(f"([c, f]) => ({em_curso})(c, f)", [caminho, 0.6])
    pagina.wait_for_timeout(30)
    conferir(pagina.evaluate("() => window.__botao.isConnected && document.activeElement === window.__botao"),
             "andamento sem mudança de estado não recria o botão, e o foco fica nele")
    # O núcleo escreveu o transcricao.json: a ponte falsa passa a dizer isso,
    # como o Listar diria.
    pagina.evaluate("(c) => { window.__gravacoes.find((g) => g.caminho === c).transcrita = true; }", caminho)
    pagina.evaluate("(c) => window.__emitir({ tipo: 'transcricoes', transcricoes: { atual: null, ultimo: "
                    "{ gravacao: c, nome: 'x', tarefa: 'transcricao', etapa: 'montagem', fracao: 1, "
                    "texto: '', comecou_em: '', terminou: true, erro: null, cancelada: false } } })", caminho)
    pagina.wait_for_timeout(30)
    conferir(pagina.evaluate("() => window.__linha.lastElementChild.textContent") == "Sem ata",
             "terminada, a etiqueta diz 'Sem ata' sozinha")
    conferir(pagina.get_attribute(".reunioes__painel [data-acao]", "data-acao") == "gerar-ata",
             "e o painel passa a oferecer gerar a ata")


def prova_janela_estreita(pagina) -> None:
    conferir(pagina.evaluate(SEM_ROLAGEM_LATERAL), "a 860 px a lista cabe, sem rolagem lateral")
    conferir(not pagina.is_visible(".reunioes__painel"), "em janela estreita o painel some")
    linha_por_titulo(pagina, "Reunião de lideranças")
    pagina.evaluate("() => window.__linha.click()")
    pagina.wait_for_selector(".revisao", timeout=5000)
    conferir(True, "e o clique abre a reunião direto")


prova_janela_estreita.janela = (860, 700)



def abrir_reuniao(pagina, inicio: str) -> None:
    """Abre a reunião cuja linha começa por `inicio`, pelo duplo clique da lista."""
    linha_por_titulo(pagina, inicio)
    pagina.evaluate("() => window.__linha.dispatchEvent(new MouseEvent('dblclick', { bubbles: true }))")
    pagina.wait_for_selector(".reuniao-aberta [role='tab']", timeout=5000)


ABA_ESCOLHIDA = """() => {
  const a = document.querySelector(".reuniao-aberta [role='tab'][aria-selected='true']");
  return a ? a.dataset.aba : null;
}"""

# Um evento de fim de tarefa, como o núcleo empurra.
FIM_DE_TAREFA = ("([c, t]) => window.__emitir({ tipo: 'transcricoes', transcricoes: { atual: null, ultimo: "
                 "{ gravacao: c, nome: 'x', tarefa: t, etapa: 'montagem', fracao: 1, texto: '', "
                 "comecou_em: '', terminou: true, erro: null, cancelada: false } } })")
TAREFA_EM_CURSO = ("([c, t, e]) => window.__emitir({ tipo: 'transcricoes', transcricoes: { atual: "
                   "{ gravacao: c, nome: 'x', tarefa: t, etapa: e, fracao: 0.5, texto: 'lendo', "
                   "comecou_em: '', terminou: false, erro: null, cancelada: false }, ultimo: null } })")


def prova_ata_na_reuniao_pedida(pagina) -> None:
    linha_por_titulo(pagina, "Comunicação")
    pagina.evaluate("() => window.__linha.click()")
    pagina.click(".reunioes__painel [data-acao='abrir-ata']")
    pagina.wait_for_selector(".reuniao-aberta [role='tab']", timeout=5000)
    conferir(pagina.evaluate(ABA_ESCOLHIDA) == "ata", "'Abrir a ata' abre a reunião na aba Ata")
    foco = pagina.evaluate("() => document.activeElement && document.activeElement.dataset.aba")
    conferir(foco == "ata", f"com o foco na aba, e não num botão que um Enter dispararia ({foco!r})")
    conferir(pagina.evaluate(FOCO_A_VISTA), "e a aba com o foco está à vista, e não debaixo da barra do topo")
    pagina.wait_for_selector("#painel-ata .ata__texto", timeout=5000)
    conferir("tom passa a ser separado" in pagina.text_content("#painel-ata .ata__texto"),
             "e a ata daquela reunião está ali, aberta")


def prova_gerar_ata_na_reuniao_pedida(pagina) -> None:
    linha_por_titulo(pagina, "Sherlock")
    pagina.evaluate("() => window.__linha.click()")
    pagina.click(".reunioes__painel [data-acao='gerar-ata']")
    pagina.wait_for_selector("#painel-ata [data-acao='ata']", timeout=5000)
    conferir(pagina.evaluate(ABA_ESCOLHIDA) == "ata", "'Gerar a ata' abre a reunião na aba Ata")
    conferir(pagina.text_content("#painel-ata [data-acao='ata']") == "Gerar ata",
             "que oferece gerar, porque ainda não há ata")


def prova_reuniao_abas(pagina) -> None:
    abrir_reuniao(pagina, "Comunicação")
    abas = pagina.eval_on_selector_all(".reuniao-aberta [role='tab']", "els => els.map((e) => e.dataset.aba)")
    conferir(abas == ["transcricao", "ata", "notas"], f"a reunião tem as três abas ({abas})")
    conferir(pagina.evaluate(ABA_ESCOLHIDA) == "transcricao", "e abre na Transcrição")
    conferir(pagina.is_visible(".revisao"), "que é a revisão de sempre")
    em_cima = pagina.evaluate("""() => {
        const abas = document.querySelector("[role='tablist']").getBoundingClientRect();
        const painel = document.querySelector("#painel-transcricao").getBoundingClientRect();
        return abas.bottom <= painel.top + 1 && painel.width >= abas.width - 1;
    }""")
    conferir(em_cima, "as abas ficam em cima do conteúdo, e o conteúdo tem a largura delas")
    conferir(pagina.text_content("#titulo") == "Comunicação Beegol + App", "a barra do topo diz a reunião")
    # A revisão não se remonta ao trocar de aba: ela guarda na memória nomes de
    # falante e edições que gravam com atraso.
    pagina.fill(".revisao input[type='search']", "começar")
    pagina.evaluate("() => { window.__revisao = document.querySelector('.revisao'); }")
    pagina.click(".reuniao-aberta [data-aba='notas']")
    conferir(pagina.evaluate(ABA_ESCOLHIDA) == "notas", "clicar em Notas troca de aba")
    conferir(pagina.is_visible("#painel-notas .notas"), "e mostra o bloco de notas")
    conferir(not pagina.is_visible(".revisao"), "a revisão fica escondida")
    pagina.click(".reuniao-aberta [data-aba='transcricao']")
    conferir(pagina.evaluate("() => window.__revisao === document.querySelector('.revisao') "
                             "&& window.__revisao.isConnected"),
             "voltar à Transcrição traz a MESMA revisão, sem remontar")
    conferir(pagina.input_value(".revisao input[type='search']") == "começar", "com a busca como estava")
    notas_na_revisao = pagina.eval_on_selector_all(
        ".revisao .ferramentas button", "els => els.filter((e) => e.textContent.trim() === 'Notas').length")
    conferir(notas_na_revisao == 0, "a revisão não tem mais o botão Notas: as notas são uma aba")


def prova_reuniao_teclado(pagina) -> None:
    abrir_reuniao(pagina, "Comunicação")
    pagina.focus(".reuniao-aberta [data-aba='transcricao']")
    pagina.keyboard.press("ArrowRight")
    conferir(pagina.evaluate(ABA_ESCOLHIDA) == "ata", "a seta para a direita vai à próxima aba")
    conferir(pagina.evaluate("() => document.activeElement.dataset.aba") == "ata", "e leva o foco junto")
    pagina.keyboard.press("End")
    conferir(pagina.evaluate(ABA_ESCOLHIDA) == "notas", "End vai à última")
    pagina.keyboard.press("ArrowRight")
    conferir(pagina.evaluate(ABA_ESCOLHIDA) == "transcricao", "e a seta dá a volta")
    paradas = pagina.eval_on_selector_all(".reuniao-aberta [role='tab']",
                                          "els => els.filter((e) => e.tabIndex === 0).length")
    conferir(paradas == 1, f"as abas são uma parada de Tab só ({paradas})")


def prova_reuniao_cabecalho(pagina) -> None:
    abrir_reuniao(pagina, "Comunicação")
    na_barra = pagina.eval_on_selector_all("#acoes-da-barra [data-acao]", "els => els.map((e) => e.dataset.acao)")
    conferir(na_barra == ["falantes", "exportar"], f"Falantes e Exportar moram na barra do topo ({na_barra})")
    nas_ferramentas = pagina.eval_on_selector_all(
        ".revisao .ferramentas button",
        "els => els.filter((e) => ['Falantes', 'Exportar', '⏸'].includes(e.textContent.trim())).length")
    conferir(nas_ferramentas == 0, "e saíram das ferramentas da revisão")
    pagina.click("#acoes-da-barra [data-acao='exportar']")
    # abrirExportacao é assíncrona (espera "clientes" na ponte) antes de abrir a
    # gaveta — sem esperar, o clique do Playwright volta antes da gaveta abrir,
    # e a prova falha por sorteio.
    pagina.wait_for_selector("#gaveta-exportar:not([hidden])", timeout=5000)
    conferir(pagina.is_visible("#gaveta-exportar"), "Exportar abre a gaveta de exportar")


def prova_reuniao_falantes_da_ata(pagina) -> None:
    # Abre outra reunião antes, para o estado da revisão ser o de outra reunião.
    pagina.evaluate("() => { window.__transcricao = JSON.stringify({ language: 'pt', segments: ["
                    "{ start: 0, end: 3, text: ' Outra.', speaker: 'Zé da Outra' }] }); }")
    abrir_reuniao(pagina, "Sherlock")
    pagina.click("#voltar")
    pagina.wait_for_selector(".reuniao-linha", timeout=5000)
    pagina.evaluate("() => { window.__transcricao = null; }")
    linha_por_titulo(pagina, "Comunicação")
    pagina.evaluate("() => window.__linha.click()")
    pagina.click(".reunioes__painel [data-acao='abrir-ata']")
    pagina.wait_for_selector("#painel-ata .ata__texto", timeout=5000)
    pagina.click("#acoes-da-barra [data-acao='falantes']")
    pagina.wait_for_selector("#gaveta-falantes .tabela-falantes", timeout=5000)
    quem = pagina.eval_on_selector_all("#gaveta-falantes .tabela-falantes tr td:first-child",
                                       "els => els.map((e) => e.textContent)")
    conferir(quem == ["André", "Carol"], f"aberta na Ata, Falantes mostra os falantes DESTA reunião ({quem})")


def prova_renomear_e_trocar_de_reuniao(pagina) -> None:
    abrir_reuniao(pagina, "Comunicação")
    caminho = pagina.evaluate("() => window.__gravacoes.find((g) => g.titulo?.startsWith('Comunicação')).caminho")
    pagina.click("#acoes-da-barra [data-acao='falantes']")
    pagina.wait_for_selector("#gaveta-falantes .tabela-falantes input", timeout=5000)
    entrada = pagina.locator("#gaveta-falantes .tabela-falantes tr:nth-child(3) input")
    entrada.fill("Carolina")
    entrada.dispatch_event("change")
    # Menos de 800 ms depois, outra reunião. A gaveta é modal de verdade (o
    # fundo fica inert enquanto ela está aberta, pecas.js abrirGaveta), então
    # sair para a lista primeiro fecha a gaveta — Escape, como quem desiste do
    # painel — e só então clica em Voltar.
    pagina.keyboard.press("Escape")
    pagina.click("#voltar")
    pagina.wait_for_selector(".reuniao-linha", timeout=5000)
    abrir_reuniao(pagina, "Sherlock")
    pagina.wait_for_timeout(1200)
    salvos = pagina.evaluate("() => window.__pedidos.filter((q) => q.op === 'salvar-transcricao')"
                             ".map((q) => ({ g: q.gravacao, carolina: q.conteudo.includes('Carolina') }))")
    conferir(salvos == [{"g": caminho, "carolina": True}],
             f"o nome vai para a reunião em que foi dado, e só para ela ({salvos})")


def prova_barra_esvazia_no_preparo(pagina) -> None:
    # Transcrever de novo leva ao preparo com o MESMO título: a troca de título
    # não esvazia a barra, e Falantes ficaria lá, abrindo a gaveta de nada.
    pagina.evaluate("() => { window.__config = { permitir_retranscrever: true }; }")
    abrir_reuniao(pagina, "Comunicação")
    pagina.click(".ferramentas__perigo >> text=Transcrever de novo")
    pagina.click(".aa-btn-primario:has-text('Transcrever de novo')")
    pagina.wait_for_selector("#vocabulario", timeout=5000)
    sobra = pagina.eval_on_selector_all("#acoes-da-barra > *", "els => els.length")
    conferir(sobra == 0, f"no preparo, a barra do topo não tem o que era da reunião ({sobra})")


def abrir_preparo(pagina) -> None:
    linha_por_titulo(pagina, "Semanal")
    pagina.evaluate("() => window.__linha.dispatchEvent(new MouseEvent('dblclick', { bubbles: true }))")
    pagina.wait_for_selector("#vocabulario", timeout=5000)


TERMOS = "() => [...document.querySelectorAll('#vocabulario .etiquetas__termo')].map((e) => e.firstChild.textContent)"


def prova_preparo_vocabulario(pagina) -> None:
    pagina.evaluate("""() => {
        window.__vinculo = { cliente: 'Vivo', projeto: 'Sherlock' };
        window.__prefs = { language: 'pt', model_size: 'large-v3', initial_prompt: 'Beegol, NOC\\nnoc; CCIL' };
    }""")
    abrir_preparo(pagina)
    pagina.wait_for_timeout(100)
    conferir(pagina.evaluate(TERMOS) == ["Beegol", "NOC", "CCIL"],
             f"o vocabulário do projeto vira etiquetas, sem repetir ({pagina.evaluate(TERMOS)})")
    pagina.click("#vocabulario-novo")
    pagina.keyboard.type("Sherlock")
    pagina.keyboard.press("Enter")
    conferir(pagina.evaluate(TERMOS)[-1] == "Sherlock", "Enter põe o termo")
    conferir(pagina.evaluate("() => document.activeElement.id") == "vocabulario-novo", "e o cursor fica no campo")
    pagina.evaluate("""() => { const c = document.getElementById('vocabulario-novo');
        const d = new DataTransfer(); d.setData('text/plain', 'Algar, Mobi, NOC');
        c.dispatchEvent(new ClipboardEvent('paste', { clipboardData: d, bubbles: true, cancelable: true })); }""")
    conferir(pagina.evaluate(TERMOS) == ["Beegol", "NOC", "CCIL", "Sherlock", "Algar", "Mobi"],
             f"colar uma lista põe cada termo, e o repetido não entra ({pagina.evaluate(TERMOS)})")
    pagina.click("#vocabulario .etiquetas__tirar[aria-label='Tirar CCIL']")
    conferir("CCIL" not in pagina.evaluate(TERMOS), "o × tira a etiqueta")
    pagina.focus("#vocabulario-novo")
    pagina.keyboard.press("Backspace")
    conferir(pagina.evaluate(TERMOS)[-1] == "Algar", "Backspace no campo vazio tira a última")
    pagina.keyboard.type("Pendente")
    pagina.focus("#cliente")
    conferir(pagina.evaluate(TERMOS)[-1] == "Pendente", "sair do campo com texto digitado não perde o termo")
    pagina.wait_for_timeout(50)
    salvo = pagina.evaluate("() => window.__pedidos.filter((q) => q.op === 'salvar-projeto').pop()")
    conferir(salvo and salvo["prefs"]["initial_prompt"] == "Beegol, NOC, Sherlock, Algar, Pendente",
             f"e o projeto guarda a lista no formato de sempre ({salvo and salvo['prefs']['initial_prompt']!r})")


def prova_reuniao_ata(pagina) -> None:
    abrir_reuniao(pagina, "Comunicação")
    largura = "() => Math.round(document.querySelector('.reuniao-aberta').getBoundingClientRect().width)"
    na_transcricao = pagina.evaluate(largura)
    pagina.click(".reuniao-aberta [data-aba='ata']")
    pagina.wait_for_selector("#painel-ata .ata__texto", timeout=5000)
    na_ata = pagina.evaluate(largura)
    conferir(na_ata == na_transcricao,
             f"a página não muda de largura ao trocar de aba ({na_transcricao} → {na_ata} px)")
    conferir("tom passa a ser separado" in pagina.text_content("#painel-ata .ata__texto"),
             "a aba Ata mostra a ata da reunião")
    conferir(pagina.locator("#painel-ata details").count() == 0, "aberta, sem dobra: a aba é a ata")
    conferir(pagina.locator("#painel-ata .ata__tipo select").count() == 1, "com o tipo, para refazer")
    botao = pagina.text_content("#painel-ata [data-acao='ata']")
    conferir(botao == "Refazer ata", f"e o botão diz refazer, porque a ata existe ({botao!r})")
    conferir(pagina.locator("#painel-ata >> text=Copiar").count() == 1, "dá para copiar")
    conferir(pagina.locator("#painel-ata >> text=Exportar").count() == 1, "e para exportar")


def prova_reuniao_gerar_ata(pagina) -> None:
    # Gerar na aba, sair dela no meio, e a ata chegar mesmo assim.
    abrir_reuniao(pagina, "Sherlock")
    pagina.click(".reuniao-aberta [data-aba='ata']")
    pagina.wait_for_selector("#painel-ata [data-acao='ata']", timeout=5000)
    caminho = pagina.evaluate("() => document.querySelector('#painel-ata .ata').dataset.gravacao")
    pagina.click("#painel-ata [data-acao='ata']")
    pagina.evaluate(TAREFA_EM_CURSO, [caminho, "ata", "lendo"])
    pagina.wait_for_timeout(50)
    conferir(pagina.is_visible("#painel-ata .aa-progresso"), "gerar mostra o andamento na aba")
    pagina.click(".reuniao-aberta [data-aba='notas']")
    pagina.evaluate("(c) => { window.__atas[c] = '# Ata\\n\\n## Resumo\\n\\nO NOC assume o sábado.\\n\\n"
                    "## Pendências\\n\\n- [ ] Escalar o sábado — **Dimi** — sexta\\n'; }", caminho)
    pagina.evaluate(FIM_DE_TAREFA, [caminho, "ata"])
    pagina.wait_for_timeout(80)
    pagina.click(".reuniao-aberta [data-aba='ata']")
    pagina.wait_for_selector("#painel-ata .ata__texto", timeout=5000)
    conferir("NOC assume" in pagina.text_content("#painel-ata .ata__texto"),
             "a ata que terminou com a aba escondida está lá quando se volta")
    conferir(pagina.text_content("#painel-ata [data-acao='ata']") == "Refazer ata", "e o botão vira refazer")
    # O selo da aba vem do resumo da lista, lido antes de a ata existir: ele
    # tem de acompanhar a ata escrita aqui, e não o disco de quando se abriu.
    selo = lambda: pagina.locator("#aba-ata .reuniao-aberta__conta")  # noqa: E731
    conferir(selo().count() == 1 and selo().text_content() == "1 pendência",
             f"a aba Ata passa a contar a pendência da ata nova ({selo().all_text_contents()})")
    pagina.click("#painel-ata [data-acao='ata']")
    # O clique é assíncrono: o fim só é ouvido depois que o pedido volta e o
    # andamento aparece.
    pagina.wait_for_selector("#painel-ata .aa-progresso", timeout=5000)
    pagina.evaluate("(c) => { window.__atas[c] = '# Ata\\n\\n## Resumo\\n\\nNada ficou pendente.\\n'; }",
                    caminho)
    pagina.evaluate(FIM_DE_TAREFA, [caminho, "ata"])
    pagina.wait_for_function("() => document.querySelector('#painel-ata .ata__texto')"
                             "?.textContent.includes('Nada ficou')", timeout=5000)
    conferir(selo().count() == 0, "e refeita sem pendência, o selo some")


# Uma reunião de verdade tem centenas de trechos: é nela que se perde o lugar.
TRANSCRICAO_LONGA = """() => {
  window.__transcricao = JSON.stringify({ language: "pt", segments: Array.from({ length: 400 }, (_, i) => ({
    start: i * 4, end: i * 4 + 3, text: ` Trecho ${i} da reunião longa.`, speaker: i % 2 ? "Carol" : "André" })) });
}"""

A_VISTA = """(sel) => {
  const e = document.querySelector(sel);
  if (!e) return false;
  const r = e.getBoundingClientRect();
  if (r.bottom <= 0 || r.top >= innerHeight) return false;
  const alvo = document.elementFromPoint(r.left + r.width / 2, r.top + Math.min(r.height / 2, 10));
  return alvo === e || e.contains(alvo);
}"""


def prova_reuniao_rolagem(pagina) -> None:
    # A gaveta de notas existia para "mexer nelas sem perder o lugar no texto";
    # a aba tem de manter a promessa: as abas ficam à vista rolando, e voltar à
    # Transcrição devolve o trecho em que se estava.
    pagina.evaluate(TRANSCRICAO_LONGA)
    abrir_reuniao(pagina, "Comunicação")
    pagina.wait_for_function("() => document.querySelectorAll('.revisao .trecho, .revisao [data-indice]').length > 60",
                             timeout=5000)
    rolagem = "() => Math.round(document.querySelector('.conteudo').scrollTop)"
    pagina.evaluate("() => { document.querySelector('.conteudo').scrollTop = 3000; }")
    pagina.wait_for_timeout(150)
    antes = pagina.evaluate(rolagem)
    conferir(antes > 2000, f"a transcrição longa rola ({antes} px)")
    conferir(pagina.evaluate(A_VISTA, "#aba-notas"), "rolada a transcrição, as abas continuam à vista")
    conferir(pagina.evaluate(A_VISTA, ".revisao input[type='search']"),
             "e a busca da revisão também, sem ficar debaixo delas")
    # Encostados, e não um atrás do outro: a barra do topo muda de altura com o
    # que mostra, e um sticky que gruda na altura errada desliza para trás dela.
    encaixe = pagina.evaluate("""() => {
        const b = (s) => document.querySelector(s).getBoundingClientRect();
        return [b('.barra').bottom, b('.reuniao-aberta__abas').top,
                b('.reuniao-aberta__abas').bottom, b('.revisao .controles').top];
    }""")
    # Meio pixel: uma fresta de 1 px já deixa o texto rolado aparecer entre os dois.
    conferir(abs(encaixe[0] - encaixe[1]) < 0.5 and abs(encaixe[2] - encaixe[3]) < 0.5,
             f"as abas encostam na barra do topo, e a barra da revisão nas abas ({encaixe})")
    pagina.click("#aba-notas")
    pagina.click("#aba-transcricao")
    pagina.wait_for_timeout(50)
    depois = pagina.evaluate(rolagem)
    conferir(abs(depois - antes) <= 1, f"voltar à Transcrição devolve o lugar no texto ({antes} → {depois} px)")
    pagina.click("#aba-ata")
    pagina.wait_for_selector("#painel-ata .ata__texto", timeout=5000)
    pagina.click("#aba-transcricao")
    pagina.wait_for_timeout(50)
    conferir(abs(pagina.evaluate(rolagem) - antes) <= 1, "e passar pela Ata também")


TOCADOR = """() => ({
  rotulo: document.querySelector('.tocador [data-acao="tocar"]').getAttribute('aria-label'),
  tempo: document.querySelector('.tocador__tempo').textContent,
  posicao: document.querySelector('.tocador__posicao').value,
})"""


def prova_tocador(pagina) -> None:
    abrir_reuniao(pagina, "Comunicação")
    conferir(pagina.is_visible(".reuniao-aberta .tocador"), "a reunião tem o tocador")
    conferir(pagina.evaluate(TOCADOR) == {"rotulo": "Tocar", "tempo": "00:00 / 30:00", "posicao": "0"},
             f"parado, no zero, com a duração da reunião ({pagina.evaluate(TOCADOR)})")
    # A reunião "Comunicação" tem poucos trechos e não enche a janela por si só
    # — mesmo assim o tocador tem que encostar no pé, como na prancha
    # (ref/aberta.png): a coluna da reunião tem altura mínima até lá
    # (.reuniao-aberta) e o painel da aba cresce para empurrá-lo (conserto
    # rodada 1, revertendo a folga do Step 7 do brief).
    no_pe = pagina.evaluate("""() => {
        const t = document.querySelector('.tocador').getBoundingClientRect();
        return Math.abs(t.bottom - innerHeight) <= 1;
    }""")
    conferir(no_pe, "e ele fica no pé da janela")
    pagina.click("#corpo-transcricao .trecho >> nth=1")
    tocou = pagina.evaluate("() => window.__tocou")
    conferir(tocou and tocou["t"] == 4 and tocou["src"].endswith("/mix.wav"),
             f"clicar no trecho toca o mix de onde ele começa ({tocou})")
    conferir(pagina.evaluate(TOCADOR)["rotulo"] == "Pausar", "e o tocador passa a oferecer pausar")
    conferir(pagina.evaluate(TOCADOR)["tempo"] == "00:04 / 30:00", "mostrando onde está")
    pagina.click(".tocador [data-acao='tocar']")
    conferir(pagina.evaluate(TOCADOR)["rotulo"] == "Tocar", "pausar pelo tocador")
    pagina.evaluate("""() => { const r = document.querySelector('.tocador__posicao');
        r.value = '900'; r.dispatchEvent(new Event('input', { bubbles: true })); }""")
    conferir(pagina.evaluate("() => document.getElementById('audio').currentTime") == 900,
             "arrastar a posição leva o áudio para lá")
    conferir(pagina.evaluate(TOCADOR)["tempo"] == "15:00 / 30:00", "e o tempo acompanha")
    pagina.click(".reuniao-aberta [data-aba='notas']")
    conferir(pagina.is_visible(".reuniao-aberta .tocador"), "o tocador continua na aba Notas")

    # Numa reunião comprida a coluna passa da altura mínima e quem rola é o
    # .conteudo — o tocador precisa continuar grudado no pé, e não só na
    # primeira pintura: é o sticky que segura, não a altura mínima.
    pagina.evaluate(TRANSCRICAO_LONGA)
    pagina.click("#voltar")
    pagina.wait_for_selector(".reuniao-linha", timeout=5000)
    abrir_reuniao(pagina, "Comunicação")
    pagina.wait_for_function(
        "() => document.querySelectorAll('.revisao .trecho, .revisao [data-indice]').length > 60",
        timeout=5000)
    pagina.evaluate("() => { document.querySelector('.conteudo').scrollTop = 3000; }")
    pagina.wait_for_timeout(150)
    preso = pagina.evaluate("""() => {
        const t = document.querySelector('.tocador').getBoundingClientRect();
        return Math.abs(t.bottom - innerHeight) <= 1;
    }""")
    conferir(preso, "e continua grudado no pé com a transcrição comprida rolada")


def prova_tocador_troca_de_reuniao(pagina) -> None:
    abrir_reuniao(pagina, "Comunicação")
    pagina.click("#corpo-transcricao .trecho >> nth=1")
    pagina.click("#voltar")
    pagina.wait_for_selector(".reuniao-linha", timeout=5000)
    conferir(pagina.evaluate("() => !document.getElementById('audio').hasAttribute('src')"),
             "sair da reunião solta o áudio")
    abrir_reuniao(pagina, "Sherlock")
    conferir(pagina.evaluate(TOCADOR)["tempo"] == "00:00 / 30:00", "a reunião seguinte começa com o tocador zerado")
    # Um evento do <audio> depois da troca não pode repintar o tocador que saiu.
    pagina.evaluate("() => document.getElementById('audio').dispatchEvent(new Event('play'))")
    conferir(pagina.locator(".tocador").count() == 1, "há um tocador só na página")


def prova_ata_so_acompanha_a_ata(pagina) -> None:
    # A separação de falantes roda sobre a legenda de uma reunião que já abre em
    # abas. A aba Ata não pode tomá-la por uma ata sendo escrita: diria
    # "Escrevendo…", e o Parar dela cancelaria a separação.
    abrir_reuniao(pagina, "Comunicação")
    caminho = pagina.evaluate("() => window.__gravacoes.find((g) => g.caminho.includes('13-59')).caminho")
    pagina.evaluate(TAREFA_EM_CURSO, [caminho, "falantes", "diarizacao"])
    pagina.wait_for_timeout(30)
    pagina.click("#aba-ata")
    pagina.wait_for_selector("#painel-ata .ata__texto", timeout=5000)
    conferir(pagina.locator("#painel-ata .aa-progresso").count() == 0,
             "com a separação de falantes rodando, a aba Ata não mostra andamento")
    botao = pagina.text_content("#painel-ata [data-acao='ata']")
    conferir(botao == "Refazer ata", f"e o botão continua sendo o da ata ({botao!r})")


def prova_notas_tocam(pagina) -> None:
    pagina.evaluate("() => { window.__notas = 'abertura\\n[00:00:04] Carol começa\\nsem tempo'; }")
    abrir_reuniao(pagina, "Comunicação")
    pagina.click(".reuniao-aberta [data-aba='notas']")
    pagina.wait_for_selector("#painel-notas .notas__momento", timeout=5000)
    momentos = pagina.eval_on_selector_all("#painel-notas .notas__momento", "els => els.map((e) => e.textContent)")
    conferir(momentos == ["00:00:04 Carol começa"], f"cada linha com tempo vira um botão ({momentos})")
    pagina.click("#painel-notas .notas__momento")
    tocou = pagina.evaluate("() => window.__tocou")
    conferir(tocou and tocou["t"] == 4, f"e o botão toca dali ({tocou})")
    pagina.focus("#painel-notas .notas__texto")
    pagina.keyboard.press("Control+End")
    pagina.keyboard.type("\n[00:01:00] novo")
    conferir(pagina.eval_on_selector_all("#painel-notas .notas__momento", "els => els.length") == 2,
             "escrever uma marca nova acrescenta o botão")
    conferir(pagina.evaluate("() => document.activeElement.classList.contains('notas__texto')"),
             "sem tirar o cursor de quem escreve")


def prova_trilho_sem_atas(pagina) -> None:
    conferir(pagina.locator("#ir-atas").count() == 0, "Atas saiu do trilho: a ata mora na reunião")
    ordem = pagina.eval_on_selector_all(".trilho__item", "els => els.map((e) => e.id)")
    conferir(ordem == ["ir-gravador", "ir-reunioes", "ir-config"],
             f"o trilho é Gravador, Reuniões, Ajustes ({ordem})")
    caminho = pagina.evaluate("() => window.__gravacoes[4].caminho")
    pagina.evaluate(TAREFA_EM_CURSO, [caminho, "ata", "lendo"])
    pagina.wait_for_timeout(30)
    conferir(pagina.get_attribute("#ir-reunioes", "data-ocupado") == "true",
             "escrever a ata acende a bolinha de Reuniões")
    rotulo = pagina.get_attribute("#ir-reunioes", "aria-label") or ""
    conferir("escrevendo a ata" in rotulo, f"e a bolinha diz que é a ata, e não a transcrição ({rotulo!r})")


def prova_endereco_de_atas(pagina) -> None:
    # O --tela atas das fotos antigas não pode cair numa tela em branco.
    pagina.evaluate("() => { location.hash = 'atas'; location.reload(); }")
    pagina.wait_for_selector(".reuniao-linha", timeout=5000)
    conferir(True, "o endereço antigo de Atas cai na lista de Reuniões")


def prova_reuniao_pelo_endereco(pagina) -> None:
    # O --tela do app (e as fotos de documentação) abrem uma reunião numa aba.
    pagina.evaluate("() => { location.hash = 'revisao=4&notas'; location.reload(); }")
    pagina.wait_for_selector(".reuniao-aberta [role='tab']", timeout=5000)
    conferir(pagina.evaluate(ABA_ESCOLHIDA) == "notas", "'#revisao=4&notas' abre a reunião na aba Notas")


def prova_troca_de_etapa(pagina) -> None:
    linha_por_titulo(pagina, "Semanal")
    pagina.evaluate("() => window.__linha.click()")
    caminho = pagina.evaluate("() => window.__linha.dataset.gravacao")
    etapa = ("([c, e]) => window.__emitir({ tipo: 'transcricoes', transcricoes: { atual: "
             "{ gravacao: c, nome: 'x', tarefa: 'transcricao', etapa: e, fracao: 0.5, texto: '', "
             "comecou_em: '', terminou: false, erro: null, cancelada: false }, ultimo: null } })")
    pagina.evaluate(etapa, [caminho, "asr"])
    pagina.wait_for_timeout(30)
    pagina.evaluate("() => { window.__botao = document.querySelector('.reunioes__painel [data-acao]'); "
                    "window.__botao.focus(); }")
    pagina.evaluate(etapa, [caminho, "diarizacao"])
    pagina.wait_for_timeout(30)
    conferir(pagina.evaluate("() => window.__linha.lastElementChild.textContent") == "Separando falantes…",
             "a etiqueta acompanha a etapa")
    conferir(pagina.evaluate("() => window.__botao.isConnected && document.activeElement === window.__botao"),
             "trocar de etapa não recria o botão do painel, e o foco fica nele")


def prova_fim_rele_o_nucleo(pagina) -> None:
    # Retranscrever uma reunião que tem ata deixa a ata velha no disco: o
    # núcleo diz ata_velha, e a lista tem de acreditar nele e não num palpite.
    linha_por_titulo(pagina, "Comunicação")
    pagina.evaluate("() => window.__linha.click()")
    caminho = pagina.evaluate("() => window.__linha.dataset.gravacao")
    fim = ("([c, t]) => window.__emitir({ tipo: 'transcricoes', transcricoes: { atual: null, ultimo: "
           "{ gravacao: c, nome: 'x', tarefa: t, etapa: 'montagem', fracao: 1, texto: '', "
           "comecou_em: '', terminou: true, erro: null, cancelada: false } } })")
    pagina.evaluate("(c) => { window.__gravacoes.find((g) => g.caminho === c).ata_velha = true; }", caminho)
    pagina.evaluate(fim, [caminho, "transcricao"])
    pagina.wait_for_timeout(50)
    conferir(pagina.evaluate("() => window.__linha.lastElementChild.textContent") == "Ata desatualizada",
             "retranscrita com ata, a linha diz 'Ata desatualizada'")
    conferir(pagina.get_attribute(".reunioes__painel [data-acao]", "data-acao") == "refazer-ata",
             "e o painel oferece refazer a ata")
    # A ata terminou com a pessoa parada na lista: resumo e pendências chegam.
    linha_por_titulo(pagina, "Sherlock")
    pagina.evaluate("() => window.__linha.click()")
    caminho = pagina.evaluate("() => window.__linha.dataset.gravacao")
    pagina.evaluate("""(c) => Object.assign(window.__gravacoes.find((g) => g.caminho === c), {
        tem_ata: true, pendencias: 2, resumo: 'O NOC assume o plantão de sábado.',
        pendencias_inicio: ['Escala do sábado — Marcos — sexta', 'Contato do NOC — André — [prazo a definir]'] })""",
                    caminho)
    pagina.evaluate(fim, [caminho, "ata"])
    pagina.wait_for_timeout(50)
    conferir(pagina.evaluate("() => window.__linha.lastElementChild.textContent") == "Ata pronta",
             "a ata terminou, e a linha diz 'Ata pronta'")
    meta = pagina.evaluate("() => window.__linha.querySelector('.reuniao-linha__meta').textContent")
    conferir("2 pendências" in meta, f"e a linha conta as pendências da ata nova ({meta!r})")
    conferir("plantão de sábado" in (pagina.text_content(".reunioes__painel") or ""),
             "e o painel mostra o resumo da ata nova")


def prova_teclado(pagina) -> None:
    paradas = pagina.eval_on_selector_all(".reuniao-linha", "els => els.filter((e) => e.tabIndex === 0).length")
    conferir(paradas == 1, f"a lista é uma parada de Tab só ({paradas} linhas com tabIndex 0)")
    pagina.focus(".reuniao-linha[tabindex='0']")
    pagina.keyboard.press("ArrowDown")
    segunda = pagina.evaluate("() => document.activeElement.querySelector('.reuniao-linha__titulo').textContent")
    conferir(segunda == "Semanal — Beegol · Uberlândia", f"a seta para baixo vai à próxima linha ({segunda!r})")
    conferir(pagina.evaluate("() => document.activeElement.getAttribute('aria-pressed')") == "true",
             "e a escolhe")
    pagina.keyboard.press("Tab")
    conferir(pagina.evaluate("() => Boolean(document.activeElement.closest('.reunioes__painel'))"),
             "um Tab depois da lista cai no painel")
    pagina.keyboard.press("Shift+Tab")
    pagina.keyboard.press("ArrowUp")
    pagina.keyboard.press("Enter")
    pagina.wait_for_selector(".revisao", timeout=5000)
    conferir(True, "Enter numa linha abre a reunião, como o duplo clique")


def prova_janela_intermediaria(pagina) -> None:
    conferir(pagina.is_visible(".reunioes__painel"), "a 1000 px o painel aparece")
    conferir(pagina.evaluate(SEM_ROLAGEM_LATERAL), "e a lista cabe, sem rolagem lateral")
    aviso = pagina.evaluate("""() => {
        const e = document.querySelector('.reuniao-linha__aviso');
        return e && e.offsetParent !== null ? e.textContent : null;
    }""")
    conferir(aviso == "O microfone não teve áudio nenhum.",
             f"o aviso da gravação aparece escrito na linha, e não só ao passar o mouse ({aviso!r})")


prova_janela_intermediaria.janela = (1000, 700)


def prova_calendario_cabe_na_janela(pagina) -> None:
    # Na janela estreita o botão de data fica perto da borda direita, e o
    # painel preso à esquerda dele saía da tela.
    abrir_data(pagina)
    cabe = pagina.evaluate("""() => {
        const r = document.querySelector('.popover').getBoundingClientRect();
        return [Math.round(r.left), Math.round(r.right), innerWidth];
    }""")
    conferir(cabe[0] >= 0 and cabe[1] <= cabe[2], f"o calendário aberto cabe inteiro na janela ({cabe})")
    conferir(pagina.evaluate(SEM_ROLAGEM_LATERAL), "e não cria rolagem lateral")


prova_calendario_cabe_na_janela.janela = (860, 700)


def ir_para_semana(pagina) -> None:
    pagina.click(".barra [data-vista='semana']")
    pagina.wait_for_selector(".semana .semana__dia", timeout=3000)


def segunda_de(d: date) -> date:
    return d - timedelta(days=d.weekday())


def no_acervo(de: date, ate: date, hoje: date | None = None) -> int:
    hoje = hoje or date.today()
    return sum(1 for d in DIAS_DO_ACERVO if de <= hoje - timedelta(days=d) <= ate)


# O dia desta semana em que uma prova com `relogio` roda: a suíte não pode
# depender do dia em que é rodada, e o que quebra num domingo tem de quebrar hoje.
DOMINGO = lambda: segunda_de(date.today()) + timedelta(days=6)  # noqa: E731


def cartoes(pagina) -> list[str]:
    return pagina.eval_on_selector_all(".semana__cartao .semana__titulo", "els => els.map((e) => e.textContent)")


def prova_semana(pagina) -> None:
    vista = lambda: pagina.eval_on_selector_all(  # noqa: E731
        ".barra [data-vista]", "els => els.filter((e) => e.getAttribute('aria-pressed') === 'true').map((e) => e.dataset.vista)")
    conferir(vista() == ["lista"], f"Reuniões abre na lista, com Lista e Semana na barra ({vista()})")
    ir_para_semana(pagina)
    conferir(vista() == ["semana"] and not pagina.is_visible(".reunioes__lista"), "Semana troca a lista pela semana")
    conferir(not pagina.is_visible("#filtro-data"), "e o filtro de data sai, porque quem escolhe a data é a semana")

    hoje = date.today()
    seg = segunda_de(hoje)
    fim_de_semana = no_acervo(seg + timedelta(days=5), seg + timedelta(days=6)) > 0
    colunas = pagina.eval_on_selector_all(".semana__dia", "els => els.map((e) => e.dataset.dia)")
    conferir(len(colunas) == (7 if fim_de_semana else 5) and colunas[0] == seg.isoformat(),
             f"de segunda a sexta, e o fim de semana só com gravação ({len(colunas)} colunas)")
    de_hoje = pagina.eval_on_selector_all(".semana__dia--hoje .semana__titulo", "els => els.map((e) => e.textContent)")
    conferir(de_hoje == ["Sherlock Diário — Status e Ações", "Semanal — Beegol · Uberlândia", "Reunião de lideranças"],
             f"hoje vem marcado, com as reuniões dele em ordem de hora ({de_hoje})")
    conferir(len(cartoes(pagina)) == no_acervo(seg, seg + timedelta(days=6)),
             f"a semana tem as reuniões dela ({len(cartoes(pagina))})")
    sub = pagina.text_content("#subtitulo")
    conferir("semana de" in sub, f"o subtítulo diz a semana ({sub!r})")
    conferir(pagina.evaluate(SEM_ROLAGEM_LATERAL), "a semana cabe na largura da janela")

    buscar(pagina, "sherlock")
    conferir(cartoes(pagina) == ["Sherlock Diário — Status e Ações"], "a busca continua filtrando na semana")
    ativo = pagina.evaluate("() => document.activeElement && document.activeElement.id")
    conferir(ativo == "busca-reunioes", f"sem tirar o cursor do campo ({ativo!r})")
    buscar(pagina, "")

    rotulo = lambda: pagina.text_content(".semana-nav__escolher").strip()  # noqa: E731
    agora = rotulo()
    pagina.click(".semana-nav [aria-label='Semana anterior']")
    pagina.wait_for_timeout(50)
    conferir(rotulo() != agora and len(cartoes(pagina)) == no_acervo(seg - timedelta(days=7), seg - timedelta(days=1)),
             f"‹ vai à semana anterior ({rotulo()!r}, {len(cartoes(pagina))} reuniões)")
    rotulo_do_foco = pagina.evaluate("() => document.activeElement.getAttribute('aria-label')")
    conferir(rotulo_do_foco == "Semana anterior", f"e o foco fica no ‹, para clicar de novo ({rotulo_do_foco!r})")
    pagina.click(".semana-nav >> text=Hoje")
    pagina.wait_for_timeout(50)
    conferir(rotulo() == agora, "Hoje volta à semana de hoje")

    pagina.click(".semana-nav__escolher")
    pagina.wait_for_selector(".popover .calendario", timeout=2000)
    dia_no_calendario(pagina, hoje - timedelta(days=45))
    conferir(cartoes(pagina) == ["Planejamento trimestral"],
             f"escolher um dia no calendário mostra a semana dele ({cartoes(pagina)})")
    conferir(not pagina.is_visible(".popover"), "e fecha o calendário")


def prova_semana_abre_e_volta(pagina) -> None:
    ir_para_semana(pagina)
    pagina.click(".semana-nav [aria-label='Semana anterior']")
    pagina.wait_for_timeout(50)
    antes = pagina.text_content(".semana-nav__escolher").strip()
    # Sete dias atrás é sempre a semana anterior; seis, no domingo, ainda é esta.
    pagina.dblclick(".semana__cartao >> text=Agente de Crédito — kickoff")
    pagina.wait_for_selector(".reuniao-aberta [role='tab']", timeout=5000)
    conferir(True, "o duplo clique no cartão abre a reunião, como na lista")
    pagina.click("#voltar")
    pagina.wait_for_selector(".semana .semana__dia", timeout=5000)
    conferir(pagina.text_content(".semana-nav__escolher").strip() == antes,
             "← Reuniões volta à semana, na mesma semana")
    pagina.click(".barra [data-vista='lista']")
    pagina.wait_for_selector(".reuniao-linha", timeout=3000)
    conferir(pagina.is_visible("#filtro-data") and not pagina.is_visible(".semana-nav"),
             "Lista devolve a lista, com o filtro de data, e tira a navegação da semana")


def prova_semana_abre_e_volta_no_domingo(pagina) -> None:
    # A mesma prova, com o relógio num domingo: ela falhava um dia em sete.
    prova_semana_abre_e_volta(pagina)


prova_semana_abre_e_volta_no_domingo.relogio = lambda: f"{DOMINGO().isoformat()}T12:00:00"


def prova_semana_hoje_vira_a_semana(pagina) -> None:
    # A bandeja fica aberta dias a fio — fechar a janela só esconde —, e a tela
    # não se remonta. Na segunda de manhã, Hoje tem de ir à semana nova.
    domingo = DOMINGO()
    segunda = domingo + timedelta(days=1)
    ir_para_semana(pagina)
    primeira = lambda: pagina.evaluate("() => document.querySelector('.semana__dia').dataset.dia")  # noqa: E731
    conferir(primeira() == (domingo - timedelta(days=6)).isoformat(), "no domingo à noite, a semana é a que acaba nele")
    pagina.clock.set_system_time(f"{segunda.isoformat()}T08:00:00")
    pagina.click(".semana-nav >> text=Hoje")
    pagina.wait_for_timeout(50)
    conferir(primeira() == segunda.isoformat(), f"na segunda de manhã, Hoje vai à semana nova ({primeira()!r})")
    hoje_marcado = pagina.evaluate("() => document.querySelector('.semana__dia--hoje')?.dataset.dia")
    conferir(hoje_marcado == segunda.isoformat(), f"e marca a segunda como hoje ({hoje_marcado!r})")
    conferir("semana-nav__hoje--agora" in (pagina.get_attribute(".semana-nav__hoje", "class") or ""),
             "e o Hoje diz que está na semana de hoje")
    # Quem estava na semana de hoje continua nela ao voltar pelo trilho.
    pagina.clock.set_system_time(f"{(segunda + timedelta(days=7)).isoformat()}T08:00:00")
    pagina.click("#ir-reunioes")
    pagina.wait_for_selector(".semana .semana__dia", timeout=5000)
    conferir(primeira() == (segunda + timedelta(days=7)).isoformat(),
             f"e remontar a tela na outra segunda abre a semana dela ({primeira()!r})")


prova_semana_hoje_vira_a_semana.relogio = lambda: f"{DOMINGO().isoformat()}T23:58:00"


def prova_semana_fim_de_semana_numa_linha(pagina) -> None:
    # 1200 px é a largura da janela a 100%. Sábado e domingo entram juntos, e
    # juntos ficam: lado a lado com a semana, ou os dois na linha de baixo.
    ir_para_semana(pagina)
    topos = lambda: pagina.eval_on_selector_all(".semana__dia", "els => els.map((e) => Math.round(e.getBoundingClientRect().top))")  # noqa: E731
    t = topos()
    conferir(len(t) == 7 and len(set(t)) == 1, f"a 1200 px, as sete colunas numa linha só ({t})")
    pagina.set_viewport_size({"width": 1000, "height": 800})
    pagina.wait_for_timeout(100)
    t = topos()
    conferir(t[5] == t[6] and t[5] > t[4], f"a 1000 px, sábado e domingo descem juntos ({t})")


prova_semana_fim_de_semana_numa_linha.janela = (1200, 800)
# Num sábado, as reuniões de "hoje" do acervo caem no fim de semana.
prova_semana_fim_de_semana_numa_linha.relogio = lambda: f"{(DOMINGO() - timedelta(days=1)).isoformat()}T12:00:00"


def prova_semana_filtro_esconde(pagina) -> None:
    # Os critérios ficam na memória do módulo: um filtro deixado ontem na lista
    # continua valendo na semana de hoje, e ela não pode dizer que não houve nada.
    ir_para_semana(pagina)
    seg = segunda_de(date.today())
    total = no_acervo(seg, seg + timedelta(days=6))
    buscar(pagina, "zzzz")
    sub = pagina.text_content("#subtitulo")
    conferir(f"0 de {total}" in sub, f"o subtítulo diz quantas a semana tem, e quantas sobraram ({sub!r})")
    de_hoje = pagina.text_content(".semana__dia--hoje")
    conferir("Nenhuma com esses filtros" in de_hoje, f"o dia que tinha reuniões diz que o filtro as escondeu ({de_hoje!r})")
    conferir(pagina.is_visible(".semana .reunioes__vazio button"), "e a semana oferece limpar os filtros")
    pagina.click(".semana .reunioes__vazio button")
    pagina.wait_for_timeout(50)
    conferir(len(cartoes(pagina)) == total, "Limpar filtros devolve a semana inteira")


def prova_popover_fecha_com_shift_tab(pagina) -> None:
    # Voltar com Shift+Tab passa pelo botão que abriu; o painel tem de fechar
    # quando o foco sai dos dois, como fecha com Tab para a frente.
    for abrir_painel, gatilho in [(lambda: abrir_data(pagina), "#filtro-data"),
                                  (None, ".semana-nav__escolher")]:
        if abrir_painel is None:
            ir_para_semana(pagina)
            pagina.click(gatilho)
            pagina.wait_for_selector(".popover .calendario", timeout=2000)
        else:
            abrir_painel()
        for _ in range(20):
            if pagina.evaluate(f"() => document.activeElement === document.querySelector('{gatilho}')"):
                break
            pagina.keyboard.press("Shift+Tab")
        pagina.keyboard.press("Shift+Tab")
        conferir(not pagina.is_visible(".popover"), f"Shift+Tab para fora fecha o painel de {gatilho}")
        conferir(pagina.get_attribute(gatilho, "aria-expanded") == "false", "e o botão diz que fechou")


def prova_semana_clique_mostra_o_resumo(pagina) -> None:
    # Achado no percurso do dono em 24/09/2026: na semana, o clique ia direto à
    # reunião, e o que o painel da lista mostra — o próximo passo, o que a ata
    # diz, as pendências — não se via.
    ir_para_semana(pagina)
    cartao = ".semana__cartao >> text=Reunião de lideranças"
    pagina.click(cartao)
    pagina.wait_for_timeout(50)
    conferir(pagina.locator(".reuniao-aberta").count() == 0, "um clique no cartão não sai da semana")
    conferir(pagina.is_visible(".semana__detalhe .reunioes__painel"), "e abre o painel da reunião por cima")
    conferir("Reunião de lideranças" in (pagina.text_content(".semana__detalhe") or ""), "com a reunião clicada")
    marcado = pagina.eval_on_selector_all(".semana__cartao[aria-pressed='true'] .semana__titulo",
                                          "els => els.map((e) => e.textContent)")
    conferir(marcado == ["Reunião de lideranças"], f"e o cartão fica marcado ({marcado})")
    conferir(pagina.evaluate(A_VISTA, ".semana__detalhe [data-acao]"), "com o próximo passo à vista")
    pagina.keyboard.press("Escape")
    conferir(not pagina.is_visible(".semana__detalhe"), "Esc fecha o painel")
    ativo = pagina.evaluate("() => document.activeElement.closest('.semana__cartao') !== null")
    conferir(ativo, "e devolve o foco ao cartão")
    pagina.click(cartao)
    pagina.click(".semana__detalhe [aria-label='Fechar o painel']")
    conferir(not pagina.is_visible(".semana__detalhe"), "o ✕ também fecha")
    pagina.focus(".semana__cartao >> nth=0")
    pagina.keyboard.press("Enter")
    pagina.wait_for_selector(".reuniao-aberta [role='tab']", timeout=5000)
    conferir(True, "Enter no cartão abre a reunião, como na lista")


def prova_semana_resumo_na_janela_estreita(pagina) -> None:
    # Na lista estreita o painel some; na semana ele é por cima, e cabe.
    ir_para_semana(pagina)
    pagina.click(".semana__cartao >> text=Reunião de lideranças")
    pagina.wait_for_timeout(50)
    caixa = pagina.evaluate("""() => { const r = document.querySelector('.semana__detalhe').getBoundingClientRect();
                                        return [Math.round(r.left), Math.round(r.right), innerWidth]; }""")
    conferir(pagina.is_visible(".semana__detalhe .reunioes__painel") and caixa[0] >= 0 and caixa[1] <= caixa[2],
             f"na janela estreita, o painel da semana aparece e cabe ({caixa})")


prova_semana_resumo_na_janela_estreita.janela = (860, 700)


def prova_painel_rola_sozinho(pagina) -> None:
    # Achado no percurso do dono em 24/09/2026: o painel mais alto que a janela
    # só mostrava o fim quando a lista inteira rolava até o fim.
    linha_por_titulo(pagina, "Comunicação")
    pagina.evaluate("() => window.__linha.click()")
    pagina.wait_for_timeout(50)
    m = pagina.evaluate("""() => { const p = document.querySelector('.reunioes__painel'), r = p.getBoundingClientRect();
                                    return { baixo: Math.round(r.bottom), janela: innerHeight,
                                             rola: p.scrollHeight > p.clientHeight }; }""")
    conferir(m["baixo"] <= m["janela"], f"o painel cabe na janela ({m})")
    conferir(m["rola"], "e rola sozinho quando o que ele mostra é mais alto")
    pagina.evaluate("() => { const p = document.querySelector('.reunioes__painel'); p.scrollTop = p.scrollHeight; }")
    pagina.wait_for_timeout(50)
    fim = pagina.evaluate("""() => { const p = document.querySelector('.reunioes__painel');
                                      return Math.round(p.lastElementChild.getBoundingClientRect().bottom) <= innerHeight
                                             && document.querySelector('.conteudo').scrollTop === 0; }""")
    conferir(fim, "o fim do painel se alcança rolando o painel, sem rolar a lista")


prova_painel_rola_sozinho.janela = (1280, 520)


def prova_semana_etiqueta(pagina) -> None:
    ir_para_semana(pagina)
    caminho = pagina.evaluate("() => window.__gravacoes[3].caminho")   # Sherlock, hoje
    pagina.evaluate("(c) => { window.__cartao = document.querySelector(`.semana__cartao[data-gravacao='${CSS.escape(c)}']`); }",
                    caminho)
    pagina.evaluate(TAREFA_EM_CURSO, [caminho, "ata", "lendo"])
    pagina.wait_for_timeout(50)
    etiqueta = pagina.evaluate("() => window.__cartao.lastElementChild.textContent")
    conferir(etiqueta == "Escrevendo a ata…", f"a etiqueta do cartão acompanha a tarefa ({etiqueta!r})")
    conferir(pagina.evaluate("(c) => window.__cartao === document.querySelector(`.semana__cartao[data-gravacao='${CSS.escape(c)}']`)",
                             caminho), "sem recriar o cartão")


def prova_semana_estreita(pagina) -> None:
    ir_para_semana(pagina)
    conferir(pagina.evaluate(SEM_ROLAGEM_LATERAL), "a semana cabe numa janela estreita, sem rolagem lateral")
    conferir(pagina.evaluate(A_VISTA, ".semana-nav [aria-label='Próxima semana']"),
             "e a navegação da semana continua à vista na barra")


prova_semana_estreita.janela = (860, 700)


def prova_calendario_atravessa_o_mes(pagina) -> None:
    # As setas passam da borda do mês, e a grade tem de ir junto: o dia focado
    # sumir da tela deixaria o foco em lugar nenhum.
    hoje = date.today()
    abrir_data(pagina)
    pagina.keyboard.press("Home")
    for _ in range(hoje.day // 7 + 2):
        pagina.keyboard.press("ArrowUp")
    esperado = hoje - timedelta(days=hoje.weekday()) - timedelta(days=7 * (hoje.day // 7 + 2))
    focado = pagina.evaluate("() => document.activeElement.dataset.dia")
    conferir(focado == esperado.isoformat(), f"a seta atravessa o mês e leva o foco junto ({focado!r})")
    mes = pagina.text_content(".popover .calendario__mes")
    conferir(str(esperado.year) in mes and pagina.locator(f".popover [data-dia='{esperado.isoformat()}']").count() == 1,
             f"e a grade mostra o mês do dia focado ({mes!r})")


def prova_calendario_sobrevive_a_tarefa(pagina) -> None:
    # Os eventos de andamento chegam várias vezes por etapa. A barra da lista não
    # se redesenha com eles, e o calendário aberto nela também não pode fechar.
    abrir_data(pagina)
    pagina.keyboard.press("ArrowLeft")
    antes = pagina.evaluate("() => document.activeElement.dataset.dia")
    caminho = pagina.evaluate("() => window.__gravacoes[1].caminho")
    pagina.evaluate(TAREFA_EM_CURSO, [caminho, "asr", "asr"])
    pagina.evaluate(FIM_DE_TAREFA, [caminho, "asr"])
    pagina.wait_for_timeout(100)
    conferir(pagina.is_visible(".popover"), "uma tarefa andando não fecha o calendário aberto")
    depois = pagina.evaluate("() => document.activeElement.dataset.dia")
    conferir(depois == antes, f"nem tira o foco do dia ({antes!r} → {depois!r})")


def prova_semana_a_125(pagina) -> None:
    # A janela abre com 1200 px físicos, e a 125% de escala — a de notebook —
    # isso dá ~945 px: a barra está no limite de caber título e navegação.
    ir_para_semana(pagina)
    conferir(pagina.evaluate(SEM_ROLAGEM_LATERAL), "a 125%, a semana cabe sem rolagem lateral")
    for rotulo in ["Semana anterior", "Próxima semana"]:
        conferir(pagina.evaluate(A_VISTA, f".semana-nav [aria-label='{rotulo}']"), f"e o botão '{rotulo}' está à vista")
    conferir(pagina.evaluate(A_VISTA, ".barra [data-vista='lista']"), "e o Lista também")


prova_semana_a_125.janela = (945, 700)


def prova_semana_barra_troca_de_tela(pagina) -> None:
    # A navegação da semana mora na barra do topo, que é de todas as telas. A
    # reunião aberta tem outro título, e é pela troca de título que a barra
    # se esvazia (cabecalho, em app.js).
    ir_para_semana(pagina)
    pagina.dblclick(".semana__cartao >> text=Sherlock Diário")
    pagina.wait_for_selector(".reuniao-aberta [role='tab']", timeout=5000)
    conferir(pagina.locator(".barra .semana-nav, .barra [data-vista]").count() == 0,
             "na reunião aberta, a barra não mostra a navegação da semana")
    pagina.click("#ir-reunioes")
    pagina.wait_for_selector(".semana .semana__dia", timeout=5000)
    conferir(pagina.locator(".barra .semana-nav").count() == 1 and pagina.is_visible(".semana-nav"),
             "de volta a Reuniões, a semana e a navegação voltam, uma vez só")


def prova_data_nao_rouba_o_foco(pagina) -> None:
    # Era o campo de data que aparecia e levava o foco; o botão só abre quando
    # se pede, e passar por ele com Tab não abre nada.
    pagina.focus("#filtro-cliente")
    pagina.keyboard.press("Tab")
    ativo = pagina.evaluate("() => document.activeElement.id")
    conferir(ativo == "filtro-data" and not pagina.is_visible(".popover"),
             f"passar pelo filtro de data com Tab não abre o calendário ({ativo!r})")



# Com a janela baixa, que é onde a barra fixa do topo cobriria o que recebe o
# foco. Era o cartão rolado da tela de Atas; é a aba Ata desde que ela existe.
prova_ata_na_reuniao_pedida.janela = (1280, 520)
prova_gerar_ata_na_reuniao_pedida.janela = (1280, 520)

FOCO_A_VISTA = """() => {
  const e = document.activeElement;
  if (!e || e === document.body) return false;
  const r = e.getBoundingClientRect();
  const alvo = document.elementFromPoint(r.left + r.width / 2, r.top + Math.min(r.height / 2, 10));
  return alvo === e || e.contains(alvo);
}"""

PROVAS = [prova_grupos, prova_busca, prova_filtros, prova_filtro_de_data, prova_calendario, prova_calendario_cabe_na_janela,
          prova_calendario_atravessa_o_mes, prova_calendario_sobrevive_a_tarefa,
          prova_sem_resultado, prova_criterios_sobrevivem,
          prova_painel, prova_transcricao_em_curso, prova_janela_estreita, prova_ata_na_reuniao_pedida,
          prova_gerar_ata_na_reuniao_pedida, prova_troca_de_etapa, prova_fim_rele_o_nucleo,
          prova_teclado, prova_janela_intermediaria, prova_data_nao_rouba_o_foco,
          prova_reuniao_abas, prova_reuniao_teclado, prova_reuniao_cabecalho,
          prova_reuniao_falantes_da_ata, prova_renomear_e_trocar_de_reuniao, prova_barra_esvazia_no_preparo,
          prova_reuniao_ata, prova_reuniao_gerar_ata, prova_preparo_vocabulario,
          prova_reuniao_pelo_endereco, prova_trilho_sem_atas, prova_endereco_de_atas,
          prova_reuniao_rolagem, prova_tocador, prova_tocador_troca_de_reuniao, prova_ata_so_acompanha_a_ata,
          prova_notas_tocam,
          prova_semana, prova_semana_abre_e_volta, prova_semana_etiqueta, prova_semana_estreita,
          prova_semana_a_125, prova_semana_barra_troca_de_tela,
          prova_semana_abre_e_volta_no_domingo, prova_semana_hoje_vira_a_semana,
          prova_semana_fim_de_semana_numa_linha, prova_semana_filtro_esconde,
          prova_popover_fecha_com_shift_tab, prova_semana_clique_mostra_o_resumo,
          prova_semana_resumo_na_janela_estreita, prova_painel_rola_sozinho]



def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--fotos", type=Path, help="pasta onde deixar as fotos das telas")
    ap.add_argument("--so", help="só estas provas, separadas por vírgula (prova_filtros,prova_calendario)")
    args = ap.parse_args()
    provas = PROVAS
    if args.so:
        pedidas = set(args.so.split(","))
        provas = [p for p in PROVAS if p.__name__ in pedidas]
        if len(provas) != len(pedidas):
            print(f"prova desconhecida: {sorted(pedidas - {p.__name__ for p in PROVAS})}", file=sys.stderr)
            return 2

    try:
        from playwright.sync_api import sync_playwright
    except ImportError:
        print("falta o playwright: uv run --with playwright python tools/provar_reunioes.py",
              file=sys.stderr)
        return 2

    socketserver.TCPServer.allow_reuse_address = True
    with socketserver.TCPServer(("127.0.0.1", 0), Servidor) as srv:
        porta = srv.server_address[1]
        threading.Thread(target=srv.serve_forever, daemon=True).start()

        with sync_playwright() as pw:
            navegador = pw.chromium.launch()
            for prova in provas:
                print(f"── {prova.__name__}")
                largura, altura = getattr(prova, "janela", (1280, 800))
                relogio = getattr(prova, "relogio", lambda: None)()
                pagina, erros = abrir(navegador, porta, largura, altura, relogio=relogio)
                # Uma prova que estoura (um seletor que nunca aparece) conta como
                # falha, e as outras continuam: parar na primeira esconderia o
                # resto do que quebrou.
                try:
                    prova(pagina)
                except Exception as e:  # noqa: BLE001 — qualquer estouro é falha da prova
                    conferir(False, f"{prova.__name__} estourou: {str(e).splitlines()[0]}")
                conferir(not erros, f"sem erro de JavaScript {erros[:2] if erros else ''}")
                pagina.close()

            if args.fotos:
                args.fotos.mkdir(parents=True, exist_ok=True)
                for tema in ["escuro", "claro"]:
                    for largura, altura in [(1280, 800), (1000, 700)]:
                        pagina, _ = abrir(navegador, porta, largura, altura, tema)
                        pagina.screenshot(path=str(args.fotos / f"reunioes-{tema}-{largura}.png"))
                        pagina.close()
            navegador.close()
        srv.shutdown()

    if falhas:
        print(f"\n{len(falhas)} falha(s).", file=sys.stderr)
        return 1
    print("\ntudo certo.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
