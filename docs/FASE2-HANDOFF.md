# Fase 2 — onde parou e o que falta

Documento de passagem de bastão. A carta de execução é a
[FASE2.md](FASE2.md); aqui está o estado real contra ela, em 11/08/2026.
O equivalente da fase anterior é a [FASE1-HANDOFF.md](FASE1-HANDOFF.md), e
tudo o que ela diz sobre o gravador continua valendo.

Branch: `feat/recorder-and-accuracy`. Nada foi para `main`, e nada foi
empurrado para o remoto — as duas coisas são decisão do dono do produto.

---

## 1. Estado em uma frase

O app nativo **transcreve de ponta a ponta e entrega arquivo**: escolher a
gravação, preparar com cliente/projeto/modelo, rodar os motores no Windows sem
WSL nenhum, ler e corrigir o texto ouvindo o trecho, nomear os falantes, e
exportar em TXT/SRT/VTT/DOCX com cabeçalho da reunião. O que falta não é o
fluxo — é **a evidência de que a biblioteca de vozes funciona com áudio real**,
as três telas de gestão (vozes, clientes, configurações) e a medição de
paridade que autoriza aposentar o Gradio.

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
| `app-net/Sidecar/` | o cliente NDJSON: `Protocolo.cs` e `MotorSidecar.cs` |
| `app-net/App/` | a janela Win32 crua, o WebView2, a ponte JSON |
| `app-net/App/web/` | a interface: 1612 linhas de HTML/CSS/JS, sem framework |
| `app-net/Cli/` | o mesmo pipeline por linha de comando — é por aqui que se mede |
| `motores/asr/`, `motores/diarizacao/` | os sidecars Python |

**A ponte** aceita: `gravacoes`, `transcricao`, `transcrever`, `clientes`,
`prefs`, `salvar-projeto`, `salvar-transcricao`, `aprender-voz`, `exportar`,
`config`, `salvar-config`. Toda tela nova começa por adicionar um caso ali.

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
| 3. UI na ordem do fluxo | ✅ o fluxo principal; ❌ histórico, vozes e projetos (as telas) |
| 4. correção fonética + filtro de silêncio | ✅ no núcleo, ligados por opção |
| 5. paridade final e aposentadoria do Docker | ❌ não começou |

### Critérios de aceite (§96 da FASE2.md)

| | estado |
|---|---|
| **A.** as 27 entregas do FEATURES ponta a ponta, igual ou melhor que o Gradio | 🟡 o fluxo roda numa gravação real; a **comparação medida** contra o Gradio não foi refeita depois da UI existir |
| **B.** matar o app não deixa motor órfão; cancelar libera a GPU em ≤2 s | 🟡 medido no CLI (≤0,3 s), **nunca medido matando o app** |
| **C.** motor que morre devolve erro legível e o app continua vivo | 🟡 o caminho existe (`MotorException` chega à tela), sem teste de injeção de falha |
| **D.** as trocas da correção fonética visíveis e inspecionáveis na UI | ❌ a correção **roda** ([`Transcritor.cs:226`](../app-net/Nucleo/Transcritor.cs#L226)) mas descarta a lista de trocas; a UI não mostra nada |
| **E.** Docker/Gradio aposentado | ❌ depende de A |

O critério D é o mais barato de fechar e está pela metade: `CorrecaoFonetica.Corrigir`
já devolve `trocas`, e o `Transcritor` joga fora. Basta carregá-las até o
`transcricao.json` e desenhar onde mostrá-las.

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
2. **O cabeçalho da exportação foi testado por unidade, não visto na tela.** As
   mudanças de 11/08 (cliente, projeto, data com hora, data no nome do arquivo)
   têm teste e o binário está publicado, mas ninguém exportou um DOCX depois
   delas.
3. **Nenhuma medição de tempo do app inteiro.** Sabe-se que os motores rodam a
   ~4,5× o tempo real no Windows; não se sabe quanto o app adiciona.

---

## 5. Pendências, na ordem que o dono do produto pediu

1. **Tela de gestão de vozes** — listar quem é conhecido, ouvir os trechos que
   geraram cada amostra, aprovar o que caiu em quarentena, apagar. O núcleo já
   tem tudo: `Vozes.Aprovar()`, `Vozes.EmQuarentena()`, e cada `AmostraDeVoz`
   guarda a procedência (gravação, faixa, t0, t1, dispositivo) exatamente para
   esta tela poder tocar o trecho. Desenho em [VOZES.md](VOZES.md) §6.
2. **Tela de clientes e projetos** — hoje os botões existem e não levam a lugar
   nenhum. O dono do produto disse que **vai passar referências de outros apps**
   antes de modelarmos; não desenhe sem elas.
3. **Ajustes no menu de configurações** — mesma espera pelas referências.
4. **Critério D** (as trocas fonéticas na UI), que é o único critério de aceite
   fechável sem decisão de produto.
5. **Critério A**: refazer a comparação com o Gradio agora que a UI existe, e
   então aposentar o Docker.

Da fase anterior, ainda abertos: **repetir o soak de 1h com o binário atual**
(critérios A e C da Fase 1 estão 🟡 porque a âncora mudou depois da medição),
**o clique do OAuth inicial**, e **a decisão de tirar o `recorder/` Python de
uso**.

---

## 6. Como trabalhar aqui

```bash
export PATH="$HOME/.dotnet:$PATH"

# testes — 72, sempre verdes antes de commitar
dotnet test app-net/Tests/MeetingApp.Tests.csproj

# publicar o app. As três flags e o token são obrigatórios — ver §7.
dotnet publish app-net/App/MeetingApp.App.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true \
  -p:TokenHuggingFace=/mnt/c/Users/andre/.meeting-recorder/hf_token.txt \
  -o <saida>
cp <saida>/MeetingApp.exe /mnt/c/Users/andre/MeetingApp/

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
