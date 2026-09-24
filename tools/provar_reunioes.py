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
      case "transcricao": return { transcricao };
      case "reuniao": return { cliente: "", projeto: "" };
      case "legenda-gravada": return { legenda_gravada: [] };
      case "clientes": return { clientes: {} };
      case "prefs": return { prefs: null };
      case "notas": return { notas: "" };
      case "modelos-de-ata": return { tipos: [{ id: "geral", nome: "Reunião geral" }] };
      case "ata": return q.gravacao.includes("13-59") ? { ata, ata_velha: false } : { ata: null };
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
    conferir(not pagina.is_visible("#filtro-data"), "sem 'Um dia…', o campo de data não aparece")
    pagina.select_option("#filtro-periodo", "dia")
    conferir(pagina.is_visible("#filtro-data"), "com 'Um dia…', o campo de data aparece")
    conferir(len(titulos(pagina)) == 9, "e, sem o dia escolhido, a lista continua inteira")
    ha_seis_dias = (date.today() - timedelta(days=6)).isoformat()
    pagina.fill("#filtro-data", ha_seis_dias)
    pagina.wait_for_timeout(50)
    conferir(titulos(pagina) == ["Comunicação Beegol + App", "Pedido Sugerido — alinhamento"],
             f"um dia escolhido deixa só as reuniões dele ({titulos(pagina)})")


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
    pagina.wait_for_selector(".revisao", timeout=5000)
    pagina.click("#voltar")
    pagina.wait_for_selector(".reuniao-linha", timeout=5000)
    conferir(pagina.input_value("#filtro-cliente") == "Vivo", "o filtro voltou como estava")
    conferir(titulos(pagina) == ["Sherlock Diário — Status e Ações"], "e a lista voltou filtrada")


PROVAS = [prova_grupos, prova_busca, prova_filtros, prova_sem_resultado, prova_criterios_sobrevivem]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--fotos", type=Path, help="pasta onde deixar as fotos das telas")
    args = ap.parse_args()

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
            for prova in PROVAS:
                print(f"── {prova.__name__}")
                largura, altura = getattr(prova, "janela", (1280, 800))
                pagina, erros = abrir(navegador, porta, largura, altura)
                prova(pagina)
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
