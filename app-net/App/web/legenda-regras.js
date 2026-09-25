// Onde a legenda quebra a linha — a régua, sem DOM, para se provar com
// `node --test tools/web/legenda-regras.test.mjs`.
//
// **A medição** (docs/superpowers/specs/2026-09-23-ui-ux.md §2, 23/09/2026): nas
// 11 gravações com `legenda.json`, a legenda só quebrava quando o dono trocava,
// e com o microfone mudo os outros viravam um bloco só — p90 de 200 palavras, o
// maior com 3.060. O streaming não dá tempo por palavra nem pontuação (0% dos
// trechos terminam em ponto); dá o trecho, que firma a cada quadro de 1,12 s.
// Quebrar numa pausa de **dois quadros sem fala (≥ 3,36 s)**, ou de **≥ 2,24 s
// depois de 40 palavras**, leva a p90 57 e máximo 236. Um quadro só picota
// (mediana 9 palavras). Para refazer no acervo, é esta a régua.
//
// **O tempo é o de chegada.** O pedaço (`PedacoDaLegendaJson`) chega sem tempo;
// a tela o carimba com o relógio da gravação no momento em que ele chega. O
// intervalo entre dois pedaços firmes é o que a medição chamou de pausa — o
// motor não firma nada enquanto ninguém fala.

export const PAUSA_S = 3.36;
export const PAUSA_EM_LINHA_LONGA_S = 2.24;
export const PALAVRAS_DE_LINHA_LONGA = 40;

/**
 * O pedaço que chega abre uma linha nova?
 *
 * @param linha a linha corrente `{ dono, ate_s, palavras }`, ou nulo.
 * @param pedaco `{ dono, t }` — de quem é e quando chegou, em segundos.
 */
export function novaLinha(linha, pedaco) {
  if (!linha || linha.dono !== pedaco.dono) return true;
  const pausa = pedaco.t - linha.ate_s;
  if (pausa >= PAUSA_S - 1e-9) return true;
  return linha.palavras >= PALAVRAS_DE_LINHA_LONGA && pausa >= PAUSA_EM_LINHA_LONGA_S - 1e-9;
}

/** "Você"/"Outros" só na primeira linha de uma sequência do mesmo dono. */
export function mostraDono(anterior, dono) {
  return !anterior || anterior.dono !== dono;
}

export function contarPalavras(texto) {
  const t = (texto || "").trim();
  return t ? t.split(/\s+/).length : 0;
}
