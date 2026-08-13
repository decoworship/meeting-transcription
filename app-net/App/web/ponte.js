// A ponte com o núcleo C#.
//
// Mesmo contrato dos motores (docs/SIDECAR.md): JSON com `op` e um `id` que
// volta na resposta. O WebView2 entrega mensagens sem ordem garantida, então o
// id é o que casa pedido e resposta — sem ele, duas chamadas em voo trocariam
// de resultado.

const pendentes = new Map();
const assinantes = new Map();
let proximoId = 1;

window.chrome.webview.addEventListener("message", (evento) => {
  let resposta;
  try {
    resposta = JSON.parse(evento.data);
  } catch {
    return;                       // lixo no canal: ignorar é melhor que travar
  }

  // Evento empurrado pelo núcleo: id zero, porque não responde a pedido nenhum.
  // É por aqui que o nível de áudio chega cinco vezes por segundo sem a página
  // ficar perguntando — a única coisa neste app que flui sem alguém pedir.
  if (resposta.id === 0) {
    for (const fn of assinantes.get(resposta.tipo) ?? []) fn(resposta);
    return;
  }

  const pendente = pendentes.get(resposta.id);
  if (!pendente) return;          // resposta de um pedido já abandonado

  // Progresso não encerra o pedido: a promessa continua pendente até a
  // mensagem sem `tipo`, que é a final. É o que permite uma operação de
  // minutos reportar andamento sem um segundo canal.
  if (resposta.tipo === "progresso") {
    pendente.aoProgredir?.(resposta);
    return;
  }

  pendentes.delete(resposta.id);
  if (resposta.erro) pendente.rejeitar(new Error(resposta.erro));
  else pendente.resolver(resposta);
});

/**
 * Envia um pedido ao núcleo e espera a resposta.
 * @param aoProgredir chamado a cada aviso de andamento, quando houver.
 */
/**
 * Ouve os eventos que o núcleo empurra.
 *
 * Devolve a função de cancelar. Quem sai de tela precisa chamá-la: uma tela
 * antiga continuando a desenhar num DOM que já foi trocado é o jeito clássico
 * de um medidor "voltar a se mexer sozinho" depois de fechado.
 */
export function assinar(tipo, fn) {
  if (!assinantes.has(tipo)) assinantes.set(tipo, new Set());
  assinantes.get(tipo).add(fn);
  return () => assinantes.get(tipo)?.delete(fn);
}

export function pedir(op, campos = {}, aoProgredir = null) {
  const id = proximoId++;
  return new Promise((resolver, rejeitar) => {
    pendentes.set(id, { resolver, rejeitar, aoProgredir });
    window.chrome.webview.postMessage(JSON.stringify({ id, op, ...campos }));
  });
}
