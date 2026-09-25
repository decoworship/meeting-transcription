// Ajustes › Clientes: mestre-detalhe (plano 4a do redesenho de UI, spec
// 2026-09-23-ui-ux.md §3.4).
//
// Era uma lista de clientes com os projetos em sanfona embaixo de cada um. Com
// quatro clientes de quatro projetos a tela já não cabia, e o que se procura
// aqui é um cliente — por isso a busca à esquerda, e os projetos só do
// escolhido à direita.
//
// Pela D-C, o que a tela tinha e a prancha não mostra (o modelo de diarização,
// a dica do vocabulário, o "salvo", o texto de como cliente e projeto nascem)
// fica abaixo do que a prancha mostra. Nada saiu.

import { pedir } from "/ponte.js";
import { campo, confirmar, avisar, perguntarTexto } from "/pecas.js";
import { popover } from "/popover.js";
import { campoDeEtiquetas } from "/etiquetas.js";
import { contar, rotuloDoCliente, rotuloDoProjeto, filtrarClientes, ordenar } from "/clientes-regras.js";
import { filtrarReunioesPor } from "/reunioes.js";
import { telaDeLista } from "/app.js";

/** Os idiomas que se escolhem por nome. Um código gravado fora desta lista vira opção própria. */
const IDIOMAS = [["", "Detectar sozinho"], ["pt", "Português"], ["en", "Inglês"], ["es", "Espanhol"]];

// A escolha mora no módulo: criar, renomear ou apagar recarrega a tela, e
// quem estava num projeto tem de continuar nele.
const escolha = { cliente: null, projeto: null, busca: "" };

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

/** Roda a op e recarrega; se falhar, diz por quê e não recarrega. */
async function executar(titulo, op, recarregar) {
  try {
    await op();
  } catch (e) {
    await avisar(e.message, { titulo });
    return;
  }
  recarregar();
}

/**
 * @param dados `{ clientes, gravacoes, catalogo, diarizadores, tipos }`
 * @param recarregar redesenha a tela de Ajustes inteira, relendo o núcleo.
 * @param estado o parágrafo "salvando… / salvo" da tela de Ajustes.
 */
export function abaClientes({ clientes, gravacoes, catalogo, diarizadores, tipos }, recarregar, estado) {
  const nomes = ordenar(Object.keys(clientes));
  const contagem = contar(gravacoes);
  if (!nomes.includes(escolha.cliente)) escolha.cliente = nomes[0] ?? null;

  const painel = el("div", "painel");
  const b = el("section", "bloco clientes");

  const cabeca = el("div", "clientes__cabeca");
  const textos = el("div");
  textos.append(
    el("h2", "bloco__titulo", "Clientes e projetos"),
    el("p", "bloco__texto", "Cada projeto guarda o vocabulário, o idioma e o tipo de ata das reuniões dele."));
  cabeca.append(textos, botao("+ Cliente", "aa-btn-secundario", async () => {
    const cliente = await perguntarTexto("Nome do cliente", "", { titulo: "Novo cliente", ok: "Continuar" });
    if (!cliente) return;
    const projeto = await perguntarTexto("Nome do primeiro projeto", "",
                                         { titulo: `Primeiro projeto de ${cliente}`, ok: "Criar" });
    if (!projeto) return;
    Object.assign(escolha, { cliente, projeto, busca: "" });
    executar("Não deu para criar",
             () => pedir("salvar-projeto", { cliente, projeto, prefs: {} }), recarregar);
  }));
  b.appendChild(cabeca);

  if (nomes.length === 0) {
    b.appendChild(el("p", "campo__dica", "Nenhum cliente ainda. Crie um em “+ Cliente”, "
      + "ou digite um nome novo ao preparar uma transcrição."));
  } else {
    const corpo = el("div", "clientes__corpo");
    const mestre = el("div", "clientes__mestre");
    const busca = el("input", "aa-entrada clientes__busca");
    busca.type = "search";
    busca.id = "busca-clientes";
    busca.placeholder = "Buscar cliente…";
    busca.setAttribute("aria-label", "Buscar cliente");
    busca.value = escolha.busca;
    const lista = el("div", "clientes__lista");
    const detalhe = el("div", "clientes__detalhe");

    // F-2: digitar redesenha a lista, nunca o campo.
    function desenharLista() {
      const visiveis = filtrarClientes(nomes, escolha.busca);
      if (visiveis.length === 0) {
        lista.replaceChildren(el("p", "campo__dica clientes__nada", "Nenhum cliente com esse nome."));
        return;
      }
      lista.replaceChildren(...visiveis.map((nome) => {
        const item = el("button", "clientes__cliente");
        item.type = "button";
        item.setAttribute("aria-current", String(nome === escolha.cliente));
        item.append(el("span", "clientes__nome", nome),
                    el("span", "clientes__sub", rotuloDoCliente(clientes[nome].length, contagem.cliente(nome))));
        item.addEventListener("click", () => {
          if (escolha.cliente === nome) return;
          escolha.cliente = nome;
          escolha.projeto = null;
          for (const outro of lista.children) outro.setAttribute?.("aria-current", String(outro === item));
          desenharDetalhe();
        });
        return item;
      }));
    }
    busca.addEventListener("input", () => { escolha.busca = busca.value; desenharLista(); });

    function desenharDetalhe() {
      detalhe.replaceChildren(detalheDoCliente(escolha.cliente, clientes[escolha.cliente],
        { contagem, catalogo, diarizadores, tipos, recarregar, estado }));
    }

    mestre.append(busca, lista);
    corpo.append(mestre, detalhe);
    b.appendChild(corpo);
    desenharLista();
    desenharDetalhe();
  }

  painel.appendChild(b);
  painel.appendChild(el("p", "campo__dica",
    "Cliente e projeto novos também nascem na tela de preparo: digite um nome que "
    + "ainda não existe e ele passa a valer. Renomear leva junto o vocabulário e as "
    + "preferências do projeto."));
  return painel;
}

function detalheDoCliente(cliente, projetos, ctx) {
  const { contagem, recarregar } = ctx;
  const raiz = el("div", "clientes__detalhe-dentro");

  const cabeca = el("div", "clientes__detalhe-cabeca");
  const nome = el("div");
  nome.append(el("h3", "clientes__detalhe-nome", cliente),
              el("p", "clientes__sub", rotuloDoCliente(projetos.length, contagem.cliente(cliente))));

  const novo = botao("+ Projeto", "aa-btn-secundario", async () => {
    const projeto = await perguntarTexto("Nome do projeto", "", { titulo: `Novo projeto de ${cliente}`, ok: "Criar" });
    if (!projeto) return;
    escolha.projeto = projeto;
    executar("Não deu para criar",
             () => pedir("salvar-projeto", { cliente, projeto, prefs: {} }), recarregar);
  });

  // Renomear e apagar o cliente ficam no ⋯: são raros, e o apagar leva todos
  // os projetos junto — não pode estar a um clique errado do "+ Projeto".
  const mais = el("button", "aa-btn aa-btn-secundario clientes__mais-cliente", "⋯");
  mais.type = "button";
  mais.setAttribute("aria-label", `Mais ações de ${cliente}`);
  const { ancora } = popover(mais, `Ações de ${cliente}`, (fechar) => {
    const menu = el("div", "clientes__menu");
    const renomear = botao("Renomear cliente", "aa-btn-texto", async () => {
      fechar();
      const para = await perguntarTexto("Novo nome do cliente", cliente, { titulo: "Renomear cliente" });
      if (!para || para === cliente) return;
      escolha.cliente = para;
      executar("Não deu para renomear",
               () => pedir("renomear-cliente", { cliente, nome: para }), recarregar);
    });
    const apagar = botao("Apagar cliente", "aa-btn-texto clientes__perigo", async () => {
      fechar();
      const n = projetos.length;
      if (!await confirmar(
        `Apagar o cliente "${cliente}" e os ${n} ${n === 1 ? "projeto" : "projetos"} dele?\n\n`
        + "Isto esquece o vocabulário e as preferências. As transcrições já "
        + "feitas continuam onde estão.", { titulo: "Apagar", ok: "Apagar" })) return;
      executar("Não deu para apagar", () => pedir("apagar-cliente", { cliente }), recarregar);
    });
    menu.append(renomear, apagar);
    return { raiz: menu, focar: () => renomear.focus() };
  });

  const acoes = el("div", "clientes__detalhe-acoes");
  acoes.append(novo, ancora);
  cabeca.append(nome, acoes);

  const lista = el("div", "clientes__projetos");
  const ordenados = ordenar(projetos);
  if (!ordenados.includes(escolha.projeto)) escolha.projeto = ordenados[0] ?? null;
  const linhas = [];
  for (const p of ordenados)
    linhas.push(linhaDeProjeto(cliente, p, ctx, () => { for (const l of linhas) l.fechar(); }));
  lista.append(...linhas.map((l) => l.raiz));
  if (ordenados.length === 0)
    lista.appendChild(el("p", "campo__dica clientes__vazio", "Nenhum projeto. Crie um em “+ Projeto”."));

  raiz.append(cabeca, lista);
  return raiz;
}

/** Um projeto: a linha com o resumo, e o corpo com as preferências quando aberto. */
function linhaDeProjeto(cliente, projeto, ctx, fecharOsOutros) {
  const { contagem, catalogo, diarizadores, tipos, recarregar, estado } = ctx;
  const raiz = el("div", "clientes__projeto");

  const topo = el("button", "clientes__projeto-topo");
  topo.type = "button";
  topo.setAttribute("aria-expanded", "false");
  const esquerda = el("span", "clientes__projeto-nome");
  esquerda.append(el("span", "clientes__nome", projeto),
                  el("span", "clientes__sub", rotuloDoProjeto(contagem.projeto(cliente, projeto))));
  const direita = el("span", "clientes__tipo");
  const tipo = el("span", "", "");
  const resumo = el("span", "clientes__resumo", "");
  direita.append(tipo, resumo);
  topo.append(esquerda, direita);

  const corpo = el("div", "clientes__projeto-corpo");
  corpo.hidden = true;
  raiz.append(topo, corpo);

  let prefs = null;
  const nomeDoTipo = (id) => (id ? tipos.find((t) => t.id === id)?.nome ?? `${id} (não existe mais)` : "Tipo padrão do app");

  /** O que dá para saber sem abrir: o tipo de ata, e o modelo e o idioma embaixo. */
  function resumir() {
    if (!prefs) return;
    tipo.textContent = nomeDoTipo(prefs.tipo_de_ata);
    resumo.textContent = [
      prefs.model_size || "modelo padrão",
      prefs.language || "idioma automático",
      prefs.diarization === false ? "sem falantes" : "com falantes",
    ].join(" · ");
  }

  async function gravar(mudanca) {
    Object.assign(prefs, mudanca);
    resumir();
    estado.textContent = "salvando…";
    try {
      await pedir("salvar-projeto", { cliente, projeto, prefs });
      estado.textContent = "salvo";
    } catch (e) {
      estado.textContent = `não salvou: ${e.message}`;
    }
  }

  function seletor(rotulo, id, opcoes, valor, aoMudar) {
    const c = campo(rotulo, "select", { id, opcoes: opcoes.map(([, nome]) => nome) });
    const sel = c.querySelector("select");
    opcoes.forEach(([v], n) => { sel.options[n].value = v; });
    sel.value = valor ?? "";
    // Um valor gravado que não está na lista não pode sumir em silêncio: ele
    // decide como as reuniões deste projeto são transcritas.
    if (sel.selectedIndex === -1) {
      const solto = el("option", "", `${valor} (não instalado)`);
      solto.value = valor;
      sel.appendChild(solto);
      sel.value = valor;
    }
    sel.addEventListener("change", () => aoMudar(sel.value));
    return c;
  }

  function montar() {
    const asr = catalogo.filter((i) => i.pacote.familia === "asr");

    const vocab = campoDeEtiquetas({
      id: "projeto-vocabulario", rotulo: "Vocabulário",
      aoMudar: () => gravar({ initial_prompt: vocab.valor() || null }),
    });
    vocab.definir(prefs.initial_prompt ?? "");
    vocab.raiz.querySelector(".etiquetas__novo").placeholder = "+ termo";

    const idiomas = [...IDIOMAS];
    if (prefs.language && !idiomas.some(([v]) => v === prefs.language))
      idiomas.push([prefs.language, prefs.language]);

    const campos = el("div", "clientes__campos");
    campos.append(
      seletor("Idioma", "projeto-idioma", idiomas, prefs.language,
              (v) => gravar({ language: v || null })),
      seletor("Modelo", "projeto-modelo",
              [["", "Padrão do app"], ...asr.map((i) => [i.pacote.id, i.pacote.nome])],
              prefs.model_size, (v) => gravar({ model_size: v || null })),
      seletor("Falantes", "projeto-falantes", [["sim", "Separar"], ["nao", "Não separar"]],
              prefs.diarization === false ? "nao" : "sim",
              (v) => gravar({ diarization: v === "sim" })),
      // Vazio, e não nulo, é o padrão: nulo não chega ao disco (Projetos.cs,
      // PreferenciasDoProjeto.TipoDeAta), e o projeto nunca voltaria.
      seletor("Tipo de ata", "projeto-tipo",
              [["", "Padrão do app"], ...tipos.map((t) => [t.id, t.nome])],
              prefs.tipo_de_ata, (v) => gravar({ tipo_de_ata: v })),
    );

    const acoes = el("div", "clientes__acoes");
    acoes.append(
      botao("Ver as reuniões deste projeto", "aa-btn-texto clientes__ver", () => {
        filtrarReunioesPor({ cliente, texto: projeto });
        telaDeLista();
      }),
      botao("Renomear", "aa-btn-texto", async () => {
        const para = await perguntarTexto("Novo nome do projeto", projeto, { titulo: "Renomear projeto" });
        if (!para || para === projeto) return;
        escolha.projeto = para;
        executar("Não deu para renomear",
                 () => pedir("renomear-projeto", { cliente, projeto, nome: para }), recarregar);
      }),
      botao("Apagar projeto", "aa-btn-texto clientes__perigo", async () => {
        if (!await confirmar(`Apagar o projeto "${projeto}" de ${cliente}?\n\n`
          + "Isto esquece o vocabulário e as preferências dele. As transcrições já "
          + "feitas continuam onde estão.", { titulo: "Apagar", ok: "Apagar" })) return;
        escolha.projeto = null;
        executar("Não deu para apagar", () => pedir("apagar-projeto", { cliente, projeto }), recarregar);
      }),
    );

    // Abaixo do que a prancha mostra (D-C): o que já existia e ela não desenha.
    const mais = el("div", "clientes__mais");
    const diar = seletor("Modelo de diarização", "projeto-diarizacao",
                         [["", "Padrão do app"], ...diarizadores.map((d) => [d, d])],
                         prefs.diar_model, (v) => gravar({ diar_model: v || null }));
    // O mesmo texto do preparo: o teto de 224 tokens do initial_prompt morreu
    // quando a correção fonética entrou.
    const dica = el("p", "campo__dica", "Vocabulário: nomes de pessoas, jargão, nomes de "
      + "sistemas. Sem limite de tamanho — o que o modelo escrever parecido é corrigido "
      + "depois. É este vocabulário que alimenta a correção fonética.");
    mais.append(diar, dica);

    corpo.replaceChildren(vocab.raiz, campos, acoes, mais);
  }

  function abrir() {
    fecharOsOutros();
    escolha.projeto = projeto;
    raiz.classList.add("clientes__projeto--aberto");
    topo.setAttribute("aria-expanded", "true");
    corpo.hidden = false;
    if (prefs) montar();
    else corpo.replaceChildren(el("p", "campo__dica", "carregando…"));
  }

  function fechar() {
    raiz.classList.remove("clientes__projeto--aberto");
    topo.setAttribute("aria-expanded", "false");
    corpo.hidden = true;
    corpo.replaceChildren();
  }

  topo.addEventListener("click", () => {
    if (topo.getAttribute("aria-expanded") === "true") { fechar(); escolha.projeto = null; }
    else abrir();
  });

  // As preferências vêm já, e não só ao abrir: é o que responde "qual tipo de
  // ata este projeto usa?" sem um clique por projeto.
  pedir("prefs", { cliente, projeto })
    .then((r) => {
      prefs = r.prefs ?? {};
      resumir();
      if (topo.getAttribute("aria-expanded") === "true") montar();
    })
    .catch(() => { prefs = {}; });

  if (escolha.projeto === projeto) abrir();
  return { raiz, fechar };
}
