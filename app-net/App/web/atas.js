// A ata de uma reunião: escolher o tipo, gerar, acompanhar, ler, copiar e
// exportar. Mora na aba Ata da reunião aberta (reuniao.js).
//
// Foi um destino próprio de 14/08 a 24/09/2026 (FASE3.md §4): a ata "tinha vida
// própria" — é o que se copia para o e-mail, o que se relê dias depois, o que se
// regenera quando a transcrição foi corrigida. Tudo isso continua; o que saiu
// foi a segunda lista de reuniões que o destino desenhava para chegar a ela.
// Ver a decisão D-A de docs/superpowers/specs/2026-09-23-ui-ux.md §6.

import { pedir } from "/ponte.js";
import { alerta, campo } from "/pecas.js";
import { assinarTranscricoes, emCurso, ultimoResultado, cancelar } from "/transcricoes.js";
import { tituloDe } from "/app.js";

// A transcrição e a separação de falantes dividem o registro com a ata. A aba
// Ata só acompanha o que é dela: tomar a separação de falantes por uma ata
// escrevendo mostrava "Escrevendo…", e o Parar daqui a cancelava.
const ataEmCurso = (caminho) => (emCurso(caminho)?.tarefa === "ata" ? emCurso(caminho) : null);
const fimDaAta = (caminho) => (ultimoResultado(caminho)?.tarefa === "ata" ? ultimoResultado(caminho) : null);

const ETAPAS = {
  modelo: "Carregando o modelo",
  lendo: "Lendo a reunião",
  montagem: "Montando a ata",
};

/**
 * Copiar e exportar a ata — o que se faz com ela depois de lida.
 *
 * @param depois onde pôr o caminho do arquivo exportado, ou o erro: logo
 *   depois deste elemento.
 */
function botoesDaAta(markdown, gravacao, depois) {
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

  // Exportar leva a ata para a pasta das atas, que é configurada à parte da de
  // transcrições — a ata é o que sai para o cliente.
  const exportar = document.createElement("button");
  exportar.className = "aa-btn aa-btn-texto";
  exportar.type = "button";
  exportar.textContent = "Exportar";
  exportar.addEventListener("click", async () => {
    exportar.disabled = true;
    try {
      const r = await pedir("exportar-ata", { gravacao: gravacao.caminho,
                                              nome: tituloDe(gravacao) });
      exportar.textContent = "Exportada";
      // O caminho fica na tela: exportar sem dizer onde obriga a procurar.
      const onde = document.createElement("p");
      onde.className = "campo__dica";
      onde.textContent = r.arquivo;
      depois.after(onde);
    } catch (e) {
      depois.after(alerta(e.message, "erro"));
    } finally {
      exportar.disabled = false;
      setTimeout(() => { exportar.textContent = "Exportar"; }, 2000);
    }
  });

  return [copiar, exportar];
}

// ───────────────────────────────────────── a ata na aba da reunião

/**
 * A ata de uma reunião, na aba Ata da reunião aberta (reuniao.js).
 *
 * É o que era o cartão da tela de Atas, sem a dobra e sem o título: numa aba
 * que é só a ata, dobrá-la esconderia a única coisa que a aba mostra, e o
 * título da reunião já está na barra do topo.
 *
 * @param aoContar recebe o número de pendências a cada vez que a ata é lida —
 *   ao abrir e depois de cada geração. É o que mantém certo o selo da aba, que
 *   nasceu do resumo da lista, lido antes de a ata nova existir.
 */
export async function montarAta(painel, g, { aoContar } = {}) {
  let tipos, doProjeto;
  try {
    // O tipo de ata padrão do projeto (Ajustes › Clientes, plano 4a). Sem
    // projeto, ou sem preferência, fica o primeiro da lista, como sempre foi.
    [{ tipos }, doProjeto] = await Promise.all([
      pedir("modelos-de-ata"),
      g.cliente && g.projeto
        ? pedir("prefs", { cliente: g.cliente, projeto: g.projeto })
          .then((r) => r.prefs?.tipo_de_ata || null, () => null)
        : null,
    ]);
  } catch (e) {
    painel.replaceChildren(alerta(e.message, "erro"));
    return;
  }

  const raiz = document.createElement("div");
  raiz.className = "ata";
  raiz.dataset.gravacao = g.caminho;

  const topo = document.createElement("div");
  topo.className = "ata__topo";
  const escolha = campo("Tipo de ata", "select", {
    id: `tipo-${g.nome}`,
    opcoes: tipos.map((t) => t.nome),
  });
  escolha.classList.add("ata__tipo");
  const padrao = tipos.find((t) => t.id === doProjeto);
  if (padrao) escolha.querySelector("select").value = padrao.nome;
  const botao = document.createElement("button");
  botao.className = "aa-btn aa-btn-primario";
  botao.type = "button";
  botao.textContent = "Gerar ata";
  botao.dataset.acao = "ata";
  topo.append(escolha, botao);

  const andamento = document.createElement("div");
  andamento.className = "ata__painel";
  const corpo = document.createElement("div");
  corpo.className = "ata__corpo";
  raiz.append(topo, andamento, corpo);
  painel.replaceChildren(raiz);

  const idDoTipo = () => {
    const nome = escolha.querySelector("select").value;
    return (tipos.find((t) => t.nome === nome) ?? tipos[0]).id;
  };
  const carregar = () => carregarAta(g, corpo, botao, aoContar);

  botao.addEventListener("click", async () => {
    botao.disabled = true;
    try {
      await pedir("gerar-ata", { gravacao: g.caminho, modelo: idDoTipo() });
      acompanhar(g, botao, andamento, carregar);
    } catch (e) {
      botao.disabled = false;
      andamento.replaceChildren(alerta(e.message, "erro"));
    }
  });

  await carregar();
  if (ataEmCurso(g.caminho)) acompanhar(g, botao, andamento, carregar);
}

/** Lê a ata do disco e a desenha aberta. Sem ata, o corpo fica vazio e o botão diz gerar. */
async function carregarAta(g, corpo, botao, aoContar) {
  let r;
  try {
    r = await pedir("ata", { gravacao: g.caminho });
  } catch {
    return;   // sem ata é o estado normal de quem nunca gerou
  }
  if (!r.ata) {
    corpo.replaceChildren();
    return;
  }
  botao.textContent = "Refazer ata";
  botao.className = "aa-btn aa-btn-secundario";

  const cabeca = document.createElement("div");
  cabeca.className = "ata__cabeca";
  const estado = document.createElement("p");
  estado.className = "ata__estado";
  const linhas = r.ata.split("\n").filter((l) => l.trim().length > 0).length;
  const pendencias = (r.ata.match(/^- \[ \]/gm) ?? []).length;
  aoContar?.(pendencias);
  estado.textContent = pendencias > 0
    ? `${pendencias} pendência${pendencias > 1 ? "s" : ""} · ${linhas} linhas`
    : `${linhas} linhas`;
  cabeca.append(estado, ...botoesDaAta(r.ata, g, cabeca));

  const texto = document.createElement("div");
  texto.className = "ata__texto";
  texto.append(...renderizar(r.ata));

  corpo.replaceChildren(cabeca);
  if (r.ata_velha) {
    corpo.appendChild(alerta(
      "A transcrição foi corrigida depois que esta ata foi escrita. Vale refazer.", "aviso"));
  }
  corpo.appendChild(texto);
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

/**
 * A geração em curso, desenhada do registro do núcleo — como a transcrição.
 *
 * @param aoTerminar o que fazer quando a ata fica pronta: reler e desenhar.
 *   Quem sabe onde a ata aparece é quem chamou.
 */
function acompanhar(g, botao, painel, aoTerminar) {
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
  const atual = ataEmCurso(g.caminho);
  if (atual) pintar(atual);

  const cancelarAssinatura = assinarTranscricoes(() => {
    if (!painel.isConnected) { cancelarAssinatura(); return; }

    const rodando = ataEmCurso(g.caminho);
    if (rodando) { pintar(rodando); return; }

    const fim = fimDaAta(g.caminho);
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

    aoTerminar();
  });
}
