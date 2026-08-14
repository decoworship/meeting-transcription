# Mudanças

Escrito para quem usa o app, não para quem o compila. O histórico técnico está
nos commits e nos `docs/*-HANDOFF.md`.

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
  problema à distância sem vinte perguntas.

O que ainda **não** existe, dito com todas as letras:

- **sem placa NVIDIA o app funciona, mas devagar** — uma reunião de uma hora pode
  levar horas. Esta versão só traz o caminho CUDA;
- **não há atualização automática.** Atualizar é rodar o instalador novo;
- o instalador **não é assinado**, então o Windows mostra um aviso de editor
  desconhecido na primeira execução.
