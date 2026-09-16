// O painel da prévia: o que já foi dito, enquanto a reunião acontece.
//
// **Não chame isto de tempo real, nem aqui nem na tela.** São blocos de 3
// minutos: uma frase dita no minuto 10 aparece entre o 12 e o 13 — o bloco
// fecha aos 12 e leva ~20 s para ser transcrito. Quem espera legenda e recebe
// bloco de três minutos acha que está quebrado. O nome é *consciência da
// reunião*, e a tela diz o atraso em vez de escondê-lo
// (docs/FASE7-FRONTEND.md §9).
//
// **Ele vive fora do `aplicar()` do gravador**, que roda cinco vezes por segundo
// e reconstrói os avisos a cada volta. Pôr a lista lá dentro seria redesenhar a
// transcrição inteira 5×/s na tela do app que está gravando a reunião — o §7 do
// documento marca isso como o erro a não cometer.
//
// **A lista é a mesma da revisão** (`lista-de-trechos.js`), pelo `acrescentar()`
// dela: duas listas escritas em separado divergem, e a que diverge é sempre a
// que ninguém está olhando.

import { assinar, pedir } from "/ponte.js";
import { alerta } from "/pecas.js";
import { listaDeTrechos } from "/lista-de-trechos.js";

/** O rótulo que a faixa do microfone dá, e que é certeza e não palpite. */
const DONO = "You";

/**
 * Monta o painel e o liga ao canal de eventos.
 *
 * @returns `{ raiz, encerrar }`. Quem sai de tela chama `encerrar()`.
 */
export function painelAoVivo() {
  const raiz = document.createElement("section");
  raiz.className = "bloco aovivo";

  const topo = document.createElement("div");
  topo.className = "aovivo__topo";

  const titulo = document.createElement("h2");
  titulo.className = "bloco__titulo";
  titulo.textContent = "O que já foi dito";

  // **O atraso, dito de frente — e o modo certo.** A dica ficava cravada em
  // "blocos de 3 minutos" e mentia quando a legenda era o que rodava. Ela nasce
  // vazia e o núcleo diz qual é o modo; até lá não se afirma nada.
  const dica = document.createElement("p");
  dica.className = "aovivo__dica";

  topo.append(titulo, dica);

  // O "voltar ao vivo", que só existe quando o seguimento se soltou.
  const voltar = document.createElement("button");
  voltar.className = "aa-btn aa-btn-secundario aovivo__voltar";
  voltar.type = "button";
  voltar.textContent = "↓ voltar ao vivo";
  voltar.hidden = true;

  const corpo = document.createElement("div");
  corpo.className = "transcricao aovivo__corpo";

  const aviso = document.createElement("div");

  // ── Perguntar ao modelo o que já aconteceu ───────────────────────────────
  //
  // **O motor sobe por pergunta e morre depois dela**, e é por isso que cada
  // resposta leva uns dez segundos. Um 4B quente a reunião inteira é o terceiro
  // contexto CUDA que derrubou a legenda de 2,46x para 0,45x em 11/09/2026
  // (docs/FASE7-ROTA.md §4) — e legenda abaixo de 1x atrasa sem parar.
  //
  // **A resposta fica acima do campo**, e não dentro da lista: a lista é o que
  // foi dito, e misturar nela o que um modelo deduziu apagaria a diferença
  // entre o que alguém falou e o que a máquina achou.
  // **As respostas acumulam, a mais nova no topo.** Antes cada pergunta
  // apagava a anterior, e perguntar o detalhe custava o que veio antes — visto
  // em uso em 16/09/2026. O painel é superfície de relance: o que se acabou de
  // pedir tem de estar em cima, sem rolar.
  const respostas = document.createElement("div");
  respostas.className = "aovivo__respostas";

  const perguntar = document.createElement("form");
  perguntar.className = "aovivo__perguntar";

  // O botão do resumo, que é a primeira interação: ele manda a instrução de
  // lista, e a caixa livre ao lado é por onde se pede o detalhe.
  const resumir = document.createElement("button");
  resumir.className = "aa-btn aa-btn-secundario aovivo__resumir";
  resumir.type = "button";
  resumir.textContent = "O que já rolou?";

  const campo = document.createElement("input");
  campo.className = "aa-entrada";
  campo.type = "text";
  campo.placeholder = "Pergunte o que já aconteceu…";

  const enviar = document.createElement("button");
  enviar.className = "aa-btn";
  enviar.type = "submit";
  enviar.textContent = "Perguntar";

  perguntar.append(campo, enviar);

  // **Nasce escondida.** O núcleo diz no `aovivo` se ela pode existir; até lá
  // não se promete caixa nenhuma. Mostrá-la com a chave desligada faria a
  // pessoa escrever a pergunta para só então descobrir o impedimento.
  perguntar.hidden = true;
  resumir.hidden = true;

  raiz.append(topo, aviso, corpo, voltar, resumir, respostas, perguntar);

  resumir.addEventListener("click", () => enviarPergunta("O que já rolou?", true));

  perguntar.addEventListener("submit", (ev) => {
    ev.preventDefault();
    const pergunta = campo.value.trim();
    if (pergunta) enviarPergunta(pergunta, false);
  });

  /**
   * Manda a pergunta e devolve um bloco novo no topo.
   *
   * @param resumo quando `true`, vai a instrução de lista do botão; quando
   *   `false`, a pergunta é livre e o núcleo só acrescenta as regras.
   */
  async function enviarPergunta(pergunta, resumo) {
    if (enviar.disabled) return;
    enviar.disabled = true;
    resumir.disabled = true;

    const bloco = document.createElement("div");
    bloco.className = "aovivo__resposta";
    bloco.dataset.estado = "esperando";
    bloco.append(linhaDaPergunta(pergunta),
                 paragrafo("carregando o modelo…", "aovivo__resposta-texto"));
    respostas.prepend(bloco);

    try {
      const r = await pedir("perguntar-ao-vivo", { pergunta, resumo }, (p) => {
        if (raiz.isConnected && p.texto)
          bloco.replaceChildren(linhaDaPergunta(pergunta),
                                paragrafo(p.texto, "aovivo__resposta-texto"));
      });
      if (!raiz.isConnected) return;

      bloco.dataset.estado = "pronta";
      const partes = [linhaDaPergunta(pergunta),
                      paragrafo(r.resposta || "", "aovivo__resposta-texto")];
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
    return paragrafo(texto, "aovivo__resposta-pergunta");
  }

  function paragrafo(texto, classe) {
    const el = document.createElement("p");
    el.className = classe;
    el.textContent = texto;
    return el;
  }

  // A coluna rola sozinha — é o que a decisão D1 pede (duas colunas com
  // rolagens separadas enquanto grava) e o que permite ler o que foi dito há
  // dez minutos sem perder o fim.
  const lista = listaDeTrechos(corpo, linhaDoTrecho, vazio, corpo);
  // **O estado vazio é montado pelo `definir()`**, e sem esta chamada a dica
  // "o primeiro bloco chega depois de ~3 minutos" nunca aparecia — o painel
  // ficava em branco durante os três primeiros minutos, que é justamente quando
  // a pessoa olha para ele perguntando se ligou.
  lista.definir([]);

  let seguindo = true;
  lista.seguir(true);

  // O seguimento se solta ao rolar para cima, e volta pelo botão. Sem isso,
  // reler o que foi dito há dez minutos é impossível durante a reunião — que é
  // justamente o motivo pelo qual este painel vale a pena.
  corpo.addEventListener("scroll", () => {
    const noFim = lista.noFim();
    if (seguindo === noFim) return;
    seguindo = noFim;
    lista.seguir(seguindo);
    voltar.hidden = seguindo;
  });

  voltar.addEventListener("click", () => {
    seguindo = true;
    lista.seguir(true);
    voltar.hidden = true;
  });

  const cancelar = assinar("aovivo", (ev) => {
    // A disciplina de sempre: uma tela que já saiu não desenha mais.
    if (!raiz.isConnected) { cancelar(); return; }
    if (ev.aovivo) acrescentarBloco(ev.aovivo);
    if (ev.legenda) acrescentarLegenda(ev.legenda);
  });

  //: O balão que está crescendo, e de quem ele é. A legenda não vem em
  //: segmentos — vem em texto que firma aos poucos —, então **quem dá forma é a
  //: tela**: enquanto o dono não muda, o texto cresce no mesmo balão; quando
  //: muda, começa outro. É o que faz uma conversa parecer conversa.
  let balao = null;
  let donoDoBalao = null;
  //: O balão volátil, com o que o motor ainda pode reescrever. Ele vive fora da
  //: lista porque não é um item: é um rascunho que some quando firma.
  let volatil = null;

  function acrescentarLegenda(g) {
    if (g.novo) {
      if (balao === null || donoDoBalao !== g.dono) {
        balao = document.createElement("div");
        balao.className = "fala";
        balao.dataset.dono = String(g.dono);
        const t = document.createElement("p");
        t.className = "fala__texto";
        balao.appendChild(t);
        donoDoBalao = g.dono;
        corpo.insertBefore(balao, volatil);
        lista.seguirAoFim();
      }
      const t = balao.querySelector(".fala__texto");
      t.textContent = (t.textContent + g.novo).replace(/\s+/g, " ").trimStart();
    }

    // O tentativo, em cinza e fora do fluxo firme: quem lê precisa saber que
    // aquilo ainda pode mudar. Sem essa distinção a tela treme e ninguém
    // entende por quê.
    const texto = (g.tentativo || "").trim();
    if (!texto) { volatil?.remove(); volatil = null; return; }
    if (!volatil) {
      volatil = document.createElement("div");
      volatil.className = "fala fala--volatil";
      volatil.appendChild(document.createElement("p")).className = "fala__texto";
      corpo.appendChild(volatil);
    }
    volatil.dataset.dono = String(g.dono);
    volatil.querySelector(".fala__texto").textContent = texto;
    lista.seguirAoFim();
  }

  // Duas coisas, numa pergunta só, ao montar:
  //
  // 1. o impedimento — sem ele o painel ficaria vazio para sempre sem dizer por
  //    quê, que é a forma de falha que este projeto mais evita;
  // 2. **o que já aconteceu.** O painel só existe enquanto o Gravador está
  //    montado: quem estava em Reuniões no minuto 20 e volta encontraria um
  //    painel vazio, porque os blocos de antes já passaram pelo canal de
  //    eventos. O núcleo os guarda justamente para esta volta.
  pedir("aovivo").then((r) => {
    if (!raiz.isConnected) return;
    dica.textContent = r.aovivo_modo === "legenda"
      ? "O texto aparece conforme a fala, e o que ainda pode mudar fica em cinza."
      : r.aovivo_modo === "bloco"
        ? "Em blocos de 3 minutos — o texto aparece depois que cada bloco fecha."
        : "";
    if (r.aovivo_impedimento)
      aviso.replaceChildren(alerta(r.aovivo_impedimento, "atencao"));
    // A caixa de perguntar tem chave própria, e não depende de a legenda ou a
    // prévia estarem ligadas: ela lê o que houver, e se não houver nada ela diz.
    perguntar.hidden = !!r.perguntar_impedimento;
    resumir.hidden = perguntar.hidden;
    for (const b of r.aovivo_ate ?? []) acrescentarBloco(b);
  }).catch(() => {
    // Sem resposta não se afirma nada: o painel só fica esperando bloco.
  });

  function acrescentarBloco(b) {
    const separador = { separador: true, bloco: b };
    const itens = [{ item: separador, indice: -1 - b.n }];

    for (const t of b.trechos ?? [])
      itens.push({ item: t, indice: itens.length + b.n * 1000 });
    lista.acrescentar(itens);
  }

  /** O separador de bloco, ou uma linha de trecho. */
  function linhaDoTrecho(item, indice) {
    return item.separador ? separadorDeBloco(item.bloco) : trecho(item, indice);
  }

  /**
   * O separador que nomeia o bloco e o estado dele.
   *
   * É a decisão D4: `36:00 – 39:00 · provisório`. Sem ele não há como
   * distinguir "não falaram" de "ainda não processei" — e essa distinção é
   * metade do que o painel existe para dar.
   */
  function separadorDeBloco(b) {
    const el = document.createElement("p");
    el.className = "aovivo__bloco";
    el.dataset.estado = b.estado;
    const quando = `${relogio(b.inicio_s)} – ${relogio(b.fim_s)}`;
    el.textContent = b.estado === "mudo"
      ? `${quando} · ninguém falou`
      : `${quando} · provisório`;
    return el;
  }

  // **O item chega cru, e o índice vem à parte.** É o contrato do
  // `desenharLinha` da lista-de-trechos.js — `mais()` desestrutura
  // `{ item, indice }` e chama `desenharLinha(item, indice)`, que é como a
  // revisão a usa. Esta função desembrulhava um embrulho que já tinha sido
  // aberto, e `t` saía `undefined` na primeira linha desenhada.
  /**
   * Uma fala, em formato de conversa: a sua à direita, a dos outros à esquerda.
   *
   * **Ao vivo não se diz quem falou — só se é seu ou não.** Decidido em
   * 10/09/2026, e não é economia de tela: a costura de falantes por bloco
   * **divide gente demais** (15 identidades para 8 pessoas na reunião de 122
   * min, docs/FASE7-RESULTADOS.md §11.2), e "Falante 7" numa reunião de quatro
   * é uma afirmação errada com cara de certeza. O lado da tela afirma só o que
   * a faixa do microfone sabe, que é fato e não palpite.
   *
   * **E o lado substitui o rótulo**, em vez de conviver com ele: sem a coluna
   * de nome, o texto ganha a largura que ela ocupava — que é o que torna isto
   * legível numa coluna estreita ao lado do gravador.
   *
   * Quem é cada um continua vindo depois, na passada final, que é a única que
   * vê a reunião inteira de uma vez.
   */
  function trecho(t, indice) {
    const fala = document.createElement("div");
    fala.className = "fala";
    fala.dataset.indice = String(indice);
    fala.dataset.dono = String((t.speaker || "") === DONO);

    const texto = document.createElement("p");
    texto.className = "fala__texto";
    texto.textContent = (t.text || "").trim();

    const quando = document.createElement("span");
    quando.className = "fala__tempo";
    quando.textContent = relogio(t.start);

    fala.append(texto, quando);
    return fala;
  }

  function vazio() {
    const p = document.createElement("p");
    p.className = "aovivo__dica";
    p.textContent = "O texto aparece conforme a reunião acontece.";
    return p;
  }

  return { raiz, encerrar() { cancelar(); lista.encerrar(); } };
}

/** mm:ss, ou h:mm:ss quando passa da hora. */
function relogio(s) {
  const t = Math.max(0, Math.floor(s || 0));
  const h = Math.floor(t / 3600), m = Math.floor((t % 3600) / 60), seg = t % 60;
  const mm = String(m).padStart(2, "0"), ss = String(seg).padStart(2, "0");
  return h > 0 ? `${h}:${mm}:${ss}` : `${mm}:${ss}`;
}
