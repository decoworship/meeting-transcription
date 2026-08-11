// Pedaços de interface usados em mais de uma tela.
//
// Não é uma biblioteca de componentes: é o mínimo para não repetir a mesma
// dúzia de linhas em cada tela. Tudo aqui devolve nós do DOM prontos, com as
// classes do design system.

export function alerta(texto, variacao = "atencao") {
  const el = document.createElement("div");
  el.className = `aa-alerta aa-alerta--${variacao}`;
  const ponto = document.createElement("span");
  ponto.className = "aa-alerta__ponto";
  el.append(ponto, document.createTextNode(texto));
  return el;
}

export function secao(titulo) {
  const s = document.createElement("section");
  s.className = "secao";
  const h = document.createElement("h2");
  h.className = "secao__titulo";
  h.textContent = titulo;
  s.appendChild(h);
  return s;
}

/**
 * Um rótulo com o controle embaixo.
 * @param tipo "input", "select" ou "textarea"
 */
export function campo(rotulo, tipo, opcoes = {}) {
  const label = document.createElement("label");
  label.className = "campo";

  const span = document.createElement("span");
  span.textContent = rotulo;

  let controle;
  if (tipo === "select") {
    controle = document.createElement("select");
    for (const o of opcoes.opcoes ?? []) {
      const op = document.createElement("option");
      op.textContent = o;
      controle.appendChild(op);
    }
  } else if (tipo === "textarea") {
    controle = document.createElement("textarea");
    controle.rows = opcoes.linhas ?? 3;
    controle.value = opcoes.valor ?? "";
  } else {
    controle = document.createElement("input");
    controle.type = opcoes.tipo ?? "text";
    controle.value = opcoes.valor ?? "";
    if (opcoes.dica) controle.placeholder = opcoes.dica;
  }

  controle.className = "aa-entrada";
  if (opcoes.id) controle.id = opcoes.id;

  label.append(span, controle);
  return label;
}

/**
 * Campo que escolhe de uma lista mas aceita um nome novo digitado.
 *
 * É um input com datalist, e não um select: no app Python dá para digitar um
 * cliente que ainda não existe e sair transcrevendo, e perder isso obrigaria a
 * um "cadastrar antes" que ninguém quer fazer no meio do trabalho.
 */
export function campoComSugestoes(rotulo, id, valores, valor = "") {
  const label = document.createElement("label");
  label.className = "campo";

  const span = document.createElement("span");
  span.textContent = rotulo;

  const entrada = document.createElement("input");
  entrada.className = "aa-entrada";
  entrada.id = id;
  entrada.value = valor;
  entrada.setAttribute("list", `${id}-lista`);
  entrada.placeholder = "escolha ou digite um novo";
  entrada.autocomplete = "off";

  const lista = document.createElement("datalist");
  lista.id = `${id}-lista`;
  preencherSugestoes(lista, valores);

  label.append(span, entrada, lista);
  return label;
}

export function preencherSugestoes(lista, valores) {
  lista.replaceChildren();
  for (const v of valores) {
    const o = document.createElement("option");
    o.value = v;
    lista.appendChild(o);
  }
}

// ──────────────────────────────────────────── gavetas que abrem por cima

const veu = document.getElementById("veu");

/**
 * Abre um painel lateral sobre a tela.
 *
 * Sobreposição e não navegação: o pedido é poder mexer nos falantes ou nas
 * configurações <b>sem perder o lugar no texto</b>. Trocar de tela obrigaria a
 * rolar de volta até onde se estava.
 */
export function abrirGaveta(id) {
  fecharGavetas();
  document.getElementById(id).hidden = false;
  veu.hidden = false;
}

export function fecharGavetas() {
  for (const g of document.querySelectorAll(".gaveta")) g.hidden = true;
  veu.hidden = true;
}

/** Uma cor estável por falante, para o olho achar quem fala sem ler o nome. */
const PALETA = [
  "var(--cor-acao)",
  "#8a6d3b",
  "#4a7c59",
  "#8c4a5f",
  "#3d6d8a",
  "#7a5c9e",
];

export function corDoFalante(nome, ordem) {
  // "You" é sempre o primeiro tom da paleta: é o falante que o usuário procura
  // primeiro quando revisa a própria fala.
  if (nome === "You") return PALETA[0];
  return PALETA[1 + (ordem % (PALETA.length - 1))];
}
