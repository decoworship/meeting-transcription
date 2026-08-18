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
import { corDoFalante, abrirGaveta, secao, campo, alerta,
         campoComSugestoes, preencherSugestoes } from "/pecas.js";
import { blocoDeNotas } from "/notas.js";

let estado = null;
let aguardando = null;

const audio = document.getElementById("audio");

/**
 * Toca a gravação a partir de um instante.
 *
 * O arquivo é o mix — a mesma soma das faixas que o ASR ouviu —, então os
 * tempos da transcrição batem com o que se escuta. Sem isso, conferir se o
 * falante está certo exigiria abrir o WAV noutro programa e procurar o minuto
 * na mão.
 */
function ouvirA(segundos) {
  if (!audio.src) {
    // Mapeado em JanelaDoApp: o WebView2 serve direto do disco, com Range, que
    // é o que faz pular para o meio de um WAV de 200 MB ser instantâneo.
    audio.src = `https://gravacoes.local/${encodeURIComponent(estado.gravacao.nome)}/mix.wav`;
  }
  audio.currentTime = segundos;
  audio.play().catch((e) => marcarEstado(`sem áudio: ${e.message}`, true));
}

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
  else if (qual === "notas") abrirNotas();
  else if (qual === "exportar") abrirExportacao();
  else if (qual.startsWith("editar")) editar(Number(qual.split(":")[1] ?? 0));
}

export function telaDeRevisao(gravacao, dados, { cabecalho, tela, aoRefazer, aoApagar }) {
  audio.pause();
  audio.removeAttribute("src");

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

  // As notas escritas na reunião, ao lado da transcrição dela.
  //
  // Em gaveta pelo mesmo motivo dos falantes: mexer nelas sem perder o lugar no
  // texto. Quem lê a transcrição dias depois quer conferir o que anotou na hora
  // — e, quando a ata por LLM chegar, é este texto que vale mais que o que o
  // modelo ouviu (FASE3.md §3).
  const botaoNotas = document.createElement("button");
  botaoNotas.className = "aa-btn aa-btn-secundario";
  botaoNotas.type = "button";
  botaoNotas.textContent = "Notas";
  botaoNotas.addEventListener("click", abrirNotas);

  const botaoExportar = document.createElement("button");
  botaoExportar.className = "aa-btn aa-btn-primario";
  botaoExportar.type = "button";
  botaoExportar.textContent = "Exportar";
  botaoExportar.addEventListener("click", abrirExportacao);

  const parar = document.createElement("button");
  parar.className = "aa-btn aa-btn-texto";
  parar.type = "button";
  parar.textContent = "⏸";
  parar.title = "Parar o áudio";
  parar.addEventListener("click", () => {
    audio.pause();
    for (const o of document.querySelectorAll("[data-tocando]"))
      o.removeAttribute("data-tocando");
  });

  const estadoSalvo = document.createElement("span");
  estadoSalvo.className = "campo__dica";
  estadoSalvo.id = "estado-salvo";

  ferramentas.append(busca, parar, estadoSalvo, botaoNotas, botaoFalantes, botaoExportar);

  // As duas que destroem trabalho vão para um invólucro próprio, e o CSS o
  // empurra para a direita atrás de um fio. Antes elas eram apenas o sétimo e o
  // oitavo filho de uma grade de seis colunas, e por isso desciam para uma
  // segunda linha bem no meio do cabeçalho.
  const perigo = document.createElement("div");
  perigo.className = "ferramentas__perigo";

  if (aoRefazer) {
    const refazer = document.createElement("button");
    refazer.className = "aa-btn aa-btn-texto";
    refazer.type = "button";
    refazer.textContent = "Transcrever de novo";
    refazer.addEventListener("click", () => {
      // Confirmação porque o custo é assimétrico: refazer descarta a revisão
      // inteira e reprocessa o áudio, e o clique distraído não avisa antes.
      if (confirm("Isto descarta os nomes e as correções desta reunião. Continuar?"))
        aoRefazer();
    });
    perigo.appendChild(refazer);
  }

  // Apagar fica ao lado de refazer: são as duas ações que destroem trabalho, e
  // manter as duas no mesmo canto é mais legível que espalhá-las.
  if (aoApagar) perigo.appendChild(aoApagar);

  if (perigo.childElementCount) ferramentas.appendChild(perigo);

  const corpo = document.createElement("div");
  corpo.className = "transcricao";
  corpo.id = "corpo-transcricao";

  // Ferramentas e filtros num invólucro só: é ele que gruda no topo.
  const controles = document.createElement("div");
  controles.className = "controles";
  controles.append(ferramentas, filtros);

  raiz.append(controles, corpo);
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

  // O filtro das correções vem primeiro e só aparece quando houve alguma. É o
  // que torna as trocas inspecionáveis de verdade: sem ele, conferir 12 trocas
  // numa reunião de 400 trechos seria caçá-las uma a uma.
  const corrigidos = estado.dados.segments.filter((s) => s.swaps?.length).length;
  if (corrigidos > 0) {
    const b = document.createElement("button");
    b.className = "filtro filtro--troca";
    b.type = "button";
    b.setAttribute("aria-pressed", String(!!estado.soCorrigidos));
    b.textContent = `✎ ${corrigidos} ${corrigidos === 1 ? "correção" : "correções"}`;
    b.title = "Trocas feitas pelo vocabulário do projeto. Clique para ver só elas.";
    b.addEventListener("click", () => {
      estado.soCorrigidos = !estado.soCorrigidos;
      redesenhar();
    });
    estado.filtros.appendChild(b);
  } else if (estado.soCorrigidos) {
    // A última troca foi desfeita: sair do filtro sozinho, senão a tela fica
    // vazia sem explicar por quê.
    estado.soCorrigidos = false;
  }

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
    if (estado.soCorrigidos && !seg.swaps?.length) return;

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

    // A correção fonética é um palpite, e palpite tem que poder ser conferido.
    // Sem esta marca o texto parece ter saído assim do modelo, e uma troca
    // errada — palavra comum virando nome do vocabulário — passa despercebida.
    if (seg.swaps?.length) {
      const marca = document.createElement("button");
      marca.className = "troca";
      marca.type = "button";
      marca.textContent = "✎";
      marca.title = seg.swaps.map((s) => `"${s.from}" → "${s.to}"`).join("\n")
        + "\n\nCorreção pelo vocabulário do projeto. Clique para desfazer.";
      marca.setAttribute("aria-label", "Ver as correções deste trecho");
      marca.addEventListener("click", (e) => {
        e.stopPropagation();        // não tocar o áudio ao clicar na marca
        desfazerTrocas(indice);
      });
      texto.appendChild(marca);
    }

    linha.append(t, quem, texto);

    // Clique simples ouve daqui; duplo clique edita. É a divisão do app
    // antigo, e a que faz sentido: conferir o falante é o gesto frequente,
    // corrigir o texto é o raro.
    linha.addEventListener("click", () => {
      for (const o of corpo.querySelectorAll("[data-tocando]"))
        o.removeAttribute("data-tocando");
      linha.dataset.tocando = "true";
      ouvirA(seg.start);
    });
    linha.addEventListener("dblclick", () => editar(indice));
    corpo.appendChild(linha);
  });

  if (corpo.children.length === 0)
    corpo.appendChild(alerta("Nenhum trecho corresponde ao filtro.", "atencao"));
}

/**
 * Reverte as trocas da correção fonética num trecho.
 *
 * Troca ao contrário no texto atual, em vez de guardar o original: entre a
 * correção e este clique o usuário pode ter editado o trecho à mão, e restaurar
 * um original guardado apagaria essa edição. Reverter só as palavras deixa o
 * resto como está.
 */
function desfazerTrocas(indice) {
  const seg = estado.dados.segments[indice];
  if (!seg.swaps?.length) return;

  const lista = seg.swaps.map((s) => `"${s.to}" volta a ser "${s.from}"`).join("\n");
  if (!confirm(`Desfazer a correção deste trecho?\n\n${lista}`)) return;

  for (const s of seg.swaps) {
    // Só palavra inteira: sem isto, desfazer "Dimi"→"Dimitri" estragaria
    // qualquer outra palavra que contivesse as letras.
    const escapado = s.to.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    seg.text = seg.text.replace(new RegExp(`\\b${escapado}\\b`, "g"), s.from);
  }
  delete seg.swaps;

  desenharTrechos();
  salvar();
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

/**
 * A gaveta de notas.
 *
 * Monta um editor novo a cada abertura e o joga fora ao fechar: o bloco carrega
 * do disco ao montar e grava ao perder o foco, então guardá-lo entre aberturas
 * só criaria a chance de mostrar um texto velho depois de alguém editar o
 * arquivo por fora.
 */
function abrirNotas() {
  const corpo = document.getElementById("corpo-notas");
  const bloco = blocoDeNotas(estado.gravacao.caminho, { linhas: 18 });
  corpo.replaceChildren(bloco.raiz);
  abrirGaveta("gaveta-notas");
  bloco.campo.focus();
}

function abrirFalantes() {
  const corpo = document.getElementById("corpo-falantes");
  corpo.replaceChildren();

  const falantes = ordemDosFalantes(estado.dados.segments);
  const total = estado.dados.segments.reduce((s, x) => s + (x.end - x.start), 0);

  const s = secao("Nomear");

  const aviso = document.createElement("p");
  aviso.className = "campo__dica";
  aviso.textContent =
    "Ao nomear alguém, a voz é aprendida e reconhecida nas próximas reuniões.";

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
    entrada.addEventListener("change", async () => {
      const v = entrada.value.trim();
      if (v && v !== cru) estado.nomes.set(cru, v);
      else estado.nomes.delete(cru);
      redesenhar();
      salvar();

      if (!v || v === cru) return;

      // Aprender a voz vem depois de gravar o nome, e não junto: o nome é o que
      // o usuário pediu, e a voz é o extra. Se extrair falhar, o nome fica.
      aviso.textContent = "aprendendo a voz…";
      try {
        const r = await pedir("aprender-voz", {
          gravacao: estado.gravacao.caminho,
          falante: cru,
          nome: v,
        });
        aviso.textContent = r.voz ?? "";
      } catch (e) {
        aviso.textContent = `não aprendeu a voz: ${e.message}`;
      }
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
  s.append(aviso, tabela);

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

async function abrirExportacao() {
  // Gaveta própria desde que as configurações viraram tela. Antes ela pegava a
  // gaveta de configurações emprestada e trocava o título — o que funcionava e
  // deixava duas telas sem dono do mesmo espaço.
  const corpo = document.getElementById("corpo-exportar");
  corpo.replaceChildren();

  // Cliente e projeto vêm da transcrição quando ela os tem — as feitas antes
  // deste campo existir não têm, e é aqui que dá para preencher sem refazer
  // nada. O que for digitado volta para o arquivo.
  const { clientes } = await pedir("clientes");
  const reuniao = secao("Reunião");
  const linha = document.createElement("div");
  linha.className = "linha";
  linha.append(
    campoComSugestoes("Cliente", "exp-cliente", Object.keys(clientes),
                      estado.dados.client ?? ""),
    campoComSugestoes("Projeto", "exp-projeto",
                      clientes[estado.dados.client] ?? [], estado.dados.project ?? ""),
  );
  reuniao.appendChild(linha);

  // Trocar o cliente troca a lista de projetos sugeridos.
  linha.querySelector("#exp-cliente").addEventListener("input", (e) =>
    preencherSugestoes(document.getElementById("exp-projeto-lista"),
                       clientes[e.target.value] ?? []));

  const s = secao("Formato");

  const opcoes = document.createElement("div");
  opcoes.className = "secao";
  for (const [id, texto, marcado] of [
    ["com-falantes", "Incluir os nomes dos falantes", true],
    ["com-copia", "Salvar também uma cópia em outra pasta", false],
  ]) {
    const l = document.createElement("label");
    l.className = "campo campo--linha";
    const c = document.createElement("input");
    c.type = "checkbox";
    c.checked = marcado;
    c.id = id;
    const r = document.createElement("span");
    r.textContent = texto;
    l.append(c, r);
    opcoes.appendChild(l);
  }

  const resultado = document.createElement("p");
  resultado.className = "campo__dica";

  for (const [formato, rotuloBotao, descricao] of [
    ["txt", "Texto (.txt)", "com marcas de tempo"],
    ["srt", "Legenda (.srt)", "para vídeo"],
    ["vtt", "Legenda (.vtt)", "para web"],
    ["docx", "Documento (.docx)", "formatado, com cor por falante"],
  ]) {
    const linha = document.createElement("div");
    linha.className = "acoes";

    const b = document.createElement("button");
    b.className = "aa-btn aa-btn-secundario";
    b.type = "button";
    b.textContent = rotuloBotao;
    b.addEventListener("click", async () => {
      resultado.textContent = "gerando…";
      try {
        const r = await pedir("exportar", {
          gravacao: estado.gravacao.caminho,
          formato,
          nome: estado.gravacao.titulo || estado.gravacao.nome,
          com_falantes: document.getElementById("com-falantes").checked,
          copiar: document.getElementById("com-copia").checked,
          // Vão para o cabeçalho do arquivo: um TXT que chega por e-mail sem
          // dizer de que reunião é obriga quem recebe a perguntar.
          cliente: document.getElementById("exp-cliente").value.trim(),
          projeto: document.getElementById("exp-projeto").value.trim(),
        });
        // O caminho inteiro, e não só "pronto": quem exporta precisa achar o
        // arquivo, e ele fica junto da gravação — não em Downloads.
        resultado.textContent = r.copia ? `${r.arquivo}\ne uma cópia em ${r.copia}` : r.arquivo;
      } catch (e) {
        resultado.textContent = `não deu: ${e.message}`;
      }
    });

    const d = document.createElement("span");
    d.className = "campo__dica";
    d.textContent = descricao;

    linha.append(b, d);
    s.appendChild(linha);
  }

  s.append(opcoes, resultado);
  corpo.append(reuniao, s);
  abrirGaveta("gaveta-exportar");
}
