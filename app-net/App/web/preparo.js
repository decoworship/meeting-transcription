import { pedir } from "/ponte.js";
import { alerta, campo, secao, campoComSugestoes, preencherSugestoes, corDoFalante } from "/pecas.js";
import { transcrever as pedirTranscricao, assinarTranscricoes, emCurso,
         ultimoResultado, cancelar } from "/transcricoes.js";
import { blocoDeNotas } from "/notas.js";
import { duracao, quando, tituloDe, botaoApagarGravacao } from "/app.js";
import { campoDeEtiquetas } from "/etiquetas.js";
import { separarTermos, estadoDasEtapas, resumoDoMotor } from "/reuniao-regras.js";

// ───────────────────────────────────────────── preparar / transcrever

/**
 * A tela de antes da transcrição.
 *
 * Reproduz o formulário do app Python, mas já preenchido com o que a gravação
 * sabe de si: título e convidados vêm da agenda, e cliente/projeto vêm do
 * último uso. O que o usuário faz aqui é conferir, não digitar do zero.
 */
export async function telaDePreparo(g, { cabecalho, tela, navegar, aoTerminar }) {
  navegar();
  // A escolha do projeto, carregada com as preferências e repassada intacta.
  let modeloDeDiarizacao = null;

  // Enquanto isto for falso, nada é gravado no projeto.
  //
  // A tela monta com o vocabulário vazio e o motor nos padrões, e só depois
  // busca o que o projeto guardou. Sem esta trava, o que acontecesse no meio —
  // um blur na caixa, uma troca de modelo — gravava a tela recém-montada por
  // cima do projeto: vocabulário zerado, e com ele model_size, language,
  // diarization e diar_model. Silencioso, e o dono só descobre quando a
  // transcrição seguinte sai sem os nomes próprios.
  let prefsProntas = false;
  // Cada chamada a carregarPreferencias tira um número; só a carga cujo
  // número ainda é o mais alto quando a resposta chega é aplicada. Sem isto,
  // trocar de projeto duas vezes rápido deixava a resposta da troca de antes
  // — que chega depois, por não haver ordem garantida — pisar na de agora.
  let cargaDeProjeto = 0;

  cabecalho(tituloDe(g), `${duracao(g.duracao_s)} · ${quando(g.nome)}`, true);
  tela.replaceChildren();

  // O vínculo vem junto: cliente e projeto escolhidos antes sobrevivem a sair
  // da tela, porque moram em reuniao.json na pasta da gravação e não dentro da
  // transcrição — que, na tela de preparo, ainda não existe.
  const [{ clientes }, vinculo, leg] = await Promise.all([
    pedir("clientes"), pedir("reuniao", { gravacao: g.caminho }),
    // O que a legenda ao vivo deixou, se ela estava ligada. **Não é
    // transcrição** — é para conferir se o que foi dito está lá antes de
    // gastar a placa com a passada inteira.
    pedir("legenda-gravada", { gravacao: g.caminho }).catch(() => ({})),
  ]);

  // ---- o que a legenda ouviu, quando ouviu
  //
  // Vem antes de tudo porque é o que responde a pergunta desta tela: vale
  // transcrever? Quem leu e viu que está lá decide com informação; quem não
  // tinha legenda ligada não vê nada, e a tela é a de sempre.
  // Guardado em variável, e não posto na tela direto: o andamento tem de ser
  // o primeiro filho da tela quando a transcrição roda, e este bloco vem antes
  // dele na leitura — mas depois dele no DOM.
  let legendaGravada = null;
  const turnos = leg?.legenda_gravada;
  if (turnos?.length) {
    const b = document.createElement("details");
    b.className = "bloco legenda-gravada";
    const t = document.createElement("summary");
    t.className = "bloco__titulo";
    const palavras = turnos.reduce((n, x) => n + x.texto.trim().split(/\s+/).length, 0);
    t.textContent = `O que a legenda ouviu — ${palavras} palavras`;

    // **Enquanto os falantes não chegaram, a legenda está incompleta e diz
    // isso.** A diarização roda em segundo plano depois da reunião e leva ~2 min
    // numa de uma hora; quem abrir antes disso veria um rascunho sem quem falou
    // e não saberia se ele ficou assim ou ainda vai mudar.
    if (leg?.legenda_falantes === false) {
      const pendente = document.createElement("span");
      pendente.className = "aa-etiqueta legenda-gravada__pendente";
      pendente.textContent = "separando falantes…";
      t.appendChild(pendente);
    }
    b.appendChild(t);

    const aviso = document.createElement("p");
    aviso.className = "campo__dica";
    aviso.textContent = "Rascunho do que foi dito durante a reunião. A "
      + "transcrição abaixo é outra coisa: ela roda com o modelo inteiro, "
      + "separa quem falou e é a que fica.";
    b.appendChild(aviso);

    const corpo = document.createElement("div");
    corpo.className = "transcricao legenda-gravada__corpo";
    // A ordem em que os falantes aparecem decide a cor de cada um — a mesma
    // regra da revisão, para a mesma pessoa não trocar de cor entre as telas.
    const ordem = [...new Set(turnos.map((x) => x.falante).filter(Boolean))];
    for (const x of turnos) {
      const fala = document.createElement("div");
      fala.className = "fala";
      fala.dataset.dono = String(x.dono);

      // **Quem falou, quando a separação já rodou.** Antes de 18/09/2026 a
      // legenda só sabia "você" contra "os outros", e o lado da tela dizia
      // isso; agora, quando o falante existe, ele é dito com todas as letras.
      if (x.falante) {
        const quem = document.createElement("span");
        quem.className = "fala__falante";
        quem.style.color = corDoFalante(x.falante, ordem.indexOf(x.falante));
        quem.textContent = x.falante;
        fala.appendChild(quem);
      }

      const p = document.createElement("p");
      p.className = "fala__texto";
      p.textContent = x.texto.trim();
      fala.appendChild(p);
      corpo.appendChild(fala);
    }
    b.appendChild(corpo);
    legendaGravada = b;
  }

  const forma = document.createElement("div");
  forma.className = "secao preparo__formulario";

  // ---- reunião: cliente e projeto aceitam nome novo digitado
  const reuniao = secao("Reunião");
  const linha1 = document.createElement("div");
  linha1.className = "linha";
  linha1.append(
    campoComSugestoes("Cliente", "cliente", Object.keys(clientes), vinculo.cliente ?? ""),
    campoComSugestoes("Projeto", "projeto", clientes[vinculo.cliente] ?? [],
                      vinculo.projeto ?? ""),
    campo("Data", "input", { id: "data", tipo: "date", valor: dataDe(g.nome) }),
  );
  reuniao.appendChild(linha1);

  // ---- motor, dobrado: se mexe uma vez por projeto, e a linha fechada já diz
  // o que ele escolhe. Os ids ficam os mesmos, e com eles tudo o que os lê.
  const motor = document.createElement("details");
  motor.className = "bloco preparo__motor";
  const motorResumo = document.createElement("summary");
  motorResumo.className = "preparo__motor-resumo";
  const linha2 = document.createElement("div");
  linha2.className = "linha";
  linha2.append(
    campo("Modelo", "select", {
      id: "modelo",
      opcoes: ["large-v3", "medium", "small", "base", "tiny"],
    }),
    campo("Idioma", "input", { id: "idioma", valor: "pt" }),
    // Sim ou não, e nada de escolher o modelo de diarização: o "3.1" era opção
    // de tela que o pipeline ignorava — o motor sempre usou o community-1, que
    // a Fase 0 mediu 6,7 pontos de DER melhor. Oferecer o pior, e não aplicar
    // nem isso, era duas mentiras na mesma linha (FASE0-RESULTADOS).
    campo("Separar falantes", "select", { id: "diarizacao", opcoes: ["sim", "não"] }),
  );
  motor.append(motorResumo, linha2);

  const escolhaDoMotor = () => ({
    modelo: document.getElementById("modelo").value,
    idioma: document.getElementById("idioma").value,
    diarizar: document.getElementById("diarizacao").value === "sim",
  });
  const pintarMotor = () => { motorResumo.textContent = `Motor · ${resumoDoMotor(escolhaDoMotor())}`; };

  // ---- notas escritas durante a reunião
  //
  // Antes do vocabulário de propósito: é delas que saem os nomes próprios e as
  // siglas que o vocabulário quer, e lê-las primeiro é a ordem em que a pessoa
  // vai querer copiar.
  const blocoNotas = secao("Notas");
  blocoNotas.id = "painel-preparo-notas";
  const notas = blocoDeNotas(g.caminho, { linhas: 5, aoMudar: (t) => sugerirTermos(t) });
  blocoNotas.appendChild(notas.raiz);

  // ---- vocabulário
  const vocab = secao("Vocabulário");
  const termos = campoDeEtiquetas({
    id: "vocabulario", rotulo: "Termos do projeto",
    // Cada termo posto ou tirado grava no projeto — o equivalente do blur da
    // caixa de antes, que gravava ao sair dela.
    aoMudar: () => guardarVocabulario(),
  });
  const dica = document.createElement("p");
  dica.className = "campo__dica";
  // O aviso de 224 tokens do app antigo morreu de propósito: a correção
  // fonética a jusante recupera o termo mesmo quando o modelo erra a grafia,
  // então a lista não tem mais teto (FASE0 5-A).
  dica.textContent =
    "Nomes de pessoas, jargão, nomes de sistemas — Enter ou vírgula separam. "
    + "Sem limite de tamanho: o que o modelo escrever parecido é corrigido depois.";
  // Os termos que as notas revelaram, oferecidos um a um.
  //
  // Sugestão e não injeção: o nome vai para o vocabulário quando a pessoa
  // clica, porque quem escreveu a nota sabe o que é nome de sistema e o que é
  // a primeira palavra de uma frase (FASE3.md §3).
  const sugestoes = document.createElement("div");
  sugestoes.className = "sugestoes";
  vocab.append(termos.raiz, dica, sugestoes);

  function sugerirTermos(candidatos) {
    sugestoes.replaceChildren();

    const jaTem = new Set(separarTermos(termos.valor()).map((t) => t.toLocaleLowerCase("pt-BR")));
    const novos = candidatos.filter((t) => !jaTem.has(t.toLocaleLowerCase("pt-BR")));
    if (novos.length === 0) return;

    const rotulo = document.createElement("span");
    rotulo.className = "campo__dica";
    rotulo.textContent = "Das suas notas:";
    sugestoes.appendChild(rotulo);

    for (const termo of novos.slice(0, 12)) {
      const b = document.createElement("button");
      b.className = "aa-etiqueta sugestao";
      b.type = "button";
      b.textContent = `+ ${termo}`;
      b.title = "Acrescentar ao vocabulário";
      b.addEventListener("click", () => {
        termos.acrescentar(termo);
        b.remove();
        if (sugestoes.querySelectorAll(".sugestao").length === 0) sugestoes.replaceChildren();
      });
      sugestoes.appendChild(b);
    }
  }

  const acoes = document.createElement("div");
  acoes.className = "acoes";
  const botao = document.createElement("button");
  botao.className = "aa-btn aa-btn-primario aa-btn--grande";
  botao.type = "button";
  botao.textContent = "Transcrever";

  const aviso = document.createElement("span");
  aviso.className = "campo__dica";
  acoes.append(botao, aviso, botaoApagarGravacao(g));

  const andamento = document.createElement("div");
  andamento.className = "preparo__andamento";
  andamento.hidden = true;
  const resumo = document.createElement("p");
  resumo.className = "preparo__resumo";
  resumo.hidden = true;

  forma.append(reuniao, vocab, motor, acoes);
  // As notas ficam fora do formulário: não são mandadas ao motor, e anotar
  // enquanto a transcrição roda é uso normal — elas não se trancam com ele.
  tela.append(andamento, resumo, ...(legendaGravada ? [legendaGravada] : []), forma, blocoNotas);
  pintarMotor();

  // ---- ligações entre os campos
  const campoCliente = document.getElementById("cliente");
  const campoProjeto = document.getElementById("projeto");

  // O que foi mandado, numa linha: é o que o formulário diz enquanto não se
  // pode mais editá-lo.
  const linhaDeResumo = () => {
    const n = separarTermos(termos.valor()).length;
    return [
      [campoCliente.value.trim(), campoProjeto.value.trim()].filter(Boolean).join(" › ") || null,
      resumoDoMotor(escolhaDoMotor()),
      n ? `${n} ${n === 1 ? "termo" : "termos"}` : null,
    ].filter(Boolean).join(" · ");
  };
  const partes = { andamento, resumo, forma, linhaDeResumo };

  function atualizarProjetos() {
    const projetos = clientes[campoCliente.value] ?? [];
    preencherSugestoes(document.getElementById("projeto-lista"), projetos);
    aviso.textContent = clientes[campoCliente.value]
      ? "" : campoCliente.value ? "cliente novo — será criado ao transcrever" : "";
  }

  /** Ao escolher um projeto conhecido, suas preferências voltam. */
  async function carregarPreferencias() {
    if (!campoCliente.value || !campoProjeto.value) return;
    const minhaCarga = ++cargaDeProjeto;
    const { prefs } = await pedir("prefs", {
      cliente: campoCliente.value,
      projeto: campoProjeto.value,
    });

    // Uma troca de projeto mais nova já começou enquanto esta resposta vinha:
    // aplicá-la agora pisaria no que a carga mais nova está fazendo (ou já
    // fez). Só a última pedida vale.
    if (minhaCarga !== cargaDeProjeto) return;

    // A tela pode ter sido trocada enquanto a resposta vinha — escolher o
    // projeto e sair no mesmo segundo é um caminho normal. Sem esta guarda, o
    // preenchimento cai num getElementById que devolve null e o erro sobe no
    // console sem ninguém ver.
    if (!campoCliente.isConnected) return;

    // Zerado a cada troca de projeto, antes de qualquer saída: sem isto, ir de
    // um projeto que escolheu um pipeline para um projeto novo levava a escolha
    // do primeiro junto, e a gravação a carimbava no segundo.
    modeloDeDiarizacao = null;

    if (!prefs) {
      aviso.textContent = "projeto novo — será criado ao transcrever";
      // Sem isto, o vocabulário do projeto anterior ficava nos campos do
      // projeto novo, e a próxima etiqueta posta ou tirada gravava esses
      // termos velhos nele.
      termos.definir("");
      pintarMotor();
      return;
    }
    aviso.textContent = "preferências do projeto carregadas";
    if (prefs.model_size) document.getElementById("modelo").value = prefs.model_size;
    if (prefs.language) document.getElementById("idioma").value = prefs.language;
    document.getElementById("diarizacao").value = prefs.diarization === false ? "não" : "sim";
    termos.definir(prefs.initial_prompt ?? "");
    pintarMotor();
    // Qual pipeline separa os falantes não tem campo nesta tela — escolhe-se em
    // Ajustes › Clientes, por projeto, e aqui ele só viaja. Guardá-lo é o que
    // impede as duas gravações abaixo de o substituírem por uma constante.
    modeloDeDiarizacao = prefs.diar_model ?? null;
  }

  /**
   * Guarda o vínculo assim que ele muda.
   *
   * Na hora da escolha, e não só ao transcrever: quem escolhe o projeto e sai
   * da tela — para ouvir um trecho, para conferir outra reunião — voltava e
   * encontrava os campos vazios.
   */
  async function guardarVinculo() {
    try {
      await pedir("salvar-reuniao", {
        gravacao: g.caminho,
        cliente: campoCliente.value.trim(),
        projeto: campoProjeto.value.trim(),
      });
    } catch {
      // Não vale interromper o preparo por causa disto: o vínculo é gravado de
      // novo ao transcrever, que é quando ele passa a importar de verdade.
    }
  }

  /**
   * Guarda o vocabulário no projeto, que é de quem ele é.
   *
   * O vocabulário é preferência de cliente/projeto, não desta reunião: os nomes
   * e siglas de um projeto valem para todas as reuniões dele. Até 14/08 só era
   * gravado ao clicar em Transcrever — quem digitava um termo e saía da tela
   * perdia o que escreveu, e a tela seguinte já mostrava o vocabulário velho.
   */
  async function guardarVocabulario() {
    const cliente = campoCliente.value.trim();
    const projeto = campoProjeto.value.trim();
    // Sem projeto não há onde guardar: o vocabulário mora no par, e inventar um
    // projeto "sem nome" só para ter onde salvar criaria lixo no cadastro.
    if (!cliente || !projeto) return;

    // A tela pode ter saído.
    if (!termos.raiz.isConnected) return;
    if (!prefsProntas) return;

    try {
      await pedir("salvar-projeto", {
        cliente, projeto,
        prefs: {
          language: document.getElementById("idioma").value.trim(),
          model_size: document.getElementById("modelo").value,
          engine: "faster-whisper",
          diarization: document.getElementById("diarizacao").value === "sim",
          diar_model: modeloDeDiarizacao,
          condition_on_previous_text: false,
          initial_prompt: termos.valor(),
        },
      });
    } catch {
      // O vocabulário é gravado de novo ao transcrever; falhar aqui não pode
      // interromper quem está preparando a reunião.
    }
  }

  /**
   * Lê as preferências e libera a gravação, aconteça o que acontecer na leitura.
   *
   * O `finally` é o que impede a trava de virar um travamento: se o pedido
   * falhar, a tela continua funcionando como funcionava antes — o pior caso
   * volta a ser o de hoje, e não uma tela que não guarda mais nada.
   */
  async function carregarEliberar() {
    // Destranca de novo a cada chamada, não só na primeira: sem isto, trocar
    // de projeto depois da carga inicial já feita deixava a trava aberta o
    // tempo todo, e uma etiqueta posta ou tirada no meio da troca gravava o
    // vocabulário do projeto ANTIGO nos campos do projeto NOVO.
    prefsProntas = false;
    const antes = cargaDeProjeto;
    try { await carregarPreferencias(); } finally {
      // Só destranca se, entre o início e o fim desta chamada, nenhuma OUTRA
      // troca de projeto começou (o número subiu mais de um passo) — essa
      // outra tem a própria chamada, e a própria trava, para destrancar.
      if (cargaDeProjeto <= antes + 1) prefsProntas = true;
    }
  }

  campoCliente.addEventListener("change", () => {
    atualizarProjetos(); carregarEliberar(); guardarVinculo();
  });
  campoCliente.addEventListener("input", atualizarProjetos);
  campoProjeto.addEventListener("change", () => { carregarEliberar(); guardarVinculo(); });
  // O blur é a rede: grava de novo quando o campo perde o foco, inclusive nos
  // caminhos em que o change não chega a disparar. Gravar duas vezes o mesmo
  // valor não custa nada — são dois campos num JSON pequeno.
  campoCliente.addEventListener("blur", guardarVinculo);
  campoProjeto.addEventListener("blur", guardarVinculo);

  // As preferências do motor seguem o mesmo caminho do vínculo: gravam ao
  // sair do campo, e não só ao transcrever. O vocabulário grava a cada termo
  // posto ou tirado, pelo `aoMudar` do campo de etiquetas.
  for (const id of ["modelo", "idioma", "diarizacao"])
    document.getElementById(id).addEventListener("change", () => { guardarVocabulario(); pintarMotor(); });

  // As preferências do projeto que já veio vinculado, lidas na montagem.
  //
  // **É o conserto do defeito que apagava o vocabulário.** Cliente e projeto
  // chegam preenchidos de reuniao.json, mas valor posto por código não dispara
  // `change` — então `carregarPreferencias` nunca corria para quem só abria uma
  // gravação já vinculada, e a tela ficava mostrando vazio o que o disco tinha
  // cheio. A primeira interação gravava esse vazio por cima.
  //
  // Sem vínculo não há o que ler, e a trava abre na hora: uma gravação nova não
  // tem projeto a proteger.
  const preferenciasProntas =
    vinculo.cliente && vinculo.projeto ? carregarEliberar() : Promise.resolve();
  if (!vinculo.cliente || !vinculo.projeto) prefsProntas = true;

  // O modelo de diarização vai por parâmetro, e não por variável de módulo:
  // `transcrever` é irmã de `telaDePreparo`, não aninhada nela, e ler a
  // variável da outra dava "modeloDeDiarizacao is not defined" no clique de
  // transcrever — com a tela já montada e tudo o mais funcionando.
  //
  // O clique espera a leitura pelo mesmo motivo da trava: transcrever grava as
  // mesmas preferências, e quem clicasse na montagem entraria pela outra porta
  // do mesmo defeito — com o agravante de mandar o vocabulário vazio ao motor.
  botao.addEventListener("click", async () => {
    await preferenciasProntas;
    transcrever(g, botao, partes, modeloDeDiarizacao, termos.valor(), aoTerminar);
  });

  await preferenciasProntas;

  // Reencontrar uma transcrição já em curso é o motivo de esta tela existir do
  // jeito que existe: quem saiu no meio e voltou cai aqui, e o que ele precisa
  // ver é a barra onde ela está — não um botão "Transcrever" que começaria tudo
  // de novo. O erro da última tentativa aparece pelo mesmo caminho.
  if (emCurso(g.caminho)) acompanhar(g, botao, partes, aoTerminar);
  else if (ultimoResultado(g.caminho)?.erro)
    mostrarFim(partes, alerta(ultimoResultado(g.caminho).erro, "erro"));
}

function dataDe(nome) {
  const m = nome.match(/^(\d{4}-\d{2}-\d{2})/);
  return m ? m[1] : "";
}

/** Transcrevendo: o andamento no topo, e o formulário vira uma linha. */
function trancar({ andamento, resumo, forma, linhaDeResumo }) {
  resumo.textContent = linhaDeResumo();
  resumo.hidden = false;
  forma.hidden = true;
  andamento.hidden = false;
}

/** Parada ou com erro: o recado fica no topo, onde se olhava, e o formulário volta. */
function mostrarFim({ andamento, resumo, forma }, ...nos) {
  andamento.replaceChildren(...nos);
  andamento.hidden = nos.length === 0;
  resumo.hidden = true;
  forma.hidden = false;
}

/**
 * Desenha a transcrição desta gravação enquanto ela roda, esteja ela recém
 * pedida ou já a meio caminho quando esta tela montou.
 *
 * O andamento vem do registro do núcleo, e não de uma promessa: é o que permite
 * sair da tela e voltar sem perder a barra, e é o que faz o resultado chegar
 * mesmo que ninguém estivesse olhando quando ele ficou pronto.
 */
function acompanhar(g, botao, partes, aoTerminar) {
  const { andamento } = partes;
  botao.disabled = true;
  botao.textContent = "Transcrevendo…";

  const etapas = document.createElement("ol");
  etapas.className = "preparo__etapas";

  const barra = document.createElement("div");
  barra.className = "aa-progresso";
  const preenchimento = document.createElement("div");
  barra.appendChild(preenchimento);

  const estado = document.createElement("p");
  estado.className = "campo__dica";
  estado.textContent = "preparando…";

  // Parar mora no andamento, e não entre os botões do formulário: é a ação de
  // quem está olhando a transcrição correr e mudou de ideia. Some junto com ela.
  const parar = document.createElement("button");
  parar.className = "aa-btn aa-btn-texto";
  parar.type = "button";
  parar.textContent = "Parar transcrição";
  parar.addEventListener("click", async () => {
    parar.disabled = true;
    parar.textContent = "parando…";
    try {
      await cancelar(g.caminho);
    } catch (e) {
      parar.disabled = false;
      parar.textContent = "Parar transcrição";
      andamento.appendChild(alerta(e.message, "erro"));
    }
  });

  const linha = document.createElement("div");
  linha.className = "progresso__linha";
  linha.append(estado, parar);
  andamento.replaceChildren(etapas, barra, linha);
  trancar(partes);

  function pintar(t) {
    etapas.replaceChildren(...estadoDasEtapas(t.etapa).map((e) => {
      const li = document.createElement("li");
      li.dataset.estado = e.estado;
      if (e.estado === "atual") li.setAttribute("aria-current", "step");
      li.textContent = e.rotulo;
      return li;
    }));
    estado.textContent = t.texto || "";
    preenchimento.style.width = `${t.fracao >= 0 ? Math.round(t.fracao * 100) : 0}%`;
  }
  pintar({ etapa: null, texto: "preparando…", fracao: 0 });

  const atual = emCurso(g.caminho);
  if (atual) pintar(atual);

  const cancelarAssinatura = assinarTranscricoes(() => {
    // A tela saiu do documento (trocou-se de destino): largar a assinatura e
    // deixar o trabalho seguir. Quem voltar a esta gravação monta outra.
    if (!andamento.isConnected) { cancelarAssinatura(); return; }

    const rodando = emCurso(g.caminho);
    if (rodando) { pintar(rodando); return; }

    const fim = ultimoResultado(g.caminho);
    if (!fim) return;              // é outra gravação que mudou de estado

    cancelarAssinatura();
    if (fim.cancelada) {
      // Parou a pedido: o app obedeceu, então nada de alerta vermelho. O botão
      // volta a convidar, porque recomeçar é o próximo passo provável.
      botao.disabled = false;
      botao.textContent = "Transcrever";
      const nota = document.createElement("p");
      nota.className = "campo__dica";
      nota.textContent = "Transcrição interrompida. A placa foi liberada.";
      mostrarFim(partes, nota);
      return;
    }
    if (fim.erro) {
      botao.disabled = false;
      botao.textContent = "Tentar de novo";
      mostrarFim(partes, alerta(fim.erro, "erro"));
      return;
    }
    aoTerminar(g);
  });
}

/**
 * @param modeloDeDiarizacao o que o projeto escolheu, ou null para o padrão do
 *   app. Vem de fora porque quem o carrega é a tela de preparo.
 * @param vocabulario o valor do campo de etiquetas, no formato de sempre.
 *   Vem de fora pelo mesmo motivo: `transcrever` é irmã de `telaDePreparo`,
 *   não aninhada nela, e não enxerga o campo.
 */
async function transcrever(g, botao, partes, modeloDeDiarizacao = null, vocabulario, aoTerminar) {
  botao.disabled = true;

  try {
    const diar = document.getElementById("diarizacao").value;

    // Guardar antes de transcrever, e não depois: se a transcrição falhar, o
    // que foi digitado aqui não pode se perder junto.
    const cliente = document.getElementById("cliente").value.trim();
    const projeto = document.getElementById("projeto").value.trim();
    if (cliente && projeto) {
      await pedir("salvar-projeto", {
        cliente, projeto,
        prefs: {
          language: document.getElementById("idioma").value.trim(),
          model_size: document.getElementById("modelo").value,
          engine: "faster-whisper",
          diarization: diar === "sim",
          // Repassado como veio. Escrever uma constante aqui apagava, a cada
          // transcrição, o que a pessoa tivesse escolhido em Ajustes › Clientes
          // — e não se notava, porque o valor era ignorado adiante de qualquer
          // jeito. Ver FASE6 §4.6.
          diar_model: modeloDeDiarizacao,
          condition_on_previous_text: false,
          initial_prompt: vocabulario,
        },
      });
    }

    // Volta na hora: daqui em diante o trabalho é do núcleo, e a tela passa a
    // desenhar o que ele empurra. Recusa quando já há outra transcrição em
    // curso, e a mensagem nomeia qual.
    await pedirTranscricao({
      gravacao: g.caminho,
      vocabulario,
      // Sem estes dois, escolher modelo e idioma na tela não tinha efeito
      // nenhum: o motor caía no padrão e detectava o idioma sozinho.
      idioma: document.getElementById("idioma").value.trim(),
      modelo: document.getElementById("modelo").value,
      // Sem isto a escolha de separar falantes era colhida na tela, salva nas
      // preferências do projeto e ignorada pelo pipeline.
      diarizar: diar === "sim",
      // E qual pipeline separa. Sem isto o valor chegava até o disco e parava
      // ali: o motor pedia o community-1 pelo nome, sempre.
      diar_model: modeloDeDiarizacao,
      // Guardados com a transcrição: sem isto o cabeçalho do arquivo exportado
      // saía sem dizer de que cliente e projeto era a reunião.
      cliente: document.getElementById("cliente").value.trim(),
      projeto: document.getElementById("projeto").value.trim(),
    });
    acompanhar(g, botao, partes, aoTerminar);
  } catch (e) {
    botao.disabled = false;
    botao.textContent = "Tentar de novo";
    mostrarFim(partes, alerta(e.message, "erro"));
  }
}
