# Fase 2.5 — um app só: carta de execução

Escrita em 12/08/2026, a pedido do dono do produto, ao notar que **esta fase não
existia em nenhum plano**. A Fase 1 entregou o gravador nativo; a Fase 2
entregou o app de transcrição nativo. Nenhuma das duas previu o momento em que
os dois viram **um**.

O equivalente das anteriores são [FASE1.md](FASE1.md) e [FASE2.md](FASE2.md), e
tudo o que elas dizem sobre WASAPI, âncora de relógio e motores continua valendo.

---

## 1. O que a fase entrega

**Um executável, um ícone na bandeja, uma janela.** Hoje são dois programas que
não se conhecem:

| hoje | onde |
|---|---|
| `MeetingRecorder.exe` (15,5 MB) | `C:\Users\andre\MeetingRecorder\` — bandeja, sem janela |
| `MeetingApp.exe` (15,9 MB) | `C:\Users\andre\MeetingApp\` — janela, sem bandeja |

Quem usa grava numa coisa e transcreve em outra, e a única ligação entre elas é
uma pasta no disco. Ao fim desta fase existe **`MeetingApp.exe` sozinho**, que:

1. sobe na bandeja e grava exatamente como o gravador de hoje;
2. abre a janela pelo ícone da bandeja, ou sozinho quando iniciado sem
   `--bandeja`;
3. mostra e controla a gravação **de dentro da janela** — o "espelho" que o
   trilho da UI já reserva, hoje desabilitado;
4. continua rodando na bandeja quando a janela fecha, porque fechar a janela no
   meio de uma reunião não pode parar a gravação.

**A bandeja continua.** Decisão registrada do dono do produto: o espelho na
janela é adição, não substituição. Gravar não pode depender de ter uma janela
aberta.

---

## 2. Por que isto é uma fase, e não um ajuste

Três coisas que parecem detalhe e não são:

**O ciclo de vida inverte.** Hoje o `MeetingApp` é um app de janela: fechar a
janela encerra o processo. Depois desta fase ele é um app de bandeja que
*também* tem janela. Fechar a janela passa a significar "esconder", e sair de
verdade só acontece pelo menu da bandeja. Errar isso perde gravação.

**Dois laços de mensagens Win32 no mesmo processo.** O gravador tem a
`JanelaDeMensagens` (invisível, só para o ícone da bandeja); o app tem a janela
real com o WebView2. Os dois são `HWND` com `WndProc` próprio, e o
`Shell_NotifyIcon` exige uma janela que sobreviva ao `TaskbarCreated` — o
`AoRenascerABarra` do `Tray/Program.cs:64` existe por causa disso.

**A gravação roda enquanto a transcrição roda.** Hoje isso é impossível por
construção: são processos separados que ninguém abre junto. Depois, o usuário
vai transcrever a reunião da manhã enquanto grava a da tarde — com dois motores
Python disputando a GPU e a captura WASAPI não podendo perder um único pacote.
**Este é o risco de qualidade da fase**, e o critério de aceite mais importante
sai dele.

---

## 3. O que porta inalterado (não reabrir)

O gravador tem 5.457 linhas de C# e **todo o valor acumulado do projeto** está
nas menores delas. Nada aqui deve ser reescrito, repensado ou "melhorado de
passagem":

| onde | o que é, e por que não se toca |
|---|---|
| `Core/DriftAnchor.cs` (154 linhas) | a âncora no relógio de parede. A deriva medida sem ela é de +0,10% e +0,145% — 3,7 s e 5,2 s por hora. Já foi trocada por uma versão "tecnicamente melhor" (carimbo QPC) e **perdeu em campo**; voltou por decisão do dono, com a dívida registrada |
| `Core/StreamingResampler.cs` | `sinc_size: 256`. O usuário ouviu um craquelado que três métricas objetivas não pegaram: era alias em −43,3 dB. Com o filtro, −109,9 dB |
| `Core/CrashSafeWavWriter.cs` | cabeçalho WAV que sobrevive a desligamento no meio |
| `Core/PacketTimeline.cs` (241 linhas) | a contabilidade de pacotes de onde saem a inserção e o descarte de amostras |
| `Core/TrackStats.cs` | a instrumentação de silêncio por faixa — maior trecho mudo, total mudo, tempo mutado. Nasceu da gravação de 06/08, que pareceu saudável pelos metadados e tinha o microfone 95,3% em silêncio |
| `Capture/WasapiTrackCapture.cs` (383 linhas) | a captura em si, incluindo o preenchimento dos buracos quando o loopback não dispara `DataAvailable` por não haver áudio tocando |
| `Agenda/` inteiro | OAuth com PKCE, escolha de evento, os participantes que alimentam o vocabulário |

**Mute escreve silêncio, não interrompe a escrita.** Continua valendo, e é o que
mantém as duas faixas alinhadas.

---

## 4. O que a janela precisa mostrar do gravador

O trilho já tem o lugar: o item **Gravador**, hoje `disabled`. O que entra nele
é o espelho do que a bandeja faz — e o menu da bandeja é a especificação pronta
(`Tray/Program.cs:105`):

| do menu da bandeja | na janela |
|---|---|
| Iniciar / Parar gravação | o controle principal, com o tempo decorrido correndo |
| estado atual (gravando, mutado, parado) | um indicador que se lê de relance, sem abrir menu |
| mute do microfone | um botão, **com aviso de mute prolongado** — mute esquecido é o modo de falha mais provável |
| dispositivo de microfone e de loopback | seleção, com o rótulo do dispositivo em uso |
| pasta das gravações | já existe na aba Geral dos ajustes; passa a ser a mesma configuração |
| conta do Google e agenda | vai para uma aba de ajustes, não para a tela de gravação |
| notificações | ajustes |

Duas coisas que a janela pode fazer e a bandeja não, e que justificam o espelho
além da conveniência:

- **os medidores de nível das duas faixas, ao vivo.** É o que teria denunciado o
  microfone mudo de 06/08 no primeiro minuto, em vez de 36 minutos depois;
- **a reunião da agenda que está sendo gravada**, com os participantes já
  reconhecidos — hoje isso só aparece depois, no `meta.json`.

---

## 5. Arquitetura sugerida

Uma solução, três projetos onde hoje há seis. O `recorder-net/` some como árvore
separada e vira parte do app:

```
app-net/
  Nucleo/        + Gravacao/   (o Core do gravador, inalterado)
  Captura/                     (o Capture do gravador, inalterado)
  Agenda/                      (o Agenda do gravador, inalterado)
  Sidecar/
  App/           a janela, o WebView2, a ponte, E a bandeja
  Cli/
  Tests/         as duas suítes juntas
```

**A bandeja entra no `App/`, não num projeto próprio**, porque ela e a janela
disputam o mesmo laço de mensagens e separá-las em bibliotecas só empurraria a
coordenação para uma terceira camada.

**A gravação é um serviço em processo, não um sidecar.** Diferente dos motores,
ela não tem modelo pesado para carregar, não usa GPU e não pode tolerar a
latência de um pipe entre o clique e o início da captura. O isolamento de
processo que os motores exigem, a captura não exige.

**A ponte ganha as operações do gravador** — `gravador-estado`, `gravar`,
`parar`, `mutar`, `dispositivos` — e um canal de eventos: hoje toda resposta é
reação a um pedido da página, e o nível de áudio precisa fluir sem ninguém
pedir. É a única mudança estrutural no contrato da ponte desde que ele existe.

---

## 6. Critérios de aceite

| | o quê |
|---|---|
| **A** | gravar pelo app novo e pelo gravador de hoje **em paralelo**, na mesma reunião, e comparar as faixas amostra a amostra. É o mesmo critério da Fase 1, pelo mesmo motivo: a única prova de que o porte não perdeu nada |
| **B** | **transcrever e gravar ao mesmo tempo**, por uma hora, e a gravação não perder um pacote. O `meta.json` tem que sair sem buracos, com a deriva dentro do que a Fase 1 mediu |
| **C** | fechar a janela durante uma gravação **não para a gravação**; o ícone continua na bandeja e o `meta.json` sai íntegro |
| **D** | matar o processo pelo Gerenciador de Tarefas no meio de uma gravação deixa um WAV **legível** (o `CrashSafeWavWriter` já garante isso — aqui é a régua de que continua garantindo) |
| **E** | o soak de 1 h da Fase 1, repetido com o binário fundido |
| **F** | o instalador único entrega os dois papéis, e a migração lê o `settings.json` do gravador e o `app.json` do app sem o usuário reconfigurar nada |

O **B** é o que decide a fase. Se gravar durante uma transcrição perder áudio, a
alternativa é voltar a dois processos — o que é uma decisão de arquitetura, não
um ajuste, e por isso ele vem cedo na ordem de trabalho.

---

## 7. Ordem de trabalho sugerida

1. **Medir o B antes de fundir nada.** Abrir o gravador de hoje e o app de hoje
   ao mesmo tempo, gravar uma hora enquanto se transcreve uma reunião longa, e
   olhar o `meta.json`. Os dois programas já existem; isto não custa código
   nenhum e responde a pergunta que decide o desenho. *Se aqui já perder áudio,
   a fusão em um processo está descartada antes de começar.*
2. **Mover as árvores** (`recorder-net/` → `app-net/`), sem mudar
   comportamento, com as duas suítes verdes.
3. **Bandeja e janela no mesmo processo**, com a inversão do ciclo de vida:
   fechar a janela esconde, sair é pelo menu.
4. **As operações do gravador na ponte**, e o canal de eventos.
5. **A tela do gravador**, com os medidores de nível.
6. **Instalador único e migração** das duas configurações.

---

## 8. Fora desta fase

- **Resumo e ata por LLM.** Continua sendo a próxima fase de verdade, e é onde
  entram os `templates/*.json` por tipo de reunião que a análise do Meetily
  registrou ([FASE2-HANDOFF.md](FASE2-HANDOFF.md) §10).
- **Motores como pacotes baixáveis** (CUDA/Vulkan/CPU como variantes), que é o
  que encolheria o instalador de verdade — ver [PLANO.md](PLANO.md) §5.
- **A integração com o Teams** (espelhar o mute), que está na PLANO.md §2.1 e não
  depende desta fusão.
- **Linux e Mac.** Os motores já são multiplataforma; o núcleo não. Continua
  sendo decisão de ordem, não dívida.

---

## 9. O risco que vale dizer em voz alta

O gravador funciona. Ele passou no soak de 1 h, grava reuniões reais toda semana,
e o que ele resolve — deriva de clock, mute que escreve silêncio, WASAPI em
loopback — custou caro para acertar e é invisível quando está certo.

**Esta fase mexe nele para ganhar conveniência, não qualidade de gravação.** O
usuário ganha um app só, um instalador só e o nível de áudio à vista; a gravação
em si não fica melhor. Por isso a ordem de trabalho começa medindo, e por isso o
critério A é comparar amostra a amostra com o gravador atual: o pior resultado
possível desta fase é ficar mais bonita e gravar pior.
