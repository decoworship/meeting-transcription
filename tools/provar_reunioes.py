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
      case "config": return { config: {} };
      case "transcricao": return { transcricao: window.__transcricao ?? transcricao };
      case "reuniao": return { cliente: "", projeto: "" };
      case "legenda-gravada": return { legenda_gravada: [] };
      case "clientes": return { clientes: {} };
      case "prefs": return { prefs: null };
      case "notas": return { notas: "" };
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
      const r = Object.assign({ id: q.id }, responder(q));
      setTimeout(() => { for (const f of this._ouvintes) f({ data: JSON.stringify(r) }); }, 0);
    },
  } };
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


def abrir(navegador, porta: int, largura: int = 1280, altura: int = 800, tema: str | None = None):
    pagina = navegador.new_page(viewport={"width": largura, "height": altura})
    erros: list[str] = []
    pagina.on("pageerror", lambda e: erros.append(str(e)))
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
          prova_reuniao_abas, prova_reuniao_teclado, prova_reuniao_ata, prova_reuniao_gerar_ata,
          prova_reuniao_pelo_endereco, prova_trilho_sem_atas, prova_endereco_de_atas,
          prova_reuniao_rolagem, prova_ata_so_acompanha_a_ata]



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
                pagina, erros = abrir(navegador, porta, largura, altura)
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
