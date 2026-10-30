// Roda dentro da reunião — o Google Meet (aba ou app instalado) e o Teams no
// navegador: lê o botão do microfone, avisa o fundo quando ele muda, e o clica
// quando o PulseMeet pede.
//
// **Nenhum dos dois tem API para isto**, e o que se lê aqui é a página. Quando
// o Google ou a Microsoft mudarem a página, é aqui que se conserta — e o
// diagnóstico (abaixo) põe no registro.log do PulseMeet o que se encontrou, que
// é o que diz o que mudou. Ver GRA-3 no docs/BACKLOG.md.

"use strict";

/** O nome que o leitor de tela diz: aria-label, ou o texto do aria-labelledby. */
function rotulo(el) {
  const direto = el.getAttribute("aria-label");
  if (direto) return direto;
  const ids = el.getAttribute("aria-labelledby");
  if (!ids) return "";
  return ids.split(/\s+/)
    .map((id) => document.getElementById(id)?.textContent ?? "")
    .join(" ");
}

/**
 * Cada site: onde está o botão e como ele diz que está mudo.
 *
 * **Meet:** o botão do microfone e o da câmera carregam `data-is-muted`; o que
 * separa os dois em qualquer idioma é o atalho no rótulo — ctrl + d é o
 * microfone, ctrl + e a câmera. Medido em reunião real em 22/10/2026.
 *
 * **Teams:** o `#microphone-button` do Teams novo, com `data-state` "mic" ou
 * "mic-off". **Não medido** — é o que se sabe de fora, e por isso as
 * alternativas: o `data-tid`, e o atalho ctrl + shift + m no rótulo.
 *
 * **Medido em 27/10/2026**, no Teams em pt-BR no Chrome: o botão é o
 * `#mic-button`, sem `data-state` nem atalho, e o estado só está no rótulo —
 * "Ativar mudo do microfone" com som, "Desativar mudo do microfone" mudo.
 */
const SITES = {
  meet: {
    botao() {
      const candidatos = [...document.querySelectorAll("[data-is-muted]")];
      return candidatos.find((el) => /(ctrl|⌘)\s*\+\s*d\b/i.test(rotulo(el)))
        // Sem o atalho no rótulo (ele já sumiu numa versão do Meet), o nome.
        ?? candidatos.find((el) => /micro/i.test(rotulo(el)));
    },
    mudo: (b) => b.dataset.isMuted === "true",
  },
  teams: {
    botao() {
      return document.querySelector("#microphone-button, [data-tid='microphone-button'], #mic-button")
        ?? [...document.querySelectorAll("button[aria-label]")]
          .find((el) => /(ctrl|⌘)\s*\+\s*shift\s*\+\s*m\b/i.test(rotulo(el)));
    },
    mudo(b) {
      const estado = b.getAttribute("data-state");
      if (estado) return /off|mute/i.test(estado);
      // Sem o data-state: o rótulo diz a ação do clique — "ativar" o microfone
      // é porque ele está desligado.
      // "Desativar" contém "ativar", por isso o lookbehind. E "desativar mudo"
      // é o contrário: a ação é tirar o mudo, então ele está mudo.
      return /\bunmute\b|desativar (o )?mudo|(?<!des)ativar (o )?mic|reativar|(?<!des)activar/i.test(rotulo(b));
    },
  },
};

const APP = location.hostname === "meet.google.com" ? "meet" : "teams";
const site = SITES[APP];

/**
 * Sem o botão, não há reunião: é a tela inicial, o chat, ou a reunião acabou.
 * A sala de espera do Meet tem o botão, e conta — o que se escolhe nela é o
 * mute com que se entra.
 */
function estado() {
  const b = site.botao();
  const e = b
    ? { tipo: "estado", app: APP, em_reuniao: true, mudo: site.mudo(b) }
    : { tipo: "estado", app: APP, em_reuniao: false, mudo: false };
  // O título da aba, que é o da janela quando ela está na frente: é por ele
  // que o PulseMeet acha a janela da reunião para gravar em vídeo. Só o quadro
  // de cima o tem — o do iframe é outro documento. Entra no texto que o
  // `avisar` compara, então mudar de título também avisa.
  if (window === window.top) e.titulo = document.title;
  return e;
}

function enviar(m) {
  // O fundo pode estar dormindo ou recarregando: perder um aviso é inócuo,
  // porque o próximo — no máximo em dez segundos — diz o estado inteiro.
  chrome.runtime.sendMessage(m).catch(() => {});
}

let ultimo = "";

function avisar(sempre = false) {
  const e = estado();
  const texto = JSON.stringify(e);
  if (!sempre && texto === ultimo) return;
  ultimo = texto;
  enviar(e);
}

// ─── diagnóstico

/** Os atributos que importam de um elemento, sem o texto da página. */
function descrever(el) {
  const partes = [el.tagName.toLowerCase()];
  for (const a of ["id", "data-tid", "data-state", "data-is-muted", "aria-pressed", "aria-checked", "role"]) {
    const v = el.getAttribute(a);
    if (v !== null) partes.push(`${a}=${v}`);
  }
  partes.push(`rótulo="${rotulo(el).slice(0, 80)}"`);
  return partes.join(" ");
}

let ultimoDiagnostico = "";

/**
 * Diz ao PulseMeet o que se achou, uma vez por mudança: o botão, e se ele está
 * mudo — ou, numa página de reunião sem botão reconhecido, os candidatos. É o
 * que permite consertar o Teams sem ninguém abrir as ferramentas do navegador.
 */
function diagnosticar() {
  const b = site.botao();
  let texto;
  if (b) {
    texto = `${APP}: botão ${descrever(b)} → ${site.mudo(b) ? "mudo" : "com som"}`;
  } else if (APP === "teams") {
    // Só os que parecem microfone: nome, id ou atalho.
    const candidatos = [...document.querySelectorAll("button, [role='button'], [role='switch']")]
      .filter((el) => /mic|mudo|mute/i.test(`${el.id} ${el.getAttribute("data-tid") ?? ""} ${rotulo(el)}`))
      .slice(0, 4)
      .map(descrever);
    if (candidatos.length === 0) return;
    texto = `${APP}: nenhum botão reconhecido; candidatos: ${candidatos.join(" | ")}`;
  } else {
    return;
  }
  if (texto === ultimoDiagnostico) return;
  ultimoDiagnostico = texto;
  enviar({ tipo: "diagnostico", app: APP, texto });
}

// O Meet e o Teams mexem no DOM o tempo todo; um quarto de segundo de folga
// junta as rajadas sem atrasar o mute de um jeito que se perceba.
let pendente = null;
new MutationObserver(() => {
  if (pendente) return;
  pendente = setTimeout(() => { pendente = null; avisar(); }, 250);
}).observe(document.documentElement, {
  subtree: true,
  childList: true,
  attributes: true,
  attributeFilter: ["data-is-muted", "data-state", "aria-label"],
});

// Também é o que mantém o fundo acordado enquanto há uma reunião aberta.
setInterval(() => { avisar(true); diagnosticar(); }, 10_000);

chrome.runtime.onMessage.addListener((m) => {
  if (m?.tipo !== "mutar") return;
  const b = site.botao();
  // O pedido diz o estado, não "inverter": clicar só se for diferente.
  if (b && site.mudo(b) !== m.mudo) b.click();
});

addEventListener("pagehide", () => {
  enviar({ tipo: "estado", app: APP, em_reuniao: false, mudo: false });
});

avisar(true);
