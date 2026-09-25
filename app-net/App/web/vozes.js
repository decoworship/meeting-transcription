// Ajustes › Vozes: mestre-detalhe (plano 6 do redesenho de UI, spec
// 2026-09-23-ui-ux.md §3.4, backlog UI-2).
//
// Era uma pessoa por <details>, com todas as amostras dentro. A limpeza da
// biblioteca precisa ser um gesto e não uma tarde (docs/VOZES.md §6): por isso
// "Para revisar" vem primeiro, com o áudio pronto para ouvir e três respostas,
// e as pessoas embaixo dizem de quem o app precisa de mais voz.
//
// Pela D-C, o que a tela tinha e a prancha não mostra — as amostras fora de uso
// de cada pessoa, com o motivo, e o "Aprovar" dentro do perfil — fica abaixo do
// que a prancha mostra. Nada saiu.
//
// A tela só lê e edita os perfis guardados. Como uma voz é reconhecida ou
// aprendida numa transcrição não passa por aqui.

import { pedir } from "/ponte.js";
import { campo, confirmar, avisar, perguntarTexto, anunciar } from "/pecas.js";
import { popover } from "/popover.js";
import {
  inerte, saude, resumoDaPessoa, pessoasEmUso, ordenarPessoas, fila, foraDeUso,
  procedencia, numero, primeiroNome, chaveDoPar, parecidosVisiveis,
} from "/vozes-regras.js";

const el = (tag, classe, texto) => {
  const e = document.createElement(tag);
  if (classe) e.className = classe;
  if (texto != null) e.textContent = texto;
  return e;
};

function botao(rotulo, classe, aoClicar) {
  const b = el("button", `aa-btn ${classe}`, rotulo);
  b.type = "button";
  b.addEventListener("click", aoClicar);
  return b;
}

// O que está escolhido, e os pares que a pessoa disse que não são a mesma.
// Moram no módulo: cada ação redesenha a seção, e quem estava num perfil
// continua nele. "Não são" vale até fechar o app — guardar em disco mudaria o
// vozes.json, e fica para quando a sugestão voltar e incomodar.
const escolha = { vista: "revisar" };
const recusados = new Set();

// ─────────────────────────────────────────────────────────── tocar

/**
 * Toca o recorte que gerou uma amostra.
 *
 * Usa o <audio> único do app, e não um por linha: com dezenas de amostras,
 * seriam dezenas de conexões — e dois trechos tocando juntos, que é pior para
 * quem está decidindo se a voz é da mesma pessoa. Clicar no que toca, para.
 */
function ouvirTrecho(relativo, botaoDeTocar) {
  const audio = document.getElementById("audio");
  const url = `https://vozes.local/${relativo.split("/").map(encodeURIComponent).join("/")}`;

  if (audio.getAttribute("src") === url && !audio.paused) {
    audio.pause();
    botaoDeTocar.removeAttribute("data-tocando");
    return;
  }
  for (const b of document.querySelectorAll(".tocar[data-tocando]")) b.removeAttribute("data-tocando");

  audio.src = url;
  audio.currentTime = 0;
  botaoDeTocar.dataset.tocando = "true";
  audio.onended = () => botaoDeTocar.removeAttribute("data-tocando");
  audio.play().catch((e) => {
    botaoDeTocar.removeAttribute("data-tocando");
    botaoDeTocar.title = `não tocou: ${e.message}`;
  });
}

function botaoDeTocar(a, grande = false) {
  const b = el("button", grande ? "tocar tocar--grande" : "tocar");
  b.type = "button";
  b.disabled = !a.trecho;
  b.title = a.trecho ? "Ouvir este trecho" : "Esta amostra foi guardada sem o trecho de áudio";
  b.setAttribute("aria-label", "Ouvir o trecho");
  if (a.trecho) b.addEventListener("click", () => ouvirTrecho(a.trecho, b));
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  const uso = document.createElementNS("http://www.w3.org/2000/svg", "use");
  uso.setAttribute("href", "#i-tocar");
  svg.appendChild(uso);
  b.appendChild(svg);
  return b;
}

// ─────────────────────────────────────────────────────────── diálogos

/**
 * Pergunta com qual pessoa juntar, num <select>: o destino tem de ser alguém
 * que já existe, e digitar o nome de novo é o gesto que criou o problema.
 */
function escolherPessoa(de, outras) {
  return new Promise((resolver) => {
    const dialogo = el("dialog", "modal");
    const corpo = el("div", "modal__corpo");
    const escolhaDoNome = campo("Manter o nome", "select", { opcoes: outras });
    const sel = escolhaDoNome.querySelector("select");
    const acoes = el("div", "modal__acoes");
    acoes.append(botao("Cancelar", "aa-btn-secundario", () => dialogo.close("")),
                 botao("Continuar", "aa-btn-primario", () => dialogo.close("sim")));
    corpo.append(el("h2", "modal__titulo", `Juntar "${de}" com quem?`), escolhaDoNome, acoes);
    dialogo.appendChild(corpo);
    dialogo.addEventListener("close", () => {
      const v = dialogo.returnValue === "sim" ? sel.value : null;
      dialogo.remove();
      resolver(v);
    }, { once: true });
    dialogo.addEventListener("click", (e) => { if (e.target === dialogo) dialogo.close(""); });
    document.body.appendChild(dialogo);
    dialogo.returnValue = "";
    dialogo.showModal();
    sel.focus();
  });
}

const amostras = (n) => `${n} ${n === 1 ? "amostra" : "amostras"}`;

async function confirmarJuntar(de, para, n) {
  return confirmar(
    `As ${amostras(n)} de "${de}" passam para "${para}", e "${de}" deixa de existir. `
    + "As gravações e os trechos de áudio não são tocados.",
    { titulo: `Juntar "${de}" com "${para}"?`, ok: "Juntar" });
}

// ─────────────────────────────────────────────────────────── a seção

/**
 * @param dados `{ vozes, parecidos }`, como a op "vozes" os devolve.
 * @param estado o parágrafo "salvando… / salvo" da tela de Ajustes.
 */
export function abaVozes(dados, estado) {
  let { vozes, parecidos = [] } = dados;

  const painel = el("div", "painel");
  const b = el("section", "bloco vozes");
  const cabeca = el("div", "vozes__cabeca");
  cabeca.append(el("h2", "bloco__titulo", "Vozes"),
                el("p", "bloco__texto", "Quem o app reconhece sozinho na próxima reunião. "
                  + "Ele aprende quando você nomeia um falante na transcrição."));
  const corpo = el("div", "vozes__corpo");
  const mestre = el("div", "vozes__mestre");
  const detalhe = el("div", "vozes__detalhe");
  corpo.append(mestre, detalhe);
  b.append(cabeca, corpo);
  painel.appendChild(b);

  // Abaixo do que a prancha mostra (D-C): a nota de obra que a tela tinha.
  const obra = el("p", "campo__dica vozes-obra",
    "O ciclo completo ainda não foi visto com áudio real: nomear alguém numa reunião e ela "
    + "chegar nomeada na seguinte está implementado e não comprovado. Esta tela é o "
    + "instrumento para comprovar — depois de nomear um falante, a pessoa tem que aparecer aqui.");
  painel.appendChild(obra);

  /** Roda uma op de voz; o núcleo devolve a biblioteca inteira de volta. */
  async function mexer(op, campos, feito) {
    estado.textContent = "salvando…";
    try {
      const r = await pedir(op, campos);
      vozes = r.vozes ?? vozes;
      parecidos = r.parecidos ?? parecidos;
      estado.textContent = "salvo";
      if (feito) anunciar(feito);
    } catch (e) {
      estado.textContent = "";
      await avisar(e.message, { titulo: "Não deu para mudar a voz" });
      // A recusa mais provável é a biblioteca ter mudado por baixo: relê.
      try {
        const r = await pedir("vozes");
        vozes = r.vozes ?? vozes;
        parecidos = r.parecidos ?? parecidos;
      } catch { /* fica o que havia */ }
    }
    desenhar();
  }

  const porAmostra = (pessoa, a) => ({ pessoa, indice: a.indice, criada_em: a.criada_em });

  // ── o mestre

  function item(classe, nome, sub, direita, vista) {
    const i = el("button", `vozes__item ${classe}`);
    i.type = "button";
    i.setAttribute("aria-current", String(escolha.vista === vista));
    if (vista !== "revisar") i.dataset.pessoa = vista;
    const textos = el("span", "vozes__textos");
    textos.append(el("span", "vozes__nome", nome), el("span", "vozes__sub", sub));
    i.appendChild(textos);
    if (direita) i.appendChild(direita);
    i.addEventListener("click", () => { escolha.vista = vista; desenhar(); });
    return i;
  }

  function desenharMestre(casos, fora) {
    const contagem = el("span", "vozes__contagem", String(casos.length));
    contagem.dataset.zero = String(casos.length === 0);
    const revisar = item("vozes__revisar", "Para revisar", "amostras que soaram diferente", contagem, "revisar");

    const emUso = ordenarPessoas(pessoasEmUso(vozes));
    const rotulo = el("p", "vozes__rotulo", `Pessoas · ${emUso.length}`);
    const lista = el("div", "vozes__pessoas");
    for (const p of emUso) {
      const s = saude(p);
      const tag = el("span", "vozes__saude", s.rotulo);
      tag.dataset.saude = s.rotulo === "boa" ? "boa" : "pouca";
      lista.appendChild(item("", p.nome, resumoDaPessoa(p), tag, p.nome));
    }
    if (emUso.length === 0)
      lista.appendChild(el("p", "campo__dica vozes__vazio",
        "Ninguém ainda. Nomeie um falante numa transcrição e a voz dele aparece aqui."));

    const dobra = el("details", "vozes__fora");
    // Aberto só se quem está escolhido mora aqui dentro: senão, escolher do
    // "Fora de uso" dobraria a lista de volta e esconderia a escolha.
    dobra.open = fora.pessoas.includes(escolha.vista);
    dobra.appendChild(el("summary", "", `Fora de uso · ${amostras(fora.total)}`));
    dobra.appendChild(el("p", "campo__dica vozes__fora-texto",
      "De um modelo de voz antigo, ou aprendidas antes da guarda de contaminação. Não "
      + "entram no reconhecimento; ficam no perfil de cada pessoa, apagadas."));
    for (const nome of fora.pessoas) {
      const p = vozes.find((x) => x.nome === nome);
      dobra.appendChild(item("", nome, `${amostras(p.amostras.length)} fora de uso`, null, nome));
    }

    mestre.replaceChildren(revisar, rotulo, lista, dobra);
  }

  // ── a fila

  function outraPessoa(pessoa, a, rotulo, classe) {
    const gatilho = el("button", `aa-btn ${classe}`, rotulo);
    gatilho.type = "button";
    const { ancora } = popover(gatilho, "Mover para quem", (fechar) => {
      const menu = el("div", "vozes__menu");
      const outras = ordenarPessoas(pessoasEmUso(vozes)).map((p) => p.nome).filter((n) => n !== pessoa);
      for (const nome of outras)
        menu.appendChild(botao(nome, "aa-btn-texto", () => {
          fechar();
          mexer("mover-voz", { ...porAmostra(pessoa, a), nome }, `amostra movida para ${nome}`);
        }));
      menu.appendChild(botao("Pessoa nova…", "aa-btn-texto", async () => {
        fechar();
        const nome = await perguntarTexto("Nome da pessoa", "", { titulo: "De quem é esta voz?", ok: "Mover" });
        if (!nome || nome === pessoa) return;
        mexer("mover-voz", { ...porAmostra(pessoa, a), nome }, `amostra movida para ${nome}`);
      }));
      return { raiz: menu, focar: () => menu.querySelector("button")?.focus() };
    });
    return ancora;
  }

  function caso({ pessoa, amostra: a }) {
    const c = el("article", "vozes__caso");
    const textos = el("div", "vozes__caso-textos");
    textos.append(el("p", "vozes__marcada", `Marcada como ${pessoa}`), el("p", "vozes__proc", procedencia(a)));
    const topo = el("div", "vozes__caso-topo");
    topo.append(botaoDeTocar(a, true), textos);
    if (a.semelhanca != null) {
      const sim = el("span", "vozes__semelhanca");
      sim.append("semelhança ", el("strong", "", numero(a.semelhanca)));
      topo.appendChild(sim);
    }

    const respostas = el("div", "vozes__respostas");
    respostas.append(
      botao(`É ${primeiroNome(pessoa)}`, "aa-btn-secundario", () =>
        mexer("aprovar-voz", porAmostra(pessoa, a), `amostra aprovada como ${pessoa}`)),
      outraPessoa(pessoa, a, "É outra pessoa ▾", "aa-btn-secundario vozes__outra"),
      botao("Descartar", "aa-btn-texto vozes__perigo", async () => {
        if (!await confirmar("A amostra e o trecho de áudio dela saem do perfil. As gravações "
                             + "não são tocadas.",
                             { titulo: `Descartar esta amostra de ${pessoa}?`, ok: "Descartar" })) return;
        mexer("esquecer-voz", porAmostra(pessoa, a), "amostra descartada");
      }));
    c.append(topo, respostas);
    return c;
  }

  function sugestao(par) {
    const s = el("div", "vozes__sugestao");
    const textos = el("div", "vozes__caso-textos");
    textos.append(el("p", "vozes__marcada", `“${par.a}” e “${par.b}” parecem a mesma pessoa`),
                  el("p", "vozes__proc", `semelhança ${numero(par.semelhanca)} entre os dois perfis`));
    // Quem tem menos voz vai para quem tem mais: é o nome que o app já usa mais.
    const n = (nome) => vozes.find((p) => p.nome === nome)?.amostras.length ?? 0;
    const [de, para] = n(par.a) <= n(par.b) ? [par.a, par.b] : [par.b, par.a];
    const acoes = el("div", "vozes__respostas");
    acoes.append(
      botao("Juntar", "aa-btn-secundario", async () => {
        if (!await confirmarJuntar(de, para, n(de))) return;
        mexer("juntar-vozes", { pessoa: de, nome: para }, `${de} juntado a ${para}`);
      }),
      botao("Não são", "aa-btn-texto", () => {
        recusados.add(chaveDoPar(par.a, par.b));
        desenhar();
      }));
    s.append(textos, acoes);
    return s;
  }

  function desenharFila(casos) {
    const cab = el("div", "vozes__detalhe-cabeca");
    const t = el("div");
    t.append(el("h3", "vozes__detalhe-nome", "Para revisar"),
             el("p", "vozes__sub", "Soaram diferente do resto do perfil. Ouça e diga de quem é a voz — "
               + "até lá, ficam fora do reconhecimento."));
    cab.appendChild(t);
    const lista = el("div", "vozes__fila");
    lista.append(...casos.map(caso));
    if (casos.length === 0) lista.appendChild(el("p", "campo__dica vozes__nada", "Nada para revisar agora."));
    const sugestoes = parecidosVisiveis(parecidos, recusados, vozes.map((p) => p.nome));
    lista.append(...sugestoes.map(sugestao));
    detalhe.replaceChildren(cab, lista);
  }

  // ── o perfil

  function linhaDeAmostra(pessoa, a) {
    const linha = el("div", "vozes__amostra");
    linha.dataset.quarentena = String(a.quarentena && !inerte(a));
    linha.dataset.inerte = String(inerte(a));
    const textos = el("div", "vozes__caso-textos");
    const proc = el("p", "vozes__proc", procedencia(a));
    textos.appendChild(proc);
    // Duas causas para a mesma inércia, e a diferença importa para quem decide
    // se apaga ou espera: uma volta se o modelo voltar, a outra nunca.
    const motivo = [a.quarentena && !inerte(a) ? "soou diferente — aguardando revisão" : null,
                    a.outro_modelo === true ? "de um modelo de voz antigo" : null,
                    a.regras_antigas === true ? "aprendida antes da guarda de contaminação" : null]
      .filter(Boolean).join(" · ");
    if (motivo) textos.appendChild(el("p", "vozes__motivo", motivo));
    linha.append(botaoDeTocar(a), textos);

    const direita = el("div", "vozes__amostra-acoes");
    if (a.semelhanca != null) {
      const sim = el("span", "vozes__sub", numero(a.semelhanca));
      sim.title = "Semelhança com o resto do perfil";
      direita.appendChild(sim);
    }
    if (a.quarentena && !inerte(a))
      direita.appendChild(botao("Aprovar", "aa-btn-secundario", () =>
        mexer("aprovar-voz", porAmostra(pessoa, a), "amostra aprovada")));

    const mais = el("button", "aa-btn aa-btn-texto vozes__mais-amostra", "⋯");
    mais.type = "button";
    mais.setAttribute("aria-label", "Mais ações desta amostra");
    const { ancora } = popover(mais, "Ações da amostra", (fechar) => {
      const menu = el("div", "vozes__menu");
      const mover = botao("Mover para…", "aa-btn-texto", () => {
        // O mesmo menu de "É outra pessoa", no lugar deste.
        const outras = ordenarPessoas(pessoasEmUso(vozes)).map((p) => p.nome).filter((n) => n !== pessoa);
        const lista = el("div", "vozes__menu");
        for (const nome of outras)
          lista.appendChild(botao(nome, "aa-btn-texto", () => {
            fechar();
            mexer("mover-voz", { ...porAmostra(pessoa, a), nome }, `amostra movida para ${nome}`);
          }));
        lista.appendChild(botao("Pessoa nova…", "aa-btn-texto", async () => {
          fechar();
          const nome = await perguntarTexto("Nome da pessoa", "", { titulo: "De quem é esta voz?", ok: "Mover" });
          if (!nome || nome === pessoa) return;
          mexer("mover-voz", { ...porAmostra(pessoa, a), nome }, `amostra movida para ${nome}`);
        }));
        menu.replaceChildren(lista);
        lista.querySelector("button")?.focus();
      });
      const remover = botao("Remover", "aa-btn-texto vozes__perigo", async () => {
        fechar();
        if (!await confirmar("A voz aprendida nesta amostra deixa de ser reconhecida nas próximas "
                             + "reuniões, e o trecho de áudio dela sai. As gravações não são tocadas.",
                             { titulo: `Remover esta amostra de ${pessoa}?`, ok: "Remover" })) return;
        mexer("esquecer-voz", porAmostra(pessoa, a), "amostra removida");
      });
      menu.append(mover, remover);
      return { raiz: menu, focar: () => mover.focus() };
    });
    direita.appendChild(ancora);
    linha.appendChild(direita);
    return linha;
  }

  function desenharPessoa(p) {
    const cab = el("div", "vozes__detalhe-cabeca");
    const t = el("div");
    const s = saude(p);
    const sub = el("p", "vozes__sub", resumoDaPessoa(p));
    t.append(el("h3", "vozes__detalhe-nome", p.nome), sub);

    // Renomear, juntar e apagar ficam no ⋯: são raros, e apagar leva o perfil
    // inteiro.
    const mais = el("button", "aa-btn aa-btn-secundario vozes__mais-pessoa", "⋯");
    mais.type = "button";
    mais.setAttribute("aria-label", `Mais ações de ${p.nome}`);
    const { ancora } = popover(mais, `Ações de ${p.nome}`, (fechar) => {
      const menu = el("div", "vozes__menu");
      const renomear = botao("Renomear", "aa-btn-texto", async () => {
        fechar();
        const para = await perguntarTexto("Novo nome", p.nome, { titulo: `Renomear ${p.nome}` });
        if (!para || para === p.nome) return;
        // Renomear para quem já existe é juntar — e juntar confirma.
        if (vozes.some((o) => o.nome === para) && !await confirmarJuntar(p.nome, para, p.amostras.length)) return;
        escolha.vista = para;
        mexer("juntar-vozes", { pessoa: p.nome, nome: para }, `${p.nome} agora é ${para}`);
      });
      const outras = vozes.map((o) => o.nome).filter((n) => n !== p.nome);
      const juntar = botao("Juntar com…", "aa-btn-texto", async () => {
        fechar();
        const alvo = await escolherPessoa(p.nome, outras);
        if (!alvo || !await confirmarJuntar(p.nome, alvo, p.amostras.length)) return;
        escolha.vista = alvo;
        mexer("juntar-vozes", { pessoa: p.nome, nome: alvo }, `${p.nome} juntado a ${alvo}`);
      });
      juntar.disabled = outras.length === 0;
      const apagar = botao("Apagar perfil", "aa-btn-texto vozes__perigo", async () => {
        fechar();
        if (!await confirmar(`As ${amostras(p.amostras.length)} de "${p.nome}" e os trechos de áudio `
                             + "delas saem, e o app deixa de reconhecer esta voz. As gravações e as "
                             + "transcrições não são tocadas.",
                             { titulo: `Apagar o perfil de ${p.nome}?`, ok: "Apagar" })) return;
        escolha.vista = "revisar";
        mexer("apagar-voz", { pessoa: p.nome }, `perfil de ${p.nome} apagado`);
      });
      menu.append(renomear, juntar, apagar);
      return { raiz: menu, focar: () => renomear.focus() };
    });
    const direita = el("div", "vozes__detalhe-acoes");
    if (s.rotulo) {
      const tag = el("span", "vozes__saude", s.rotulo);
      tag.dataset.saude = s.rotulo === "boa" ? "boa" : "pouca";
      direita.appendChild(tag);
    }
    direita.appendChild(ancora);
    cab.append(t, direita);

    const ativas = el("div", "vozes__amostras");
    for (const a of p.amostras) if (!inerte(a)) ativas.appendChild(linhaDeAmostra(p.nome, a));
    const partes = [cab, ativas];

    // Abaixo (D-C): as que não participam de nada, apagadas e com o motivo.
    const paradas = p.amostras.filter(inerte);
    if (paradas.length > 0) {
      const inertes = el("div", "vozes__inertes");
      inertes.appendChild(el("p", "vozes__rotulo", `Fora de uso · ${paradas.length}`));
      for (const a of paradas) inertes.appendChild(linhaDeAmostra(p.nome, a));
      partes.push(inertes);
    }
    detalhe.replaceChildren(...partes);
  }

  function desenhar() {
    const casos = fila(vozes);
    const fora = foraDeUso(vozes);
    const p = vozes.find((x) => x.nome === escolha.vista);
    if (!p) escolha.vista = "revisar";
    desenharMestre(casos, fora);
    if (p) desenharPessoa(p);
    else desenharFila(casos);
  }

  desenhar();
  return painel;
}
