"""Mede se a página do app cabe na janela, em vários tamanhos.

Existe por um defeito que custou uma sessão para achar e é invisível lendo o
CSS: um ``style="position:absolute"`` no sprite de ícones era **descartado pela
CSP** da página (``style-src 'self'``, sem ``'unsafe-inline'``). O sprite voltava
ao fluxo, criava uma linha de texto de 23 px, e empurrava a página inteira —
barra de rolagem em qualquer tamanho de janela, e o botão Ajustes 11 px fora da
tela. Nenhum teste de unidade pega isso; o navegador pega em dois segundos.

Serve a página do app com uma **ponte falsa**: o ``window.chrome.webview`` não
existe num navegador comum, e sem ele o JS morre no primeiro import e só a
moldura renderiza. O dublê responde às operações com dados inventados, o que
basta para as telas se montarem e serem medidas — inclusive a de Ajustes, que
é onde o segundo defeito de layout apareceu.

Uso:
    .venv/bin/python tools/medir_layout.py

Requer o Chromium do Playwright (ver tools/ui_check.py para a instalação).
"""

from __future__ import annotations

import http.server
import os
import socketserver
import sys
import threading
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
WEB = RAIZ / "app-net" / "App" / "web"
DS = RAIZ / "assets" / "ds"

# Os mesmos tamanhos que uma janela real assume: notebook pequeno, tela cheia,
# e um caso apertado de propósito.
TAMANHOS = [(1280, 800), (1024, 600), (900, 480), (1600, 1000)]

# As duas telas densas. Ajustes foi onde o segundo defeito de layout apareceu;
# o Gravador entrou na Fase 2.5 e tem o pior caso de texto longo do app — nome
# de dispositivo WASAPI e lista de participantes, os dois sem teto.
TELAS = [
    ("ajustes", "config=vozes", ".abas"),
    ("gravador", "gravador", ".gravador"),
]

# A ponte falsa. Responde o suficiente para as telas se montarem, e com dados
# grandes o bastante para a rolagem existir — uma tela curta esconderia
# justamente o defeito que se procura.
PONTE_FALSA = """
window.chrome = { webview: {
  _ouvintes: [],
  addEventListener(_, f) { this._ouvintes.push(f); },
  postMessage(cru) {
    const p = JSON.parse(cru);
    const r = { id: p.id };
    const clientes = {};
    for (let i = 0; i < 12; i++)
      clientes['Cliente ' + i] = ['Projeto A', 'Projeto B', 'Projeto C'];

    const gravador = {
      gravando: true, mudo: true, mudo_ha_s: 320, cor: 'laranja',
      status: 'Gravando 00:12:34 (mudo)', duracao_s: 754,
      pasta: 'C:\\\\Users\\\\andre\\\\Documents\\\\MeetingRecordings',
      titulo: 'Planejamento trimestral com um título bem comprido para esticar',
      participantes: ['Ana Paula', 'Bruno', 'Carla', 'Diego', 'Eduardo'],
      notificacoes: true, usar_agenda: true, agenda_configurada: true,
      conta: 'andre@exemplo.com',
      faixas: [
        { nome: 'mic', dispositivo: 'Microfone (Headset AN01 Hands-Free)',
          nivel: 0.04, ja_ouviu: true, mudo: true, silencio_s: 320,
          desconectado: false, falha: null },
        { nome: 'system', dispositivo: 'Alto-falantes (Realtek(R) Audio)',
          nivel: 0.2, ja_ouviu: true, mudo: false, silencio_s: 0,
          desconectado: false, falha: null },
      ],
    };

    if (p.op === 'gravacoes') r.gravacoes = [];
    else if (p.op === 'gravador' || p.op === 'gravar' || p.op === 'parar-gravacao'
             || p.op === 'mutar' || p.op === 'notificacoes' || p.op === 'usar-agenda'
             || p.op === 'conectar-agenda' || p.op === 'desconectar-agenda'
             || p.op === 'pasta-das-gravacoes') r.gravador = gravador;
    else if (p.op === 'dispositivos' || p.op === 'escolher-dispositivo') {
      r.gravador = gravador;
      r.dispositivos = {
        entradas: [{ id: 'a', nome: 'Microfone (Headset AN01 Hands-Free)', padrao: true },
                   { id: 'b', nome: 'Microfone (Realtek(R) Audio)', padrao: false }],
        saidas: [{ id: 'c', nome: 'Alto-falantes (Realtek(R) Audio)', padrao: true }],
        mic_id: 'a', loopback_id: null,
      };
    }
    else if (p.op === 'clientes') r.clientes = clientes;
    else if (p.op === 'prefs') r.prefs = { language: 'pt', model_size: 'large-v3' };
    else if (p.op === 'config') r.config = {};
    else if (p.op === 'catalogo') r.catalogo = [
      { pacote: { id: 'large-v3', nome: 'Large v3', familia: 'asr',
                  descricao: 'o mais exato', repositorio: 'x/y',
                  tamanho_esperado_bytes: 3e9, tamanho_medido: true },
        estado: 'instalado', bytes_em_disco: 3e9, em_uso: true },
      { pacote: { id: 'medium', nome: 'Medium', familia: 'asr',
                  descricao: 'meio-termo', repositorio: 'x/y',
                  tamanho_esperado_bytes: 1.5e9, tamanho_medido: false },
        estado: 'ausente', bytes_em_disco: 0, em_uso: false },
      { pacote: { id: 'community-1', nome: 'Community 1', familia: 'diarizacao',
                  descricao: 'separa quem falou', repositorio: 'x/y',
                  tamanho_esperado_bytes: 3e7, tamanho_medido: true },
        estado: 'instalado', bytes_em_disco: 3e7, em_uso: true },
    ];
    else if (p.op === 'vozes') r.vozes = Array.from({ length: 8 }, (_, i) => ({
      nome: 'Pessoa ' + i,
      amostras: Array.from({ length: 4 }, (_, j) => ({
        indice: j, criada_em: '2026-08-10T10:00:00', duracao_s: 4.2,
        gravacao: '2026-08-10_08-08-10', faixa: 'system',
        t0: 10, t1: 14, dispositivo: 'Headset AN01',
        quarentena: j === 0, trecho: null,
      })),
    }));

    setTimeout(() => {
      for (const f of this._ouvintes) f({ data: JSON.stringify(r) });
    }, 0);
  },
} };
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


def main() -> int:
    try:
        from playwright.sync_api import sync_playwright
    except ImportError:
        print("falta o playwright: uv pip install playwright "
              "&& python -m playwright install chromium", file=sys.stderr)
        return 2

    # Porta 0: o sistema escolhe uma livre. Porta fixa falha ao rodar duas vezes
    # seguidas, porque a anterior ainda está em TIME_WAIT.
    socketserver.TCPServer.allow_reuse_address = True
    with socketserver.TCPServer(("127.0.0.1", 0), Servidor) as srv:
        porta = srv.server_address[1]
        threading.Thread(target=srv.serve_forever, daemon=True).start()

        problemas = 0
        with sync_playwright() as pw:
            navegador = pw.chromium.launch()
            for nome_da_tela, hash_, marcador in TELAS:
              print(f"── {nome_da_tela}")
              for largura, altura in TAMANHOS:
                  pagina = navegador.new_page(
                      viewport={"width": largura, "height": altura})
                  pagina.add_init_script(PONTE_FALSA)
                  pagina.goto(f"http://127.0.0.1:{porta}/index.html#{hash_}",
                              wait_until="load")
                  pagina.wait_for_selector(marcador, timeout=5000)
                  pagina.wait_for_timeout(250)

                  m = pagina.evaluate("""() => {
                      const rolador = document.querySelector('.conteudo');
                      // A tela do Gravador não tem abas; onde não houver, mede-se
                      // só o que existe. O sticky é a pergunta das abas.
                      const abas = document.querySelector('.abas');
                      const antes = abas ? abas.getBoundingClientRect().top : 0;

                      // Rolar até o fim e ver se as abas ficaram no lugar. É a
                      // pergunta que importa: sticky que não gruda desce junto
                      // com o painel e some da tela.
                      rolador.scrollTop = rolador.scrollHeight;
                      return new Promise(ok => requestAnimationFrame(() => {
                          const depois = abas ? abas.getBoundingClientRect().top : 0;
                          ok({
                              sobra: document.scrollingElement.scrollHeight - window.innerHeight,
                              ajustes: Math.round(document.getElementById('ir-config')
                                                          .getBoundingClientRect().bottom),
                              janela: window.innerHeight,
                              rolou: Math.round(rolador.scrollTop),
                              abasAntes: Math.round(antes),
                              abasDepois: Math.round(depois),
                              barra: Math.round(document.querySelector('.barra')
                                                        .getBoundingClientRect().bottom),
                          });
                      }));
                  }""")
                  pagina.close()

                  # Três perguntas: a página cabe, o último item do trilho está
                  # visível, e as abas não passam por baixo do cabeçalho ao rolar.
                  cabe = m["sobra"] <= 0
                  trilho = m["ajustes"] <= m["janela"]
                  # Só exigir das telas que de fato rolaram e que têm abas: numa
                  # janela grande o painel cabe inteiro e não há o que provar.
                  abas = (marcador != ".abas" or m["rolou"] == 0
                          or m["abasDepois"] >= m["barra"] - 1)

                  ok = cabe and trilho and abas
                  if not ok:
                      problemas += 1
                  print(f'{largura}x{altura}: sobra {m["sobra"]}px, '
                        f'Ajustes em {m["ajustes"]}/{m["janela"]}, '
                        f'abas {m["abasAntes"]}→{m["abasDepois"]} '
                        f'(cabeçalho em {m["barra"]}, rolou {m["rolou"]}px) '
                        f'-> {"ok" if ok else "PROBLEMA"}')
                  if not abas:
                      print("     as abas passaram por baixo do cabeçalho ao rolar.")
            navegador.close()

        srv.shutdown()

    if problemas:
        print(f"\n{problemas} de {len(TAMANHOS)} tamanhos com problema.",
              file=sys.stderr)
    return 1 if problemas else 0


if __name__ == "__main__":
    raise SystemExit(main())
