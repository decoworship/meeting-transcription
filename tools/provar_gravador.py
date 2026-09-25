#!/usr/bin/env python3
"""Prova a tela do Gravador num Chromium de verdade — o plano 3 do redesenho.

A tela da única hora em que o app não pode falhar se prova no navegador, não no
raciocínio (spec §4). Mesmo molde do ``tools/provar_reunioes.py``: o app inteiro
(``index.html`` com a moldura, o trilho e o chip de gravando), uma ponte falsa
que responde o que o núcleo responderia, e afirmações que falham com mensagem.

O estado do gravador é o do núcleo (``EstadoDoGravador`` em ``App/Ponte.cs``) e
chega pelo ``id: 0`` como lá; a prova o empurra com ``window.__empurrar()``. A
legenda chega pelo evento ``aovivo`` com ``{novo, tentativo, dono}`` — sem tempo,
como o núcleo manda —, e o relógio que a tela usa para carimbá-la é o
``duracao_s`` do último estado empurrado.

Uso::

    uv run --with playwright python tools/provar_gravador.py [--so prova_a,prova_b] [--fotos PASTA]
"""

import argparse
import http.server
import socketserver
import sys
import threading
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
WEB = RAIZ / "app-net" / "App" / "web"
DS = RAIZ / "assets" / "ds"

# A ponte falsa. As horas da agenda nascem de "agora", porque o herói diz
# "começa em N min" e a lista marca o que já terminou.
PONTE_FALSA = r"""
(() => {
  const agora = Date.now();
  const hora = (min) => new Date(agora + min * 60000).toISOString();
  const p = (n) => String(n).padStart(2, "0");
  const hoje = new Date();
  const nome = (h, m) => `${hoje.getFullYear()}-${p(hoje.getMonth() + 1)}-${p(hoje.getDate())}_${p(h)}-${p(m)}-00`;

  const faixa = (nome, dispositivo, extra) => Object.assign({
    nome, dispositivo, nivel: 0.05, ja_ouviu: true, mudo: false, silencio_s: 0,
    desconectado: false, falha: null }, extra);

  window.__estado = {
    gravando: false, mudo: false, mudo_ha_s: 0, cor: "cinza", status: "Parado",
    duracao_s: 0, pasta: "C:\\Users\\andre\\Gravacoes", gravacao: null,
    titulo: null, participantes: null, fixado: null, notificacoes: true,
    usar_agenda: true, agenda_configurada: true, conta: "andre@beegol.com",
    faixas: [faixa("mic", "Headset AN01 Hands-Free"), faixa("system", "Alto-falantes (Realtek Audio)")],
  };

  const EVENTOS = [
    { id: "e1", titulo: "Sherlock Diário — Status e Ações", inicio: hora(-300), fim: hora(-270), participantes: 4, organizador: "vivo.com.br" },
    { id: "e2", titulo: "Reunião de lideranças", inicio: hora(-170), fim: hora(-130), participantes: 9, organizador: "beegol.com" },
    { id: "e3", titulo: "Comunicação Beegol + App", inicio: hora(5), fim: hora(35), participantes: 7, organizador: "algar.com.br",
      nomes: ["Carol Souza", "Rafael Prado", "André Yuri"] },
    { id: "e4", titulo: "Update Squad Beegol — Faturamento B2B", inicio: hora(125), fim: hora(155), participantes: 5, organizador: "beegol.com" },
  ];
  window.__proximas = { status: "ok", eventos: EVENTOS, pre_definido: "e3" };

  const G = (n, extra) => Object.assign({
    nome: n, caminho: "C:\\Rec\\" + n, duracao_s: 1800, titulo: null, convidados: 0,
    transcrita: true, cliente: null, projeto: null, com_notas: false, avisos: [],
    tem_ata: false, ata_velha: false, pendencias: 0, pendencias_inicio: [],
    resumo: null, nomes: [], notas_inicio: null,
  }, extra);
  window.__gravacoes = [
    G(nome(11, 2), { titulo: "Reunião de lideranças", cliente: "Beegol (interno)", projeto: "Gestão",
                     duracao_s: 2220, transcrita: false, convidados: 9 }),
    G(nome(9, 0), { titulo: "Sherlock Diário — Status e Ações", cliente: "Vivo", projeto: "Sherlock" }),
    G("2026-09-18_14-00-00", { titulo: "Comunicação Beegol + App", cliente: "Algar", projeto: "Agentes" }),
  ];
  window.__transcricoes = { atual: null, ultimo: null };
  window.__aovivo = { aovivo_modo: "legenda", aovivo_impedimento: null, aovivo_ate: [], perguntar_impedimento: null };
  window.__notas = {};
  window.__vinculos = {};
  window.__pedidos = [];

  const G_ = () => JSON.parse(JSON.stringify(window.__estado));
  const responder = (q) => {
    const e = window.__estado;
    switch (q.op) {
      case "gravador": return { gravador: G_() };
      case "dispositivos": return { dispositivos: {
        entradas: [{ id: "m1", nome: "Headset AN01 Hands-Free", padrao: true }, { id: "m2", nome: "Microfone (Realtek)" }],
        saidas: [{ id: "s1", nome: "Alto-falantes (Realtek Audio)", padrao: true }],
        mic_id: null, loopback_id: null } };
      case "agenda-proximas": return { proximas: window.__proximas };
      case "gravacoes": return { gravacoes: window.__gravacoes };
      case "transcricoes": return { transcricoes: window.__transcricoes };
      case "clientes": return { clientes: { "Algar": ["Agentes", "Agente de Crédito"], "Beegol (interno)": ["Gestão", "App"], "Vivo": ["Sherlock"] } };
      case "aovivo": return window.__aovivo;
      case "config": return { config: {} };
      case "notas": return { notas: window.__notas[q.gravacao] ?? "" };
      case "salvar-notas": window.__notas[q.gravacao] = q.conteudo; return {};
      case "reuniao": return window.__vinculos[q.gravacao] ?? { cliente: "", projeto: "" };
      case "salvar-reuniao": window.__vinculos[q.gravacao] = { cliente: q.cliente, projeto: q.projeto }; return {};
      case "fixar-evento": {
        e.fixado = q.evento || null;
        const ev = EVENTOS.find((x) => x.id === e.fixado);
        if (!e.gravando) { e.titulo = ev?.titulo ?? null; e.participantes = ev?.nomes ?? null; }
        return { gravador: G_() };
      }
      case "gravar": {
        const ev = EVENTOS.find((x) => x.id === (e.fixado || window.__proximas.pre_definido));
        Object.assign(e, { gravando: true, cor: "vermelho", status: "Gravando", duracao_s: 0,
          gravacao: "C:\\Rec\\" + nome(14, 0), titulo: ev?.titulo ?? null, participantes: ev?.nomes ?? null });
        return { gravador: G_() };
      }
      case "parar-gravacao":
        Object.assign(e, { gravando: false, cor: "cinza", status: "Parado", gravacao: null, fixado: null, mudo: false, mudo_ha_s: 0 });
        return { gravador: G_() };
      case "mutar":
        e.mudo = !e.mudo; e.mudo_ha_s = 0; e.cor = e.mudo ? "laranja" : "vermelho";
        e.faixas[0].mudo = e.mudo;
        return { gravador: G_() };
      case "escolher-dispositivo": return { gravador: G_() };
      case "catalogo": return { catalogo: [] };
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
  window.__emitir = (ev) => {
    for (const f of window.chrome.webview._ouvintes)
      f({ data: JSON.stringify(Object.assign({ id: 0 }, ev)) });
  };
  // Empurra o estado como o núcleo faz a cada 200 ms, mudando o que se pedir.
  window.__empurrar = (mudar) => {
    Object.assign(window.__estado, mudar || {});
    window.__emitir({ tipo: "gravador", gravador: G_() });
  };
  window.__legenda = (novo, tentativo, dono) =>
    window.__emitir({ tipo: "aovivo", legenda: { novo, tentativo, dono } });
})();
"""


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
          antes: str | None = None, hash_: str = "gravador"):
    pagina = navegador.new_page(viewport={"width": largura, "height": altura})
    erros: list[str] = []
    pagina.on("pageerror", lambda e: erros.append(str(e)))
    pagina.add_init_script(PONTE_FALSA)
    # O que a prova muda na ponte antes de a tela montar (sem agenda, sem legenda…).
    if antes:
        pagina.add_init_script(antes)
    if tema:
        pagina.add_init_script(
            "document.addEventListener('DOMContentLoaded', () => "
            f"document.documentElement.dataset.tema = '{tema}')")
    pagina.goto(f"http://127.0.0.1:{porta}/index.html#{hash_}", wait_until="load")
    pagina.wait_for_selector("#tela[aria-busy='false']", timeout=5000)
    pagina.wait_for_timeout(100)
    return pagina, erros


def pedidos(pagina, op: str) -> list[dict]:
    return pagina.evaluate("(op) => window.__pedidos.filter((q) => q.op === op)", op)


SEM_ROLAGEM_LATERAL = """() => {
  const c = document.querySelector('.conteudo');
  return c.scrollWidth <= c.clientWidth && document.documentElement.scrollWidth <= innerWidth;
}"""


# ─────────────────────────────────────────────────────────────── as provas

def prova_monta(pagina) -> None:
    conferir(pagina.text_content("#titulo") == "Gravador", "o Gravador monta, com o título na barra")


GRAVANDO = """
Object.assign(window.__estado, { gravando: true, cor: "vermelho", status: "Gravando",
  duracao_s: 1884, gravacao: "C:\\\\Rec\\\\hoje_14-00-00", titulo: "Comunicação Beegol + App",
  participantes: ["Carol Souza", "Rafael Prado", "André Yuri"], fixado: "e3" });
window.__vinculos["C:\\\\Rec\\\\hoje_14-00-00"] = { cliente: "Algar", projeto: "Agentes" };
"""


def legenda(pagina, t: float, novo: str, tentativo: str = "", dono: bool = False) -> None:
    """Um pedaço da legenda chegando no segundo `t` da gravação."""
    pagina.evaluate("(t) => window.__empurrar({ duracao_s: t })", t)
    pagina.evaluate("([n, te, d]) => window.__legenda(n, te, d)", [novo, tentativo, dono])


LINHAS = """() => [...document.querySelectorAll('.aovivo .fala')].map((e) => ({
  tempo: e.querySelector('.fala__tempo').textContent,
  dono: e.querySelector('.fala__dono').textContent,
  texto: e.querySelector('.fala__texto').textContent,
  volatil: e.classList.contains('fala--volatil') }))"""


def prova_legenda_quebra_na_pausa(pagina) -> None:
    pedacos = [(100, "eu vou"), (101.1, "consolidar"), (102.2, "as referências"),
               (103.3, "e compartilhar"),
               (106.7, "aí a gente"), (107.8, "valida"), (108.9, "com o Rafael"), (110.0, "antes de fechar"),
               (113.4, "concordo e"), (114.5, "o jurídico"), (115.6, "precisa ver"), (116.7, "os textos")]
    for t, novo in pedacos:
        legenda(pagina, t, " " + novo)
    linhas = pagina.evaluate(LINHAS)
    conferir(len(linhas) == 3, f"doze pedaços dos Outros com duas pausas viram três linhas ({len(linhas)})")
    conferir([l["tempo"] for l in linhas] == ["00:01:40", "00:01:46", "00:01:53"],
             f"cada linha com o próprio tempo ({[l['tempo'] for l in linhas]})")
    conferir([l["dono"] for l in linhas] == ["Outros", "", ""],
             f"e \"Outros\" só na primeira da sequência ({[l['dono'] for l in linhas]})")
    conferir(linhas and linhas[0]["texto"] == "eu vou consolidar as referências e compartilhar",
             f"o texto cresce na mesma linha ({linhas[0]['texto'] if linhas else None!r})")

    legenda(pagina, 118, "", "também vale validar", True)
    linhas = pagina.evaluate(LINHAS)
    vol = [l for l in linhas if l["volatil"]]
    conferir(len(vol) == 1 and vol[0]["tempo"] == "agora" and vol[0]["dono"] == "Você",
             f"o rascunho é uma linha só, \"agora\", com o dono ({vol})")
    italico = pagina.evaluate("() => getComputedStyle(document.querySelector('.fala--volatil .fala__texto')).fontStyle")
    conferir(italico == "italic", "e em itálico")
    legenda(pagina, 119, " também vale validar", "", True)
    linhas = pagina.evaluate(LINHAS)
    conferir(not any(l["volatil"] for l in linhas) and linhas[-1]["dono"] == "Você",
             "o rascunho some quando firma, e a fala firme é do Você")
    no_fim = pagina.evaluate("""() => { const c = document.querySelector('.aovivo__corpo');
        return c.scrollHeight - c.scrollTop - c.clientHeight < 4; }""")
    conferir(no_fim, "a rolagem segue o fim")


prova_legenda_quebra_na_pausa.antes = GRAVANDO


def texto(pagina, seletor: str) -> str:
    return (pagina.text_content(seletor) or "").strip()


def prova_antes_heroi(pagina) -> None:
    conferir(texto(pagina, "#subtitulo") == "pronto para gravar", "o subtítulo diz \"pronto para gravar\"")
    rotulo = texto(pagina, ".grav-heroi__rotulo")
    conferir(rotulo.startswith("Próxima na agenda · começa em") and "min" in rotulo,
             f"o herói diz quando a próxima começa ({rotulo!r})")
    conferir(texto(pagina, ".grav-heroi__titulo") == "Comunicação Beegol + App", "com o título da reunião")
    fatos = texto(pagina, ".grav-heroi__fatos")
    conferir("7 convidados · organizada por algar.com.br" in fatos, f"horário, convidados e organizador ({fatos!r})")
    conferir(texto(pagina, ".grav-heroi .grav-vinculo") == "Algar › Agentes"
             and "o mesmo da última reunião com este título" in texto(pagina, ".grav-heroi__vinculo"),
             "sugere o cliente › projeto da última reunião com este título")
    conferir(pagina.is_visible("text=Gravar esta reunião") and pagina.is_visible("text=Gravar sem reunião da agenda"),
             "os dois botões de gravar")
    vai = pagina.eval_on_selector_all(".grav-vai__valor", "els => els.map((e) => e.textContent)")
    conferir(vai == ["Headset AN01 Hands-Free", "Alto-falantes (Realtek Audio)", "andre@beegol.com"],
             f"\"Vai gravar\" diz microfone, áudio da reunião e a conta ({vai})")
    conferir(pagina.is_visible("text=Trocar em Ajustes › Gravação"), "com o link para Ajustes")
    conferir(pagina.evaluate(SEM_ROLAGEM_LATERAL), "sem rolagem lateral")


def prova_antes_agenda(pagina) -> None:
    fins = pagina.eval_on_selector_all(".grav-agenda__linha",
        "els => els.map((e) => e.lastElementChild.textContent.trim())")
    conferir(fins == ["gravada", "gravada", "a próxima", "Gravar esta"],
             f"hoje na agenda: gravada, gravada, a próxima, Gravar esta ({fins})")
    conferir("três horas" in texto(pagina, ".grav-agenda__dica"), "com a dica das três horas")
    pagina.click(".grav-agenda__linha[data-evento='e4'] >> text=Gravar esta")
    pagina.wait_for_selector(".grav-gravando:not([hidden])", timeout=3000)
    ops = [q["op"] for q in pagina.evaluate("() => window.__pedidos")
           if q["op"] in ("fixar-evento", "gravar")]
    fix = pedidos(pagina, "fixar-evento")
    conferir(ops == ["fixar-evento", "gravar"] and fix[0]["evento"] == "e4",
             f"\"Gravar esta\" numa linha fixa a reunião e grava ({ops})")
    conferir(not pedidos(pagina, "salvar-reuniao"),
             "e não leva a sugestão do herói, que era de outra reunião")


def prova_antes_gravar_esta(pagina) -> None:
    pagina.click("text=Gravar esta reunião")
    pagina.wait_for_selector(".grav-gravando:not([hidden])", timeout=3000)
    pagina.wait_for_timeout(300)
    fix = pedidos(pagina, "fixar-evento")
    conferir(fix and fix[0]["evento"] == "e3", "Gravar esta reunião fixa a do herói")
    salvos = pedidos(pagina, "salvar-reuniao")
    conferir(len(salvos) == 1 and salvos[0]["cliente"] == "Algar" and salvos[0]["projeto"] == "Agentes",
             f"e grava a sugestão na gravação nova, uma vez só ({len(salvos)})")
    pagina.evaluate("() => { for (let i = 0; i < 10; i++) window.__empurrar({ duracao_s: 3 + i }); }")
    pagina.wait_for_timeout(200)
    conferir(len(pedidos(pagina, "salvar-reuniao")) == 1, "cinco estados por segundo não a gravam de novo")
    conferir(texto(pagina, ".grav-faixa .grav-vinculo") == "Algar › Agentes", "a faixa mostra o par")


def prova_antes_sem_agenda(pagina) -> None:
    conferir(not pagina.is_visible(".grav-agenda"), "sem credencial do Google, a agenda some")
    conferir(texto(pagina, ".grav-heroi__titulo") == "Pronto para gravar", "o herói diz que está pronto")
    conferir(pagina.get_by_role("button", name="Gravar", exact=True).is_visible()
             and not pagina.is_visible("text=Gravar esta reunião"),
             "com um botão só: Gravar")
    pagina.get_by_role("button", name="Gravar", exact=True).click()
    pagina.wait_for_selector(".grav-gravando:not([hidden])", timeout=3000)
    conferir(True, "e ele grava")


prova_antes_sem_agenda.antes = """
window.__proximas = { status: "nao_configurado", eventos: [] };
window.__estado.agenda_configurada = false; window.__estado.conta = null;
"""


def prova_antes_ultima_gravacao(pagina) -> None:
    conferir(texto(pagina, ".grav-ultima__titulo") == "Reunião de lideranças", "a última gravação, pelo título")
    fatos = texto(pagina, ".grav-ultima__fatos")
    conferir(fatos == "hoje, 11:02 · 37 min · Beegol (interno) › Gestão", f"com quando, duração e cliente ({fatos!r})")
    conferir(texto(pagina, ".grav-ultima__rotulo").startswith("Transcrev") and texto(pagina, ".grav-ultima__pct") == "46%"
             and pagina.is_visible(".grav-ultima__barra"), "transcrevendo, com a porcentagem e a barra")
    pagina.click(".grav-ultima >> text=Abrir reunião")
    pagina.wait_for_function("document.getElementById('titulo').textContent === 'Reunião de lideranças'", timeout=3000)
    conferir(True, "Abrir reunião abre a reunião")


prova_antes_ultima_gravacao.antes = None  # preenchido abaixo, depois de TRANSCREVENDO


def prova_antes_transcrever(pagina) -> None:
    conferir(texto(pagina, ".grav-ultima .aa-btn") == "Transcrever",
             "a última, só gravada, oferece Transcrever sem ir a Reuniões")
    conferir(texto(pagina, ".grav-ultima__rotulo") == "Não transcrita", "e diz o estado")


def prova_resto_embaixo(pagina) -> None:
    ordem = pagina.evaluate("""() => [...document.querySelectorAll('.gravador-tela > *')]
        .filter((e) => !e.hidden).map((e) => e.className)""")
    conferir(ordem == ["grav-antes", "grav-resto"], f"o resto vem abaixo do que a prancha mostra ({ordem})")
    titulos = pagina.eval_on_selector_all(".grav-resto .bloco__titulo", "els => els.map((e) => e.textContent)")
    conferir("Dispositivos" in titulos and "Pasta das gravações" in titulos,
             f"dispositivos e pasta continuam no Gravador ({titulos})")


def prova_gravando_faixa_e_grade(pagina) -> None:
    conferir(texto(pagina, "#subtitulo").startswith("gravando desde"), "o subtítulo diz desde quando grava")
    conferir(texto(pagina, "#acoes-da-barra") == "Placa: legenda ao vivo", "e o selo da placa na barra")
    conferir(texto(pagina, ".grav-faixa__tempo") == "00:31:24", "a faixa tem o relógio")
    conferir(texto(pagina, ".grav-faixa__titulo") == "Comunicação Beegol + App"
             and texto(pagina, ".grav-faixa__convidados") == "3 convidados", "o título e os convidados")
    pagina.wait_for_timeout(200)
    conferir(texto(pagina, ".grav-faixa .grav-vinculo") == "Algar › Agentes", "o cliente › projeto da gravação")
    rotulos = pagina.eval_on_selector_all(".grav-faixa .faixa", "els => els.map((e) => [e.textContent, e.title])")
    conferir(rotulos == [["Seu microfone", "Headset AN01 Hands-Free"], ["Áudio da reunião", "Alto-falantes (Realtek Audio)"]],
             f"os medidores, com o dispositivo no title ({rotulos})")
    botoes = pagina.eval_on_selector_all(".grav-faixa__acoes button", "els => els.map((e) => e.textContent)")
    conferir(botoes == ["Marcar momento", "Mutar", "Parar"], f"Marcar momento · Mutar · Parar ({botoes})")
    caixas = pagina.evaluate("""() => ['.grav-grade > .aovivo', '.grav-grade > .grav-abas'].map((s) => {
        const r = document.querySelector(s).getBoundingClientRect(); return [Math.round(r.left), Math.round(r.width)]; })""")
    conferir(caixas[0][0] < caixas[1][0] and caixas[0][1] > caixas[1][1] * 1.5,
             f"a legenda larga à esquerda, as abas à direita ({caixas})")
    abas = pagina.eval_on_selector_all("[role='tab']", "els => els.map((e) => [e.textContent, e.getAttribute('aria-selected')])")
    conferir(abas == [["Notas", "true"], ["Perguntar", "false"]], f"Notas e Perguntar em abas ({abas})")
    pagina.focus("#grav-aba-notas")
    pagina.keyboard.press("ArrowRight")
    conferir(pagina.is_visible(".perguntar") and not pagina.is_visible(".notas__texto")
             and pagina.evaluate("() => document.activeElement.id") == "grav-aba-perguntar",
             "a seta troca de aba e leva o foco junto")
    conferir(pagina.evaluate(SEM_ROLAGEM_LATERAL), "sem rolagem lateral")


def prova_gravando_nao_rouba_foco(pagina) -> None:
    pagina.click(".notas__texto")
    pagina.keyboard.type("Definir o tom")
    antes = pagina.evaluate("() => document.querySelector('.grav-faixa').outerHTML.length")
    pagina.evaluate("() => { for (let i = 0; i < 20; i++) window.__empurrar({ duracao_s: 1890 + i }); }")
    pagina.keyboard.type(" da comunicação")
    conferir(pagina.evaluate("() => document.activeElement.className").find("notas__texto") >= 0
             and pagina.input_value(".notas__texto").endswith("Definir o tom da comunicação"),
             "vinte estados seguidos não tiram o cursor das notas")
    conferir(texto(pagina, ".grav-faixa__tempo") == "00:31:49", "e o relógio andou")


def prova_gravando_marcar_momento(pagina) -> None:
    legenda(pagina, 1838, " Precisamos alinhar o tom de comunicação", "", True)
    pagina.click("#grav-aba-perguntar")
    pagina.fill(".perguntar__caixa .aa-entrada", "quem ficou")
    pagina.click(".grav-faixa__acoes >> text=Marcar momento")
    conferir("[00:30:38]" in pagina.input_value(".notas__texto"),
             "Marcar momento escreve nas notas com a aba Perguntar ativa")
    conferir(pagina.evaluate("() => document.activeElement.textContent") == "Marcar momento",
             "sem levar o foco ao campo de notas escondido")
    conferir(pagina.input_value(".perguntar__caixa .aa-entrada") == "quem ficou", "e a pergunta continua lá")
    conferir(texto(pagina, ".aovivo .fala .fala__momento") == "momento marcado", "e o momento aparece no trecho")


for _p in (prova_gravando_faixa_e_grade, prova_gravando_nao_rouba_foco, prova_gravando_marcar_momento):
    _p.antes = GRAVANDO

def prova_mudo_na_faixa(pagina) -> None:
    conferir(not pagina.is_visible(".grav-avisos .aa-alerta"), "sem mudo, sem aviso")
    pagina.click(".grav-faixa__acoes >> text=Mutar")
    pagina.wait_for_selector(".grav-avisos [data-aviso='mudo']", timeout=2000)
    conferir(pagina.get_attribute(".grav-faixa", "data-mudo") == "true", "mudo, a faixa se marca")
    borda = pagina.evaluate("""() => [getComputedStyle(document.querySelector('.grav-faixa')).borderTopColor,
        getComputedStyle(document.querySelector('.aa-alerta--erro, .grav-avisos .aa-alerta')).borderTopColor]""")
    conferir(borda[0] != pagina.evaluate("() => getComputedStyle(document.querySelector('.grav-abas')).borderTopColor"),
             f"com a borda de erro ({borda[0]})")
    conferir(texto(pagina, ".grav-avisos [data-aviso='mudo']").startswith("Microfone mudo. Sua voz não está sendo gravada."),
             "o aviso entra já no primeiro segundo")
    irmao = pagina.evaluate("() => document.querySelector('.grav-faixa').nextElementSibling.className")
    conferir(irmao == "grav-avisos", f"logo abaixo da faixa ({irmao})")
    conferir(texto(pagina, ".grav-faixa__acoes button:nth-child(2)") == "Desmutar", "o botão da faixa vira Desmutar")
    pagina.evaluate("() => { window.__desmutar = document.querySelector('.grav-desmutar'); }")
    pagina.evaluate("() => { for (let i = 0; i < 10; i++) window.__empurrar({ mudo_ha_s: 5 + i }); }")
    conferir(pagina.evaluate("() => window.__desmutar === document.querySelector('.grav-desmutar')"),
             "o Desmutar do aviso sobrevive a cinco estados por segundo")
    pagina.evaluate("() => window.__empurrar({ mudo_ha_s: 75 })")
    conferir("há 1 min" in texto(pagina, ".grav-avisos [data-aviso='mudo']"), "aos 60 s, a frase dos minutos")
    pagina.click(".grav-desmutar")
    pagina.wait_for_timeout(100)
    conferir(len(pedidos(pagina, "mutar")) == 2 and not pagina.is_visible(".grav-avisos .aa-alerta"),
             "Desmutar manda mutar, e o aviso some")


def prova_dispositivo_caiu(pagina) -> None:
    pagina.evaluate("""() => { const f = JSON.parse(JSON.stringify(window.__estado.faixas));
        f[1].desconectado = true; window.__empurrar({ faixas: f }); }""")
    conferir("O dispositivo de áudio da reunião caiu." == texto(pagina, ".grav-avisos [data-aviso='caiu']"),
             "o dispositivo que caiu aparece no mesmo lugar")


for _p in (prova_mudo_na_faixa, prova_dispositivo_caiu):
    _p.antes = GRAVANDO

def prova_vinculo_durante_a_gravacao(pagina) -> None:
    pagina.wait_for_timeout(200)
    pagina.click(".grav-faixa .grav-vinculo")
    pagina.wait_for_selector(".popover .grav-vinculo__form", timeout=2000)
    conferir(pagina.input_value("#grav-faixa-cliente") == "Algar" and pagina.input_value("#grav-faixa-projeto") == "Agentes",
             "o editor abre com o par gravado")
    pagina.fill("#grav-faixa-projeto", "Agente de Crédito")
    pagina.click(".popover >> text=Usar")
    pagina.wait_for_timeout(100)
    salvos = pedidos(pagina, "salvar-reuniao")
    conferir(len(salvos) == 1 and salvos[0]["gravacao"].endswith("hoje_14-00-00")
             and salvos[0]["projeto"] == "Agente de Crédito",
             f"trocar o projeto gravando manda salvar-reuniao com a gravação corrente ({salvos})")
    conferir(texto(pagina, ".grav-faixa .grav-vinculo") == "Algar › Agente de Crédito", "a faixa mostra o novo par")
    pagina.evaluate("() => window.__empurrar({ duracao_s: 1900 })")
    conferir(texto(pagina, ".grav-faixa__tempo") == "00:31:40" and not pedidos(pagina, "parar-gravacao"),
             "e a gravação segue")
    pagina.click(".grav-faixa__acoes >> text=Parar")
    pagina.wait_for_selector(".grav-antes:not([hidden])", timeout=2000)
    conferir(len(pedidos(pagina, "salvar-reuniao")) == 1, "parar não escreve o vínculo de novo")


prova_vinculo_durante_a_gravacao.antes = GRAVANDO

def prova_sem_legenda(pagina) -> None:
    pagina.wait_for_timeout(200)
    conferir(not pagina.is_visible(".grav-grade > .aovivo"), "sem legenda, nenhuma coluna de legenda vazia")
    conferir(pagina.is_visible(".grav-sem-legenda") and "placa de vídeo" in texto(pagina, ".grav-sem-legenda")
             and "Por que não há: nem a legenda" in texto(pagina, ".grav-sem-legenda"),
             "um cartão diz o que a legenda faria, quanto custa e por que não há")
    caixas = pagina.evaluate("""() => ['.grav-grade > .grav-abas', '.grav-grade > .grav-sem-legenda'].map((s) => {
        const r = document.querySelector(s).getBoundingClientRect(); return [Math.round(r.left), Math.round(r.width)]; })""")
    conferir(caixas[0][0] < caixas[1][0] and caixas[0][1] > caixas[1][1], f"as notas ficam largas, à esquerda ({caixas})")
    conferir(texto(pagina, "#acoes-da-barra") == "", "e o selo da placa some")


prova_sem_legenda.antes = GRAVANDO + """
window.__aovivo = { aovivo_modo: "nada", aovivo_ate: [], perguntar_impedimento: "desligado",
  aovivo_impedimento: "nem a legenda nem a prévia em blocos estão ligadas em Ajustes › Transcrição." };
"""

PROVAS = [prova_monta, prova_sem_legenda, prova_vinculo_durante_a_gravacao, prova_mudo_na_faixa, prova_dispositivo_caiu, prova_gravando_faixa_e_grade, prova_gravando_nao_rouba_foco,
          prova_gravando_marcar_momento, prova_legenda_quebra_na_pausa, prova_antes_heroi, prova_antes_agenda,
          prova_antes_gravar_esta, prova_antes_sem_agenda, prova_antes_ultima_gravacao,
          prova_antes_transcrever, prova_resto_embaixo]


# A cena da prancha grav-gravando.png, para a foto.
CENA = [
    (1752, " A gente pode usar o mesmo padrão do app de cobrança, que o jurídico já aprovou.", False),
    (1780, " Pode ser, mas lá o público é outro. Aqui o agente fala direto com o cliente final.", True),
    (1838, " Precisamos alinhar o tom de comunicação antes de seguir para o próximo desenho.", True),
    (1851, " eu vou consolidar as referências e compartilhar uma proposta ainda esta semana", False),
    (1862, " aí a gente valida com o Rafael antes de fechar", False),
    (1870, " concordo e o jurídico precisa ver os textos de cobrança antes", False),
]
TRANSCREVENDO = """
window.__transcricoes = { atual: { gravacao: window.__gravacoes[0].caminho, tarefa: "transcricao",
  nome: "Reunião de lideranças", etapa: "asr", fracao: 0.46, texto: "transcrevendo" }, ultimo: null };
"""


prova_antes_ultima_gravacao.antes = TRANSCREVENDO


def cena_gravando(pagina) -> None:
    for t, novo, dono in CENA:
        legenda(pagina, t, novo, "", dono)
        if t == 1838:
            pagina.click(".grav-faixa__acoes >> text=Marcar momento")
    legenda(pagina, 1884, "", "Também vale validar quais mensagens funcionam melhor com o cliente e aí a gente fecha…", True)
    pagina.evaluate("() => window.__empurrar({ duracao_s: 1884 })")
    pagina.fill(".notas__texto", "Definir o tom da comunicação com o cliente antes do novo desenho.\n\n"
                "[00:30:38] Carol compartilha as referências até sexta.\n\nJurídico revisa os textos de cobrança.")
    pagina.locator(".notas__texto").blur()
    pagina.wait_for_timeout(1000)


def fotografar(navegador, porta: int, pasta: Path) -> None:
    pasta.mkdir(parents=True, exist_ok=True)
    for tema in ["escuro", "claro"]:
        for largura, altura in [(1280, 800), (900, 800)]:
            sufixo = f"{tema}" + ("" if largura == 1280 else f"-{largura}")
            pagina, _ = abrir(navegador, porta, largura, altura, tema=tema, antes=TRANSCREVENDO)
            pagina.screenshot(path=str(pasta / f"gravador-antes-{sufixo}.png"))
            pagina.close()
            pagina, _ = abrir(navegador, porta, largura, altura, tema=tema, antes=GRAVANDO)
            cena_gravando(pagina)
            pagina.screenshot(path=str(pasta / f"gravador-gravando-{sufixo}.png"))
            pagina.close()


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--fotos", type=Path, help="pasta onde deixar as fotos das telas")
    ap.add_argument("--so", help="só estas provas, separadas por vírgula")
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
        print("falta o playwright: uv run --with playwright python tools/provar_gravador.py", file=sys.stderr)
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
                pagina, erros = abrir(navegador, porta, largura, altura,
                                      antes=getattr(prova, "antes", None),
                                      hash_=getattr(prova, "hash", "gravador"))
                try:
                    prova(pagina)
                except Exception as e:  # noqa: BLE001 — qualquer estouro é falha da prova
                    conferir(False, f"{prova.__name__} estourou: {str(e).splitlines()[0]}")
                conferir(not erros, f"sem erro de JavaScript {erros[:2] if erros else ''}")
                pagina.close()
            if args.fotos:
                fotografar(navegador, porta, args.fotos)
            navegador.close()
        srv.shutdown()

    if falhas:
        print(f"\n{len(falhas)} falha(s).", file=sys.stderr)
        return 1
    print("\ntudo certo.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
