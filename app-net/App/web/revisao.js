// A tela de depois da transcrição: ler, corrigir, nomear falantes, exportar.
//
// É a tela em que o usuário passa mais tempo, e o desenho segue disso:
//
// * a lista é uma linha por trecho — tempo, quem, o quê —, como no app antigo,
//   porque é o formato que se lê rápido;
// * corrigir abre um diálogo por cima (duplo clique), em vez de um painel no
//   topo: com centenas de trechos, editar o de baixo exigia rolar até o topo,
//   corrigir, e rolar de volta;
// * os falantes ficam numa gaveta lateral pelo mesmo motivo — dá para nomear
//   todo mundo sem sair de onde se estava lendo.

import { pedir } from "/ponte.js";
import { corDoFalante, abrirGaveta, secao, campo, alerta } from "/pecas.js";

let estado = null;
let aguardando = null;

/**
 * Grava a transcrição, juntando edições próximas numa escrita só.
 *
 * Sem espera, renomear três falantes seguidos daria três gravações do arquivo
 * inteiro. Com ela, o trabalho de revisão vira uma escrita a cada segundo
 * parado — e nada se perde, porque cada edição reinicia a contagem.
 */
function salvar() {
  marcarEstado("salvando…");
  clearTimeout(aguardando);
  aguardando = setTimeout(async () => {
    try {
      // Os nomes entram nos segmentos só na hora de gravar: durante a revisão
      // eles vivem à parte, para renomear em massa ser trocar uma entrada.
      const copia = {
        ...estado.dados,
        segments: estado.dados.segments.map((s) => ({
          ...s,
          speaker: nomeDe(s.speaker ?? "Unknown"),
        })),
      };
      await pedir("salvar-transcricao", {
        gravacao: estado.gravacao.caminho,
        conteudo: JSON.stringify(copia, null, 2),
      });
      marcarEstado("salvo");
    } catch (e) {
      marcarEstado(`não salvou: ${e.message}`, true);
    }
  }, 800);
}

function marcarEstado(texto, erro = false) {
  const el = document.getElementById("estado-salvo");
  if (!el) return;
  el.textContent = texto;
  el.style.color = erro ? "var(--cor-erro)" : "var(--cor-texto-suave)";
}

export function abrirPainel(qual) {
  if (qual === "falantes") abrirFalantes();
  else if (qual === "exportar") abrirExportacao();
  else if (qual.startsWith("editar")) editar(Number(qual.split(":")[1] ?? 0));
}

export function telaDeRevisao(gravacao, dados, { cabecalho, tela }) {
  estado = {
    gravacao,
    dados,
    // Nomes aplicados sobre os rótulos crus. Ficam à parte dos segmentos para
    // renomear em massa ser trocar uma entrada, e não percorrer 387 trechos.
    nomes: new Map(),
    escondidos: new Set(),
    busca: "",
  };

  const falantes = ordemDosFalantes(dados.segments);
  cabecalho(
    gravacao.titulo || "Transcrição",
    `${dados.segments.length} trechos · ${falantes.length} falantes`
      + (dados.language ? ` · ${dados.language}` : ""),
    true,
  );

  const raiz = document.createElement("div");
  raiz.className = "revisao";

  // ---- ferramentas: buscar, filtrar, e os dois botões que abrem por cima
  const ferramentas = document.createElement("div");
  ferramentas.className = "ferramentas";

  const busca = document.createElement("input");
  busca.className = "aa-entrada";
  busca.type = "search";
  busca.placeholder = "Buscar na transcrição…";
  busca.addEventListener("input", () => {
    estado.busca = busca.value.trim();
    redesenhar();
  });

  const filtros = document.createElement("div");
  filtros.className = "filtros";

  const botaoFalantes = document.createElement("button");
  botaoFalantes.className = "aa-btn aa-btn-secundario";
  botaoFalantes.type = "button";
  botaoFalantes.textContent = "Falantes";
  botaoFalantes.addEventListener("click", abrirFalantes);

  const botaoExportar = document.createElement("button");
  botaoExportar.className = "aa-btn aa-btn-primario";
  botaoExportar.type = "button";
  botaoExportar.textContent = "Exportar";
  botaoExportar.addEventListener("click", abrirExportacao);

  const estadoSalvo = document.createElement("span");
  estadoSalvo.className = "campo__dica";
  estadoSalvo.id = "estado-salvo";

  ferramentas.append(busca, estadoSalvo, botaoFalantes, botaoExportar);

  const corpo = document.createElement("div");
  corpo.className = "transcricao";
  corpo.id = "corpo-transcricao";

  raiz.append(ferramentas, filtros, corpo);
  tela.replaceChildren(raiz);

  estado.filtros = filtros;
  redesenhar();
}

/** Os falantes na ordem em que aparecem, que é a ordem que faz sentido ler. */
function ordemDosFalantes(segmentos) {
  const vistos = [];
  for (const s of segmentos) {
    const n = s.speaker ?? "Unknown";
    if (!vistos.includes(n)) vistos.push(n);
  }
  return vistos;
}

const nomeDe = (cru) => estado.nomes.get(cru) ?? cru;

function redesenhar() {
  desenharFiltros();
  desenharTrechos();
}

function desenharFiltros() {
  const falantes = ordemDosFalantes(estado.dados.segments);
  estado.filtros.replaceChildren();

  falantes.forEach((cru, i) => {
    const b = document.createElement("button");
    b.className = "filtro";
    b.type = "button";
    const ligado = !estado.escondidos.has(cru);
    b.setAttribute("aria-pressed", String(ligado));
    b.style.color = corDoFalante(cru, i);

    const ponto = document.createElement("span");
    ponto.className = "filtro__ponto";
    b.append(ponto, document.createTextNode(nomeDe(cru)));

    b.addEventListener("click", () => {
      if (estado.escondidos.has(cru)) estado.escondidos.delete(cru);
      else estado.escondidos.add(cru);
      redesenhar();
    });
    estado.filtros.appendChild(b);
  });
}

function tempo(s) {
  const m = Math.floor(s / 60);
  return `${String(m).padStart(2, "0")}:${String(Math.floor(s % 60)).padStart(2, "0")}`;
}

function desenharTrechos() {
  const corpo = document.getElementById("corpo-transcricao");
  const falantes = ordemDosFalantes(estado.dados.segments);
  corpo.replaceChildren();

  const alvo = estado.busca.toLowerCase();

  estado.dados.segments.forEach((seg, indice) => {
    const cru = seg.speaker ?? "Unknown";
    if (estado.escondidos.has(cru)) return;
    if (alvo && !seg.text.toLowerCase().includes(alvo)) return;

    const linha = document.createElement("div");
    linha.className = "trecho";
    linha.dataset.indice = String(indice);

    const t = document.createElement("span");
    t.className = "trecho__tempo";
    t.textContent = tempo(seg.start);

    const quem = document.createElement("span");
    quem.className = "trecho__falante";
    quem.style.color = corDoFalante(cru, falantes.indexOf(cru));
    quem.textContent = nomeDe(cru);

    const texto = document.createElement("p");
    texto.className = "trecho__texto";
    marcar(texto, seg.text.trim(), alvo);

    linha.append(t, quem, texto);
    // Duplo clique abre o diálogo; o clique simples fica reservado para
    // "ouvir daqui", que é o que o app antigo faz.
    linha.addEventListener("dblclick", () => editar(indice));
    corpo.appendChild(linha);
  });

  if (corpo.children.length === 0)
    corpo.appendChild(alerta("Nenhum trecho corresponde ao filtro.", "atencao"));
}

/** Escreve o texto destacando as ocorrências da busca, sem innerHTML. */
function marcar(destino, texto, alvo) {
  if (!alvo) {
    destino.textContent = texto;
    return;
  }
  const baixo = texto.toLowerCase();
  let i = 0;
  while (true) {
    const achou = baixo.indexOf(alvo, i);
    if (achou < 0) {
      destino.append(texto.slice(i));
      return;
    }
    destino.append(texto.slice(i, achou));
    const m = document.createElement("mark");
    m.textContent = texto.slice(achou, achou + alvo.length);
    destino.appendChild(m);
    i = achou + alvo.length;
  }
}

// ─────────────────────────────────────────────── editar um trecho

const modal = document.getElementById("modal-segmento");

function editar(indice) {
  const seg = estado.dados.segments[indice];
  const falantes = ordemDosFalantes(estado.dados.segments);

  document.getElementById("titulo-segmento").textContent =
    `Trecho ${indice + 1} · ${tempo(seg.start)}–${tempo(seg.end)}`;
  document.getElementById("campo-texto").value = seg.text.trim();

  const select = document.getElementById("campo-falante");
  select.replaceChildren();
  for (const cru of falantes) {
    const o = document.createElement("option");
    o.value = cru;
    o.textContent = nomeDe(cru);
    o.selected = cru === (seg.speaker ?? "Unknown");
    select.appendChild(o);
  }

  modal.returnValue = "";
  modal.showModal();
  modal.dataset.indice = String(indice);
}

modal.addEventListener("close", () => {
  if (modal.returnValue !== "salvar") return;
  const indice = Number(modal.dataset.indice);
  const seg = estado.dados.segments[indice];
  seg.text = " " + document.getElementById("campo-texto").value.trim();
  seg.speaker = document.getElementById("campo-falante").value;
  redesenhar();
  salvar();
});

// ───────────────────────────────────────────────────── falantes

function abrirFalantes() {
  const corpo = document.getElementById("corpo-falantes");
  corpo.replaceChildren();

  const falantes = ordemDosFalantes(estado.dados.segments);
  const total = estado.dados.segments.reduce((s, x) => s + (x.end - x.start), 0);

  const s = secao("Nomear");
  const tabela = document.createElement("table");
  tabela.className = "tabela-falantes";

  const cabeca = document.createElement("tr");
  for (const h of ["Falante", "Nome", "Trechos", "Tempo", "Parte"]) {
    const th = document.createElement("th");
    th.textContent = h;
    cabeca.appendChild(th);
  }
  tabela.appendChild(cabeca);

  falantes.forEach((cru, i) => {
    const meus = estado.dados.segments.filter((x) => (x.speaker ?? "Unknown") === cru);
    const tempoDele = meus.reduce((s, x) => s + (x.end - x.start), 0);

    const tr = document.createElement("tr");

    const id = document.createElement("td");
    id.textContent = cru;
    id.style.color = corDoFalante(cru, i);
    id.style.fontWeight = "600";

    const nome = document.createElement("td");
    const entrada = document.createElement("input");
    entrada.className = "aa-entrada";
    entrada.value = nomeDe(cru);
    entrada.addEventListener("change", () => {
      const v = entrada.value.trim();
      if (v && v !== cru) estado.nomes.set(cru, v);
      else estado.nomes.delete(cru);
      redesenhar();
      salvar();
    });
    nome.appendChild(entrada);

    const n = document.createElement("td");
    n.className = "numero";
    n.textContent = String(meus.length);

    const td = document.createElement("td");
    td.className = "numero";
    td.textContent = tempo(tempoDele);

    const parte = document.createElement("td");
    const barra = document.createElement("div");
    barra.className = "barra-share";
    const dentro = document.createElement("div");
    dentro.style.width = `${(100 * tempoDele) / total}%`;
    dentro.style.background = corDoFalante(cru, i);
    barra.appendChild(dentro);
    parte.appendChild(barra);

    tr.append(id, nome, n, td, parte);
    tabela.appendChild(tr);
  });
  s.appendChild(tabela);

  // ---- fundir dois falantes
  const fundir = secao("Fundir");
  const explicacao = document.createElement("p");
  explicacao.className = "campo__dica";
  explicacao.textContent =
    "Quando a separação dividiu a mesma pessoa em dois falantes.";
  const linha = document.createElement("div");
  linha.className = "linha";
  linha.append(
    campo("Fundir", "select", { id: "fundir-de", opcoes: falantes.map(nomeDe) }),
    campo("em", "select", { id: "fundir-para", opcoes: falantes.map(nomeDe) }),
  );
  const acao = document.createElement("div");
  acao.className = "acoes";
  const b = document.createElement("button");
  b.className = "aa-btn aa-btn-secundario";
  b.type = "button";
  b.textContent = "Fundir";
  b.addEventListener("click", () => {
    const de = falantes[document.getElementById("fundir-de").selectedIndex];
    const para = falantes[document.getElementById("fundir-para").selectedIndex];
    if (de === para) return;

    // Fundir é reescrever o rótulo nos segmentos, e não criar um apelido: os
    // dois falantes deixam de existir separados, inclusive para o filtro.
    for (const s of estado.dados.segments)
      if ((s.speaker ?? "Unknown") === de) s.speaker = para;
    estado.nomes.delete(de);
    estado.escondidos.delete(de);

    redesenhar();
    salvar();
    abrirFalantes();
  });
  acao.appendChild(b);
  fundir.append(explicacao, linha, acao);

  corpo.append(s, fundir);
  abrirGaveta("gaveta-falantes");
}

// ───────────────────────────────────────────────────── exportar

function abrirExportacao() {
  const corpo = document.getElementById("corpo-config");
  corpo.replaceChildren();
  document.querySelector("#gaveta-config h2").textContent = "Exportar";

  const s = secao("Formato");
  for (const [rotulo, descricao] of [
    ["Texto (.txt)", "com marcas de tempo e nomes"],
    ["Legenda (.srt)", "para vídeo"],
    ["Legenda (.vtt)", "para web"],
    ["Documento (.docx)", "formatado, com cor por falante"],
  ]) {
    const linha = document.createElement("div");
    linha.className = "acoes";
    const b = document.createElement("button");
    b.className = "aa-btn aa-btn-secundario";
    b.type = "button";
    b.textContent = rotulo;
    const d = document.createElement("span");
    d.className = "campo__dica";
    d.textContent = descricao;
    linha.append(b, d);
    s.appendChild(linha);
  }

  corpo.appendChild(s);
  abrirGaveta("gaveta-config");
}
