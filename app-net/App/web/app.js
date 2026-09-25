import { pedir } from "/ponte.js";
import { telaDaReuniao, abrirPainel } from "/reuniao.js";
import { telaDeAjustes } from "/configuracoes.js";
import { telaDoGravador } from "/gravador.js";
import { abrirGaveta, fecharGavetas, pararAudio, alerta, confirmar, avisar,
         anunciar } from "/pecas.js";
import { sincronizar } from "/transcricoes.js";
import { telaDeReunioes } from "/reunioes.js";
import { ligarBolinhas } from "/trilho.js";
import { telaDePreparo as montarPreparo } from "/preparo.js";

const tela = document.getElementById("tela");
const titulo = document.getElementById("titulo");
const subtitulo = document.getElementById("subtitulo");
const voltar = document.getElementById("voltar");
const acoes = document.getElementById("acoes-da-barra");

// A altura da barra do topo, medida — e não chutada no CSS. Quatro blocos
// grudam abaixo dela (as abas da reunião e as de Ajustes, os controles da
// revisão, o painel de Reuniões), e a barra muda de altura com o que mostra:
// com título, subtítulo e o ← da reunião aberta ela tinha 90 px, e o chute de
// 4.75rem (76 px) deixava 14 px de cada bloco atrás dela. Ver o :root do app.css.
new ResizeObserver(([e]) => {
  document.documentElement.style.setProperty(
    // Sem arredondar: a barra tem altura fracionária, e o ceil deixava uma fresta
    // de 1 px por onde o texto rolado aparecia.
    "--altura-da-barra", `${e.borderBoxSize[0].blockSize}px`);
}).observe(document.querySelector(".barra"));

/** "1h 02min" ou "3min 20s" — a duração é para dar noção, não para cronometrar. */
export function duracao(segundos) {
  const s = Math.round(segundos);
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  if (h > 0) return `${h}h ${String(m).padStart(2, "0")}min`;
  if (m > 0) return `${m}min ${String(s % 60).padStart(2, "0")}s`;
  return `${s}s`;
}

/** "2026-08-10_08-08-10" -> "10/08/2026 às 08:08". */
export function quando(nome) {
  const m = nome.match(/^(\d{4})-(\d{2})-(\d{2})_(\d{2})-(\d{2})/);
  if (!m) return nome;
  const [, ano, mes, dia, hora, min] = m;
  return `${dia}/${mes}/${ano} às ${hora}:${min}`;
}

export const tituloDe = (g) => g.titulo || quando(g.nome);

/**
 * Põe na barra do topo, à direita, o que é da tela — e tira o que havia.
 *
 * Todo ponto de navegação a esvazia (`navegar`), e a tela que chega põe o seu.
 * Esvaziar pela troca de título falhava na única troca em que o título é o
 * mesmo: da reunião para o preparo dela, em "Transcrever de novo".
 */
export function acoesDaBarra(...nos) {
  acoes.replaceChildren(...nos);
}

/** O que toda troca de destino faz antes de desenhar: gavetas fechadas, barra vazia. */
function navegar() {
  fecharGavetas();
  acoes.replaceChildren();
}

/**
 * Troca o título da moldura — e, com ele, a tela.
 *
 * Toda tela chama isto ao se montar, e <b>só</b> ao se montar: é o único ponto
 * do app por onde todas as trocas de tela passam, incluindo as que moram noutro
 * módulo e o recebem por <code>ctx</code>. Por isso o áudio para aqui, e não em
 * cada destino — um destino novo não pode nascer esquecendo de parar o que a
 * tela anterior estava tocando.
 *
 * O que ele <b>não</b> pega são as gavetas, que não passam por aqui. É de
 * propósito: abrir os falantes enquanto se ouve um trecho não deve cortar o
 * áudio (ver <code>pararAudio</code> em pecas.js).
 */
function cabecalho(t, sub, comVoltar) {
  pararAudio();
  const mudou = titulo.textContent !== t;
  titulo.textContent = t;
  subtitulo.textContent = sub ?? "";
  voltar.hidden = !comVoltar;

  // **O foco vai para o título, e é a metade do FE-3 que faltava.** Trocar o
  // <h1> não é trocar de tela para quem navega por teclado ou lê por leitor de
  // tela: o foco continuava no botão do trilho, e clicar em "Atas" não produzia
  // sinal nenhum de que a página tinha mudado. Ver docs/FASE7-FRONTEND.md §F-3.
  //
  // Só quando o título muda de verdade: o cabeçalho é reescrito também para
  // trocar o subtítulo — "carregando a transcrição…" vira o nome do projeto —,
  // e mover o foco a cada um desses seria pior que não mover.
  if (!mudou) return;

  // Quem está digitando não perde o cursor. Acontece de verdade: o aviso de
  // versão e o fim de uma transcrição reescrevem o cabeçalho, e a pessoa pode
  // estar no meio das notas.
  const foco = document.activeElement;
  if (foco && foco.matches?.("input, textarea, select, [contenteditable]")) return;

  // -1 e não 0: o título entra na ordem de leitura, mas não vira mais uma parada
  // de Tab para quem só navega.
  titulo.tabIndex = -1;
  titulo.focus({ preventScroll: true });

  // E o anúncio diz o ESTADO, nunca o conteúdo: o nome da tela, não o que ela
  // tem dentro. A regra vale desde já e é o que impede o painel ao vivo de um
  // dia ler a reunião inteira em voz alta, por cima da reunião (§9).
  anunciar(sub ? `${t} — ${sub}` : t);
}

/**
 * Marca no trilho onde estamos.
 *
 * Reuniões, preparo e revisão são o mesmo destino: quem está revisando uma
 * transcrição continua "em Reuniões", e acender outra coisa mentiria sobre
 * onde o ← voltar leva.
 */
function destino(qual) {
  for (const b of document.querySelectorAll(".trilho__item"))
    b.removeAttribute("aria-current");
  document.getElementById(qual)?.setAttribute("aria-current", "page");
}

// ─────────────────────────────────────────────────────────── lista

/**
 * A lista mora em reunioes.js, pelo mesmo motivo dos outros destinos: o
 * app.js é o único lugar que sabe da moldura, e a tela recebe o que precisa
 * dela.
 */
export function telaDeLista() {
  navegar();
  destino("ir-reunioes");
  return telaDeReunioes({ cabecalho, tela });
}

/**
 * Apaga a gravação inteira — áudio, metadados e transcrição.
 *
 * Mora <b>dentro</b> da gravação aberta, e não na lista. Um botão de apagar em
 * cada cartão põe a ação mais destrutiva do app a um clique errado de distância,
 * numa tela que se percorre rápido; abrir a gravação primeiro custa um clique e
 * garante que quem apaga está olhando para o que apaga.
 *
 * A confirmação nomeia a gravação e diz o que não volta. O áudio original não se
 * refaz: não há lixeira, e o gravador não guarda cópia.
 */
export function botaoApagarGravacao(g) {
  const b = document.createElement("button");
  b.className = "aa-btn aa-btn-texto";
  b.type = "button";
  b.textContent = "Apagar gravação";
  b.addEventListener("click", async () => {
    if (!await confirmar(
      "Isto apaga o áudio, os metadados e a transcrição desta reunião.\n"
      + "O áudio original não pode ser recuperado.",
      { titulo: `Apagar "${tituloDe(g)}"?`, ok: "Apagar" })) return;

    b.disabled = true;
    try {
      await pedir("apagar-gravacao", { gravacao: g.caminho });
    } catch (e) {
      await avisar(e.message, { titulo: "Não deu para apagar" });
      b.disabled = false;
      return;
    }
    telaDeLista();
  });
  return b;
}

// ───────────────────────────────────────────── preparar / transcrever

/** O preparo mora em preparo.js; daqui ele leva a moldura e o que fazer no fim. */
function telaDePreparo(g) {
  return montarPreparo(g, { cabecalho, tela, navegar, aoTerminar: abrirResultado });
}

/** Abre a revisão do que acabou de ficar pronto, lendo o que foi salvo em disco. */
async function abrirResultado(g) {
  try {
    const r = await pedir("transcricao", { gravacao: g.caminho });
    if (!r.transcricao) throw new Error("a transcrição não foi encontrada");
    g.transcrita = true;
    telaDaReuniao(g, JSON.parse(r.transcricao), { cabecalho, tela });
  } catch (e) {
    tela.replaceChildren(alerta(e.message, "erro"));
  }
}

/**
 * Abre uma gravação: a reunião com abas, se ela já foi transcrita, ou a tela de
 * transcrever, se não foi.
 *
 * @param aba qual aba da reunião abrir — "transcricao" (o padrão), "ata" ou
 *   "notas". O painel de Reuniões pede "ata" para o próximo passo da ata.
 */
export async function abrirGravacao(g, { aba = "transcricao" } = {}) {
  navegar();
  if (!g.transcrita) return telaDePreparo(g);

  // Refazer é ação de exceção: some da tela a menos que tenha sido ligada nas
  // configurações. Ver ConfiguracoesDoApp.PermitirRetranscrever.
  const { config } = await pedir("config");
  if (config?.permitir_retranscrever) {
    const r = await pedir("transcricao", { gravacao: g.caminho });
    if (r.transcricao) {
      cabecalho(tituloDe(g), "carregando…", true);
      tela.replaceChildren();
      telaDaReuniao(g, JSON.parse(r.transcricao), {
        cabecalho, tela, aba,
        aoRefazer: () => { g.transcrita = false; telaDePreparo(g); },
        aoApagar: botaoApagarGravacao(g),
      });
      return;
    }
  }

  cabecalho(tituloDe(g), "carregando a transcrição…", true);
  tela.replaceChildren();
  const r = await pedir("transcricao", { gravacao: g.caminho });
  if (!r.transcricao) {
    tela.replaceChildren(alerta("A transcrição não foi encontrada.", "erro"));
    return;
  }
  telaDaReuniao(g, JSON.parse(r.transcricao), {
    cabecalho, tela, aba, aoApagar: botaoApagarGravacao(g),
  });
}

// ──────────────────────────────────────────────────────── ajustes

/**
 * A tela de ajustes mora em configuracoes.js.
 *
 * Ela precisa do cabeçalho e do <main> daqui, e não os importa: passar os dois
 * como contexto mantém o app.js como o único lugar que sabe da moldura.
 */
export function abrirAjustes(aba) {
  navegar();
  destino("ir-config");
  return telaDeAjustes({ cabecalho, tela }, aba);
}

// ─────────────────────────────────────────────────────── gravador

/**
 * A tela do Gravador mora em gravador.js, pelo mesmo motivo dos ajustes: o
 * app.js é o único lugar que sabe da moldura, e as telas recebem o que
 * precisam dela.
 */
export function abrirGravador() {
  navegar();
  destino("ir-gravador");
  return telaDoGravador({ cabecalho, tela });
}

// ─────────────────────────────────────────────────────────── ligação

document.getElementById("ir-config").addEventListener("click", () => abrirAjustes());
document.getElementById("ir-gravador").addEventListener("click", abrirGravador);

document.getElementById("ir-reunioes").addEventListener("click", telaDeLista);
voltar.addEventListener("click", telaDeLista);

for (const b of document.querySelectorAll("[data-fechar]"))
  b.addEventListener("click", fecharGavetas);
document.getElementById("veu").addEventListener("click", fecharGavetas);
document.addEventListener("keydown", (e) => { if (e.key === "Escape") fecharGavetas(); });

/**
 * Abre direto numa tela quando o app foi iniciado com --tela.
 *
 * Existe para desenhar e fotografar cada estado sem depender de clique — e
 * clique automatizado, quando tentado, acertou a janela errada.
 */
/**
 * A máquina ainda não tem o modelo de transcrição?
 *
 * É a pergunta da primeira execução, e ela nasceu com o instalador da Fase 4:
 * até então o modelo estava sempre lá, porque a máquina era a de quem o baixou.
 * Numa instalação nova, cair na lista de gravações vazia não diz o que fazer —
 * e o que há para fazer é um download de 3 GB que precisa começar antes da
 * primeira reunião, não durante.
 *
 * Só o de transcrição decide isto. O de ata é grande também, mas quem abre o app
 * pela primeira vez quer gravar e transcrever; mandá-lo para a tela de Modelos
 * por causa da ata seria responder uma pergunta que ele ainda não fez.
 *
 * Falhar aqui não pode custar a tela: sem resposta, segue para a lista, que é o
 * comportamento de sempre.
 */
async function faltaModelo() {
  try {
    const { catalogo } = await pedir("catalogo");
    const asr = catalogo.find((i) => i.pacote.familia === "asr" && i.em_uso);
    return asr ? asr.estado !== "instalado" : false;
  } catch {
    return false;
  }
}

async function inicio() {
  // Antes de qualquer tela: se já havia uma transcrição rodando quando esta
  // página subiu, a bolinha tem que acender agora. Esperar o próximo evento
  // pode custar minutos — as etapas longas do pipeline não reportam progresso
  // contínuo.
  await sincronizar().catch(() => {});
  ligarBolinhas();

  const hash = location.hash.slice(1);
  if (!hash) return (await faltaModelo()) ? abrirAjustes("modelos") : telaDeLista();

  // "revisao=1&falantes" — a parte depois do & abre um painel por cima, que é
  // o que não dá para alcançar sem clique.
  const [principal, extra] = hash.split("&");
  const [tela, arg] = principal.split("=");

  // Os ajustes não dependem de gravação nenhuma, e pedir a lista antes atrasaria
  // a tela por nada. "#config=vozes" cai direto na aba.
  if (tela === "config") return abrirAjustes(arg || "geral");
  if (tela === "gravador") return abrirGravador();
  // Atas deixou de ser destino (a ata é uma aba da reunião): o endereço antigo
  // cai na lista, e não numa tela em branco.
  if (tela === "atas") return telaDeLista();

  const { gravacoes } = await pedir("gravacoes");
  const g = gravacoes[Number(arg) || 0];
  if (!g) return telaDeLista();

  if (tela === "preparo") { g.transcrita = false; return abrirGravacao(g); }
  if (tela === "revisao") {
    await abrirGravacao(g);
    if (extra) abrirPainel(extra);
    return;
  }
  return telaDeLista();
}

inicio();
