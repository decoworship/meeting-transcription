// O destino Atas: escolher a reunião, escolher o tipo, gerar e ler.
//
// Gerar e ler acontecem aqui, sem passar por Reuniões — decisão do dono do
// produto (FASE3.md §4). Reuniões continua sendo gravar, transcrever e revisar;
// a ata tem vida própria: é o que se copia para o e-mail, o que se relê dias
// depois, e o que se regenera quando a transcrição foi corrigida.

import { pedir } from "/ponte.js";
import { alerta, campo } from "/pecas.js";
import { assinarTranscricoes, emCurso, ultimoResultado, cancelar } from "/transcricoes.js";
import { duracao, quando, tituloDe } from "/app.js";

const ETAPAS = {
  modelo: "Carregando o modelo",
  lendo: "Lendo a reunião",
  montagem: "Montando a ata",
};

export async function telaDeAtas(ctx) {
  const { cabecalho, tela } = ctx;
  cabecalho("Atas", "", false);
  tela.setAttribute("aria-busy", "true");
  tela.replaceChildren();

  let gravacoes, tipos;
  try {
    [{ gravacoes }, { tipos }] = await Promise.all([
      pedir("gravacoes"), pedir("modelos-de-ata"),
    ]);
  } catch (e) {
    tela.setAttribute("aria-busy", "false");
    tela.replaceChildren(alerta(e.message, "erro"));
    return;
  }
  tela.setAttribute("aria-busy", "false");

  // Só reunião transcrita: a ata é escrita a partir da transcrição, e oferecer
  // as outras seria oferecer um botão que só sabe dizer não.
  const prontas = gravacoes.filter((g) => g.transcrita);
  cabecalho("Atas", prontas.length === 1
    ? "1 reunião transcrita" : `${prontas.length} reuniões transcritas`, false);

  if (prontas.length === 0) {
    const vazio = document.createElement("p");
    vazio.className = "vazio";
    vazio.textContent =
      "Nenhuma reunião transcrita ainda. A ata é escrita a partir da transcrição.";
    tela.append(vazio);
    return;
  }

  for (const g of prontas) tela.appendChild(cartaoDeAta(g, tipos, ctx));
}

/**
 * Um cartão por reunião: o que se sabe dela, o tipo, e o botão.
 *
 * A ata aberta fica no próprio cartão, e não numa tela à parte: ela tem meia
 * página, e mandar o usuário a outro destino para ler meia página o faria voltar
 * a cada reunião que quisesse conferir.
 */
function cartaoDeAta(g, tipos, ctx) {
  const raiz = document.createElement("div");
  raiz.className = "aa-cartao ata";
  raiz.dataset.gravacao = g.caminho;

  const topo = document.createElement("div");
  topo.className = "ata__topo";

  const esquerda = document.createElement("div");
  const titulo = document.createElement("p");
  titulo.className = "gravacao__titulo";
  titulo.textContent = tituloDe(g);
  const meta = document.createElement("p");
  meta.className = "gravacao__meta";
  for (const parte of [
    [g.cliente, g.projeto].filter(Boolean).join(" · "),
    duracao(g.duracao_s), quando(g.nome),
  ].filter(Boolean)) {
    const s = document.createElement("span");
    s.textContent = parte;
    meta.appendChild(s);
  }
  esquerda.append(titulo, meta);

  const escolha = campo("Tipo de reunião", "select", {
    id: `tipo-${g.nome}`,
    opcoes: tipos.map((t) => t.nome),
  });
  escolha.classList.add("ata__tipo");

  const botao = document.createElement("button");
  botao.className = "aa-btn aa-btn-primario";
  botao.type = "button";
  botao.textContent = "Gerar ata";

  topo.append(esquerda, escolha, botao);

  const painel = document.createElement("div");
  painel.className = "ata__painel";

  const corpo = document.createElement("div");
  corpo.className = "ata__corpo";

  raiz.append(topo, painel, corpo);

  const idDoTipo = () => {
    const nome = escolha.querySelector("select").value;
    return (tipos.find((t) => t.nome === nome) ?? tipos[0]).id;
  };

  botao.addEventListener("click", async () => {
    botao.disabled = true;
    try {
      await pedir("gerar-ata", { gravacao: g.caminho, modelo: idDoTipo() });
      acompanhar(g, botao, painel, corpo);
    } catch (e) {
      botao.disabled = false;
      painel.replaceChildren(alerta(e.message, "erro"));
    }
  });

  // Uma ata que já existe abre junto com a tela: quem vem aqui quer lê-la, e
  // exigir um clique para mostrar o que já está pronto é pedágio.
  mostrarAtaExistente(g, corpo, botao);
  if (emCurso(g.caminho)) acompanhar(g, botao, painel, corpo);

  return raiz;
}

async function mostrarAtaExistente(g, corpo, botao) {
  try {
    const r = await pedir("ata", { gravacao: g.caminho });
    if (!r.ata) return;
    botao.textContent = "Refazer ata";
    botao.className = "aa-btn aa-btn-secundario";
    desenharAta(corpo, r.ata, r.ata_velha, g);
  } catch {
    // Sem ata é o estado normal de quem nunca gerou.
  }
}

/** A ata em si, com o aviso de desatualizada e o botão de copiar. */
function desenharAta(corpo, markdown, velha, g) {
  corpo.replaceChildren();

  if (velha) {
    corpo.appendChild(alerta(
      "A transcrição foi corrigida depois que esta ata foi escrita. "
      + "Vale refazer.", "aviso"));
  }

  const acoes = document.createElement("div");
  acoes.className = "ata__acoes";

  const copiar = document.createElement("button");
  copiar.className = "aa-btn aa-btn-texto";
  copiar.type = "button";
  copiar.textContent = "Copiar";
  copiar.addEventListener("click", async () => {
    // A ata existe para ser colada num e-mail. Markdown puro, e não o texto
    // renderizado: é o que o Teams, o Slack e o e-mail entendem.
    await navigator.clipboard.writeText(markdown);
    copiar.textContent = "Copiado";
    setTimeout(() => { copiar.textContent = "Copiar"; }, 1500);
  });
  acoes.appendChild(copiar);

  const texto = document.createElement("div");
  texto.className = "ata__texto";
  texto.append(...renderizar(markdown));

  corpo.append(acoes, texto);
}

/**
 * Markdown suficiente para uma ata, sem biblioteca.
 *
 * São seis construções — título, item de lista, checkbox, negrito, parágrafo e
 * regra — e uma biblioteca custaria mais que isso em bytes e em CSP. Monta nós,
 * nunca innerHTML: o texto vem de um modelo de linguagem, e o dia em que uma
 * transcrição contiver algo parecido com uma tag não pode ser o dia em que o app
 * a executa.
 */
function renderizar(markdown) {
  const nos = [];
  let lista = null;

  const fecharLista = () => { lista = null; };

  for (const linha of markdown.split("\n")) {
    const t = linha.trim();

    if (t.length === 0) { fecharLista(); continue; }

    const titulo = t.match(/^(#{1,4})\s+(.*)$/);
    if (titulo) {
      fecharLista();
      const h = document.createElement(`h${Math.min(6, titulo[1].length + 1)}`);
      h.append(...comNegrito(titulo[2]));
      nos.push(h);
      continue;
    }

    const item = t.match(/^[-*]\s+(?:\[( |x)\]\s+)?(.*)$/);
    if (item) {
      if (!lista) {
        lista = document.createElement("ul");
        nos.push(lista);
      }
      const li = document.createElement("li");
      if (item[1] !== undefined) {
        li.className = "ata__pendencia";
        const caixa = document.createElement("input");
        caixa.type = "checkbox";
        caixa.checked = item[1] === "x";
        // Só leitura: marcar aqui não voltaria para o arquivo, e um check que
        // some ao recarregar é pior que check nenhum.
        caixa.disabled = true;
        li.appendChild(caixa);
      }
      li.append(...comNegrito(item[2]));
      lista.appendChild(li);
      continue;
    }

    fecharLista();
    const p = document.createElement("p");
    p.append(...comNegrito(t));
    nos.push(p);
  }
  return nos;
}

function comNegrito(texto) {
  const nos = [];
  let resto = texto;
  const negrito = /\*\*(.+?)\*\*/;

  let m;
  while ((m = negrito.exec(resto)) !== null) {
    if (m.index > 0) nos.push(document.createTextNode(resto.slice(0, m.index)));
    const forte = document.createElement("strong");
    forte.textContent = m[1];
    nos.push(forte);
    resto = resto.slice(m.index + m[0].length);
  }
  if (resto.length > 0) nos.push(document.createTextNode(resto));
  return nos;
}

/** A geração em curso, desenhada do registro do núcleo — como a transcrição. */
function acompanhar(g, botao, painel, corpo) {
  botao.disabled = true;
  botao.textContent = "Escrevendo…";

  const barra = document.createElement("div");
  barra.className = "aa-progresso";
  const preenchimento = document.createElement("div");
  barra.appendChild(preenchimento);

  const estado = document.createElement("p");
  estado.className = "campo__dica";

  const parar = document.createElement("button");
  parar.className = "aa-btn aa-btn-texto";
  parar.type = "button";
  parar.textContent = "Parar";
  parar.addEventListener("click", () => cancelar(g.caminho).catch(() => {}));

  const linha = document.createElement("div");
  linha.className = "progresso__linha";
  linha.append(estado, parar);
  painel.replaceChildren(barra, linha);

  const pintar = (t) => {
    estado.textContent = `${ETAPAS[t.etapa] ?? t.etapa}: ${t.texto}`;
    preenchimento.style.width = `${t.fracao >= 0 ? Math.round(t.fracao * 100) : 0}%`;
  };
  const atual = emCurso(g.caminho);
  if (atual) pintar(atual);

  const cancelarAssinatura = assinarTranscricoes(() => {
    if (!painel.isConnected) { cancelarAssinatura(); return; }

    const rodando = emCurso(g.caminho);
    if (rodando) { pintar(rodando); return; }

    const fim = ultimoResultado(g.caminho);
    if (!fim) return;

    cancelarAssinatura();
    botao.disabled = false;
    botao.textContent = "Gerar ata";
    painel.replaceChildren();

    if (fim.cancelada) {
      const nota = document.createElement("p");
      nota.className = "campo__dica";
      nota.textContent = "Interrompida. A placa foi liberada.";
      painel.replaceChildren(nota);
      return;
    }
    if (fim.erro) { painel.replaceChildren(alerta(fim.erro, "erro")); return; }

    mostrarAtaExistente(g, corpo, botao);
  });
}
