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

## Publicar uma versão

```bash
$EDITOR app-net/Directory.Build.props     # <Version>0.1.1</Version>
$EDITOR CHANGELOG.md                      # o que mudou, para quem usa
$EDITOR versao.json                       # a MESMA versão, e uma linha de notas
tools/publicar.sh                         # se algum motor.py mudou
tools/montar_instalador.sh
git push                                  # é o push que faz o aviso aparecer
```

**O `versao.json` só vale depois do push**, porque o canal é o repositório. Subir
o número antes de o instalador existir avisa todo mundo de uma versão que ninguém
tem como pegar.

O campo `onde` é opcional: preenchido, a tela diz onde baixar; vazio, ela diz
para pedir o instalador.

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
