"""Exercita, num navegador real, a transcrição que sobrevive a trocar de tela.

Existe porque os critérios A, B e C da Fase 3 são sobre *comportamento da
página*, e nenhum teste de unidade os alcança: o registro em C# tem os seus
(``RegistroDeTranscricoesTests``), mas a pergunta "sair da tela no meio e voltar
mostra a mesma barra?" só o DOM responde. Foi exatamente aí que estava o defeito
que a fase conserta — o progresso vivia numa closure sobre nós que o clique no
trilho jogava fora.

Sobe a página com a **mesma ponte falsa** da ideia do ``medir_layout.py``, só
que esta sabe transcrever: aceita ``transcrever``, empurra eventos ``id: 0`` com
andamento, e recusa a segunda transcrição como o núcleo recusa.

Uso:
    .venv/bin/python tools/checar_transcricao.py

Requer o Chromium do Playwright (ver tools/ui_check.py para a instalação).
"""

from __future__ import annotations

import socketserver
import sys
import threading
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from medir_layout import Servidor  # noqa: E402  (o servidor é o mesmo do outro tool)

# A ponte falsa que transcreve.
#
# O andamento não corre sozinho: quem manda o tempo passar é o teste, por
# ``window.__avancar()``. Um cronômetro de verdade tornaria o resultado
# dependente de quanto o navegador demorou para pintar.
PONTE_FALSA = """
window.chrome = { webview: {
  _ouvintes: [],
  addEventListener(_, f) { this._ouvintes.push(f); },
  _emitir(r) { for (const f of this._ouvintes) f({ data: JSON.stringify(r) }); },
  postMessage(cru) {
    const p = JSON.parse(cru);
    const r = { id: p.id };
    const w = window.chrome.webview;

    // "transcrita" sai do disco no app de verdade — o núcleo olha se existe
    // transcricao.json. Aqui sai do conjunto que o __terminar alimenta, para a
    // lista mudar de etiqueta pelo mesmo motivo que muda lá.
    if (p.op === 'gravacoes') r.gravacoes = [
      { nome: '2026-08-12_09-00-00', caminho: 'C:/g/a', duracao_s: 3600,
        titulo: 'Comitê de dados', convidados: 4,
        transcrita: w._transcritas.has('C:/g/a'),
        cliente: (w._vinculos['C:/g/a'] ?? {}).cliente ?? null,
        projeto: (w._vinculos['C:/g/a'] ?? {}).projeto ?? null, avisos: [] },
      { nome: '2026-08-11_14-00-00', caminho: 'C:/g/b', duracao_s: 1800,
        titulo: 'Sprint', convidados: 3,
        transcrita: w._transcritas.has('C:/g/b'),
        cliente: (w._vinculos['C:/g/b'] ?? {}).cliente ?? null,
        projeto: (w._vinculos['C:/g/b'] ?? {}).projeto ?? null, avisos: [] },
      // A terceira existe para o teste de parar: no fim do roteiro as duas
      // primeiras já foram transcritas, e reunião transcrita abre na revisão.
      { nome: '2026-08-10_09-00-00', caminho: 'C:/g/c', duracao_s: 2400,
        titulo: 'Kickoff', convidados: 5,
        transcrita: w._transcritas.has('C:/g/c'),
        cliente: null, projeto: null, avisos: [] },
    ];
    // O Gravador entra porque é o destino usado para *sair* de Reuniões: o
    // critério A é justamente trocar de destino e voltar.
    else if (p.op === 'gravador') r.gravador = w._gravador;
    else if (p.op === 'dispositivos') {
      r.gravador = w._gravador;
      r.dispositivos = { entradas: [], saidas: [], mic_id: null, loopback_id: null };
    }
    else if (p.op === 'clientes') r.clientes = { 'Cliente 1': ['Projeto A'] };
    else if (p.op === 'prefs') r.prefs = { language: 'pt', model_size: 'large-v3' };
    else if (p.op === 'config') r.config = {};
    else if (p.op === 'salvar-projeto') r.clientes = {};
    else if (p.op === 'transcricao') r.transcricao = JSON.stringify(
      { language: 'pt', duration: 12,
        segments: [{ start: 0, end: 2, text: ' bom dia', speaker: 'You' }] });
    else if (p.op === 'transcricoes') r.transcricoes = w._registro;
    // O vínculo da reunião, que no app mora em reuniao.json na pasta da
    // gravação. Aqui um dicionário em memória basta: o que se testa é que a
    // tela grava ao escolher e lê ao montar.
    else if (p.op === 'reuniao') {
      const v = w._vinculos[p.gravacao] ?? {};
      r.cliente = v.cliente ?? null;
      r.projeto = v.projeto ?? null;
    }
    else if (p.op === 'modelos-de-ata') r.tipos = [
      { id: 'cliente-update', nome: 'Update com cliente', do_usuario: false },
      { id: 'sprint', nome: 'Sprint', do_usuario: false },
    ];
    else if (p.op === 'ata') {
      r.ata = w._atas[p.gravacao] ?? null;
      r.ata_velha = false;
    }
    else if (p.op === 'gerar-ata') {
      if (w._registro.atual) {
        r.erro = 'já estou escrevendo a ata de "' + w._registro.atual.nome + '".';
      } else {
        w._registro = { atual: {
          gravacao: p.gravacao, nome: 'Comitê de dados', etapa: 'modelo',
          fracao: 0.05, texto: 'carregando o modelo',
          comecou_em: '2026-08-14T10:00:00Z', terminou: false,
        }, ultimo: null };
        r.transcricoes = w._registro;
        setTimeout(() => w._emitir(
          { id: 0, tipo: 'transcricoes', transcricoes: w._registro }), 0);
      }
    }
    else if (p.op === 'notas') {
      r.notas = w._notas[p.gravacao] ?? '';
      r.termos = w.__termos(r.notas);
    }
    else if (p.op === 'salvar-notas') {
      w._notas[p.gravacao] = p.conteudo;
      r.termos = w.__termos(p.conteudo ?? '');
    }
    else if (p.op === 'salvar-reuniao') {
      w._vinculos[p.gravacao] = { cliente: p.cliente, projeto: p.projeto };
    }
    else if (p.op === 'cancelar-transcricao') {
      const t = w._registro.atual;
      if (t) {
        Object.assign(t, { terminou: true, cancelada: true });
        w._registro = { atual: null, ultimo: t };
      }
      r.transcricoes = w._registro;
      setTimeout(() => w._emitir(
        { id: 0, tipo: 'transcricoes', transcricoes: w._registro }), 0);
    }
    else if (p.op === 'esquecer-transcricao') {
      w._registro = { atual: w._registro.atual, ultimo: null };
      r.transcricoes = w._registro;
    }
    else if (p.op === 'transcrever') {
      if (w._registro.atual) {
        // A mesma frase do núcleo: recusar sem nomear manda o usuário procurar
        // sozinho qual reunião está ocupando a placa.
        r.erro = 'já estou transcrevendo "' + w._registro.atual.nome + '". '
               + 'Uma de cada vez: as duas disputariam a mesma placa de vídeo.';
      } else {
        w._registro = { atual: {
          gravacao: p.gravacao,
          nome: { 'C:/g/a': 'Comitê de dados', 'C:/g/b': 'Sprint' }[p.gravacao] ?? 'Kickoff',
          etapa: 'mix', fracao: 0.05, texto: 'somando',
          comecou_em: '2026-08-13T10:00:00Z', terminou: false,
        }, ultimo: null };
        r.transcricoes = w._registro;
        setTimeout(() => w._emitir(
          { id: 0, tipo: 'transcricoes', transcricoes: w._registro }), 0);
      }
    }

    setTimeout(() => w._emitir(r), 0);
  },
} };

window.chrome.webview._registro = { atual: null, ultimo: null };
window.chrome.webview._transcritas = new Set();
window.chrome.webview._vinculos = {};
window.chrome.webview._notas = {};
window.chrome.webview._atas = {};

/** A ata ficando pronta, empurrada pelo teste. */
window.__ataPronta = (caminho, markdown) => {
  const w = window.chrome.webview;
  w._atas[caminho] = markdown;
  const t = w._registro.atual;
  if (t) {
    Object.assign(t, { terminou: true, erro: null, fracao: 1 });
    w._registro = { atual: null, ultimo: t };
  }
  w._emitir({ id: 0, tipo: 'transcricoes', transcricoes: w._registro });
};

/* O mesmo critério grosseiro do Notas.TermosSugeridos em C#: sigla ou palavra
   capitalizada que não abre a frase. Aqui só precisa ser bom o bastante para a
   tela ter o que mostrar — quem tem a regra de verdade é o núcleo, e ela tem
   os testes dela.

   Sem expressão regular de propósito: esta string atravessa o Python antes de
   virar JS, e uma barra invertida aqui vira sequência de escape lá. A primeira
   versão usava /[s,;()]/ e o navegador recusou o script inteiro — a página
   subiu sem ponte e o teste morreu esperando um seletor. */
window.chrome.webview.__termos = (texto) => {
  const QUEBRA = String.fromCharCode(10);
  const SEPARADORES = ' ,;()[]"';
  const MARCAS = '-*# ';
  const FINAIS = '.:!?…';
  const achados = [];

  for (const linha of (texto || '').split(QUEBRA)) {
    let limpa = linha;
    while (limpa.length > 0 && MARCAS.includes(limpa[0])) limpa = limpa.slice(1);

    const palavras = [...limpa]
      .map((c) => (SEPARADORES.includes(c) ? ' ' : c))
      .join('')
      .split(' ')
      .filter(Boolean);

    palavras.forEach((cru, i) => {
      let p = cru;
      while (p.length > 0 && FINAIS.includes(p[p.length - 1])) p = p.slice(0, -1);
      if (p.length < 3) return;
      if (p[0].toLowerCase() === p[0].toUpperCase()) return;   // não começa com letra

      const sigla = p === p.toUpperCase();
      const nomeNoMeio = i > 0 && p[0] === p[0].toUpperCase();
      if ((sigla || nomeNoMeio) && !achados.includes(p)) achados.push(p);
    });
  }
  return achados;
};

window.chrome.webview._gravador = {
  gravando: false, mudo: false, mudo_ha_s: 0, cor: 'cinza', status: 'Parado',
  duracao_s: 0, pasta: 'C:/g', gravacao: null, notificacoes: true,
  usar_agenda: false, agenda_configurada: false, faixas: [],
};

/** Começa ou para a gravação, do lado do dublê. */
window.__gravar = (ligado) => {
  const w = window.chrome.webview;
  Object.assign(w._gravador, {
    gravando: ligado, duracao_s: ligado ? 754 : 0,
    gravacao: ligado ? 'C:/g/agora' : null,
    status: ligado ? 'Gravando 00:12:34' : 'Parado',
    cor: ligado ? 'vermelho' : 'cinza',
    faixas: ligado ? [
      { nome: 'mic', dispositivo: 'Headset', nivel: 0.05, ja_ouviu: true,
        mudo: false, silencio_s: 0, desconectado: false, falha: null },
      { nome: 'system', dispositivo: 'Alto-falantes', nivel: 0.2, ja_ouviu: true,
        mudo: false, silencio_s: 0, desconectado: false, falha: null },
    ] : [],
  });
  w._emitir({ id: 0, tipo: 'gravador', gravador: w._gravador });
};

/** O pipeline andando um passo, empurrado pelo teste. */
window.__avancar = (etapa, fracao, texto) => {
  const w = window.chrome.webview;
  Object.assign(w._registro.atual, { etapa, fracao, texto });
  w._emitir({ id: 0, tipo: 'transcricoes', transcricoes: w._registro });
};

/** O fim, bem ou mal. */
window.__terminar = (erro) => {
  const w = window.chrome.webview;
  const t = w._registro.atual;
  Object.assign(t, { terminou: true, erro: erro ?? null, fracao: erro ? t.fracao : 1 });
  if (!erro) w._transcritas.add(t.gravacao);
  w._registro = { atual: null, ultimo: t };
  w._emitir({ id: 0, tipo: 'transcricoes', transcricoes: w._registro });
};
"""

FALHAS: list[str] = []


def conferir(nome: str, condicao: bool, detalhe: str = "") -> None:
    print(f'{"ok  " if condicao else "FALHA"} {nome}' + (f"  ({detalhe})" if detalhe else ""))
    if not condicao:
        FALHAS.append(nome)


def main() -> int:
    try:
        from playwright.sync_api import sync_playwright
    except ImportError:
        print("falta o playwright: uv pip install playwright "
              "&& python -m playwright install chromium", file=sys.stderr)
        return 2

    socketserver.TCPServer.allow_reuse_address = True
    with socketserver.TCPServer(("127.0.0.1", 0), Servidor) as srv:
        porta = srv.server_address[1]
        threading.Thread(target=srv.serve_forever, daemon=True).start()

        with sync_playwright() as pw:
            navegador = pw.chromium.launch()
            pagina = navegador.new_page(viewport={"width": 1280, "height": 800})
            # Um erro de JS não derruba a página: ele some no console e o teste
            # morre depois, esperando um seletor que nunca aparece. Repetir aqui
            # troca "timeout" por "TypeError na linha tal".
            pagina.on("pageerror", lambda e: print(f"  js: {e}", file=sys.stderr))
            pagina.add_init_script(PONTE_FALSA)
            pagina.goto(f"http://127.0.0.1:{porta}/index.html", wait_until="load")
            pagina.wait_for_selector(".gravacao")

            ponto = "#ir-reunioes"
            estado = lambda: pagina.get_attribute(ponto, "data-ocupado")  # noqa: E731

            conferir("a bolinha começa apagada", estado() == "false")

            # ---- o vínculo com cliente/projeto, que é anterior a transcrever
            pagina.click('[data-gravacao="C:/g/a"]')
            pagina.wait_for_selector("#vocabulario")
            pagina.fill("#cliente", "Vivo")
            pagina.dispatch_event("#cliente", "change")
            pagina.fill("#projeto", "Faturamento B2B")
            pagina.dispatch_event("#projeto", "change")
            pagina.wait_for_timeout(80)

            pagina.click("#ir-reunioes")
            pagina.wait_for_selector(".gravacao")
            meta = pagina.inner_text('[data-gravacao="C:/g/a"] .gravacao__meta')
            conferir("o cartão mostra cliente e projeto", "Vivo · Faturamento B2B" in meta,
                     meta.replace("\n", " | "))

            pagina.click('[data-gravacao="C:/g/a"]')
            pagina.wait_for_selector("#vocabulario")
            voltou = pagina.input_value("#cliente"), pagina.input_value("#projeto")
            conferir("sair da tela e voltar não apaga cliente/projeto",
                     voltou == ("Vivo", "Faturamento B2B"), str(voltou))

            # ---- começa a transcrição da primeira reunião
            pagina.click("text=Transcrever")
            pagina.wait_for_selector(".aa-progresso")

            conferir("critério B: a bolinha acende ao começar", estado() == "true")

            pagina.evaluate("window.__avancar('asr', 0.42, 'minuto 12 de 60')")
            pagina.wait_for_timeout(50)
            texto_no_meio = pagina.inner_text(".progresso__linha .campo__dica")
            # A largura pelo style inline, e não pelo computado: a barra tem
            # transição, e medir em pixels no meio dela compara animação, não
            # estado.
            largura_no_meio = pagina.evaluate(
                "document.querySelector('.aa-progresso div').style.width")
            conferir("a etapa aparece em português", "Transcrevendo: minuto 12 de 60" in texto_no_meio,
                     texto_no_meio)

            # ---- critério A: sair e voltar
            pagina.click("#ir-gravador")
            pagina.wait_for_selector(".gravador")
            conferir("a bolinha continua acesa fora de Reuniões", estado() == "true")

            pagina.click("#ir-reunioes")
            pagina.wait_for_selector(".gravacao")
            etiqueta = pagina.inner_text('[data-gravacao="C:/g/a"] .aa-etiqueta')
            conferir("a lista diz que está transcrevendo", etiqueta == "Transcrevendo…", etiqueta)

            pagina.click('[data-gravacao="C:/g/a"]')
            pagina.wait_for_selector(".aa-progresso")
            texto_de_volta = pagina.inner_text(".progresso__linha .campo__dica")
            largura_de_volta = pagina.evaluate(
                "document.querySelector('.aa-progresso div').style.width")
            conferir("critério A: volta na mesma etapa", texto_de_volta == texto_no_meio,
                     texto_de_volta)
            conferir("critério A: volta na mesma fração", largura_de_volta == largura_no_meio,
                     f"{largura_no_meio} → {largura_de_volta}")

            # ---- critério C: a segunda é recusada
            pagina.click("#voltar")
            pagina.wait_for_selector(".gravacao")
            pagina.click('[data-gravacao="C:/g/b"]')
            pagina.wait_for_selector("#vocabulario")
            pagina.click("text=Transcrever")
            pagina.wait_for_selector(".aa-alerta, .alerta")
            recusa = pagina.inner_text(".aa-pagina")
            conferir("critério C: a recusa nomeia a reunião ocupada",
                     "Comitê de dados" in recusa)

            # ---- o fim chega com a tela em outro lugar
            pagina.click("#ir-gravador")
            pagina.wait_for_selector(".gravador")
            pagina.evaluate("window.__terminar()")
            pagina.wait_for_timeout(50)
            conferir("critério B: a bolinha apaga ao terminar", estado() == "false")

            pagina.click("#ir-reunioes")
            pagina.wait_for_selector(".gravacao")
            pagina.click('[data-gravacao="C:/g/a"]')
            pagina.wait_for_selector(".revisao", timeout=5000)
            conferir("a reunião pronta abre na revisão",
                     pagina.locator(".revisao .trecho").count() > 0)

            # ---- o erro sobrevive a ninguém estar olhando
            pagina.click("#ir-reunioes")
            pagina.wait_for_selector(".gravacao")
            pagina.click('[data-gravacao="C:/g/b"]')
            pagina.wait_for_selector("#vocabulario")
            pagina.click("text=Transcrever")
            pagina.wait_for_selector(".aa-progresso")
            pagina.click("#ir-gravador")
            pagina.wait_for_selector(".gravador")
            pagina.evaluate("window.__terminar('o motor de ASR não respondeu')")
            pagina.wait_for_timeout(50)
            conferir("critério B: a bolinha apaga também quando falha", estado() == "false")

            pagina.click("#ir-reunioes")
            pagina.wait_for_selector(".gravacao")
            pagina.click('[data-gravacao="C:/g/b"]')
            pagina.wait_for_selector("#vocabulario")
            pagina.wait_for_timeout(100)
            texto = pagina.inner_text(".aa-pagina")
            conferir("o erro espera na tela de preparo", "não respondeu" in texto)

            # ---- e o caso simples, que continua tendo de valer: terminar com a
            # tela aberta abre a revisão sozinho, sem passar pela lista.
            # Numa tela remontada o botão volta a dizer "Transcrever": o "Tentar
            # de novo" é do momento da falha, e quem chega agora está começando.
            pagina.click("text=Transcrever")
            pagina.wait_for_selector(".aa-progresso")
            pagina.evaluate("window.__terminar()")
            pagina.wait_for_selector(".revisao", timeout=5000)
            conferir("terminar com a tela aberta abre a revisão",
                     pagina.locator(".revisao .trecho").count() > 0)

            # ---- parar a transcrição: o comando funcionando não é um erro
            pagina.click("#ir-reunioes")
            pagina.wait_for_selector(".gravacao")
            pagina.click('[data-gravacao="C:/g/c"]')
            pagina.wait_for_selector("#vocabulario")
            pagina.click("text=Transcrever")
            pagina.wait_for_selector(".aa-progresso")
            conferir("a transcrição em curso oferece parar",
                     pagina.locator("text=Parar transcrição").count() == 1)

            pagina.click("text=Parar transcrição")
            pagina.wait_for_timeout(120)
            conferir("parar apaga a bolinha", estado() == "false")
            texto_parado = pagina.inner_text(".aa-pagina")
            conferir("parar não vira alerta de erro",
                     "interrompida" in texto_parado
                     and pagina.locator(".aa-alerta--erro, .alerta--erro").count() == 0)
            conferir("depois de parar dá para recomeçar",
                     pagina.locator("text=Transcrever").count() >= 1)

            # ---- notas da reunião (item 2 da Fase 3)
            #
            # O que dói perder é o texto, então o que se confere é ele: escrito
            # numa tela, encontrado na outra, e sobrevivendo a parar a gravação.
            pagina.click("#ir-gravador")
            pagina.wait_for_selector(".gravador")
            conferir("sem gravação o bloco de notas fica fechado",
                     pagina.is_disabled(".notas__texto"))

            pagina.evaluate("window.__gravar(true)")
            pagina.wait_for_timeout(120)
            conferir("gravando, o bloco abre", not pagina.is_disabled(".notas__texto"))

            pagina.fill(".notas__texto", "decidimos adiar o piloto com a Vivo")
            pagina.click("text=Marcar momento")
            pagina.wait_for_timeout(150)
            marcado = pagina.input_value(".notas__texto")
            conferir("marcar momento carimba o tempo decorrido", "[00:12:34]" in marcado,
                     marcado.replace("\n", " ⏎ "))

            # Parar a gravação não pode levar a última frase junto.
            pagina.evaluate("window.__gravar(false)")
            pagina.wait_for_timeout(250)
            guardado = pagina.evaluate("window.chrome.webview._notas['C:/g/agora'] ?? ''")
            conferir("parar a gravação guarda o que estava escrito",
                     "adiar o piloto" in guardado and "[00:12:34]" in guardado)
            conferir("parada, a tela volta a fechar o bloco",
                     pagina.is_disabled(".notas__texto"))

            # E as notas de uma reunião aparecem na tela dela, com os termos
            # oferecidos ao vocabulário.
            pagina.evaluate(
                "window.chrome.webview._notas['C:/g/c'] = 'combinado com a Vanessa: subir o CSV'")
            pagina.click("#ir-reunioes")
            pagina.wait_for_selector(".gravacao")
            pagina.click('[data-gravacao="C:/g/c"]')
            pagina.wait_for_selector(".notas__texto")
            pagina.wait_for_timeout(150)
            conferir("a reunião mostra as notas escritas na gravação",
                     "Vanessa" in pagina.input_value(".notas__texto"))
            conferir("os termos das notas viram sugestão de vocabulário",
                     pagina.locator(".sugestao").count() >= 2,
                     pagina.inner_text(".sugestoes").replace("\n", " "))

            pagina.click(".sugestao >> nth=0")
            pagina.wait_for_timeout(80)
            conferir("clicar na sugestão a joga no vocabulário",
                     pagina.input_value("#vocabulario").strip() != "",
                     pagina.input_value("#vocabulario"))

            # ---- a tela Atas (item 3 da Fase 3)
            pagina.evaluate("window.chrome.webview._transcritas.add('C:/g/a')")
            pagina.click("#ir-atas")
            pagina.wait_for_selector(".ata")
            conferir("Atas lista só as reuniões transcritas",
                     pagina.locator(".ata").count() >= 1)
            conferir("o cartão oferece o tipo de reunião",
                     pagina.locator(".ata__tipo select").count() >= 1)

            pagina.click("text=Gerar ata")
            pagina.wait_for_selector(".ata .aa-progresso")
            conferir("gerar mostra o progresso e a bolinha acende",
                     estado() == "true")

            pagina.evaluate("""window.__ataPronta('C:/g/a',
              '# Ata — Teste\\n\\n## Decisões\\n\\n- **decidido** aqui\\n\\n'
              + '## Pendências\\n\\n- [ ] Mandar a base — **Dimi** — amanhã\\n')""")
            pagina.wait_for_selector(".ata__texto", timeout=5000)
            conferir("a ata pronta aparece no cartão",
                     "decidido" in pagina.inner_text(".ata__texto"))
            # O "#" da ata vira h2 e o "##" vira h3 — um nível abaixo do que o
            # Markdown diz, porque a página já tem o h1 na barra do topo. Título
            # de documento dentro de documento quebraria a hierarquia para quem
            # navega por cabeçalho.
            conferir("o markdown vira HTML de verdade",
                     pagina.locator(".ata__texto h2").count() == 1
                     and pagina.locator(".ata__texto h3").count() == 2
                     and pagina.locator(".ata__texto strong").count() >= 2)
            conferir("a pendência vira caixa marcável",
                     pagina.locator(".ata__pendencia input").count() == 1)
            conferir("a bolinha apaga quando a ata fica pronta", estado() == "false")

            navegador.close()
        srv.shutdown()

    if FALHAS:
        print(f"\n{len(FALHAS)} falha(s): " + ", ".join(FALHAS), file=sys.stderr)
        return 1
    print("\ntudo certo.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
