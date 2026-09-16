// A caixa de perguntar ao modelo sobre a reunião em curso.
//
// **Bloco próprio, e não dentro do painel da legenda** — pedido pelo dono do
// produto em 16/09/2026, depois de usar as duas juntas. São duas coisas
// diferentes: a legenda é o que está sendo dito, e isto é o que um modelo
// deduziu do que foi dito. Misturadas na mesma coluna, a resposta empurrava a
// legenda para fora da tela.
//
// **A pilha de respostas tem teto e rolagem própria.** Sem teto, três perguntas
// seguidas faziam o bloco crescer sem parar e empurrar o resto da coluna — o
// mesmo `F-12` que a lista de trechos já tinha resolvido do seu lado.
//
// **Ela vive fora do `aplicar()` do gravador**, que roda cinco vezes por
// segundo: redesenhar a resposta a cada volta apagaria o texto sendo lido.

import { pedir } from "/ponte.js";
import { alerta } from "/pecas.js";

/**
 * Monta o bloco de perguntas.
 *
 * @returns `{ raiz }`. Ele não assina eventos, então não há o que encerrar.
 */
export function painelDePerguntas() {
  const raiz = document.createElement("section");
  raiz.className = "bloco perguntar";

  const titulo = document.createElement("h2");
  titulo.className = "bloco__titulo";
  titulo.textContent = "Perguntar sobre a reunião";

  // O botão do resumo, que é a primeira interação: ele manda a instrução de
  // lista, e a caixa livre abaixo é por onde se pede o detalhe.
  const resumir = document.createElement("button");
  resumir.className = "aa-btn aa-btn-secundario perguntar__resumir";
  resumir.type = "button";
  resumir.textContent = "O que já rolou?";

  // **As respostas acumulam, a mais nova no topo**, com teto e rolagem. Antes
  // cada pergunta apagava a anterior, e pedir o detalhe custava o que veio
  // antes — visto em uso em 16/09/2026.
  const respostas = document.createElement("div");
  respostas.className = "perguntar__respostas";

  const formulario = document.createElement("form");
  formulario.className = "perguntar__caixa";

  const campo = document.createElement("input");
  campo.className = "aa-entrada";
  campo.type = "text";
  campo.placeholder = "Pergunte o detalhe…";

  const enviar = document.createElement("button");
  enviar.className = "aa-btn";
  enviar.type = "submit";
  enviar.textContent = "Perguntar";

  const aviso = document.createElement("div");

  formulario.append(campo, enviar);
  raiz.append(titulo, aviso, resumir, respostas, formulario);

  // Nasce escondido: o núcleo diz se a funcionalidade pode existir, e mostrar a
  // caixa com a chave desligada faria a pessoa escrever a pergunta para só
  // então descobrir o impedimento.
  raiz.hidden = true;
  pedir("aovivo").then((r) => {
    if (!raiz.isConnected) return;
    raiz.hidden = !!r.perguntar_impedimento;
  }).catch(() => {
    // Sem resposta não se afirma nada: o bloco fica escondido.
  });

  resumir.addEventListener("click", () => enviarPergunta("O que já rolou?", true));

  formulario.addEventListener("submit", (ev) => {
    ev.preventDefault();
    const pergunta = campo.value.trim();
    if (pergunta) enviarPergunta(pergunta, false);
  });

  /**
   * Manda a pergunta e devolve um bloco novo no topo da pilha.
   *
   * @param resumo quando `true`, vai a instrução de lista do botão; quando
   *   `false`, a pergunta é livre e o núcleo só acrescenta as regras.
   */
  async function enviarPergunta(pergunta, resumo) {
    if (enviar.disabled) return;
    enviar.disabled = true;
    resumir.disabled = true;

    const bloco = document.createElement("div");
    bloco.className = "perguntar__resposta";
    bloco.dataset.estado = "esperando";
    bloco.append(linhaDaPergunta(pergunta),
                 paragrafo("carregando o modelo…", "perguntar__texto"));
    respostas.prepend(bloco);
    respostas.scrollTop = 0;

    try {
      const r = await pedir("perguntar-ao-vivo", { pergunta, resumo }, (p) => {
        if (raiz.isConnected && p.texto)
          bloco.replaceChildren(linhaDaPergunta(pergunta),
                                paragrafo(p.texto, "perguntar__texto"));
      });
      if (!raiz.isConnected) return;

      bloco.dataset.estado = "pronta";
      const partes = [linhaDaPergunta(pergunta),
                      paragrafo(r.resposta || "", "perguntar__texto")];
      // Quem perguntou tem direito de saber que a resposta não considerou a
      // reunião inteira. Escondê-lo faria o modelo parecer confiante sobre um
      // começo que ele não viu.
      if (r.cortado)
        partes.push(alerta("A reunião passou de uma hora: o modelo leu só a "
                         + "parte final dela.", "atencao"));
      bloco.replaceChildren(...partes);
      if (!resumo) campo.value = "";
    } catch (erro) {
      if (!raiz.isConnected) return;
      bloco.dataset.estado = "erro";
      bloco.replaceChildren(linhaDaPergunta(pergunta), alerta(erro.message, "atencao"));
    } finally {
      enviar.disabled = false;
      resumir.disabled = false;
    }
  }

  function linhaDaPergunta(texto) {
    return paragrafo(texto, "perguntar__pergunta");
  }

  function paragrafo(texto, classe) {
    const el = document.createElement("p");
    el.className = classe;
    el.textContent = texto;
    return el;
  }

  return { raiz };
}
