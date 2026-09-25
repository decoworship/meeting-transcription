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
    // Cada assinante isolado: o erro de uma tela (o chip, um painel) não pode
    // pular as outras — a faixa do Gravador é uma delas.
    for (const fn of assinantes.get(resposta.tipo) ?? []) {
      try { fn(resposta); } catch (e) { console.error(`assinante de "${resposta.tipo}" quebrou:`, e); }
    }
    return;
  }

  const pendente = pendentes.get(resposta.id);
  if (!pendente) return;          // resposta de um pedido já abandonado

  // Progresso não encerra o pedido: a promessa continua pendente até a
  // mensagem sem `tipo`, que é a final. É o que permite uma operação de
  // minutos reportar andamento sem um segundo canal.
  if (resposta.tipo === "progresso") {
    // E é também o sinal de vida em que o prazo se apoia: enquanto chega
    // andamento, o núcleo está trabalhando, e o relógio recomeça do zero.
    pendente.rearmar();
    pendente.aoProgredir?.(resposta);
    return;
  }

  pendente.encerrar();
  if (resposta.erro) pendente.rejeitar(new Error(resposta.erro));
  else pendente.resolver(resposta);
});

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

/**
 * Envia um pedido ao núcleo e espera a resposta.
 *
 * @param aoProgredir chamado a cada aviso de andamento, quando houver.
 * @param opcoes `{ prazoMs, sinal }` — ver abaixo.
 *
 * <b>O prazo não é ligado por padrão, e isso é a decisão, não o esquecimento.</b>
 * Um prazo cego mataria as operações que existem: `escolher-pasta` fica aberta
 * enquanto o usuário procura a pasta no diálogo do Windows, `aprender-voz` sobe
 * o pyannote pela segunda vez, e `diagnostico` roda um processo filho. Nenhuma
 * delas reporta andamento, e nenhuma delas está quebrada por demorar. Quem
 * chama é quem sabe quanto a sua operação pode demorar; a ponte não tem como
 * saber, e chutar aqui transforma um pedido lento num erro inventado.
 *
 * <b>Quando há prazo, ele conta silêncio e não tempo total</b> — cada
 * `progresso` rearma o relógio. É o que permite pôr prazo num download de
 * 641 MB sem que o tamanho do arquivo entre na conta: o que se está vigiando é
 * o núcleo parar de falar, que é o defeito real (uma tela em "gerando…" para
 * sempre, sem erro e sem saída), e não a operação ser demorada.
 *
 * <b>Cancelar é abandonar, e só.</b> O `sinal` (um AbortSignal) faz esta página
 * parar de esperar e devolve uma rejeição; ele <b>não</b> manda o núcleo parar,
 * porque não há op para isso no protocolo — quem quer que o trabalho pare de
 * verdade usa a op própria, como o `cancelar-transcricao`. Uma resposta que
 * chegue depois cai no `if (!pendente) return` lá em cima, que é o mesmo
 * caminho de sempre.
 */
export function pedir(op, campos = {}, aoProgredir = null, opcoes = {}) {
  const { prazoMs = 0, sinal = null } = opcoes;
  const id = proximoId++;

  return new Promise((resolver, rejeitar) => {
    let relogio = null;
    const aoAbortar = () => {
      encerrar();
      rejeitar(new Error("cancelado"));
    };

    // Tudo o que precisa ser desfeito num lugar só: sem isto, um pedido que
    // responde deixa para trás um setTimeout e um ouvinte de abort presos ao
    // sinal — que costuma viver mais que o pedido, porque é da tela.
    const encerrar = () => {
      pendentes.delete(id);
      clearTimeout(relogio);
      sinal?.removeEventListener("abort", aoAbortar);
    };

    const rearmar = () => {
      if (!prazoMs) return;
      clearTimeout(relogio);
      relogio = setTimeout(() => {
        encerrar();
        rejeitar(new Error(`o núcleo não respondeu a "${op}"`));
      }, prazoMs);
    };

    const pendente = { resolver, rejeitar, aoProgredir, rearmar, encerrar };
    pendentes.set(id, pendente);

    // O sinal já abortado é caso normal — a tela pode ter saído entre montar o
    // pedido e mandá-lo —, e nele nada chega a ser postado.
    if (sinal?.aborted) return aoAbortar();
    sinal?.addEventListener("abort", aoAbortar, { once: true });

    rearmar();
    window.chrome.webview.postMessage(JSON.stringify({ id, op, ...campos }));
  });
}
