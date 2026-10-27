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

// Uma imagem da reunião em markdown: só as de capturas/, e sem subir de pasta.
// O mesmo critério de Notas.SemImagens no núcleo.
const IMAGEM = /[ \t]?!\[([^\]\n]*)\]\((capturas\/[^)\s/\\]+)\)/g;
const imagemValida = (caminho) => !caminho.includes("..");

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
    const resto = linha.slice(0, m.index) + linha.slice(m.index + m[0].length);
    // A imagem não é texto do momento: ela vira miniatura ao lado dele.
    const imagem = [...resto.matchAll(IMAGEM)].find((i) => imagemValida(i[2]))?.[2];
    const momento = {
      segundos: h * 3600 + min * 60 + s,
      marca: m[0].slice(1, -1),
      texto: (imagem ? resto.replace(IMAGEM, "") : resto).trim(),
    };
    if (imagem) momento.imagem = imagem;
    momentos.push(momento);
  }
  return momentos;
}

/**
 * As imagens que as notas citam, na ordem em que aparecem.
 *
 * O print do atalho e a imagem colada viram `![legenda](capturas/arquivo)`
 * nas notas. O momento é o da linha, quando ela tem um — é o que deixa a
 * miniatura tocar a reunião dali.
 */
export function imagensDasNotas(texto) {
  const imagens = [];
  for (const linha of String(texto ?? "").split("\n")) {
    const m = linha.match(MARCA);
    const segundos = m ? Number(m[1]) * 3600 + Number(m[2]) * 60 + Number(m[3]) : null;
    for (const i of linha.matchAll(IMAGEM)) {
      if (!imagemValida(i[2])) continue;
      imagens.push({
        caminho: i[2],
        legenda: i[1],
        segundos: m ? segundos : null,
        marca: m ? m[0].slice(1, -1) : null,
      });
    }
  }
  return imagens;
}

/**
 * O endereço da imagem para o `<img>`: a pasta das gravações é servida em
 * `gravacoes.local` (JanelaDoApp), como o áudio do tocador.
 */
export function urlDaImagem(pastaDaGravacao, caminho) {
  const nome = String(pastaDaGravacao).split(/[\\/]/).filter(Boolean).pop() ?? "";
  const partes = String(caminho).split("/").map(encodeURIComponent).join("/");
  return `https://gravacoes.local/${encodeURIComponent(nome)}/${partes}`;
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
/**
 * As opções do seletor de modelo do preparo, tiradas do catálogo.
 *
 * **A lista era fixa** — `["large-v3", "medium", "small", "base", "tiny"]` — e
 * divergiu do catálogo sem ninguém ver: faltava o Turbo, sobrava um "tiny" que
 * o catálogo não oferece, e o Parakeet Ultra (27/10/2026) não apareceu. Agora é
 * a mesma fonte de Ajustes › Modelos, com o nome bonito e o id como valor.
 *
 * `padrao` marca o que Ajustes escolheu (`em_uso`): é com ele que a tela começa,
 * e não com o primeiro da lista — que mandava o Large v3 ao núcleo mesmo com
 * outro modelo escolhido em Ajustes. Um valor salvo no projeto que o catálogo
 * não conhece entra como está, para a escolha não se perder em silêncio.
 */
export function opcoesDeModelo(catalogo, atual) {
  const opcoes = (catalogo ?? [])
    .filter((i) => i?.pacote?.familia === "asr")
    .map((i) => ({
      valor: i.pacote.id,
      rotulo: i.estado === "instalado" ? i.pacote.nome : `${i.pacote.nome} (não baixado)`,
      padrao: Boolean(i.em_uso),
    }));
  if (atual && !opcoes.some((o) => o.valor === atual)) {
    opcoes.push({ valor: atual, rotulo: atual, padrao: false });
  }
  return opcoes;
}

export function resumoDoMotor({ modelo, idioma, diarizar }) {
  return [
    modelo,
    idioma?.trim() || "idioma automático",
    diarizar ? "separa os falantes" : "sem separar falantes",
  ].join(" · ");
}
