# Fase 5 — handoff: o acabamento visual

O que a fase de fato entregou, medido, e o que ela deixa para as seguintes. A
carta é a §3 do [PLANO.md](PLANO.md) — esta fase nunca teve arquivo próprio,
porque nasceu como "Fase 3, o redesign" e foi reduzida a acabamento quando a
Fase 2.5 escreveu a interface inteira já sobre o design system.

Executada em 18/08/2026, na branch `feat/fase-5-acabamento`.

**A fase fechou por medição, e não por opinião.** Cada item abaixo foi conferido
abrindo o app na máquina, tela a tela, nos dois temas, e amostrando pixels do
retrato. Onde havia número — contraste, altura, tamanho de fonte — o número está
aqui.

---

## 1. Os números

| | antes | depois |
|---|---|---|
| valores fora de token no `app.css` | 29 | **0** |
| variáveis inexistentes usadas | 2 | **0** |
| temas alcançáveis | 1 | **3** (claro, escuro, igual ao Windows) |
| barra de título no escuro | branca, (243,243,243) | **(32,32,32)** |
| polegar da barra de rolagem | cinza neutro do Chromium | **areia, 3,80:1** |
| altura até o 1º trecho da revisão | 370 px | **281 px** |
| texto da transcrição | 13 px | **15 px** |
| testes | 322 | **336** |

---

## 2. O que a fase entregou, item a item

A §3 do PLANO listava cinco itens. Um já estava feito, um foi recusado com
motivo, e os outros três saíram — mais quatro que a varredura visual cobrou.

| # | o que a carta pedia | como ficou |
|---|---|---|
| 1 | fidelidade aos tokens, tela a tela | 29 valores soltos para token; **zero** sobrando |
| 2 | as fontes auto-hospedadas | **já estava pronto** desde a Fase 2.5 (`EmbeddedResource` no `.csproj`) |
| 3 | os componentes React do design system | **não feito, de propósito** — ver §4 |
| 4 | o tema escuro de ponta a ponta | de inalcançável a três opções em Ajustes › Geral |
| 5 | densidade e tipografia da revisão | 89 px recuperados, texto no token de corpo |

E quatro que **não estavam na carta** e entraram porque o retrato as mostrou:

- **a ata saía sem hierarquia nenhuma** — `--texto-medio` e `--texto-base` não
  existem no `tokens.json`, e um `var()` que não resolve invalida a declaração
  inteira: `h2` e `h3` herdavam o tamanho do corpo;
- **a barra de título ficava branca no escuro**, porque moldura é do Windows e
  não da página;
- **o gravador escrevia "Parado" duas vezes**, o relógio e a linha de situação
  dizendo a mesma palavra;
- **os cartões de Atas não se alinhavam** entre si.

---

## 3. As três decisões que valem ser lembradas

### 3.1 O tema inicial é escrito pelo núcleo, não pelo JavaScript

O caminho óbvio — a página lê a configuração pela ponte e põe o `data-tema` —
tem um defeito que só aparece no uso: a ponte é assíncrona, e a resposta chega
**depois da primeira pintura**. Quem escolheu escuro veria um lampejo branco a
cada abertura.

O caminho normal para isso é um `<script>` embutido no `<head>`, e a CSP desta
página é `script-src 'self'` sem `'unsafe-inline'` — afrouxá-la para pintar um
fundo seria caro pelo preço errado.

Então quem troca o atributo é o **núcleo**, reescrevendo o `index.html` enquanto
o serve (`App/Conteudo.cs`, `ComTema`). Metade do lampejo ainda era do host, e
essa se resolve com o `DefaultBackgroundColor` do WebView2, que pinta antes de
existir HTML.

**Consequência para quem mexer nisso depois:** o `data-tema="claro"` do
`index.html` é procurado por texto exato. Mudá-lo faz o tema parar de funcionar
— e parar em silêncio, mostrando sempre claro. Trocar de tema com o app aberto
continua sendo trabalho do JavaScript, e a moldura da janela só acompanha na
próxima abertura.

### 3.2 O tema é fechado numa lista, e não escapado

`TemaAceito()` em `ConfiguracoesDoApp` é o portão único. O valor sai de um
`app.json` que qualquer um edita, atravessa a ponte e termina **dentro de um
atributo do HTML servido** — e a CSP não veria nada de errado num atributo que
fechou aspas e abriu marcação. Devolver uma de três constantes deste repositório
é mais barato e mais seguro que escapar.

O padrão é `claro`, e não `auto`: quem já tem o app instalado não deve ver a
interface trocar de cor porque atualizou.

### 3.3 Medir contraste, e não confiar no token

A barra de rolagem foi para `--cor-borda-forte`, que era a escolha semântica
óbvia. Medido no retrato: **1,8:1** contra o fundo escuro — pior que os 3,4:1 do
polegar padrão do Chromium que ela substituía. `--cor-borda-controle` dá
**3,80:1**, acima do piso de 3:1 para controle.

Vale o inverso também: o "Remover" do cartão do modelo em uso mede **2,42:1** e
**não é defeito** — o botão está desabilitado de propósito, e controle inativo é
exceção explícita na régua. Um alarme falso conferido antes de virar correção.

---

## 4. O que a fase recusou, e por quê

**Os componentes React do design system não entraram.** A própria carta dá a
régua: "só onde pagarem por si". Aqui não pagam — a página não tem passo de
build, então React exigiria um empacotador; e a CSP fechada teria de ser
afrouxada para trocar HTML que já funciona. Fica registrado como decisão, e não
como pendência.

---

## 5. A bancada de fotografia

A fase precisou de retrato, e o app já tinha as duas alavancas: `--tela` abre
direto em qualquer destino sem clique, e o tema mora no `app.json`. O laço
completo é publicar num destino separado e abrir uma tela por vez:

```bash
tools/publicar.sh --destino /mnt/c/Users/andre/MeetingApp-fase5
```

Três coisas custaram uma tentativa cada, e ficam aqui:

- **fotografe a JANELA, não a tela.** Capturar o `VirtualScreen` "para não
  perder um diálogo fora do enquadramento" fotografa a área de trabalho de
  outra pessoa. `PrintWindow` com `PW_RENDERFULLCONTENT` (flag 2) pede os
  pixels à própria janela: resolve a oclusão e é incapaz de pegar outra coisa.
  O flag 2 não é opcional — sem ele o conteúdo do WebView2, que desenha por
  composição, sai em branco;
- **`SetForegroundWindow` não funciona** chamado de um processo que não está em
  primeiro plano. É por isso que `PrintWindow` é o caminho, e não `CopyFromScreen`
  do retângulo da janela;
- **mate a instância pendurada antes de abrir.** O mutex faz o lançamento novo
  só trazer a janela VELHA para a frente e sair — o retrato sai da tela errada,
  sem erro nenhum. Aconteceu uma vez, e o retrato passou por bom.

Uma armadilha a mais, esta no `tools/publicar.sh`: o `--destino` falhava ao
criar a junção dos motores. O `mklink /J` pelo `cmd.exe` responde *"The
filename, directory name, or volume label syntax is incorrect"* mesmo com o
`/s` e o `cd /mnt/c` que os comentários do script já documentavam; os caminhos
existem e o `dir` os lista pelo mesmo `cmd.exe`. **Corrigido** com
`New-Item -ItemType Junction` do PowerShell, que faz o mesmo trabalho — ver §8.

---

## 6. O que a fase deixa aberta

### 6.1 A moldura não repinta ao trocar de tema

Escolher outro tema vira a página na hora; a barra de título do Windows só
acompanha na próxima abertura. Repintar exigiria refazer a chamada do DWM a
partir da ponte, e o custo não pareceu valer o ganho de um caso que acontece uma
vez por instalação.

### 6.2 A varredura não cobriu os estados que exigem uma gravação em curso

Medidores de nível, o aviso de mute prolongado e a barra de progresso da
transcrição não foram fotografados no escuro: todos precisam do app gravando ou
transcrevendo, e o destino de teste não tem os motores. Nenhum deles usa cor
fora de token — a conferência é de olho, não de código.

### 6.3 O `.aa-pagina` do design system é largo demais para tela de app

A revisão precisou de `padding-top` próprio porque os 64px do `.aa-pagina` são
de página de documento. Provavelmente vale para as outras telas também, e a
correção certa seria no design system, não aqui. Não medido nas demais.

---

## 7. Nota de processo: duas sessões no mesmo diretório

Esta fase começou com o trabalho sendo varrido para dentro de um commit alheio:
outra sessão rodava no **mesmo diretório de trabalho** e commitou tudo que
estava modificado. Criar um branch não resolve — branch é do repositório, não da
árvore. O que resolve é `git worktree`, e é assim que a fase foi executada.

O commit `c7ce16f`, que fala de detecção de GPU, carrega junto a primeira leva
da varredura de tokens desta fase. Não foi separado: o conteúdo está correto,
só está arquivado no lugar errado.

---

## 8. Depois da fase: os motores mudaram de casa

Em 18/08/2026, ao gerar a 0.3.0, o dono do produto **apagou
`C:\Users\andre\MeetingApp`** para liberar disco — o `C:` estava a 97%, e os
4,3 GB de Python embarcado estavam duplicados ali e na instalação que o
instalador da Fase 4 produz. Decisão certa, e ela quebrou três defaults:

- `montar_instalador.sh` reprovava em "falta o Python embarcado". Falha alta,
  que é o comportamento desejável;
- `publicar.sh` **recriava a pasta em silêncio**, com um executável que abre e
  não transcreve. Este era o ruim: desfazia a limpeza a cada publicação;
- a junção do `--destino` passava a apontar para o nada.

Os dois scripts passaram a ler os motores de
`AppData\Local\Programs\MeetingApp\motores`, a instalação oficial. O destino
de publicação **continua** sendo `C:\Users\andre\MeetingApp`, e não a oficial:
um build meio pronto não pode cair no app que grava reunião.

A junção deixou de ser detalhe e virou a peça que faz isso funcionar, então ela
foi consertada (§5) e ampliada: além do `python/`, agora liga também os pesos de
diarização e o motor de ata. **Custo de uma instalação de teste completa e
funcional: 18 MB.** Os três `motor.py` continuam sendo cópias de verdade — é o
que impede uma publicação de teste de reescrever os sidecars do app de verdade.

Medido: publicar num destino novo produz as três junções sem aviso nenhum, e
`rm -rf` nesse destino **não toca** os 4,3 GB do alvo. Junção não é dona dos
bytes.
