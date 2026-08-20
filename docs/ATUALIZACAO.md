# A rota de atualização

Escrito em 17/08/2026, depois de a Fase 4 fechar e o instalador chegar à máquina
de outra pessoa. A carta da fase tinha dispensado o assunto
([FASE4.md](FASE4.md) §10) com um argumento que era verdadeiro e deixou de ser
no dia seguinte.

---

## Por que ela é pré-requisito, e não enfeite

Duas razões, e a segunda é a que decide a ordem do trabalho:

1. **Existe gente com o app instalado** numa máquina que não é a de quem
   compila. Sem rota, toda correção fica presa aqui;
2. **O catálogo de modelos é código.** `Catalogo.Pacotes` é uma lista C# dentro
   do binário, e o modelo de ata é escolhido por ela. Então **oferecer um modelo
   novo é publicar uma versão nova do app** — não adianta achar um modelo de ata
   melhor se ele não chega em ninguém.

---

## O que existe hoje: o app avisa

O degrau mais barato dos três desenhados em
[FASE4-HANDOFF.md](FASE4-HANDOFF.md) §6.1.

| | |
|---|---|
| **canal** | `versao.json` na raiz do repositório, servido pelo raw.githubusercontent |
| **quem lê** | `Nucleo/Atualizacao.cs` |
| **quando** | ao abrir a lista de Reuniões, e ao abrir os Ajustes |
| **o que aparece** | uma linha no alto da lista, dispensável, e um bloco em Ajustes › Sobre |
| **desligar** | Ajustes › Geral › "Avisar de versão nova" |

**Não há servidor.** O arquivo mora no repositório público e é editado no mesmo
commit que sobe a versão — que é justamente o que impede os dois de divergirem.
Nada para manter, nada para pagar, nada que possa sair do ar sozinho.

**O app só avisa.** Ele não baixa nem troca binário, e isso é decisão e não
preguiça: baixar e executar um instalador é ensinar o app a rodar o que veio da
internet, e sem assinatura de código isso é um vetor de ataque, não uma
funcionalidade.

### O que trafega

Um `GET` de um arquivo público, **sem parâmetro nenhum** — sem identificador,
sem a versão instalada, sem contador. Quem hospeda vê o download de um arquivo,
como qualquer visita a uma página. É a única conexão que o app abre por conta
própria, e ela é desligável.

### Como se comporta quando falha

Falhar é o caso comum: máquina offline, proxy de empresa, GitHub fora. Nada
disso aparece como problema para quem só queria transcrever uma reunião — o
aviso some, a tela abre igual, e tenta de novo na próxima vez.

---

## O segundo canal: o winget

Escrito em 19/08/2026, no dia em que o primeiro release público saiu — a
[v0.3.0](https://github.com/decoworship/meeting-transcription/releases/tag/v0.3.0),
que até então só existia como arquivo entregue na mão.

O winget não hospeda nada: ele guarda um YAML que aponta para o instalador no
GitHub Releases. Os três arquivos do manifesto, o que cada um diz e como testar
antes de publicar estão em
[instalador/winget/LEIAME.md](../instalador/winget/LEIAME.md). Aqui fica só o
que ele muda nesta rota.

**O que ele resolve.** O degrau 2 lá embaixo foi adiado porque baixar e executar
um instalador é ensinar o app a rodar o que veio da internet. O winget faz
exatamente isso **sem que o app aprenda nada** — quem baixa, confere o SHA256 e
executa é um cliente da Microsoft, e o app continua só avisando.

**O que ele não resolve**, e convém não confundir com o que resolve:

- **não substitui a assinatura de código.** O winget confere que o arquivo é o
  que você publicou, não que você é quem diz ser. O SmartScreen continua
  avisando na primeira execução;
- **não empurra.** Ninguém é atualizado sozinho: o `winget upgrade` é rodado por
  quem usa, quando quer. Quem avisa que existe versão nova continua sendo o
  `versao.json` desta página — os dois se somam, um não substitui o outro;
- **não emagrece o download.** Cada `winget upgrade` baixa o instalador inteiro,
  1,59 GB, mesmo quando só o `MeetingApp.exe` de 18 MB mudou. É o mesmo problema
  do degrau 2, com o mesmo remédio: separar a versão do app da versão dos
  motores.

**O `winget upgrade` só existe com o pacote numa fonte.** Instalar por
`--manifest` — que é como se testa, e é tudo o que dá para fazer hoje — não
deixa rastro de onde o app veio, então não há a quem perguntar por versão nova.
Enquanto o PR em `microsoft/winget-pkgs` não for aceito, cada versão nova é uma
instalação manual, e o ganho do winget é só não precisar mandar o arquivo.

**O PR ainda não foi aberto, e o motivo é o tamanho.** O pipeline de validação
da Microsoft baixa o instalador inteiro e roda antivírus nele; 1,59 GB está bem
acima do que costuma passar por lá. Separar os motores é pré-requisito prático
da submissão, e não só economia de banda — o que faz do degrau 2 o caminho para
o winget, e não uma alternativa a ele.

---

## Publicar uma versão

```bash
$EDITOR app-net/Directory.Build.props     # <Version>0.4.0</Version>
$EDITOR CHANGELOG.md                      # o que mudou, para quem usa
$EDITOR versao.json                       # a MESMA versão, e uma linha de notas
tools/publicar.sh                         # se algum motor.py mudou
tools/montar_instalador.sh
```

Até aqui é o que sempre foi. O resto existe desde 19/08/2026, e é o que põe o
instalador ao alcance de quem não recebe arquivo na mão:

```bash
V=0.4.0
sha256sum dist/instalador/MeetingApp-$V-instalador.exe   # anote: vai no manifesto

gh release create v$V \
  --target "$(git rev-parse HEAD)" \
  --title "PulseMeet $V — <o título da seção do CHANGELOG>" \
  --notes-file <um .md com as notas, o SHA256 e o aviso de SmartScreen> \
  dist/instalador/MeetingApp-$V-instalador.exe

cp -r instalador/winget/<versão anterior> instalador/winget/$V
$EDITOR instalador/winget/$V/*.yaml       # PackageVersion, InstallerUrl,
                                          # InstallerSha256, DisplayVersion,
                                          # ReleaseDate, ReleaseNotesUrl
$EDITOR versao.json                       # "onde": a URL do release

git push                                  # é o push que faz o aviso aparecer
```

Três coisas que já custaram tempo, todas na primeira vez:

- **`--target` quer o SHA completo.** Abreviado, a API responde
  `422 Release.target_commitish is invalid` e não diz por quê;
- **o SHA256 muda a cada build.** O instalador não é reproduzível; copiar o hash
  da versão anterior faz o winget recusar o download — na máquina da outra
  pessoa, não na sua;
- **o `DisplayVersion` do manifesto tem que bater com o que o Inno registra.**
  Se não bater, a instalação dá certo, o app abre, e o `winget upgrade` nunca
  enxerga o pacote. Rode `winget list MeetingApp` depois de instalar: é o único
  jeito de descobrir isso antes de publicar.

**O `versao.json` só vale depois do push**, porque o canal é o repositório. Subir
o número antes de o instalador existir avisa todo mundo de uma versão que ninguém
tem como pegar.

O campo `onde` é opcional: preenchido, a tela diz onde baixar; vazio, ela diz
para pedir o instalador. Ele nasceu vazio porque não havia URL nenhuma para pôr
nele — desde a v0.3.0 há: a página do release.

---

## Os degraus que ficam para depois

**2 — atualizar só o app.** O aviso vira botão: baixa o `MeetingApp.exe`
(18,5 MB), confere, troca e reabre. É o degrau que mais paga, porque **os
motores são 4,1 GB e quase nunca mudam** — a maioria das versões novas é só o
executável. Exige assinatura de código ou conferência de hash, e o segundo sem o
primeiro protege pouco.

**3 — instalador completo por dentro.** Só quando os motores mudarem. É o que já
existe, apenas disparado de dentro do app.

Ferramentas prontas (Velopack, Squirrel, WinSparkle) cobrem 2 e 3 com delta e
assinatura. Entrar nelas é decisão a tomar sabendo que o payload grande raramente
muda — e que a economia real está em separar a versão do app da versão dos
motores, coisa que hoje não existe.
