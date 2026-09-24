// Um painel pequeno que abre colado a um botão: o calendário do filtro de data
// da lista e o da semana.
//
// Não é modal nem gaveta. Quem abriu pode clicar fora, e ele fecha; é pequeno,
// fica encostado em quem o abriu, e some ao escolher. As regras de foco são as
// do diálogo não modal do WAI-ARIA APG: abrir leva o foco para dentro, Esc
// fecha e devolve o foco ao botão, e sair dele — com Tab ou clicando fora —
// fecha sem prender ninguém.

/**
 * @param gatilho o botão que abre e fecha.
 * @param rotulo o nome do painel, para o leitor de tela.
 * @param montar `(fechar) => ({ raiz, focar })`, chamado a cada abertura: o
 *   painel nasce do estado de agora, e não do de quando a tela foi montada.
 * @returns `{ ancora, fechar }` — a âncora, com o gatilho dentro, é o que se
 *   põe na tela.
 */
export function popover(gatilho, rotulo, montar) {
  const ancora = document.createElement("div");
  ancora.className = "popover__ancora";
  ancora.appendChild(gatilho);
  gatilho.setAttribute("aria-haspopup", "dialog");
  gatilho.setAttribute("aria-expanded", "false");

  let painel = null;

  function fechar({ devolverFoco = true } = {}) {
    if (!painel) return;
    painel.remove();
    painel = null;
    gatilho.setAttribute("aria-expanded", "false");
    document.removeEventListener("pointerdown", aoApontar, true);
    document.removeEventListener("focusin", aoFocar, true);
    if (devolverFoco) gatilho.focus();
  }

  // O foco entrando em qualquer coisa fora do painel e do botão fecha. Para a
  // frente, o Tab sai direto do painel; para trás, o Shift+Tab passa pelo
  // botão antes — e um ouvinte só de saída do painel deixava o painel aberto
  // dali em diante, com o Esc surdo.
  function aoFocar(e) {
    if (!ancora.contains(e.target)) fechar({ devolverFoco: false });
  }

  // Na captura: um clique fora que caísse num botão da lista chegaria lá antes
  // de o painel saber que perdeu.
  function aoApontar(e) {
    if (!ancora.contains(e.target)) fechar({ devolverFoco: false });
  }

  function abrir() {
    painel = document.createElement("div");
    painel.className = "popover";
    painel.setAttribute("role", "dialog");
    painel.setAttribute("aria-label", rotulo);
    const { raiz, focar } = montar(() => fechar());
    painel.appendChild(raiz);

    painel.addEventListener("keydown", (e) => {
      if (e.key !== "Escape") return;
      // Sem subir: o Esc do documento fecha as gavetas, e não é disso que se trata.
      e.preventDefault();
      e.stopPropagation();
      fechar();
    });

    ancora.appendChild(painel);
    // Preso à esquerda do botão, ele sai da tela quando o botão está perto da
    // borda direita — a janela estreita põe o filtro de data lá. Encosta então
    // na direita do botão.
    if (painel.getBoundingClientRect().right > document.documentElement.clientWidth)
      painel.classList.add("popover--a-direita");
    gatilho.setAttribute("aria-expanded", "true");
    document.addEventListener("pointerdown", aoApontar, true);
    // O calendário redesenha a grade ao trocar de mês, e o foco se perde no
    // corpo por um instante: isso não entra em nada, e não fecha.
    document.addEventListener("focusin", aoFocar, true);
    focar();
  }

  gatilho.addEventListener("click", () => (painel ? fechar() : abrir()));
  return { ancora, fechar };
}
