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

UI: AA Design System com os componentes React, agora sem o Gradio no caminho
(a fase 3 do redesign vira a via normal — PLANO §3). Idioma: pt-BR, seguindo o
design system.

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
4. Correção fonética + filtro de silêncio no núcleo, com os testes portados
   das ferramentas Python.
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

| | resultado |
|---|---|
| mix das faixas | **byte a byte idêntico** (3.871.516 bytes dos dois lados) |
| idioma, duração | `pt`, 120,98 s — iguais |
| segmentos | 44 x 44, **texto idêntico em todos** |
| tempos | diferença máxima **0,0 ms** |
| falantes | 3 x 3, e a mesma atribuição em todos os 44 |
| segmentos seus | 32 x 32 |

O mix idêntico é o que dá peso ao resto: com a mesma entrada e os mesmos
parâmetros, texto igual deixa de ser coincidência. Custo do lado novo: ASR
31,8 s + diarização 17,0 s numa gravação de 121 s.

A única divergência da primeira medição foi de nomenclatura — o C# devolvia
`SPEAKER_00` onde o app dizia `Speaker 1`. Era o núcleo não fazendo a parte
dele: o protocolo manda o rótulo cru de propósito, e nomear é do núcleo.
Corrigido em `Montagem.AtribuirFalantes`, com a mesma ordem alfabética do
`_create_speaker_map` — a atribuição em si já estava idêntica.

**O achado que vale para todo motor futuro**: `torch`, `pyannote` e amigos
escrevem no `stdout` sem pedir licença, e uma linha dessas corrompe o
protocolo com um sintoma que não aponta para a causa. Todo motor duplica o
descritor 1 antes de qualquer import e manda o `stdout` do processo para o
`stderr`. Está na [SIDECAR.md](SIDECAR.md), com teste que reproduz a falha.

Fora do escopo do que foi feito: empacotamento (python-embeddable), o motor de
ASR, e o `HF_TOKEN` — hoje vem do ambiente, e quem o fornece na v1 instalada é
questão do passo 2.
