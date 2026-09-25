# Plano 4a · Ajustes › Clientes e projetos

**Objetivo.** A seção Clientes de Ajustes vira mestre-detalhe, como a prancha
(`.superpowers/sdd/ref/aj-clientes.png`) e o spec §3.4: clientes à esquerda com
busca e "+ Cliente"; à direita os projetos do escolhido com "+ Projeto"; o
projeto aberto mostra vocabulário em etiquetas, Idioma, Modelo, Falantes e
**Tipo de ata** padrão, com "Ver as reuniões deste projeto", "Renomear" e
"Apagar projeto".

## Decisões

- **D-C:** a aba continua "Clientes"; nenhuma seção muda de nome. O que existe
  hoje e a prancha não mostra (modelo de diarização, a dica do vocabulário, o
  "salvando…/salvo", o texto de como cliente e projeto nascem) vai **abaixo** do
  que a prancha mostra. Nada sai.
- **Contagens na página.** "N projetos · N reuniões" e "N reuniões · última em
  17 set" saem da lista `gravacoes` (cliente/projeto de cada uma, dia pelo nome
  da pasta). Sem op nova: a lista já é lida pela tela de Reuniões e é barata.
- **Tipo de ata por projeto é dado novo.** `PreferenciasDoProjeto` ganha
  `tipo_de_ata` (o id de `modelos-de-ata`). "Padrão do app" grava `""` e não
  `null`: o `Salvar` ignora nulo (`WhenWritingNull`), então nulo nunca voltaria
  ao padrão. O preparo não manda a chave e por isso não a apaga (mescla).
- **O tipo do projeto é usado:** a aba Ata abre com o tipo do projeto da reunião
  escolhido, quando ele existe no catálogo; senão, o primeiro, como hoje.
- **"+ Cliente"** pede o nome do cliente e o do primeiro projeto: o núcleo só
  cria cliente por `salvar-projeto` (cliente sem projeto não existe no arquivo).
  **"+ Projeto"** pede o nome e salva preferências vazias.
- **"Ver as reuniões deste projeto"** abre Reuniões com o filtro de cliente e a
  busca com o nome do projeto (não há filtro de projeto na lista; a busca já
  cobre projeto). `reunioes.js` exporta `filtrarReunioesPor({cliente, texto})`.
- **Idioma** vira seletor (Detectar sozinho, Português, Inglês, Espanhol), com
  o valor gravado que não estiver na lista preservado como opção própria.
  **Falantes**: Separar / Não separar (a chave `diarization`).
- **Renomear e Apagar o cliente** ficam no `⋯` do cabeçalho do cliente (popover).
- **Escolha preservada:** cliente e projeto abertos moram no módulo, e sobrevivem
  ao `recarregar()` depois de criar, renomear ou apagar.
- **F-2:** digitar na busca de clientes redesenha só a lista, nunca o campo.

## Mapa de arquivos

| arquivo | o quê |
|---|---|
| `app-net/Nucleo/Projetos.cs` | `TipoDeAta` (`tipo_de_ata`) |
| `app-net/Tests/ProjetosTests.cs` | ida e volta; salvar sem a chave não a apaga |
| `app-net/App/web/clientes-regras.js` (novo) | contagens, rótulos, filtro da busca |
| `tools/web/clientes-regras.test.mjs` (novo) | as regras puras |
| `app-net/App/web/clientes.js` (novo) | a seção mestre-detalhe |
| `app-net/App/web/configuracoes.js` | a aba Clientes chama `clientes.js`; lê `gravacoes` e `modelos-de-ata` |
| `app-net/App/web/reunioes.js` | `filtrarReunioesPor` |
| `app-net/App/web/atas.js` | abre no tipo do projeto |
| `app-net/App/web/app.css` | `.clientes*` só com tokens |
| `tools/provar_ajustes.py` (novo) | a tela no Chromium, `--so`, `--fotos` |
| `tools/provar_reunioes.py` | a ata abre no tipo do projeto |

## Tarefas

1. **Núcleo: `tipo_de_ata` no projeto.** RED: teste em `ProjetosTests` que
   salva `TipoDeAta = "cliente"`, relê, e salva de novo sem a chave e confere
   que ficou. GREEN: a propriedade.
2. **Regras puras.** RED: `clientes-regras.test.mjs` — contagens por cliente e
   projeto (reunião sem cliente não conta; projeto sem reunião dá 0),
   "1 projeto · 6 reuniões", "5 reuniões · última em 17 set", "nenhuma reunião
   ainda", busca sem acento e sem caixa. GREEN: `clientes-regras.js`.
3. **A tela mestre-detalhe.** RED: `provar_ajustes.py` — a aba Clientes mostra
   a lista com as contagens, o primeiro cliente escolhido, os projetos dele; um
   clique abre o projeto com as etiquetas, os quatro seletores na ordem da
   prancha e os três botões; a busca filtra sem tirar o foco do campo; o que
   não está na prancha (diarização, dica) vem depois dos botões. GREEN:
   `clientes.js` + CSS.
4. **Gravar.** RED: pôr/tirar etiqueta, trocar Idioma, Modelo, Falantes e Tipo
   manda `salvar-projeto` com as prefs certas (tipo padrão = `""`); "+ Projeto"
   e "+ Cliente" mandam `salvar-projeto` com os nomes pedidos e a escolha vai
   para o novo; Renomear/Apagar pedem as ops de sempre. GREEN.
5. **Ver as reuniões.** RED: o botão leva a Reuniões com o cliente filtrado e o
   projeto na busca, e só as reuniões dele à vista. GREEN: `filtrarReunioesPor`.
6. **A ata usa o tipo do projeto.** RED: prova em `provar_reunioes.py` — reunião
   de Algar/Agentes com `tipo_de_ata: "cliente"` abre a aba Ata com "Reunião com
   cliente". GREEN: `atas.js`.
7. **Comparar com a prancha** (`--fotos /tmp/claude-1000/fotos-4a`), ajustar,
   rodar tudo, marcar 4a no §5 do spec, relatório.
