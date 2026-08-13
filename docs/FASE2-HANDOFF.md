# Fase 2 — onde parou e o que falta

> **A Fase 2 foi dada por concluída pelo dono do produto em 12/08/2026**, com os
> cinco critérios de aceite fechados. A continuação é a
> [FASE2.5.md](FASE2.5.md) — juntar o app e o gravador num executável só.

Documento de passagem de bastão. A carta de execução é a
[FASE2.md](FASE2.md); aqui está o estado real contra ela, em 12/08/2026.
O equivalente da fase anterior é a [FASE1-HANDOFF.md](FASE1-HANDOFF.md), e
tudo o que ela diz sobre o gravador continua valendo.

Branch: `feat/recorder-and-accuracy`. Nada foi para `main`, e nada foi
empurrado para o remoto — as duas coisas são decisão do dono do produto.

---

## 1. Estado em uma frase

O app nativo **transcreve de ponta a ponta e entrega arquivo**: escolher a
gravação, preparar com cliente/projeto/modelo, rodar os motores no Windows sem
WSL nenhum, ler e corrigir o texto ouvindo o trecho, nomear os falantes, e
exportar em TXT/SRT/VTT/DOCX com cabeçalho da reunião. **O dono do produto rodou
o ciclo completo numa reunião real em 11/08 e o aceitou.**

O que falta não é o fluxo, nem a moldura, nem mais o que estava atrás dela: as
telas de gestão existem e funcionam — baixar e remover modelo, ouvir o trecho de
cada voz, as trocas fonéticas visíveis e reversíveis, os parâmetros por projeto.

**A fase foi encerrada em 12/08 com os cinco critérios fechados.** O que
continua aberto não bloqueia nada: a evidência de que a biblioteca de vozes
funciona com áudio real (§4) — que só duas gravações da mesma pessoa fecham —, o
filtro de silêncio sem chave na UI, e o histórico.

A continuação é a [FASE2.5.md](FASE2.5.md): o app e o gravador viram um
executável só, com bandeja e janela no mesmo processo.

---

## 2. O mapa do código

Quem chega novo deve ler nesta ordem: `Transcritor.cs` (o pipeline inteiro em
um arquivo), `Ponte.cs` (tudo o que a tela pode pedir) e `revisao.js` (a tela
onde o usuário passa 90% do tempo).

| onde | o que é |
|---|---|
| `app-net/Nucleo/Transcritor.cs` | o pipeline: mix → ASR → diarização → montagem → vozes conhecidas → `transcricao.json` |
| `app-net/Nucleo/Faixas.cs` | leitura das duas faixas, mix, RMS por intervalo |
| `app-net/Nucleo/Transcricao.cs` | os tipos que a UI consome, e a montagem (atribuir falantes, atribuir dono) |
| `app-net/Nucleo/Vozes.cs` | a biblioteca de vozes v2: procedência, sub-perfis, quarentena |
| `app-net/Nucleo/AprendizadoDeVozes.cs` | extrair amostra ao nomear, reconhecer na transcrição seguinte |
| `app-net/Nucleo/Exportacao.cs` | os quatro formatos, com cabeçalho opcional |
| `app-net/Nucleo/Cabecalho.cs` | os dados da reunião que abrem o arquivo exportado |
| `app-net/Nucleo/Projetos.cs` | lê e escreve o `projects.json` do app Python, preservando chaves que não conhece |
| `app-net/Nucleo/ConfiguracoesDoApp.cs` | `~/.meeting-transcription/app.json` |
| `app-net/Nucleo/Catalogo.cs` | os pacotes de modelo e o que já está nesta máquina — ver §10 |
| `app-net/Sidecar/` | o cliente NDJSON: `Protocolo.cs` e `MotorSidecar.cs` |
| `app-net/App/` | a janela Win32 crua, o WebView2, a ponte JSON |
| `app-net/App/web/` | a interface: 2304 linhas de HTML/CSS/JS, sem framework |
| `app-net/Cli/` | o mesmo pipeline por linha de comando — é por aqui que se mede |
| `motores/asr/`, `motores/diarizacao/`, `motores/modelos/` | os sidecars Python |

**A ponte** aceita: `gravacoes`, `transcricao`, `transcrever`, `clientes`,
`prefs`, `salvar-projeto`, `salvar-transcricao`, `aprender-voz`, `exportar`,
`config`, `salvar-config`, `catalogo`, `baixar-pacote`, `remover-pacote`,
`vozes`, `aprovar-voz`, `esquecer-voz`.
Toda tela nova começa por adicionar um caso ali.

**As telas** são três: a lista de reuniões e o preparo (`app.js`), a revisão
(`revisao.js`) e os ajustes (`configuracoes.js`). O trilho à esquerda é o único
elemento que nunca sai da tela, e é por ele que se troca de destino.

**A UI não tem framework** e não vai ter: o design system do projeto é CSS puro
(não existem componentes React), então trazer React seria trazer um build para
não usar nada dele. `pecas.js` tem as poucas peças reaproveitadas — `secao`,
`campo`, `campoComSugestoes`, `abrirGaveta`, `alerta`, `corDoFalante`.

---

## 3. Contra a carta

### Ordem de trabalho (§115 da FASE2.md)

| passo | estado |
|---|---|
| 1. contrato do sidecar | ✅ 08/08 — [SIDECAR.md](SIDECAR.md), cancelamento medido (VRAM de volta em ≤0,3 s) |
| 2. pipeline por CLI + empacotamento | ✅ 09–10/08 — paridade **byte a byte** com o Gradio em duas gravações; motores rodando no Windows sem WSL |
| 3. UI na ordem do fluxo | ✅ o fluxo principal; ✅ 11/08 a moldura das telas de gestão (vozes, clientes, modelos); ❌ histórico |
| 4. correção fonética + filtro de silêncio | ✅ a fonética aparece e se desfaz na UI (§12); o filtro de silêncio roda no núcleo e **ainda não tem chave na tela** |
| 5. paridade final e aposentadoria do Docker | ✅ a paridade foi dispensada; a aposentadoria foi autorizada em 12/08 |

### Critérios de aceite (§96 da FASE2.md)

| | estado |
|---|---|
| **A.** as 27 entregas do FEATURES ponta a ponta, igual ou melhor que o Gradio | ✅ **aceito em 11/08 pelo dono do produto**, que rodou o ciclo completo — gravação, transcrição, diarização — e validou os cabeçalhos da exportação. A comparação medida contra o Gradio foi **dispensada**: ver a nota abaixo |
| **B.** matar o app não deixa motor órfão; cancelar libera a GPU em ≤2 s | ✅ **12/08** — cancelar já era medido (≤0,3 s); matar o app **era impossível de funcionar** e foi corrigido com um Job Object. O dono do produto fechou o app pelo Gerenciador de Tarefas e **confirmou a VRAM caindo**. Ver §13 |
| **C.** motor que morre devolve erro legível e o app continua vivo | ✅ **quatro testes de injeção de falha** em `MotorSidecarTests`: morre no handshake, erro na requisição, morre no meio, lixo no stdout. O último salto (a `Ponte` responder `erro` em vez de derrubar a janela) é por inspeção — ver §13 |
| **D.** as trocas da correção fonética visíveis e inspecionáveis na UI | ✅ **11/08** — cada trecho corrigido ganha marca ✎ com as trocas, um filtro no topo mostra só os corrigidos, e clicar na marca desfaz. Ver §12 |
| **E.** Docker/Gradio aposentado | ✅ **12/08 — autorizado pelo dono do produto.** O que sai do repositório está listado em §16 |

**Sobre o critério A, e por que ele fechou sem a medição.** A carta pedia
paridade medida contra o Gradio. O dono do produto dispensou a medição depois de
usar o app numa reunião real de ponta a ponta. Vale registrar o que isso custa:
o Gradio era o oráculo, e comparar com ele pegou três defeitos do porte que
nenhum teste de unidade pegaria (§208 da FASE2.md). Aposentá-lo sem a comparação
final significa que **defeitos de porte remanescentes, se existirem, só
aparecerão em uso.** Foi uma decisão informada, não um esquecimento — e é por
isso que está escrita aqui.

**Os cinco critérios estão fechados.** A fase está encerrada.

---

## 4. O que nunca rodou de verdade

Esta seção existe para ninguém confundir "implementado" com "funciona".

1. **A biblioteca de vozes nunca completou o ciclo com áudio real.** Estão
   provados em separado: a extração de vetor pelo motor (256 dimensões, norma
   0,98) e a biblioteca em memória (testes de `Vozes.cs`). **Não** está provado
   o ciclo inteiro — nomear "Vanessa" numa reunião, e ela chegar nomeada na
   reunião seguinte. É o próximo teste a fazer, e precisa de duas gravações da
   mesma pessoa. Ver [VOZES.md](VOZES.md) para o desenho e os limiares
   (reconhecer 0,70; quarentena 0,35; mínimo 3 s de fala).
   **A aba Vozes dos ajustes existe agora e é o instrumento deste teste:** depois
   de nomear um falante, a pessoa tem que aparecer lá, com a procedência da
   amostra visível.
2. **Nenhuma medição de tempo do app inteiro.** Sabe-se que os motores rodam a
   ~4,5× o tempo real no Windows; não se sabe quanto o app adiciona.

Saíram desta lista em 11/08, validados pelo dono do produto: o **cabeçalho da
exportação** (conferido na tela), o **ciclo completo** de gravação a diarização,
e o **soak de 1 h do gravador** com o binário atual.

---

## 5. Pendências, na ordem que o dono do produto pediu

As referências chegaram em 11/08 (prints do Meetily, em `meetily_references/`) e
destravaram os três itens que esperavam por elas. A ordem virou **a moldura
primeiro, as funcionalidades depois** — decisão do dono do produto: montar a UI
inteira para validar o desenho antes de construir o que falta atrás dela.

**Feito em 11/08 (a moldura):** o trilho de destinos à esquerda, os ajustes como
tela com cinco abas numa coluna à esquerda (Geral, Modelos, Transcrição,
Clientes, Vozes), o catálogo de modelos lendo o estado real do cache, e a lista
de vozes com procedência e as ações de aprovar e esquecer. Ver §10.

**O que falta atrás dela, na ordem:**

1. ~~Baixar e remover modelo pela tela~~ — **feito em 11/08**, pelo
   `motores/modelos/motor.py`. Ver §11.
2. ~~Ouvir o trecho de cada amostra de voz~~ — **feito e confirmado pelo dono do
   produto em 11/08.** A pasta de vozes é servida como `vozes.local`, pelo mesmo
   mapeamento de host que já servia o `mix.wav`.
3. ~~Critério D~~ — **feito em 11/08**. Ver §12.
4. ~~Ajustar os parâmetros de cada projeto pela tela~~ — **feito em 11/08**, a
   pedido do dono do produto: clicar num projeto abre modelo, idioma, diarização
   e vocabulário dele, numa sanfona. **Renomear e apagar** continuam de fora;
   cliente e projeto novos ainda nascem no preparo da reunião.
5. **O filtro de silêncio não tem chave na UI** — existe no núcleo e só liga por
   linha de comando. É a última pendência pequena da aba Transcrição.
6. **O espelho do gravador dentro do app.** O trilho já reserva o lugar,
   desabilitado. Decisão registrada do dono do produto: a bandeja **continua**, e
   o espelho é adição, não substituição. Fica para depois das telas acima.

Da fase anterior, ainda abertos: **o clique do OAuth inicial** e **a decisão de
tirar o `recorder/` Python de uso**. O soak de 1 h foi refeito e passou.

---

## 6. Como trabalhar aqui

```bash
export PATH="$HOME/.dotnet:$PATH"

# testes — sempre verdes antes de commitar
dotnet test app-net/Tests/MeetingApp.Tests.csproj

# publicar e instalar. Desde a Fase 2.5 sempre por aqui: o script confere as
# réguas (tamanho, token, ícones da bandeja) antes de copiar, e o destino
# padrão é a pasta de teste, não a instalação em uso.
tools/publicar.sh

# iterar na interface sem recompilar
MeetingApp.exe --web C:\caminho\para\app-net\App\web
```

Os motores ficam em `C:\Users\andre\MeetingApp\motores` (4,3 GB, fora do
repositório), montados por `tools/empacotar_motores.sh` a partir do WSL — o
`uv` baixa wheels `win_amd64` daqui com `--python-platform x86_64-pc-windows-msvc`.

O `.exe` publicado corretamente tem **~15,7 MB** e roda numa máquina sem .NET
instalado. É a mesma escolha da bandeja, pelo mesmo motivo: `SelfContained` fica
`false` no `.csproj` para o loop de desenvolvimento ficar rápido, e só a linha
de comando de publicação o liga.

### A cultura, em quatro linhas

Medir antes de decidir. Afirmação forte pede número. Desvio da carta é
permitido, mas fica registrado **no documento**, com a medição que o justifica.
Commit pequeno que narra o que a execução ensinou. `TreatWarningsAsErrors`
continua ligado.

---

## 7. Armadilhas que já custaram caro

As de Win32 estão na [FASE1-HANDOFF.md](FASE1-HANDOFF.md) §3 e continuam
valendo — a janela do app é a mesma técnica. Estas são da Fase 2:

- **`stdout` corrompe o protocolo.** O sidecar fala NDJSON pela saída padrão, e
  qualquer `print` de biblioteca no meio quebra a conversa. O motor duplica o
  fd 1 na entrada e redireciona o `stdout` do Python para o `stderr`. Ao
  adicionar biblioteca nova ao motor, é a primeira coisa a verificar.
- **`torch` do PyPI no Windows é CPU-only**, e o `ctranslate2` precisa das DLLs
  de CUDA que só vêm no índice do PyTorch. O `_achar_cuda()` do motor registra
  o diretório de DLLs do torch antes de importar o resto; sem isso a GPU some
  em silêncio e tudo fica 20× mais lento.
- **`torchcodec` está acoplado à versão do `torch`.** Ler WAV por ele quebra a
  cada atualização; `_ler_wav()` lê com `soundfile` e evita o caminho inteiro.
- **`--no-build` mede o passado.** Uma correção foi dada como sem efeito porque
  a medição rodou sobre o binário anterior. Se mediu, recompile antes.
- **Nunca matar um `MeetingApp` em execução.** Já custou uma transcrição do
  usuário no meio. Se o app estiver aberto, publique como `-novo.exe` e avise;
  `tools/ver_ui.ps1` se recusa a rodar com um app aberto, e fecha o que abre.
- **Screenshot e clique sintético foram abandonados.** As janelas abriam em
  telas arbitrárias e os cliques acertavam o editor do usuário. Peça para uma
  pessoa olhar.
- **`dotnet publish` sem as flags produz um app que não abre.** O `.csproj` tem
  `SelfContained=false` de propósito, e publicar sem
  `--self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true`
  gera um `.exe` de 193 KB que depende de DLLs soltas — sozinho, ele não abre.
  A régua está no tamanho: **~15,7 MB é certo, menos de 1 MB é errado.**
- **O `USERPROFILE` do MSBuild é vazio no WSL.** Publicar sem
  `-p:TokenHuggingFace=...` produz um binário **sem o token embutido**, que
  compila, publica e só falha na diarização, na máquina do usuário. Confira com
  `strings MeetingApp.exe | grep -c hf_token` (tem que dar 1).
- **As duas armadilhas acima aconteceram no mesmo dia (11/08), e as duas foram
  entregues ao usuário.** As duas se evitam pelo mesmo hábito, que é o resto
  deste projeto inteiro: depois de publicar, **abra o app e veja**. Custa oito
  segundos, e é a diferença entre "publiquei" e "funciona".
  ```powershell
  $p = Start-Process 'C:\Users\andre\MeetingApp\MeetingApp.exe' -PassThru
  Start-Sleep -Seconds 8
  if ($p.HasExited) { "MORREU: $($p.ExitCode)" }
  else { (Get-Process -Id $p.Id).MainWindowTitle; Stop-Process -Id $p.Id -Force }
  ```
  Feche o que você abrir — janela deixada aberta já foi confundida com o
  resultado do próprio usuário, que editou a gravação errada por causa disso.

---

## 8. As duas credenciais, e por que elas são diferentes

Nenhuma das duas está no repositório. Ambas entram no executável na hora de
publicar, a partir do perfil de quem publica, e só se o arquivo existir.

- **`google_client_secret.json`** — segredo de cliente OAuth do tipo
  "aplicativo instalado". O próprio Google documenta que **não é secreto**:
  quem protege o fluxo é o PKCE, que está implementado. O limite real não é
  esse arquivo, é o projeto estar em modo *Testing* no Google Cloud — só
  autoriza contas cadastradas e expira todo refresh token em 7 dias.
- **`hf_token.txt`** — este **é** secreto de verdade: dá acesso à conta
  HuggingFace de quem publica. Use um token só de leitura, criado para esta
  finalidade e revogável. Está embutido porque exigir que cada usuário crie
  conta no HuggingFace e aceite os termos do pyannote inviabilizaria o produto
  — foi decisão explícita do dono, com a gestão ficando com quem desenvolve.

---

## 9. O que a execução ensinou nesta fase

Registro do que não estava nos documentos e só apareceu rodando.

- **O usuário ouviu o que três métricas objetivas não pegaram.** O craquelado
  era aliasing do resampler sem filtro sinc; alias em −43,3 dB passava por
  todos os testes automáticos. Com `sinc_size: 256`, −109,9 dB. Quando ele diz
  que o áudio está estranho, é porque está.
- **A hipótese do usuário sobre o próprio ambiente venceu a minha.** O
  desalinhamento no headset era troca de perfil Bluetooth (A2DP → mãos-livres),
  como ele suspeitou. A âncora por carimbo QPC, tecnicamente mais correta,
  perdia em campo; voltamos à do Python, por decisão dele, com a dívida
  registrada.
- **Comparar com o app antigo pegou três defeitos do porte** que nenhum teste
  de unidade pegaria — está em [FASE2.md](FASE2.md) §208. Enquanto o Gradio
  existir, ele é o oráculo. Depois de aposentá-lo, não é mais: é mais um motivo
  para o critério A ser medido antes, e não depois.
- **Um teste que falha pode estar denunciando uma funcionalidade que falta.** O
  `OReconhecimentoUsaOMelhorSubPerfil` falhou porque, por desenho, condição nova
  cai em quarentena — e não havia como sair dela. Nasceram daí `Aprovar()` e
  `EmQuarentena()`.
- **Commitei uma vez com a suíte vermelha.** O `&&` estava encadeado depois de
  um `tail`, que sempre devolve sucesso. Verifique o resultado do teste, não o
  do pipe.

---

## 10. O que as referências do Meetily ensinaram

Em 11/08 o dono do produto trouxe seis capturas do Meetily instalado — duas do
instalador, quatro da tela de configurações. Estão em `meetily_references/`. O
que ficou decidido a partir delas.

### O que copiamos

- **Ajustes em blocos, um assunto por cartão, com título e descrição dentro.**
  Funciona porque cada bloco se lê sozinho: dá para mudar uma coisa sem ler as
  outras.
- **A tabela de modelos como dado, e não como `<option>` no JavaScript.** Virou
  [`Nucleo/Catalogo.cs`](../app-net/Nucleo/Catalogo.cs): id, nome, descrição,
  repositório e **tamanho esperado**, num lugar só. O tamanho não é enfeite — é
  o que permite dizer quanto vai custar antes de custar, e é a detecção de
  corrupção que eles fazem e que é barata e eficaz.
- **Cartão de modelo com o custo à vista.** Diferença deliberada: onde eles põem
  adjetivos ("High accuracy", "Slow processing"), nós pomos o que medimos, e a
  tela marca quando o número é aproximado em vez de medido.

### O que não copiamos, e por quê

O instalador deles tem 43,3 MB porque linka o `whisper-rs` estático e **sem
CUDA** — para ter GPU no Windows o usuário recompila. O nosso é maior por
desenho, e isso está aceito: GPU sem o usuário compilar nada é requisito.
Registrado para ninguém tomar os 43 MB como meta.

**A distinção que evita a expectativa errada:** os nossos 4,3 GB de `motores/`
são **runtime** (Python + torch + pyannote), não modelo. Uma tela de modelos não
encolhe o instalador. O que encolheria é o motor virar pacote baixável também —
CUDA/Vulkan/CPU como variantes, que é o desenho de "motores como plugins" da
[PLANO.md](PLANO.md) §5, ainda não construído.

### O achado que mais importa

**Nós já baixamos modelo em tempo de execução — invisível e sem controle.** O
`motor.py` chama `WhisperModel("large-v3")` e o faster-whisper puxa ~3 GB do
HuggingFace na primeira transcrição: sem barra de progresso, sem anunciar o
tamanho, sem verificar o que chegou. É por isso que o token do HuggingFace
precisa estar embutido no `.exe`, e é por isso que a primeira transcrição de uma
máquina nova trava por minutos sem explicar.

A tela de modelos, portanto, **não é funcionalidade nova: é o conserto de um
buraco que já existe.** O `Catalogo` é a metade que responde "está aí?", lendo o
cache de verdade (e respeitando `HF_HOME` e `HF_HUB_CACHE`, senão quem move o
cache de disco vê "ausente" sobre 3 GB presentes). A metade que baixa entra
atrás do mesmo contrato, sem a tela mudar.

### O que ficou para depois

Os sete `templates/*.json` que o instalador deles extrai — formatos de ata por
tipo de reunião — são a ideia mais reaproveitável do conjunto, e casam com o
vocabulário por projeto que já temos. **Só fazem sentido quando existir o motor
de resumo por LLM**, que ainda não começou. Registrado para não se perder.

---

## 11. O motor de modelos, e a medida de progresso que sobreviveu

Feito em 11/08. `motores/modelos/motor.py`, o terceiro sidecar, com uma só
operação: `baixar`. A tela de Modelos agora baixa, retoma e remove.

**Por que em Python e não em C#.** O que o app precisa não é baixar arquivos: é
produzir **exatamente o layout de cache que o faster-whisper e o pyannote vão
procurar depois** — a árvore `blobs`/`refs`/`snapshots` com os links, que é
formato interno da `huggingface_hub`. Reimplementá-lo em C# criaria um segundo
dono de um formato que não é nosso, e errar teria o pior sintoma possível: o
modelo baixa, a tela diz "instalado", e o motor baixa tudo de novo por não
reconhecer o que está lá.

**A primeira tentativa de progresso estava errada, e o teste pegou.** A
`snapshot_download` aceita um `tqdm_class`, e parecia ser o gancho óbvio. Medido:
ele alimenta só a barra **externa**, que conta *arquivos* — a saída real foi
`pct: 0.16` e `"0 MB de 0 MB"`. Num modelo em que um arquivo é 3 GB dos 3,09 GB,
essa barra ficaria parada em "1 de 6" o download inteiro. Chegar aos bytes por
esse caminho exigiria remendar `huggingface_hub.file_download`, que é interno.

**O que ficou no lugar:** uma thread mede o **tamanho da pasta em disco** a cada
segundo contra o **tamanho esperado do catálogo** — o mesmo número que detecta
pacote corrompido. Não depende de nenhum interno da biblioteca, e reusa um dado
que já existia. Medido com o `base` (147,9 MB): `3 MB de 148 MB` → `148 MB de
148 MB`. A fração trava em 0,99; o 1,0 vem do C#, depois de reler o disco —
barra que enche antes do fim ensina a não confiar nela.

Três decisões menores que vale não redescobrir:

- **`pasta` e `tamanho_esperado` vão do C# para o motor**, em vez de o motor
  deduzi-los. O `Catalogo` é o dono desses números; dois lados calculando o
  caminho do cache é garantir que um dia discordem.
- **Remover monta o caminho a partir do catálogo, nunca do que a página manda.**
  Caminho vindo da tela seria um apagador recursivo controlado pelo HTML.
- **O botão de remover fica desabilitado no modelo em uso.** Sem isso o app
  ficaria sem motor e o erro só apareceria na transcrição seguinte.

### A armadilha nova: os sidecars não estão dentro do `.exe`

Eles moram em `motores/`, que tem 4,3 GB e não se reempacota a cada mudança.
Corrigir um `motor.py` no repositório e publicar **não** o atualiza na máquina de
quem usa — o app abre normalmente, e só a operação alterada se comporta como
antes. O `tools/publicar.sh` agora sincroniza os três `motor.py` junto com o
`.exe`, e é por isso que ele existe.

---

## 12. O critério D, e o que ele obrigou a decidir

A correção fonética existe por uma medição: "Dimi" saía como **"Jimmy" 10 vezes**
(FASE0, resultado 5). Ela funcionava desde sempre e **não deixava rastro** — o
`Transcritor` usava o texto corrigido e jogava a lista de trocas fora, então o
usuário lia o resultado sem saber que houve troca. O texto parecia ter saído
assim do modelo.

Agora `SegmentoFinal.Swaps` vai para o `transcricao.json`, e a tela de revisão
marca cada trecho corrigido com um **✎** que lista as trocas, com um filtro no
topo para ver só os corrigidos e um clique para desfazer.

Três decisões que valem não redescobrir:

- **A posição da troca não é guardada.** O `Troca` do núcleo a tem, mas ela é do
  texto **antes** da correção, e o que a tela mostra é o de depois — pior, a
  edição manual o reescreve de novo. Um índice apontando para um texto que não
  existe mais marcaria a palavra errada, que é pior que não marcar nada.
- **Desfazer troca ao contrário no texto atual**, em vez de restaurar um original
  guardado. Entre a correção e o clique o usuário pode ter editado o trecho à
  mão; restaurar o original apagaria essa edição. A substituição é por palavra
  inteira (`\b`), senão desfazer "Dimi" estragaria qualquer palavra que
  contivesse as letras.
- **Trecho sem troca não ganha lista vazia**, e sim `null`. A tela decide pela
  presença, e uma lista vazia em cada um dos ~400 trechos de uma reunião de duas
  horas engordaria o arquivo por nada.

**A régua está num teste** (`AsTrocasChegamAoArquivoQueAUiLe`): o que ele protege
não é a correção — isso já tinha teste — é o **rastro sobreviver à
serialização**. Sem ele, a lista voltaria a se perder no caminho sem nada quebrar.

---

## 13. Os critérios B e C, e o defeito que estava escondido no B

Em 12/08, ao ir fechar os dois, o **B se revelou pior do que estava registrado**.
Ele não era "não medido": ele **não tinha como funcionar**.

O encerramento dos motores dependia inteiramente do `Dispose` do `MotorSidecar`,
que chama `Kill(entireProcessTree: true)`. Esse caminho cobre o cancelamento e a
saída normal — e é ele que devolve a VRAM em ≤0,3 s. Mas **o `Dispose` só roda se
o app estiver vivo para rodá-lo.** Matar o app pelo Gerenciador de Tarefas, ou um
travamento, pulava tudo: o Python seguia rodando, invisível, com o modelo na GPU.
O sintoma seria a transcrição *seguinte* falhar por falta de memória — longe da
causa, e sem nada apontando para ela.

**A correção é `Sidecar/JobDosMotores.cs`**: um Job Object do Windows com
`KILL_ON_JOB_CLOSE`. Cada motor é adotado logo após o `Process.Start`, antes de
qualquer `await`. Quando o último identificador do job fecha — o que acontece
quando o app morre, de qualquer maneira que ele morra —, o sistema operacional
mata os processos do job. A garantia sai do nosso código e vai para o SO, que é o
único lugar de onde ela pode valer contra um `kill -9`.

Três detalhes que valem não redescobrir:

- **O job é um `static readonly` de propósito.** O tempo de vida dele *é* a
  funcionalidade: se o identificador fechar antes da hora, o Windows mata os
  motores no meio de uma transcrição.
- **`DllImport` e não `LibraryImport`.** O gerador do `LibraryImport` exige
  `AllowUnsafeBlocks` no projeto inteiro, e ligar `unsafe` num projeto que não
  precisava, por quatro assinaturas triviais, é caro demais pela conveniência.
- **Falhar ao adotar não impede transcrever.** Entre "não grava a reunião" e
  "pode sobrar um processo se o app for morto", a segunda é menos ruim.

Fora do Windows o `Adotar` é inócuo — e os 81 testes, que sobem sidecars de
verdade no Linux, são a prova de que ele não quebra o caminho normal.

### A medição, feita em 12/08

```
1. abrir o app e começar a transcrever uma gravação
2. esperar o motor de ASR subir (a barra sai de "carregando o modelo")
3. matar o MeetingApp.exe pelo Gerenciador de Tarefas
4. conferir no Gerenciador que nenhum python.exe sobrou
5. conferir com nvidia-smi que a VRAM voltou
```

**Feito pelo dono do produto em 12/08: a VRAM caiu ao fechar o app pelo
Gerenciador de Tarefas.** O critério B está fechado.

### Sobre o C, e por que o handoff o subestimava

O registro dizia "sem teste de injeção de falha". Estava errado: existem quatro,
todos em `MotorSidecarTests`, cobrindo motor que morre no handshake, erro que
encerra a requisição sem derrubar o motor, motor que morre no meio da operação, e
lixo no canal do protocolo.

O que de fato não tem teste é **o último salto**: a `Ponte` capturar a
`MotorException` e responder `erro` à página em vez de derrubar a janela. Isso
está verificado por leitura — o `catch (Exception e)` envolve o `switch` inteiro e
responde `Erro = e.Message`. Não vira teste porque a `Ponte` vive num `WinExe`
`net8.0-windows` que o projeto de testes (`net8.0`) não referencia; forçar a
referência traria a janela Win32 para dentro da suíte, que hoje roda no Linux em
um segundo.

---

## 14. Os ajustes de 12/08: escolher pasta, e apagar coisas

Três pedidos do dono do produto depois de usar as telas.

**O campo de pasta não abria diálogo nenhum.** O `SeletorDePasta` já existia e já
era usado pela cópia da exportação — faltava expô-lo. Agora os dois campos da aba
Geral têm "Escolher…" e "Limpar". O campo de texto **continua editável**: colar um
caminho de rede que o diálogo não navega bem é caso real, e tirar o teclado do
usuário resolveria um problema criando outro. Cancelar o diálogo é inócuo — sem
essa guarda, desistir da escolha limparia a pasta já configurada.

**Renomear e apagar cliente/projeto.** No `Projetos.cs`, movendo o nó inteiro em
vez de recriá-lo campo a campo, para as chaves que o app Python escreve e este
código não modela irem junto — a mesma razão de o `Salvar` mesclar. Renomear para
um nome que já existe é **recusado**: fundir dois cadastros em silêncio faria o
vocabulário de um cliente passar a valer para o outro. Apagar o que não existe
devolve falso em vez de explodir, porque a tela pode pedir para apagar algo que
outra janela já apagou, e isso é rotina.

Apagar cadastro **não apaga transcrição**: elas moram junto das gravações e
guardam o nome do cliente dentro de si. A confirmação diz isso.

**Apagar gravação** é a operação mais destrutiva do app — leva o áudio original,
que não se refaz, e não há lixeira. Duas decisões de contenção:

- **O botão mora dentro da gravação aberta, não na lista.** Um "apagar" em cada
  cartão põe a ação a um clique errado de distância numa tela que se percorre
  rápido. Abrir a gravação custa um clique e garante que quem apaga está olhando
  para o que apaga.
- **Ele não aparece logo depois de transcrever.** Aquela tela é o resultado de
  meia hora de GPU; um "apagar" ao lado dele é convite ao clique errado. Só
  aparece ao reabrir a gravação.

No lado C#, o caminho vindo da página é conferido contra a raiz das gravações
antes de qualquer coisa — com o separador no fim, para `gravacoes-antigas` não
passar por estar sob `gravacoes` por prefixo de texto. **Caminho vindo da tela
que chega direto a um `Directory.Delete(recursive)` é um apagador de disco
controlado pelo HTML**, e basta um `..\..\` para virar outra coisa.

---

## 15. A barra de rolagem que não existia no CSS

Defeito de 12/08, relatado pelo dono do produto: barra de rolagem vertical em
qualquer tamanho de janela, e o botão Ajustes um pouco fora da tela.

**A causa não estava no layout.** O sprite de ícones SVG, adicionado com a UI
nova, trazia `style="position:absolute"` — e a CSP desta página é
`style-src 'self'` **sem `'unsafe-inline'`**. O atributo foi descartado em
silêncio, o sprite voltou ao fluxo como elemento inline, criou uma linha de texto
de **23 px** e empurrou a página inteira para baixo.

Medido antes: `scrollHeight` 823 contra `innerHeight` 800, `.app` começando em
y=23, e o botão Ajustes terminando em 811 — 11 px fora. Depois:
`scrollHeight` 800, Ajustes em 788, em quatro tamanhos de janela.

O comentário no topo do `index.html` já avisava — *"'unsafe-inline' fica de fora
de propósito — estilo e script moram em arquivos"* — e o defeito entrou mesmo
assim. Os dois esqueletos da tela de carregamento tinham o mesmo problema desde
antes (`style="height:5rem"`), e nunca tiveram altura nenhuma.

**A lição, que vale além deste caso:** com uma CSP restritiva, *estilo inline não
falha — ele some*. Não há erro no console de layout, o CSS "está certo" quando
lido, e o sintoma aparece a três camadas de distância da causa.

**A ferramenta:** `tools/medir_layout.py` sobe a página estática num Chromium e
mede sobra vertical e a posição do último item do trilho em quatro tamanhos de
janela. Sai com código 1 se algum falhar. É o mesmo espírito do
`tools/ui_check.py`: ler o CSS diz o que deveria acontecer, o navegador diz o que
acontece.

### O segundo defeito de layout, achado pela mesma ferramenta

Relatado logo depois: dentro dos Ajustes, as abas rolavam junto com o painel
quando a aba era longa (Clientes, Vozes).

Elas **eram** `position: sticky` — só grudavam no lugar errado. Medido: as abas
paravam em **16 px** do topo, e o cabeçalho termina em **70 px**. Ou seja,
grudavam atrás da barra e desapareciam por baixo dela, o que da poltrona parece
"rolou junto".

A causa de fundo é mais interessante que o valor errado: **dois blocos grudam
abaixo da barra do topo — os controles da revisão e as abas dos ajustes — e cada
um chutava a altura dela por conta própria.** O da revisão chutou `4.75rem` e
acertou; o das abas chutou `var(--espaco-4)` e errou. Um chute errado aqui não
quebra nada visivelmente: o bloco só desliza para trás da barra e some.

Agora existe `--altura-da-barra` em `:root`, e os dois a usam. As abas também
ganharam `max-height` com rolagem própria, para uma lista de abas maior que a
janela não deixar a última inalcançável.

**O medidor ganhou uma ponte falsa** (`PONTE_FALSA` em `tools/medir_layout.py`).
Sem `window.chrome.webview`, o JS morre no primeiro import e só a moldura
renderiza — foi por isso que a primeira versão da ferramenta não teria pego este
defeito. O dublê responde às operações com dados inventados **grandes de
propósito** (12 clientes, 8 pessoas com 4 amostras): tela curta não rola, e o que
não rola não revela defeito de rolagem. A verificação agora é comportamental —
rola até o fim e confere se as abas continuam abaixo do cabeçalho.

---

## 16. Aposentar o Docker e o Gradio (autorizado em 12/08)

O dono do produto autorizou a limpeza. **Nada foi removido ainda** — este é o
inventário para quem fizer, e a parte importante é o que **não** sai.

### O que sai

| o quê | por quê |
|---|---|
| `Dockerfile`, `docker-compose.yml`, `.dockerignore` | o caminho de GPU por container, substituído pelos motores rodando no Windows |
| `web.py` | o ponto de entrada do Gradio |
| `src/web/gradio_app.py`, `theme.py` | 1.916 linhas de UI, substituídas por `app-net/App/web/` |
| `main.py`, `src/gui/` | a UI CustomTkinter, anterior ao Gradio e sem uso desde então |
| `src/transcription/`, `src/diarization/`, `src/audio/` | o pipeline Python, substituído por `Nucleo/Transcritor.cs` mais os sidecars |
| `Huggingface access guide.md` | o guia de 5 KB de onboarding do token, que o token embutido tornou desnecessário |

### O que NÃO sai, e é onde a limpeza pode dar errado

- **`src/web/assets/ds/`** — o design system. O app nativo o consome
  diretamente: o `ds.css` de `app-net/App/web/` faz `@import` de `/ds/`, e o
  `.csproj` embute esses arquivos por caminho literal
  (`../../src/web/assets/ds/...`). **Apagar `src/web/` inteiro quebra o build do
  app.** Se a pasta for movida, os `EmbeddedResource` do `.csproj` vão junto.
- **`src/web/projects.py`, `history.py`, `recordings.py`, `voices.py`,
  `exporters.py`** — não estão em uso pelo app nativo, mas são a **referência
  escrita** dos formatos que ele lê e escreve (`projects.json` com as chaves que
  o C# preserva sem modelar, o layout do `history/`). Enquanto houver dado
  antigo para migrar, o valor deles é documental. Sair, se sair, é depois da
  Fase 2.5.
- **`motores/`** — é Python, e é a implementação atual dos três sidecars. Não
  tem relação com o Docker.
- **`tools/`** — sete ferramentas de medição dependem do `.venv`
  (`benchmark_wer.py`, `benchmark_der.py`, `medir_layout.py`, `ui_check.py`,
  `comparar_gravadores.py`, `sweep_vad.py`, `compare_diarization.py`). Elas são
  como se mede este projeto; o `.venv` e o `pyproject.toml` ficam por causa
  delas, mesmo sem Gradio nenhum.

### A ressalva que já estava registrada

Aposentar o Gradio tira o **oráculo**. Comparar com ele pegou três defeitos do
porte que nenhum teste de unidade pegaria ([FASE2.md](FASE2.md) §208), e a
comparação final foi dispensada (§3). Depois da limpeza, defeitos de porte
remanescentes só aparecem em uso. Vale fazer a remoção **num commit próprio e
isolado**, para que reverter seja barato se algo faltar.
