# O manifesto do winget

O winget não hospeda nada. Ele guarda um YAML que aponta para o instalador
publicado no GitHub Releases, e é o `montar_instalador.sh` que produz o arquivo
para onde o YAML aponta.

Três arquivos por versão, todos obrigatórios:

| arquivo | o que diz |
|---|---|
| `decoworship.PulseMeet.yaml` | o índice: identificador, versão, idioma padrão |
| `...locale.pt-BR.yaml` | nome, licença, descrição — o que aparece no `winget show` |
| `...installer.yaml` | a URL, o SHA256 e como instalar em silêncio |

## O identificador ainda pode mudar — e só agora

O `PackageIdentifier` virou `decoworship.PulseMeet` na 0.4.0, junto com o nome
do app. Foi de graça porque **nada tinha sido submetido ao
`microsoft/winget-pkgs`** — e continua sem ser. Enquanto for assim, ele pode
mudar de novo; a pasta `0.3.0/` guarda o desenho anterior, com o identificador
antigo, porque é o que casa com o release v0.3.0 que existe.
Depois da primeira submissão aceita, o mesmo `PackageIdentifier` é a única coisa
que faz o `winget upgrade` reconhecer as versões seguintes: trocá-lo ali vira um
segundo pacote, e quem instalou pelo primeiro nunca mais recebe atualização.

Como a marca ainda não está fechada (ver [../../docs/MARCA.md](../../docs/MARCA.md)),
a ordem que evita o problema é: **decidir o nome, depois submeter.** O que muda
junto, quando decidir, é o nome dos três arquivos, o `PackageIdentifier` dos
três, o `PackageName` e o `Moniker` do locale.

O `ProductCode` **não** entra nessa conta: ele sai do `AppId` do `.iss`, que não
muda nunca — é ele que faz o Windows ver atualização em vez de programa novo.

## Testar antes de publicar

Do Windows, com o manifesto numa pasta local (o winget não lê caminho UNC do
WSL de forma confiável — copie para o `C:`):

```powershell
winget validate --manifest C:\caminho\0.4.0
winget install --manifest C:\caminho\0.4.0
winget list PulseMeet         # confere que o ProductCode casou
winget uninstall PulseMeet
```

O `winget list` é a parte que se esquece: se ele não achar o pacote, o
`ProductCode` está errado e o `winget upgrade` nunca vai funcionar, mesmo com a
instalação tendo dado certo.

## Publicar uma versão nova

O SHA256 muda a cada build — o instalador não é reproduzível. Não copie o
número da versão anterior.

```powershell
wingetcreate update decoworship.PulseMeet `
  --version 0.4.0 `
  --urls https://github.com/decoworship/meeting-transcription/releases/download/v0.4.0/MeetingApp-0.4.0-instalador.exe `
  --submit --token $env:GITHUB_TOKEN
```

Isso abre um PR em `microsoft/winget-pkgs`. Enquanto esse PR não for aceito, a
instalação por uma linha só é a forma com `--manifest` acima.

## O que ainda não foi resolvido

- **O instalador tem 1,5 GB.** O pipeline de validação da Microsoft baixa o
  arquivo inteiro e roda antivírus nele. Esse tamanho é bem acima do usual, e é
  o risco número um da submissão ao repositório da comunidade. Se reprovar, a
  saída é o instalador baixar os motores no primeiro uso.
- **O binário não é assinado.** Não impede a submissão, mas o SmartScreen avisa
  na primeira execução e cada versão nova recomeça a reputação do zero.
