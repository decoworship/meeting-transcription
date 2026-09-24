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
import { duracao, quando, tituloDe, abrirGravacao, abrirGravador, acoesDaBarra } from "/app.js";
import { agruparPorDia, clientesDe, colunasDaSemana, diaDe, diasComGravacao, estadoDe, filtrar, horaDe,
         hojeLocal, intervaloDoPeriodo, proximoPasso, rotuloDaSemana, rotuloDoFiltroDeData,
         rotuloLongoDoDia, segundaDe, somarDias, ESTADOS, PERIODOS, SEM_CLIENTE } from "/reunioes-regras.js";
import { popover } from "/popover.js";
import { calendario } from "/calendario.js";


/**
 * Os critérios sobrevivem a abrir uma reunião e voltar.
 *
 * No módulo, e não no disco: são o "onde eu estava", não uma preferência. Quem
 * filtrou por Algar, abriu uma reunião e clicou em ← Reuniões espera a lista
 * como a deixou.
 */
const criterios = { texto: "", cliente: "", periodo: "tudo", data: "", estado: "", modo: "dia" };

/** A gravação no painel, pelo caminho. Sobrevive a voltar, como os critérios. */
let escolhida = null;

/**
 * Lista ou semana, e a semana na tela (a segunda-feira dela). Sobrevive a
 * voltar, como os critérios: quem abriu uma reunião da semana passada volta à
 * semana passada.
 *
 * **`segunda: null` é "a semana de hoje"**, resolvida a cada desenho pelo
 * relógio (semanaNaTela), e não uma data guardada. A bandeja fica aberta dias
 * a fio — fechar a janela só esconde —, e uma segunda-feira guardada no
 * domingo deixava o Hoje da segunda de manhã na semana passada.
 */
const vista = { modo: "lista", segunda: null };

const semanaNaTela = () => vista.segunda ?? segundaDe(hojeLocal());

/**
 * Abaixo disto o painel não cabe, o CSS o esconde, e o clique na linha volta
 * a abrir a reunião direto. O número é o mesmo do @media do app.css.
 *
 * **900, e não 1100.** A janela abre com 1200 px físicos (JanelaDoApp.cs), e a
 * 125% de escala — a de notebook — isso dá ~945 px de CSS: com o corte em 1100
 * a tela nascia sem o painel justamente onde ela mais é usada.
 */
const ESTREITA = window.matchMedia("(max-width: 899px)");


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
  // Voltando de Reuniões para Reuniões o título não muda, e a barra não se
  // esvazia sozinha: o que estava lá é da montagem anterior.
  acoesDaBarra();
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
  busca.placeholder = "Buscar por título, cliente, projeto, convidado, data ou estado…";
  busca.setAttribute("aria-label", "Buscar reuniões");
  busca.value = criterios.texto;

  const filtroCliente = seletor("Cliente", "filtro-cliente", [
    ["", "Todos os clientes"],
    ...clientesDe(gravacoes).map((c) => [c.nome, `${c.nome} (${c.n})`]),
    [SEM_CLIENTE, "Sem cliente"],
  ], criterios.cliente);
  // O filtro de data: um botão que diz o critério e abre os atalhos em cima do
  // calendário. Foi um seletor com "Um dia…" mais o campo de data do navegador
  // (plano 1), e o nativo não marca os dias que têm gravação.
  const botaoData = document.createElement("button");
  botaoData.type = "button";
  botaoData.id = "filtro-data";
  botaoData.className = "aa-entrada reunioes__filtro reunioes__data";
  const limparData = document.createElement("button");
  limparData.type = "button";
  limparData.id = "filtro-data-limpar";
  limparData.className = "aa-btn aa-btn-texto reunioes__limpar-data";
  limparData.setAttribute("aria-label", "Tirar o filtro de data");
  limparData.textContent = "✕";
  limparData.addEventListener("click", () => { escolherData("tudo"); botaoData.focus(); });
  const comGravacao = diasComGravacao(gravacoes);
  const { ancora: filtroData } = popover(botaoData, "Filtrar por data", painelDeData);

  function pintarData() {
    botaoData.textContent = rotuloDoFiltroDeData(criterios.periodo, criterios.data, hoje);
    const filtra = intervaloDoPeriodo(criterios.periodo, criterios.data, hoje) !== null;
    botaoData.classList.toggle("reunioes__data--ligada", filtra);
    limparData.hidden = !filtra;
  }
  pintarData();

  function escolherData(periodo, data = "") {
    criterios.periodo = periodo;
    criterios.data = data;
    pintarData();
    aoMudar();
  }

  function painelDeData(fechar) {
    const raiz = document.createElement("div");
    raiz.className = "filtro-data";

    const atalhos = document.createElement("div");
    atalhos.className = "filtro-data__atalhos";
    atalhos.setAttribute("role", "group");
    atalhos.setAttribute("aria-label", "Atalhos");
    for (const [valor, rotulo] of PERIODOS.filter(([v]) => v !== "dia" && v !== "semana")) {
      atalhos.appendChild(atalho(rotulo, criterios.periodo === valor, () => {
        escolherData(valor);
        fechar();
      }));
    }

    // Um dia ou a semana dele: a mesma grade escolhe os dois, e o modo fica
    // lembrado junto com os outros critérios.
    const modo = document.createElement("div");
    modo.className = "filtro-data__modo";
    const legenda = document.createElement("span");
    legenda.id = "filtro-data-modo";
    legenda.textContent = "No calendário, escolher";
    const opcoes = document.createElement("div");
    opcoes.className = "filtro-data__opcoes";
    opcoes.setAttribute("role", "group");
    opcoes.setAttribute("aria-labelledby", legenda.id);
    const botoesDoModo = [["dia", "um dia"], ["semana", "uma semana"]].map(([valor, rotulo]) => {
      const b = atalho(rotulo, criterios.modo === valor, () => {
        criterios.modo = valor;
        for (const x of botoesDoModo) x.setAttribute("aria-pressed", String(x.dataset.modo === valor));
      });
      b.dataset.modo = valor;
      return b;
    });
    opcoes.append(...botoesDoModo);
    modo.append(legenda, opcoes);

    const cal = calendario({
      hoje,
      foco: criterios.data || hoje,
      marcados: comGravacao,
      faixa: intervaloDoPeriodo(criterios.periodo, criterios.data, hoje),
      aoEscolher: (dia) => { escolherData(criterios.modo, dia); fechar(); },
    });

    const dica = document.createElement("p");
    dica.className = "campo__dica";
    dica.textContent = "A semana vai de segunda a domingo. O ponto marca os dias com gravação.";

    raiz.append(atalhos, modo, cal.raiz, dica);
    return { raiz, focar: cal.focar };
  }

  const filtroEstado = seletor("Estado", "filtro-estado", ESTADOS, criterios.estado);

  // Um cliente que sumiu desde a última visita não pode continuar filtrando em
  // silêncio: o seletor caiu em "Todos", e o critério cai junto.
  criterios.cliente = filtroCliente.value;

  ferramentas.append(busca, filtroCliente, filtroData, limparData, filtroEstado);

  const lista = document.createElement("section");
  lista.className = "reunioes__lista";
  lista.setAttribute("aria-label", "Gravações");

  const painel = document.createElement("aside");
  painel.className = "reunioes__painel";
  painel.setAttribute("aria-label", "Reunião escolhida");

  const corpo = document.createElement("div");
  corpo.className = "reunioes__corpo";
  corpo.append(lista, painel);

  // A semana: colunas de cartões, e não grade de horas — com cinco reuniões num
  // dia a grade cortava os títulos (spec §2, "Calendário").
  const grade = document.createElement("div");
  grade.className = "semana";
  grade.setAttribute("aria-label", "A semana");

  raiz.append(ferramentas, corpo, grade);
  tela.appendChild(raiz);

  // ---- Lista ou semana, e a navegação da semana, na barra do topo (mockup,
  // prancha "Reuniões · semana"). Montadas uma vez: trocar de semana só repinta
  // o rótulo, e o foco fica no botão que se clicou — quem volta três semanas
  // clica três vezes no mesmo ‹.
  const vistas = document.createElement("div");
  vistas.className = "vistas";
  vistas.setAttribute("role", "group");
  vistas.setAttribute("aria-label", "Visualização");
  const botoesDaVista = [["lista", "Lista"], ["semana", "Semana"]].map(([valor, rotulo]) => {
    const b = atalho(rotulo, vista.modo === valor, () => trocarDeVista(valor));
    b.dataset.vista = valor;
    return b;
  });
  vistas.append(...botoesDaVista);

  const nav = document.createElement("div");
  nav.className = "semana-nav";
  const antes = botao("‹", "aa-btn aa-btn-secundario semana-nav__seta",
                      () => irParaSemana(somarDias(semanaNaTela(), -7)));
  antes.setAttribute("aria-label", "Semana anterior");
  const escolherSemana = document.createElement("button");
  escolherSemana.type = "button";
  escolherSemana.className = "aa-btn aa-btn-secundario semana-nav__escolher";
  const { ancora: calendarioDaSemana } = popover(escolherSemana, "Escolher a semana", (fechar) => {
    const caixa = document.createElement("div");
    caixa.className = "filtro-data";
    const agora = hojeLocal();
    const cal = calendario({
      hoje: agora,
      foco: vista.segunda ?? agora,
      marcados: comGravacao,
      faixa: [semanaNaTela(), somarDias(semanaNaTela(), 6)],
      aoEscolher: (dia) => { irParaSemana(segundaDe(dia)); fechar(); },
    });
    const dica = document.createElement("p");
    dica.className = "campo__dica";
    dica.textContent = "Escolha qualquer dia: a semana dele aparece, de segunda a domingo.";
    caixa.append(cal.raiz, dica);
    return { raiz: caixa, focar: cal.focar };
  });
  const depois = botao("›", "aa-btn aa-btn-secundario semana-nav__seta",
                       () => irParaSemana(somarDias(semanaNaTela(), 7)));
  depois.setAttribute("aria-label", "Próxima semana");
  const irHoje = botao("Hoje", "aa-btn aa-btn-secundario semana-nav__hoje", () => irParaSemana(null));
  nav.append(antes, calendarioDaSemana, depois, irHoje);
  acoesDaBarra(nav, vistas);

  function pintarVista() {
    const semana = vista.modo === "semana";
    for (const b of botoesDaVista) b.setAttribute("aria-pressed", String(b.dataset.vista === vista.modo));
    nav.hidden = !semana;
    corpo.hidden = semana;
    grade.hidden = !semana;
    // Na semana, quem escolhe a data é a navegação dela.
    filtroData.hidden = semana;
    if (semana) limparData.hidden = true;
    else pintarData();
    escolherSemana.textContent = rotuloDaSemana(semanaNaTela(), hojeLocal());
    irHoje.classList.toggle("semana-nav__hoje--agora", vista.segunda === null);
  }

  function trocarDeVista(modo) {
    vista.modo = modo;
    pintarVista();
    aoMudar();
  }

  /** `null` é a semana de hoje; chegar nela por ‹ › ou pelo calendário também. */
  function irParaSemana(segunda) {
    vista.segunda = segunda === segundaDe(hojeLocal()) ? null : segunda;
    pintarVista();
    aoMudar();
  }

  /** caminho → linha, para trocar a etiqueta sem redesenhar a lista. */
  const linhas = new Map();
  let visiveis = [];

  function desenhar() {
    if (vista.modo === "semana") return desenharSemana();
    visiveis = filtrar(gravacoes, criterios, { hoje, rodandoDe: emCurso });
    cabecalho("Reuniões", visiveis.length === gravacoes.length
      ? contagem(gravacoes.length)
      : `${visiveis.length} de ${contagem(gravacoes.length)}`, false);

    linhas.clear();
    lista.replaceChildren();
    if (visiveis.length === 0) {
      lista.appendChild(semResultado());
      desenharPainel(null);
      return;
    }

    const grupos = agruparPorDia(visiveis, hoje);
    for (const grupo of grupos) {
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

    // A escolhida que o filtro escondeu cede o lugar à primeira que sobrou:
    // um painel falando de uma reunião que não está na lista confunde.
    // **A primeira da tela, e não a primeira que o núcleo mandou**: o "Sem
    // data" chega na frente e é desenhado no fim.
    if (!visiveis.some((g) => g.caminho === escolhida)) escolhida = grupos[0].itens[0].caminho;
    marcarEscolhida();
  }


  /** A semana na tela, com os filtros de agora — menos o de data, que é ela. */
  function desenharSemana() {
    // O relógio de agora, e não o da montagem: é o que decide a coluna de hoje.
    const hoje = hojeLocal();
    const segunda = semanaNaTela();
    visiveis = filtrar(gravacoes, { ...criterios, periodo: "semana", data: segunda },
                       { hoje, rodandoDe: emCurso });
    // A semana sem os outros filtros, para dizer o que eles esconderam. Os
    // critérios ficam na memória: um cliente deixado ontem na lista continua
    // valendo aqui, e "Nenhuma gravação" num dia que teve três seria mentira.
    const daSemana = filtrar(gravacoes, { periodo: "semana", data: segunda }, { hoje });
    const escondidos = new Set(daSemana.map((g) => diaDe(g.nome)));
    cabecalho("Reuniões", `semana de ${rotuloDaSemana(segunda, hoje)} · ${
      visiveis.length === daSemana.length
        ? contagem(daSemana.length) : `${visiveis.length} de ${contagem(daSemana.length)}`}`, false);
    linhas.clear();
    grade.replaceChildren();
    if (visiveis.length === 0 && daSemana.length > 0) {
      const nada = semResultado();
      nada.classList.add("semana__sem-resultado");
      grade.appendChild(nada);
    }
    const colunas = colunasDaSemana(visiveis, segunda, hoje, gravacoes);
    grade.classList.toggle("semana--sete", colunas.length === 7);
    for (const coluna of colunas) {
      const dia = document.createElement("section");
      dia.className = "semana__dia" + (coluna.hoje ? " semana__dia--hoje" : "");
      dia.dataset.dia = coluna.dia;
      const cabeca = document.createElement("h2");
      cabeca.className = "semana__rotulo";
      cabeca.id = `semana-${coluna.dia}`;
      // "Seg 21" é para o olho; o leitor de tela ouve o dia inteiro.
      cabeca.setAttribute("aria-label", rotuloLongoDoDia(coluna.dia) + (coluna.hoje ? ", hoje" : ""));
      cabeca.textContent = coluna.rotulo;
      if (coluna.hoje) {
        const marca = document.createElement("span");
        marca.className = "aa-etiqueta aa-etiqueta--info";
        marca.textContent = "hoje";
        cabeca.appendChild(marca);
      }
      dia.setAttribute("aria-labelledby", cabeca.id);
      dia.appendChild(cabeca);
      for (const g of coluna.itens) {
        const c = cartao(g);
        linhas.set(g.caminho, c);
        dia.appendChild(c);
      }
      if (coluna.itens.length === 0) {
        dia.appendChild(texto("semana__vazio", escondidos.has(coluna.dia)
          ? "Nenhuma com esses filtros" : "Nenhuma gravação"));
      }
      grade.appendChild(dia);
    }
  }

  function cartao(g) {
    const b = document.createElement("button");
    b.type = "button";
    b.className = "semana__cartao";
    b.dataset.gravacao = g.caminho;
    preencherCartao(b, g);
    // Um clique abre: a semana não tem painel ao lado para escolher antes.
    b.addEventListener("click", () => abrirGravacao(g));
    return b;
  }

  /** Como preencherLinha: repinta sem trocar o botão, e a etiqueta vem por último. */
  function preencherCartao(b, g) {
    b.title = g.avisos.join("\n");
    // <span>, e não o texto() de <p>: dentro de botão só cabe conteúdo de frase.
    const pedaco = (classe, conteudo) => {
      const e = document.createElement("span");
      e.className = classe;
      e.textContent = conteudo;
      return e;
    };
    const partes = [pedaco("semana__hora", horaDe(g.nome)), pedaco("semana__titulo", tituloDe(g)),
                    pedaco("semana__vinculo", g.cliente || g.projeto
                      ? [g.cliente, g.projeto].filter(Boolean).join(" › ") : "sem cliente")];
    if (g.avisos.length > 0) partes.push(pedaco("semana__aviso", g.avisos[0]));
    b.replaceChildren(...partes, etiqueta(g));
  }

  function linha(g) {
    const b = document.createElement("button");
    b.type = "button";
    b.className = "reuniao-linha";
    b.dataset.gravacao = g.caminho;
    // Uma parada de Tab só para a lista inteira: a escolhida fica com 0 e as
    // setas andam entre as linhas (marcarEscolhida, aoTeclar). Com uma parada
    // por linha, chegar ao painel custava um Tab por gravação — setenta.
    b.tabIndex = -1;
    preencherLinha(b, g);
    b.setAttribute("aria-pressed", "false");
    // Escolher custa um clique a mais para abrir, e o duplo clique o devolve.
    // Sem largura para o painel, escolher não mostraria nada — o clique abre.
    b.addEventListener("click", () => (ESTREITA.matches ? abrirGravacao(g) : escolher(g)));
    b.addEventListener("dblclick", () => abrirGravacao(g));
    b.addEventListener("keydown", (e) => aoTeclar(e, g));
    return b;
  }

  /**
   * O conteúdo da linha, à parte do botão: o fim de uma tarefa repinta a linha
   * sem trocar o botão — e quem estava com o foco nele continua.
   */
  function preencherLinha(b, g) {
    b.title = g.avisos.join("\n");

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

    // O aviso escrito na linha, e não só no title: sem o painel (janela
    // estreita) a linha é o único lugar que o diz, e o teclado não vê title.
    if (g.avisos.length > 0) {
      const aviso = document.createElement("span");
      aviso.className = "reuniao-linha__aviso";
      aviso.textContent = g.avisos.length === 1
        ? g.avisos[0] : `${g.avisos[0]} (+${g.avisos.length - 1})`;
      textos.appendChild(aviso);
    }

    b.replaceChildren(hora, textos, etiqueta(g));
  }

  /**
   * O teclado na lista: as setas e Home/End andam e escolhem, Enter abre.
   *
   * Enter abre porque o teclado não tem duplo clique: sem isto, abrir uma
   * reunião pelo teclado era escolher e depois ir até o painel. Espaço segue
   * sendo o clique do botão — escolhe.
   */
  function aoTeclar(e, g) {
    if (e.key === "Enter") {
      e.preventDefault();
      abrirGravacao(g);
      return;
    }
    const todas = [...linhas.values()];
    const i = todas.indexOf(e.currentTarget);
    const destino = { ArrowDown: i + 1, ArrowUp: i - 1, Home: 0, End: todas.length - 1 }[e.key];
    if (destino === undefined) return;
    e.preventDefault();
    const alvo = todas[Math.max(0, Math.min(todas.length - 1, destino))];
    escolhida = alvo.dataset.gravacao;
    marcarEscolhida();
    alvo.focus();
  }

  function escolher(g) {
    escolhida = g.caminho;
    marcarEscolhida();
  }

  function marcarEscolhida() {
    for (const [caminho, l] of linhas) {
      const esta = caminho === escolhida;
      l.setAttribute("aria-pressed", String(esta));
      l.tabIndex = esta ? 0 : -1;
    }
    desenharPainel(gravacoes.find((g) => g.caminho === escolhida) ?? null);
  }

  /** O que o painel desenhou por último, para não redesenhá-lo à toa. */
  let pintado = "";

  /**
   * O painel da escolhida: o que ela é, o que ela precisa, e o que a ata diz.
   *
   * @param seMudou redesenhar só se o estado mudou. É o caso dos eventos de
   *   andamento, que chegam várias vezes por etapa: redesenhar a cada um
   *   tiraria o foco de quem está com o Tab no botão do painel.
   */
  function desenharPainel(g, { seMudou = false } = {}) {
    const rodando = g ? emCurso(g.caminho) : null;
    const passo = g ? proximoPasso(g, rodando) : null;
    // **A chave é o que o painel desenha, e só isso.** O rótulo da etapa
    // ("Transcrevendo…", "Separando falantes…") muda quatro vezes numa
    // transcrição e não aparece aqui: com ele na chave o painel era recriado a
    // cada etapa, e o foco de quem estava no botão caía no body.
    const chave = g ? JSON.stringify([g.caminho, passo.acao, textoDaAta(g, rodando), g.resumo,
      g.pendencias, g.pendencias_inicio, g.notas_inicio, g.avisos, g.cliente, g.projeto,
      g.titulo, g.convidados]) : "";
    if (seMudou && chave === pintado) return;
    pintado = chave;
    // Quem estava dentro do painel continua nele quando o redesenho é de verdade:
    // o botão principal novo recebe o foco.
    const tinhaFoco = painel.contains(document.activeElement);
    painel.replaceChildren();
    if (!g) return;

    const titulo = document.createElement("h2");
    titulo.className = "reunioes__painel-titulo";
    titulo.textContent = tituloDe(g);

    painel.append(
      titulo,
      // Sem data no nome da pasta, o quando() devolveria o próprio nome.
      texto("reunioes__painel-meta", [horaDe(g.nome) ? quando(g.nome) : null,
        duracao(g.duracao_s),
        g.convidados > 0 ? `${g.convidados} convidados` : null].filter(Boolean).join(" · ")),
      texto("reunioes__painel-vinculo", g.cliente || g.projeto
        ? [g.cliente, g.projeto].filter(Boolean).join(" › ")
        : "Sem cliente — escolha ao transcrever"),
    );
    for (const aviso of g.avisos) painel.appendChild(alerta(aviso));

    const principal = botao(passo.rotulo, "aa-btn aa-btn-primario", () => seguir(g, passo.acao));
    principal.dataset.acao = passo.acao;
    const acoes = document.createElement("div");
    acoes.className = "reunioes__painel-acoes";
    acoes.append(principal, botao("Abrir", "aa-btn aa-btn-secundario", () => abrirGravacao(g)));
    painel.appendChild(acoes);

    painel.appendChild(secaoDoPainel("Ata", textoDaAta(g, rodando)));
    if (g.resumo) painel.appendChild(texto("reunioes__painel-resumo", g.resumo));

    const pendencias = g.pendencias_inicio ?? [];
    if (pendencias.length > 0) {
      const s = secaoDoPainel(g.pendencias === 1 ? "1 pendência" : `${g.pendencias} pendências`);
      const ul = document.createElement("ul");
      ul.className = "reunioes__pendencias";
      for (const p of pendencias) {
        const li = document.createElement("li");
        li.textContent = p;
        ul.appendChild(li);
      }
      s.appendChild(ul);
      painel.appendChild(s);
    }
    if (g.notas_inicio) painel.appendChild(secaoDoPainel("Notas", g.notas_inicio));
    if (tinhaFoco) principal.focus({ preventScroll: true });
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
      pintarData();
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
  filtroEstado.addEventListener("change", () => { criterios.estado = filtroEstado.value; aoMudar(); });

  pintarVista();
  desenhar();

  // A etiqueta acompanha o trabalho da placa: quem fica parado na lista vê
  // "Transcrevendo…" virar "Sem ata" sozinho.
  //
  // **Só a etiqueta muda, e a lista não se refiltra.** Refiltrar a cada evento
  // de andamento recriaria as linhas, e quem estivesse com o Tab numa delas
  // perderia o lugar. Uma reunião que deixou de casar com o filtro de estado
  // sai no próximo filtro, não no meio da leitura.
  //
  // **O fim de uma tarefa relê a gravação no núcleo**, em vez de adivinhar o que
  // ficou no disco: retranscrever uma reunião que tinha ata deixa a ata velha, e
  // uma ata nova traz resumo e pendências — o palpite local errava os dois.
  function marcaDoFim(caminho) {
    const fim = ultimoResultado(caminho);
    return fim && !fim.erro && !fim.cancelada && !emCurso(caminho)
      ? `${caminho}|${fim.tarefa}|${fim.comecou_em}` : null;
  }

  // Semeado com os fins que já existiam: a lista acabou de ler o núcleo.
  const tratados = new Set(gravacoes.map((g) => marcaDoFim(g.caminho)).filter(Boolean));

  async function reler(caminho) {
    try {
      const { gravacoes: novas } = await pedir("gravacoes");
      if (!raiz.isConnected) return;
      const nova = novas.find((x) => x.caminho === caminho);
      const g = gravacoes.find((x) => x.caminho === caminho);
      if (!nova || !g) return;
      Object.assign(g, nova);
      // Só a linha dela, e sem trocar o botão: quem estava nela continua.
      const l = linhas.get(caminho);
      if (l) (l.classList.contains("semana__cartao") ? preencherCartao : preencherLinha)(l, g);
      desenharPainel(gravacoes.find((x) => x.caminho === escolhida) ?? null, { seMudou: true });
    } catch {
      // Sem resposta, a linha fica como estava; a próxima visita à tela relê tudo.
    }
  }

  const cancelar = assinarTranscricoes(() => {
    if (!raiz.isConnected) { cancelar(); return; }
    for (const g of gravacoes) {
      const marca = marcaDoFim(g.caminho);
      if (marca && !tratados.has(marca)) {
        tratados.add(marca);
        // Adiantado enquanto o núcleo responde: que a transcrição terminou é
        // fato. O resto — ata velha, pendências — vem da releitura.
        if (ultimoResultado(g.caminho).tarefa !== "ata") g.transcrita = true;
        reler(g.caminho);
      }
      const l = linhas.get(g.caminho);
      if (l && l.lastElementChild.textContent !== estadoDe(g, emCurso(g.caminho)).rotulo)
        l.lastElementChild.replaceWith(etiqueta(g));
    }
    desenharPainel(gravacoes.find((x) => x.caminho === escolhida) ?? null, { seMudou: true });

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

/** "Algar › Agentes · 31min 00s · 3 pendências". Os avisos têm linha própria. */
function metaDe(g) {
  const partes = [g.cliente || g.projeto
    ? [g.cliente, g.projeto].filter(Boolean).join(" › ") : "sem cliente"];
  partes.push(duracao(g.duracao_s));
  if (g.tem_ata && g.pendencias > 0)
    partes.push(g.pendencias === 1 ? "1 pendência" : `${g.pendencias} pendências`);
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

/** Um botão de ligar e desligar, dos atalhos e do modo do filtro de data. */
function atalho(rotulo, ligado, aoClicar) {
  const b = document.createElement("button");
  b.type = "button";
  b.className = "atalho";
  b.textContent = rotulo;
  b.setAttribute("aria-pressed", String(ligado));
  b.addEventListener("click", aoClicar);
  return b;
}

function texto(classe, conteudo) {
  const p = document.createElement("p");
  if (classe) p.className = classe;
  p.textContent = conteudo;
  return p;
}

function botao(rotulo, classe, aoClicar) {
  const b = document.createElement("button");
  b.type = "button";
  b.className = classe;
  b.textContent = rotulo;
  b.addEventListener("click", aoClicar);
  return b;
}

function secaoDoPainel(rotulo, conteudo = null) {
  const s = document.createElement("section");
  s.className = "reunioes__painel-secao";
  const h = document.createElement("h3");
  h.className = "reunioes__rotulo";
  h.textContent = rotulo;
  s.appendChild(h);
  if (conteudo) s.appendChild(texto("", conteudo));
  return s;
}

function textoDaAta(g, rodando) {
  if (rodando?.tarefa === "ata") return "Sendo escrita agora.";
  if (!g.transcrita) return "A ata é escrita a partir da transcrição.";
  if (!g.tem_ata) return "Ainda não foi escrita.";
  if (g.ata_velha) return "A transcrição foi corrigida depois que esta ata foi escrita.";
  return "Pronta.";
}

/**
 * O botão do próximo passo leva aonde o passo se dá: transcrever na tela de
 * transcrever, e tudo o que é da ata na aba Ata da reunião.
 */
function seguir(g, acao) {
  if (acao === "transcrever" || acao === "acompanhar-transcricao") return abrirGravacao(g);
  return abrirGravacao(g, { aba: "ata" });
}

