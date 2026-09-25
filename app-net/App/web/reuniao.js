// A reunião aberta: Transcrição · Ata · Notas.
//
// A ata foi um destino à parte (FASE3.md §4), e esse destino desenhava a mesma
// lista de Reuniões com um botão a mais. Ela é uma propriedade da reunião, e
// mora aqui desde a decisão D-A de docs/superpowers/specs/2026-09-23-ui-ux.md §6.
//
// **As abas não se remontam ao trocar.** Cada uma é montada na primeira vez que
// aparece e depois só se esconde: a revisão guarda na memória os nomes de
// falante e as edições que gravam com atraso (revisao.js, `salvar`), e
// remontá-la a cada troca perderia o que ainda não foi para o disco. A aba da
// ata, escondida no meio de uma geração, continua acompanhando o andamento.
//
// As classes são `reuniao-aberta*`, e não `reuniao*`: `.reuniao` já é a linha
// de reunião da agenda no Gravador, com grade e borda próprias.

import { telaDeRevisao, abrirPainel as abrirPainelDaRevisao } from "/revisao.js";
import { montarAta } from "/atas.js";
import { blocoDeNotas } from "/notas.js";
import { duracao, quando, tituloDe, acoesDaBarra } from "/app.js";
import { tocadorDaReuniao } from "/tocador.js";

const ABAS = ["transcricao", "ata", "notas"];

/** A troca de aba da reunião aberta, para o endereço (`#revisao=N&notas`). */
let mostrarAba = null;

/**
 * Desenha a reunião aberta dentro de <main>.
 *
 * @param aba qual abrir: "transcricao" (o padrão), "ata" ou "notas". Pedida de
 *   fora — o painel de Reuniões pede "ata" —, a aba recebe o foco: quem clicou
 *   em "Abrir a ata" chega na ata, e não no título.
 */
export function telaDaReuniao(g, dados, { cabecalho, tela, aba = "transcricao", aoRefazer, aoApagar }) {
  const raiz = document.createElement("div");
  raiz.className = "reuniao-aberta";

  const lista = document.createElement("div");
  lista.className = "reuniao-aberta__abas";
  lista.setAttribute("role", "tablist");
  lista.setAttribute("aria-label", "Partes da reunião");
  raiz.appendChild(lista);

  const rotulos = {
    transcricao: ["Transcrição", String(dados.segments.length)],
    ata: ["Ata", g.tem_ata ? rotuloDePendencias(g.pendencias) : null],
    notas: ["Notas", null],
  };

  /** id → { botao, painel, montada, rolagem } */
  const abas = new Map();
  for (const id of ABAS) {
    const [rotulo, conta] = rotulos[id];
    const botao = document.createElement("button");
    botao.type = "button";
    botao.className = "reuniao-aberta__aba";
    botao.id = `aba-${id}`;
    botao.dataset.aba = id;
    botao.setAttribute("role", "tab");
    botao.setAttribute("aria-controls", `painel-${id}`);
    botao.textContent = rotulo;
    contar(botao, conta);
    botao.addEventListener("click", () => mostrar(id));
    botao.addEventListener("keydown", (e) => aoTeclar(e, id));
    lista.appendChild(botao);

    const painel = document.createElement("section");
    painel.className = "reuniao-aberta__painel";
    painel.id = `painel-${id}`;
    painel.setAttribute("role", "tabpanel");
    painel.setAttribute("aria-labelledby", botao.id);
    painel.hidden = true;
    raiz.appendChild(painel);

    abas.set(id, { botao, painel, montada: false });
  }

  // O tocador é da reunião, e não de uma aba: a nota também toca (UI-4), e
  // tocar sem ter onde pausar era o defeito do ⏸ solto.
  raiz.appendChild(tocadorDaReuniao(g));

  tela.replaceChildren(raiz);
  cabecalho(tituloDe(g), [
    quando(g.nome), duracao(g.duracao_s),
    g.cliente || g.projeto ? [g.cliente, g.projeto].filter(Boolean).join(" › ") : null,
    g.convidados > 0 ? `${g.convidados} convidados` : null,
  ].filter(Boolean).join(" · "), true);

  // Falantes e Exportar são da reunião, e não de uma aba: nomear quem falou
  // serve à ata e às notas também, e exportar leva a transcrição inteira.
  // Os painéis continuam sendo da revisão (revisao.js), que é quem sabe os nomes.
  const botaoDaBarra = (acao, rotulo, classe) => {
    const b = document.createElement("button");
    b.type = "button";
    b.className = `aa-btn ${classe}`;
    b.dataset.acao = acao;
    b.textContent = rotulo;
    b.addEventListener("click", () => abrirPainelDaRevisao(acao));
    return b;
  };
  acoesDaBarra(botaoDaBarra("falantes", "Falantes", "aa-btn-secundario"),
               botaoDaBarra("exportar", "Exportar", "aa-btn-primario"));

  const montar = {
    // A revisão recebe um cabeçalho que não faz nada: quem diz o título da
    // barra é a reunião, e não a aba.
    transcricao: (p) => telaDeRevisao(g, dados, { cabecalho: () => {}, tela: p, aoRefazer, aoApagar }),
    ata: (p) => montarAta(p, g, { aoContar: (n) => contar(abas.get("ata").botao, rotuloDePendencias(n)) }),
    notas: (p) => p.appendChild(blocoDeNotas(g.caminho, { linhas: 18 }).raiz),
  };

  // A página inteira rola num lugar só (.conteudo), e uma aba mais curta a
  // traria de volta ao topo: cada aba guarda onde estava. Sem isto, ir anotar e
  // voltar perdia o trecho em que se estava — o que a gaveta de notas existia
  // para proteger.
  const rolador = () => raiz.closest(".conteudo") ?? document.scrollingElement;

  function mostrar(id, { focar = false } = {}) {
    const alvo = abas.has(id) ? id : "transcricao";
    const antes = [...abas.values()].find((a) => !a.painel.hidden);
    if (antes) antes.rolagem = rolador().scrollTop;
    for (const [outro, a] of abas) {
      const esta = outro === alvo;
      a.botao.setAttribute("aria-selected", String(esta));
      // Uma parada de Tab só para as abas; as setas andam entre elas.
      a.botao.tabIndex = esta ? 0 : -1;
      a.painel.hidden = !esta;
    }
    const a = abas.get(alvo);
    if (!a.montada) {
      a.montada = true;
      montar[alvo](a.painel);
    }
    if (antes && antes !== a) rolador().scrollTop = a.rolagem ?? 0;
    if (focar) a.botao.focus();
  }

  /** O padrão de abas: setas, Home e End andam, e a aba escolhida já se mostra. */
  function aoTeclar(e, id) {
    const i = ABAS.indexOf(id);
    const destino = {
      ArrowRight: (i + 1) % ABAS.length, ArrowLeft: (i - 1 + ABAS.length) % ABAS.length,
      Home: 0, End: ABAS.length - 1,
    }[e.key];
    if (destino === undefined) return;
    e.preventDefault();
    mostrar(ABAS[destino], { focar: true });
  }

  // **A transcrição monta sempre**, mesmo aberta noutra aba. A gaveta de
  // falantes lê o estado da revisão, que é de módulo: sem isto, a reunião aberta
  // direto na Ata mostrava — e renomeava — os falantes da reunião aberta antes.
  // A lista é virtual, e montar escondida custa uma leva de 80 linhas.
  const t = abas.get("transcricao");
  t.montada = true;
  montar.transcricao(t.painel);

  mostrarAba = mostrar;
  mostrar(aba, { focar: aba !== "transcricao" });
}

function rotuloDePendencias(n) {
  if (!(n > 0)) return null;
  return n === 1 ? "1 pendência" : `${n} pendências`;
}

/** Põe, troca ou tira o selo de contagem de uma aba. */
function contar(botao, texto) {
  let selo = botao.querySelector(".reuniao-aberta__conta");
  if (!texto) { selo?.remove(); return; }
  if (!selo) {
    selo = document.createElement("span");
    selo.className = "reuniao-aberta__conta";
    botao.appendChild(selo);
  }
  selo.textContent = texto;
}

/**
 * Abre por cima o que o endereço pedir: uma aba da reunião, ou um painel da
 * revisão (falantes, exportar, editar:N), que continua sendo dela.
 */
export function abrirPainel(qual) {
  if (ABAS.includes(qual)) return mostrarAba?.(qual, { focar: true });
  return abrirPainelDaRevisao(qual);
}
