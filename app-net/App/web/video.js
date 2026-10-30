// A tela gravada da reunião, com a transcrição como legenda por cima.
//
// **O áudio manda, e o vídeo segue.** O vídeo não tem som (Nucleo/VideoDaTela.cs):
// quem toca é o mesmo <audio> do tocador do pé, com o `mix.wav` que o ASR
// ouviu, e o vídeo é arrastado até o instante dele menos o `video_inicio_s`.
// Assim a legenda, o clique num trecho e as notas continuam falando os tempos
// do áudio, e nada precisa ser convertido.
//
// **A legenda é desenhada aqui, e não por <track>.** O WebVTT do navegador não
// mostra quem fala com cor nem troca de tamanho sem regerar o arquivo, e some
// do elemento quando ele vai para tela cheia sozinho. Desenhada por cima, ela
// vai junto com o palco inteiro.

import { ouvir } from "/tocador.js";
import { corDoFalante } from "/pecas.js";
import { legendaNoInstante, tempoNoVideo, relogioDoTocador } from "/reuniao-regras.js";

const audio = document.getElementById("audio");

/** Acima disto, o vídeo pula até o áudio; abaixo, é a diferença de um quadro. */
const DERIVA_MAXIMA_S = 0.3;

const PREFERENCIAS = "pulsemeet.video";
const PADRAO = { legenda: true, falante: true, tamanho: "media" };

function lerPreferencias() {
  try {
    return { ...PADRAO, ...JSON.parse(localStorage.getItem(PREFERENCIAS) ?? "{}") };
  } catch {
    return { ...PADRAO };
  }
}

function guardarPreferencias(p) {
  try { localStorage.setItem(PREFERENCIAS, JSON.stringify(p)); } catch { /* fica só nesta sessão */ }
}

function el(tag, classe, texto) {
  const e = document.createElement(tag);
  if (classe) e.className = classe;
  if (texto !== undefined) e.textContent = texto;
  return e;
}

/** Uma caixinha com rótulo, que devolve o <input>. */
function marcar(rotulo, ligada, aoMudar) {
  const label = el("label", "video__opcao");
  const caixa = document.createElement("input");
  caixa.type = "checkbox";
  caixa.checked = ligada;
  caixa.addEventListener("change", () => aoMudar(caixa.checked));
  label.append(caixa, document.createTextNode(rotulo));
  return label;
}

/**
 * O painel da aba Vídeo da reunião aberta.
 *
 * @param g a gravação, com `nome` e `video_inicio_s`.
 * @param dados a transcrição aberta. Os `segments` são os mesmos objetos que a
 *   revisão edita, então um falante renomeado lá já sai renomeado aqui.
 */
export function painelDoVideo(g, dados) {
  const inicio = Number(g.video_inicio_s) || 0;
  const pref = lerPreferencias();

  const raiz = el("div", "video");
  const palco = el("div", "video__palco");
  palco.tabIndex = 0;
  palco.setAttribute("aria-label", "Vídeo da reunião. Espaço toca e pausa; setas andam cinco segundos.");

  const video = document.createElement("video");
  video.className = "video__tela";
  video.muted = true;
  video.playsInline = true;
  video.preload = "metadata";
  video.src = `https://gravacoes.local/${encodeURIComponent(g.nome)}/tela.mp4`;

  const antes = el("p", "video__antes", `A tela começou a ser gravada em ${relogioDoTocador(inicio)}.`);
  antes.hidden = true;

  const legenda = el("div", "video__legenda");
  legenda.setAttribute("aria-hidden", "true");   // é o texto da aba Transcrição, repetido
  const quem = el("span", "video__falante");
  const texto = el("span", "video__texto");
  legenda.append(quem, texto);

  palco.append(video, antes, legenda);

  // ---- opções
  const opcoes = el("div", "video__opcoes");
  const aplicar = () => {
    palco.dataset.tamanho = pref.tamanho;
    legenda.hidden = !pref.legenda;
    quem.hidden = !pref.falante;
    guardarPreferencias(pref);
    pintar();
  };
  const tamanho = document.createElement("select");
  tamanho.className = "aa-entrada video__tamanho";
  tamanho.setAttribute("aria-label", "Tamanho da legenda");
  for (const [valor, rotulo] of [["pequena", "Legenda pequena"], ["media", "Legenda média"], ["grande", "Legenda grande"]]) {
    const o = document.createElement("option");
    o.value = valor;
    o.textContent = rotulo;
    tamanho.appendChild(o);
  }
  tamanho.value = pref.tamanho;
  tamanho.addEventListener("change", () => { pref.tamanho = tamanho.value; aplicar(); });

  const ampliar = el("button", "aa-btn aa-btn-secundario", "Tela cheia");
  ampliar.type = "button";
  ampliar.addEventListener("click", () => {
    if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
    else palco.requestFullscreen().catch(() => {});
  });

  opcoes.append(
    marcar("Legenda", pref.legenda, (v) => { pref.legenda = v; aplicar(); }),
    marcar("Quem fala", pref.falante, (v) => { pref.falante = v; aplicar(); }),
    tamanho, ampliar);

  raiz.append(palco, opcoes);

  // ---- a cor de cada falante, na ordem em que aparecem, como na revisão
  const ordem = [];
  for (const s of dados.segments) {
    const n = s.speaker ?? "Unknown";
    if (!ordem.includes(n)) ordem.push(n);
  }

  // ---- sincronia
  const tocando = () => audio.hasAttribute("src") && !audio.paused;
  const agora = () => (audio.hasAttribute("src") ? audio.currentTime : 0);
  let ultimaLegenda = null;

  function pintar() {
    const t = agora();
    const l = pref.legenda ? legendaNoInstante(dados.segments, t) : null;
    const chave = l ? `${l.indice}|${l.falante}|${l.texto}` : null;
    if (chave !== ultimaLegenda) {
      ultimaLegenda = chave;
      legenda.classList.toggle("video__legenda--vazia", !l);
      texto.textContent = l?.texto ?? "";
      quem.textContent = l?.falante ?? "";
      quem.style.setProperty("--cor-do-falante",
        l?.falante ? corDoFalante(l.falante, ordem.indexOf(l.falante)) : "transparent");
    }
  }

  function sincronizar() {
    const alvo = tempoNoVideo(agora(), inicio);
    antes.hidden = alvo >= 0 || !audio.hasAttribute("src");
    if (alvo < 0) {
      if (!video.paused) video.pause();
      if (video.currentTime !== 0) video.currentTime = 0;
    } else {
      if (Math.abs(video.currentTime - alvo) > DERIVA_MAXIMA_S) video.currentTime = alvo;
      if (tocando() && video.paused) video.play().catch(() => {});
      if (!tocando() && !video.paused) video.pause();
    }
    pintar();
  }

  // Enquanto toca, a cada quadro de tela: o `timeupdate` do <audio> vem só
  // quatro vezes por segundo, e a legenda trocaria atrasada.
  let quadro = 0;
  function laco() {
    quadro = 0;
    if (!raiz.isConnected) { vida.abort(); return; }
    if (!raiz.offsetParent && !document.fullscreenElement) {   // aba escondida
      if (!video.paused) video.pause();
      return;
    }
    sincronizar();
    if (tocando()) quadro = requestAnimationFrame(laco);
  }
  const acordar = () => { if (!quadro) quadro = requestAnimationFrame(laco); };

  const vida = new AbortController();
  for (const evento of ["play", "pause", "seeked", "timeupdate", "emptied"]) {
    audio.addEventListener(evento, () => {
      if (!raiz.isConnected) { vida.abort(); return; }
      acordar();
    }, { signal: vida.signal });
  }

  function alternar() {
    if (tocando()) audio.pause();
    else ouvir(g, agora()).catch(() => {});
  }
  function andar(segundos) {
    const destino = Math.max(0, agora() + segundos);
    if (audio.hasAttribute("src")) audio.currentTime = destino;
    else ouvir(g, destino).then(() => audio.pause()).catch(() => {});
  }

  video.addEventListener("click", alternar);
  video.addEventListener("loadedmetadata", sincronizar);
  palco.addEventListener("keydown", (e) => {
    const acao = { " ": () => alternar(), ArrowRight: () => andar(5), ArrowLeft: () => andar(-5) }[e.key];
    if (!acao) return;
    e.preventDefault();
    acao();
  });

  aplicar();
  return {
    raiz,
    /** Chamado quando a aba aparece: o vídeo se põe no instante do áudio. */
    mostrar: () => acordar(),
  };
}
