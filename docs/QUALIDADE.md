# Qualidade máxima: recomendações de desenho

Continuação da [AUDITORIA.md](AUDITORIA.md), agora em modo propositivo: dado
que a migração vai acontecer, **como desenhar o pipeline para o melhor
resultado possível** — não paridade, teto. Ordenado por impacto esperado.

A tese que atravessa tudo: os maiores ganhos de qualidade disponíveis não
estão em trocar modelo. Estão em (1) medir a coisa certa, (2) explorar as
duas faixas até o fim, e (3) deixar cada componente fazer só o trabalho em
que ele é bom.

---

## 1. O alicerce: medir a métrica do produto, não a do componente

Hoje existem três réguas parciais: `benchmark_wer.py` (WER contra corpus
público), `compare_engines.py` (similaridade entre dois palpites) e
`compare_diarization.py` (concordância entre dois palpites). Nenhuma mede o
que o usuário recebe: **a palavra certa, atribuída à pessoa certa**.

### 1.1 Construir o gold set

- 3 a 5 trechos de 2–3 min de **reuniões reais suas** (não FLEURS/CORAA —
  esses medem português lido, não reunião com jargão e sobreposição);
- para cada trecho: transcrição corrigida à mão + turnos de falante com
  início/fim. Um trecho de 3 min leva ~30–45 min para rotular; o conjunto
  inteiro é um dia de trabalho que destrava *todas* as decisões pendentes;
- guardar em `tools/golden/` com o áudio referenciado (as gravações já
  ficam em `data/recordings/`);
- incluir de propósito: 1 trecho com sobreposição de fala, 1 com você
  falando bastante, 1 com nomes próprios densos.

### 1.2 A métrica única: cpWER + erro de atribuição

- **cpWER** (concatenated minimum-permutation WER): concatena as palavras
  por falante, acha o pareamento ótimo de rótulos e mede WER. É o padrão
  dos benchmarks de reunião (CHiME) e pune os dois erros de uma vez —
  palavra errada e falante errado. Implementação: ~80 linhas sobre o
  `_levenshtein` que o `benchmark_wer.py` já tem;
- secundárias, para diagnóstico: WER puro (transcrição), DER (diarização),
  **recall de vocabulário** (dos nomes falados no gold set, quantos saíram
  certos — o `compare_engines.py` já tem a semente disso), e contagem de
  alucinação (segmentos sem sobreposição com fala real);
- **um comando, uma tabela**: `tools/eval.py <config>` roda o pipeline
  inteiro sobre o gold set e imprime uma linha por configuração. Toda
  decisão daqui em diante (q5 vs q8, turbo vs large, sherpa vs pyannote,
  chunking A vs B) vira uma linha nessa tabela em vez de uma discussão.

Sem isso, cada escolha da migração é um palpite comparado com outro
palpite — que é exatamente a limitação que a Fase 0 registrou em si mesma.

---

## 2. A mudança com maior teto: ASR por faixa, não sobre o mix

O desenho atual soma as faixas e transcreve o mix. Isso foi correto quando
a única fonte era um arquivo só — mas para gravações do gravador, o mix
**joga fora a maior vantagem que o produto tem**:

- `mic.wav` contém **só você**, limpo, sem os outros por cima;
- `system.wav` contém **só os outros**, sem a sua voz;
- fala sobreposta — o caso em que todo transcritor de reunião erra — chega
  **separada por construção**. O mix a recombina e devolve o problema.

O pipeline recomendado para gravações de duas faixas:

```
mic.wav    ── VAD ──► regiões suas ────────► ASR ──► segmentos já rotulados "você"
system.wav ── diarização ──► turnos ───────► ASR ──► segmentos rotulados por turno
                                   merge por timestamp ──► transcrição final
```

O que isso compra:

- **atribuição do dono deixa de ser heurística.** `assign_owner`,
  `OWNER_MARGIN`, RMS relativo — tudo isso existe para desfazer o que o mix
  fez. Sem mix, todo segmento nasce sabendo de qual faixa veio;
- **sobreposição vira feature**: quando você e outro falam juntos, saem
  dois segmentos corretos e simultâneos, em vez de um segmento com as duas
  falas embaralhadas;
- **a diarização encolhe**: só precisa separar os "outros" no system.wav —
  tipicamente 2–4 pessoas em vez de N+1, e sem a sua voz para confundir;
- o custo de 2× ASR é ilusório: o mic.wav é majoritariamente silêncio, e o
  VAD corta antes — só as suas regiões de fala são transcritas.

**O risco a medir antes de adotar** (vira o Teste E, no gold set): o
vazamento entre faixas. Com fone, o mic não ouve o sistema — o caso está
limpo. Com caixas de som, o mic capta os outros e a transcrição do mic.wav
duplicaria falas. O gate é objetivo: correlação entre as faixas nos trechos
em que só o sistema fala (o plano já previa essa medição para o AEC).
Regra prática: fone → por faixa; caixas → mix, como hoje. Detectável
automaticamente pela correlação, sem perguntar ao usuário.

Arquivos avulsos (upload) continuam no caminho atual — mix é tudo que há.

---

## 3. Segmentação: diarização primeiro, ASR depois

A Fase 0 provou que deixar o Whisper segmentar quebra a atribuição
(segmentos de 73s). O PARIDADE propõe cortar por VAD. Dá para ir um passo
além: **cortar por turno de falante**.

Ordem invertida do pipeline no system.wav:

1. **diarização primeiro** — é barata (33 MB de ONNX, ~1 min por hora de
   áudio na CPU) e devolve turnos com fronteiras de falante;
2. fundir turnos consecutivos do mesmo falante até ~25s (o teto que o app
   já usa), respeitando o VAD para não cortar palavra no meio;
3. **ASR por turno**, com `--carry-initial-prompt` para o vocabulário.

Consequências:

- segmento nunca atravessa turno — o erro que o `assign_speakers()` por
  sobreposição tenta remediar **deixa de existir**, junto com a
  necessidade de word timestamps (o risco 3 do plano some);
- timestamps exatos por construção (offset do turno + posição no chunk);
- alucinação sobre silêncio some pela raiz: só regiões com fala chegam ao
  ASR.

**A perda a controlar**: contexto entre chunks. O Whisper usa a janela de
30s inteira como contexto; chunks curtos perdem coesão local. Mitigação a
testar no gold set: prompt do chunk = vocabulário + últimas ~20 palavras do
chunk anterior (um `condition_on_previous_text` manual, limitado, que não
propaga alucinação indefinidamente porque o prompt é reconstruído por
chunk). Comparar as três variantes — sem contexto, com cauda, com
`condition` clássico — é uma linha do `eval.py` cada.

---

## 4. Diarização: encolher o problema antes de trocar o modelo

Quatro alavancas, em ordem de custo:

1. **Escopo reduzido pelo desenho da seção 2** — diarizar só os "outros"
   já elimina a classe de erro mais comum (confundir você com alguém).
2. **O calendário informa nomes, nunca restringe o clustering.** A lista
   de convidados não é teto nem piso do número de falantes: entra gente
   que não foi convidada (link compartilhado, colega de sala) e falta
   gente que foi. Decisão registrada: **o número de falantes é do modelo
   de diarização, sem hint** — porque a lista de convidados é fonte ruim
   de *k*, não porque informar *k* atrapalhe. (O FASE0 3-A/3-C mostrou o
   oposto: conhecer o *k* real ajuda muito o clustering; o que não existe
   é um jeito confiável de obtê-lo a partir do convite.) O papel do
   calendário fica todo a jusante: vocabulário do ASR e reforço de
   confiança na hora de *nomear* os falantes encontrados (ver
   [VOZES.md](VOZES.md), seção 5) — sem nunca excluir a possibilidade de
   alguém de fora do convite.
3. **Exportar o community-1 para ONNX por conta própria** (a opção que
   faltava no PARIDADE): o modelo de segmentação é exportável com o token
   que já existe, e o embedding (wespeaker) já é o mesmo ONNX. O que falta
   na via nativa é pós-processamento — decodificação do powerset,
   resegmentação, clustering — que é código, não modelo. Auditar o
   sherpa-onnx primeiro, como o PARIDADE manda: se ele decodificar o
   powerset, o gap medido era do modelo, e o community-1 convertido o
   fecha.
4. **Enquanto isso, a diarização Python vive como motor sidecar** — a
   ponte que desacopla a migração do risco. Promovida a plano default na
   auditoria; mantida aqui.

O gate de decisão continua o DER/cpWER no gold set — não a concordância
entre dois palpites.

---

## 5. ASR: default de qualidade e a rede contra alucinação

- **Candidato a default: `large-v3` q5_0, não o turbo.** O resultado 1-A
  da Fase 0 mostrou o large-v3 recuperando 24% mais fala; a fragmentação
  que veio junto é resolvida pela seção 3 (quem corta é a diarização, não
  o ASR). Em GGML q5_0 o large-v3 usa ~1,1 GB — cabe folgado nos 6 GB da
  RTX 2060, e o argumento "turbo porque o large não cabe" desaparece com a
  quantização. O turbo fica como opção "rápido". Decisão final: cpWER no
  gold set, com a mesma segmentação nos dois lados.
- **Idioma travado por projeto.** `language=pt` explícito nas configurações
  de projeto, nunca auto-detect em reunião — um trecho de inglês no meio
  não pode trocar o idioma da janela.
- **Filtro pós-ASR de alucinação, no núcleo (agnóstico de motor).** O
  whisper.cpp não tem `hallucination_silence_threshold`; em vez de
  depender de flag de motor, o núcleo valida cada segmento:
  - sem sobreposição mínima com regiões de fala do VAD → descarta;
  - n-grama repetido acima de limiar (o caso "Tata-tata…" e "Eu acho que
    é um pouco mais difícil" ×30 da Fase 0) → descarta;
  - >5s e ≤2 palavras (o padrão que a própria Fase 0 catalogou) → marca
    como suspeito na UI em vez de descartar.
  Cada regra é testável no gold set (falsos positivos = fala real
  descartada) e vale para qualquer motor futuro.
- **Vocabulário com orçamento gerenciado**: os 224 tokens do prompt são o
  recurso mais escasso do ASR. Prioridade de preenchimento: participantes
  do evento → nomes recorrentes do projeto → jargão. O recall de
  vocabulário do `eval.py` (seção 1) diz se o orçamento está sendo bem
  gasto — hoje o aviso da UI pede para o usuário adivinhar.

---

## 6. Vozes: reinscrição limpa, aproveitando o descarte forçado

O risco 4 do plano já força descartar os perfis (embeddings ONNX ≠ torch).
Transformar a perda em ganho — reinscrever com regras melhores:

- **a sua voz vem do `mic.wav`** — áudio limpo por construção, o melhor
  perfil possível, inscrito automaticamente a cada gravação de duas faixas;
- **as dos outros vêm do `system.wav`**, apenas de segmentos sem
  sobreposição (a informação de powerset da diarização diz quais são) e
  com duração mínima real — implementar a concatenação que o
  `voices.py` promete e não faz (bug 1.3 da auditoria);
- **matching por centróide ou média dos top-k**, não máximo sobre 25
  amostras — máximo é refém do pior embedding já salvo; um outlier gera
  falso positivo para sempre;
- guardar junto de cada embedding a procedência (duração, de qual faixa,
  data) — é o que permite depurar um falso match e expirar amostras ruins;
- o threshold (hoje um slider 0,5–0,9 no escuro) se calibra no gold set:
  curva de falso aceite × falso rejeite com os falantes rotulados.

---

## 7. Captura: o teto de tudo que vem depois

A Fase 0 concluiu que a qualidade da gravação domina a escolha de motor.
Já coberto na auditoria (crash-safe, disco, âncora no clock do
dispositivo), mais duas notas de qualidade:

- **não recomendo AGC/denoise por padrão** — a posição do plano ("só entra
  se melhorar métrica") está certa e agora tem régua: gold set antes/depois.
  Supressão agressiva come consoantes; o vazamento com fone é mínimo;
- **subir o alarme de faixa morta para dentro do fluxo de reunião**: os
  avisos da bandeja existem, mas a lição da gravação de 06/08 é que aviso
  passivo não foi visto. O gravador sabe em tempo real que o mic está em
  zeros há 23 min — isso merece notificação repetida (como o lembrete de
  mute já faz), não só cor de ícone.

---

## 8. Ordem de execução recomendada

| # | o quê | destrava |
|---|---|---|
| 1 | Gold set + `eval.py` com cpWER (seção 1) | toda decisão abaixo |
| 2 | Corrigir bugs 1.1–1.4 da auditoria | benchmarks honestos no pipeline atual |
| 3 | Teste E (vazamento entre faixas) + protótipo ASR-por-faixa **no pipeline Python atual** | a decisão de desenho da seção 2, antes de escrever C# |
| 4 | Protótipo diarização-primeiro (seção 3) idem | risco 3 do plano eliminado ou não |
| 5 | Rodar a matriz {large-v3, turbo} × {q5, q8} × {seg. atual, seg. por turno} no gold set | defaults do instalador |
| 6 | Auditoria do powerset no sherpa + export ONNX do community-1 | o caminho da diarização nativa |
| 7 | Só então congelar o contrato do motor e portar | Fases 1–2 do plano |

O ponto do item 3–4: **as duas mudanças de desenho se testam em Python, no
pipeline que já existe, antes de qualquer linha de C#.** São mudanças de
orquestração (ordem das etapas, o que alimenta o quê), e o código atual já
tem todas as peças. Validar lá custa dias; descobrir depois do porte custa
o porte.
