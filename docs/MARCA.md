# A marca

O app se chama **PulseMeet** e o símbolo é o monograma M dentro do círculo.
Nenhum dos dois está fechado — este documento existe para que trocá-los custe
uma edição, e não uma tarde.

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
dois atalhos, a tarefa "iniciar com o Windows", o botão do fim da instalação e a
mensagem da desinstalação.

## O que não muda com a marca

Estes carregam o nome antigo e **continuam carregando**, de propósito. Nenhum
deles é visto por quem usa o app; todos quebram alguma coisa se mudarem.

| O quê | Se mudar |
|---|---|
| `AppId` do `.iss` | o Windows deixa de reconhecer a atualização: duas entradas em "Aplicativos Instalados" e duas pastas de 5 GB |
| `MeetingApp.exe` e `%LOCALAPPDATA%\Programs\MeetingApp` | o atalho de quem já instalou passa a apontar para o vazio |
| `Global\MeetingApp` (mutex) | o instalador volta a copiar por cima de um app que pode estar gravando |
| namespaces, `AssemblyName`, `LogicalName` dos recursos | `Conteudo.cs` monta `"MeetingApp.web." + caminho` por texto — o app abre com a página em branco |
| `PackageIdentifier` do winget | vira um pacote novo em vez de uma atualização |

O `OutputBaseFilename` (`MeetingApp-<versão>-instalador.exe`) fica junto do
`.exe` pelo mesmo motivo: é o nome do arquivo que os manifestos winget já
publicados apontam.

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
