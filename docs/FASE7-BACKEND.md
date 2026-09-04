# Fase 7 — o MOSS como motor opcional: tarefas

Plano fechado em 03/09/2026, a partir das medições da
[FASE7-RESULTADOS.md](FASE7-RESULTADOS.md). **Cada tarefa é escrita para ser
entregue a alguém que não acompanhou a medição** — traz o que fazer, onde, como
saber que acabou, e as armadilhas que já custaram tempo.

## O princípio, e ele não é negociável

O MOSS faz texto **e** falante numa passada. No pipeline de hoje isso são duas
etapas — o `RodarAsrAsync` e a chamada ao motor de diarização. **Tudo o que vem
depois delas não muda.**

```
Faixas.Ler(mic, sistema)
        │
        ├─ motor == "classico" ─►  RodarAsrAsync(mix) ; diarizar(system.wav) ─┐
        │                                                                     │
        └─ motor == "moss" ─────►  MossEmBlocos(mix) ; CosturaDeFalantes ────┤
                                                                              ▼
                    ───────── daqui para baixo, IDÊNTICO ao de hoje ─────────
                    VozDoDono.Trilha + Juntar · Montagem.RepartirPorFalante
                    FiltroDeSilencio · CorrecaoFonetica · RevisaoDeTermos
                    AprendizadoDeVozes.ReconhecerAsync · Retomada.Escrever
```

Isso não é conveniência de implementação. É o que **preserva as peças que as
medições provaram necessárias**: a `CorrecaoFonetica` e a `RevisaoDeTermos` são
o que recupera a parte "de grafia" do vocabulário que o MOSS perde
([§12](FASE7-RESULTADOS.md)), e a `VozDoDono` é o que dá o dono de graça pela
faixa do microfone.

**Com a chave em `classico`, o caminho executado é o de hoje.** A régua da fase B
é a suíte: os 504 testes têm de passar **sem alteração nenhuma**. Se algum
precisar mudar, a bifurcação vazou para baixo e o desenho está errado.

---

## A. O sidecar — fora do app

### A1 · Empacotar o `transcribe.cpp` no Python embarcado

**Onde:** `tools/empacotar_motores.sh`, e a régua de tamanho do
`tools/montar_instalador.sh`.

**O quê:** acrescentar `transcribe-cpp` e o nativo com CUDA ao Python embarcado.
O wheel nativo vem do release do GitHub, não do PyPI — o do PyPI instala um stub
de versão `0.0.0` que não registra o backend de CUDA:

```
transcribe_cpp_native_cu12-0.2.3-py3-none-manylinux_2_27_x86_64.manylinux_2_28_x86_64.whl
```

E baixar o GGUF `MOSS-Transcribe-Diarize-Q5_K_M.gguf` (0,70 GB) de
`handy-computer/moss-transcribe-diarize-gguf`.

**Pronto quando:** o `montar_instalador.sh` passa nas réguas e o instalador é
medido. **Esperar ~200 MB de nativo + 0,70 GB de modelo.** Se passar muito disso,
parar e reportar antes de seguir — o instalador tem 1,59 GB e o `DIST-1` do
backlog quer encolhê-lo, não engordá-lo.

**Armadilha:** `EmbeddedResource` com barra invertida não expande glob no MSBuild
em Linux — compila, publica, passa nos testes, e o recurso não está lá.

---

### A2 · Escrever `motores/moss/motor.py`

**Onde:** arquivo novo, ao lado de `motores/asr/motor.py`.

**O quê:** um sidecar que fala o protocolo do [SIDECAR.md](SIDECAR.md) — uma
linha de JSON por mensagem, `stdout` só para o protocolo. Op nova:

```json
{"id": 1, "op": "transcrever_e_separar", "audio": "C:\\...\\bloco.wav"}
```

Resposta no mesmo formato de segmento dos outros motores, com **`falante`
preenchido**:

```json
{"id": 1, "tipo": "resultado", "duracao": 180.0,
 "segmentos": [{"inicio": 0.9, "fim": 2.3, "texto": " olá", "falante": "S1"}]}
```

**Regras que não podem ser esquecidas:**

* **duplicar o fd 1 antes de qualquer import**, como os outros motores. O ggml
  escreve no stdout e uma linha dele corrompe o protocolo;
* **não passar `language`.** O build GGUF declara `languages = ('en','zh')` e
  **recusa `pt`** com `UnsupportedRequest` — mas transcreve português
  corretamente quando o parâmetro é omitido. A lista do port está subdeclarada;
* **tratar `OutputTruncated` por bloco e seguir.** O modelo já estourou o teto de
  geração uma vez e a biblioteca **levantou exceção**; num sidecar isso derrubaria
  a transcrição inteira. Bloco que falha devolve `erro` e a orquestração decide;
* o motor fica quente entre requisições — carregar o modelo a cada bloco pagaria
  1,3 s por bloco à toa.

**Pronto quando:** sobe, responde `pronto`, transcreve uma gravação do acervo e
**a saída bate com a que o `tools/medir_moss.py` já produziu** para o mesmo
áudio.

---

### A3 · Teste do sidecar, sem o app

**Onde:** `app-net/Tests/`, no padrão dos testes de sidecar que já existem.

**O quê:** subir o motor, mandar um bloco curto, conferir formato e cancelamento
(matar o processo devolve a VRAM em ≤0,3 s — critério B da Fase 2).

**Pronto quando:** passa na suíte e o teste roda sem GPU disponível também
(pulando, não falhando).

---

## B. O núcleo — a bifurcação

### B1 · `Motores` ganha `ScriptMoss`

**Onde:** `Nucleo/Transcritor.cs`, o `record Motores` na linha 17.

**O quê:** mais um caminho, `motores/moss/motor.py`, no mesmo padrão dos outros.

**Pronto quando:** compila e o `Diagnostico` mostra o caminho novo.

---

### B2 · `Nucleo/MossEmBlocos.cs`

**O quê:** corta o mix em blocos de **3 minutos**, chama o sidecar bloco a bloco,
desloca os carimbos e junta.

**Por que em C# e não no sidecar:** quem já leu as faixas é o núcleo
(`Faixas.Ler`), e mandar caminho de arquivo por bloco escreveria 20 WAVs
temporários numa reunião de uma hora. **E é o mesmo desenho que o ao vivo vai
precisar**, quando o áudio vier da captura em vez do arquivo.

**Três coisas obrigatórias:**

* **bloco de 3 min, nunca passada inteira.** Não é otimização, é requisito: a
  passada inteira do MOSS não escala nesta placa — foi interrompida depois de
  **1h36** numa gravação de 32 min, a 0,35× o tempo real
  ([§7.2](FASE7-RESULTADOS.md));
* **portão de RMS antes de mandar o bloco.** Use o `VozDoDono.LimiarDeFala`
  (`1e-2`). Bloco mudo faz o MOSS **alucinar em chinês** — ele completa o prompt
  padrão embutido no GGUF. No mix isso apareceu em 1 bloco de 579 no acervo, mas
  o custo do portão é zero e o modo de falha é silencioso;
* **rótulo local por bloco.** O `S1` do bloco 3 não é o `S1` do bloco 7. Marcar a
  origem (`b3_S1`) até a costura rodar.

**Pronto quando:** transcreve uma gravação do acervo e o resultado bate com o do
`tools/medir_moss.py`.

---

### B3 · `Nucleo/CosturaDeFalantes.cs`

**O quê:** rótulo local do bloco → identidade global, por vetor de voz, **em
ordem de chegada e sem olhar o futuro** — porque ao vivo não há futuro para
olhar.

O algoritmo, medido em [§11](FASE7-RESULTADOS.md): percorre os blocos em ordem;
para cada falante do bloco monta um vetor com a fala dele; compara com as
identidades conhecidas; **acima do limiar** é a mesma pessoa e o centroide é
atualizado; abaixo, é gente nova.

**Reusa o `vetor_de_voz`** que o motor de diarização já expõe — o
`AprendizadoDeVozes.ExtrairAsync` já o chama, e o modelo de voz é o mesmo. Não
carregar modelo próprio.

**Limiar: 0,55.** Medido: a acurácia é quase insensível ao limiar, mas a
**contagem de pessoas não é** — em 0,75 a costura conclui que há 44 falantes onde
há 8. Em 0,45 começa a fundir demais.

**Pronto quando:** sobre as gravações do acervo, reproduz os números da §11.1 —
acerto de falante ~1 ponto abaixo do que a régua dava de graça.

> **Custo conhecido:** mesmo em 0,55 ela **divide gente demais** — 15 identidades
> para 8 pessoas na reunião de 122 min. Isso não é conserto desta tarefa; é o que
> promove o `VOZ-1` do [BACKLOG.md](BACKLOG.md) a requisito (tarefa E3).

---

### B4 · A marca de motor na `Retomada`

**Onde:** `Nucleo/Retomada.cs`.

**O quê:** o parcial passa a registrar **qual motor o produziu**, e
`Retomada.Ler` recusa um parcial de outro motor.

**Por que é crítico:** hoje o `Ler` confere modelo, idioma e vocabulário. Um
parcial do MOSS bateria nos três e seria reaproveitado numa rodada clássica —
que **pularia o ASR** e devolveria texto do MOSS rotulado como clássico, em
silêncio. É o defeito da 0.4.0 com outra roupa.

**Pronto quando:** há teste que transcreve com um motor, retranscreve com o
outro, e prova que o ASR rodou de novo.

---

### B5 · A chave em `ConfiguracoesDoApp`

**O quê:** `motor_de_transcricao`, **padrão `"classico"`**.

**Pronto quando:** o `app.json` aceita a chave e o padrão sobrevive a um arquivo
sem ela.

---

### B6 · A bifurcação no `Transcritor`

**Onde:** `Nucleo/Transcritor.cs`, entre o `Faixas.Ler` e o `VozDoDono.Trilha`.

**O quê:** o parâmetro novo e o `if`. Nada abaixo da bifurcação.

**Pronto quando:** **os 504 testes passam sem alteração nenhuma.** É a régua da
fase inteira.

---

### B7 · Origem nas vozes aprendidas

**Onde:** `Nucleo/Vozes.cs` e `Nucleo/AprendizadoDeVozes.cs`.

**O quê:** a amostra de voz guarda com qual motor de diarização a identidade foi
formada.

**Por quê:** transcrever com o MOSS **não** contamina o banco — o pipeline só
chama `ReconhecerAsync`, que lê e não escreve. Mas **nomear** um falante olhando
para uma transcrição do MOSS grava um vetor que atravessa para todas as reuniões
seguintes, e retranscrever não desfaz. Se a costura tiver fundido duas pessoas, o
vetor sai contaminado.

A origem permite desfazer em bloco se algo der errado. É barato agora e caro
depois.

**Pronto quando:** vozes aprendidas nos dois modos são distinguíveis no arquivo, e
há teste.

---

## C. A tela e o RC

### C1 · A chave em Ajustes › Transcrição

**Fora deste plano** — é a auditoria de interface que corre em paralelo. O texto
precisa dizer o que muda, **inclusive que o vocabulário funciona diferente**.

### C2 · Montar o RC

Como a chave é por transcrição e a gravação fica salva, **dá para transcrever a
mesma reunião nos dois motores e comparar**. A migração é reversível a qualquer
momento.

---

## D. Só depois do RC

### D1 · O MOSS competindo com a gravação

A ferramenta existe: `tools/carga_de_bloco.py`, apontada para o motor novo. O
perfil do MOSS é diferente do medido (~9% de ciclo contra ~20%), então o T0.2
precisa ser refeito.

### D2 · Auditoria estrutural da ata

O `tools/auditar_atas.py` precisa do `ata.json`, que sai do `RedatorDeAta` em C#.
As medições de fora só alcançaram a transcrição e o conteúdo — **a estrutura da
ata do MOSS nunca foi auditada**.

---

## E. Independentes — PR próprio cada um

| # | tarefa | por quê |
|---|---|---|
| **E1** | `RevisaoDeTermos` colar variantes de espaço e hífen | O MOSS escreve `next best` por `nextbest` e `lifecycle` por `life cycle`. **Ajuda os dois motores**, e fecha boa parte da perda de vocabulário (§12) |
| **E2** | `SUP-1` — instrumentar o fim do pipeline | Duas linhas das cinco são de uma linha cada. **Deveria vir antes** de qualquer coisa chegar à máquina do segundo usuário |
| **E3** | `VOZ-1` — sugerir fusão de perfis parecidos | Deixou de ser conveniência: a costura divide 8 pessoas em 15 identidades |
| **E4** | Consertar `tools/medir_motor_de_ata.py` | **Está quebrado**: `GRAVACOES` aponta para `Documents/MeetingRecordings`, que sumiu quando o Documents virou OneDrive; e `SKILL_ZIP` aponta para um `.skill` zipado que virou arquivos soltos em `assets/atas/` |
| **E5** | Atualizar a `ATA.md` | Ela registra o Qwen3-4B como motor de ata; o `app.json` desta máquina usa **`gemma-4-e4b-q4km.gguf`**. Quem ler a documentação mede o motor errado — aconteceu comigo |
| **E6** | Separar o `LimiarDeReconhecimento` | Duas medições independentes dizem que 0,70 é apertado demais **dentro** da mesma reunião (0,55–0,60), mas ele também governa o banco **entre** reuniões, que é o caso difícil |

---

## O que este plano deliberadamente não faz

* **não troca o padrão** — o clássico segue sendo o motor de todo mundo;
* **não toca em `Gravacao/` nem `Captura/`** — é o que o [CLAUDE.md](../CLAUDE.md)
  marca como o que não se reabre;
* **não faz transcrição ao vivo** — isto é o motor; o produto A da
  [FASE7.md](FASE7.md) é outra fase e depende desta;
* **não chama o `transcribe.cpp` por P/Invoke.** Ele tem C API e seria tentador,
  mas cancelar é matar o processo, e este app **está gravando uma reunião**: um
  *segfault* no ggml mata um processo descartável hoje, e mataria o processo que
  segura o áudio em P/Invoke. Trocar o sidecar Python por um `Sidecar.exe` em C#
  depois é mudança interna a ele.
