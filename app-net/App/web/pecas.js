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

// ─────────────────────────────────────────────────── o que se anuncia

/**
 * Diz uma linha a quem lê a tela por leitor de tela.
 *
 * <b>Estado, nunca conteúdo.</b> A região é `polite` e vive fora das telas, no
 * index.html, porque um `aria-live` que nasce junto com o texto que ele deveria
 * anunciar não anuncia nada: o leitor de tela só lê o que <i>muda</i> numa
 * região que já existia. Por isso ela está no HTML e não é criada aqui.
 *
 * A regra do que entra é do docs/FASE7-FRONTEND.md §9, e ela foi escrita
 * pensando no painel ao vivo: anunciar cada bloco de transcrição que chega
 * leria a reunião inteira em voz alta, por cima da reunião. Vale desde já —
 * "12 gravações", "salvo", "não apagou" são estado; o texto de um trecho não é.
 *
 * O texto é limpo antes e reposto num tique: repetir a mesma frase (dois
 * "salvo" seguidos) não muda o nó, e o que não muda não é lido.
 */
const regiaoDeAnuncio = document.getElementById("anuncio");
let anuncioPendente = null;

export function anunciar(texto) {
  if (!regiaoDeAnuncio) return;
  clearTimeout(anuncioPendente);
  regiaoDeAnuncio.textContent = "";
  anuncioPendente = setTimeout(() => { regiaoDeAnuncio.textContent = texto; }, 60);
}

// ──────────────────────────────────────────── gavetas que abrem por cima

const veu = document.getElementById("veu");

/** O que o navegador deixa alcançar por Tab. */
const FOCAVEL = 'a[href], button:not([disabled]), input:not([disabled]), '
              + 'select:not([disabled]), textarea:not([disabled]), '
              + '[tabindex]:not([tabindex="-1"])';

/** A gaveta aberta e para onde o foco volta quando ela fechar. */
let gavetaAberta = null;

/**
 * Abre um painel lateral sobre a tela.
 *
 * Sobreposição e não navegação: o pedido é poder mexer nos falantes ou nas
 * configurações <b>sem perder o lugar no texto</b>. Trocar de tela obrigaria a
 * rolar de volta até onde se estava.
 *
 * <b>É modal de verdade desde o FE-4</b>, e antes só parecia. O véu escondia o
 * fundo do olho e não do teclado: o Tab de dentro da gaveta caminhava para a
 * página atrás dela, e quem navega por teclado saía da gaveta sem nada dizer
 * que saiu — os botões continuavam sendo alcançados por baixo de uma cortina.
 * As quatro peças que faltavam são estas: `inert` no fundo (que é o que tira o
 * fundo do teclado <i>e</i> do leitor de tela, e é mais forte que prender o
 * Tab), o foco que entra, o Tab que dá a volta, e o foco que é devolvido a quem
 * abriu.
 */
export function abrirGaveta(id) {
  // Guardado antes do fecharGavetas: se já havia uma gaveta aberta, ele
  // devolveria o foco ao botão de antes e o "de onde viemos" seria o errado.
  const veioDe = document.activeElement;
  fecharGavetas();

  const gaveta = document.getElementById(id);
  gaveta.hidden = false;
  veu.hidden = false;

  // `aria-label` já está no HTML de cada uma; o que falta é dizer que é
  // diálogo, senão o leitor de tela a anuncia como mais uma região da página.
  gaveta.setAttribute("role", "dialog");
  gaveta.setAttribute("aria-modal", "true");
  gaveta.tabIndex = -1;

  fundo()?.setAttribute("inert", "");
  gavetaAberta = { gaveta, veioDe };
  document.addEventListener("keydown", darAVoltaNoTab, true);

  // O foco vai para a gaveta, e não para o primeiro botão dela: o primeiro
  // botão é o ✕, e entrar num painel ouvindo "fechar" não diz onde se está.
  // Quem quiser algo mais útil focado — o campo das notas, por exemplo — chama
  // focus() depois, e ganha.
  gaveta.focus({ preventScroll: true });
}

export function fecharGavetas() {
  for (const g of document.querySelectorAll(".gaveta")) {
    g.hidden = true;
    g.removeAttribute("role");
    g.removeAttribute("aria-modal");
  }
  veu.hidden = true;

  document.removeEventListener("keydown", darAVoltaNoTab, true);
  fundo()?.removeAttribute("inert");

  const aberta = gavetaAberta;
  gavetaAberta = null;
  // Sem isto o foco cai no <body> e o próximo Tab recomeça do trilho, a três
  // telas de distância de onde a pessoa estava. Só devolve se o elemento ainda
  // existir: quem abriu pode ter saído com a tela.
  if (aberta?.veioDe?.isConnected) aberta.veioDe.focus({ preventScroll: true });
}

/** O que fica atrás da gaveta. As gavetas e o véu são irmãos dele, não filhos. */
const fundo = () => document.querySelector(".app");

/**
 * Prende o Tab dentro da gaveta.
 *
 * O `inert` do fundo já faz quase tudo — o que ele não faz é impedir que o Tab
 * saia da página para a interface do navegador e volte pelo outro lado, que no
 * WebView2 é uma viagem sem nada visível acontecendo.
 */
function darAVoltaNoTab(e) {
  if (e.key !== "Tab" || !gavetaAberta) return;

  const focaveis = [...gavetaAberta.gaveta.querySelectorAll(FOCAVEL)]
    .filter((el) => el.offsetParent !== null || el === document.activeElement);
  if (focaveis.length === 0) return;

  const primeiro = focaveis[0];
  const ultimo = focaveis[focaveis.length - 1];
  const atual = document.activeElement;

  if (e.shiftKey && (atual === primeiro || atual === gavetaAberta.gaveta)) {
    e.preventDefault();
    ultimo.focus();
  } else if (!e.shiftKey && atual === ultimo) {
    e.preventDefault();
    primeiro.focus();
  }
}

// ────────────────────────────────────────── perguntar sem sair do app

/**
 * Confirma uma ação numa caixa do app, e não do Edge.
 *
 * Substitui os `confirm()` nativos. Não é preciosismo: no WebView2 eles saem
 * como diálogos do navegador, com o título do executável e fora do design
 * system, e <b>bloqueiam o laço de mensagens</b> — que neste app é o mesmo laço
 * da bandeja (App/Aplicacao.cs). Um `confirm()` aberto é o gravador sem quem
 * despache as mensagens dele.
 *
 * O `<dialog>` nativo é quem faz o trabalho difícil: fundo inerte, Tab preso,
 * Escape, e o foco devolvido a quem abriu. Não há por que reimplementar nada
 * disso — o modal de editar trecho já usava `showModal()` desde a Fase 3.
 *
 * @param texto pode ter quebras de linha; cada parágrafo vira um <p>.
 * @returns uma promessa que resolve `true` só se a pessoa confirmar. Escape,
 *          clique fora e o botão de cancelar resolvem `false` — nunca rejeita,
 *          porque desistir não é erro.
 */
export function confirmar(texto, opcoes = {}) {
  const { titulo = "Confirmar", ok = "Confirmar", cancelar = "Cancelar" } = opcoes;

  return new Promise((resolver) => {
    const { dialogo, corpo, acoes } = caixa(titulo, texto);

    const nao = document.createElement("button");
    nao.className = "aa-btn aa-btn-secundario";
    nao.type = "button";
    nao.textContent = cancelar;
    nao.addEventListener("click", () => dialogo.close(""));

    const sim = document.createElement("button");
    sim.className = "aa-btn aa-btn-primario";
    sim.type = "button";
    sim.textContent = ok;
    sim.addEventListener("click", () => dialogo.close("sim"));

    acoes.append(nao, sim);
    corpo.appendChild(acoes);

    abrirCaixa(dialogo, () => resolver(dialogo.returnValue === "sim"),
               // O foco começa em cancelar. Estas caixas existem porque a ação
               // destrói trabalho, e o Enter distraído não pode ser o "sim".
               nao);
  });
}

/**
 * Diz uma coisa e espera o "entendi" — o que o `alert()` nativo fazia.
 *
 * Mesmo motivo do `confirmar()`: o `alert()` do WebView2 bloqueia o laço de
 * mensagens, e neste app esse laço é o mesmo da bandeja. Os três que existiam
 * eram todos a mesma frase — "não deu para apagar/renomear" —, dita depois de
 * uma ação que falhou, e é justamente quando a caixa não pode parecer do
 * sistema operacional.
 *
 * @returns uma promessa que resolve quando a pessoa fecha. Nunca rejeita.
 */
export function avisar(texto, opcoes = {}) {
  const { titulo = "Aviso", ok = "Entendi" } = opcoes;

  return new Promise((resolver) => {
    const { dialogo, corpo, acoes } = caixa(titulo, texto);

    const botao = document.createElement("button");
    botao.className = "aa-btn aa-btn-primario";
    botao.type = "button";
    botao.textContent = ok;
    botao.addEventListener("click", () => dialogo.close(""));

    acoes.appendChild(botao);
    corpo.appendChild(acoes);

    // Aqui o foco vai no único botão: não há o que destruir, e a caixa existe
    // para ser fechada. É o oposto do confirmar(), e de propósito.
    abrirCaixa(dialogo, resolver, botao);
  });
}

/**
 * Pede um texto — o que o `prompt()` nativo fazia, pelo mesmo motivo do
 * `confirmar()` acima.
 *
 * @returns o texto aparado, ou nulo se a pessoa desistiu ou deixou vazio.
 */
export function perguntarTexto(rotulo, valor = "", opcoes = {}) {
  const { titulo = rotulo, ok = "Salvar", texto = "" } = opcoes;

  return new Promise((resolver) => {
    const { dialogo, corpo, acoes } = caixa(titulo, texto);

    const entrada = document.createElement("input");
    entrada.className = "aa-entrada";
    entrada.type = "text";
    entrada.value = valor;

    // Nome próprio para não sombrear o `campo()` exportado logo acima.
    const linha = document.createElement("label");
    linha.className = "campo";
    const span = document.createElement("span");
    span.textContent = rotulo;
    linha.append(span, entrada);
    corpo.appendChild(linha);

    // Enter confirma: é o gesto de quem acabou de digitar, e sem isto ele
    // fecha a caixa pelo `method="dialog"` implícito com retorno vazio.
    entrada.addEventListener("keydown", (e) => {
      if (e.key !== "Enter") return;
      e.preventDefault();
      dialogo.close("sim");
    });

    const nao = document.createElement("button");
    nao.className = "aa-btn aa-btn-secundario";
    nao.type = "button";
    nao.textContent = "Cancelar";
    nao.addEventListener("click", () => dialogo.close(""));

    const sim = document.createElement("button");
    sim.className = "aa-btn aa-btn-primario";
    sim.type = "button";
    sim.textContent = ok;
    sim.addEventListener("click", () => dialogo.close("sim"));

    acoes.append(nao, sim);
    corpo.appendChild(acoes);

    abrirCaixa(dialogo, () => {
      const v = entrada.value.trim();
      resolver(dialogo.returnValue === "sim" && v ? v : null);
    }, entrada);
  });
}

/** A casca das duas caixas acima: título, parágrafos e o lugar dos botões. */
function caixa(titulo, texto) {
  const dialogo = document.createElement("dialog");
  dialogo.className = "modal";

  const corpo = document.createElement("div");
  corpo.className = "modal__corpo";

  const h = document.createElement("h2");
  h.className = "modal__titulo";
  h.textContent = titulo;
  corpo.appendChild(h);

  // Um <p> por parágrafo em vez de white-space: as mensagens herdadas do
  // `confirm()` vêm com \n\n separando "o que" de "o que se perde", e essa
  // separação é a parte que faz a pessoa ler antes de clicar.
  for (const paragrafo of String(texto).split("\n").map((t) => t.trim())) {
    if (!paragrafo) continue;
    const p = document.createElement("p");
    p.className = "modal__texto";
    p.textContent = paragrafo;
    corpo.appendChild(p);
  }

  const acoes = document.createElement("div");
  acoes.className = "modal__acoes";

  dialogo.appendChild(corpo);
  return { dialogo, corpo, acoes };
}

/** Mostra a caixa, devolve o resultado e some — inclusive do DOM. */
function abrirCaixa(dialogo, aoFechar, focoInicial) {
  dialogo.addEventListener("close", () => {
    aoFechar();
    dialogo.remove();
  }, { once: true });

  // Clique no fundo cancela, como no véu das gavetas. O <dialog> não distingue
  // o backdrop do próprio elemento, então o alvo do clique é a pista: só o
  // backdrop reporta o <dialog> como alvo.
  dialogo.addEventListener("click", (e) => {
    if (e.target === dialogo) dialogo.close("");
  });

  document.body.appendChild(dialogo);
  dialogo.returnValue = "";
  dialogo.showModal();
  focoInicial?.focus();
}

/** Uma cor estável por falante, para o olho achar quem fala sem ler o nome.
 *
 * Os matizes vêm da paleta categórica do design system (--dados-*), em tokens
 * porque o app tem dois temas: os cinco hexadecimais fixos que estavam aqui
 * reprovavam em AA no escuro — o vinho ficava em 2,77:1, abaixo até do 3:1 de
 * texto grande —, e no escuro só o "Você" era legível porque só ele era token.
 * Os valores por tema estão no app.css; ver docs/FASE7-FRONTEND.md §12.3.
 *
 * São oito, e não seis: o ciclo deixa de começar na sexta pessoa e passa a
 * começar na nona. Cinco gravações do acervo têm 9 falantes ou mais.
 *
 * Até cinco falantes as cores se distinguem com folga; de seis em diante duas
 * ficam em tons vizinhos. Isso é propriedade dos oito matizes do design system,
 * não da derivação — cores de verdade distintas seriam cores novas, que ele não
 * tem. */
const PALETA = [
  "var(--falante-1)", "var(--falante-2)", "var(--falante-3)", "var(--falante-4)",
  "var(--falante-5)", "var(--falante-6)", "var(--falante-7)", "var(--falante-8)",
];

export function corDoFalante(nome, ordem) {
  // "You" é sempre o primeiro tom da paleta: é o falante que o usuário procura
  // primeiro quando revisa a própria fala.
  if (nome === "You") return PALETA[0];
  return PALETA[1 + (ordem % (PALETA.length - 1))];
}

/**
 * Para o áudio e apaga a marca de quem estava tocando.
 *
 * O <audio> é um só para o app inteiro (ver index.html): ele fica fora das
 * telas para sobreviver a abrir uma gaveta, que é o comportamento certo — quem
 * abre os falantes enquanto ouve um trecho não quer o áudio cortado. Mas
 * sobreviver à gaveta virou sobreviver à <b>troca de tela</b>: saindo de uma
 * reunião para outra, para o Gravador ou para os Ajustes, a gravação anterior
 * continuava tocando por cima da tela nova, sem nada visível para pará-la.
 *
 * Por isso mora aqui e não em revisao.js: quem toca são duas telas — os trechos
 * da revisão e as amostras de voz dos Ajustes —, e as duas dividem o mesmo
 * elemento e a mesma marca `data-tocando`.
 */
export function pararAudio() {
  const audio = document.getElementById("audio");
  if (!audio) return;

  audio.pause();
  // Sem isto o `onended` da amostra de voz dispararia depois, num botão que
  // pode já não existir na tela nova.
  audio.onended = null;
  // Solta o arquivo: o mix de uma reunião de 2 h passa de 200 MB, e mantê-lo
  // aberto por uma tela que não toca nada não paga. A revisão volta a montar a
  // src no próximo clique, e a amostra de voz sempre atribui a sua.
  audio.removeAttribute("src");

  for (const o of document.querySelectorAll("[data-tocando]"))
    o.removeAttribute("data-tocando");
}
