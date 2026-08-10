# Fase 2 — o app nativo com UI completa: carta de execução

Registro da direção decidida ao fim da Fase 1, para a fase não depender de
nenhuma conversa. Contexto: [FASE1-HANDOFF.md](FASE1-HANDOFF.md) (o que resta
da Fase 1), [FASE0-RESULTADOS.md](FASE0-RESULTADOS.md) (as decisões de motor).

## A fusão que define a fase

As Fases 2 e 3 do [PLANO.md](PLANO.md) viraram uma só. A Fase 0 decidiu que os
motores ficam em Python na v1 (faster-whisper pelo `hotwords`… na verdade pelo
conjunto: ASR condicionado ao teste de segmentação; diarização pelos 6,7
pontos de DER) — então "migrar o motor" deixou de ser uma fase. O que resta
dela, o **contrato do sidecar**, é exatamente o que a UI nova precisa.

**A fase entrega: a casca nativa Windows com a UI completa, falando com os
motores Python existentes por um contrato de processo.**

## Arquitetura

```
MeetingApp.exe (C#, WebView2)          motores (pastas auto-contidas)
  UI: AA Design System, React    ◄──►    asr/         faster-whisper (Python embutido)
  núcleo: projetos, histórico,   stdio   diarizacao/  pyannote community-1 (idem)
  vozes, exportação, correção
  fonética, filtro de silêncio
                                         (futuro: whisper.cpp, resumo LLM)
MeetingRecorder.exe (bandeja)  — SEPARADO, se comunica por arquivos
```

Princípios já decididos, com onde está o porquê:

1. **O gravador continua executável separado.** A confiabilidade da gravação
   não depende do processo da UI. Encontro pelos arquivos
   (`recordings/` + `meta.json`), como hoje.
2. **Sidecar por stdio com protocolo por linha, não HTTP** (PLANO §5,
   "Correção: stdin/stdout"): sem porta, sem diálogo de firewall, morte
   detectável por pipe fechado. `CREATE_NO_WINDOW` em todo spawn.
3. **Cancelamento real = matar o processo do motor.** É o conserto do C8
   cosmético da AUDITORIA §1.5, e vem de graça do desenho.
4. **Motores escolhidos por qualidade, não por stack** (decisão da Fase 0):
   `asr/` = faster-whisper com `word_timestamps` (o risco 3 não existe neste
   arranjo); `diarizacao/` = pyannote `community-1`. Empacotados com
   python-embeddable + venv congelado, cada um numa pasta com manifesto
   mínimo (nome, versão, comando). **Sem sistema de plugins ainda** — dois
   motores hardcoded atrás de uma interface; manifesto/download/registry só
   quando o terceiro motor (resumo) chegar.
5. **No núcleo (C#), não nos motores**: correção fonética
   (`tools/correcao_fonetica.py` é a referência; guarda de capitalização +
   hunspell pt-BR + trocas visíveis na UI), filtro de segmentos sobre
   silêncio digital (FASE0 resultado 6-A), e o merge das duas faixas.

## Especificação funcional

**O [FEATURES.md](FEATURES.md) é a especificação: 53 entregas, 27 críticas.**
A regra de ouro de lá: E1–E5 (ler colorido, clicar-e-ouvir, corrigir no lugar,
trocar falante, buscar) são interações, não telas — a UI nova precisa entregar
as cinco ou regride. A divisão núcleo/motor segue a leitura do próprio
FEATURES: núcleo é dono de A, B, E, F, G e de D2/D3/D5/D6/D7; motores entregam
C1–C3, C6 e D1.

UI: AA Design System, agora sem o Gradio no caminho (a fase 3 do redesign vira
a via normal — PLANO §3). Idioma: pt-BR, seguindo o design system.

> **Desvio da carta, decidido em 10/08/2026: sem React.** A carta previa "os
> componentes React do design system". Eles **não existem** — o que o design
> system tem, e o que está copiado em `src/web/assets/ds/`, é CSS puro: tokens,
> fontes auto-hospedadas e uma biblioteca de classes (`.aa-btn`, `.aa-cartao`,
> `.aa-abas`, `.aa-alerta`, `.aa-etiqueta`…) que cobre a interface inteira.
>
> Sem componentes prontos para reaproveitar, React entraria só como
> infraestrutura: um build de node no caminho, mais peças no instalador da
> Fase 4, e um passo a mais entre editar e ver. A UI é HTML + as classes do
> design system + JavaScript de módulo, servido de arquivos embutidos no
> executável. Zero build, e o CSP do WebView2 fica trivial porque nada vem de
> fora.
>
> O que se perde é estado declarativo na lista de segmentos, que é a tela mais
> complexa (pode passar de mil itens, com edição no lugar). A mitigação é
> renderização por delegação de evento e atualização pontual do nó editado —
> se isso apertar, trocar por um framework depois é local, porque o CSS e o
> contrato com o núcleo não mudam.

## Dados e migração

- `history/`, `projects.json`, `config.json`: migram como estão (JSON
  portável), mas os `audio_path` de `/tmp` estão mortos (AUDITORIA §1.6) — o
  áudio processado passa a morar em diretório de dados gerenciado, referência
  relativa.
- `voices.json`: **não migra** — reinscrição com o modelo de dados da
  [VOZES.md](VOZES.md) §1 (amostra com procedência + snippet). É a única
  chance barata de fazer isso.
- Vocabulário: liberto do orçamento de 224 tokens pela correção a jusante
  (FASE0 5-A) — lista por projeto pode ser ilimitada; o aviso de orçamento da
  UI atual morre.

## Critérios de aceite

- **A.** As 27 entregas críticas do FEATURES.md funcionando de ponta a ponta
  numa gravação real de duas faixas — mesmo resultado (ou melhor) que o app
  Gradio na mesma gravação.
- **B.** Matar o app no meio de uma transcrição não deixa processo de motor
  órfão; cancelar libera a GPU em ≤2 s.
- **C.** Motor que morre no meio devolve erro legível na UI e o app continua
  vivo (a gravação nunca esteve no mesmo processo, por desenho).
- **D.** As trocas da correção fonética visíveis e inspecionáveis na UI.
- **E.** O Docker/Gradio aposentado — mesma régua da Fase 1: o velho sai de
  uso porque o novo é comprovadamente igual ou melhor.

## Fora da fase

Instalador e assinatura (Fase 4 continua existindo), motores nativos
(whisper.cpp aguarda o teste de segmentação — trilha do agente de
benchmarks), resumo por LLM, Teams, transcrição ao vivo, Linux/Mac.

## Ordem de trabalho sugerida

1. ~~**Contrato do sidecar primeiro, com o motor de diarização**~~ — **FEITO
   (08/08/2026)**. Especificação em [SIDECAR.md](SIDECAR.md); cliente em
   `app-net/Sidecar/`, motor em `motores/diarizacao/motor.py`, validação por
   CLI em `app-net/Cli/`. Ver §"O que o sidecar já provou" abaixo.
2. **Pipeline inteiro por CLI** — **FEITO (09/08/2026)**: gravação → mix → ASR →
   diarização → `TranscriptionResult` JSON idêntico ao de hoje, com a paridade
   medida (ver abaixo). `Sidecar.exe --gravacao <pasta>`.
   **Falta o empacotamento** dos motores como pastas auto-contidas
   (python-embeddable) — hoje eles rodam do venv de desenvolvimento.

   > **Inversão de ordem, registrada.** A carta pedia empacotar *e* provar no
   > mesmo passo. Provar primeiro é mais barato e mais informativo: empacotar
   > antes congelaria um contrato ainda não exercitado, e os ~2,5 GB de
   > download do python-embeddable com torch não mudariam nada do desenho. A
   > troca de ordem custa zero e a informação veio antes.
3. UI por cima, na ordem do fluxo do usuário: escolher gravação → rodar →
   ler/corrigir (E1–E5) → exportar. Histórico, vozes e projetos depois do
   fluxo principal.
   **Esqueleto em pé (10/08/2026)**: janela Win32 crua hospedando o WebView2,
   conteúdo servido de dentro do executável, ponte JSON com o núcleo e a
   primeira tela — a lista de gravações — funcionando sobre o acervo real
   (29 gravações, com os avisos de faixa muda lidos do `meta.json`). Falta o
   resto do fluxo.
4. **Correção fonética + filtro de silêncio no núcleo** — **FEITO
   (09/08/2026)**, em `app-net/Nucleo/`. Ligados por opção no CLI
   (`--vocabulario`, `--filtrar-silencio`) e **desligados por padrão**: os dois
   mudam o texto, e a paridade com o Gradio só é medível enquanto a saída for
   comparável à dele.

   Não havia testes no `tools/` para portar — o que existe lá são **decisões
   medidas, registradas em prosa**: a regra de "remover vogal final" que fazia
   `fixo`→`Fixa`, o teto de distância que não pode apertar sem matar o caso
   `Jimmy`→`Dimi`. Os testes em C# prendem exatamente esses casos, para ninguém
   "melhorar" o corretor de volta ao que já se provou pior.

   > **Resultado negativo do filtro de silêncio, medido.** Rodado sobre
   > `2026-08-06_09-03-05` — a mesma gravação do resultado 6-A, e a única do
   > acervo com silêncio digital em quantidade (32,2% no `system`, 25,5% no
   > mix) — ele descartou **1 segmento de 194** ("Eu acho que tem"). Os ~5% de
   > palavras inventadas que a FASE0 mediu **não estão em segmentos inteiros
   > sobre zeros**: estão espalhadas dentro de segmentos que também cobrem
   > fala, e a FASE0 as contava rateando por tempo. Um filtro que decide por
   > segmento inteiro não alcança o fenômeno, e afrouxar o limiar de 2/3 para
   > alcançá-lo removeria fala verdadeira junto.
   >
   > A via certa é **filtrar por palavra**, e ela já está meio aberta: o ASR
   > roda com `word_timestamps=True` e o protocolo simplesmente não carrega as
   > palavras ainda. Fica registrado como o próximo passo desta frente — com o
   > filtro atual mantido, porque o segmento inteiramente sobre zeros que ele
   > pega é invenção pura.
5. Paridade final (critério A) e aposentadoria do Docker.

## O que o sidecar já provou (08/08/2026)

Medido com o motor **real** (pyannote `community-1` na RTX 2060), não com
simulação:

- **Ponta a ponta**: 121 s de gravação real → 28 segmentos, 2 falantes, em
  16,9 s. O handshake sai em ~90 ms.
- **Critério B, a parte do cancelamento**: pedido o cancelamento no meio da
  inferência, o processo morre em **70 ms** e a VRAM cai de 3766 MiB para os
  941 MiB de linha de base em **≤0,3 s** — contra o orçamento de 2 s. Medido
  por amostragem de `nvidia-smi` a cada 200 ms, e nenhum processo de motor
  sobra. Vem de graça do desenho: cancelar é matar.
- **Critério C**: motor que morre no meio, motor que recusa a requisição e
  motor que nem sobe viram três mensagens diferentes e legíveis, sem derrubar o
  cliente. Sete testes cobrem isso contra um motor falso — protocolo e ciclo de
  vida não deviam depender de um modelo de 2 GB para serem verificados.

### Paridade do pipeline com o app Gradio (09/08/2026)

`tools/comparar_pipeline.py` roda o pipeline Python de hoje sobre a mesma
gravação e compara com o JSON do `Sidecar.exe --gravacao`. Em
`2026-08-07_15-39-58` (121 s, duas pessoas):

| | 121 s, 2 pessoas | 995 s, reunião cheia |
|---|---|---|
| mix das faixas | **idêntico**, 3.871.516 B | **idêntico**, 31.856.152 B |
| idioma, duração | `pt`, 120,98 s | `pt`, 995,50 s |
| segmentos | 44 x 44, **texto idêntico** | 311 x 311, **texto idêntico** |
| pior diferença de tempo | **0,0 ms** | **0,0 ms** |
| falantes / atribuição | 3 x 3, zero divergência | 4 x 4, zero divergência |
| segmentos seus | 32 x 32 | 0 x 0 |

**PARIDADE nas duas.** O mix idêntico é o que dá peso ao resto: com a mesma
entrada e os mesmos parâmetros, texto igual deixa de ser coincidência. Custo do
lado novo na gravação longa: ASR 112,8 s + diarização 60,3 s para 16,6 min de
áudio.

### Três defeitos do porte que só a comparação pegou

Nenhum apareceria em teste unitário — os testes tinham sido escritos com o
mesmo entendimento errado do original. É o argumento inteiro para comparar
contra o sistema antigo em vez de contra a própria expectativa.

1. **Rótulo cru vazando.** O C# devolvia `SPEAKER_00` onde o app dizia
   `Speaker 1`. O protocolo manda o rótulo cru de propósito e nomear é do
   núcleo — faltava o núcleo fazer a parte dele.
2. **Falante pelo maior trecho, não pela soma.** O original agrupa as
   sobreposições por pessoa e soma; o porte pegava o maior trecho isolado. Num
   segmento que atravessa uma troca de turno, quem fala três vezes por 1 s
   domina quem falou uma vez por 2 s — o porte respondia errado exatamente nas
   trocas, que é onde a diarização importa.
3. **`Unknown` virando ausência.** Sem sobreposição nenhuma, o app grava
   `Unknown`; o porte deixava nulo. Era a diferença de "4 falantes" contra "3"
   na gravação longa — `Unknown` conta como rótulo.

E um erro de método, registrado porque custou uma rodada inteira de medição:
a primeira reexecução usou `--no-build` sobre um binário compilado **antes** da
correção, e mediu o código velho concluindo que a correção não funcionara. A
regra da Fase 1 ("medir sem executar não vale nada") tem uma irmã: **medir sem
recompilar mede o passado**.

Também verificado, porque a hipótese óbvia estava errada: a diarização **é
determinística** entre execuções (321 trechos e 3 falantes em duas rodadas
independentes) e **não é contaminada** por rodar o ASR antes no mesmo processo.
A divergência era do porte, não do modelo.

### O que a primeira tela já decidiu (10/08/2026)

- **Sem WinForms nem WPF, de novo.** O pacote do WebView2 traz invólucros para
  os dois, e referenciá-los arrastaria o `Microsoft.WindowsDesktop.App` — o
  mesmo que custou 140 MB na bandeja e recusa trimming. O `.csproj` exclui os
  ativos do pacote e referencia só o `Microsoft.Web.WebView2.Core`, mais o
  `WebView2Loader.dll` nativo. Sem isso o build já acusava `MSB3277`, conflito
  entre duas versões de `WindowsBase`.
- **A interface inteira vai embutida no executável** e é servida por um host
  falso (`https://app.local/`) que o WebView2 intercepta antes de qualquer
  rede. Isso permite um CSP fechado (`default-src 'none'`, sem
  `'unsafe-inline'`) e mata a classe de bug em que a interface e o binário saem
  de versões diferentes.
- **O app não referencia o assembly do gravador.** Para achar as gravações ele
  lê o mesmo `settings.json`, à mão. Os dois executáveis são separados por
  desenho (princípio 1) e se encontram pelos arquivos; acoplar os binários por
  causa de uma chave seria trocar o desenho por conveniência.
- **Gravação em andamento não aparece na lista**, porque o `meta.json` só é
  escrito no fim. Verificado sem querer: uma gravação estava em curso durante o
  teste e foi corretamente omitida. Se isso confundir na prática, o conserto é
  a lista mostrar "gravando agora" a partir dos `.wav` sem `meta.json`.

**O achado que vale para todo motor futuro**: `torch`, `pyannote` e amigos
escrevem no `stdout` sem pedir licença, e uma linha dessas corrompe o
protocolo com um sintoma que não aponta para a causa. Todo motor duplica o
descritor 1 antes de qualquer import e manda o `stdout` do processo para o
`stderr`. Está na [SIDECAR.md](SIDECAR.md), com teste que reproduz a falha.

Fora do escopo do que foi feito: empacotamento (python-embeddable), o motor de
ASR, e o `HF_TOKEN` — hoje vem do ambiente, e quem o fornece na v1 instalada é
questão do passo 2.
