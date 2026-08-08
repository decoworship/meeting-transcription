# Auditoria: arquitetura, features e plano de migração

Revisão independente feita em 07/08/2026, lendo todo o código e os quatro
documentos ([PLANO.md](PLANO.md), [FEATURES.md](FEATURES.md),
[PARIDADE.md](PARIDADE.md), [FASE0-RESULTADOS.md](FASE0-RESULTADOS.md)).
Organizada em: veredito, bugs no código atual, incoerências entre FEATURES e
código, críticas ao plano, e riscos que o plano não cobre.

---

## Veredito geral

O plano é raro de bom: mede antes de decidir, registra o porquê antes do como,
e trata a diarização — o único risco real — como bloqueante em vez de
empurrá-lo. As decisões grandes (sidecar por stdio, não bifurcar o anarlog,
copiar o desenho do motor de resumo do Meetily, Docker como rede de segurança)
estão certas e bem fundamentadas.

As ressalvas desta auditoria são de três tipos:

1. **o código atual tem bugs que contaminam a especificação** — o FEATURES.md
   declara entregas que o código não cumpre (D2, C8), e é o FEATURES.md que
   vai virar o critério de aceite do porte;
2. **o plano não cobre riscos operacionais do gravador** que são mais prováveis
   de perder uma reunião do que qualquer regressão de modelo (WAV não
   sobrevive a crash, disco cheio);
3. **duas decisões do plano merecem ser revistas**: o caminho da diarização
   tem uma opção não listada, e o corte por VAD tem uma armadilha de desenho
   quando combinado com as duas faixas.

---

## 1. Bugs no código atual

Em ordem de gravidade. Importam mesmo com a UI condenada: vários estão nos
módulos que o plano manda **portar**, não reescrever.

### 1.1 WhisperX + vocabulário = crash (confirmado)

[whisperx_transcriber.py:94](../src/transcription/whisperx_transcriber.py#L94)
passa `asr_options` para `model.transcribe()`. A assinatura real do
`FasterWhisperPipeline.transcribe` no whisperx instalado (conferida em
`.venv/.../whisperx/asr.py:190`) não aceita `asr_options` nem `**kwargs` —
os parâmetros são `audio, batch_size, num_workers, language, task,
chunk_size, print_progress, combined_progress, verbose`. **Qualquer
transcrição com engine WhisperX e vocabulário preenchido levanta
`TypeError`.** Como o vocabulário é preenchido automaticamente ao escolher
uma gravação (participantes da agenda), a combinação não é exótica.
`asr_options` só existe no `whisperx.load_model()`.

### 1.2 D2 depende da diarização, ao contrário do que o FEATURES afirma

O FEATURES.md vende D2 como *"atribuição garantida, sem depender de
diarização"*. Mas em [gradio_app.py:600-620](../src/web/gradio_app.py#L600),
o `assign_owner` só roda **dentro do bloco `if diarization:`** — e pior,
depois do `diarizer.load_model()`, então se o pyannote falhar (token, rede),
a exceção pula o `assign_owner` junto. Com diarização desligada ou quebrada,
a única atribuição que não precisa de modelo nenhum não acontece.
`assign_owner` só precisa dos dois WAVs; deveria rodar incondicionalmente
quando `dual` existe, fora do try da diarização.

### 1.3 O fallback de concatenação em voices.py não existe

[voices.py:193-207](../src/web/voices.py#L193): o comentário diz *"Try
concatenating all segments if individual ones are too short"*, calcula o
total… e então **embeda só o segmento mais longo mesmo assim**. Se o mais
longo tem 1,0s (abaixo de `MIN_SPEECH_SECONDS=1.5`) mas o total tem 5s, o
código segue com o segmento curto — exatamente o caso em que o embedding sai
ruim. Ou implementa a concatenação, ou retorna `None`; o estado atual é o
pior dos dois.

### 1.4 O reconhecimento de voz pode sobrescrever a certeza das duas faixas

Fluxo em [gradio_app.py:641-658](../src/web/gradio_app.py#L641): o
`assign_owner` marca segmentos como `user_label` ("You"), e **depois** o
`match_speakers` roda sobre *todos* os grupos de falante — inclusive o grupo
"You". Se algum perfil salvo passar do threshold, `speaker_names["You"]`
vira o nome de outra pessoa, e a atribuição *garantida* pelo microfone é
renomeada por um palpite de cosseno. No sentido inverso,
`learn_speakers` filtra apenas `"Speaker N"` — "You" passa pelo filtro e
vira um perfil de voz salvo com o nome "You", poluindo o `voices.json`.
O grupo do `user_label` deveria ser excluído do match e do aprendizado.

### 1.5 Cancelar não cancela (C8 é cosmético)

[gradio_app.py:1767](../src/web/gradio_app.py#L1767): o botão cancela o
evento do Gradio, mas a thread `worker` continua rodando transcrição e
diarização até o fim — GPU ocupada, VRAM presa, e o resultado é jogado fora.
O FEATURES lista C8 como entrega pronta; hoje é só a UI que desiste.
No porte, cancelamento real deve ser requisito do contrato do motor
(matar o processo sidecar resolve de graça — mais um argumento a favor
dele).

### 1.6 O áudio do histórico morre com o container

`history.save_entry` grava `audio_path` apontando para `/tmp`
(conferido: `data/meeting-transcription/history/*.json` têm
`"audio_path": "/tmp/mix_....wav"`). Depois de recriar o container, E2
(clicar e ouvir) quebra para toda entrada do histórico. No porte, o áudio
processado precisa morar num diretório de dados gerenciado, e o histórico
referenciá-lo por caminho relativo.

### 1.7 Tabela de falantes perde os nomes após merge/edição

`merge_speakers` e `save_segment_edit` chamam `compute_speaker_stats(result_state)`
**sem** `speaker_names` nem `voice_matches`
([gradio_app.py:883](../src/web/gradio_app.py#L883) e
[:967](../src/web/gradio_app.py#L967)) — depois de fundir falantes ou salvar
uma edição, a tabela volta a mostrar os rótulos crus e a coluna de voice
match esvazia.

### 1.8 MODEL_SIZES não tem o turbo

[base.py:59](../src/transcription/base.py#L59): a lista para em `large-v3`.
O faster-whisper aceita `large-v3-turbo` — que é justamente o candidato a
default do plano. Custo de um item na lista; permitiria comparar o turbo
dentro do pipeline atual (mesmo VAD, mesmos parâmetros), o que isola a
variável "modelo" da variável "motor" nos benchmarks em andamento.

---

## 2. Riscos do gravador que o plano não cobre

O plano trata os riscos de *qualidade* (modelo, quantização). Os riscos
abaixo são de *perda total da gravação* — mais graves e mais prováveis.

### 2.1 Um crash perde a reunião inteira

`capture.py` usa o módulo `wave`, que só escreve os tamanhos no header no
`close()`. Se o processo morrer no meio (crash, kill, queda de energia,
Windows Update), o WAV fica com header de 0 frames — os dados estão no
disco, mas nenhum player nem o transcritor abrem. **NAudio tem o mesmo
comportamento** (`WaveFileWriter` finaliza no Dispose), então o porte herda
o problema se não for tratado. Correções possíveis, da mais barata à melhor:

- flush periódico reescrevendo o header (a cada N segundos, `Flush()` +
  patch dos 8 bytes de tamanho);
- gravar PCM cru + um `meta` com formato, e finalizar para WAV no stop —
  crash deixa um arquivo trivialmente recuperável;
- critério de aceite da Fase 1: **`kill -9` no meio de uma gravação de 10
  minutos tem que deixar arquivos recuperáveis sem ferramenta externa.**

### 2.2 Disco cheio é silencioso

Não há verificação de espaço livre nem no início nem durante a gravação.
Duas faixas de 16 kHz mono são ~230 MB/h — pouco, mas a pasta padrão pode
ser um caminho de rede (`\\wsl$`...) cuja cota não é o disco local.
`wave.writeframes` vai levantar `OSError` dentro da thread writer, que morre
sem avisar a bandeja — o ícone continua vermelho, gravando nada. Barato de
cobrir: checar espaço no start, monitorar falha de escrita na writer thread
e promover para o estado WARNING da bandeja.

### 2.3 A âncora de deriva mede o relógio errado

`_correct_drift` compara amostras escritas contra
`time.monotonic() - t0` — o tempo de *chegada ao writer*, não o tempo de
*captura*. A fila entre callback e writer aguenta ~10s de backlog
(512 × 1024 frames); um engasgo de disco ou GC faz o writer atrasar, a
correção injeta zeros "para compensar", e quando o backlog drena, ela
descarta amostras reais. O resultado líquido é jitter estrutural disfarçado
de correção. Funciona hoje porque a máquina sobra; no porte C#, ancorar na
**posição do dispositivo** que o WASAPI expõe (`IAudioClock` /
`GetPosition`, em unidades de QPC) em vez do relógio de chegada. Segundo
refinamento: aplicar inserção/descarte preferencialmente em trechos
silenciosos — hoje um ajuste de 50ms pode cair no meio de uma palavra.

### 2.4 Higiene operacional que falta na Fase 1

- **instância única** — dois tray.py gravando no mesmo dispositivo é
  indefinido; um mutex nomeado resolve;
- **autostart opcional** com o Windows (chave Run) — um gravador que não
  está rodando não grava;
- **contador de amostras descartadas** no `queue.Full` — já está no
  PARIDADE.md via anarlog; reforço que é a instrumentação mais barata da
  lista e ataca o modo de falha que já aconteceu;
- `default_output_dir()` hardcoda `\\wsl$\Ubuntu\home\andre\...` — ok para
  a máquina atual, mas é uma suposição pessoal dentro do código; no porte,
  primeira execução pergunta.

---

## 3. Críticas ao plano de migração

### 3.1 Diarização: existe uma opção não listada, e a opção 4 merece promoção

O PARIDADE.md lista quatro saídas. Duas observações:

**A opção que falta: exportar o community-1 para ONNX por conta própria.**
O raciocínio do plano é "não há ONNX do community-1, logo migrar = cair para
segmentation-3.0". Mas o ONNX do segmentation-3.0 que o sherpa distribui
veio de alguém rodando `torch.onnx.export` sobre o checkpoint do pyannote —
nada impede fazer o mesmo com o modelo de segmentação do community-1, que já
está acessível com o token existente. Para uso próprio não há questão de
redistribuição (o objetivo "matar a dependência do HuggingFace" vira "baixar
uma vez, converter, guardar"). O que realmente falta na via nativa não é o
modelo, é o **pós-processamento**: decodificação do powerset, resegmentação
com sobreposição e clustering com restrições — algumas centenas de linhas de
lógica, não um problema de modelo. Se a auditoria do sherpa-onnx (que o
PARIDADE já manda fazer) mostrar que ele decodifica o powerset, essa opção
fica barata: sherpa + modelo convertido do community-1.

**A opção 4 (diarização Python como motor sidecar) deveria ser o plano
default, não o último recurso.** A arquitetura de motores existe exatamente
para isso: um motor de diarização com runtime Python embutido
(python-embeddable + venv congelado, ~2–3 GB na pasta do motor) mantém o
`community-1` intacto e **desacopla a Fase 2 do único risco não resolvido**.
O ASR migra já (está liberado); a diarização migra quando a medição com
rótulo manual autorizar. O plano trata isso como "feio, mas honesto" — é
mais que isso: é o que impede o pior resultado possível, que seria segurar a
migração inteira por causa da diarização, ou migrá-la sem medir por pressa.

### 3.2 Corte por VAD: uma armadilha quando combinado com as duas faixas

O PARIDADE celebra o corte por VAD antes do ASR, e sugere *"rodar o VAD por
faixa: um trecho com fala só no mic.wav já nasce sabendo que é você"*.
Cuidado com o passo seguinte implícito: se o VAD roda por faixa e o **ASR
também roda por faixa**, a fala sobreposta é transcrita duas vezes e o merge
das duas transcrições vira um problema novo — que o desenho atual (ASR sobre
o mix, exatamente para enxergar sobreposição) evita de propósito. As
combinações coerentes são:

- **ASR no mix + cortes do VAD sobre o mix** (conservador, preserva o
  desenho atual, resolve os segmentos de 73s); o VAD por faixa entra só como
  *informação de atribuição*, não como fronteira de transcrição; ou
- **ASR por faixa** — que precisa de um desenho de merge explícito e um
  teste próprio antes de ser adotado.

Vale registrar a escolha no plano antes da Fase 2, porque o contrato do
motor de transcrição muda entre uma e outra.

Segunda ressalva: fatiar o áudio em regiões de fala curtas reduz o contexto
que o Whisper usa dentro da janela de 30s. O `--carry-initial-prompt` mitiga
para o vocabulário, mas a coesão local (pronomes, continuação de frase) pode
piorar. O experimento da Fase 0 mediu o whisper.cpp *sem* chunking externo;
o pipeline "VAD corta → ASR por região" é uma configuração nova que precisa
passar pelas mesmas métricas antes de virar o desenho.

### 3.3 O sistema de plugins está desenhado além da necessidade

O desenho "motores como pastas baixáveis com manifesto, negociação de
hardware e registro" é a forma certa **em regime**, mas para n=2 motores e
n=1 usuário é infraestrutura na frente da necessidade. O próprio plano diz
"dois pontos definem a reta; um ponto define uma abstração inventada" — vale
aplicar a regra ao próprio sistema de plugins: na Fase 2, o contrato é só
*"processo filho + protocolo por linha via stdio + kill para cancelar"*,
hardcoded para dois motores. Manifesto, download, verificação e seleção de
acelerador por pacote entram quando o terceiro motor (resumo) chegar — que é
quando os requisitos reais do manifesto aparecem.

### 3.4 Integridade por tamanho de arquivo é fraca

O plano manda copiar do Meetily a tabela de modelos com "detecção de
corrupção por tamanho". Ao copiar, trocar por **sha256**: mesmo custo de
implementação, e cobre tanto corrupção quanto a hipótese (real, dado que os
assets vêm de releases do GitHub de terceiros) de o conteúdo do URL mudar
por baixo. Para os próprios motores baixáveis, hash é o mínimo; assinatura
fica para quando houver distribuição.

### 3.5 O veredito "ASR liberado" está apoiado numa métrica sem chão

A Fase 0 foi bem conduzida e honesta sobre os limites, mas vale explicitar:
o "whisper.cpp recupera 24% mais fala" foi validado lendo **uma janela de
30s** lado a lado, contra um baseline que comprovadamente alucina nos
trechos degradados. A rotulagem manual de 2–3 minutos (passo 1 dos próximos
passos) não é só para a diarização — ela deveria **gatear também a escolha
final de modelo/quantização** (turbo vs large-v3, q5 vs q8), porque hoje
essas escolhas se apoiam em similaridade contra um baseline imperfeito. O
`tools/benchmark_wer.py` já existe; com 3 minutos de referência ele vira a
régua de tudo.

### 3.6 Pontos menores

- **A13** (desempate por `responseStatus`): endossado, é pequeno e certeiro.
- **Migração de dados**: o histórico é portável, mas os `audio_path` de
  `/tmp` já estão mortos (item 1.6) — migrar o texto e re-homear ou
  descartar as referências de áudio. Perfis de voz: descartar e reinscrever,
  como o plano já decide (risco 4).
- **Matching de voz no porte**: o `best_match` usa o **máximo** de cosseno
  contra até 25 embeddings por pessoa — máximo é sensível a outlier (um
  embedding ruim aprendido de um segmento poluído gera falso positivo para
  sempre). Na reinscrição, usar centróide ou média dos top-k. Barato, e o
  formato JSON permite.
- **Teams (seção 2.1 do plano)**: além de espelhar o mute, gravar no
  `meta.json` os eventos de estado (entrou/saiu da reunião) — de graça na
  mesma assinatura do WebSocket, e dá fronteiras de reunião para cortar
  gravação esquecida ligada.

---

## 4. Documentação desatualizada (custa pouco, confunde muito)

- **Os dois CLAUDE.md** (raiz de `~/projects` e do repositório) descrevem a
  arquitetura como `src/gui/app.py:_transcription_worker` com CustomTkinter —
  esse código está morto desde a migração para Gradio (intocado desde o
  commit inicial). Qualquer agente ou pessoa nova começa pelo CLAUDE.md e é
  mandado para o lugar errado.
- **Código morto**: `src/gui/app.py` (1.206 linhas), `main.py`, e as
  dependências `customtkinter` e `ffmpeg-python` no pyproject. Se a decisão
  é manter até o porte, um aviso de uma linha no topo resolve; melhor ainda
  é apagar — o git guarda.
- **PLANO.md seção 4** lista duas "correções pequenas" que **já estão
  feitas**: a data preenche ao escolher gravação (`on_recording_change`, lê
  `recorded_at`) e o `user_label` persiste (salvo no config e lido no
  build). Riscar, para a seção não sugerir trabalho pendente.
- **pyproject**: `whisperx` sem versão e `requires-python >= 3.13` — o
  uv.lock segura hoje, mas registra a fragilidade (numba/whisperx em 3.13 é
  terreno recente). Irrelevante se a stack Python vai embora; relevante se o
  motor de diarização Python sobreviver como sidecar (3.1 acima).

---

## 5. Uma nota de segurança

O Gradio sobe em `0.0.0.0:7860` sem autenticação. Na LAN atual isso expõe:
todas as transcrições (histórico completo via UI), o campo do token do
HuggingFace (type="password" esconde do ombro, não da API), e upload de
arquivos arbitrários para processamento. Enquanto o Docker sobreviver
(fases 0–2), vale ou bind em `127.0.0.1`, ou `auth=` no `launch()`. O app
nativo elimina o problema por construção — mais um ganho colateral do porte
que o plano pode listar.

---

## Resumo: o que fazer com isto

| # | ação | quando |
|---|---|---|
| 1 | Corrigir 1.1 (WhisperX+vocabulário), 1.2 (assign_owner fora do if), 1.4 (excluir user_label do match/learn) | agora — são bugs no que será portado ou usado nos benchmarks |
| 2 | Adicionar `large-v3-turbo` ao MODEL_SIZES | agora — ajuda os benchmarks em andamento |
| 3 | Rotular 2–3 min à mão e gatear **modelo e diarização** nisso | antes de fechar defaults |
| 4 | Decidir mix-vs-por-faixa para o VAD/ASR e registrar no plano | antes da Fase 2 |
| 5 | Critérios de aceite da Fase 1: crash-safe WAV, disco, instância única, âncora no clock do dispositivo | Fase 1 |
| 6 | Promover diarização-Python-como-sidecar a plano default; investigar export ONNX do community-1 | Fase 2 |
| 7 | Atualizar CLAUDE.md, riscar PLANO seção 4, decidir sobre src/gui | quando der |
