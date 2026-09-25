// O tocador da reunião aberta: uma faixa fixa no pé, com tocar/pausar, o tempo
// e a posição.
//
// Antes dele, o único sinal de que havia áudio tocando era um ⏸ solto nas
// ferramentas da revisão (spec 2026-09-23-ui-ux.md §1, item 7). O <audio> é um
// só para o app inteiro (index.html) e quem o solta na troca de tela é o
// `pararAudio` de pecas.js; este módulo só o aponta e o mostra.

import { relogioDoTocador } from "/reuniao-regras.js";

const audio = document.getElementById("audio");

/**
 * Toca o mix de uma gravação a partir de um instante.
 *
 * O arquivo é a mesma soma das faixas que o ASR ouviu, então os tempos da
 * transcrição e das notas batem com o que se escuta. Mapeado em JanelaDoApp: o
 * WebView2 serve direto do disco, com Range, que é o que faz pular para o meio
 * de um WAV de 200 MB ser instantâneo.
 */
export function ouvir(g, segundos) {
  const src = `https://gravacoes.local/${encodeURIComponent(g.nome)}/mix.wav`;
  if (audio.getAttribute("src") !== src) audio.src = src;
  audio.currentTime = segundos;
  audio.play().catch(() => {});
}

/** A faixa do pé da reunião. */
export function tocadorDaReuniao(g) {
  const raiz = document.createElement("div");
  raiz.className = "tocador";

  const botao = document.createElement("button");
  botao.type = "button";
  botao.className = "aa-btn aa-btn-secundario tocador__botao";
  botao.dataset.acao = "tocar";

  const tempo = document.createElement("span");
  tempo.className = "tocador__tempo";

  const posicao = document.createElement("input");
  posicao.type = "range";
  posicao.className = "tocador__posicao";
  posicao.min = "0";
  posicao.max = String(Math.max(1, Math.round(g.duracao_s || 0)));
  posicao.step = "1";
  posicao.value = "0";
  posicao.setAttribute("aria-label", "Posição no áudio");

  const dica = document.createElement("span");
  dica.className = "tocador__dica";
  dica.textContent = "Clique num trecho para ouvir · duplo clique para corrigir";

  raiz.append(botao, tempo, posicao, dica);

  // O estado vem dos eventos, e não de `audio.paused`: é o que o <audio> diz
  // que aconteceu, inclusive quando quem pausou foi o `pararAudio` da troca de tela.
  let tocando = false;
  const total = relogioDoTocador(g.duracao_s);

  function pintar() {
    botao.textContent = tocando ? "❚❚" : "▶";
    botao.setAttribute("aria-label", tocando ? "Pausar" : "Tocar");
    const agora = audio.hasAttribute("src") ? audio.currentTime : 0;
    tempo.textContent = `${relogioDoTocador(agora)} / ${total}`;
    // Enquanto a pessoa arrasta, a posição é dela.
    if (document.activeElement !== posicao) posicao.value = String(Math.floor(agora));
  }

  // Os ouvintes são do <audio>, que vive fora da tela: sem soltá-los, cada
  // reunião aberta deixaria um tocador desconectado repintando a cada evento.
  const vida = new AbortController();
  const ouvinte = (fn) => () => {
    if (!raiz.isConnected) { vida.abort(); return; }
    fn();
    pintar();
  };
  audio.addEventListener("play", ouvinte(() => { tocando = true; }), { signal: vida.signal });
  audio.addEventListener("pause", ouvinte(() => { tocando = false; }), { signal: vida.signal });
  audio.addEventListener("ended", ouvinte(() => { tocando = false; }), { signal: vida.signal });
  audio.addEventListener("timeupdate", ouvinte(() => {}), { signal: vida.signal });

  botao.addEventListener("click", () => {
    if (tocando) audio.pause();
    else ouvir(g, audio.hasAttribute("src") ? audio.currentTime : Number(posicao.value));
  });

  posicao.addEventListener("input", () => {
    const s = Number(posicao.value);
    if (audio.hasAttribute("src")) audio.currentTime = s;
    tempo.textContent = `${relogioDoTocador(s)} / ${total}`;
  });

  pintar();
  return raiz;
}
