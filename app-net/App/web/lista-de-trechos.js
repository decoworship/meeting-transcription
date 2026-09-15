// A lista de trechos, desenhada aos poucos.
//
// **O defeito que ela conserta.** A revisão redesenhava a transcrição inteira a
// cada tecla digitada na busca: `replaceChildren()` no corpo e um nó por trecho,
// quatro nós cada. A maior reunião do acervo tem 2.058 trechos — algo como 8.000
// nós destruídos e recriados por letra, sem espera e sem virtualização. Ver
// docs/FASE7-FRONTEND.md §F-2.
//
// **É renderização em blocos, e não virtualização por altura** — e a escolha é
// deliberada. Virtualizar de verdade exige saber a altura de cada linha antes de
// desenhá-la, e aqui a altura varia com o texto: seria uma cache de medidas, um
// espaçador que se corrige sozinho, e uma classe de defeito ("a barra de rolagem
// pula") que este projeto não tem como reproduzir sem rodar o app no Windows. O
// que se mede aqui é o custo por tecla, e desenhar 80 linhas em vez de 2.058 o
// resolve inteiro — o resto chega rolando.
//
// **A mesma peça serve às duas telas**, e é por isso que ela nasce separada: a
// lista ao vivo do produto A cresce por acréscimo durante a reunião, que é
// exatamente o que o `acrescentar()` daqui faz. Duas listas escritas em separado
// divergiriam, e a que diverge é sempre a que ninguém está olhando.

/** Quantas linhas por vez. */
//
// 80 cobre com folga a altura de qualquer janela — a maior tela do acervo mostra
// ~25 trechos —, de modo que a primeira leva já enche o corpo e a segunda só
// acontece se a pessoa rolar. Subir isto não deixa a tela mais completa, só
// devolve o custo que a peça existe para tirar.
const POR_VEZ = 80;

/**
 * Cria a lista dentro de um contêiner.
 *
 * @param corpo o elemento que recebe as linhas.
 * @param desenharLinha `(item, indice) => Element`. Chamada uma vez por linha
 *        que entra na tela, e nunca de novo enquanto ela estiver lá.
 * @param aoVazio `() => Element`, o que mostrar quando nada sobra do filtro.
 * @param rolagem quem rola. **Nulo é a página**, e é o caso da revisão: o
 *        `.transcricao` não é contêiner de rolagem — a tela inteira rola, e
 *        passar o corpo como `root` do observador faria a sentinela estar sempre
 *        dentro dele, portanto sempre visível, portanto todas as levas de uma
 *        vez. O painel ao vivo passa a coluna dele, que rola de verdade (a
 *        decisão D1 da docs/FASE7-FRONTEND.md põe duas colunas com rolagens
 *        separadas).
 */
export function listaDeTrechos(corpo, desenharLinha, aoVazio = null, rolagem = null) {
  //: O que a lista deve mostrar, já filtrado por quem chama. Guardamos os
  //: índices originais junto: a tela edita por índice, e renumerar a cada filtro
  //: faria "editar o trecho 12" apontar para outro trecho a cada busca.
  let itens = [];
  let desenhados = 0;
  let seguindo = false;
  let vazio = null;

  // A sentinela é o gatilho: quando ela aparece no fim da rolagem, entra a
  // próxima leva. Um ouvinte de `scroll` faria a mesma coisa disparando dezenas
  // de vezes por gesto — o observador dispara uma.
  const sentinela = document.createElement("div");
  sentinela.className = "lista__sentinela";
  sentinela.setAttribute("aria-hidden", "true");
  // **Ela entra no corpo agora, e não só no `definir()`.** O `mais()` desenha
  // com `insertBefore(pedaco, sentinela)`, que **levanta NotFoundError** se a
  // sentinela não for filha do corpo. Quem chama `definir()` primeiro nunca viu
  // isso; quem só chama `acrescentar()` — que é o painel ao vivo — batia na
  // exceção no primeiro bloco e não desenhava nada, para sempre e sem erro na
  // tela. Uma linha aqui vale mais que a regra "chame definir() antes", que é
  // exatamente o tipo de contrato que ninguém lembra.
  corpo.appendChild(sentinela);

  const observador = new IntersectionObserver((entradas) => {
    if (entradas.some((e) => e.isIntersecting)) mais();
  }, { root: rolagem, rootMargin: "400px" });

  /** Quem manda na rolagem: o contêiner, ou a página. */
  const quemRola = () => rolagem ?? document.scrollingElement ?? document.documentElement;

  function mais() {
    if (desenhados >= itens.length) return;

    // Num fragmento: sem ele, cada `appendChild` num corpo já grande é um
    // recálculo de layout, e são 80 por leva.
    const pedaco = document.createDocumentFragment();
    const ate = Math.min(desenhados + POR_VEZ, itens.length);
    for (; desenhados < ate; desenhados++) {
      const { item, indice } = itens[desenhados];
      pedaco.appendChild(desenharLinha(item, indice));
    }
    corpo.insertBefore(pedaco, sentinela);

    // A sentinela some quando acabou: um observador ativo sobre um elemento no
    // fim de uma lista completa dispara a cada rolagem, para nada.
    if (desenhados >= itens.length) {
      observador.unobserve(sentinela);
      return;
    }

    if (seguindo) aoFim();

    // **A armadilha do desenho aos poucos, e ela trava a lista em silêncio.** O
    // observador só avisa quando o estado da sentinela *muda*. Se a leva que
    // acabou de entrar não a empurrou para fora dos 400 px de folga — janela
    // alta, linhas curtas —, ela continua visível, nada muda, nenhum aviso
    // chega, e a lista para de crescer no meio sem erro nenhum. Reobservar
    // força um aviso novo com o estado de agora.
    observador.unobserve(sentinela);
    observador.observe(sentinela);
  }

  function aoFim() {
    const r = quemRola();
    r.scrollTop = r.scrollHeight;
  }

  return {
    /**
     * Troca o conteúdo inteiro — é o que a busca e os filtros chamam.
     *
     * **Não mexe na rolagem.** Com a página rolando, forçar o topo a cada tecla
     * digitada seria pior que o defeito que esta peça conserta; e a lista
     * encurtando já leva o navegador a ajustar a posição sozinho.
     */
    definir(novos) {
      itens = novos;
      desenhados = 0;
      corpo.replaceChildren(sentinela);

      observador.unobserve(sentinela);
      vazio = null;

      if (itens.length === 0) {
        if (aoVazio) corpo.appendChild(vazio = aoVazio());
        return;
      }

      mais();
      // Só volta a observar depois da primeira leva: observar antes faria o
      // observador disparar sobre um corpo vazio e desenhar duas levas de uma
      // vez, que é o oposto do que esta peça existe para fazer.
      if (desenhados < itens.length) observador.observe(sentinela);
    },

    /**
     * Acrescenta no fim sem tocar no que já está desenhado.
     *
     * É a operação que a lista ao vivo faz a cada bloco de 3 minutos, e a razão
     * de o `definir()` não servir para ela: redesenhar a reunião inteira a cada
     * bloco é `O(n²)` ao longo da hora, com o engasgo acontecendo na tela do app
     * que está gravando a reunião.
     */
    acrescentar(novos) {
      if (novos.length === 0) return;
      if (vazio) { vazio.remove(); vazio = null; }

      const faltavam = desenhados >= itens.length;
      itens = itens.concat(novos);

      // Se a lista já estava toda na tela, os novos entram agora — senão eles
      // ficariam invisíveis atrás de uma sentinela que ninguém mais observa.
      if (faltavam) mais();
      if (desenhados < itens.length) observador.observe(sentinela);
    },

    /**
     * Desenha até o índice pedido aparecer, e rola até ele.
     *
     * O preço de desenhar aos poucos: a linha do trecho que começou a tocar pode
     * ainda não existir no DOM. Quem pede para vê-la paga as levas que faltam —
     * e paga uma vez, não a cada tecla.
     */
    mostrar(indice) {
      const onde = itens.findIndex((x) => x.indice === indice);
      if (onde < 0) return null;

      while (desenhados <= onde && desenhados < itens.length) mais();

      const linha = corpo.querySelector(`[data-indice="${indice}"]`);
      linha?.scrollIntoView({ block: "nearest" });
      return linha;
    },

    /**
     * Gruda no fim, ou solta.
     *
     * A lista ao vivo rola sozinha enquanto a pessoa está no fim; no instante em
     * que ela rola para trás para reler, o seguimento para. Sem isso, ler o que
     * foi dito há dez minutos é impossível durante a reunião — que é justamente
     * o motivo pelo qual o produto A vale a pena. Ver FASE7-FRONTEND.md §6.1.
     */
    seguir(ligado) {
      seguindo = ligado;
      if (ligado) aoFim();
    },

    /**
     * Rola até o fim, se o seguimento estiver ligado.
     *
     * A legenda desenha fora da lista — ela acrescenta nós direto no corpo,
     * porque texto que firma aos poucos não é "um item" —, mas a rolagem é a
     * mesma, e quem a governa é esta peça.
     */
    seguirAoFim() {
      if (seguindo) aoFim();
    },

    /** Se a rolagem está no fim, com a folga de um gesto de dedo. */
    noFim() {
      const r = quemRola();
      return r.scrollHeight - r.scrollTop - r.clientHeight < 40;
    },

    /** Solta os recursos. Quem sai de tela chama. */
    encerrar() {
      observador.disconnect();
    },
  };
}
