# Fase 7 — tempo real: carta de estudo

Aberta em 27/08/2026, a pedido do dono do produto, depois de ele ver a
demonstração da NVIDIA de transcrição com fala sobreposta.

**Esta carta não manda fazer nada.** As cartas de fase anteriores descrevem o
que vai ser feito; a [FASE6.md](FASE6.md) lista o que pode ser feito, com
gatilho. Esta faz uma pergunta e define **o que precisaria ser verdade para a
resposta ser sim**. É um estudo — e o resultado legítimo dele é "não vale a
pena", desde que venha com número.

A Fase 6 continua aberta enquanto isto roda. Nada aqui tem prioridade sobre ela.

---

## 1. A pergunta

> Transcrever e separar falantes **durante** a reunião acelera o que o usuário
> espera, e a um preço que a gravação aguenta?

Repare no que a pergunta **não** é. Não é "dá para fazer tempo real" — dá, e a
§3 lista três caminhos prontos. É se o ganho vale o risco, e o risco aqui não é
qualidade: é a gravação.

**O que o usuário espera hoje.** Encerrada a reunião, ele clica em transcrever e
espera. Numa reunião mediana do acervo — 25 min, medido em 44 gravações — o
pipeline inteiro leva alguns minutos de GPU. Se o ASR corresse junto com a
reunião, a ata começaria a ser escrita no instante do "encerrar", e o tempo de
espera cairia para o que a diarização e a ata custam sozinhas.

**O que ele ganharia além de tempo**, e que é a parte interessante: uma
transcrição **visível enquanto a reunião acontece**. Isso não é otimização de
pipeline, é outra funcionalidade — anotar, procurar o que foi dito há dez
minutos, ver o nome do cliente sair errado e corrigir na hora, com a pessoa
ainda falando. Se a Fase 7 tiver um motivo forte, é este, não o relógio.

---

## 2. O que já é tempo real, e o que falta

Vale registrar, porque a distância é menor do que parece:

- **a captura já é contínua**, em duas faixas, com âncora no relógio de parede e
  escrita à prova de queda (`Gravacao/`, e o que não se reabre no
  [CLAUDE.md](../CLAUDE.md));
- **já existe um canal de eventos empurrados** do núcleo para a tela: o `id: 0`
  da ponte, que hoje leva o nível de áudio cinco vezes por segundo
  (`App/Ponte.cs` + `web/ponte.js`). Um segmento transcrito iria pelo mesmo cano;
- **os motores já são sidecars** que falam JSON por stdin/stdout
  ([SIDECAR.md](SIDECAR.md)). O protocolo é pedido-e-resposta; tempo real precisa
  de uma sessão que **fica aberta** e cospe resultado — é uma extensão do
  protocolo, não uma reescrita;
- **a retomada já existe** (`Nucleo/Retomada.cs`), e é o que torna aceitável um
  ASR ao vivo que falhe no meio: cai para o caminho de hoje sem perder nada.

O que falta é o modelo e a disciplina de rodá-lo sem estorvar a gravação.

---

## 3. Os candidatos

### 3.1 Diarização ao vivo — Streaming Sortformer (NVIDIA)

É o mais maduro dos três, e o mais fácil de medir contra o que já temos.

| | |
|---|---|
| modelo | `nvidia/diar_streaming_sortformer_4spk-v2` |
| tamanho | 117M parâmetros (FastConformer + Transformer) |
| latência | quatro modos, de **0,32 s** a 30,4 s de bloco |
| teto | **4 falantes**; degrada feio de 5 em diante (DER 42,6% em 5–9) |
| DER | 13,2% no DIHARD III (1–4 falantes), 6,6% no CALLHOME de 2 |
| licença | CC-BY-4.0 |
| como roda | NeMo, `NeMo-Speech.cpp`, Riva — **e há exports ONNX da comunidade** |

**O ONNX é o que torna isto possível aqui.** Os sidecars rodam num Python
embarcado de 4,3 GB que já é a maior parte do instalador; instalar NeMo dentro
dele não é uma opção séria. Um `.onnx` de 117M parâmetros é outra conversa —
e o instalador precisa **encolher**, não crescer (o winget espera exatamente
isso, ver [ATUALIZACAO.md](ATUALIZACAO.md)).

**O teto de 4 falantes é o risco principal, e ele é mensurável.** Medido no
acervo em 27/08/2026, sobre as 44 gravações que têm falante atribuído:

| falantes | gravações |
|---|---|
| 1–4 | **19 (43%)** |
| 5–8 | 20 (45%) |
| 9+ | 5 (11%) |

A comparação é justa: a diarização deste projeto roda **só na faixa do
sistema**, então esses números já são "participantes remotos", que é exatamente
o que o Sortformer veria. E ela é pessimista por um lado — a nossa diarização
divide uma pessoa em várias, então parte das de 5 a 8 é de 4 de verdade — e
otimista por outro, porque a contagem sai do rótulo, não da verdade.

**43% não é suficiente** para trocar a diarização de hoje. Pode ser suficiente
para uma **prévia ao vivo** que a passada offline depois corrige — e essa
distinção é a decisão de desenho mais importante desta carta.

O treino é **majoritariamente em inglês**, com aviso explícito de degradação em
outras línguas. Diarização depende menos da língua que ASR, mas "menos" não é
"nada", e as reuniões deste projeto são todas em português.

### 3.2 ASR ao vivo — Whisper em janela deslizante

Não precisa de modelo novo: o `faster-whisper` que já está no sidecar roda em
streaming com a política de **LocalAgreement** (o `whisper_streaming` da UFAL, e
o `WhisperLive`), que só emite o que duas janelas seguidas concordaram. Latência
relatada entre 0,5 e 3,3 s conforme o tamanho do modelo e a janela.

É o caminho de menor risco: mesmo modelo, mesma língua, mesma qualidade de
palavra — o que muda é **quando** o texto aparece. E o `large-v3` que usamos hoje
é o mais lento de todos, então a medida real é se ele acompanha em tempo real na
2060 ou se a prévia teria que rodar num modelo menor, com o `large-v3` refazendo
tudo no fim.

### 3.3 Fala sobreposta — Multitalker Parakeet Streaming

É o modelo da demonstração que o dono do produto lembrou:
`nvidia/multitalker-parakeet-streaming-0.6b-v1`. Transcreve **fala totalmente
sobreposta** rodando uma instância por falante sobre o mesmo áudio, guiado pela
saída de um diarizador — o Sortformer. Latência de 80 ms a 1,12 s.

**E é só inglês.** Isso o descarta como motor deste app, hoje. Fica registrado
porque é a direção para onde a área está indo, e porque a sobreposição é um
problema real e medido aqui: foi ela que motivou a `VozDoDono` da 0.6.1, e o que
sobra de erro de atribuição depois dela está em
[AUDITORIA-ATAS.md](AUDITORIA-ATAS.md) §6.

---

## 4. O que trabalha contra

### 4.1 A gravação vem primeiro, e há um precedente ruim

O computador do segundo usuário **desliga sozinho** durante a transcrição
([FASE6.md](FASE6.md) §3.0). A causa mais provável é entrega de energia sob
carga de GPU, e não é conserto nosso.

Hoje isso custa uma transcrição, e a retomada recupera. **Em tempo real,
custaria a reunião** — a máquina desliga no minuto 20 de uma reunião de 50, e
não há retomada que traga de volta áudio que nunca foi gravado.

Este é o argumento mais forte contra a Fase 7, e ele não some com engenharia
boa: some com um interruptor. **Se isto for feito, nasce desligado**, e quem
ligar está aceitando uma troca que a tela precisa dizer em português.

### 4.2 A GPU já está ocupada — pelo Meet

Durante a reunião a placa está codificando vídeo, e a 2060 tem 6 GB. Somar ASR
ao vivo compete com a chamada, e o sintoma de perder essa disputa é o áudio do
usuário travando para os outros — o pior tipo de defeito, porque quem sofre não
é quem instalou o app.

### 4.3 A qualidade cai, e o dono do produto já disse isso

"Eu imagino que não vá ter a melhor qualidade mas precisamos testar." Correto, e
por um motivo estrutural: streaming decide sem ter ouvido o que vem depois. O
Whisper offline reescreve uma palavra à luz da frase inteira; ao vivo, ele já
emitiu.

Daí a única forma responsável de fazer isto: **o texto ao vivo é rascunho, e a
passada offline no fim é a verdade.** Nunca a prévia como resultado final.

---

## 5. Como medir, sem construir nada

A parte boa é que **o acervo já responde quase tudo, offline**. Áudio gravado
pode ser alimentado a um motor de streaming em blocos, na velocidade que se
quiser, e o resultado comparado com o que temos hoje. Não é preciso reunião ao
vivo para saber se vale a pena — e as réguas já existem:

- `tools/comparar_com_gemini.py` — concordância de falante por palavra, contra as
  três reuniões que têm Gemini em paralelo. É a régua que já mediu a 0.6.1;
- `tools/wer_contra_gemini.py` — divergência de texto;
- `tools/auditar_atas.py` — o que a ata perde, sem gabarito.

**A ordem certa, e cada passo pode matar o estudo:**

1. **exportar o Sortformer para ONNX e rodá-lo nas 3 gravações com Gemini**, em
   modo offline primeiro. Se ele não empata com a pyannote de hoje em português
   parado, não vai ganhar em movimento. *Custa uma tarde e não toca no app.*
2. **repetir em streaming**, com os quatro modos de latência, medindo quanto se
   perde da (1) para cá;
3. **medir a contagem de falantes que ele devolve** contra o rótulo de hoje nas
   reuniões de 5+. O teto de 4 vira erro de que tipo — junta duas pessoas, ou
   descarta a quinta?
4. **cronometrar o `large-v3` em janela deslizante na 2060**, com o Meet aberto.
   Se não acompanhar, a pergunta vira "com qual modelo menor", e a resposta muda
   o desenho todo;
5. **só então** decidir se existe funcionalidade.

---

## 6. Os gatilhos

Como na Fase 6, nada aqui se faz por gostar:

- **o estudo (§5, passos 1 a 4)** — gatilho: nenhum. É o que foi pedido, e é
  barato. Roda quando a Fase 6 der folga;
- **prévia ao vivo na tela** — gatilho: o estudo mostrar latência abaixo de ~2 s
  com o Meet aberto **e** a diarização ao vivo não piorar o que já temos. Sem os
  dois, é enfeite que arrisca a gravação;
- **trocar a diarização offline pelo Sortformer** — gatilho: ele empatar ou
  ganhar da pyannote no passo 1, em português. Aí é uma troca boa por si só,
  **sem nada de tempo real**: 117M contra os 57 MB de modelos que hoje
  substituem o token, e uma dependência a menos;
- **fala sobreposta pelo Multitalker** — gatilho: sair uma versão multilíngue com
  português. Até lá, não existe.

Repare que o terceiro gatilho é o mais provável de disparar, e é o que **menos
tem a ver com o pedido original**. Estudos costumam ser assim.

---

## 7. O que não fazer

- **não trocar o `faster-whisper` por um motor de streaming como padrão.** O
  offline é a verdade; o streaming é conveniência;
- **não deixar a prévia ao vivo escrever no `transcricao.json`.** Se o rascunho
  chegar ao disco, a retomada vai lê-lo como parcial e pular o ASR de verdade —
  é o defeito de 0.4.0 de volta, com outra roupa;
- **não ligar nada disso por padrão** enquanto o desligamento do §4.1 estiver
  aberto;
- **não fazer disto uma fase de execução** sem os números do §5. A Fase 6 nasceu
  de uma comparação com *n* = 1 e passou três semanas consertando o que aquela
  amostra não mostrava.

---

## 8. Fontes

- [Streaming Sortformer no Hugging Face](https://huggingface.co/nvidia/diar_streaming_sortformer_4spk-v2)
- [Streaming Sortformer, artigo (Interspeech 2025)](https://www.isca-archive.org/interspeech_2025/medennikov25_interspeech.pdf)
- [Multitalker Parakeet Streaming](https://huggingface.co/nvidia/multitalker-parakeet-streaming-0.6b-v1)
- [Anúncio da NVIDIA](https://developer.nvidia.com/blog/identify-speakers-in-meetings-calls-and-voice-apps-in-real-time-with-nvidia-streaming-sortformer/)
- [whisper_streaming (LocalAgreement)](https://github.com/ufal/whisper_streaming)
