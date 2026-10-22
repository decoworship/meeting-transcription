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
import { blocosDaAta, htmlParaEmail } from "/ata-regras.js";

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
function botoesDaAta(lerMarkdown, gravacao, depois) {
  const copiar = document.createElement("button");
  copiar.className = "aa-btn aa-btn-texto";
  copiar.type = "button";
  copiar.textContent = "Copiar";
  copiar.addEventListener("click", async () => {
    // A ata existe para ser colada num e-mail. Vão duas versões, e quem cola
    // escolhe: o Gmail, o Outlook e o Word pegam o HTML, com fundo branco e
    // estilo em cada elemento; o que só aceita texto pega o Markdown.
    // O texto de agora, e não o de quando a ata abriu: as caixinhas marcadas
    // depois vão junto.
    const markdown = lerMarkdown();
    try {
      await navigator.clipboard.write([new ClipboardItem({
        "text/html": new Blob([htmlParaEmail(markdown)], { type: "text/html" }),
        "text/plain": new Blob([markdown], { type: "text/plain" }),
      })]);
    } catch {
      // Sem ClipboardItem, ou recusado: ao menos o Markdown, como sempre foi.
      await navigator.clipboard.writeText(markdown);
    }
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
      exportar.textContent = "Exportada em Word";
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
  let atual = r.ata;
  // Recontado a cada caixinha: a pendência marcada deixa de estar aberta, aqui
  // e no selo da aba — a mesma régua do EstadoDaAta, que só conta "- [ ]".
  const contar = () => {
    const linhas = atual.split("\n").filter((l) => l.trim().length > 0).length;
    const pendencias = (atual.match(/^- \[ \]/gm) ?? []).length;
    aoContar?.(pendencias);
    estado.textContent = pendencias > 0
      ? `${pendencias} pendência${pendencias > 1 ? "s" : ""} · ${linhas} linhas`
      : `${linhas} linhas`;
  };
  contar();
  cabeca.append(estado, ...botoesDaAta(() => atual, g, cabeca));

  // A marca vai para o ata.md (Nucleo/Atas/Pendencias.cs); depois relê o
  // arquivo, para o Copiar e a contagem verem o que está em disco.
  let aviso = null;
  const aoMarcar = async (indice, textoDaPendencia, feita) => {
    aviso?.remove();
    try {
      await pedir("marcar-pendencia",
        { gravacao: g.caminho, indice, texto: textoDaPendencia, feita });
      const relida = await pedir("ata", { gravacao: g.caminho });
      if (relida.ata) atual = relida.ata;
      contar();
      return true;
    } catch (e) {
      aviso = alerta(e.message, "erro");
      cabeca.after(aviso);
      return false;
    }
  };

  const texto = document.createElement("div");
  texto.className = "ata__texto";
  texto.append(...renderizar(r.ata, { aoMarcar }));

  corpo.replaceChildren(cabeca);
  if (r.ata_velha) {
    corpo.appendChild(alerta(
      "A transcrição foi corrigida depois que esta ata foi escrita. Vale refazer.", "aviso"));
  }
  corpo.appendChild(texto);

  // As observações do verificador, quando a ata é por tema (ATA-5): moram fora
  // do ata.md para não irem ao cliente, e ficam aqui, recolhidas, para quem
  // gerou conferir números e compromissos.
  if (r.observacoes) {
    const obs = document.createElement("details");
    obs.className = "ata__observacoes";
    const titulo = document.createElement("summary");
    titulo.textContent = "Observações sobre a transcrição";
    const dentro = document.createElement("div");
    dentro.className = "ata__texto";
    // O arquivo começa com o próprio título; o <summary> já o diz. "[^\n]", e
    // não ".": no Windows a linha termina em \r\n, e o "." para no \r.
    dentro.append(...renderizar(r.observacoes.replace(/^##[^\n]*\n+(?:\r?\n)*/, "")));
    obs.append(titulo, dentro);
    corpo.appendChild(obs);
  }
}

/**
 * A ata desenhada na tela, a partir dos blocos de ata-regras.js.
 *
 * Monta nós, nunca innerHTML: o texto vem de um modelo de linguagem, e o dia em
 * que uma transcrição contiver algo parecido com uma tag não pode ser o dia em
 * que o app a executa.
 */
function renderizar(markdown, { aoMarcar } = {}) {
  const nos = [];
  // O número da caixinha, na ordem em que aparece: é o que o núcleo usa para
  // achar a linha (e confere pelo texto, caso a ata tenha mudado).
  let caixinhas = -1;

  for (const bloco of blocosDaAta(markdown)) {
    if (bloco.tipo === "titulo") {
      const h = document.createElement(`h${bloco.nivel}`);
      h.append(...comNegrito(bloco.trechos));
      nos.push(h);
      continue;
    }

    if (bloco.tipo === "lista") {
      const lista = document.createElement("ul");
      for (const item of bloco.itens) {
        const li = document.createElement("li");
        if (item.caixa !== null) {
          li.className = "ata__pendencia";
          const caixa = document.createElement("input");
          caixa.type = "checkbox";
          caixa.checked = item.caixa;
          li.classList.toggle("ata__pendencia--feita", caixa.checked);
          // Sem quem grave, só leitura: um check que some ao recarregar é pior
          // que check nenhum.
          caixa.disabled = !aoMarcar;
          if (aoMarcar) {
            const indice = ++caixinhas;
            const textoDaPendencia = item.texto;
            caixa.setAttribute("aria-label", "Pendência feita");
            caixa.addEventListener("change", async () => {
              const feita = caixa.checked;
              li.classList.toggle("ata__pendencia--feita", feita);
              caixa.disabled = true;
              const gravou = await aoMarcar(indice, textoDaPendencia, feita);
              if (!gravou) {
                caixa.checked = !feita;
                li.classList.toggle("ata__pendencia--feita", !feita);
              }
              caixa.disabled = false;
            });
          }
          li.appendChild(caixa);
        }
        li.append(...comNegrito(item.trechos));
        lista.appendChild(li);
      }
      nos.push(lista);
      continue;
    }

    const p = document.createElement("p");
    if (bloco.rotulo) p.classList.add("ata__rotulo", `ata__rotulo--${bloco.rotulo}`);
    p.append(...comNegrito(bloco.trechos));
    nos.push(p);
  }
  return nos;
}

function comNegrito(trechos) {
  return trechos.map(({ texto, forte }) => {
    if (!forte) return document.createTextNode(texto);
    const s = document.createElement("strong");
    s.textContent = texto;
    return s;
  });
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
