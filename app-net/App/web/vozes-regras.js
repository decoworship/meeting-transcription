// As regras de Ajustes › Vozes (plano 6 do redesenho de UI, backlog UI-2).
//
// Puras, e por isso provadas fora do navegador em
// tools/web/vozes-regras.test.mjs. O que é "em uso" é o mesmo que o núcleo
// conta (Nucleo/Vozes.Conta): fora da quarentena, do modelo de voz e da
// geração de regras atuais.

const MESES = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

const plural = (n, um, varios) => `${n} ${n === 1 ? um : varios}`;

/** Modelo antigo ou geração 1: não participa de nada, e nem pede ação. */
export const inerte = (a) => a.outro_modelo === true || a.regras_antigas === true;

/** A amostra que o reconhecimento usa. */
export const emUso = (a) => !a.quarentena && !inerte(a);

/** Abaixo disto o perfil reconhece mal: "pouca voz". Não medido — é o corte do gabarito. */
export const AMOSTRAS_PARA_BOA = 4;

/**
 * A saúde do perfil. `rotulo` é "boa", "pouca voz", ou nulo quando nada da
 * pessoa está em uso (ela vai para "Fora de uso").
 */
export function saude(p) {
  const n = p.amostras.filter(emUso).length;
  return { emUso: n, rotulo: n === 0 ? null : n >= AMOSTRAS_PARA_BOA ? "boa" : "pouca voz" };
}

/** "22 amostras · 2 aparelhos" — as ativas (em uso ou esperando revisão). */
export function resumoDaPessoa(p) {
  const ativas = p.amostras.filter((a) => !inerte(a));
  const aparelhos = new Set(ativas.map((a) => a.dispositivo).filter(Boolean)).size;
  return `${plural(ativas.length, "amostra", "amostras")} · ${plural(aparelhos, "aparelho", "aparelhos")}`;
}

/** Quem tem alguma amostra ativa: a lista de pessoas. */
export const pessoasEmUso = (vozes) => vozes.filter((p) => p.amostras.some((a) => !inerte(a)));

/** O que espera ouvido, a mais nova primeiro. */
export function fila(vozes) {
  const itens = [];
  for (const p of vozes)
    for (const a of p.amostras)
      if (a.quarentena && !inerte(a)) itens.push({ pessoa: p.nome, amostra: a });
  return itens.sort((x, y) => (y.amostra.criada_em ?? "").localeCompare(x.amostra.criada_em ?? ""));
}

/** As amostras inertes, e quem só tem delas. */
export function foraDeUso(vozes) {
  let total = 0;
  const pessoas = [];
  for (const p of vozes) {
    const n = p.amostras.filter(inerte).length;
    total += n;
    if (n > 0 && n === p.amostras.length) pessoas.push(p.nome);
  }
  return { total, pessoas };
}

/** "17 set" do carimbo ISO, no dia local. */
export function dia(iso) {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  return `${d.getDate()} ${MESES[d.getMonth()]}`;
}

/** Duas casas, com vírgula: "0,41". */
export const numero = (x) => x.toFixed(2).replace(".", ",");

const FAIXAS = { system: "áudio da reunião", loopback: "áudio da reunião", mic: "seu microfone" };

/** "17 set · áudio da reunião · 3,1 s · Sala Conf B". */
export function procedencia(a) {
  return [dia(a.criada_em), FAIXAS[a.faixa] ?? a.faixa,
          `${a.duracao_s.toFixed(1).replace(".", ",")} s`, a.dispositivo]
    .filter(Boolean).join(" · ");
}

/** O nome curto do botão "É Carol". */
export const primeiroNome = (nome) => nome.trim().split(/\s+/)[0];

/** A mesma chave para (A, B) e (B, A). */
export const chaveDoPar = (a, b) => [a, b].sort().join("\u0000");

/** As sugestões de juntar que ainda valem: sem as recusadas e sem quem sumiu. */
export function parecidosVisiveis(pares, recusados, nomes) {
  const existe = new Set(nomes);
  return pares.filter((p) => existe.has(p.a) && existe.has(p.b) && !recusados.has(chaveDoPar(p.a, p.b)));
}

/**
 * A ordem da lista: quem tem voz boa primeiro, e dentro de cada saúde quem
 * tem mais amostras ativas. "Pouca voz" junta no fim, que é onde se procura
 * de quem o app precisa ouvir mais.
 */
export function ordenarPessoas(vozes) {
  const n = (p) => p.amostras.filter((a) => !inerte(a)).length;
  const boa = (p) => (saude(p).rotulo === "boa" ? 0 : 1);
  return [...vozes].sort((x, y) => boa(x) - boa(y) || n(y) - n(x) || x.nome.localeCompare(y.nome, "pt-BR"));
}
