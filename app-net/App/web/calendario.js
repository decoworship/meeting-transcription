// Um mês para escolher um dia — o do filtro de data da lista e o da semana.
//
// Próprio, e não o <input type="date"> do WebView2: o nativo não sabe marcar os
// dias que têm gravação, e o ponto é o que responde "em que dia foi mesmo?" sem
// abrir dia por dia (docs/superpowers/specs/2026-09-23-ui-ux.md §2, "Filtro de
// data"). E o nativo fala a língua do Windows, não a do app.
//
// O teclado é o da grade de datas do WAI-ARIA APG: as setas andam um dia e uma
// semana, Home e End vão às pontas da semana, PageUp e PageDown trocam de mês no
// mesmo dia, e Enter escolhe. A grade inteira é uma parada de Tab só.

import { gradeDoMes, rotuloDoMes, rotuloLongoDoDia, segundaDe, somarDias,
         somarMeses } from "/reunioes-regras.js";

const CABECA = [["seg", "segunda"], ["ter", "terça"], ["qua", "quarta"], ["qui", "quinta"],
                ["sex", "sexta"], ["sáb", "sábado"], ["dom", "domingo"]];

let proximoId = 0;

/**
 * @param o.hoje "AAAA-MM-DD", marcado na grade.
 * @param o.foco o dia que recebe o foco, e cujo mês abre.
 * @param o.marcados os dias com gravação, que ganham o ponto.
 * @param o.faixa `[de, até]` realçado — o critério de agora —, ou null.
 * @param o.aoEscolher `(dia) => void`.
 * @returns `{ raiz, focar }`.
 */
export function calendario({ hoje, foco = null, marcados = new Set(), faixa = null, aoEscolher }) {
  let atual = foco ?? hoje;
  const id = `calendario-mes-${++proximoId}`;

  const raiz = document.createElement("div");
  raiz.className = "calendario";

  const topo = document.createElement("div");
  topo.className = "calendario__topo";
  const anterior = seta("calendario__anterior", "Mês anterior", "‹");
  const proximo = seta("calendario__proximo", "Próximo mês", "›");
  const mes = document.createElement("span");
  mes.className = "calendario__mes";
  mes.id = id;
  // O mês novo é dito ao trocar: a grade sozinha não diz que mudou.
  mes.setAttribute("aria-live", "polite");
  topo.append(anterior, mes, proximo);

  const grade = document.createElement("table");
  grade.className = "calendario__grade";
  grade.setAttribute("role", "grid");
  grade.setAttribute("aria-labelledby", id);
  const cabeca = document.createElement("thead");
  const linhaDaCabeca = document.createElement("tr");
  for (const [curto, longo] of CABECA) {
    const th = document.createElement("th");
    th.scope = "col";
    th.abbr = longo;
    th.textContent = curto;
    linhaDaCabeca.appendChild(th);
  }
  cabeca.appendChild(linhaDaCabeca);
  const corpo = document.createElement("tbody");
  grade.append(cabeca, corpo);
  raiz.append(topo, grade);

  function desenhar() {
    const [a, m] = atual.split("-").map(Number);
    mes.textContent = rotuloDoMes(a, m);
    corpo.replaceChildren();
    for (const semana of gradeDoMes(a, m)) {
      const tr = document.createElement("tr");
      for (const c of semana) {
        const td = document.createElement("td");
        const na = faixa && c.dia >= faixa[0] && c.dia <= faixa[1];
        td.setAttribute("aria-selected", String(Boolean(na)));
        const b = document.createElement("button");
        b.type = "button";
        b.className = "calendario__dia";
        b.dataset.dia = c.dia;
        b.tabIndex = c.dia === atual ? 0 : -1;
        b.textContent = String(c.n);
        if (c.fora) b.classList.add("calendario__dia--fora");
        if (na) b.classList.add("calendario__dia--na-faixa");
        if (c.dia === hoje) b.setAttribute("aria-current", "date");
        const com = marcados.has(c.dia);
        if (com) {
          b.classList.add("calendario__dia--com-gravacao");
          const ponto = document.createElement("span");
          ponto.className = "calendario__ponto";
          ponto.setAttribute("aria-hidden", "true");
          b.appendChild(ponto);
        }
        b.setAttribute("aria-label", rotuloLongoDoDia(c.dia) + (com ? ", com gravação" : ""));
        b.addEventListener("click", () => aoEscolher(c.dia));
        td.appendChild(b);
        tr.appendChild(td);
      }
      corpo.appendChild(tr);
    }
  }

  const botaoDe = (dia) => corpo.querySelector(`[data-dia="${dia}"]`);

  /** Leva o foco a um dia; troca a grade quando ele é de outro mês. */
  function mover(dia) {
    const trocouDeMes = dia.slice(0, 7) !== atual.slice(0, 7);
    botaoDe(atual)?.setAttribute("tabindex", "-1");
    atual = dia;
    if (trocouDeMes) desenhar();
    const b = botaoDe(atual);
    b.tabIndex = 0;
    b.focus();
  }

  grade.addEventListener("keydown", (e) => {
    const destino = {
      ArrowLeft: () => somarDias(atual, -1),
      ArrowRight: () => somarDias(atual, 1),
      ArrowUp: () => somarDias(atual, -7),
      ArrowDown: () => somarDias(atual, 7),
      Home: () => segundaDe(atual),
      End: () => somarDias(segundaDe(atual), 6),
      PageUp: () => somarMeses(atual, -1),
      PageDown: () => somarMeses(atual, 1),
    }[e.key];
    if (!destino) return;
    e.preventDefault();
    mover(destino());
  });

  // As setas do topo trocam o mês e deixam o foco nelas: quem clica em ‹ três
  // vezes para voltar três meses não quer o foco pulando para a grade.
  anterior.addEventListener("click", () => { atual = somarMeses(atual, -1); desenhar(); });
  proximo.addEventListener("click", () => { atual = somarMeses(atual, 1); desenhar(); });

  desenhar();
  return { raiz, focar: () => botaoDe(atual)?.focus() };
}

function seta(classe, rotulo, texto) {
  const b = document.createElement("button");
  b.type = "button";
  b.className = `aa-btn aa-btn-texto ${classe}`;
  b.setAttribute("aria-label", rotulo);
  b.textContent = texto;
  return b;
}
