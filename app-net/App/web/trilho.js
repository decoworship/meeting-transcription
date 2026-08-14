// As bolinhas do trilho: o que está acontecendo, visto de qualquer tela.
//
// Uma por destino, e cada uma acesa pelo trabalho que pertence àquele destino:
//
//   Reuniões  — transcrevendo
//   Gravador  — gravando
//   Atas      — escrevendo a ata
//
// Existem porque este app faz coisas que levam minutos e o usuário sai da tela
// enquanto elas rodam. Antes da Fase 3 nada disso aparecia fora da tela em que
// tinha começado; depois dela, a transcrição aparecia — mas a ata acendia a
// bolinha de Reuniões, porque as duas dividem o mesmo registro e a tela não
// sabia distinguir. Foi o defeito que o dono do produto viu no primeiro uso.
//
// **Gravar não disputa nada com os motores** (capturar áudio não usa GPU), então
// a bolinha do Gravador pode conviver com qualquer uma das outras duas. As
// outras duas nunca convivem entre si — o núcleo recusa, porque os modelos não
// cabem juntos na placa e porque a ata precisa da transcrição pronta.

import { pedir, assinar } from "/ponte.js";
import { assinarTranscricoes, transcricoes } from "/transcricoes.js";

const DESTINO_DA_TAREFA = {
  transcricao: "ir-reunioes",
  ata: "ir-atas",
};

const ROTULO_DA_TAREFA = {
  transcricao: "transcrevendo",
  ata: "escrevendo a ata de",
};

function acender(id, ligado, rotuloBase, oQue = "") {
  const botao = document.getElementById(id);
  if (!botao) return;

  botao.dataset.ocupado = String(ligado);
  if (ligado) botao.setAttribute("aria-label", `${rotuloBase} — ${oQue}`);
  else botao.removeAttribute("aria-label");
}

/** Reuniões e Atas: só uma das duas acende, porque só uma das duas roda. */
function pintarTrabalhos() {
  const atual = transcricoes().atual;
  for (const [tarefa, id] of Object.entries(DESTINO_DA_TAREFA)) {
    const minha = atual?.tarefa === tarefa;
    acender(id, minha, id === "ir-atas" ? "Atas" : "Reuniões",
            minha ? `${ROTULO_DA_TAREFA[tarefa]} ${atual.nome}` : "");
  }
}

function pintarGravador(g) {
  acender("ir-gravador", Boolean(g?.gravando), "Gravador",
          g?.mudo ? "gravando, microfone mudo" : "gravando");
}

/**
 * Liga as bolinhas aos dois canais de evento.
 *
 * O estado do gravador é pedido uma vez na subida: ele só é empurrado quando
 * muda ou enquanto grava, e uma janela reaberta no meio de uma gravação ficaria
 * sem bolinha até a próxima mudança.
 */
export function ligarBolinhas() {
  assinarTranscricoes(pintarTrabalhos);
  pintarTrabalhos();

  assinar("gravador", (evento) => pintarGravador(evento.gravador));
  pedir("gravador").then((r) => pintarGravador(r.gravador)).catch(() => {});
}
