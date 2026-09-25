// A tela do Gravador: o espelho, dentro da janela, do que a bandeja faz.
//
// A bandeja continua sendo o gravador — esta tela é adição, não substituição.
// O que ela pode e a bandeja não, e que justifica existir:
//
//   1. os medidores de nível das duas faixas, ao vivo. É o que teria denunciado
//      o microfone mudo de 06/08 no primeiro minuto, e não 36 minutos depois;
//   2. a reunião da agenda que está sendo gravada, com os convidados já
//      reconhecidos — hoje isso só aparece depois, no meta.json;
//   3. as reuniões de hoje, com a marca de qual delas rotularia a gravação se
//      ela começasse agora — e o botão de escolher outra. A escolha automática
//      acerta o caso comum e não tem como acertar o ambíguo (duas reuniões
//      sobrepostas, ou a que começa em vinte minutos), e o rótulo errado só
//      aparece depois, na ata.
//
// **Dois estados, uma tela** (plano 3 do redesenho, spec §3.2): antes da
// reunião, a próxima da agenda como herói e a última gravação com o estado dela;
// gravando, a faixa em cima, a legenda larga à esquerda e Notas · Perguntar em
// abas à direita (D-B). Os dois estados estão sempre no DOM, e quem troca é o
// `data-gravando` da raiz — trocar de estado não reconstrói nada.
//
// **O que o app tinha e a prancha não mostra desce, e nada sai** (decisão do
// dono, 25/09): a lista de convidados por nome, dispositivos e pasta ficam
// abaixo do que a prancha mostra. Dispositivos e pasta vão para Ajustes num
// plano próprio.
//
// Desenha uma vez e depois só atualiza os nós que mudam. O `aplicar()` roda
// cinco vezes por segundo enquanto grava: redesenhar ali derrubaria o foco de
// quem digita nas notas e faria o texto piscar.

import { pedir, assinar } from "/ponte.js";
import { alerta, campoComSugestoes, icone } from "/pecas.js";
import { blocoDeNotas } from "/notas.js";
import { painelAoVivo } from "/aovivo.js";
import { painelDePerguntas } from "/perguntar.js";
import { popover } from "/popover.js";
import { estadoDe } from "/reunioes-regras.js";
import { assinarTranscricoes, emCurso } from "/transcricoes.js";

/** "00:12:34" — aqui o tempo é para cronometrar, ao contrário da lista. */
function relogio(segundos) {
  const s = Math.max(0, Math.round(segundos));
  const p = (n) => String(n).padStart(2, "0");
  return `${p(Math.floor(s / 3600))}:${p(Math.floor((s % 3600) / 60))}:${p(s % 60)}`;
}

/**
 * O nível em largura de barra.
 *
 * Escala logarítmica, e não o RMS cru: a fala normal fica entre 0,01 e 0,1 de
 * RMS, e uma barra linear passaria a reunião inteira nos primeiros 10% —
 * indistinguível de silêncio, que é justamente o que este medidor existe para
 * distinguir. -60 dB vira 0% e 0 dB vira 100%.
 */
function porcentagem(rms) {
  if (!(rms > 0)) return 0;
  const db = 20 * Math.log10(rms);
  return Math.max(0, Math.min(100, ((db + 60) / 60) * 100));
}

const NOME_DA_FAIXA = { mic: "Seu microfone", system: "Áudio da reunião" };

const hora = (d) => d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", hourCycle: "h23" });

/** "14:30 – 15:00", com o dia na frente quando não é hoje. */
function quando(inicio, fim) {
  if (!inicio) return "";
  const i = new Date(inicio);
  let texto = hora(i);
  if (fim) texto += ` – ${hora(new Date(fim))}`;

  // O horizonte é de doze horas para a frente e três para trás, então no fim da
  // tarde a lista já mostra o dia seguinte — e "09:00" sem o dia seria a reunião
  // de amanhã parecendo a de daqui a pouco.
  if (i.toDateString() !== new Date().toDateString())
    texto = `${i.toLocaleDateString([], { weekday: "short", day: "2-digit", month: "2-digit" })}, ${texto}`;
  return texto;
}

/** "começa em 5 min", "começou há 12 min", "começa às 16:00". */
function quandoComeca(inicio, fim) {
  if (!inicio) return "";
  const min = Math.round((new Date(inicio) - Date.now()) / 60000);
  if (fim && new Date(fim) < new Date()) return "já terminou";
  if (min < 0) return `começou há ${-min} min`;
  if (min === 0) return "começa agora";
  if (min < 60) return `começa em ${min} min`;
  return `começa às ${hora(new Date(inicio))}`;
}

/** O que dizer quando a lista não vem, por status da agenda. */
const SEM_LISTA = {
  nao_configurado: "Este app foi instalado sem a credencial do Google, então não há agenda para ler.",
  nao_autorizado: "Conecte sua conta do Google em Ajustes › Gravador para ver as reuniões.",
  token_expirado: "A autorização do calendário expirou. Reconecte em Ajustes › Gravador.",
  sem_evento: "Nenhuma reunião nas últimas 3 nem nas próximas 12 horas.",
};

const rotuloDoVinculo = (v) =>
  v?.cliente ? [v.cliente, v.projeto].filter(Boolean).join(" › ") : "sem cliente";

const el = (tag, classe, texto) => {
  const e = document.createElement(tag);
  if (classe) e.className = classe;
  if (texto !== undefined) e.textContent = texto;
  return e;
};

const botao = (classe, texto) => {
  const b = el("button", `aa-btn ${classe}`, texto);
  b.type = "button";
  return b;
};

/** Um medidor por faixa: rótulo e barra; o dispositivo vai no `title`. */
function medidor(nome) {
  const raiz = el("div", "faixa");
  raiz.dataset.faixa = nome;
  const rotulo = el("p", "faixa__nome", NOME_DA_FAIXA[nome] ?? nome);
  const trilho = el("div", "medidor");
  const preenchimento = el("div");
  trilho.appendChild(preenchimento);
  raiz.append(rotulo, trilho);
  return { raiz, preenchimento, trilho, rotulo };
}

/**
 * Monta a tela.
 *
 * @param ctx `{ cabecalho, tela, acoesDaBarra, abrirGravacao, abrirAjustes }` —
 *   a moldura é do app.js, e a tela recebe o que precisa dela.
 */
export async function telaDoGravador(ctx) {
  const { cabecalho, tela } = ctx;
  cabecalho("Gravador", "", false);
  tela.setAttribute("aria-busy", "true");
  tela.replaceChildren();

  let estado, dispositivos, gravacoes;
  try {
    [{ gravador: estado }, { dispositivos }, { gravacoes }] = await Promise.all([
      pedir("gravador"), pedir("dispositivos"),
      pedir("gravacoes").catch(() => ({ gravacoes: [] })),
    ]);
  } catch (e) {
    tela.setAttribute("aria-busy", "false");
    tela.replaceChildren(alerta(e.message, "erro"));
    return;
  }
  tela.setAttribute("aria-busy", "false");

  const raiz = el("div", "gravador-tela");

  // ════════════════════════════════════════════════ antes da reunião

  const antes = el("section", "grav-antes");
  antes.setAttribute("aria-label", "Antes da reunião");

  // ---- o herói: a próxima da agenda
  const heroi = el("div", "bloco grav-heroi");
  const heroiTexto = el("div", "grav-heroi__texto");
  const heroiRotulo = el("p", "grav-rotulo grav-heroi__rotulo");
  const heroiTitulo = el("h2", "grav-heroi__titulo");
  const heroiFatos = el("p", "grav-heroi__fatos");
  const heroiVinculo = el("div", "grav-heroi__vinculo");
  const heroiDica = el("span", "grav-heroi__dica");
  const heroiAcoes = el("div", "grav-heroi__acoes");
  const gravarEsta = botao("aa-btn-primario aa-btn--grande grav-gravar", "Gravar esta reunião");
  const gravarSem = botao("aa-btn-secundario aa-btn--grande", "Gravar sem reunião da agenda");
  heroiAcoes.append(gravarEsta, gravarSem);
  heroiTexto.append(heroiRotulo, heroiTitulo, heroiFatos, heroiVinculo, heroiAcoes);

  // ---- o que vai ser gravado
  const vai = el("aside", "grav-vai");
  vai.append(el("p", "grav-rotulo", "Vai gravar"));
  const itemVai = (icone, nome) => {
    const item = el("div", "grav-vai__item");
    item.dataset.icone = icone;
    const valor = el("span", "grav-vai__valor");
    item.append(el("span", "grav-vai__nome", nome), valor);
    vai.appendChild(item);
    return valor;
  };
  const vaiMic = itemVai("mic", "Seu microfone");
  const vaiAudio = itemVai("audio", "Áudio da reunião");
  const vaiAgenda = itemVai("agenda", "Agenda");
  const trocar = el("a", "grav-vai__trocar", "Trocar em Ajustes › Gravação");
  trocar.href = "#config=gravador";
  trocar.addEventListener("click", (e) => { e.preventDefault(); ctx.abrirAjustes?.("gravador"); });
  vai.appendChild(trocar);
  heroi.append(heroiTexto, vai);

  // ---- hoje na agenda
  const proximas = el("div", "bloco grav-agenda");
  const topoProximas = el("div", "bloco__topo");
  const atualizar = botao("aa-btn-texto aa-btn--pequeno", "Atualizar");
  topoProximas.append(el("h2", "bloco__titulo", "Hoje na agenda"), atualizar);
  const listaProximas = el("div", "grav-agenda__lista");
  const dicaProximas = el("p", "grav-agenda__dica",
    "A próxima dá nome à gravação. Reunião que já terminou fica aqui por três horas — "
    + "reunião atrasada termina no papel antes de começar de verdade.");
  proximas.append(topoProximas, listaProximas, dicaProximas);

  // ---- a última gravação
  const ultima = el("div", "bloco grav-ultima");
  ultima.append(el("p", "grav-rotulo", "Última gravação"));
  const ultimaTitulo = el("p", "grav-ultima__titulo");
  const ultimaFatos = el("p", "grav-ultima__fatos");
  const ultimaEstado = el("div", "grav-ultima__estado");
  const ultimaRotulo = el("span", "grav-ultima__rotulo");
  const ultimaPct = el("span", "grav-ultima__pct");
  ultimaEstado.append(ultimaRotulo, ultimaPct);
  const ultimaBarra = el("div", "aa-progresso grav-ultima__barra");
  const ultimaPreench = el("div", "aa-progresso__barra");
  ultimaBarra.appendChild(ultimaPreench);
  const ultimaAbrir = botao("aa-btn-secundario", "Abrir reunião");
  ultima.append(ultimaTitulo, ultimaFatos, ultimaEstado, ultimaBarra, ultimaAbrir);

  const baixo = el("div", "grav-antes__baixo");
  baixo.append(proximas, ultima);
  antes.append(heroi, baixo);

  // ════════════════════════════════════════════════════════ gravando

  const gravando = el("section", "grav-gravando");
  gravando.setAttribute("aria-label", "Gravando");

  // ---- a faixa
  const faixa = el("div", "bloco grav-faixa");
  const ponto = el("span", "gravador__ponto");
  const tempo = el("p", "grav-faixa__tempo");
  const info = el("div", "grav-faixa__info");
  const faixaTitulo = el("p", "grav-faixa__titulo");
  const faixaLinha = el("div", "grav-faixa__linha");
  const faixaConvidados = el("span", "grav-faixa__convidados");
  info.append(faixaTitulo, faixaLinha);
  const faixas = { mic: medidor("mic"), system: medidor("system") };
  const medidores = el("div", "grav-faixa__medidores");
  medidores.append(faixas.mic.raiz, faixas.system.raiz);
  const acoes = el("div", "grav-faixa__acoes");
  const marcar = botao("aa-btn-primario grav-marcar", "Marcar momento");
  marcar.prepend(icone("i-marca"));
  const mutar = botao("aa-btn-secundario", "Mutar");
  const parar = botao("aa-btn-secundario grav-parar", "Parar");
  acoes.append(marcar, mutar, parar);
  faixa.append(ponto, tempo, info, medidores, acoes);

  // O aviso de mudo e as falhas de dispositivo: logo abaixo da faixa, que é
  // para onde se olha.
  const avisos = el("div", "grav-avisos");
  avisos.setAttribute("role", "status");

  // ---- a legenda e as abas
  const grade = el("div", "grav-grade");
  const previa = painelAoVivo({ tempo: () => estado.duracao_s, aoSaber: aoSaberDaLegenda });

  const semLegenda = el("div", "bloco grav-sem-legenda");
  semLegenda.hidden = true;
  const semLegendaMotivo = el("p", "bloco__texto");
  const semLegendaIr = botao("aa-btn-secundario aa-btn--pequeno", "Abrir Ajustes › Transcrição");
  semLegendaIr.addEventListener("click", () => ctx.abrirAjustes?.("transcricao"));
  semLegenda.append(
    el("h2", "bloco__titulo", "Sem legenda nesta gravação"),
    el("p", "bloco__texto",
      "Ligada, a legenda mostra o que está sendo dito enquanto a reunião acontece — "
      + "Você e Outros, com o tempo de cada fala — e dá para perguntar sobre a reunião "
      + "no meio dela. O custo: a placa de vídeo fica ocupada a reunião inteira, e o "
      + "texto é rascunho; a transcrição do fim continua sendo a que vale."),
    semLegendaMotivo, semLegendaIr);

  const abas = el("section", "bloco grav-abas");
  const listaAbas = el("div", "grav-abas__lista");
  listaAbas.setAttribute("role", "tablist");
  listaAbas.setAttribute("aria-label", "Notas e perguntas");
  abas.appendChild(listaAbas);

  // As notas seguem a gravação: começar uma aponta o bloco para a pasta dela.
  // O tempo vem do estado que já chega cinco vezes por segundo — é ele que o
  // "marcar momento" carimba.
  const notas = blocoDeNotas(estado.gravando ? estado.gravacao : null, {
    tempo: () => estado.duracao_s,
    linhas: 14,
  });
  const perguntas = painelDePerguntas();
  // A dica e o "salvo" das notas numa linha só, embaixo, como na prancha.
  const rodapeNotas = el("div", "grav-abas__dica");
  rodapeNotas.append(el("span", "", "Marcar momento põe o tempo onde está o cursor."),
                     notas.raiz.querySelector(".notas__estado"));

  const paineis = {};
  const botoesAba = {};
  for (const [id, rotulo, conteudo] of [
    ["notas", "Notas", [notas.raiz, rodapeNotas]],
    ["perguntar", "Perguntar", [perguntas.raiz]],
  ]) {
    const b = el("button", "grav-abas__aba", rotulo);
    b.type = "button";
    b.id = `grav-aba-${id}`;
    b.setAttribute("role", "tab");
    b.setAttribute("aria-controls", `grav-painel-${id}`);
    b.addEventListener("click", () => mostrarAba(id));
    b.addEventListener("keydown", (e) => {
      if (e.key !== "ArrowRight" && e.key !== "ArrowLeft") return;
      e.preventDefault();
      const outra = id === "notas" ? "perguntar" : "notas";
      mostrarAba(outra);
      botoesAba[outra].focus();
    });
    listaAbas.appendChild(b);
    const p = el("div", "grav-abas__painel");
    p.id = `grav-painel-${id}`;
    p.setAttribute("role", "tabpanel");
    p.setAttribute("aria-labelledby", b.id);
    p.append(...conteudo);
    abas.appendChild(p);
    paineis[id] = p;
    botoesAba[id] = b;
  }
  let abaAtiva = "notas";
  function mostrarAba(id) {
    abaAtiva = id;
    for (const k of Object.keys(paineis)) {
      paineis[k].hidden = k !== id;
      botoesAba[k].setAttribute("aria-selected", String(k === id));
      botoesAba[k].tabIndex = k === id ? 0 : -1;
    }
  }
  mostrarAba("notas");
  // A caixa de perguntar nasce escondida e só aparece se o núcleo deixar; sem
  // ela, a aba não tem o que mostrar.
  new MutationObserver(() => {
    botoesAba.perguntar.hidden = perguntas.raiz.hidden;
    if (perguntas.raiz.hidden && abaAtiva === "perguntar") mostrarAba("notas");
  }).observe(perguntas.raiz, { attributes: true, attributeFilter: ["hidden"] });
  botoesAba.perguntar.hidden = perguntas.raiz.hidden;

  grade.append(previa.raiz, semLegenda, abas);
  gravando.append(faixa, avisos, grade);

  // ════════════════════════════════════ o resto: abaixo do que a prancha mostra

  const resto = el("div", "grav-resto");

  // ---- a reunião da agenda, com os convidados por nome
  const reuniao = el("div", "bloco");
  const tituloReuniao = el("h2", "bloco__titulo");
  const participantes = el("p", "bloco__texto");
  reuniao.append(tituloReuniao, participantes);

  // ---- dispositivos
  const blocoDisp = el("div", "bloco");
  const notaDisp = el("p", "bloco__texto",
    // A trava não é limitação de implementação e sim o que preserva o valor das
    // duas faixas separadas: reabrir o stream no meio exigiria realinhá-las.
    "Não dá para trocar de dispositivo durante uma gravação: as duas faixas "
    + "começam alinhadas por terem começado juntas.");
  const escolhaMic = seletor("Microfone", "mic", dispositivos.entradas, dispositivos.mic_id);
  const escolhaLoop = seletor("Áudio do sistema", "loopback", dispositivos.saidas, dispositivos.loopback_id);
  blocoDisp.append(el("h2", "bloco__titulo", "Dispositivos"), escolhaMic.raiz, escolhaLoop.raiz, notaDisp);

  // ---- pasta
  const blocoPasta = el("div", "bloco");
  const caminho = el("p", "bloco__texto caminho");
  blocoPasta.append(el("h2", "bloco__titulo", "Pasta das gravações"), caminho,
    el("p", "bloco__texto", "Trocar em Ajustes › Geral. É a mesma pasta que a lista de reuniões lê."));

  resto.append(reuniao, blocoDisp, blocoPasta);

  raiz.append(antes, gravando, resto);
  tela.replaceChildren(raiz);

  // ─────────────────────────────────────────────── cliente › projeto

  // O vínculo da gravação corrente, e a sugestão de antes de gravar. A
  // sugestão é memória da página: ela só vai para o disco quando a gravação
  // que ela sugere existe (`aplicarSugestao`).
  let vinculo = null;
  let sugestao = null;
  let sugestaoPendente = false;
  let clientes = {};
  pedir("clientes").then((r) => { clientes = r.clientes ?? {}; }).catch(() => {});

  /** O botão "Cliente › Projeto ⌄" que abre o editor, em dois lugares. */
  function editorDeVinculo(lugar, aoSalvar) {
    const gatilho = el("button", "grav-vinculo");
    gatilho.type = "button";
    gatilho.dataset.lugar = lugar;
    const texto = el("span", "grav-vinculo__texto");
    gatilho.append(texto);
    const { ancora } = popover(gatilho, "Cliente e projeto", (fechar) => {
      const atual = lugar === "heroi" ? sugestao : vinculo;
      const form = el("form", "grav-vinculo__form");
      const cCliente = campoComSugestoes("Cliente", `grav-${lugar}-cliente`, Object.keys(clientes), atual?.cliente ?? "");
      const cProjeto = campoComSugestoes("Projeto", `grav-${lugar}-projeto`, clientes[atual?.cliente] ?? [], atual?.projeto ?? "");
      const salvar = botao("aa-btn-primario aa-btn--pequeno", "Usar");
      salvar.type = "submit";
      form.append(cCliente, cProjeto, salvar);
      form.addEventListener("submit", (e) => {
        e.preventDefault();
        aoSalvar({
          cliente: cCliente.querySelector("input").value.trim(),
          projeto: cProjeto.querySelector("input").value.trim(),
        });
        fechar();
      });
      return { raiz: form, focar: () => cCliente.querySelector("input").focus() };
    });
    return { raiz: ancora, texto };
  }

  const vinculoHeroi = editorDeVinculo("heroi", (v) => {
    sugestao = v.cliente ? v : null;
    heroiDica.textContent = sugestao ? "escolhido agora" : "";
    vinculoHeroi.texto.textContent = rotuloDoVinculo(sugestao);
  });
  heroiVinculo.append(vinculoHeroi.raiz, heroiDica);

  const vinculoFaixa = editorDeVinculo("faixa", (v) => salvarVinculo(v));
  faixaLinha.append(vinculoFaixa.raiz, faixaConvidados);

  let vinculoDe = null;
  async function carregarVinculo(g) {
    if (!g) { vinculo = null; vinculoDe = null; return; }
    vinculoDe = g;
    try {
      const r = await pedir("reuniao", { gravacao: g });
      if (vinculoDe !== g) return;
      vinculo = { cliente: r.cliente || "", projeto: r.projeto || "" };
    } catch {
      vinculo = { cliente: "", projeto: "" };
    }
    vinculoFaixa.texto.textContent = rotuloDoVinculo(vinculo);
    if (sugestaoPendente) aplicarSugestao();
  }

  /**
   * Grava o par no `reuniao.json` da gravação que está correndo. É o mesmo
   * pedido da reunião aberta; ele só escreve esse arquivo, e a correção de
   * termos que ele dispara sai cedo enquanto não há falantes — o caso de toda
   * gravação em curso.
   */
  async function salvarVinculo(v) {
    const g = estado.gravacao;
    if (!g) return;
    vinculo = v;
    vinculoFaixa.texto.textContent = rotuloDoVinculo(v);
    try {
      await pedir("salvar-reuniao", { gravacao: g, cliente: v.cliente, projeto: v.projeto });
      // A falha de antes deixou de valer: a mesma ação acabou de dar certo.
      avisosDeFora.replaceChildren();
    } catch (e) {
      avisosDeFora.replaceChildren(alerta(`Não guardou o cliente: ${e.message}`, "erro"));
    }
  }

  /** A sugestão do herói vai para a gravação que ele começou — uma vez só. */
  function aplicarSugestao() {
    sugestaoPendente = false;
    // Quem já escreveu um vínculo (a bandeja, outra tela) ganha da sugestão.
    if (!sugestao || vinculo?.cliente) return;
    salvarVinculo(sugestao);
  }

  // Falhas que não são do gravador (guardar o cliente): ficam com os avisos.
  const avisosDeFora = el("div");
  gravando.insertBefore(avisosDeFora, grade);

  // ─────────────────────────────────────────────────────── desenho

  let gravandoDesenhado = null;
  let listagem = null;
  let fixadoDesenhado;

  function aplicar(g) {
    estado = g;

    // Primeiro o que diz se está gravando e se está mudo: uma exceção mais
    // abaixo nesta volta não pode congelar o relógio nem o botão de mutar.
    ponto.dataset.cor = g.cor;
    ponto.title = g.status;
    tempo.textContent = relogio(g.duracao_s);
    rotularMutar(g.mudo);
    faixa.dataset.mudo = String(Boolean(g.mudo));

    if (g.gravando !== gravandoDesenhado) trocouDeEstado(g);


    desenharAvisos(g);

    for (const [nome, m] of Object.entries(faixas)) {
      const f = g.faixas.find((x) => x.nome === nome);
      if (!f) continue;
      m.preenchimento.style.width = `${porcentagem(f.nivel)}%`;
      m.raiz.title = f.mudo ? `${f.dispositivo} (mudo)` : f.dispositivo;
      // Sem áudio nenhum passados 45 s é o mesmo limiar do ícone amarelo. O
      // medidor já mostra a barra parada; o atributo é o que a torna vermelha,
      // porque uma barra parada e uma barra baixa se parecem demais.
      m.trilho.dataset.morto = String(!f.ja_ouviu && !f.mudo && g.duracao_s > 45);
      m.raiz.dataset.mudo = String(Boolean(f.mudo));
    }

    faixaTitulo.textContent = g.titulo || "Gravação sem reunião da agenda";
    const nomes = g.participantes ?? [];
    // "convidados" e não "participantes": a lista vem do convite da agenda, e
    // ela diz quem foi CHAMADO, não quem apareceu.
    faixaConvidados.textContent = nomes.length
      ? `${nomes.length} ${nomes.length === 1 ? "convidado" : "convidados"}` : "";

    reuniao.hidden = !g.titulo;
    if (g.titulo) {
      tituloReuniao.textContent = g.titulo;
      participantes.textContent = nomes.length
        ? `${nomes.length} ${nomes.length === 1 ? "convidado" : "convidados"}: ${nomes.join(", ")}`
        : "Sem convidados na agenda.";
    }

    // Só quando a escolha muda: pode vir daqui, da bandeja, ou de o Parar ter
    // soltado a reunião fixada no fim da gravação.
    if ((g.fixado ?? null) !== fixadoDesenhado) desenharProximas();

    vaiAgenda.textContent = !g.usar_agenda ? "não usada"
      : g.conta ?? (g.agenda_configurada ? "não conectada" : "sem credencial");

    escolhaMic.campo.disabled = g.gravando;
    escolhaLoop.campo.disabled = g.gravando;
    caminho.textContent = g.pasta;

    // O apontarPara ignora repetição, então chamar isto cinco vezes por segundo
    // não custa nada.
    notas.apontarPara(g.gravando ? g.gravacao : null);
    notas.definirHabilitado(Boolean(g.gravando && g.gravacao),
      "As notas abrem quando a gravação começa. Depois, edite pela reunião.");
    marcar.disabled = !(g.gravando && g.gravacao);

    if (g.gravando && g.gravacao && g.gravacao !== vinculoDe) carregarVinculo(g.gravacao);
  }

  function rotularMutar(mudo) {
    mutar.textContent = mudo ? "Desmutar" : "Mutar";
    mutar.setAttribute("aria-pressed", String(Boolean(mudo)));
  }

  /** Começou ou parou: o que muda uma vez por gravação, e não 5×/s. */
  function trocouDeEstado(g) {
    const primeira = gravandoDesenhado === null;
    gravandoDesenhado = g.gravando;
    raiz.dataset.gravando = String(g.gravando);
    antes.hidden = g.gravando;
    gravando.hidden = !g.gravando;
    // A agenda desce para baixo da grade enquanto grava — escolher outra
    // reunião vale até a gravação terminar, e ela não pode sumir.
    if (g.gravando) resto.insertBefore(proximas, reuniao);
    else baixo.insertBefore(proximas, ultima);

    const desde = new Date(Date.now() - g.duracao_s * 1000);
    cabecalho("Gravador", g.gravando ? `gravando desde ${hora(desde)}` : "pronto para gravar", false);
    desenharSelo();

    if (!g.gravando) {
      vinculo = null;
      vinculoDe = null;
      avisosDeFora.replaceChildren();
      if (!primeira) recarregarGravacoes();
    }
    gravarEsta.disabled = gravarSem.disabled = false;
  }

  /** O aviso de mudo, e queda e falha de dispositivo, logo abaixo da faixa. */
  let avisoDesenhado = "";
  function desenharAvisos(g) {
    const itens = [];
    if (g.gravando && g.mudo) {
      const min = Math.floor((g.mudo_ha_s ?? 0) / 60);
      itens.push(["mudo", min >= 1
        ? `Microfone mudo há ${min} min. Sua voz não está sendo gravada.`
        : "Microfone mudo. Sua voz não está sendo gravada."]);
    }
    for (const f of g.faixas) {
      const nome = NOME_DA_FAIXA[f.nome] ?? f.nome;
      if (f.desconectado) itens.push(["caiu", `O dispositivo de ${nome.toLowerCase()} caiu.`]);
      if (f.falha) itens.push(["falha", `Falha ao gravar ${nome.toLowerCase()}: ${f.falha}`]);
    }
    // Reconstruir só quando o texto muda: o botão Desmutar tem de sobreviver às
    // cinco voltas por segundo, senão o clique cai num nó que já saiu.
    const chave = JSON.stringify(itens);
    if (chave === avisoDesenhado) return;
    avisoDesenhado = chave;
    avisos.replaceChildren(...itens.map(([tipo, texto]) => {
      const a = alerta(texto, "erro");
      a.dataset.aviso = tipo;
      if (tipo === "mudo") {
        const des = botao("aa-btn-secundario aa-btn--pequeno grav-desmutar", "Desmutar");
        des.addEventListener("click", () => alternarMudo());
        a.appendChild(des);
      }
      return a;
    }));
  }

  // ─────────────────────────────────────────── o selo da legenda na barra

  let modoDaLegenda = null;
  function aoSaberDaLegenda(r) {
    modoDaLegenda = r.aovivo_impedimento ? null : r.aovivo_modo;
    const sem = modoDaLegenda !== "legenda" && modoDaLegenda !== "bloco";
    grade.dataset.legenda = String(!sem);
    previa.raiz.hidden = sem;
    semLegenda.hidden = !sem;
    semLegendaMotivo.textContent = r.aovivo_impedimento
      ? `Por que não há: ${r.aovivo_impedimento}` : "";
    desenharSelo();
  }

  function desenharSelo() {
    if (!ctx.acoesDaBarra) return;
    if (!estado.gravando || !modoDaLegenda) { ctx.acoesDaBarra(); return; }
    const selo = el("span", "aa-etiqueta aa-etiqueta--info grav-selo",
      modoDaLegenda === "legenda" ? "Placa: legenda ao vivo" : "Placa: prévia em blocos");
    ctx.acoesDaBarra(selo);
  }

  // ────────────────────────────────────────────────── o herói e a agenda

  function eventoDoHeroi() {
    const eventos = listagem?.eventos ?? [];
    const agora = new Date();
    return eventos.find((e) => e.id === estado.fixado)
      ?? eventos.find((e) => e.id === listagem?.pre_definido)
      ?? eventos.find((e) => e.inicio && new Date(e.inicio) > agora)
      ?? null;
  }

  /** A gravação de hoje com este título, se houver — "gravada" na lista. */
  function gravadaHoje(ev) {
    const hoje = new Date().toDateString();
    return (gravacoes ?? []).find((g) => g.titulo === ev.titulo && diaDoNome(g.nome) === hoje);
  }

  function desenharHeroi() {
    const ev = eventoDoHeroi();
    heroi.dataset.comReuniao = String(Boolean(ev));
    if (!ev) {
      heroiRotulo.textContent = "Sem reunião na agenda";
      heroiTitulo.textContent = "Pronto para gravar";
      heroiFatos.textContent = !estado.usar_agenda
        ? "A agenda não rotula as gravações — ligue em Ajustes › Gravador."
        : listagem === null ? "Consultando a agenda…"
          : SEM_LISTA[listagem.status] ?? SEM_LISTA.sem_evento;
      heroiVinculo.hidden = true;
      gravarEsta.hidden = true;
      gravarSem.hidden = false;
      gravarSem.textContent = "Gravar";
      gravarSem.className = "aa-btn aa-btn-primario aa-btn--grande grav-gravar";
      sugestao = null;
      return;
    }
    const escolhida = estado.fixado === ev.id;
    heroiRotulo.textContent = `${escolhida ? "Escolhida na agenda" : "Próxima na agenda"} · ${quandoComeca(ev.inicio, ev.fim)}`;
    heroiTitulo.textContent = ev.titulo;
    heroiFatos.textContent = [
      quando(ev.inicio, ev.fim),
      ev.participantes > 0 ? `${ev.participantes} convidado${ev.participantes === 1 ? "" : "s"}` : null,
      ev.organizador ? `organizada por ${ev.organizador}` : null,
    ].filter(Boolean).join(" · ");
    heroiVinculo.hidden = false;
    gravarEsta.hidden = false;
    gravarSem.textContent = "Gravar sem reunião da agenda";
    gravarSem.className = "aa-btn aa-btn-secundario aa-btn--grande";
    // **Só quando é verdade.** Sem nada fixado, o núcleo rotula a gravação com
    // o `pre_definido` sozinho — e soltar a escolhida também cai nele. Com um
    // pre_definido, "sem reunião da agenda" gravaria com reunião; o botão some.
    // A versão exata precisa de um "nenhuma" no fixar-evento (C#, GRA-4).
    gravarSem.hidden = Boolean(listagem?.pre_definido);

    // A sugestão: o par da última gravação com o mesmo título. Uma escolha
    // feita aqui mesmo ganha, até o herói mudar de reunião.
    if (heroi.dataset.evento !== ev.id) {
      heroi.dataset.evento = ev.id;
      const anterior = (gravacoes ?? []).find((g) => g.titulo === ev.titulo && g.cliente);
      sugestao = anterior ? { cliente: anterior.cliente, projeto: anterior.projeto ?? "" } : null;
      heroiDica.textContent = anterior ? "o mesmo da última reunião com este título" : "";
    }
    vinculoHeroi.texto.textContent = rotuloDoVinculo(sugestao);
  }

  function desenharProximas() {
    fixadoDesenhado = estado.fixado ?? null;
    // Sem credencial do Google não há o que fazer com a agenda, e sem "usar a
    // agenda" não há o que oferecer: o bloco some inteiro, em vez de virar uma
    // linha de desculpa permanente.
    proximas.hidden = !estado.usar_agenda || listagem?.status === "nao_configurado";
    desenharHeroi();
    listaProximas.replaceChildren();

    if (listagem === null) {
      listaProximas.appendChild(el("p", "bloco__texto", "Consultando a agenda…"));
      return;
    }
    if (listagem.status === "erro") {
      listaProximas.appendChild(alerta(
        `Não deu para ler a agenda: ${listagem.detalhe ?? "erro desconhecido"}.`, "erro"));
      return;
    }
    const eventos = listagem.eventos ?? [];
    if (eventos.length === 0) {
      listaProximas.appendChild(el("p", "bloco__texto", SEM_LISTA[listagem.status] ?? SEM_LISTA.sem_evento));
      return;
    }

    const heroiId = eventoDoHeroi()?.id;
    for (const ev of eventos) {
      const fixada = estado.fixado === ev.id;
      // A marca automática só aparece enquanto ninguém escolheu: com uma
      // reunião fixada, dizer que outra "seria esta" seria falso.
      const automatica = !estado.fixado && listagem.pre_definido === ev.id;
      // Já terminou no papel. Continua escolhível — é justamente a que se
      // procura quando a reunião atrasou —, mas recuada.
      const terminou = Boolean(ev.fim) && new Date(ev.fim) < new Date();
      const gravada = terminou && gravadaHoje(ev);

      const linha = el("div", "reuniao grav-agenda__linha");
      linha.dataset.escolhida = String(fixada || automatica);
      linha.dataset.terminou = String(terminou && !fixada && !gravada);
      linha.dataset.evento = ev.id;

      const horario = el("p", "reuniao__hora", quando(ev.inicio, ev.fim));
      const nome = el("p", "reuniao__titulo", ev.titulo);
      const fatos = el("p", "reuniao__fatos");
      const partes = [];
      if (terminou && !gravada) partes.push("já terminou");
      if (ev.participantes > 0) partes.push(`${ev.participantes} convidado${ev.participantes === 1 ? "" : "s"}`);
      if (ev.organizador) partes.push(ev.organizador);
      fatos.textContent = partes.join(" · ");
      const texto = el("div");
      texto.append(nome, fatos);

      let fim;
      if (fixada) {
        const marca = el("span", "aa-etiqueta aa-etiqueta--info", "escolhida");
        const soltar = botao("aa-btn-texto aa-btn--pequeno", "Soltar");
        soltar.addEventListener("click", () => chamar("fixar-evento", { evento: "" }));
        fim = el("div", "grav-agenda__fim");
        fim.append(marca, soltar);
      } else if (gravada) {
        fim = el("span", "aa-etiqueta aa-etiqueta--sucesso", "gravada");
      } else if (ev.id === heroiId && !estado.gravando) {
        fim = el("span", "aa-etiqueta aa-etiqueta--info", "a próxima");
      } else {
        // Antes de gravar, "Gravar esta" grava; gravando, escolhe o rótulo da
        // gravação que já corre — vale até ela terminar.
        fim = botao("aa-btn-texto aa-btn--pequeno", estado.gravando ? "É esta" : "Gravar esta");
        fim.addEventListener("click", () =>
          estado.gravando ? chamar("fixar-evento", { evento: ev.id }) : gravarReuniao(ev.id));
      }
      linha.append(horario, texto, fim);
      listaProximas.appendChild(linha);
    }
  }

  /**
   * Pergunta a agenda de novo.
   *
   * A marca de "seria esta" é calculada no núcleo, com a hora do momento da
   * pergunta — ela envelhece sozinha. Por isso a tela repete a consulta de
   * minuto em minuto enquanto está aberta, em vez de desenhar uma vez.
   */
  async function carregarProximas() {
    if (!estado.usar_agenda) { listagem = null; desenharProximas(); return; }
    atualizar.disabled = true;
    try {
      const r = await pedir("agenda-proximas");
      listagem = r.proximas ?? { status: "sem_evento", eventos: [] };
    } catch (e) {
      listagem = { status: "erro", detalhe: e.message, eventos: [] };
    } finally {
      atualizar.disabled = false;
    }
    desenharProximas();
  }

  // ─────────────────────────────────────────────────── a última gravação

  function desenharUltima() {
    const g = (gravacoes ?? []).find((x) => x.caminho !== estado.gravacao);
    ultima.hidden = !g;
    if (!g) return;
    ultimaTitulo.textContent = g.titulo || "Gravação sem título";
    const dia = diaDoNome(g.nome) === new Date().toDateString() ? "hoje" : dataDoNome(g.nome);
    ultimaFatos.textContent = [
      `${dia}, ${horaDoNome(g.nome)}`,
      `${Math.max(1, Math.round((g.duracao_s ?? 0) / 60))} min`,
      g.cliente ? [g.cliente, g.projeto].filter(Boolean).join(" › ") : null,
    ].filter(Boolean).join(" · ");
    const rodando = emCurso(g.caminho);
    const e = estadoDe(g, rodando);
    ultimaRotulo.textContent = e.rotulo.replace(/…$/, "");
    ultima.dataset.estado = e.chave;
    ultimaBarra.hidden = !rodando;
    ultimaPct.textContent = rodando?.fracao != null ? `${Math.round(rodando.fracao * 100)}%` : "";
    if (rodando) ultimaPreench.style.width = `${Math.round((rodando.fracao ?? 0) * 100)}%`;
    // Acabar de gravar e achar o botão de transcrever sem ir a Reuniões.
    ultimaAbrir.textContent = !g.transcrita && !rodando ? "Transcrever" : "Abrir reunião";
    ultimaAbrir.onclick = () => ctx.abrirGravacao?.(g);
  }

  async function recarregarGravacoes() {
    try {
      ({ gravacoes } = await pedir("gravacoes"));
    } catch { /* fica a lista que havia */ }
    desenharUltima();
    desenharProximas();
  }

  // ───────────────────────────────────────────────────── interação

  async function chamar(op, campos = {}) {
    try {
      const r = await pedir(op, campos);
      // Deu certo: o erro que uma chamada anterior deixou aqui não vale mais.
      avisosDeFora.replaceChildren();
      erroHeroi.replaceChildren();
      if (r.gravador) aplicar(r.gravador);
      return true;
    } catch (e) {
      avisosDeFora.replaceChildren(alerta(e.message, "erro"));
      heroiErro(e.message);
      return false;
    }
  }

  const erroHeroi = el("div");
  heroiTexto.appendChild(erroHeroi);
  function heroiErro(texto) { erroHeroi.replaceChildren(alerta(texto, "erro")); }

  /** Grava a reunião da agenda: fixa, e só então começa. */
  async function gravarReuniao(id) {
    gravarEsta.disabled = gravarSem.disabled = true;
    erroHeroi.replaceChildren();
    const aSugestao = id === eventoDoHeroi()?.id;
    if (estado.fixado !== id && !await chamar("fixar-evento", { evento: id })) {
      gravarEsta.disabled = gravarSem.disabled = false;
      return;
    }
    sugestaoPendente = aSugestao && Boolean(sugestao);
    if (!sugestaoPendente) sugestao = null;
    if (!await chamar("gravar")) {
      sugestaoPendente = false;
      gravarEsta.disabled = gravarSem.disabled = false;
    }
  }

  gravarEsta.addEventListener("click", () => {
    const ev = eventoDoHeroi();
    if (ev) gravarReuniao(ev.id);
  });
  // **Sem reunião da agenda**: solta a escolhida e grava. O núcleo não tem hoje
  // um "não rotular" — sem nada fixado, a escolha automática dele ainda pode
  // rotular a gravação com a reunião do momento.
  gravarSem.addEventListener("click", async () => {
    gravarEsta.disabled = gravarSem.disabled = true;
    erroHeroi.replaceChildren();
    sugestaoPendente = false;
    if (estado.fixado) await chamar("fixar-evento", { evento: "" });
    if (!await chamar("gravar")) gravarEsta.disabled = gravarSem.disabled = false;
  });

  marcar.addEventListener("click", () => {
    if (notas.marcarMomento({ focar: abaAtiva === "notas" })) previa.marcarMomento();
  });
  // Um mutar por vez: dois cliques rápidos mandariam dois e desfariam o mudo.
  let mutando = false;
  async function alternarMudo() {
    if (mutando) return;
    mutando = true;
    mutar.disabled = true;
    avisos.querySelector(".grav-desmutar")?.setAttribute("disabled", "");
    try { await chamar("mutar"); } finally {
      mutando = false;
      mutar.disabled = false;
      avisos.querySelector(".grav-desmutar")?.removeAttribute("disabled");
    }
  }
  mutar.addEventListener("click", alternarMudo);
  parar.addEventListener("click", async () => {
    parar.disabled = true;
    await chamar("parar-gravacao");
    parar.disabled = false;
  });

  for (const [faixaNome, escolha] of [["mic", escolhaMic], ["loopback", escolhaLoop]])
    escolha.campo.addEventListener("change", async () => {
      await chamar("escolher-dispositivo", { faixa: faixaNome, dispositivo: escolha.campo.value });
      desenharVai();
    });

  function desenharVai() {
    const nomeDe = (lista, id) => {
      const d = id ? lista.find((x) => x.id === id) : lista.find((x) => x.padrao);
      return d?.nome ?? "Padrão do Windows";
    };
    vaiMic.textContent = nomeDe(dispositivos.entradas, escolhaMic.campo.value);
    vaiAudio.textContent = nomeDe(dispositivos.saidas, escolhaLoop.campo.value);
  }

  atualizar.addEventListener("click", carregarProximas);

  // De minuto em minuto, e não a cada 200 ms como o resto desta tela: cada
  // volta é uma chamada de rede ao Google, e o que envelhece aqui é a marca de
  // "seria esta", que muda de reunião algumas vezes por dia.
  const relogioDaAgenda = setInterval(() => {
    if (!raiz.isConnected) { clearInterval(relogioDaAgenda); return; }
    carregarProximas();
  }, 60_000);

  const cancelarTranscricoes = assinarTranscricoes(() => {
    if (!raiz.isConnected) { cancelarTranscricoes(); return; }
    desenharUltima();
  });

  const cancelar = assinar("gravador", (evento) => {
    // A tela saiu do DOM (o usuário mudou de destino): parar de desenhar e
    // largar a assinatura. Sem isto, um medidor de uma tela fechada continuaria
    // escrevendo em nós órfãos até o app fechar.
    if (!raiz.isConnected) {
      cancelar();
      cancelarTranscricoes();
      clearInterval(relogioDaAgenda);
      // A prévia também: ela segura um IntersectionObserver, que sobreviveria à
      // tela e ficaria observando um nó órfão até o app fechar.
      previa.encerrar();
      return;
    }
    aplicar(evento.gravador);
  });

  aplicar(estado);
  desenharVai();
  desenharUltima();
  desenharProximas();
  carregarProximas();
}

/** "2026-09-25_11-02-00" → a data, para comparar com hoje. */
function diaDoNome(nome) {
  const m = /^(\d{4})-(\d{2})-(\d{2})_/.exec(nome ?? "");
  return m ? new Date(+m[1], +m[2] - 1, +m[3]).toDateString() : "";
}
function dataDoNome(nome) {
  const m = /^(\d{4})-(\d{2})-(\d{2})_/.exec(nome ?? "");
  return m ? `${m[3]}/${m[2]}` : "";
}
function horaDoNome(nome) {
  const m = /_(\d{2})-(\d{2})/.exec(nome ?? "");
  return m ? `${m[1]}:${m[2]}` : "";
}

/** Um select de dispositivo, com "Padrão do Windows" primeiro. */
function seletor(rotulo, faixa, lista, escolhido) {
  const label = document.createElement("label");
  label.className = "campo";

  const span = document.createElement("span");
  span.textContent = rotulo;

  const campo = document.createElement("select");
  campo.className = "aa-entrada";
  campo.id = `dispositivo-${faixa}`;

  // Primeiro e padrão porque seguir o dispositivo do sistema é o que a maioria
  // quer: fixar um headset específico quebra no dia em que ele é desconectado.
  const padrao = document.createElement("option");
  padrao.value = "";
  padrao.textContent = "Padrão do Windows";
  campo.appendChild(padrao);

  for (const d of lista) {
    const o = document.createElement("option");
    o.value = d.id;
    o.textContent = d.padrao ? `${d.nome}  (padrão)` : d.nome;
    campo.appendChild(o);
  }
  campo.value = escolhido ?? "";

  label.append(span, campo);
  return { raiz: label, campo };
}
