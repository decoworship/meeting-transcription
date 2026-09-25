// As regras da reunião aberta e do preparo, sem DOM — para o `node --test`
// alcançá-las (tools/web/reuniao-regras.test.mjs). Ver o plano 2b,
// docs/superpowers/plans/2026-09-25-ui-02b-reuniao-resto.md.

/**
 * O vocabulário gravado, em termos.
 *
 * O `initial_prompt` do projeto sempre foi texto livre separado por vírgula,
 * mas quem digitava na caixa antiga também quebrava linha. Os três separadores
 * valem, e o repetido sai sem olhar caixa: "NOC" e "noc" são o mesmo termo para
 * quem lê a lista, e a primeira grafia é a que a pessoa escolheu.
 */
export function separarTermos(texto) {
  const vistos = new Set();
  const termos = [];
  for (const cru of String(texto ?? "").split(/[,;\n]/)) {
    const t = cru.trim();
    if (!t) continue;
    const chave = t.toLocaleLowerCase("pt-BR");
    if (vistos.has(chave)) continue;
    vistos.add(chave);
    termos.push(t);
  }
  return termos;
}

/** De volta ao formato que o motor e o disco sempre leram. */
export const juntarTermos = (lista) => lista.join(", ");

const MARCA = /\[(\d{1,2}):(\d{2}):(\d{2})\]/;

/**
 * Os momentos marcados nas notas (`UI-4`).
 *
 * "Marcar momento" do Gravador escreve `[hh:mm:ss]` com o tempo decorrido da
 * gravação, que é o mesmo relógio do mix. Uma marca por linha — a primeira —,
 * porque o botão é a linha: duas marcas na mesma linha são uma nota que cita
 * outro momento, e não dois momentos.
 */
export function momentosDasNotas(texto) {
  const momentos = [];
  for (const linha of String(texto ?? "").split("\n")) {
    const m = linha.match(MARCA);
    if (!m) continue;
    const [h, min, s] = [m[1], m[2], m[3]].map(Number);
    if (min > 59 || s > 59) continue;
    momentos.push({
      segundos: h * 3600 + min * 60 + s,
      marca: m[0].slice(1, -1),
      texto: (linha.slice(0, m.index) + linha.slice(m.index + m[0].length)).trim(),
    });
  }
  return momentos;
}

/** As etapas do pipeline, na ordem em que o núcleo as percorre. */
export const ETAPAS = [
  ["mix", "Somando as faixas"],
  ["asr", "Transcrevendo"],
  ["diarizacao", "Separando os falantes"],
  ["montagem", "Montando o resultado"],
];

/** Onde a transcrição está, etapa a etapa. Etapa desconhecida não acende nada. */
export function estadoDasEtapas(atual) {
  const i = ETAPAS.findIndex(([id]) => id === atual);
  return ETAPAS.map(([id, rotulo], j) => ({
    id, rotulo,
    estado: i < 0 ? "pendente" : j < i ? "feita" : j === i ? "atual" : "pendente",
  }));
}

/** "04:12", ou "1:02:03" a partir de uma hora — o relógio do tocador. */
export function relogioDoTocador(segundos) {
  const total = Number.isFinite(segundos) && segundos > 0 ? Math.floor(segundos) : 0;
  const h = Math.floor(total / 3600);
  const p = (n) => String(n).padStart(2, "0");
  const ms = `${p(Math.floor((total % 3600) / 60))}:${p(total % 60)}`;
  return h > 0 ? `${h}:${ms}` : ms;
}

/** A linha que diz o que as opções do motor, dobradas, estão escolhendo. */
export function resumoDoMotor({ modelo, idioma, diarizar }) {
  return [
    modelo,
    idioma?.trim() || "idioma automático",
    diarizar ? "separa os falantes" : "sem separar falantes",
  ].join(" · ");
}
