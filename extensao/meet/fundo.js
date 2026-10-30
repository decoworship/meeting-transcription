// O fundo da extensão: junta o que as abas do Meet e do Teams dizem e conversa
// com o PulseMeet por uma porta local. É a única peça que fala com ele — a
// página da reunião não pode, porque ela é de outro site.
//
// **Acorda e dorme.** O Chrome desliga o service worker depois de meio minuto
// parado; os avisos das abas (a cada dez segundos) o acordam, e o WebSocket com
// troca de mensagens o mantém de pé. Sem reunião aberta ele dorme, e a conexão
// cai junto — o PulseMeet trata isso como "sem extensão", que é verdade.

"use strict";

const PORTA = 47821;
const RECONECTAR_MS = 5_000;
const OI_MS = 20_000;

/** tabId → o título que o quadro de cima da página disse. */
const titulos = new Map();

/**
 * "tabId:frameId" → { tab, frame, app, em_reuniao, mudo, quando }. Por quadro,
 * e não por aba: o Teams pode pôr a reunião num iframe, e cada quadro da página
 * manda o seu estado — o que não tem o botão diria "fora da reunião" por cima
 * do que tem.
 */
const quadros = new Map();
let ativo = null;
let ws = null;
let tentativa = null;

/**
 * O quadro que vale: o último que mudou estando numa reunião. Duas reuniões
 * abertas é raro, e quem mexeu por último é quem está falando.
 */
function escolherAtivo() {
  let melhor = null;
  for (const [chave, e] of quadros) {
    if (e.em_reuniao && (!melhor || e.quando > quadros.get(melhor).quando)) melhor = chave;
  }
  ativo = melhor;
}

function estadoDoAtivo() {
  const e = ativo !== null ? quadros.get(ativo) : null;
  return e
    ? { tipo: "estado", app: e.app, em_reuniao: true, mudo: e.mudo }
    : { tipo: "estado", em_reuniao: false, mudo: false };
}

function enviar(m) {
  if (ws?.readyState === WebSocket.OPEN) ws.send(JSON.stringify(m));
}

/**
 * A aba da reunião para o vídeo: o título e se ela está na frente — a aba ativa
 * da janela dela, e a janela não minimizada. Nulo sem reunião.
 *
 * O título vem do `chrome.tabs` quando ele o dá, e senão do que a página disse:
 * sem a permissão "tabs", o Chrome só entrega o título das abas dos sites da
 * extensão, que são justamente as da reunião — mas a página é a garantia.
 */
async function abaDoAtivo() {
  const e = ativo !== null ? quadros.get(ativo) : null;
  if (!e) return null;
  try {
    const tab = await chrome.tabs.get(e.tab);
    const janela = await chrome.windows.get(tab.windowId);
    const titulo = tab.title || titulos.get(e.tab) || "";
    return titulo ? { titulo, na_frente: tab.active && janela.state !== "minimized" } : null;
  } catch {
    return null;
  }
}

/** O estado com a aba, quando há. A aba é consulta assíncrona ao Chrome. */
async function enviarEstado() {
  const m = estadoDoAtivo();
  const aba = m.em_reuniao ? await abaDoAtivo() : null;
  if (aba) m.aba = aba;
  enviar(m);
}

/** A aba da reunião deixou de estar, ou voltou a estar, na frente. */
function aoMexerNasAbas() {
  if (ativo !== null) enviarEstado();
}
chrome.tabs.onActivated.addListener(aoMexerNasAbas);
chrome.tabs.onUpdated.addListener((tab, mudanca) => {
  if (mudanca.title !== undefined && ativo !== null && quadros.get(ativo)?.tab === tab) enviarEstado();
});
chrome.tabs.onAttached.addListener(aoMexerNasAbas);
chrome.windows.onBoundsChanged.addListener(aoMexerNasAbas);
chrome.windows.onFocusChanged.addListener(aoMexerNasAbas);

function conectar() {
  if (ws || tentativa) return;
  const s = new WebSocket(`ws://127.0.0.1:${PORTA}/`);
  ws = s;
  let oi = null;
  s.onopen = () => {
    enviarEstado();
    oi = setInterval(() => enviar({ tipo: "oi" }), OI_MS);
  };
  s.onmessage = (ev) => {
    let m;
    try { m = JSON.parse(ev.data); } catch { return; }
    const e = ativo !== null ? quadros.get(ativo) : null;
    if (m?.tipo === "mutar" && typeof m.mudo === "boolean" && e) {
      chrome.tabs.sendMessage(e.tab, { tipo: "mutar", mudo: m.mudo }, { frameId: e.frame })
        .catch(() => {});
    }
  };
  s.onclose = () => {
    clearInterval(oi);
    if (ws === s) ws = null;
    // Só volta a tentar se ainda há uma página de reunião: sem ela, não há o
    // que sincronizar, e o worker pode dormir.
    if (quadros.size > 0) tentativa = setTimeout(() => { tentativa = null; conectar(); }, RECONECTAR_MS);
  };
}

chrome.runtime.onMessage.addListener((m, remetente) => {
  const tab = remetente.tab?.id;
  if (tab === undefined) return;
  const frame = remetente.frameId ?? 0;

  if (m?.tipo === "diagnostico" && typeof m.texto === "string") {
    conectar();
    enviar({ tipo: "diagnostico", app: m.app, texto: m.texto.slice(0, 1000) });
    return;
  }
  if (m?.tipo !== "estado") return;

  if (frame === 0 && typeof m.titulo === "string") titulos.set(tab, m.titulo.slice(0, 300));
  const chave = `${tab}:${frame}`;
  const antes = quadros.get(chave);
  const mudou = !antes || antes.em_reuniao !== m.em_reuniao || antes.mudo !== m.mudo;
  quadros.set(chave, {
    tab, frame, app: m.app,
    em_reuniao: m.em_reuniao === true,
    mudo: m.em_reuniao === true && m.mudo === true,
    quando: m.em_reuniao ? (mudou ? Date.now() : antes.quando) : 0,
  });
  escolherAtivo();
  conectar();
  enviarEstado();
});

chrome.tabs.onRemoved.addListener((tab) => {
  let tirou = false;
  titulos.delete(tab);
  for (const chave of [...quadros.keys()]) {
    if (chave.startsWith(`${tab}:`)) { quadros.delete(chave); tirou = true; }
  }
  if (!tirou) return;
  escolherAtivo();
  enviarEstado();
});
