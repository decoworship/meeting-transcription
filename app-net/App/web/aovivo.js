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
import { novaLinha, mostraDono, contarPalavras } from "/legenda-regras.js";

/** O rótulo que a faixa do microfone dá, e que é certeza e não palpite. */
const DONO = "You";

/**
 * Monta o painel e o liga ao canal de eventos.
 *
 * @returns `{ raiz, encerrar }`. Quem sai de tela chama `encerrar()`.
 */
export function painelAoVivo(opcoes = {}) {
  // O relógio que carimba a legenda: o da gravação, que o Gravador passa. Sem
  // ele (a prova do painel sozinho), os segundos desde a montagem.
  const montado = performance.now();
  //: O "o texto aparece…" do estado vazio, que some na primeira fala.
  let vazioNo = null;
  const tempo = opcoes.tempo ?? (() => (performance.now() - montado) / 1000);

  const raiz = document.createElement("section");
  raiz.className = "bloco aovivo";

  const topo = document.createElement("div");
  topo.className = "aovivo__topo";

  const titulo = document.createElement("h2");
  titulo.className = "bloco__titulo";
  titulo.textContent = "Transcrição ao vivo";
  // É rascunho, e a tela diz isso ao lado do nome: quem é cada um e os últimos
  // minutos só a passada final sabe.
  const selo = document.createElement("span");
  selo.className = "aa-etiqueta aovivo__selo";
  selo.textContent = "rascunho";
  titulo.appendChild(selo);

  // "Seguindo a fala": o estado do seguimento, à direita do título. Some quando
  // a pessoa rola para cima, e o "voltar ao vivo" aparece no lugar.
  const seguindoRotulo = document.createElement("span");
  seguindoRotulo.className = "aovivo__seguindo";
  seguindoRotulo.textContent = "Seguindo a fala";

  // **O atraso, dito de frente — e o modo certo.** A dica ficava cravada em
  // "blocos de 3 minutos" e mentia quando a legenda era o que rodava. Ela nasce
  // vazia e o núcleo diz qual é o modo; até lá não se afirma nada.
  const dica = document.createElement("p");
  dica.className = "aovivo__dica";

  topo.append(titulo, seguindoRotulo);
  dica.className = "aovivo__rodape";

  // O "voltar ao vivo", que só existe quando o seguimento se soltou.
  const voltar = document.createElement("button");
  voltar.className = "aa-btn aa-btn-secundario aovivo__voltar";
  voltar.type = "button";
  voltar.textContent = "↓ voltar ao vivo";
  voltar.hidden = true;

  const corpo = document.createElement("div");
  corpo.className = "transcricao aovivo__corpo";

  const aviso = document.createElement("div");

  // **O append vem antes de montar a lista**, e não é estilo: a sentinela da
  // lista virtualizada precisa estar no DOM para o observador enxergá-la. Foi
  // um dos dois defeitos que deixaram este painel sem desenhar nada até
  // 10/09/2026.
  raiz.append(topo, aviso, corpo, voltar, dica);

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
    seguindoRotulo.hidden = !seguindo;
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

  //: A linha que está crescendo: `{ no, texto, dono, ate_s, palavras }`. A
  //: legenda não vem em segmentos — vem em texto que firma aos poucos —, então
  //: **quem dá forma é a tela**: o texto cresce na mesma linha até o dono trocar
  //: ou haver pausa (a régua do legenda-regras.js). Cada linha nova ganha o
  //: próprio tempo, e o dono só aparece na primeira da sequência.
  let linha = null;
  //: A linha volátil, com o que o motor ainda pode reescrever. Ela vive fora da
  //: lista porque não é um item: é um rascunho que some quando firma.
  let volatil = null;

  function acrescentarLegenda(g) {
    const t = tempo();
    if (g.novo && g.novo.trim()) {
      if (novaLinha(linha, { dono: g.dono, t })) {
        const anterior = linha;
        const no = linhaDeFala(t, g.dono, mostraDono(anterior, g.dono), "");
        corpo.insertBefore(no.raiz, volatil?.raiz ?? null);
        linha = { no, texto: "", dono: g.dono, ate_s: t, palavras: 0 };
        esconderVazio();
      }
      linha.texto = (linha.texto + g.novo).replace(/\s+/g, " ").trimStart();
      linha.no.texto.textContent = linha.texto;
      linha.ate_s = t;
      linha.palavras = contarPalavras(linha.texto);
      lista.seguirAoFim();
    }

    // O tentativo, em cinza e fora do fluxo firme: quem lê precisa saber que
    // aquilo ainda pode mudar. Sem essa distinção a tela treme e ninguém
    // entende por quê.
    const texto = (g.tentativo || "").trim();
    if (!texto) { volatil?.raiz.remove(); volatil = null; return; }
    if (!volatil) {
      volatil = linhaDeFala(null, g.dono, true, "");
      volatil.raiz.classList.add("fala--volatil");
      corpo.appendChild(volatil.raiz);
      esconderVazio();
    }
    volatil.raiz.dataset.dono = String(g.dono);
    // O dono do rascunho segue a mesma regra: continuação da linha firme do
    // mesmo dono não repete o nome.
    volatil.dono.textContent = mostraDono(linha, g.dono) ? rotuloDoDono(g.dono) : "";
    volatil.texto.textContent = texto;
    lista.seguirAoFim();
  }

  function esconderVazio() { if (vazioNo) vazioNo.hidden = true; }

  /**
   * O momento marcado aparece no trecho: a etiqueta pendura na última linha
   * firme. É só vista — o registro do momento é a marca nas notas.
   */
  function marcarMomento() {
    const alvo = linha?.no.raiz ?? corpo.querySelector(".fala:not(.fala--volatil):last-of-type");
    if (!alvo || alvo.querySelector(".fala__momento")) return;
    const m = document.createElement("span");
    m.className = "fala__momento";
    m.textContent = "momento marcado";
    alvo.querySelector(".fala__corpo").appendChild(m);
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
    opcoes.aoSaber?.(r);
    dica.textContent = r.aovivo_modo === "legenda"
      ? "Rascunho da legenda. Quem é cada um e os últimos minutos entram na transcrição do fim."
      : r.aovivo_modo === "bloco"
        ? "Em blocos de 3 minutos — o texto aparece depois que cada bloco fecha."
        : "";
    if (r.aovivo_impedimento)
      aviso.replaceChildren(alerta(r.aovivo_impedimento, "atencao"));
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
    const dono = (t.speaker || "") === DONO;
    const no = linhaDeFala(t.start, dono, true, (t.text || "").trim());
    no.raiz.dataset.indice = String(indice);
    return no.raiz;
  }

  function vazio() {
    const p = document.createElement("p");
    vazioNo = p;
    p.className = "aovivo__dica";
    p.textContent = "O texto aparece conforme a reunião acontece.";
    return p;
  }

  return { raiz, marcarMomento, encerrar() { cancelar(); lista.encerrar(); } };
}

const rotuloDoDono = (dono) => (dono ? "Você" : "Outros");

/**
 * Uma linha da legenda: tempo · dono · texto.
 *
 * **Ao vivo não se diz quem falou — só se é seu ou não.** Decidido em
 * 10/09/2026: a costura de falantes por bloco divide gente demais (15
 * identidades para 8 pessoas, docs/FASE7-RESULTADOS.md §11.2), e "Falante 7"
 * numa reunião de quatro é uma afirmação errada com cara de certeza. "Você" é o
 * que a faixa do microfone sabe, e é fato. Quem é cada um vem na passada final.
 *
 * @param t segundos, ou nulo para o rascunho ("agora").
 */
function linhaDeFala(t, dono, comDono, texto) {
  const raiz = document.createElement("div");
  raiz.className = "fala";
  raiz.dataset.dono = String(dono);

  const quando = document.createElement("span");
  quando.className = "fala__tempo";
  quando.textContent = t === null ? "agora" : relogio(t);

  const quem = document.createElement("span");
  quem.className = "fala__dono";
  quem.textContent = comDono ? rotuloDoDono(dono) : "";

  const corpoDaFala = document.createElement("div");
  corpoDaFala.className = "fala__corpo";
  const p = document.createElement("p");
  p.className = "fala__texto";
  p.textContent = texto;
  corpoDaFala.appendChild(p);

  raiz.append(quando, quem, corpoDaFala);
  return { raiz, texto: p, dono: quem };
}

/** "00:29:12" — o mesmo relógio da faixa do Gravador e das notas. */
function relogio(s) {
  const t = Math.max(0, Math.floor(s || 0));
  const p = (n) => String(n).padStart(2, "0");
  return `${p(Math.floor(t / 3600))}:${p(Math.floor((t % 3600) / 60))}:${p(t % 60)}`;
}
