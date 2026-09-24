// A tela de Reuniões: achar a reunião e saber o que ela precisa em seguida.
//
// A lista de cartões não deixava procurar — seis por tela, e o acervo passou
// de 70 (docs/superpowers/specs/2026-09-23-ui-ux.md §1). Aqui cada gravação é
// uma linha, as linhas se agrupam por dia, e a busca e os filtros cortam a
// lista sem sair da tela.
//
// **As regras moram em reunioes-regras.js**, sem DOM e testadas fora do
// navegador. Este arquivo só desenha, e a prova dele é tools/provar_reunioes.py.

import { pedir } from "/ponte.js";
import { alerta, anunciar } from "/pecas.js";
import { assinarTranscricoes, emCurso, ultimoResultado } from "/transcricoes.js";
import { duracao, tituloDe, abrirGravacao, abrirGravador } from "/app.js";
import { agruparPorDia, clientesDe, estadoDe, filtrar, horaDe, hojeLocal,
         ESTADOS, PERIODOS, SEM_CLIENTE } from "/reunioes-regras.js";

/**
 * Os critérios sobrevivem a abrir uma reunião e voltar.
 *
 * No módulo, e não no disco: são o "onde eu estava", não uma preferência. Quem
 * filtrou por Algar, abriu uma reunião e clicou em ← Reuniões espera a lista
 * como a deixou.
 */
const criterios = { texto: "", cliente: "", periodo: "tudo", data: "", estado: "" };

const TOM = {
  info: "aa-etiqueta aa-etiqueta--info",
  atencao: "aa-etiqueta aa-etiqueta--atencao",
  sucesso: "aa-etiqueta aa-etiqueta--sucesso",
  neutro: "aa-etiqueta",
};

const contagem = (n) => (n === 1 ? "1 gravação" : `${n} gravações`);

/**
 * Uma linha, no alto da lista, quando saiu versão nova.
 *
 * **Por que aqui e não só nos Ajustes.** O aviso existe para chegar a quem não
 * é quem compila o app — e essa pessoa não abre Ajustes por esporte. A tela de
 * Reuniões é a que ela vê todo dia; um aviso que ninguém encontra é o mesmo que
 * aviso nenhum.
 *
 * Uma linha, dispensável com um clique, e nunca um diálogo por cima: quem abriu
 * o app queria ver as reuniões, não conversar sobre versões.
 *
 * Assíncrono e à prova de falha: sem rede, sem GitHub, sem nada — a lista
 * aparece igual e ninguém fica sabendo que houve uma tentativa.
 */
let avisoDeVersaoDispensado = false;

function avisarDeVersaoNova(tela) {
  if (avisoDeVersaoDispensado) return;

  pedir("atualizacao").then((r) => {
    const a = r.atualizacao;
    if (!a?.nova || avisoDeVersaoDispensado) return;

    const linha = document.createElement("div");
    linha.className = "aa-alerta aa-alerta--atencao";

    const ponto = document.createElement("span");
    ponto.className = "aa-alerta__ponto";

    const texto = document.createElement("span");
    texto.textContent = `Saiu a versão ${a.nova.versao}`
      + (a.nova.notas ? ` — ${a.nova.notas}` : ".")
      + " A sua é a " + a.versao_instalada + ".";

    const dispensar = document.createElement("button");
    dispensar.className = "aa-btn aa-btn-texto";
    dispensar.type = "button";
    dispensar.textContent = "Dispensar";
    dispensar.addEventListener("click", () => {
      avisoDeVersaoDispensado = true;
      linha.remove();
    });

    linha.append(ponto, texto, dispensar);
    tela.prepend(linha);
  }).catch(() => {
    // Sem rede não é assunto de quem só queria ver as reuniões.
  });
}

export async function telaDeReunioes({ cabecalho, tela }) {
  cabecalho("Reuniões", "", false);
  tela.setAttribute("aria-busy", "true");
  tela.replaceChildren();

  let gravacoes;
  try {
    ({ gravacoes } = await pedir("gravacoes"));
  } catch (e) {
    tela.setAttribute("aria-busy", "false");
    tela.replaceChildren(alerta(e.message, "erro"));
    return;
  }
  tela.setAttribute("aria-busy", "false");
  tela.replaceChildren();
  avisarDeVersaoNova(tela);

  if (gravacoes.length === 0) {
    cabecalho("Reuniões", "Nenhuma gravação encontrada", false);
    const vazio = document.createElement("p");
    vazio.className = "vazio";
    // Não fala mais em "o MeetingRecorder": desde a Fase 2.5 o gravador é
    // este mesmo app.
    vazio.textContent = "Nenhuma gravação ainda. Comece uma em Gravador.";
    const ir = document.createElement("button");
    ir.className = "aa-btn aa-btn-primario";
    ir.type = "button";
    ir.textContent = "Ir para o Gravador";
    ir.addEventListener("click", abrirGravador);
    tela.append(vazio, ir);
    return;
  }

  const hoje = hojeLocal();
  const raiz = document.createElement("div");
  raiz.className = "reunioes";

  // ---- a barra: desenhada uma vez só.
  //
  // Filtrar redesenha a lista, nunca isto. Redesenhar o campo a cada tecla
  // tiraria o cursor de quem digita — o F-2 de docs/FASE7-FRONTEND.md.
  const ferramentas = document.createElement("div");
  ferramentas.className = "reunioes__ferramentas";

  const busca = document.createElement("input");
  busca.type = "search";
  busca.id = "busca-reunioes";
  busca.className = "aa-entrada reunioes__busca";
  busca.placeholder = "Buscar por título, cliente, projeto, convidado ou data…";
  busca.setAttribute("aria-label", "Buscar reuniões");
  busca.value = criterios.texto;

  const filtroCliente = seletor("Cliente", "filtro-cliente", [
    ["", "Todos os clientes"],
    ...clientesDe(gravacoes).map((c) => [c.nome, `${c.nome} (${c.n})`]),
    [SEM_CLIENTE, "Sem cliente"],
  ], criterios.cliente);
  const filtroPeriodo = seletor("Data", "filtro-periodo", PERIODOS, criterios.periodo);

  // O dia de "Um dia…" e "Uma semana…", no calendário do próprio navegador.
  // Some quando o período não pede dia — um campo de data à toa na barra pede
  // um valor que não vai ser usado.
  const filtroData = document.createElement("input");
  filtroData.type = "date";
  filtroData.id = "filtro-data";
  filtroData.className = "aa-entrada reunioes__filtro";
  filtroData.value = criterios.data;
  function ajustarData() {
    const pede = criterios.periodo === "dia" || criterios.periodo === "semana";
    filtroData.hidden = !pede;
    filtroData.setAttribute("aria-label", criterios.periodo === "semana"
      ? "Um dia da semana que você procura" : "O dia que você procura");
  }
  ajustarData();

  const filtroEstado = seletor("Estado", "filtro-estado", ESTADOS, criterios.estado);

  // Um cliente que sumiu desde a última visita não pode continuar filtrando em
  // silêncio: o seletor caiu em "Todos", e o critério cai junto.
  criterios.cliente = filtroCliente.value;

  ferramentas.append(busca, filtroCliente, filtroPeriodo, filtroData, filtroEstado);

  const lista = document.createElement("section");
  lista.className = "reunioes__lista";
  lista.setAttribute("aria-label", "Gravações");

  raiz.append(ferramentas, lista);
  tela.appendChild(raiz);

  /** caminho → linha, para trocar a etiqueta sem redesenhar a lista. */
  const linhas = new Map();
  let visiveis = [];

  function desenhar() {
    visiveis = filtrar(gravacoes, criterios, { hoje, rodandoDe: emCurso });
    cabecalho("Reuniões", visiveis.length === gravacoes.length
      ? contagem(gravacoes.length)
      : `${visiveis.length} de ${contagem(gravacoes.length)}`, false);

    linhas.clear();
    lista.replaceChildren();
    if (visiveis.length === 0) {
      lista.appendChild(semResultado());
      return;
    }

    for (const grupo of agruparPorDia(visiveis, hoje)) {
      const dia = document.createElement("h2");
      dia.className = "reunioes__dia";
      dia.textContent = grupo.rotulo;
      lista.appendChild(dia);
      for (const g of grupo.itens) {
        const l = linha(g);
        linhas.set(g.caminho, l);
        lista.appendChild(l);
      }
    }
  }

  function linha(g) {
    const b = document.createElement("button");
    b.type = "button";
    b.className = "reuniao-linha";
    b.dataset.gravacao = g.caminho;
    // O texto inteiro dos avisos, que na linha viram só "1 aviso".
    if (g.avisos.length > 0) b.title = g.avisos.join("\n");

    const hora = document.createElement("span");
    hora.className = "reuniao-linha__hora";
    hora.textContent = horaDe(g.nome);

    const textos = document.createElement("span");
    textos.className = "reuniao-linha__textos";
    const titulo = document.createElement("span");
    titulo.className = "reuniao-linha__titulo";
    titulo.textContent = tituloDe(g);
    const meta = document.createElement("span");
    meta.className = "reuniao-linha__meta";
    meta.textContent = metaDe(g);
    textos.append(titulo, meta);

    b.append(hora, textos, etiqueta(g));
    b.addEventListener("click", () => abrirGravacao(g));
    return b;
  }

  function semResultado() {
    const caixa = document.createElement("div");
    caixa.className = "reunioes__vazio";
    const p = document.createElement("p");
    p.textContent = "Nenhuma reunião com esses filtros.";
    const limpar = document.createElement("button");
    limpar.type = "button";
    limpar.className = "aa-btn aa-btn-secundario";
    limpar.textContent = "Limpar filtros";
    limpar.addEventListener("click", () => {
      Object.assign(criterios, { texto: "", cliente: "", periodo: "tudo", data: "", estado: "" });
      busca.value = "";
      filtroCliente.value = "";
      filtroPeriodo.value = "tudo";
      filtroData.value = "";
      ajustarData();
      filtroEstado.value = "";
      aoMudar();
      busca.focus();
    });
    caixa.append(p, limpar);
    return caixa;
  }

  // Quantas sobraram, dito quando a pessoa para de digitar — e não a cada
  // letra, que faria o leitor de tela falar por cima da digitação.
  let anuncio = null;
  function aoMudar() {
    desenhar();
    clearTimeout(anuncio);
    anuncio = setTimeout(() => anunciar(
      visiveis.length === 1 ? "1 reunião" : `${visiveis.length} reuniões`), 600);
  }

  busca.addEventListener("input", () => { criterios.texto = busca.value; aoMudar(); });
  filtroCliente.addEventListener("change", () => { criterios.cliente = filtroCliente.value; aoMudar(); });
  filtroPeriodo.addEventListener("change", () => {
    criterios.periodo = filtroPeriodo.value;
    ajustarData();
    // Escolheu "Um dia…" e ainda não disse qual: o cursor vai para onde se diz.
    if (!filtroData.hidden && !filtroData.value) filtroData.focus();
    aoMudar();
  });
  filtroData.addEventListener("change", () => { criterios.data = filtroData.value; aoMudar(); });
  filtroEstado.addEventListener("change", () => { criterios.estado = filtroEstado.value; aoMudar(); });

  desenhar();

  // A etiqueta acompanha o trabalho da placa: quem fica parado na lista vê
  // "Transcrevendo…" virar "Sem ata" sozinho.
  //
  // **Só a etiqueta muda, e a lista não se refiltra.** Refiltrar a cada evento
  // de andamento recriaria as linhas, e quem estivesse com o Tab numa delas
  // perderia o lugar. Uma reunião que deixou de casar com o filtro de estado
  // sai no próximo filtro, não no meio da leitura.
  const cancelar = assinarTranscricoes(() => {
    if (!raiz.isConnected) { cancelar(); return; }
    for (const g of gravacoes) {
      const fim = ultimoResultado(g.caminho);
      if (!emCurso(g.caminho) && fim && !fim.erro && !fim.cancelada) {
        if (fim.tarefa === "ata") { g.tem_ata = true; g.ata_velha = false; }
        else g.transcrita = true;
      }
      const l = linhas.get(g.caminho);
      if (l && l.lastElementChild.textContent !== estadoDe(g, emCurso(g.caminho)).rotulo)
        l.lastElementChild.replaceWith(etiqueta(g));
    }
  });
}

function etiqueta(g) {
  const e = estadoDe(g, emCurso(g.caminho));
  const s = document.createElement("span");
  s.className = TOM[e.tom];
  s.dataset.estado = e.chave;
  s.textContent = e.rotulo;
  return s;
}

/** "Algar › Agentes · 31min 00s · 3 pendências · 1 aviso". */
function metaDe(g) {
  const partes = [g.cliente || g.projeto
    ? [g.cliente, g.projeto].filter(Boolean).join(" › ") : "sem cliente"];
  partes.push(duracao(g.duracao_s));
  if (g.tem_ata && g.pendencias > 0)
    partes.push(g.pendencias === 1 ? "1 pendência" : `${g.pendencias} pendências`);
  if (g.avisos.length > 0)
    partes.push(g.avisos.length === 1 ? "1 aviso" : `${g.avisos.length} avisos`);
  return partes.join(" · ");
}

/** Um seletor sem rótulo visível: o texto de cada opção diz o critério inteiro. */
function seletor(rotulo, id, opcoes, valor) {
  const s = document.createElement("select");
  s.id = id;
  s.className = "aa-entrada reunioes__filtro";
  s.setAttribute("aria-label", rotulo);
  for (const [v, texto] of opcoes) {
    const o = document.createElement("option");
    o.value = v;
    o.textContent = texto;
    s.appendChild(o);
  }
  s.value = opcoes.some(([v]) => v === valor) ? valor : opcoes[0][0];
  return s;
}
