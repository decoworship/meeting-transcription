// A tela do Gravador: o espelho, dentro da janela, do que a bandeja faz.
//
// A bandeja continua sendo o gravador — esta tela é adição, não substituição.
// O que ela pode e a bandeja não, e que justifica existir:
//
//   1. os medidores de nível das duas faixas, ao vivo. É o que teria denunciado
//      o microfone mudo de 06/08 no primeiro minuto, e não 36 minutos depois;
//   2. a reunião da agenda que está sendo gravada, com os participantes já
//      reconhecidos — hoje isso só aparece depois, no meta.json.
//
// Desenha uma vez e depois só atualiza os nós que mudam. Redesenhar a tela
// inteira cinco vezes por segundo derrubaria o foco de qualquer select aberto e
// faria o texto piscar.

import { pedir, assinar } from "/ponte.js";
import { alerta } from "/pecas.js";

/** "00:12:34" — aqui o tempo é para cronometrar, ao contrário da lista. */
function relogio(segundos) {
  const s = Math.max(0, Math.round(segundos));
  const p = (n) => String(n).padStart(2, "0");
  return `${p(Math.floor(s / 3600))}:${p(Math.floor((s % 3600) / 60))}:${p(s % 60)}`;
}

/**
 * O nível em altura de barra.
 *
 * Escala logarítmica, e não o RMS cru: a fala normal fica entre 0,01 e 0,1 de
 * RMS, e uma barra linear passaria a reunião inteira nos primeiros 10% —
 * indistinguível de silêncio, que é justamente o que este medidor existe para
 * distinguir. -60 dB vira 0% e 0 dB vira 100%.
 */
function porcentagem(rms) {
  if (!(rms > 0)) return 0;
  const db = 20 * Math.log10(rms);
  return Math.max(0, Math.min(100, ((db + 60) / 60) * 100));
}

const NOME_DA_FAIXA = { mic: "Microfone", system: "Áudio do sistema" };

/** Um medidor por faixa: rótulo, barra e o dispositivo em uso. */
function medidor(nome) {
  const raiz = document.createElement("div");
  raiz.className = "faixa";

  const rotulo = document.createElement("p");
  rotulo.className = "faixa__nome";
  rotulo.textContent = NOME_DA_FAIXA[nome] ?? nome;

  const trilho = document.createElement("div");
  trilho.className = "medidor";
  const preenchimento = document.createElement("div");
  trilho.appendChild(preenchimento);

  const dispositivo = document.createElement("p");
  dispositivo.className = "faixa__dispositivo";
  dispositivo.textContent = "—";

  raiz.append(rotulo, trilho, dispositivo);
  return { raiz, preenchimento, dispositivo, trilho };
}

export async function telaDoGravador(ctx) {
  const { cabecalho, tela } = ctx;
  cabecalho("Gravador", "", false);
  tela.setAttribute("aria-busy", "true");
  tela.replaceChildren();

  let estado, dispositivos;
  try {
    [{ gravador: estado }, { dispositivos }] = await Promise.all([
      pedir("gravador"), pedir("dispositivos"),
    ]);
  } catch (e) {
    tela.setAttribute("aria-busy", "false");
    tela.replaceChildren(alerta(e.message, "erro"));
    return;
  }
  tela.setAttribute("aria-busy", "false");

  const raiz = document.createElement("div");
  raiz.className = "painel";

  // ---- estado e controles
  const cartao = document.createElement("div");
  cartao.className = "bloco gravador";

  const linhaEstado = document.createElement("div");
  linhaEstado.className = "gravador__estado";
  const ponto = document.createElement("span");
  ponto.className = "gravador__ponto";
  const tempo = document.createElement("p");
  tempo.className = "gravador__tempo";
  const situacao = document.createElement("p");
  situacao.className = "gravador__situacao";
  const textos = document.createElement("div");
  textos.append(tempo, situacao);
  linhaEstado.append(ponto, textos);

  const acoes = document.createElement("div");
  acoes.className = "acoes";
  const principal = document.createElement("button");
  principal.className = "aa-btn aa-btn-primario aa-btn--grande";
  principal.type = "button";
  const mutar = document.createElement("button");
  mutar.className = "aa-btn aa-btn-secundario";
  mutar.type = "button";
  mutar.textContent = "Mutar microfone";
  acoes.append(principal, mutar);

  const avisos = document.createElement("div");
  avisos.className = "gravador__avisos";

  const faixas = { mic: medidor("mic"), system: medidor("system") };
  const medidores = document.createElement("div");
  medidores.className = "gravador__faixas";
  medidores.append(faixas.mic.raiz, faixas.system.raiz);

  cartao.append(linhaEstado, acoes, avisos, medidores);

  // ---- a reunião da agenda
  const reuniao = document.createElement("div");
  reuniao.className = "bloco";
  const tituloReuniao = document.createElement("h2");
  tituloReuniao.className = "bloco__titulo";
  const participantes = document.createElement("p");
  participantes.className = "bloco__texto";
  reuniao.append(tituloReuniao, participantes);

  // ---- dispositivos
  const blocoDisp = document.createElement("div");
  blocoDisp.className = "bloco";
  const tituloDisp = document.createElement("h2");
  tituloDisp.className = "bloco__titulo";
  tituloDisp.textContent = "Dispositivos";
  const notaDisp = document.createElement("p");
  notaDisp.className = "bloco__texto";
  // A trava não é limitação de implementação e sim o que preserva o valor das
  // duas faixas separadas: reabrir o stream no meio exigiria realinhá-las.
  notaDisp.textContent =
    "Não dá para trocar de dispositivo durante uma gravação: as duas faixas "
    + "começam alinhadas por terem começado juntas.";

  const escolhaMic = seletor("Microfone", "mic", dispositivos.entradas, dispositivos.mic_id);
  const escolhaLoop = seletor("Áudio do sistema", "loopback",
                              dispositivos.saidas, dispositivos.loopback_id);
  blocoDisp.append(tituloDisp, escolhaMic.raiz, escolhaLoop.raiz, notaDisp);

  // ---- pasta
  const blocoPasta = document.createElement("div");
  blocoPasta.className = "bloco";
  const tituloPasta = document.createElement("h2");
  tituloPasta.className = "bloco__titulo";
  tituloPasta.textContent = "Pasta das gravações";
  const caminho = document.createElement("p");
  caminho.className = "bloco__texto caminho";
  const dicaPasta = document.createElement("p");
  dicaPasta.className = "bloco__texto";
  dicaPasta.textContent = "Trocar em Ajustes › Geral. É a mesma pasta que a lista de reuniões lê.";
  blocoPasta.append(tituloPasta, caminho, dicaPasta);

  raiz.append(cartao, reuniao, blocoDisp, blocoPasta);
  tela.replaceChildren(raiz);

  // ─────────────────────────────────────────────────────── desenho

  function aplicar(g) {
    estado = g;

    ponto.dataset.cor = g.cor;
    tempo.textContent = g.gravando ? relogio(g.duracao_s) : "Parado";
    situacao.textContent = g.status;

    principal.textContent = g.gravando ? "Parar gravação" : "Iniciar gravação";
    principal.className = g.gravando
      ? "aa-btn aa-btn-secundario aa-btn--grande" : "aa-btn aa-btn-primario aa-btn--grande";
    mutar.textContent = g.mudo ? "Desmutar microfone" : "Mutar microfone";
    mutar.disabled = !g.gravando;

    // O aviso de mute prolongado. Mute esquecido é o modo de falha mais
    // provável desde que o clique no ícone passou a mutar em vez de parar — uma
    // gravação de 36 min saiu 95% muda exatamente assim.
    avisos.replaceChildren();
    if (g.mudo && g.mudo_ha_s >= 60)
      avisos.appendChild(alerta(
        `Microfone mudo há ${Math.floor(g.mudo_ha_s / 60)} min. Sua voz não está sendo gravada.`,
        "erro"));
    for (const f of g.faixas) {
      if (f.desconectado)
        avisos.appendChild(alerta(`O dispositivo de ${NOME_DA_FAIXA[f.nome] ?? f.nome} caiu.`, "erro"));
      if (f.falha)
        avisos.appendChild(alerta(`Falha ao gravar ${NOME_DA_FAIXA[f.nome] ?? f.nome}: ${f.falha}`, "erro"));
    }

    for (const [nome, m] of Object.entries(faixas)) {
      const f = g.faixas.find((x) => x.nome === nome);
      m.raiz.hidden = !g.gravando;
      if (!f) continue;
      m.preenchimento.style.width = `${porcentagem(f.nivel)}%`;
      m.dispositivo.textContent = f.mudo ? `${f.dispositivo} (mudo)` : f.dispositivo;
      // Sem áudio nenhum passados 45 s é o mesmo limiar do ícone amarelo. O
      // medidor já mostra a barra parada; o atributo é o que a torna vermelha,
      // porque uma barra parada e uma barra baixa se parecem demais.
      m.trilho.dataset.morto = String(!f.ja_ouviu && !f.mudo && g.duracao_s > 45);
    }
    medidores.hidden = !g.gravando;

    const temReuniao = Boolean(g.titulo);
    reuniao.hidden = !temReuniao;
    if (temReuniao) {
      tituloReuniao.textContent = g.titulo;
      const nomes = g.participantes ?? [];
      participantes.textContent = nomes.length
        ? `${nomes.length} participantes: ${nomes.join(", ")}`
        : "Sem participantes na agenda.";
    }

    escolhaMic.campo.disabled = g.gravando;
    escolhaLoop.campo.disabled = g.gravando;
    caminho.textContent = g.pasta;
  }

  // ───────────────────────────────────────────────────── interação

  async function chamar(op, campos = {}) {
    principal.disabled = true;
    try {
      const r = await pedir(op, campos);
      if (r.gravador) aplicar(r.gravador);
    } catch (e) {
      avisos.replaceChildren(alerta(e.message, "erro"));
    } finally {
      principal.disabled = false;
    }
  }

  principal.addEventListener("click", () =>
    chamar(estado.gravando ? "parar-gravacao" : "gravar"));
  mutar.addEventListener("click", () => chamar("mutar"));

  for (const [faixa, escolha] of [["mic", escolhaMic], ["loopback", escolhaLoop]])
    escolha.campo.addEventListener("change", () =>
      chamar("escolher-dispositivo", { faixa, dispositivo: escolha.campo.value }));

  // O núcleo empurra o estado a cada 200 ms enquanto grava, e a cada segundo
  // quando parado. A tela nunca pergunta em laço.
  const cancelar = assinar("gravador", (evento) => {
    // A tela saiu do DOM (o usuário mudou de destino): parar de desenhar e
    // largar a assinatura. Sem isto, um medidor de uma tela fechada continuaria
    // escrevendo em nós órfãos até o app fechar.
    if (!raiz.isConnected) { cancelar(); return; }
    aplicar(evento.gravador);
  });

  aplicar(estado);
}

/** Um select de dispositivo, com "Padrão do Windows" primeiro. */
function seletor(rotulo, faixa, lista, escolhido) {
  const label = document.createElement("label");
  label.className = "campo";

  const span = document.createElement("span");
  span.textContent = rotulo;

  const campo = document.createElement("select");
  campo.className = "aa-entrada";
  campo.id = `dispositivo-${faixa}`;

  // Primeiro e padrão porque seguir o dispositivo do sistema é o que a maioria
  // quer: fixar um headset específico quebra no dia em que ele é desconectado.
  const padrao = document.createElement("option");
  padrao.value = "";
  padrao.textContent = "Padrão do Windows";
  campo.appendChild(padrao);

  for (const d of lista) {
    const o = document.createElement("option");
    o.value = d.id;
    o.textContent = d.padrao ? `${d.nome}  (padrão)` : d.nome;
    campo.appendChild(o);
  }
  campo.value = escolhido ?? "";

  label.append(span, campo);
  return { raiz: label, campo };
}
