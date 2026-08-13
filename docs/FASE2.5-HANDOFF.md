# Fase 2.5 — handoff: um app só

> **A Fase 2.5 foi dada por concluída pelo dono do produto em 13/08/2026.** Ele
> usou o app unificado e **gravou uma reunião enquanto uma transcrição rodava,
> sem problema nenhum** — que é o critério B, o que decidia a fase. Ver §7.

Escrito em 12/08/2026 ao fim da execução da carta [FASE2.5.md](FASE2.5.md), e
fechado em 13/08. O que está aqui é o que foi feito, o que foi medido, e o que
ficou registrado como não medido.

O gravador e o app viraram um `MeetingApp.exe`. Ele sobe na bandeja, abre a
janela pelo ícone, mostra a gravação de dentro da janela, e continua gravando
quando a janela fecha.

---

## 1. O que mudou de lugar

O `recorder-net/` deixou de existir. Nada do que ele continha foi reescrito: os
arquivos foram movidos com `git mv`, e o `git log --follow` continua enxergando
a história inteira.

| era | é |
|---|---|
| `recorder-net/Core/` | `app-net/Gravacao/` |
| `recorder-net/Capture/` | `app-net/Captura/` |
| `recorder-net/Agenda/` | `app-net/Agenda/` |
| `recorder-net/Cli/` | `app-net/CliGravador/` (continua saindo `Capture.exe`) |
| `recorder-net/Tray/` | `app-net/App/Bandeja/` |
| `recorder-net/Tests/` | `app-net/Tests/` (as duas suítes juntas) |

**Os nomes dos projetos e dos assemblies não mudaram** — `MeetingRecorder.Core`,
`MeetingRecorder.Capture`, `MeetingRecorder.Agenda`. As pastas seguem o nome que
a carta pediu, e o conteúdo dos arquivos ficou byte a byte igual onde deu. Um
`MeetingApp.Gravacao.dll` teria obrigado a renomear namespace em 5.457 linhas
para ganhar coerência de nome, e a carta é explícita: **nada ali se reabre**.

Os dois arquivos que precisaram mudar de verdade:

- **`Win32.cs`** — havia dois, um na bandeja e um no app, com metade das
  declarações repetidas. Agora é um só (`App/Nativo/Win32.cs`), porque os dois
  `HWND` vivem no mesmo processo e duas cópias das mesmas declarações de
  `user32` no mesmo assembly seriam duas chances de divergirem;
- **`SeletorDePasta.cs`** — pelo mesmo motivo, ficou só o do app. O comentário
  que explicava a duplicação ("os dois executáveis são separados por desenho")
  deixou de ser verdade.

O `Tray/Program.cs` virou dois arquivos: `Bandeja/Gravador.cs` (a gravação como
serviço — iniciar, mutar, parar, agenda, estado) e `Bandeja/Bandeja.cs` (ícone,
menu, balões). A lógica de estado continua onde estava, no `EstadoDaBandeja` do
núcleo portátil, coberta por teste.

---

## 2. O ciclo de vida, que é onde mora o risco

```
Programa.Main
  └─ Aplicacao                     dono do processo
       ├─ JanelaDeMensagens        a janela invisível: ÚNICO laço de mensagens
       ├─ Gravador                 as duas capturas WASAPI
       ├─ Bandeja                  ícone + menu
       └─ JanelaDoApp?             criada na primeira vez que alguém a pede
```

Quatro regras, e errar qualquer uma perde gravação:

1. **O laço é da `JanelaDeMensagens`.** A janela do app não tem laço próprio; o
   `GetMessage` da bandeja despacha os dois `HWND`, que estão na mesma thread.
   Não há segunda thread de UI, e é por isso que o estado do gravador é lido dos
   dois lados sem trava.
2. **O X da janela esconde.** `WM_CLOSE` → `ShowWindow(SW_HIDE)`. Não destrói,
   não posta `WM_QUIT`, não para a captura. Reabrir é instantâneo porque o
   WebView2 continua de pé, com a página onde estava.
3. **`WM_DESTROY` da janela do app não faz nada.** Era ele que encerrava o
   processo até a Fase 2 — essa linha some, e é a inversão inteira da fase numa
   linha.
4. **Sair é só pelo menu da bandeja**, e confirma quando há gravação em
   andamento. É a única confirmação do app, porque é o único clique que perde
   uma reunião.

A janela é criada sob demanda, não no início: quem sobe com `--bandeja` (o modo
de iniciar com o Windows) não paga o WebView2 na memória por uma janela que
talvez não abra.

**Uma instância por máquina**, mutex `Global\MeetingApp`. A segunda pede a
janela à primeira por uma mensagem registrada (`MeetingApp.MostrarJanela`,
achada por `FindWindow` na classe `MeetingApp.JanelaDaBandeja`) e sai. O nome do
mutex é novo de propósito: o `Global\MeetingRecorder.Tray` continua sendo do
gravador antigo, e o critério A exige os dois gravando ao mesmo tempo.

---

## 3. A ponte ganhou eventos

Era só pergunta-e-resposta. Agora tem um segundo sentido:

- **`id` zero** identifica um evento que o núcleo empurrou. Zero porque não
  responde a pedido nenhum, e a página casa pedidos com respostas pelo id;
- `ponte.js` ganhou `assinar(tipo, fn)`, que devolve a função de cancelar;
- o núcleo empurra `{id:0, tipo:"gravador", gravador:{...}}` **a cada 200 ms
  enquanto grava e a janela está visível**, e a cada segundo quando parada.

O tick de 200 ms é um `SetTimer` separado do de 1 s, ligado e desligado com a
janela. Repintar o ícone da bandeja cinco vezes por segundo seria cinco
`Shell_NotifyIcon` por segundo a troco de nada; já um medidor de nível que anda
de segundo em segundo não parece um medidor de nível.

Operações novas: `gravador`, `gravar`, `parar-gravacao`, `mutar`,
`dispositivos`, `escolher-dispositivo`, `pasta-das-gravacoes`, `notificacoes`,
`usar-agenda`, `conectar-agenda`, `desconectar-agenda`. Todas devolvem o estado
inteiro em vez de um "ok": a tela desenha do estado que recebeu.

---

## 4. O que a janela mostra

Tela **Gravador** (`web/gravador.js`), item do trilho que estava `disabled`:

- tempo decorrido em `hh:mm:ss` com dígitos tabulares, e o ponto de estado na
  **mesma escala de cor do ícone** — laranja é você tendo mutado, amarelo é
  canal sem áudio sem ninguém ter pedido;
- iniciar/parar e mutar, com **aviso de mute prolongado** a partir de 1 min. Para
  isso o `EstadoDaBandeja` ganhou `MudoHaS(agora)`: a bandeja avisa por balão nos
  marcos e some, a janela mostra continuamente, e "mudo" sem dizer há quanto
  tempo é a informação que menos ajuda quem esqueceu;
- **medidores das duas faixas**, em escala log (−60 dB → 0%). Linear passaria a
  reunião inteira nos primeiros 10%, que é indistinguível de silêncio —
  justamente o que o medidor existe para distinguir. Faixa sem áudio nenhum
  passados 45 s fica vermelha, no mesmo limiar do ícone amarelo;
- a reunião da agenda com os participantes reconhecidos, enquanto grava;
- os dois dispositivos, travados durante a gravação.

Aba **Ajustes › Gravador**: notificações, usar a agenda, conta do Google.

A captura ganhou **um campo**, `WasapiTrackCapture.Nivel`: é o mesmo RMS que o
`AtualizarEstatisticas` já calculava. Nada no caminho de escrita mudou.

---

## 5. Uma pasta só (critério F)

Havia duas chaves para a mesma coisa, e a segunda **não era lida por ninguém**:

| chave | quem escrevia | quem lia |
|---|---|---|
| `settings.json / output_dir` | menu da bandeja | o gravador, e o app à mão |
| `app.json / pasta_das_gravacoes` | aba Geral dos ajustes | **ninguém** |

Mexer na aba Geral não tinha efeito nenhum. Agora a autoridade é o `output_dir`,
porque é onde o áudio de fato cai; a aba Geral escreve nele, e quem tinha
escolhido algo na chave morta é migrado na primeira abertura
(`Nucleo/PastaDasGravacoes.cs`, cinco testes). O `--gravacoes` passou a valer
para os dois papéis — antes o app listaria uma pasta e gravaria noutra.

---

## 6. O que foi medido, e como

**181 testes verdes** (85 do app + 90 do gravador + 6 novos), numa suíte só.

**Layout em navegador de verdade** (`tools/medir_layout.py`, agora com a tela do
Gravador e a ponte falsa respondendo o estado): quatro tamanhos de janela, sem
sobra e sem o trilho sair da tela.

**Execução do binário publicado** (`tools/validar_bandeja.ps1`, reescrito):
sobe com `--bandeja`, clica no ícone, grava, **abre a janela no meio da
gravação, fecha, e confere que o processo continua vivo e a janela escondeu**,
grava mais, e sai pelo menu. Resultado:

```
processo iniciado (sem janela)
bandeja de pé
janela aberta durante a gravação
janela fechada, processo vivo (critério C ok)
saiu limpo com código 0
meta.json: 24.8s, mic 397045 quadros, system 388584 quadros
```

**O desalinhamento de 528 ms entre as faixas foi conferido contra um controle.**
O mesmo núcleo sem janela nenhuma (`Capture.exe --seconds 25`), na mesma máquina
e no mesmo minuto, deu **542 ms**. O gap vem do loopback ocioso — a máquina
estava em silêncio e o `system` recebeu 23,4 s de silêncio inserido —, não da
fusão. Os dois números estão dentro da faixa histórica dos `meta.json` de
reuniões reais (0,01 s a 1,26 s).

---

## 7. Os critérios de aceite, um a um

| | o quê | como fechou |
|---|---|---|
| **A** | gravar em paralelo com o gravador antigo, amostra a amostra | **dispensado** pelo dono do produto ao aceitar o app em uso. O controle do §6 é o que existe no lugar: 528 ms de desalinhamento no app fundido contra 542 ms no núcleo puro, mesma máquina, mesmo minuto |
| **B** | **transcrever e gravar ao mesmo tempo** | **fechado em 13/08 pelo dono do produto**, gravando uma reunião com uma transcrição em andamento, sem problema nenhum |
| **C** | fechar a janela durante a gravação não para a gravação | automatizado no `tools/validar_bandeja.ps1`, e confirmado em uso |
| **D** | matar o processo e o WAV continuar legível | **não repetido**: é o `CrashSafeWavWriter`, que não foi tocado e continua coberto por teste |
| **E** | soak de 1 h com o binário fundido | **não feito como soak dedicado**. O uso real do dia 13 cobriu o caso que preocupava, que era a convivência |
| **F** | instalador único e migração das configurações | a **migração** existe e tem cinco testes (§5). O **instalador** não existe — ver abaixo |

O **B** era o que decidia a fase, e fechou pelo caminho mais barato: uso real.
O argumento que já estava registrado aqui antes da medição continua valendo como
explicação de *por que* fechou fácil — **os motores já eram processos
separados**. O `.NET` orquestra; quem carrega modelo e usa GPU é o Python do
sidecar, que já rodava enquanto o gravador antigo gravava, em duas janelas
diferentes. O que a fusão acrescentou ao processo da captura foi um WebView2
ocioso e um timer de 200 ms.

**O que a fase não entregou, com o custo à vista:**

- **não há instalador.** O app se instala copiando um `.exe` e uma DLL numa
  pasta, e o `motores/` de 4,3 GB continua sendo montado à parte pelo
  `tools/empacotar_motores.sh`. O critério F pedia os dois, e só a migração das
  configurações foi feita — que era a metade que arriscava o usuário
  reconfigurar coisas;
- **o critério A foi dispensado, e ele era o oráculo.** Comparar as faixas com o
  gravador antigo é o que pegaria uma perda silenciosa no porte. O controle do
  §6 mede a mesma coisa contra o CLI, mas numa sala em silêncio e por 25 s — não
  contra uma reunião de verdade. Uma degradação sutil na captura, se existir,
  agora só aparece em uso;
- **o soak de 1 h não foi repetido** com o binário fundido.

---

## 8. Onde o build vai parar

**`C:\Users\andre\MeetingUnificado\`** — e não `MeetingApp\` nem
`MeetingRecorder\`. Foi um pedido do dono do produto durante a execução: até
aprovar, os dois programas antigos continuavam sendo os que gravam reunião de
verdade todo dia, e o critério A precisava deles vivos.

**Aprovado o app em 13/08, a troca de destino continua pendente e é decisão do
dono do produto**, porque não é só copiar um arquivo:

- o `MeetingRecorder.exe` antigo provavelmente está no início automático do
  Windows, e depois da fusão ele seria um segundo ícone na bandeja disputando os
  mesmos dispositivos — o mutex do app fundido é outro (`Global\MeetingApp`) e
  **não** impede isso;
- o app fundido tem `--bandeja` para ocupar esse lugar, e é ele que deve entrar
  no início automático;
- as duas pastas antigas guardam o `motores/` real; a de teste tem só uma junção
  para ele.

```bash
tools/publicar.sh                    # publica e instala na pasta de teste
tools/publicar.sh --so-build         # só dist/publicar
tools/publicar.sh --destino /mnt/c/Users/andre/MeetingApp   # a troca definitiva
```

Os 4,3 GB de Python embarcado **não são copiados**: a pasta de teste recebe uma
junção do Windows (`mklink /J`) para `MeetingApp\motores\python`. Os três
`motor.py` são cópias de verdade, deliberadamente — com junção, publicar aqui
reescreveria os sidecars do app que ainda está em produção.

O script agora confere o processo aberto **pelo caminho**, não pelo nome: os dois
se chamam `MeetingApp.exe`, e barrar pelo nome impediria de publicar na pasta de
teste enquanto o usuário trabalha no app de produção.

### Três réguas antes de copiar

Tamanho ≥ 10 MB, exatamente uma ocorrência de `hf_token`, e — nova — os ícones
da bandeja embutidos. A terceira nasceu na primeira execução desta fase: o
`EmbeddedResource` com `..\..\assets\bandeja-*.ico` **não expande glob no
MSBuild rodando em Linux**. Compila, publica, passa nos testes, e o app sobe sem
ícone na bandeja — sem ícone não há menu, e sem menu não há como sair dele. Os
quatro `.ico` agora estão listados um a um, com barra normal.

*(A própria régua nasceu errada: `strings | grep -q` faz o `strings` morrer de
SIGPIPE, e com `set -o pipefail` ela reprovava justamente o binário correto.)*

---

## 9. Fora desta fase, e ainda de pé

Continua valendo o §8 da carta: resumo e ata por LLM — que é a próxima fase de
verdade —, motores como pacotes baixáveis, integração com o Teams, Linux e Mac.

> **13/08/2026, um dia depois.** A ata por LLM virou carta: [FASE3.md](FASE3.md),
> junto com as notas de reunião e a transcrição que sobrevive à navegação. Na
> mesma decisão, o redesign da interface (a antiga Fase 3) foi para o fim da
> fila, depois do instalador — ver [PLANO.md](PLANO.md), "A reordenação de
> 13/08/2026".

E três coisas que esta fase deixou explicitamente para trás, repetidas aqui para
não sumirem no corpo do documento:

1. **o instalador** (parte do critério F), que vai para a Fase 4;
2. **a troca do destino de publicação** e o que vem com ela — tirar o gravador
   antigo do início automático do Windows e pôr o `MeetingApp --bandeja` no
   lugar (§8);
3. **o soak de 1 h** e a comparação amostra a amostra com o gravador antigo
   (§7), dispensados com o custo registrado.
