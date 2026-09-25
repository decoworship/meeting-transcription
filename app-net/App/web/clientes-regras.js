// As regras de Ajustes › Clientes e projetos (plano 4a do redesenho de UI).
//
// Puras, e por isso provadas fora do navegador em
// tools/web/clientes-regras.test.mjs. As contagens saem da mesma lista de
// gravações que a tela de Reuniões lê: uma reunião conta para o cliente e o
// projeto gravados no vínculo dela.

import { normalizar, diaDe } from "./reunioes-regras.js";

const MESES = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

const plural = (n, um, varios) => `${n} ${n === 1 ? um : varios}`;

/** Ordem de lista em português: "Álvaro" junto de "algar", e não no fim. */
export const ordenar = (nomes) => [...nomes].sort((a, b) => a.localeCompare(b, "pt-BR"));

/**
 * Quantas reuniões cada cliente e cada projeto têm, e o dia da última.
 *
 * A reunião importada (pasta fora do padrão de data) conta, mas não tem dia.
 */
export function contar(gravacoes) {
  const porCliente = new Map();
  const porProjeto = new Map();
  const chave = (c, p) => `${c}\u0000${p}`;
  for (const g of gravacoes) {
    if (!g.cliente) continue;
    porCliente.set(g.cliente, (porCliente.get(g.cliente) ?? 0) + 1);
    if (!g.projeto) continue;
    const k = chave(g.cliente, g.projeto);
    const atual = porProjeto.get(k) ?? { reunioes: 0, ultima: null };
    const dia = diaDe(g.nome);
    porProjeto.set(k, {
      reunioes: atual.reunioes + 1,
      ultima: dia && (!atual.ultima || dia > atual.ultima) ? dia : atual.ultima,
    });
  }
  return {
    cliente: (c) => (c ? porCliente.get(c) ?? 0 : 0),
    projeto: (c, p) => ({ ...(porProjeto.get(chave(c, p)) ?? { reunioes: 0, ultima: null }) }),
  };
}

/** "2 projetos · 9 reuniões". */
export function rotuloDoCliente(projetos, reunioes) {
  return `${plural(projetos, "projeto", "projetos")} · `
    + (reunioes ? plural(reunioes, "reunião", "reuniões") : "nenhuma reunião");
}

/** "5 reuniões · última em 17 set". */
export function rotuloDoProjeto({ reunioes, ultima }) {
  if (!reunioes) return "nenhuma reunião ainda";
  const quantas = plural(reunioes, "reunião", "reuniões");
  if (!ultima) return quantas;
  const [, mes, dia] = ultima.split("-").map(Number);
  return `${quantas} · última em ${dia} ${MESES[mes - 1]}`;
}

/** Os clientes cujo nome contém o texto, sem acento e sem caixa. */
export function filtrarClientes(nomes, texto) {
  const t = normalizar(texto);
  return t ? nomes.filter((n) => normalizar(n).includes(t)) : nomes;
}
