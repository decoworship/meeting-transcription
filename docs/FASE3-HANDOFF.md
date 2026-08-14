# Fase 3 — handoff: a reunião depois da reunião

O que a fase de fato entregou, medido, e o que ela deixa para a Fase 4. A carta
está em [FASE3.md](FASE3.md); a arquitetura do motor de ata, com os números da
máquina, em [ATA.md](ATA.md).

Executada entre 13 e 14/08/2026, na branch `feat/fase-3-atas-e-notas`.

---

## 1. Os três itens

| # | o quê | como ficou |
|---|---|---|
| 1 | a transcrição sobrevive à navegação | estado no núcleo, evento `id: 0`, bolinha no trilho — e **parar a transcrição**, que não estava previsto |
| 2 | notas de reunião | `notas.md`, no Gravador e na reunião, alimentando o vocabulário |
| 3 | ata por LLM local | motor llama.cpp, esquema, verificador, redator, destino **Atas** |

E cinco coisas que **não estavam na carta** e entraram porque o uso cobrou:

- **parar a transcrição** e liberar a placa (a GPU é uma só, e a transcrição nem
  sempre é o mais importante);
- **o vínculo cliente/projeto** virou `reuniao.json` na pasta da gravação —
  antes só existia dentro da transcrição, que na tela de preparo ainda não
  existe;
- **o seletor de diarização passou a valer.** Era decorativo: colhido na tela,
  salvo nas preferências e ignorado pelo pipeline;
- **a organização de cada participante** sai do domínio do e-mail da agenda, e
  não de dedução do modelo;
- **uma bolinha por destino** — gravar convive com transcrever ou com escrever
  ata; transcrever e escrever ata nunca convivem.

---

## 2. O que foi medido, e onde

Tudo na máquina do usuário: RTX 2060 de 6 GB, driver 595.97, com as gravações
reais dela.

| pergunta | resposta |
|---|---|
| as skills cabem no contexto? | **7%** — 2.400 tokens com a referência junto |
| uma reunião de 1 h cabe? | sim, folgada. 2 h também, com KV em q4_0 |
| quanto custa o KV? | **62 KiB por token** em q8_0 — é ele que decide se cabe, não o modelo |
| quanto demora? | 29 min → 55 s · 42 min → 98 s · **122 min → 236 s** |
| o modelo inventa? | **não.** Zero fatos falsos, zero donos fora da lista |
| então qual é o defeito? | **omissão**: recuperou 7 de 14 números da reunião medida |
| a gramática segura o formato? | sim, e **só pelo `llama-server`** — pelo `llama-cli` ela colide com o template de chat |

Ferramentas que ficam: `tools/medir_motor_de_ata.py` (mede o motor com o prompt
real) e `tools/checar_transcricao.py` (34 conferências de comportamento num
Chromium de verdade, com ponte falsa).

---

## 3. As decisões que valem revisitar

**HTTP no motor de ata, contra a doutrina do SIDECAR.md.** Os motores Python
falam por pipe — sem porta, sem Firewall. O de ata fala HTTP com o
`llama-server`, porque a saída constrangida por esquema **não funciona pelo
`llama-cli`** (a gramática vale desde o primeiro token e colide com o
`<|im_start|>`), e sem ela a ata deixa de ser verificável. Mitigado com loopback,
porta sorteada a cada execução e processo filho.

**O modelo preenche campos; o C# escreve o arquivo.** É o que garante formato
(`- [ ] Ação — **Responsável** — prazo` sai assim porque o redator o escreve) e
o que torna a ata verificável — "este item tem dono?" é pergunta sobre um campo.

**O verificador é determinístico e nunca silencioso.** Ele troca dono inventado
por `[responsável a definir]`, move decisão sem eco na fala para pontos em
aberto, retira risco que ninguém levantou, corrige o lado da pendência pelo
domínio do e-mail, e lista os números citados que não entraram. Tudo o que ele
mexe vira linha em "Observações".

**Um esquema universal, e não um por tipo.** É o que permite customizar um tipo
de ata escrevendo Markdown em `%USERPROFILE%\.meeting-transcription\atas\`, sem
escrever JSON Schema. O custo: as seções muito estruturadas de alguns tipos (o
"Por pessoa" da sprint) caem como prosa.

---

## 4. O que a Fase 4 herda

1. **3,5 GB de motor de ata** (llama.cpp + GGUF) montados por
   `tools/empacotar_motor_de_ata.sh`. O instalador precisa deles, ou de baixá-los
   na primeira execução — a tela de Modelos já sabe baixar o GGUF sozinha;
2. **o build de CUDA tem que casar com o driver.** O `cuda-13.3` falha nesta
   máquina com *"the provided PTX was compiled with an unsupported toolchain"*
   (driver 595.97, que anuncia 13.2); o **12.4 funciona** e é compatível para
   trás. Publicar o 12.4, ou detectar;
3. ~~medir 12.4 × 13.x~~ — **decidido em 14/08/2026: fica na 12.4**, por
   compatibilidade. Ela roda em driver novo e velho; a 13.3 exige um mais novo
   que o de hoje.

---

## 5. O que ficou de fora, com o custo registrado

> Tudo o que segue, e mais o que a Fase 3 deixou incompleto de propósito, está
> organizado com gatilhos em [FASE6.md](FASE6.md) — a fase de revisões, criada
> por decisão do dono do produto para não atrasar a primeira versão.

- **map-reduce para reunião acima de ~2h15.** A medição mostrou que 2 h cabem
  numa passada, e um caminho de blocos existindo é um caminho de blocos sendo
  usado por engano;
- **tool calling de verdade** (o modelo decidindo chamar função). O protocolo
  reserva o campo; num 4B, trocaria um problema resolvido por um não resolvido;
- **keep-alive do motor.** O modelo carrega em 5 s; manter o processo vivo
  economiza isso e prende 2,5 GB de VRAM que a próxima transcrição vai querer;
- **provedor remoto de LLM.** O motor está atrás de uma interface, mas nada de
  remoto foi escrito — a decisão de rodar local é do dono do produto, e o
  critério E não a reabriu;
- **organização das gravações antigas.** As anteriores a 14/08 não têm
  `attendee_emails`; nelas o lado de cada pendência fica como o modelo escolheu,
  porque preferir "não sei" a chutar é a regra.

---

## 6. O risco que continua de pé

**Um 4B não segue 1.700 tokens de instrução como um modelo de fronteira.** Toda a
arquitetura é compensação para isso: o app classifica em vez do modelo, a
gramática impõe em vez de pedir, o verificador confere em vez de confiar, e o
roteiro de fatos entrega procurado o que ele esqueceria de procurar.

Na comparação lado a lado, ele **não inventou nada** e **omitiu metade**. As duas
redes contra omissão — roteiro no prompt, conferência depois — são a aposta desta
fase. Se, com uso real, a lista de "números que não entraram" trouxer coisa
importante com frequência, o caminho não é apertar o modelo: é subir de modelo,
ou reabrir a decisão de provedor.
