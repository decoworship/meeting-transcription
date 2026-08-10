// A ponte com o núcleo C#.
//
// Mesmo contrato dos motores (docs/SIDECAR.md): JSON com `op` e um `id` que
// volta na resposta. O WebView2 entrega mensagens sem ordem garantida, então o
// id é o que casa pedido e resposta — sem ele, duas chamadas em voo trocariam
// de resultado.

const pendentes = new Map();
let proximoId = 1;

window.chrome.webview.addEventListener("message", (evento) => {
  let resposta;
  try {
    resposta = JSON.parse(evento.data);
  } catch {
    return;                       // lixo no canal: ignorar é melhor que travar
  }

  const pendente = pendentes.get(resposta.id);
  if (!pendente) return;          // resposta de um pedido já abandonado
  pendentes.delete(resposta.id);

  if (resposta.erro) pendente.rejeitar(new Error(resposta.erro));
  else pendente.resolver(resposta);
});

/** Envia um pedido ao núcleo e espera a resposta. */
export function pedir(op, campos = {}) {
  const id = proximoId++;
  return new Promise((resolver, rejeitar) => {
    pendentes.set(id, { resolver, rejeitar });
    window.chrome.webview.postMessage(JSON.stringify({ id, op, ...campos }));
  });
}
