# PulseMeet — instalar

Este texto é para quem vai **usar** o app, não para quem o compila. Ele grava
suas reuniões, transcreve, separa quem falou e escreve a ata — tudo na sua
máquina, sem mandar áudio nem texto para lugar nenhum.

---

## O que a sua máquina precisa ter

| | |
|---|---|
| **Windows 10 ou 11**, 64 bits | é o único sistema desta versão |
| **placa NVIDIA** | não é obrigatória, mas leia "Sem placa NVIDIA" abaixo |
| **~12 GB livres** | 5 GB do app + os modelos que ele baixa depois |

Não é preciso instalar Python, CUDA, .NET nem nada. Está tudo dentro.

---

## Instalar

1. Rode o `MeetingApp-0.4.0-instalador.exe`. O arquivo ainda se chama
   `MeetingApp` de propósito: o nome do produto mudou na 0.4.0, o do
   executável não — é o que faz o Windows tratar a versão nova como
   atualização, e não como um segundo programa.

2. **O Windows vai mostrar um aviso azul** dizendo que o editor é desconhecido.
   Isso acontece porque o instalador não é assinado — assinatura de código custa
   uma certificação anual que esta primeira versão não tem. Clique em **Mais
   informações** e depois em **Executar assim mesmo**.

3. Ele instala em `C:\Users\<você>\AppData\Local\Programs\MeetingApp`, sem pedir
   senha de administrador, e deixa marcada a opção de **iniciar junto com o
   Windows**. Vale manter: um gravador que não está aberto quando a reunião
   começa não grava nada.

4. Na primeira vez que abrir, o app leva direto para **Ajustes → Modelos**. É de
   propósito: falta baixar o cérebro dele.

---

## A primeira execução: o que ainda falta baixar

O instalador não traz os modelos dentro — eles somam mais de 6 GB, e você
provavelmente não quer todos. Tudo se baixa em **Ajustes → Modelos**, com barra
de progresso e o tamanho à vista.

| o quê | tamanho | para quê | quando baixar |
|---|---|---|---|
| **Large v3** | 3,1 GB | transcrever | antes da primeira reunião |
| **Motor de ata** | 641 MB | rodar o modelo de ata na sua placa | antes da primeira ata |
| **Qwen3 4B** | 2,5 GB | escrever a ata | antes da primeira ata |

Baixe o **Large v3** antes de precisar dele: numa conexão comum são 10 a 20
minutos, e você não quer descobrir isso logo depois de uma reunião de uma hora.

Se você não for usar atas, os dois últimos nunca precisam ser baixados — são
3,1 GB que ficam fora da sua máquina.

O modelo que **separa quem falou** já vem dentro do app. Você não precisa baixar
nada para isso, e ele funciona sem internet.

---

## O ícone na bandeja

O app vive ao lado do relógio, e é de lá que ele grava:

| cor | o que está acontecendo |
|---|---|
| cinza | parado |
| vermelho | gravando |
| laranja | gravando, mas o seu microfone está mudo |
| amarelo | algo pede atenção |

**Fechar a janela não fecha o app** — ela só some, e a gravação continua. Sair de
verdade é pelo menu do ícone, com o botão direito. Isso é de propósito: fechar a
janela por engano no meio de uma reunião perderia a gravação inteira.

---

## Sem placa NVIDIA

O app funciona, mas **pela CPU**, e a diferença é grande: uma reunião de uma hora
que levaria uns 15 minutos para transcrever passa a levar algumas horas. A tela
de Modelos avisa quando isso acontece — não é o app travado.

Se for o seu caso, escolha um modelo menor (**Medium** ou **Small**) em Ajustes →
Modelos. Perde-se exatidão, e ganha-se a possibilidade de usar.

Placas AMD e Intel não são aceleradas nesta versão.

---

## Onde ficam as suas coisas

| o quê | onde |
|---|---|
| gravações, transcrições, atas e notas | `Documentos\MeetingRecordings` |
| configurações e vozes aprendidas | `C:\Users\<você>\.meeting-transcription` |
| modelos baixados | `C:\Users\<você>\.cache\huggingface` |

**Desinstalar não apaga nada disso.** O desinstalador tira o programa e os
modelos de ata, e mostra na tela onde o resto ficou. Se quiser apagar tudo,
remova essas pastas à mão.

---

## Atualizar

Não há atualização automática. Quando chegar uma versão nova, é só rodar o
instalador dela por cima: gravações, transcrições, notas, vozes, projetos e
modelos baixados continuam onde estão.

---

## Quando algo der errado

Vá em **Ajustes → Geral → Sobre** e clique em **Copiar diagnóstico**. Ele copia
um bloco com a versão, a sua placa, os modelos instalados e a pasta das
gravações — sem nome de reunião, de cliente nem de participante, então dá para
colar numa conversa sem pensar duas vezes.

Mande esse bloco junto com três respostas:

1. a instalação travou em algum ponto?
2. deu para gravar uma reunião inteira?
3. a ata fez sentido?

É a terceira que mais importa, e é a única que nenhuma máquina responde sozinha.

---

## Uma coisa sobre a agenda do Google

O app pode ler a sua agenda para saber qual reunião está acontecendo e usar os
participantes como vocabulário — o que melhora bastante os nomes na transcrição.

Nesta primeira versão a autorização do Google pode não funcionar na sua conta:
o app ainda não passou pela verificação do Google, e enquanto isso só contas
previamente cadastradas conseguem autorizar. **Tudo o mais funciona sem isso** —
gravar, transcrever, separar falantes e gerar ata não dependem da agenda.
