// As bolinhas do trilho: o que está acontecendo, visto de qualquer tela.
//
// Uma por destino, e cada uma acesa pelo trabalho que pertence àquele destino:
//
//   Gravador  — gravando
//   Reuniões  — transcrevendo, separando falantes, escrevendo a ata
//
// A ata acendia a bolinha de Atas até 24/09/2026, quando Atas deixou de ser
// destino e virou uma aba da reunião. O rótulo da bolinha diz qual das três
// tarefas está rodando — foi a confusão entre elas o defeito de 14/08.
//
// Existem porque este app faz coisas que levam minutos e o usuário sai da tela
// enquanto elas rodam. Antes da Fase 3 nada disso aparecia fora da tela em que
// tinha começado; depois dela, a transcrição aparecia — mas a ata acendia a
// bolinha de Reuniões, porque as duas dividem o mesmo registro e a tela não
// sabia distinguir. Foi o defeito que o dono do produto viu no primeiro uso.
//
// **Gravar não disputa nada com os motores** (capturar áudio não usa GPU), então
// a bolinha do Gravador pode conviver com a de Reuniões. As tarefas de
// Reuniões nunca rodam juntas — o núcleo recusa, porque os modelos não cabem
// juntos na placa e porque a ata precisa da transcrição pronta.

import { pedir, assinar } from "/ponte.js";
import { assinarTranscricoes, transcricoes } from "/transcricoes.js";

const DESTINO_DA_TAREFA = {
  transcricao: "ir-reunioes",
  // A separação de falantes acende a mesma bolinha da transcrição: ela roda
  // sobre a legenda de uma reunião, e é em Reuniões que se vai olhar.
  falantes: "ir-reunioes",
  ata: "ir-reunioes",
};

const ROTULO_DA_TAREFA = {
  transcricao: "transcrevendo",
  falantes: "separando os falantes de",
  ata: "escrevendo a ata de",
};

function acender(id, ligado, rotuloBase, oQue = "") {
  const botao = document.getElementById(id);
  if (!botao) return;

  botao.dataset.ocupado = String(ligado);
  if (ligado) botao.setAttribute("aria-label", `${rotuloBase} — ${oQue}`);
  else botao.removeAttribute("aria-label");
}

/**
 * As tarefas de placa acendem todas a bolinha de Reuniões, e só uma roda por
 * vez — o núcleo recusa a segunda.
 *
 * **Por destino, e não por tarefa.** Transcrição e falantes acendem a MESMA
 * bolinha, e pintar tarefa a tarefa apagava na segunda o que a primeira
 * acendera: de 17/09 a 24/09/2026 a bolinha de Reuniões não acendia com uma
 * transcrição rodando. O tools/checar_transcricao.py pegou, depois de portado
 * para a lista nova.
 */
function pintarTrabalhos() {
  const atual = transcricoes().atual;
  for (const id of new Set(Object.values(DESTINO_DA_TAREFA))) {
    const minha = DESTINO_DA_TAREFA[atual?.tarefa] === id;
    acender(id, minha, "Reuniões",
            minha ? `${ROTULO_DA_TAREFA[atual.tarefa]} ${atual.nome}` : "");
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
