// O que está sendo transcrito, visto de dentro da página.
//
// Um lugar só guarda o estado, e as telas se inscrevem nele. O motivo é o
// defeito que esta fase conserta: até aqui a transcrição vivia dentro da tela
// que a começou, e trocar de destino no trilho jogava fora o DOM em que a barra
// escrevia — o pipeline continuava rodando, sem ninguém para mostrá-lo
// (FASE3.md §2).
//
// O núcleo é a fonte da verdade. Este módulo não deduz nada: recebe o registro
// inteiro a cada evento e reparte para quem estiver ouvindo.

import { pedir, assinar } from "/ponte.js";

const VAZIO = { atual: null, ultimo: null };

let estado = VAZIO;
const ouvintes = new Set();

/** O registro agora. Quem monta uma tela lê daqui antes de desenhar. */
export function transcricoes() {
  return estado;
}

/**
 * Ouve as mudanças do registro. Devolve a função de cancelar.
 *
 * Mesma disciplina dos medidores do gravador: quem sai de tela cancela, senão
 * continua desenhando em nós que já saíram do documento.
 */
export function assinarTranscricoes(fn) {
  ouvintes.add(fn);
  return () => ouvintes.delete(fn);
}

/** A transcrição em curso desta gravação, ou nulo. */
export function emCurso(caminho) {
  return estado.atual?.gravacao === caminho ? estado.atual : null;
}

/** Como a última transcrição desta gravação terminou, ou nulo. */
export function ultimoResultado(caminho) {
  return estado.ultimo?.gravacao === caminho ? estado.ultimo : null;
}

/**
 * Pede a transcrição e volta na hora.
 *
 * A resposta diz só que foi aceita — ou recusa, quando já há outra rodando. O
 * andamento chega depois, pelo mesmo canal de eventos do gravador.
 */
export async function transcrever(campos) {
  aplicar((await pedir("transcrever", campos)).transcricoes);
}

/** Esquece o resultado anterior, quando a tela já o mostrou. */
export async function esquecer() {
  aplicar((await pedir("esquecer-transcricao")).transcricoes);
}

/**
 * Pergunta o estado ao núcleo.
 *
 * Chamado uma vez na subida da página. Sem isto, uma janela reaberta no meio de
 * uma transcrição só descobriria que há uma no próximo evento — e entre um
 * evento e outro pode haver minutos, porque as etapas longas não reportam
 * progresso contínuo.
 */
export async function sincronizar() {
  aplicar((await pedir("transcricoes")).transcricoes);
}

function aplicar(novo) {
  estado = novo ?? VAZIO;
  acenderBolinha();
  // Cópia da lista: um ouvinte que se cancela ao ser chamado — e o da tela de
  // preparo faz exatamente isso ao terminar — mutaria o Set em iteração.
  for (const fn of [...ouvintes]) fn(estado);
}

/**
 * A bolinha no trilho.
 *
 * Sai do estado, e não de um temporizador: apaga quando o núcleo diz que
 * acabou, inclusive quando acabou em erro. O rótulo acompanha, porque um ponto
 * colorido não diz nada a quem usa leitor de tela.
 */
function acenderBolinha() {
  const botao = document.getElementById("ir-reunioes");
  if (!botao) return;

  const rodando = Boolean(estado.atual);
  botao.dataset.ocupado = String(rodando);
  if (rodando) botao.setAttribute("aria-label", `Reuniões — transcrevendo ${estado.atual.nome}`);
  else botao.removeAttribute("aria-label");
}

assinar("transcricoes", (evento) => aplicar(evento.transcricoes));
