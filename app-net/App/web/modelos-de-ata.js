// Os modelos de ata em Ajustes › Atas (parte 2 da ATA-5).
//
// Mestre-detalhe, como Clientes: a lista dos tipos à esquerda, o tipo aberto à
// direita — o resumo do que a ata terá em cima, e o texto que a IA lê embaixo,
// editável. O núcleo é quem decide o que vale (Nucleo/Atas/ModelosDeAta.cs):
// salvar um tipo do app cria a cópia do usuário e guarda a marca do original;
// o texto sem esqueleto é recusado lá, com a mensagem que a tela mostra.

import { pedir } from "/ponte.js";
import { alerta, confirmar, dica, perguntarTexto } from "/pecas.js";

/** A linha que liga o agrupamento por tema — a mesma de ModeloDeAta.Marca. */
const MARCA = "<!-- ata: itens-por-secao -->";

/** O tipo aberto da última vez: trocar de aba e voltar não perde o lugar. */
let aberto = null;

function el(tag, classe, texto) {
  const e = document.createElement(tag);
  if (classe) e.className = classe;
  if (texto !== undefined) e.textContent = texto;
  return e;
}

function botao(texto, variacao, aoClicar) {
  const b = el("button", `aa-btn ${variacao}`, texto);
  b.type = "button";
  b.addEventListener("click", aoClicar);
  return b;
}

/** "personalizado", "criado por você", ou nada para o tipo do app como veio. */
function origem(tipo) {
  if (tipo.do_usuario && tipo.do_app) return "personalizado";
  if (tipo.do_usuario) return "criado por você";
  return "";
}

function comMarca(texto, ligada) {
  const sem = texto.replace(/^<!-- ata: itens-por-secao -->\r?\n?/m, "");
  return ligada ? `${MARCA}\n${sem}` : sem;
}

/**
 * A seção "Modelos de ata".
 *
 * @param tipos a lista que Ajustes leu; é atualizada no lugar quando um tipo
 *   nasce ou some, porque a aba Clientes oferece a mesma lista como padrão.
 */
export function secaoDeModelos(tipos) {
  const b = el("section", "bloco modelos-ata");

  const cabeca = el("div", "modelos-ata__cabeca");
  const titulo = el("h2", "bloco__titulo", "Modelos de ata");
  titulo.appendChild(dica("modelos de ata",
    "Cada tipo de reunião tem um modelo: as seções que a ata terá e as instruções que a IA "
    + "segue para escrever cada uma. O texto embaixo é exatamente o que ela lê.",
    "Ajustar um dos tipos do app cria a sua cópia; o original continua dentro do app, e "
    + "“Voltar ao original” apaga a cópia. Um tipo novo aparece na escolha de tipo da ata."));
  cabeca.append(titulo,
    el("p", "bloco__texto", "Veja como cada tipo de ata é montado, e ajuste o que a IA deve escrever."));
  b.appendChild(cabeca);

  const corpo = el("div", "modelos-ata__corpo");
  const mestre = el("div", "modelos-ata__mestre");
  const detalhe = el("div", "modelos-ata__detalhe");
  corpo.append(mestre, detalhe);
  b.appendChild(corpo);

  let sujo = false;

  function trocarTipos(novos) {
    tipos.splice(0, tipos.length, ...novos);
    desenharLista();
  }

  function desenharLista() {
    mestre.replaceChildren();
    for (const t of tipos) {
      const item = el("button", "modelos-ata__tipo");
      item.type = "button";
      item.setAttribute("aria-current", String(t.id === aberto));
      item.appendChild(el("span", "modelos-ata__nome", t.nome));
      const o = origem(t);
      if (o) item.appendChild(el("span", "modelos-ata__sub", o));
      item.addEventListener("click", async () => {
        if (t.id === aberto) return;
        if (sujo && !await confirmar("As mudanças neste modelo ainda não foram salvas.",
                                     { titulo: "Descartar as mudanças?", ok: "Descartar" })) return;
        abrir(t.id);
      });
      mestre.appendChild(item);
    }
  }

  async function abrir(id) {
    aberto = id;
    sujo = false;
    desenharLista();
    detalhe.replaceChildren(el("p", "modelos-ata__vazio campo__dica", "abrindo…"));
    let m;
    try {
      ({ modelo_de_ata: m } = await pedir("ler-modelo-de-ata", { modelo: id }));
    } catch (e) {
      detalhe.replaceChildren(alerta(e.message, "erro"));
      return;
    }
    desenharModelo(m);
  }

  function desenharModelo(m) {
    detalhe.replaceChildren();

    // ── a cabeça: nome, de onde veio, e o que dá para fazer com ele
    const topo = el("div", "modelos-ata__topo");
    const nome = el("div");
    nome.appendChild(el("h3", "modelos-ata__titulo", m.nome));
    const o = origem(m);
    if (o) nome.appendChild(el("span", "aa-etiqueta", o));
    const acoes = el("div", "modelos-ata__acoes");
    acoes.appendChild(botao("Novo tipo a partir deste", "aa-btn-texto", async () => {
      const novo = await perguntarTexto("Nome do tipo novo", "",
        { titulo: "Novo tipo de ata", ok: "Criar",
          texto: `Começa como uma cópia de “${m.nome}”.` });
      if (!novo) return;
      try {
        const r = await pedir("novo-modelo-de-ata", { modelo: m.id, nome: novo });
        trocarTipos(r.tipos);
        abrir(r.criado);
      } catch (e) {
        topo.after(alerta(e.message, "erro"));
      }
    }));
    if (m.do_usuario) {
      const apagar = !m.do_app;
      acoes.appendChild(botao(apagar ? "Apagar tipo" : "Voltar ao original",
        apagar ? "aa-btn-perigo" : "aa-btn-secundario", async () => {
          const ok = await confirmar(apagar
            ? `O tipo “${m.nome}” sai da lista. As atas já geradas com ele continuam onde estão.`
            : `A sua versão de “${m.nome}” é apagada, e volta a valer a do app.`,
            { titulo: apagar ? "Apagar o tipo?" : "Voltar ao original?",
              ok: apagar ? "Apagar" : "Voltar ao original" });
          if (!ok) return;
          try {
            const r = await pedir("restaurar-tipo-de-ata", { modelo: m.id });
            trocarTipos(r.tipos);
            abrir(apagar ? tipos[0]?.id : m.id);
          } catch (e) {
            topo.after(alerta(e.message, "erro"));
          }
        }));
    }
    topo.append(nome, acoes);
    detalhe.appendChild(topo);

    // ── o original mudou numa versão nova do app depois de personalizar
    if (m.original_mudou) {
      detalhe.appendChild(alerta(
        "O modelo do app mudou numa versão nova depois que você personalizou este. "
        + "A sua versão continua valendo; compare com o original abaixo, ou volte a ele.",
        "atencao"));
    }

    // ── o resumo: as seções e quem escreve cada uma
    const resumo = el("div", "modelos-ata__resumo");
    resumo.appendChild(el("p", "modelos-ata__rotulo", "Seções da ata"));
    const lista = el("ol", "modelos-ata__secoes");
    for (const s of m.secoes) {
      const li = el("li", "", s.titulo);
      const quem = el("span", `aa-etiqueta ${s.do_app ? "" : "aa-etiqueta--info"}`,
                      s.do_app ? "o app escreve" : "a IA escreve");
      li.appendChild(quem);
      lista.appendChild(li);
    }
    resumo.appendChild(lista);
    resumo.appendChild(el("p", "campo__dica",
      "“O app escreve” sai dos campos que a IA preenche — decisões, pendências, pontos em "
      + "aberto, riscos. As outras seções ela escreve seguindo o texto abaixo."));

    const agrupa = el("label", "modelos-ata__agrupa");
    const chave = el("input");
    chave.type = "checkbox";
    chave.id = "modelo-agrupa";
    chave.checked = m.agrupa;
    agrupa.append(chave, el("span", "", "Agrupar por tema"));
    agrupa.appendChild(dica("agrupar por tema",
      "Com o formato “Por tema” ligado, cada decisão, ação e ponto em aberto sai dentro do "
      + "assunto dele, e não em listas no fim.",
      "Liga e desliga a linha “" + MARCA + "” no começo do texto. Funciona melhor em atas "
      + "organizadas por assunto, como o update com cliente."));
    resumo.appendChild(agrupa);
    detalhe.appendChild(resumo);

    // ── o texto que a IA lê
    const rotulo = el("label", "modelos-ata__rotulo", "Instruções do modelo");
    rotulo.htmlFor = "modelo-texto";
    const texto = el("textarea", "aa-entrada modelos-ata__texto");
    texto.id = "modelo-texto";
    texto.spellcheck = false;
    texto.value = m.texto;
    detalhe.append(rotulo, texto);

    if (m.do_usuario && m.original) {
      const original = el("details", "modelos-ata__original");
      original.appendChild(el("summary", "", "Ver o original do app"));
      original.appendChild(el("pre", "", m.original));
      detalhe.appendChild(original);
    }

    const pe = el("div", "modelos-ata__pe");
    const salvar = botao(m.do_app && !m.do_usuario ? "Salvar como minha versão" : "Salvar",
                         "aa-btn-primario", async () => {
      salvar.disabled = true;
      aviso.textContent = "salvando…";
      try {
        const r = await pedir("salvar-modelo-de-ata", { modelo: m.id, texto: texto.value });
        trocarTipos(r.tipos);
        await abrir(m.id);
      } catch (e) {
        aviso.textContent = "";
        salvar.disabled = false;
        pe.before(alerta(e.message, "erro"));
      }
    });
    salvar.disabled = true;
    const aviso = el("span", "campo__dica");
    aviso.setAttribute("role", "status");
    pe.append(salvar, aviso);
    detalhe.appendChild(pe);

    const mudou = () => {
      sujo = texto.value !== m.texto;
      salvar.disabled = !sujo;
      aviso.textContent = sujo ? "mudanças não salvas" : "";
      chave.checked = texto.value.includes(MARCA);
    };
    texto.addEventListener("input", mudou);
    chave.addEventListener("change", () => {
      texto.value = comMarca(texto.value, chave.checked);
      mudou();
    });
  }

  desenharLista();
  if (!tipos.some((t) => t.id === aberto)) aberto = tipos[0]?.id ?? null;
  if (aberto) abrir(aberto);
  else detalhe.appendChild(el("p", "modelos-ata__vazio campo__dica", "Nenhum tipo de ata."));
  return b;
}
