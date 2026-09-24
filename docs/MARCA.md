# A marca

O app se chama **PulseMeet** e o símbolo é o monograma M dentro do círculo.
**O nome foi fechado pelo dono em 24/09/2026**; o símbolo, não. Este documento
existe para que trocar qualquer um dos dois custe uma edição, e não uma tarde.

## Trocar o nome

Dois arquivos, e um teste que garante que são só dois:

| Arquivo | O que muda |
|---|---|
| [`app-net/Nucleo/Marca.cs`](../app-net/Nucleo/Marca.cs) | `Marca.Nome` — o app inteiro |
| [`instalador/MeetingApp.iss`](../instalador/MeetingApp.iss) | `#define Marca` — o instalador |

Se os dois discordarem, `MarcaTests.OInstaladorDizOMesmoNomeQueOApp` falha. Ele
existe porque o `.iss` é Inno Setup e não compila junto do C#: sem o teste, uma
troca pela metade só apareceria na tela de quem instalou.

De onde o nome sai, a partir do `Marca.Nome`:

- o **título da janela** e o que a barra de tarefas mostra (`App/Aplicacao.cs`);
- o **tooltip e os balões da bandeja** (`App/Bandeja/Bandeja.cs`);
- o **bloco de diagnóstico** que se cola num chat (`Nucleo/Diagnostico.cs`);
- a **linha de versão em Ajustes** — a página não tem o nome escrito: ele chega
  no campo `marca` do diagnóstico, junto da versão (`web/configuracoes.js`).

E, a partir do `#define Marca`: o `AppName`, o nome do grupo no menu Iniciar, os
dois atalhos, a tarefa "iniciar com o Windows", o botão do fim da instalação, a
mensagem da desinstalação — e, desde 24/09/2026, o nome do executável que ele
instala e o do próprio instalador.

## O executável e o instalador (desde 24/09/2026)

Com o nome fechado, os dois seguiram a marca: **`PulseMeet.exe`**, pelo
`AssemblyName` do `App/MeetingApp.App.csproj` (com `AssemblyTitle` e `Product`,
que são o que o Gerenciador de Tarefas e as propriedades do arquivo mostram), e
**`PulseMeet-<versão>-instalador.exe`**, pelo `OutputBaseFilename`. O
`MarcaTests` confere os três contra o `Marca.Nome`, e confere que o `.iss`, o
`publicar.sh` e o `montar_instalador.sh` usam o nome novo.

O que apontava para o `MeetingApp.exe` é repontado por
[`tools/repontar_atalhos.ps1`](../tools/repontar_atalhos.ps1): o menu Iniciar, a
área de trabalho, os fixados da barra de tarefas, o "iniciar com
o Windows" — levando junto a escolha do Gerenciador de Tarefas, para quem tinha
desligado o início automático — e o ícone de "Aplicativos instalados". O instalador o roda no fim (e apaga o
`MeetingApp.exe` e a pasta "MeetingApp" do menu Iniciar, de antes da 0.4.0); o
`publicar.sh` o roda na primeira publicação depois da troca, e só então apaga o
`.exe` velho. A prova dele roda numa pasta e numa chave de registro de mentira:
[`tools/provar_repontar_atalhos.ps1`](../tools/provar_repontar_atalhos.ps1).

**O que não se reponta**, e o que dizer a quem usa:

- **o ícone da bandeja vai para a área escondida (^).** O Windows 11 guarda
  "mostrar na bandeja" pelo caminho do `.exe` (`HKCU\Control Panel\NotifyIconSettings`),
  e um `.exe` novo nasce escondido. É o ícone que mostra o estado da gravação e o
  único jeito de sair do app: religá-lo é Configurações › Personalização ›
  Barra de tarefas › Outros ícones da bandeja › PulseMeet;
- **um fixado da barra de tarefas** pode guardar uma cópia do atalho que o
  Windows não relê: se não abrir, fixa-se de novo pelo menu Iniciar;
- **um fixado do Iniciar**, no Windows 10 e 11, não é um atalho em disco, e some
  quando a pasta do menu Iniciar troca de nome: fixa-se de novo.

**Publicar só de uma árvore que tem a troca.** Depois dela, um `publicar.sh`
antigo — de um ramo que não recebeu a main — copia um `MeetingApp.exe` que
nenhum atalho abre mais, e o menu Iniciar continua abrindo o `PulseMeet.exe`
de antes, calado. Ramo antigo recebe a main antes de publicar. **Desfazer a
troca** é o mesmo script ao contrário, e apagar o novo:
`repontar_atalhos.ps1 -Pasta <instalação> -Antigo PulseMeet.exe -Novo MeetingApp.exe -Aplicar`.
E, na máquina que se atualiza pelo `publicar.sh`, **desinstalar antes do próximo
instalador** deixa para trás o que o desinstalador antigo não conhece: o
`PulseMeet.exe`, o atalho `PulseMeet\PulseMeet.lnk` e o valor `PulseMeet` do
iniciar com o Windows, apontando para uma pasta sem o `WebView2Loader.dll`.

## O que não muda com a marca

Estes carregam o nome antigo e **continuam carregando**, de propósito. Nenhum
deles é visto por quem usa o app; todos quebram alguma coisa se mudarem.

| O quê | Se mudar |
|---|---|
| `AppId` do `.iss` | o Windows deixa de reconhecer a atualização: duas entradas em "Aplicativos Instalados" e duas pastas de 5 GB |
| `%LOCALAPPDATA%\Programs\MeetingApp` | a atualização do Inno reaproveita a pasta anterior de qualquer jeito; mudar a das instalações novas separaria as máquinas em dois caminhos, e moveria 18 GB de motores sem ninguém ver a diferença |
| `%LOCALAPPDATA%\MeetingApp\webview` (os dados do WebView2) | o que a página guarda some |
| `Global\MeetingApp` (mutex) | o instalador volta a copiar por cima de um app que pode estar gravando |
| namespaces, `RootNamespace`, `LogicalName` dos recursos | `Conteudo.cs` monta `"MeetingApp.web." + caminho` por texto — o app abre com a página em branco. O `RootNamespace` fica escrito no `.csproj`, porque o padrão dele é o `AssemblyName`, que mudou |
| os nomes dos arquivos de projeto e do `.iss` | nada visível; são caminhos que scripts e testes usam |
| `PackageIdentifier` do winget | vira um pacote novo em vez de uma atualização |

O `OutputBaseFilename` ficou no nome antigo até a 0.7.1 porque os manifestos
winget apontam para ele — mas cada manifesto aponta para o arquivo da **sua**
versão, que já está publicado e não muda. A versão nova sai com o nome novo, e o
manifesto novo aponta para ele.

**O winget é o único ponto com prazo.** Os manifestos em
[`instalador/winget/`](../instalador/winget/) ainda não foram submetidos ao
`microsoft/winget-pkgs`. Enquanto isso for verdade, o `PackageIdentifier` pode
ser trocado de graça — depois da submissão, não. Se o nome for para valer, ele
tem de estar decidido antes da primeira submissão. Ver
[ATUALIZACAO.md](ATUALIZACAO.md).

## Trocar o símbolo

A arte é [`assets/logo.svg`](../assets/logo.svg), num `viewBox` de 496×496 com
traço 23. Trocar o desenho é trocar esse arquivo e rodar:

```bash
uv run python tools/gerar_icone.py
```

Dele saem, de uma vez: o `logo-256.png`, o `logo.ico` do executável (uma
pastilha escura com o símbolo vazado, composta tamanho a tamanho) e os quatro
`bandeja-*.ico` — cinza, vermelho, laranja e amarelo, que são os estados da
gravação. O script rasteriza o SVG **em cada tamanho** em vez de reduzir um PNG
grande, porque a diferença aparece justamente nos 16 e 24 px.

O `MarcaTests.OSimboloEOQueGeraOsIcones` confere só o `viewBox`: um SVG com
outro quadro sai cortado nos 16 px da bandeja, e isso não aparece em tamanho
grande.

**Cuidado com o traço fino.** A 16 px, 23/496 de traço dá 0,74 pixel — o ícone
da bandeja fica claro em vez de nítido. Vale para o monograma M e valia igual
para o desenho anterior; se um dia incomodar, o conserto é uma arte própria para
a bandeja, mais gorda, e não uma mudança na marca.

Os desenhos recusados ficam em
[`assets/marca-alternativas/`](../assets/marca-alternativas/), inclusive o
monograma A que era a marca até a 0.3.0.
