// As regras da lista de Reuniões: o que a busca vê, como se agrupa por dia, e o
// que cada gravação precisa em seguida.
//
// **Sem DOM e sem import**, de propósito. É a parte da tela que erra em
// silêncio — um filtro que esconde uma reunião não dá erro, só deixa a lista
// menor —, e por isso é a que se testa fora do navegador:
//
//     node --test tools/web/reunioes-regras.test.mjs
//
// O desenho está em docs/superpowers/specs/2026-09-23-ui-ux.md §3.3.

const SEMANA = ["domingo", "segunda", "terça", "quarta", "quinta", "sexta", "sábado"];
const MESES = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];
const MESES_LONGOS = ["janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto",
                      "setembro", "outubro", "novembro", "dezembro"];

/** O valor do filtro de cliente que pega as gravações sem cliente. */
export const SEM_CLIENTE = "__sem_cliente__";

/** [valor, texto]. O texto diz o critério inteiro, porque o seletor não tem rótulo visível. */
export const PERIODOS = [
  ["tudo", "Qualquer data"],
  ["hoje", "Hoje"],
  ["esta-semana", "Esta semana"],
  ["semana-passada", "Semana passada"],
  ["30", "Últimos 30 dias"],
  ["dia", "Um dia…"],
  ["semana", "Uma semana…"],
];

export const ESTADOS = [
  ["", "Todos os estados"],
  ["nao-transcrita", "Não transcritas"],
  ["sem-ata", "Sem ata"],
  ["com-pendencias", "Com pendências"],
  ["ata-velha", "Ata desatualizada"],
  ["ata-pronta", "Ata pronta"],
];

/**
 * O andamento em palavras, pela etapa.
 *
 * Sem porcentagem: a fração do registro é da etapa, e não do pipeline
 * (Nucleo/RegistroDeTranscricoes.cs) — "46%" durante a separação de falantes
 * diria que falta metade quando falta um quarto.
 */
const ETAPAS = {
  mix: "Somando as faixas…",
  asr: "Transcrevendo…",
  diarizacao: "Separando falantes…",
  montagem: "Montando…",
};

/** Minúsculas e sem acento: "Crédito" e "credito" são a mesma busca. */
export function normalizar(texto) {
  return String(texto ?? "").normalize("NFD").replace(/[\u0300-\u036f]/g, "")
    .toLowerCase().trim();
}

/** "2026-09-23_14-00-12" → "2026-09-23". Nome fora do padrão → null. */
export function diaDe(nome) {
  const m = /^(\d{4})-(\d{2})-(\d{2})_/.exec(nome ?? "");
  return m ? `${m[1]}-${m[2]}-${m[3]}` : null;
}

/** "2026-09-23_14-00-12" → "14:00". Nome fora do padrão → "". */
export function horaDe(nome) {
  const m = /^\d{4}-\d{2}-\d{2}_(\d{2})-(\d{2})/.exec(nome ?? "");
  return m ? `${m[1]}:${m[2]}` : "";
}

/** A data de hoje no fuso da máquina — o mesmo em que o gravador nomeia as pastas. */
export function hojeLocal(agora = new Date()) {
  const p = (n) => String(n).padStart(2, "0");
  return `${agora.getFullYear()}-${p(agora.getMonth() + 1)}-${p(agora.getDate())}`;
}

/** "AAAA-MM-DD" como meia-noite UTC — conta de calendário, sem hora e sem fuso. */
function utc(dia) {
  const [a, m, d] = dia.split("-").map(Number);
  return new Date(Date.UTC(a, m - 1, d));
}

const iso = (data) => data.toISOString().slice(0, 10);

/** Dias de calendário entre duas datas "AAAA-MM-DD". */
function diasEntre(de, ate) {
  return Math.round((utc(ate) - utc(de)) / 86_400_000);
}

export function somarDias(dia, n) {
  const d = utc(dia);
  d.setUTCDate(d.getUTCDate() + n);
  return iso(d);
}

/** O mesmo dia, `n` meses adiante ou atrás; no último dia, quando o mês é mais curto. */
export function somarMeses(dia, n) {
  const [a, m, d] = dia.split("-").map(Number);
  const alvo = new Date(Date.UTC(a, m - 1 + n, 1));
  const ultimo = new Date(Date.UTC(alvo.getUTCFullYear(), alvo.getUTCMonth() + 1, 0)).getUTCDate();
  alvo.setUTCDate(Math.min(d, ultimo));
  return iso(alvo);
}

/** A segunda-feira da semana de um dia. A semana vai de segunda a domingo. */
export function segundaDe(dia) {
  return somarDias(dia, -((utc(dia).getUTCDay() + 6) % 7));
}

/**
 * O intervalo [de, até] de um período, as duas pontas incluídas, ou null
 * para "qualquer data" — e para "um dia" ou "uma semana" enquanto o dia não
 * foi escolhido: um filtro pela metade não pode esvaziar a lista.
 *
 * `data` é o dia escolhido no calendário ("AAAA-MM-DD"); só "dia" e "semana"
 * o usam.
 */
export function intervaloDoPeriodo(periodo, data, hoje) {
  if (periodo === "dia") return data ? [data, data] : null;
  if (periodo === "semana") {
    if (!data) return null;
    const s = segundaDe(data);
    return [s, somarDias(s, 6)];
  }
  if (!hoje) return null;
  if (periodo === "hoje") return [hoje, hoje];
  if (periodo === "esta-semana") {
    const s = segundaDe(hoje);
    return [s, somarDias(s, 6)];
  }
  if (periodo === "semana-passada") {
    const s = somarDias(segundaDe(hoje), -7);
    return [s, somarDias(s, 6)];
  }
  if (periodo === "30") return [somarDias(hoje, -29), hoje];
  return null;
}

/** "Hoje · quarta, 23 set", "Ontem · terça, 22 set", "Quinta, 17 set", "Quarta, 31 dez 2025". */
export function rotuloDoDia(dia, hoje) {
  if (!dia) return "Sem data";
  const [a, m, d] = dia.split("-").map(Number);
  const semana = SEMANA[new Date(Date.UTC(a, m - 1, d)).getUTCDay()];
  const ano = hoje && dia.slice(0, 4) !== hoje.slice(0, 4) ? ` ${a}` : "";
  const data = `${d} ${MESES[m - 1]}${ano}`;
  const distancia = hoje ? diasEntre(dia, hoje) : null;
  if (distancia === 0) return `Hoje · ${semana}, ${data}`;
  if (distancia === 1) return `Ontem · ${semana}, ${data}`;
  return `${semana[0].toUpperCase()}${semana.slice(1)}, ${data}`;
}

/**
 * A lista em grupos de um dia, na ordem em que chegou.
 *
 * O núcleo ordena pelo nome da pasta, decrescente, e nessa ordem um nome fora
 * do padrão ("reuniao-importada") vem ANTES dos números. O grupo "Sem data" vai
 * para o fim à mão.
 */
export function agruparPorDia(gravacoes, hoje) {
  const grupos = new Map();
  for (const g of gravacoes) {
    const dia = diaDe(g.nome);
    const chave = dia ?? "";
    if (!grupos.has(chave)) grupos.set(chave, { dia, rotulo: rotuloDoDia(dia, hoje), itens: [] });
    grupos.get(chave).itens.push(g);
  }
  const todos = [...grupos.values()];
  return [...todos.filter((x) => x.dia), ...todos.filter((x) => !x.dia)];
}

// ─────────────────────────────────────────── a semana e o calendário

/** "21 a 27 set", "28 set a 4 out"; o ano entra quando não é o de hoje. */
export function rotuloDaSemana(segunda, hoje) {
  const fim = somarDias(segunda, 6);
  const [a1, m1, d1] = segunda.split("-").map(Number);
  const [a2, m2, d2] = fim.split("-").map(Number);
  const anoDeHoje = hoje ? Number(hoje.slice(0, 4)) : a2;
  if (a1 !== a2) return `${d1} ${MESES[m1 - 1]} ${a1} a ${d2} ${MESES[m2 - 1]} ${a2}`;
  const ano = a2 !== anoDeHoje ? ` ${a2}` : "";
  if (m1 === m2) return `${d1} a ${d2} ${MESES[m2 - 1]}${ano}`;
  return `${d1} ${MESES[m1 - 1]} a ${d2} ${MESES[m2 - 1]}${ano}`;
}

/**
 * O que o botão do filtro de data diz: o atalho, o dia ou a semana escolhida.
 * Um dia ou uma semana sem o dia escolhido não filtra (intervaloDoPeriodo), e
 * o botão diz isso.
 */
export function rotuloDoFiltroDeData(periodo, data, hoje) {
  if ((periodo === "dia" || periodo === "semana") && !data) return "Qualquer data";
  if (periodo === "dia") {
    const [a, m, d] = data.split("-").map(Number);
    const ano = hoje && data.slice(0, 4) !== hoje.slice(0, 4) ? ` de ${a}` : "";
    return `${d} de ${MESES_LONGOS[m - 1]}${ano}`;
  }
  if (periodo === "semana") return `Semana de ${rotuloDaSemana(segundaDe(data), hoje)}`;
  return (PERIODOS.find(([v]) => v === periodo) ?? PERIODOS[0])[1];
}

/** "setembro de 2026". `mes` vai de 1 a 12. */
export function rotuloDoMes(ano, mes) {
  return `${MESES_LONGOS[mes - 1]} de ${ano}`;
}

/** "quinta, 24 de setembro de 2026" — o dia inteiro, para o leitor de tela. */
export function rotuloLongoDoDia(dia) {
  const [a, m, d] = dia.split("-").map(Number);
  return `${SEMANA[utc(dia).getUTCDay()]}, ${d} de ${MESES_LONGOS[m - 1]} de ${a}`;
}

/**
 * O mês em semanas de segunda a domingo, inteiras: os dias de fora do mês
 * completam as pontas e vêm marcados. Quatro a seis semanas.
 *
 * @param mes de 1 a 12.
 * @returns [[{ dia: "AAAA-MM-DD", n, fora }, …7], …]
 */
export function gradeDoMes(ano, mes) {
  const p = (n) => String(n).padStart(2, "0");
  const primeiro = `${ano}-${p(mes)}-01`;
  const ultimo = iso(new Date(Date.UTC(ano, mes, 0)));
  const semanas = [];
  for (let s = segundaDe(primeiro); s <= ultimo; s = somarDias(s, 7)) {
    semanas.push(Array.from({ length: 7 }, (_, i) => {
      const dia = somarDias(s, i);
      return { dia, n: Number(dia.slice(8)), fora: dia.slice(0, 7) !== primeiro.slice(0, 7) };
    }));
  }
  return semanas;
}

/** Os dias que têm gravação, para o ponto do calendário. */
export function diasComGravacao(gravacoes) {
  return new Set(gravacoes.map((g) => diaDe(g.nome)).filter(Boolean));
}

/**
 * As colunas da semana: segunda a sexta sempre, e sábado e domingo juntos
 * quando um dos dois tem gravação. Dentro do dia, em ordem de hora — é uma
 * agenda, e não a lista, que põe a mais nova em cima.
 *
 * @param visiveis as gravações que passaram nos filtros: são os cartões.
 * @param todas o acervo, que decide o fim de semana. Filtrar não pode fazer
 *   uma coluna aparecer e sumir enquanto se digita.
 * @returns [{ dia, rotulo: "Seg 21", hoje, itens }]
 */
export function colunasDaSemana(visiveis, segunda, hoje, todas = visiveis) {
  const sabado = somarDias(segunda, 5);
  const domingo = somarDias(segunda, 6);
  const comFimDeSemana = todas.some((g) => [sabado, domingo].includes(diaDe(g.nome)));
  const dias = Array.from({ length: comFimDeSemana ? 7 : 5 }, (_, i) => somarDias(segunda, i));
  return dias.map((dia) => {
    const nome = SEMANA[utc(dia).getUTCDay()];
    return {
      dia,
      rotulo: `${nome[0].toUpperCase()}${nome.slice(1, 3)} ${Number(dia.slice(8))}`,
      hoje: dia === hoje,
      itens: visiveis.filter((g) => diaDe(g.nome) === dia)
        .sort((x, y) => (x.nome < y.nome ? -1 : x.nome > y.nome ? 1 : 0)),
    };
  });
}

/**
 * O estado da gravação em poucas palavras, e o tom da etiqueta.
 *
 * O que está rodando vem antes de tudo: "Não transcrita" ao lado de uma
 * transcrição que está andando é mentira — a regra que a etiquetaDe do app.js
 * já seguia.
 *
 * `tem_ata` e `ata_velha` ausentes (núcleo antigo, --web) contam como falso.
 */
export function estadoDe(g, rodando = null) {
  if (rodando) {
    if (rodando.tarefa === "ata")
      return { chave: "escrevendo-ata", rotulo: "Escrevendo a ata…", tom: "info" };
    const rotulo = rodando.tarefa === "falantes" ? "Separando falantes…"
      : ETAPAS[rodando.etapa] ?? "Transcrevendo…";
    return { chave: "transcrevendo", rotulo, tom: "info" };
  }
  if (!g.transcrita) return { chave: "nao-transcrita", rotulo: "Não transcrita", tom: "neutro" };
  if (!g.tem_ata) return { chave: "sem-ata", rotulo: "Sem ata", tom: "neutro" };
  if (g.ata_velha) return { chave: "ata-velha", rotulo: "Ata desatualizada", tom: "atencao" };
  return { chave: "ata-pronta", rotulo: "Ata pronta", tom: "sucesso" };
}

/** O que a reunião precisa agora — o botão principal do painel. */
export function proximoPasso(g, rodando = null) {
  if (rodando)
    return rodando.tarefa === "ata"
      ? { acao: "acompanhar-ata", rotulo: "Acompanhar a ata" }
      : { acao: "acompanhar-transcricao", rotulo: "Acompanhar a transcrição" };
  if (!g.transcrita) return { acao: "transcrever", rotulo: "Transcrever" };
  if (!g.tem_ata) return { acao: "gerar-ata", rotulo: "Gerar a ata" };
  if (g.ata_velha) return { acao: "refazer-ata", rotulo: "Refazer a ata" };
  return { acao: "abrir-ata", rotulo: "Abrir a ata" };
}

/** "2026-09-17_…" → "17/09/2026", para achar a reunião sem título pela data. */
function dataCurta(nome) {
  const dia = diaDe(nome);
  if (!dia) return "";
  const [a, m, d] = dia.split("-");
  return `${d}/${m}/${a}`;
}

/**
 * Tudo o que a busca olha numa gravação, normalizado.
 *
 * O estado entra pelo mesmo rótulo da etiqueta, e as pendências pela mesma
 * contagem da linha: quem digita "ata pronta" ou "pendências" está lendo a
 * tela, e a busca tem de achar o que a tela mostra.
 */
export function textoDeBusca(g, rodando = null) {
  const pendencias = g.tem_ata && g.pendencias > 0 ? `${g.pendencias} pendências` : null;
  return normalizar([g.titulo, g.cliente, g.projeto, dataCurta(g.nome), ...(g.nomes ?? []),
                     estadoDe(g, rodando).rotulo, pendencias]
    .filter(Boolean).join(" "));
}

/**
 * As gravações que passam em todos os critérios.
 *
 * A busca casa **todas** as palavras, em qualquer campo: "algar rafael" é a
 * reunião da Algar em que o Rafael foi convidado, não qualquer uma das duas.
 *
 * @param rodandoDe o `emCurso` de transcricoes.js — o estado depende do que
 *   está rodando agora.
 */
export function filtrar(gravacoes, criterios = {}, { hoje = null, rodandoDe = () => null } = {}) {
  const { texto = "", cliente = "", periodo = "tudo", data = "", estado = "" } = criterios;
  const palavras = normalizar(texto).split(/\s+/).filter(Boolean);
  const faixa = intervaloDoPeriodo(periodo, data, hoje);

  return gravacoes.filter((g) => {
    if (cliente === SEM_CLIENTE) {
      if (g.cliente) return false;
    } else if (cliente && g.cliente !== cliente) {
      return false;
    }

    // "AAAA-MM-DD" compara como texto na mesma ordem do calendário.
    if (faixa) {
      const dia = diaDe(g.nome);
      if (!dia || dia < faixa[0] || dia > faixa[1]) return false;
    }

    if (estado === "com-pendencias") {
      if (!(g.tem_ata && g.pendencias > 0)) return false;
    } else if (estado && estadoDe(g, rodandoDe(g.caminho)).chave !== estado) {
      return false;
    }

    if (palavras.length > 0) {
      const alvo = textoDeBusca(g, rodandoDe(g.caminho));
      if (!palavras.every((p) => alvo.includes(p))) return false;
    }
    return true;
  });
}

/** Os clientes que aparecem no acervo, com quantas gravações cada um, em ordem alfabética. */
export function clientesDe(gravacoes) {
  const contagem = new Map();
  for (const g of gravacoes)
    if (g.cliente) contagem.set(g.cliente, (contagem.get(g.cliente) ?? 0) + 1);
  return [...contagem.entries()]
    .sort((a, b) => a[0].localeCompare(b[0], "pt-BR"))
    .map(([nome, n]) => ({ nome, n }));
}
