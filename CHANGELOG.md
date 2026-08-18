# Mudanças

Escrito para quem usa o app, não para quem o compila. O histórico técnico está
nos commits e nos `docs/*-HANDOFF.md`.

## 0.2.1 — dá para saber o que aconteceu

Uma transcrição travou o computador de um usuário, e não havia nada para olhar
depois. Esta versão é sobre isso.

**O app passa a manter um registro** em `%USERPROFILE%\.meeting-transcription\registro.log`:
o que ele fez, em qual placa, e o que os motores disseram. Nada de transcrição,
nome de cliente ou de participante entra ali — e o arquivo só sai da sua máquina
se você mandar. O caminho dele aparece no bloco de diagnóstico.

**A transcrição diz onde está rodando** — "transcrevendo em NVIDIA GeForce
RTX 2060" — em vez de deixar você adivinhar.

**E recusa rodar na CPU sem você mandar.** Transcrever pela CPU leva horas e
consome muita memória; num computador apertado, o suficiente para travá-lo. Se a
sua máquina não tem placa NVIDIA e você quer mesmo assim, ligue "Transcrever sem
placa" em Ajustes → Transcrição.

## 0.2.0 — a ata fica inteira

Esta versão é quase toda sobre a **ata**. Quatro defeitos faziam com que boa
parte do trabalho do modelo nunca chegasse até você.

**A ata deixa de sair repetida.** Havia uma segunda ata inteira dentro dela, e a
lista de pendências aparecia duas vezes. As duas coisas eram erro nosso.

**"Decisões técnicas" volta a aparecer.** Nas atas de sessão de trabalho, o
raciocínio por trás de cada decisão — por que se escolheu aquilo, o que foi
descartado e o que isso trava daqui para frente — estava sendo apagado antes de
chegar ao arquivo. É a parte que faz a ata valer alguma coisa três meses depois.

**As pendências param de cair todas no colo da mesma pessoa.** A ata passa a
percorrer os participantes um a um; "me manda o número que eu falo com o fulano"
agora vira duas tarefas, uma de cada lado.

**O que se combinou _não_ fazer também vira decisão.** "Não corrija antes de
falar comigo" é o tipo de combinado que some da ata e faz alguém agir sem ele.

**Reuniões longas param de falhar.** A ata calculava o espaço necessário pelo
relógio, e reunião com conversa densa estourava a conta — uma sessão de 39
minutos falhava. Agora a conta é feita pelo texto de verdade, e uma reunião de
duas horas cabe.

**Dois modelos novos de ata**, em Ajustes → Modelos, medidos em 30 atas:

- **Gemma 4 E4B** — o mais rápido e o único que não falhou nenhuma vez. São 5 GB;
- **Qwen3.5 4B** — rápido e menor, mas registra menos pendências.

O padrão continua sendo o Qwen3 4B.

**O app avisa quando sai versão nova**, em Ajustes → Geral → Sobre e no alto da
lista de reuniões. Ele só avisa: não baixa nada sozinho. Dá para desligar.

## 0.1.0 — a primeira versão instalável

A primeira que se instala em vez de se copiar. O app faz, nesta ordem, o que uma
reunião pede:

- **grava** em duas faixas separadas, o seu microfone e o áudio do sistema, com
  correção de deriva de relógio — uma reunião de duas horas continua sincronizada
  no fim;
- **transcreve** com o Whisper large-v3 na sua placa, e **separa quem falou**;
- **aprende as vozes** entre reuniões: quem já foi identificado uma vez volta com
  nome na próxima;
- **notas** escritas durante a reunião, guardadas junto dela, alimentando o
  vocabulário da transcrição;
- **ata** escrita por um modelo que roda na sua máquina — nada de transcrição de
  cliente saindo daqui;
- **agenda**: o Google Calendar diz qual reunião está acontecendo, e os
  participantes viram vocabulário;
- exportação em txt, srt, vtt e docx.

Novo nesta versão, e é o que a torna entregável:

- **versão à vista e bloco de diagnóstico** nos Ajustes, em Geral. O botão copia
  versão, placa, modelos instalados e pasta das gravações — é o que resolve um
  problema à distância sem vinte perguntas;
- **o que é grande e opcional se baixa quando faz falta.** O modelo de
  transcrição, o motor de ata e o modelo de ata ficam fora do instalador, em
  Ajustes → Modelos, com barra e tamanho à vista. Quem não usa ata nunca baixa os
  3,1 GB dela;
- **separar quem falou funciona sem internet**, desde a primeira reunião: os
  modelos de diarização vêm dentro do app.

O que ainda **não** existe, dito com todas as letras:

- **sem placa NVIDIA o app funciona, mas devagar** — uma reunião de uma hora pode
  levar horas. Esta versão só traz o caminho CUDA;
- **não há atualização automática.** Atualizar é rodar o instalador novo;
- o instalador **não é assinado**, então o Windows mostra um aviso de editor
  desconhecido na primeira execução.
