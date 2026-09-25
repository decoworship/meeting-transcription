# A marca nos arquivos: PulseMeet.exe e o instalador PulseMeet — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** O executável passa a se chamar `PulseMeet.exe` e o instalador, `PulseMeet-<versão>-instalador.exe`; o `.exe` diz PulseMeet nas propriedades do arquivo e no Gerenciador de Tarefas; e tudo o que apontava para o `MeetingApp.exe` — atalhos do menu Iniciar e da área de trabalho, fixados, o "iniciar com o Windows" e o ícone de "Aplicativos instalados" — passa a apontar para o novo, na máquina que se atualiza pelo instalador e na que se atualiza pelo `publicar.sh`.

**Architecture:** O nome do `.exe` é o `AssemblyName` do app; o `RootNamespace` fica escrito no nome antigo, porque os recursos embutidos e o namespace padrão saem dele. Um script PowerShell, `tools/repontar_atalhos.ps1`, reponta o que aponta para o executável velho dentro da pasta de instalação — só lista sem `-Aplicar` —, e é o mesmo que o instalador roda no fim e que o `publicar.sh` roda na primeira publicação depois da troca. O `MarcaTests` passa a conferir o `.csproj`, o `.iss` e os dois scripts contra o `Marca.Nome`.

**Tech Stack:** C# / .NET 8 (xUnit), MSBuild, Inno Setup 6, PowerShell 5.1, bash.

**Spec:** [docs/superpowers/specs/2026-09-23-ui-ux.md](../specs/2026-09-23-ui-ux.md) — §5 linha 7, §6 **D-E** (decidida em 24/09/2026: "sobre a marca já está decidido o PulseMeet sim, pode mudar o executável também"), §7. E [docs/MARCA.md](../../MARCA.md), que diz o que não muda e por quê.

## Global Constraints

- **Não muda**: o `AppId` do `.iss`, o `AppMutex` (`Global\MeetingApp`), a pasta de instalação (`%LOCALAPPDATA%\Programs\MeetingApp`), a pasta de dados do WebView2 (`%LOCALAPPDATA%\MeetingApp\webview`), os namespaces, o `RootNamespace` (`MeetingApp`), os `LogicalName` dos recursos (`MeetingApp.web.*`, `MeetingApp.bandeja-*.ico`, `MeetingApp.atas.*`), os nomes das classes de janela, e os nomes dos arquivos de projeto e do `.iss`.
- `.ps1` com acento em UTF-8 **com BOM** (o PowerShell 5.1 lê sem BOM como ANSI) e CRLF.
- **Nunca rodar um instalador compilado na conferência** — ele tem o `AppId` de verdade e "atualizaria" a instalação do dono. Compilar para conferir, e apagar em seguida.
- Os documentos históricos (`docs/FASE*.md`, `docs/*-HANDOFF.md`) não mudam: são o registro do que aconteceu.
- Publicar só pelo `tools/publicar.sh`, com o app fechado pelo dono pela bandeja — nunca `dotnet publish` na mão, nunca matar um `MeetingApp.exe` ou `PulseMeet.exe` aberto.
- `export PATH="$HOME/.dotnet:$PATH"` antes de qualquer `dotnet`.
- Tudo roda no worktree `/home/andre/projects/mt-marca` (Task 1, Step 1); `git add` com os caminhos da tarefa.
- PowerShell chamado do WSL a partir de `cd /mnt/c`, e não de um diretório UNC.

## Review Focus

1. **Atalho de outro programa, ou um `MeetingApp.exe` de outra pasta** — não pode ser tocado. `provar_repontar_atalhos.ps1` (Task 1).
2. **Quem desligou o início automático no Gerenciador de Tarefas** — continua com ele desligado depois da troca de nome do valor. `provar_repontar_atalhos.ps1` (Task 1).
3. **Publicar de novo depois da troca** — não há mais `MeetingApp.exe`, e nada é repontado duas vezes. `provar_repontar_atalhos.ps1` ("rodar de novo") e o `if [[ -f MeetingApp.exe ]]` do `publicar.sh` (Task 3).
4. **A página abre com o assembly renomeado** — os recursos continuam `MeetingApp.web.*`. Conferido no `PulseMeet.dll` (Task 2, Step 5).
5. **O `.iss` compila** com as seções novas (`[InstallDelete]`, o PowerShell no `[Run]`, `UsePreviousGroup=no`). Compilado com motores de mentira e apagado (Task 3, Step 5).

---

## Mapa de arquivos

| arquivo | o quê |
|---|---|
| `tools/repontar_atalhos.ps1` | **novo** (T1) — reponta atalhos, fixados, "iniciar com o Windows" e o ícone de "Aplicativos instalados"; troca o nome do atalho e da pasta "MeetingApp" do menu Iniciar |
| `tools/provar_repontar_atalhos.ps1` | **novo** (T1) — a prova, numa pasta e numa chave de registro de mentira |
| `app-net/App/MeetingApp.App.csproj` | T2: `AssemblyName`, `AssemblyTitle`, `Product` = PulseMeet; `RootNamespace` = MeetingApp |
| `app-net/Tests/MarcaTests.cs` | T2: o `.csproj`. T3: o `.iss` e os scripts |
| `instalador/MeetingApp.iss` | T3: `{#Marca}.exe` em tudo; `OutputBaseFilename`; `[InstallDelete]`; `UsePreviousGroup=no`; o script no `[Run]` |
| `tools/publicar.sh` | T3: `PulseMeet.exe`; os dois nomes na trava de app aberto; a troca na primeira publicação |
| `tools/montar_instalador.sh` | T3: `PulseMeet.exe` no payload e nas réguas; o script no payload; o nome do instalador |
| `CLAUDE.md`, `docs/MARCA.md`, `docs/ATUALIZACAO.md`, `docs/INSTALAR.md`, `instalador/winget/LEIAME.md`, `app-net/Nucleo/Marca.cs`, `tools/ver_ui.ps1`, `tools/validar_bandeja.ps1`, o spec | T4 |

Os diffs abaixo foram tirados de um ensaio completo, com a suíte verde e o `.iss` compilado.

---

### Task 1: O script que reponta os atalhos

**Files:**
- Create: `tools/repontar_atalhos.ps1`, `tools/provar_repontar_atalhos.ps1`

**Interfaces:**
- Produces: `repontar_atalhos.ps1 -Pasta <pasta de instalação> [-Aplicar] [-Antigo MeetingApp.exe] [-Novo PulseMeet.exe]`, com os lugares como parâmetros para a prova (`-Programas`, `-AreaDeTrabalho`, `-Fixados`, `-ChaveRun`, `-ChaveAprovado`, `-ChaveDesinstalar`). Escreve uma linha por achado (`atalho:`, `nome:`, `inicio:`, `icone:`) e termina em `a repontar: N (…)` ou `repontados: N`.

- [ ] **Step 1: O worktree e o registro**

```bash
git -C /home/andre/projects/meeting-transcription worktree add /home/andre/projects/mt-marca -b marca-nos-arquivos main
```

- [ ] **Step 2: A prova** — `tools/provar_repontar_atalhos.ps1`, em UTF-8 com BOM e CRLF:

```powershell
# A prova do repontar_atalhos.ps1, numa pasta e numa chave de registro de
# mentira: ele mexe em atalhos e no "iniciar com o Windows" de verdade, e errar
# ali deixa o menu Iniciar abrindo o vazio. Cria tudo, aplica, confere, e apaga.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\provar_repontar_atalhos.ps1

$ErrorActionPreference = "Stop"
# Em UTF-8: chamado do WSL (publicar.sh), o console em ANSI chega lá como "�".
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$script = Join-Path $PSScriptRoot "repontar_atalhos.ps1"
$raiz = Join-Path $env:TEMP ("prova-repontar-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
$chave = "HKCU:\Software\PulseMeetProvaRepontar"
$falhas = 0
function Conferir($condicao, $oQue) {
    if ($condicao) { Write-Output "  ok     $oQue" } else { Write-Output "  FALHA  $oQue"; $script:falhas++ }
}

try {
    $pasta = Join-Path $raiz "instalado"
    $outra = Join-Path $raiz "outra"
    $programas = Join-Path $raiz "Programs"
    $area = Join-Path $raiz "Desktop"
    $fixados = Join-Path $raiz "TaskBar"
    foreach ($d in @($pasta, $outra, (Join-Path $programas "MeetingApp"), (Join-Path $programas "Outro"), $area, $fixados)) {
        New-Item -ItemType Directory -Force -Path $d | Out-Null
    }
    foreach ($f in @("$pasta\MeetingApp.exe", "$pasta\PulseMeet.exe", "$outra\MeetingApp.exe")) { New-Item -ItemType File -Path $f | Out-Null }

    $shell = New-Object -ComObject WScript.Shell
    function Atalho($caminho, $alvo) { $a = $shell.CreateShortcut($caminho); $a.TargetPath = $alvo; $a.Save() }
    function Alvo($caminho) { $shell.CreateShortcut($caminho).TargetPath }
    Atalho "$programas\MeetingApp\MeetingApp.lnk" "$pasta\MeetingApp.exe"
    Atalho "$programas\MeetingApp\PulseMeet.lnk" "$pasta\MeetingApp.exe"
    Atalho "$programas\Outro\Outro.lnk" "$outra\MeetingApp.exe"
    Atalho "$area\MeetingApp.lnk" "$pasta\MeetingApp.exe"
    Atalho "$fixados\MeetingApp.lnk" "$pasta\MeetingApp.exe"

    New-Item -Path "$chave\Run" -Force | Out-Null
    New-Item -Path "$chave\Desinstalar\{8B6F3A21}_is1" -Force | Out-Null
    New-Item -Path "$chave\Desinstalar\Outro" -Force | Out-Null
    Set-ItemProperty -Path "$chave\Desinstalar\{8B6F3A21}_is1" -Name DisplayIcon -Value "$pasta\MeetingApp.exe"
    Set-ItemProperty -Path "$chave\Desinstalar\Outro" -Name DisplayIcon -Value "$outra\MeetingApp.exe"
    New-Item -Path "$chave\Aprovado" -Force | Out-Null
    Set-ItemProperty -Path "$chave\Run" -Name "MeetingApp" -Value "`"$pasta\MeetingApp.exe`""
    Set-ItemProperty -Path "$chave\Run" -Name "Outro" -Value "`"$outra\MeetingApp.exe`""
    $desligado = [byte[]](3, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8)
    Set-ItemProperty -Path "$chave\Aprovado" -Name "MeetingApp" -Value $desligado -Type Binary

    $args = @{ Pasta = $pasta; Programas = $programas; AreaDeTrabalho = $area; Fixados = @($fixados);
               ChaveRun = "$chave\Run"; ChaveAprovado = "$chave\Aprovado"; ChaveDesinstalar = "$chave\Desinstalar" }

    $lista = & $script @args
    Conferir ((Alvo "$area\MeetingApp.lnk") -eq "$pasta\MeetingApp.exe") "sem -Aplicar, nada muda"
    # Quatro atalhos (dois no menu Iniciar, um na área de trabalho, um fixado),
    # o início automático e o ícone de "Aplicativos instalados".
    Conferir (($lista -join "`n") -match "a repontar: 6") "e ele diz o que mudaria ($(@($lista)[-1]))"

    $saida = & $script @args -Aplicar
    Conferir (Test-Path "$programas\PulseMeet\PulseMeet.lnk") "o menu Iniciar ganha a pasta PulseMeet com o atalho PulseMeet"
    Conferir ((Alvo "$programas\PulseMeet\PulseMeet.lnk") -eq "$pasta\PulseMeet.exe") "apontando para o PulseMeet.exe"
    Conferir (-not (Test-Path "$programas\MeetingApp")) "e a pasta MeetingApp do menu Iniciar some"
    Conferir ((Test-Path "$area\PulseMeet.lnk") -and -not (Test-Path "$area\MeetingApp.lnk")) "o atalho da área de trabalho troca de nome"
    Conferir ((Alvo "$area\PulseMeet.lnk") -eq "$pasta\PulseMeet.exe") "e de alvo"
    Conferir ((Alvo "$fixados\MeetingApp.lnk") -eq "$pasta\PulseMeet.exe") "o fixado troca de alvo e mantém o nome"
    Conferir ((Alvo "$programas\Outro\Outro.lnk") -eq "$outra\MeetingApp.exe") "o MeetingApp.exe de outra pasta fica onde está"
    $run = Get-ItemProperty -Path "$chave\Run"
    Conferir (($null -eq $run.MeetingApp) -and ($run.PulseMeet -eq "`"$pasta\PulseMeet.exe`"")) "o iniciar com o Windows troca de nome e de alvo"
    Conferir ($run.Outro -eq "`"$outra\MeetingApp.exe`"") "e o dos outros programas fica"
    $aprovado = Get-ItemProperty -Path "$chave\Aprovado"
    Conferir (($null -eq $aprovado.MeetingApp) -and ((@($aprovado.PulseMeet) -join ",") -eq ($desligado -join ","))) "quem desligou o início automático continua com ele desligado"

    $icone = (Get-ItemProperty -Path "$chave\Desinstalar\{8B6F3A21}_is1").DisplayIcon
    Conferir ($icone -eq "$pasta\PulseMeet.exe") "o ícone de Aplicativos instalados aponta para o PulseMeet.exe ($icone)"
    $outro = (Get-ItemProperty -Path "$chave\Desinstalar\Outro").DisplayIcon
    Conferir ($outro -eq "$outra\MeetingApp.exe") "e o dos outros programas fica"

    $denovo = & $script @args -Aplicar
    Conferir (@($denovo)[-1] -eq "repontados: 0") "rodar de novo não acha mais nada ($(@($denovo)[-1]))"
}
finally {
    Remove-Item -LiteralPath $raiz -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $chave -Recurse -Force -ErrorAction SilentlyContinue
}
if ($falhas -gt 0) { Write-Output "`n$falhas falha(s)."; exit 1 }
Write-Output "`ntudo certo."
```

- [ ] **Step 3: Rodar e ver falhar**

```bash
cd /mnt/c && /mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe -NoProfile -ExecutionPolicy Bypass \
  -File "$(wslpath -w /home/andre/projects/mt-marca/tools/provar_repontar_atalhos.ps1)"; echo "saída: $?"
```

Expected: estoura porque o `repontar_atalhos.ps1` não existe (o PowerShell não reconhece o caminho), com saída diferente de 0, e a chave `HKCU:\Software\PulseMeetProvaRepontar` apagada pelo `finally`.

- [ ] **Step 4: O script** — `tools/repontar_atalhos.ps1`, em UTF-8 com BOM e CRLF:

```powershell
# Reponta para o PulseMeet.exe o que ainda aponta para o MeetingApp.exe.
#
# O executável trocou de nome em 24/09/2026 (docs/MARCA.md). O instalador refaz
# os atalhos que ele mesmo cria, mas não os que a pessoa fez — o ícone fixado na
# barra de tarefas — e o publicar.sh, que é como a máquina do dono se atualiza,
# não cria atalho nenhum. Este script acha, em quatro lugares, tudo o que aponta
# para o executável velho DENTRO da pasta dada, e o reponta:
#
#   o menu Iniciar do usuário, a área de trabalho, os fixados da barra de
#   tarefas e do Iniciar, e a chave Run do "iniciar com o Windows" — levando
#   junto a escolha do Gerenciador de Tarefas (StartupApproved), para que quem
#   desligou o início automático continue com ele desligado.
#
# E o ícone de "Aplicativos instalados", que o instalador escreveu apontando
# para o .exe: sem ele, o app aparece na lista sem ícone depois que o velho sai.
#
# E troca o NOME do que ainda se chama MeetingApp no menu Iniciar e na área de
# trabalho: o atalho MeetingApp.lnk, e a pasta "MeetingApp" do menu Iniciar,
# que o Inno reaproveita de uma instalação de antes da marca. Os fixados não
# trocam de nome — o Windows os acha pelo nome do arquivo.
#
# Sem -Aplicar, só lista: é o modo de ver o que vai mudar antes de mudar.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File repontar_atalhos.ps1 -Pasta C:\...\MeetingApp [-Aplicar]
#
# Os lugares são parâmetros só para a prova (tools/provar_repontar_atalhos.ps1),
# que roda numa pasta e numa chave de registro de mentira.

param(
    [Parameter(Mandatory = $true)][string]$Pasta,
    [switch]$Aplicar,
    [string]$Antigo = "MeetingApp.exe",
    [string]$Novo = "PulseMeet.exe",
    [string]$Programas = [Environment]::GetFolderPath("Programs"),
    [string]$AreaDeTrabalho = [Environment]::GetFolderPath("Desktop"),
    [string[]]$Fixados = @(
        (Join-Path $env:APPDATA "Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"),
        (Join-Path $env:APPDATA "Microsoft\Internet Explorer\Quick Launch\User Pinned\StartMenu")),
    [string]$ChaveRun = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run",
    [string]$ChaveAprovado = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
    [string]$ChaveDesinstalar = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall"
)

$ErrorActionPreference = "Stop"
# Em UTF-8: chamado do WSL (publicar.sh), o console em ANSI chega lá como "�".
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$velho = Join-Path $Pasta $Antigo
$novo = Join-Path $Pasta $Novo
$nomeNovo = [IO.Path]::GetFileNameWithoutExtension($Novo)
$nomeVelho = [IO.Path]::GetFileNameWithoutExtension($Antigo)
$mudou = 0

# ---- os atalhos
$shell = New-Object -ComObject WScript.Shell
$programas = $Programas
$areaDeTrabalho = $AreaDeTrabalho
$repontados = @()
$lugares = @($Programas, $AreaDeTrabalho) + $Fixados
foreach ($lugar in $lugares) {
    if (-not (Test-Path -LiteralPath $lugar)) { continue }
    foreach ($lnk in Get-ChildItem -LiteralPath $lugar -Filter *.lnk -Recurse -ErrorAction SilentlyContinue) {
        $atalho = $shell.CreateShortcut($lnk.FullName)
        if ($atalho.TargetPath -ne $velho) { continue }
        Write-Output "atalho: $($lnk.FullName)"
        $mudou++
        $repontados += $lnk.FullName
        if (-not $Aplicar) { continue }
        $atalho.TargetPath = $novo
        # O ícone vem do próprio .exe; apontando para o velho, o atalho ficaria
        # com o ícone em branco depois que ele for apagado.
        if ($atalho.IconLocation -like "$velho*") { $atalho.IconLocation = "$novo,0" }
        $atalho.Save()
    }
}

# ---- os nomes, no menu Iniciar e na área de trabalho
foreach ($lnk in $repontados) {
    $dir = Split-Path -Parent $lnk
    if (-not ($dir.StartsWith($programas) -or $dir -eq $areaDeTrabalho)) { continue }
    $base = [IO.Path]::GetFileNameWithoutExtension($lnk)
    $dirNovo = if ((Split-Path -Leaf $dir) -eq $nomeVelho -and $dir.StartsWith($programas)) {
        Join-Path (Split-Path -Parent $dir) $nomeNovo } else { $dir }
    $alvo = Join-Path $dirNovo ("{0}.lnk" -f $(if ($base -eq $nomeVelho) { $nomeNovo } else { $base }))
    if ($alvo -eq $lnk) { continue }
    Write-Output "nome: $lnk -> $alvo"
    if (-not $Aplicar) { continue }
    New-Item -ItemType Directory -Force -Path $dirNovo | Out-Null
    # Dois atalhos que viram o mesmo (MeetingApp.lnk e PulseMeet.lnk na pasta
    # velha): o segundo sai, e fica um só.
    if (Test-Path -LiteralPath $alvo) { Remove-Item -LiteralPath $lnk } else { Move-Item -LiteralPath $lnk -Destination $alvo }
}
$pastaVelha = Join-Path $programas $nomeVelho
if ($Aplicar -and (Test-Path -LiteralPath $pastaVelha) -and -not (Get-ChildItem -LiteralPath $pastaVelha -Force)) {
    Remove-Item -LiteralPath $pastaVelha
}

# ---- o "iniciar com o Windows"
$run = $ChaveRun
$aprovado = $ChaveAprovado
if (Test-Path $run) {
    $valores = Get-ItemProperty -Path $run
    foreach ($p in $valores.PSObject.Properties) {
        if ($p.Name -like "PS*") { continue }
        if ("$($p.Value)" -notlike "*$velho*") { continue }
        Write-Output "inicio: $($p.Name) = $($p.Value)"
        $mudou++
        if (-not $Aplicar) { continue }
        Remove-ItemProperty -Path $run -Name $p.Name
        Set-ItemProperty -Path $run -Name $nomeNovo -Value "`"$novo`""
        # A escolha do Gerenciador de Tarefas mora noutra chave, pelo NOME do
        # valor: sem levá-la junto, quem desligou o início automático o teria
        # de volta ligado.
        if (Test-Path $aprovado) {
            $estado = (Get-ItemProperty -Path $aprovado -Name $p.Name -ErrorAction SilentlyContinue).$($p.Name)
            if ($null -ne $estado) {
                Set-ItemProperty -Path $aprovado -Name $nomeNovo -Value ([byte[]]$estado) -Type Binary
                Remove-ItemProperty -Path $aprovado -Name $p.Name
            }
        }
    }
}

# ---- o ícone de "Aplicativos instalados"
if (Test-Path $ChaveDesinstalar) {
    foreach ($item in Get-ChildItem -Path $ChaveDesinstalar) {
        $icone = (Get-ItemProperty -Path $item.PSPath -Name DisplayIcon -ErrorAction SilentlyContinue).DisplayIcon
        if (-not $icone -or "$icone" -notlike "*$velho*") { continue }
        Write-Output "icone: $($item.PSChildName) = $icone"
        $mudou++
        if ($Aplicar) { Set-ItemProperty -Path $item.PSPath -Name DisplayIcon -Value ("$icone".Replace($velho, $novo)) }
    }
}

if ($Aplicar) { Write-Output "repontados: $mudou" } else { Write-Output "a repontar: $mudou (nada mudou; rode com -Aplicar)" }
```

- [ ] **Step 5: Rodar e ver passar**

O mesmo comando do Step 3. Expected: quinze `ok`, `tudo certo.`, saída 0.

Depois, **só listando**, na instalação do dono — não muda nada:

```bash
cd /mnt/c && /mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe -NoProfile -ExecutionPolicy Bypass \
  -File "$(wslpath -w /home/andre/projects/mt-marca/tools/repontar_atalhos.ps1)" \
  -Pasta 'C:\Users\andre\AppData\Local\Programs\MeetingApp'
```

Expected (medido em 24/09/2026): os dois atalhos da pasta "MeetingApp" do menu Iniciar, as duas linhas `nome:` que os juntam em `PulseMeet\PulseMeet.lnk`, `inicio: MeetingApp = …`, `icone: {8B6F3A21-…}_is1 = …`, e `a repontar: 4`.

- [ ] **Step 6: Commit**

```bash
cd /home/andre/projects/mt-marca
git add tools/repontar_atalhos.ps1 tools/provar_repontar_atalhos.ps1
git commit -m "feat(marca): o script que reponta os atalhos para o PulseMeet.exe"
```

---

### Task 2: O executável se chama PulseMeet.exe

**Files:**
- Modify: `app-net/App/MeetingApp.App.csproj`
- Test: `app-net/Tests/MarcaTests.cs`

**Interfaces:**
- Produces: `PulseMeet.exe` (e `PulseMeet.dll` no build sem single-file), com `FileDescription` e `ProductName` = PulseMeet; `MarcaTests.Propriedade(csproj, nome)` para a Task 3.

- [ ] **Step 1: O teste**

A parte do `MarcaTests.cs` deste diff (o `OExecutavelTemONomeDaMarca` e o `Propriedade`):

```diff
diff --git a/app-net/App/MeetingApp.App.csproj b/app-net/App/MeetingApp.App.csproj
--- a/app-net/App/MeetingApp.App.csproj
+++ b/app-net/App/MeetingApp.App.csproj
@@ -2,7 +2,18 @@
 
   <PropertyGroup>
     <OutputType>WinExe</OutputType>
-    <AssemblyName>MeetingApp</AssemblyName>
+    <!-- O executável se chama PulseMeet.exe desde 24/09/2026 (docs/MARCA.md), e
+         o título e o produto são o que o Gerenciador de Tarefas e as
+         propriedades do arquivo mostram. O MarcaTests confere os três contra o
+         Marca.Nome. -->
+    <AssemblyName>PulseMeet</AssemblyName>
+    <AssemblyTitle>PulseMeet</AssemblyTitle>
+    <Product>PulseMeet</Product>
+    <!-- O namespace fica no nome antigo, e escrito: o padrão do RootNamespace é
+         o AssemblyName, e com ele mudariam o namespace padrão e o nome de
+         qualquer recurso sem LogicalName. Os LogicalName daqui ("MeetingApp.web.",
+         que o Conteudo.cs monta por texto) também não mudam. -->
+    <RootNamespace>MeetingApp</RootNamespace>
     <TargetFramework>net8.0-windows</TargetFramework>
     <EnableWindowsTargeting>true</EnableWindowsTargeting>
     <RuntimeIdentifier>win-x64</RuntimeIdentifier>
diff --git a/app-net/Tests/MarcaTests.cs b/app-net/Tests/MarcaTests.cs
--- a/app-net/Tests/MarcaTests.cs
+++ b/app-net/Tests/MarcaTests.cs
@@ -36,6 +36,31 @@ public sealed class MarcaTests
         Assert.Equal(Marca.Nome, m.Groups[1].Value);
     }
 
+    [Fact]
+    public void OExecutavelTemONomeDaMarca()
+    {
+        // O nome do .exe é o AssemblyName: desde 24/09/2026 ele é PulseMeet.exe
+        // (docs/MARCA.md). O título e o produto são o que o Gerenciador de
+        // Tarefas e as propriedades do arquivo mostram.
+        string? csproj = Achar(Path.Combine("app-net", "App", "MeetingApp.App.csproj"));
+        if (csproj is null) return;
+        string texto = File.ReadAllText(csproj);
+
+        Assert.Equal(Marca.Nome, Propriedade(texto, "AssemblyName"));
+        Assert.Equal(Marca.Nome, Propriedade(texto, "AssemblyTitle"));
+        Assert.Equal(Marca.Nome, Propriedade(texto, "Product"));
+        // O RootNamespace fica no nome antigo, e escrito: sem ele, o padrão é o
+        // AssemblyName, e o namespace padrão e os recursos sem LogicalName
+        // mudariam de nome junto com o .exe.
+        Assert.Equal("MeetingApp", Propriedade(texto, "RootNamespace"));
+    }
+
+    private static string? Propriedade(string csproj, string nome)
+    {
+        var m = Regex.Match(csproj, $@"<{nome}>([^<]*)</{nome}>");
+        return m.Success ? m.Groups[1].Value : null;
+    }
+
     [Fact]
     public void OSimboloEOQueGeraOsIcones()
     {
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~MarcaTests" 2>&1 | grep -E "Failed |Expected|Actual|Failed!"`

Expected: `Failed MeetingApp.Tests.MarcaTests.OExecutavelTemONomeDaMarca`, `Expected: "PulseMeet"`, `Actual: "MeetingApp"`.

- [ ] **Step 3: O `.csproj`** — a parte do `MeetingApp.App.csproj` do mesmo diff.

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj 2>&1 | tail -1`
Expected: 664 aprovados, 0 com falha.

- [ ] **Step 5: Os recursos sobreviveram ao nome novo**

```bash
D=app-net/App/bin/Release/net8.0-windows/win-x64
dotnet build app-net/App/MeetingApp.App.csproj -c Release -v q --nologo
for r in MeetingApp.web.index.html MeetingApp.web.app.js MeetingApp.web.ds.tokens.tokens.css MeetingApp.bandeja-vermelho.ico; do
  printf "%-40s %s\n" $r "$(strings $D/PulseMeet.dll | grep -c "^$r$" || true)"; done
strings $D/PulseMeet.dll | grep -c "^PulseMeet.web" || true
```

Expected: `1` para cada um dos quatro, e `0` para `PulseMeet.web` — senão a página abre em branco.

- [ ] **Step 6: Commit**

```bash
git add app-net/App/MeetingApp.App.csproj app-net/Tests/MarcaTests.cs
git commit -m "feat(marca): o executável se chama PulseMeet.exe"
```

---

### Task 3: O instalador e os scripts

**Files:**
- Modify: `instalador/MeetingApp.iss`, `tools/publicar.sh`, `tools/montar_instalador.sh`
- Test: `app-net/Tests/MarcaTests.cs`

**Interfaces:**
- Consumes: de Task 1, `repontar_atalhos.ps1 -Pasta … -Aplicar`; de Task 2, `PulseMeet.exe` e `Propriedade`.
- Produces: `PulseMeet-<versão>-instalador.exe`; o `publicar.sh` que troca o `.exe` na máquina do dono.

- [ ] **Step 1: Os testes** — a parte do `MarcaTests.cs` deste diff (`OInstaladorInstalaOExecutavelDaMarca`, `OsScriptsPublicamOExecutavelDaMarca`):

```diff
diff --git a/app-net/Tests/MarcaTests.cs b/app-net/Tests/MarcaTests.cs
--- a/app-net/Tests/MarcaTests.cs
+++ b/app-net/Tests/MarcaTests.cs
@@ -55,6 +55,44 @@ public sealed class MarcaTests
         Assert.Equal("MeetingApp", Propriedade(texto, "RootNamespace"));
     }
 
+    [Fact]
+    public void OInstaladorInstalaOExecutavelDaMarca()
+    {
+        string? iss = Achar(Path.Combine("instalador", "MeetingApp.iss"));
+        if (iss is null) return;
+        string texto = File.ReadAllText(iss);
+
+        // O que instala, atalha, inicia e desinstala o .exe usa o nome da marca.
+        Assert.Contains(@"Source: ""{#Payload}\{#Marca}.exe""", texto);
+        Assert.Contains(@"Name: ""{group}\{#Marca}""; Filename: ""{app}\{#Marca}.exe""", texto);
+        Assert.Contains(@"ValueName: ""{#Marca}""; ValueData: """"""{app}\{#Marca}.exe""""""", texto);
+        Assert.Contains(@"UninstallDisplayIcon={app}\{#Marca}.exe", texto);
+        Assert.Contains("OutputBaseFilename={#Marca}-{#Versao}-instalador", texto);
+        // O grupo do menu Iniciar de antes da marca não é reaproveitado.
+        Assert.Contains("UsePreviousGroup=no", texto);
+        // E os atalhos que a pessoa fez são repontados pelo mesmo script do publicar.sh.
+        Assert.Contains(@"-File """"{app}\repontar_atalhos.ps1"""" -Pasta """"{app}"""" -Aplicar", texto);
+
+        // O .exe velho só aparece para ser apagado.
+        var velhas = texto.Split('\n')
+            .Where(l => l.Contains("MeetingApp.exe") && !l.TrimStart().StartsWith(';'))
+            .Select(l => l.Trim()).ToList();
+        Assert.Equal(new[] { @"Type: files; Name: ""{app}\MeetingApp.exe""" }, velhas);
+    }
+
+    [Fact]
+    public void OsScriptsPublicamOExecutavelDaMarca()
+    {
+        // O publicar.sh (a máquina do dono) e o montar_instalador.sh (o
+        // instalador) copiam o .exe pelo nome, por texto.
+        foreach (string script in new[] { "publicar.sh", "montar_instalador.sh" })
+        {
+            string? caminho = Achar(Path.Combine("tools", script));
+            if (caminho is null) continue;
+            Assert.Contains($"{Marca.Nome}.exe", File.ReadAllText(caminho));
+        }
+    }
+
     private static string? Propriedade(string csproj, string nome)
     {
         var m = Regex.Match(csproj, $@"<{nome}>([^<]*)</{nome}>");
diff --git a/instalador/MeetingApp.iss b/instalador/MeetingApp.iss
--- a/instalador/MeetingApp.iss
+++ b/instalador/MeetingApp.iss
@@ -46,10 +46,14 @@
 ; compara os dois e falha se discordarem, porque uma versão em que o instalador
 ; diz um nome e a bandeja diz outro passa despercebida até chegar no usuário.
 ;
-; O que NÃO segue a marca, por baixo: o AppId, o DefaultDirName, o AppMutex, o
-; nome do .exe e o OutputBaseFilename. Trocar qualquer um deles transforma a
-; atualização num segundo programa instalado, ou deixa o atalho de quem já tem
-; o app apontando para o vazio.
+; O que NÃO segue a marca, por baixo: o AppId, o DefaultDirName e o AppMutex.
+; Trocar qualquer um deles transforma a atualização num segundo programa
+; instalado, ou deixa o instalador copiar por cima de um app gravando.
+;
+; O nome do .exe e o do instalador seguem desde 24/09/2026 (docs/MARCA.md): o
+; MeetingApp.exe é apagado ([InstallDelete]), e o que apontava para ele — os
+; atalhos, os fixados, o iniciar com o Windows — é refeito para o PulseMeet.exe
+; ([Icons], [Registry] e o repontar_atalhos.ps1 no [Run]).
 #define Marca "PulseMeet"
 
 [Setup]
@@ -73,6 +77,10 @@ VersionInfoVersion={#VersaoNumerica}
 AppPublisher=decoworship
 DefaultDirName={localappdata}\Programs\MeetingApp
 DefaultGroupName={#Marca}
+; Sem reaproveitar o grupo de uma instalação anterior: o de antes da 0.4.0 se
+; chama "MeetingApp", e o Inno o manteria para sempre. O velho é apagado no
+; [InstallDelete].
+UsePreviousGroup=no
 DisableProgramGroupPage=yes
 DisableDirPage=no
 ; Sem UAC: instalação por usuário. Ver o motivo 1 no cabeçalho.
@@ -87,9 +95,9 @@ ArchitecturesInstallIn64BitMode=x64compatible
 ; aberto vira um pedido educado para fechar, em vez de um erro de cópia no meio.
 AppMutex=Global\MeetingApp
 OutputDir={#Saida}
-OutputBaseFilename=MeetingApp-{#Versao}-instalador
+OutputBaseFilename={#Marca}-{#Versao}-instalador
 SetupIconFile={#Payload}\logo.ico
-UninstallDisplayIcon={app}\MeetingApp.exe
+UninstallDisplayIcon={app}\{#Marca}.exe
 ; lzma2/max e solid: o payload é dominado por DLLs de CUDA, que comprimem bem, e
 ; o instalador é entregue por link — cada 100 MB conta mais que o minuto a mais
 ; de compressão.
@@ -111,8 +119,19 @@ Name: "atalhonaarea"; Description: "Criar atalho na área de trabalho"; \
 Name: "iniciarcomwindows"; Description: "Iniciar o {#Marca} junto com o Windows"; \
   GroupDescription: "Ao ligar o computador:"
 
+[InstallDelete]
+; O executável se chamava MeetingApp.exe até a 0.7.x, e o grupo do menu Iniciar
+; "MeetingApp" até a 0.3.0 (docs/MARCA.md). Sem isto, os dois ficariam para trás:
+; um .exe velho que abre a versão velha, e uma pasta no menu Iniciar com um
+; atalho para ele.
+Type: files; Name: "{app}\MeetingApp.exe"
+Type: filesandordirs; Name: "{userprograms}\MeetingApp"
+
 [Files]
-Source: "{#Payload}\MeetingApp.exe"; DestDir: "{app}"; Flags: ignoreversion
+Source: "{#Payload}\{#Marca}.exe"; DestDir: "{app}"; Flags: ignoreversion
+; O que reponta os atalhos que a pessoa fez para o .exe de antes da marca; roda
+; no [Run], e é o mesmo que o tools/publicar.sh usa.
+Source: "{#Payload}\repontar_atalhos.ps1"; DestDir: "{app}"; Flags: ignoreversion
 ; O carregador nativo do WebView2 não entra no single-file: ele é carregado por
 ; nome, do disco, antes de o host gerenciado existir.
 Source: "{#Payload}\WebView2Loader.dll"; DestDir: "{app}"; Flags: ignoreversion
@@ -166,22 +185,33 @@ Source: "{#Payload}\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; \
   Flags: deleteafterinstall; Check: not TemWebView2
 
 [Icons]
-Name: "{group}\{#Marca}"; Filename: "{app}\MeetingApp.exe"
-Name: "{userdesktop}\{#Marca}"; Filename: "{app}\MeetingApp.exe"; \
+Name: "{group}\{#Marca}"; Filename: "{app}\{#Marca}.exe"
+Name: "{userdesktop}\{#Marca}"; Filename: "{app}\{#Marca}.exe"; \
   Tasks: atalhonaarea
 
 [Registry]
 ; Inicialização por usuário, na chave Run do próprio usuário — não há serviço,
 ; não há tarefa agendada, e desinstalar leva a chave junto.
 Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
-  ValueType: string; ValueName: "MeetingApp"; ValueData: """{app}\MeetingApp.exe"""; \
+  ValueType: string; ValueName: "{#Marca}"; ValueData: """{app}\{#Marca}.exe"""; \
   Flags: uninsdeletevalue; Tasks: iniciarcomwindows
+; O valor de quando o .exe era MeetingApp.exe. O repontar_atalhos.ps1 o troca
+; pelo novo ao instalar, levando junto a escolha do Gerenciador de Tarefas; aqui
+; ele só sai junto na desinstalação, se tiver sobrado.
+Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
+  ValueType: none; ValueName: "MeetingApp"; Flags: uninsdeletevalue
 
 [Run]
 Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; \
   StatusMsg: "Instalando o componente WebView2 do Windows…"; \
   Check: not TemWebView2; Flags: waituntilterminated
-Filename: "{app}\MeetingApp.exe"; Description: "Abrir o {#Marca}"; \
+; O fixado na barra de tarefas e o iniciar com o Windows ainda apontam para o
+; MeetingApp.exe que o [InstallDelete] apagou. Idempotente: sem nada velho, não
+; acha nada.
+Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
+  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\repontar_atalhos.ps1"" -Pasta ""{app}"" -Aplicar"; \
+  StatusMsg: "Atualizando os atalhos…"; Flags: runhidden waituntilterminated
+Filename: "{app}\{#Marca}.exe"; Description: "Abrir o {#Marca}"; \
   Flags: nowait postinstall skipifsilent
 
 [UninstallDelete]
diff --git a/tools/montar_instalador.sh b/tools/montar_instalador.sh
--- a/tools/montar_instalador.sh
+++ b/tools/montar_instalador.sh
@@ -1,5 +1,5 @@
 #!/usr/bin/env bash
-# Monta o instalador do MeetingApp — o artefato que se entrega a outra pessoa.
+# Monta o instalador do PulseMeet — o artefato que se entrega a outra pessoa.
 #
 # Irmão do publicar.sh, e com a mesma filosofia: **réguas objetivas antes de
 # produzir o artefato**, porque cada uma delas corresponde a um defeito que já
@@ -58,20 +58,23 @@ export PATH="$HOME/.dotnet:$PATH"
 VERSAO=$(grep -oP '(?<=<Version>)[^<]+' "$RAIZ/app-net/Directory.Build.props")
 [[ -n "$VERSAO" ]] || { echo "ERRO: não achei <Version> em Directory.Build.props" >&2; exit 1; }
 
-echo "==> MeetingApp $VERSAO"
+echo "==> PulseMeet $VERSAO"
 
 if (( ! PULAR_BUILD )); then
   "$RAIZ/tools/publicar.sh" --so-build
 else
   echo "==> pulando o build, a pedido (--pular-build)"
-  [[ -f "$PUBLICADO/MeetingApp.exe" ]] || {
-    echo "ERRO: --pular-build, mas não há $PUBLICADO/MeetingApp.exe" >&2; exit 1; }
+  [[ -f "$PUBLICADO/PulseMeet.exe" ]] || {
+    echo "ERRO: --pular-build, mas não há $PUBLICADO/PulseMeet.exe" >&2; exit 1; }
 fi
 
 echo "==> montando o payload em $PAYLOAD"
 rm -rf "$PAYLOAD"
 mkdir -p "$PAYLOAD"
-cp "$PUBLICADO/MeetingApp.exe" "$PUBLICADO/WebView2Loader.dll" "$PAYLOAD/"
+cp "$PUBLICADO/PulseMeet.exe" "$PUBLICADO/WebView2Loader.dll" "$PAYLOAD/"
+# O que reponta os atalhos de quem tinha o MeetingApp.exe (docs/MARCA.md); o .iss
+# o instala e o roda no fim.
+cp "$RAIZ/tools/repontar_atalhos.ps1" "$PAYLOAD/"
 cp "$RAIZ/docs/INSTALAR.md" "$RAIZ/CHANGELOG.md" "$PAYLOAD/"
 cp "$RAIZ/assets/logo.ico" "$PAYLOAD/"
 
@@ -92,12 +95,12 @@ reprovar() { echo "ERRO: $1" >&2; exit 1; }
 # O publicar.sh já conferiu tamanho, ícones e ausência de token. Estas são as do
 # INSTALADOR, e a plateia é outra: aqui o defeito viaja.
 
-bytes=$(stat -c%s "$PAYLOAD/MeetingApp.exe")
-(( bytes > 10000000 )) || reprovar "MeetingApp.exe tem $bytes bytes — as flags não pegaram."
+bytes=$(stat -c%s "$PAYLOAD/PulseMeet.exe")
+(( bytes > 10000000 )) || reprovar "PulseMeet.exe tem $bytes bytes — as flags não pegaram."
 
 # Nada secreto no artefato entregue. O do HuggingFace saiu na Fase 4; o do Google
 # fica, por decisão registrada em docs/FASE4.md §4 — e a régua é sobre o outro.
-tokens=$(strings "$PAYLOAD/MeetingApp.exe" | grep -c "hf_[A-Za-z0-9]\{20,\}" || true)
+tokens=$(strings "$PAYLOAD/PulseMeet.exe" | grep -c "hf_[A-Za-z0-9]\{20,\}" || true)
 (( tokens == 0 )) || reprovar "achei $tokens token(s) do HuggingFace no binário."
 
 # A versão do binário tem que ser a mesma do instalador. Sem isto, "Aplicativos
@@ -109,7 +112,7 @@ tokens=$(strings "$PAYLOAD/MeetingApp.exe" | grep -c "hf_[A-Za-z0-9]\{20,\}" ||
 # "\x2e0.1.0+<sha>" e um "^" nunca casaria. E `grep -c ... || true` em vez de
 # `grep -q`, senão o pipefail transforma o SIGPIPE do strings em reprovação do
 # binário correto — o mesmo tropeço que o publicar.sh documenta.
-versoes=$(strings "$PAYLOAD/MeetingApp.exe" | grep -cF "$VERSAO+" || true)
+versoes=$(strings "$PAYLOAD/PulseMeet.exe" | grep -cF "$VERSAO+" || true)
 (( versoes > 0 )) || reprovar "o binário não carrega a versão $VERSAO — rode sem --pular-build."
 
 # ── os motores ───────────────────────────────────────────────────────────────
@@ -240,7 +243,7 @@ lote="$SAIDA/compilar.cmd"
 
 (cd /mnt/c && /mnt/c/Windows/System32/cmd.exe /c "$(wslpath -w "$lote")") | tail -20
 
-FINAL="$SAIDA/MeetingApp-$VERSAO-instalador.exe"
+FINAL="$SAIDA/PulseMeet-$VERSAO-instalador.exe"
 [[ -f "$FINAL" ]] || reprovar "o ISCC terminou mas não produziu $FINAL"
 
 # A última régua, e é sobre o artefato inteiro: um instalador pequeno demais não
diff --git a/tools/publicar.sh b/tools/publicar.sh
--- a/tools/publicar.sh
+++ b/tools/publicar.sh
@@ -94,10 +94,10 @@ export PATH="$HOME/.dotnet:$PATH"
 # O app aberto é intocável. Já custou uma transcrição do usuário no meio, e
 # copiar por cima de um .exe em execução falha de qualquer forma no Windows.
 #
-# Confere pelo CAMINHO e não pelo nome do processo: desde a Fase 2.5 o app antigo
-# e o novo se chamam MeetingApp.exe, e barrar pelo nome impediria de publicar na
-# pasta de teste enquanto o usuário trabalha no app de produção — que é
-# exatamente o arranjo que esta fase pede.
+# Confere pelo CAMINHO e não pelo nome do processo: barrar pelo nome impediria
+# de publicar numa pasta de teste enquanto o usuário trabalha no app de
+# produção. Os DOIS nomes: o .exe se chama PulseMeet.exe desde 24/09/2026
+# (docs/MARCA.md), e o que está aberto na hora da troca ainda é o MeetingApp.exe.
 #
 # Com --so-build nada é copiado para lugar nenhum, então não há o que proteger:
 # barrar ali obrigaria a fechar o app para só montar o binário — que é
@@ -105,10 +105,10 @@ export PATH="$HOME/.dotnet:$PATH"
 # enquanto o app grava a reunião do dia.
 if (( ! SO_BUILD )) && [[ -n "${DESTINO:-}" ]]; then
   aberto=$(/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe -NoProfile -Command \
-    "(Get-Process MeetingApp -ErrorAction SilentlyContinue).Path" 2>/dev/null | tr -d '\r')
+    "(Get-Process PulseMeet,MeetingApp -ErrorAction SilentlyContinue).Path" 2>/dev/null | tr -d '\r')
   alvo=$(wslpath -w "$DESTINO" 2>/dev/null || echo "$DESTINO")
   if grep -qiF "$alvo" <<<"$aberto"; then
-    echo "ERRO: o MeetingApp está aberto e é ele que vai ser substituído." >&2
+    echo "ERRO: o PulseMeet está aberto e é ele que vai ser substituído." >&2
     echo "" >&2
     echo "      FECHE O APP e rode de novo — pelo menu da bandeja, em Sair." >&2
     echo "      Fechar a janela não basta: ela apenas esconde o app." >&2
@@ -149,7 +149,7 @@ dotnet publish "$RAIZ/app-net/App/MeetingApp.App.csproj" \
   -p:SegredoDoGoogle="$SEGREDO" \
   -o "$SAIDA" --nologo -v q
 
-EXE="$SAIDA/MeetingApp.exe"
+EXE="$SAIDA/PulseMeet.exe"
 
 echo "==> conferindo as réguas"
 bytes=$(stat -c%s "$EXE")
@@ -201,11 +201,24 @@ fi
 
 echo "==> instalando em $DESTINO"
 mkdir -p "$DESTINO"
-cp "$SAIDA/MeetingApp.exe" "$DESTINO/"
+cp "$SAIDA/PulseMeet.exe" "$DESTINO/"
 # O carregador nativo do WebView2 não entra no single-file: ele é carregado por
 # nome, do disco, antes de o host gerenciado existir.
 cp "$SAIDA/WebView2Loader.dll" "$DESTINO/"
-ls -la "$DESTINO/MeetingApp.exe"
+ls -la "$DESTINO/PulseMeet.exe"
+
+# A troca de nome do .exe, na máquina que se atualiza por aqui e não pelo
+# instalador. O menu Iniciar, a área de trabalho, os fixados e o iniciar com o
+# Windows apontam para o MeetingApp.exe; o repontar_atalhos.ps1 os leva para o
+# PulseMeet.exe, e só então o velho sai — apagado antes, o menu Iniciar abriria
+# o vazio. Uma vez só: depois da troca não há MeetingApp.exe para achar.
+if [[ -f "$DESTINO/MeetingApp.exe" ]]; then
+  echo "==> o executável agora é PulseMeet.exe: repontando os atalhos"
+  (cd /mnt/c && /mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe -NoProfile \
+     -ExecutionPolicy Bypass -File "$(wslpath -w "$RAIZ/tools/repontar_atalhos.ps1")" \
+     -Pasta "$(wslpath -w "$DESTINO")" -Aplicar | tr -d '\r' | sed 's/^/    /')
+  rm -f "$DESTINO/MeetingApp.exe"
+fi
 
 # O que é pesado NÃO é copiado: o destino ganha junções do Windows para a
 # instalação oficial. São 4,3 GB de Python embarcado, 3,5 GB de motor de ata e
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~MarcaTests" 2>&1 | grep -E "^\s+Failed |Failed!"`
Expected: os dois novos falhando, `Failed: 2, Passed: 3`.

- [ ] **Step 3: O `.iss` e os scripts** — as partes do `MeetingApp.iss`, do `publicar.sh` e do `montar_instalador.sh` do mesmo diff.

- [ ] **Step 4: Rodar e ver passar**

```bash
dotnet test app-net/Tests/MeetingApp.Tests.csproj 2>&1 | tail -1          # 666, 0 com falha
bash -n tools/publicar.sh && bash -n tools/montar_instalador.sh
tools/publicar.sh --so-build 2>&1 | tail -4                                # as réguas, e ".../dist/publicar/PulseMeet.exe"
/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe -NoProfile -Command \
  "(Get-Item '$(wslpath -w dist/publicar/PulseMeet.exe)').VersionInfo | Format-List FileDescription,ProductName"
```

Expected: 666 aprovados; as réguas passam; `FileDescription : PulseMeet`, `ProductName : PulseMeet`.

- [ ] **Step 5: O `.iss` compila — e o instalador de mentira é apagado**

```bash
T=/mnt/c/Users/andre/AppData/Local/Temp/pulsemeet-iss-conferencia
rm -rf $T && mkdir -p $T/payload $T/motores/python $T/saida
cp dist/publicar/PulseMeet.exe dist/publicar/WebView2Loader.dll tools/repontar_atalhos.ps1 \
   docs/INSTALAR.md CHANGELOG.md assets/logo.ico $T/payload/
echo stub > $T/payload/MicrosoftEdgeWebview2Setup.exe; echo stub > $T/motores/python/leia.txt
cp instalador/MeetingApp.iss $T/
(cd /mnt/c && "/mnt/c/Users/andre/AppData/Local/Programs/Inno Setup 6/ISCC.exe" "/DVersao=0.7.1" \
  "/DPayload=$(wslpath -w $T/payload)" "/DMotores=$(wslpath -w $T/motores)" "/DSaida=$(wslpath -w $T/saida)" \
  "$(wslpath -w $T/MeetingApp.iss)") | tr -d '\r' | grep -E "error|Successful"
ls $T/saida/
rm -rf $T        # NUNCA rodar: tem o AppId de verdade e motores vazios
```

Expected: `Successful compile`, e `PulseMeet-0.7.1-instalador.exe` na saída — apagado em seguida.

- [ ] **Step 6: Commit**

```bash
git add instalador/MeetingApp.iss tools/publicar.sh tools/montar_instalador.sh app-net/Tests/MarcaTests.cs
git commit -m "feat(marca): o instalador instala o PulseMeet.exe e reponta o que apontava para o velho"
```

---

### Task 4: Os documentos

**Files:**
- Modify: `CLAUDE.md`, `docs/MARCA.md`, `docs/ATUALIZACAO.md`, `docs/INSTALAR.md`, `instalador/winget/LEIAME.md`, `app-net/Nucleo/Marca.cs`, `tools/ver_ui.ps1`, `tools/validar_bandeja.ps1`, `docs/superpowers/specs/2026-09-23-ui-ux.md`

- [ ] **Step 1: As edições**

````diff
diff --git a/CLAUDE.md b/CLAUDE.md
--- a/CLAUDE.md
+++ b/CLAUDE.md
@@ -5,7 +5,7 @@ Guia para o Claude Code (claude.ai/code) trabalhar neste repositório.
 ## O que é
 
 Aplicativo Windows nativo que grava reuniões em duas faixas e as transcreve com
-separação de falantes. **Um executável** (`MeetingApp.exe`) que é bandeja e
+separação de falantes. **Um executável** (`PulseMeet.exe`) que é bandeja e
 janela ao mesmo tempo: C#/.NET 8 com a interface em WebView2, e os modelos
 rodando em sidecars Python.
 
@@ -107,12 +107,17 @@ pedir a informação ao usuário já custou duas idas e voltas com respostas
 erradas.
 
 **O app se chama PulseMeet desde 19/08/2026**, e o símbolo é o monograma M.
-Nenhum dos dois está fechado, então **a marca é uma constante só**: `Marca.Nome`
-em `Nucleo/Marca.cs` e o `#define Marca` do `.iss`, com um teste que falha se os
-dois discordarem. O que carrega o nome antigo por baixo — `AppId`,
-`MeetingApp.exe`, a pasta de instalação, o mutex, os namespaces e os
-`LogicalName` dos recursos — **não muda nunca**, e cada um quebra algo diferente
-se mudar. O símbolo é `assets/logo.svg`, e `tools/gerar_icone.py` gera dele os
+**O nome foi fechado pelo dono em 24/09/2026**, e desde então o executável é
+`PulseMeet.exe` e o instalador, `PulseMeet-<versão>-instalador.exe` (plano 7 do
+redesenho). **A marca é uma constante só**: `Marca.Nome` em `Nucleo/Marca.cs` e
+o `#define Marca` do `.iss`, com um teste que falha se os dois discordarem — e
+que confere também o `AssemblyName` do app e os scripts que copiam o `.exe`.
+Quem tinha o `MeetingApp.exe` tem os atalhos repontados pelo
+`tools/repontar_atalhos.ps1`, que o instalador e o `publicar.sh` rodam. O que
+carrega o nome antigo por baixo — `AppId`, a pasta de instalação, o mutex, os
+namespaces, o `RootNamespace` e os `LogicalName` dos recursos, e a pasta de
+dados do WebView2 — **não muda nunca**, e cada um quebra algo diferente se
+mudar. O símbolo é `assets/logo.svg`, e `tools/gerar_icone.py` gera dele os
 seis `.ico`. Tudo em [docs/MARCA.md](docs/MARCA.md). **O winget é o único ponto
 com prazo**: enquanto os manifestos não forem submetidos, o `PackageIdentifier`
 ainda pode ser trocado de graça.
@@ -151,7 +156,7 @@ tools/empacotar_motor_de_ata.sh               # llama.cpp + GGUF (não vai no in
 tools/empacotar_modelos_de_diarizacao.sh      # os 57 MB que substituíram o token
 
 # a interface do disco, para desenhar sem recompilar
-MeetingApp.exe --web C:\caminho\para\app-net\App\web
+PulseMeet.exe --web C:\caminho\para\app-net\App\web
 
 uv sync   # só para as ferramentas de medição em tools/
 ```
diff --git a/app-net/Nucleo/Marca.cs b/app-net/Nucleo/Marca.cs
--- a/app-net/Nucleo/Marca.cs
+++ b/app-net/Nucleo/Marca.cs
@@ -22,11 +22,10 @@ namespace MeetingApp.Nucleo;
 /// <list type="bullet">
 ///   <item><description>o <c>AppId</c> do <c>.iss</c> — é por ele que o Windows
 ///   sabe que a versão nova é atualização, e não um segundo programa;</description></item>
-///   <item><description><c>MeetingApp.exe</c>, a pasta de instalação e o
-///   <c>Global\MeetingApp</c> do mutex — renomear o executável deixa o atalho
-///   de quem já instalou apontando para o vazio, e o mutex é o que impede o
-///   instalador de copiar por cima de um app gravando;</description></item>
-///   <item><description>os namespaces, os <c>AssemblyName</c> e os
+///   <item><description>a pasta de instalação e o <c>Global\MeetingApp</c> do
+///   mutex — o mutex é o que impede o instalador de copiar por cima de um app
+///   gravando;</description></item>
+///   <item><description>os namespaces, o <c>RootNamespace</c> e os
 ///   <c>LogicalName</c> dos recursos embutidos — <c>Conteudo.cs</c> monta
 ///   <c>"MeetingApp.web." + caminho</c> por texto, e um rename silencioso ali
 ///   devolve página em branco;</description></item>
@@ -34,6 +33,11 @@ namespace MeetingApp.Nucleo;
 ///   for submetido: trocá-lo cria um pacote novo em vez de uma atualização.</description></item>
 /// </list>
 /// <para>
+/// O executável seguiu a marca em 24/09/2026: <c>PulseMeet.exe</c>, pelo
+/// <c>AssemblyName</c>. O que apontava para o <c>MeetingApp.exe</c> é repontado
+/// pelo <c>tools/repontar_atalhos.ps1</c> (docs/MARCA.md).
+/// </para>
+/// <para>
 /// O símbolo segue o mesmo caminho: <c>assets/logo.svg</c> é a arte, e
 /// <c>tools/gerar_icone.py</c> gera dela o ícone do .exe e os quatro da
 /// bandeja. Os desenhos recusados ficam em <c>assets/marca-alternativas/</c>.
diff --git a/docs/ATUALIZACAO.md b/docs/ATUALIZACAO.md
--- a/docs/ATUALIZACAO.md
+++ b/docs/ATUALIZACAO.md
@@ -83,7 +83,7 @@ executa é um cliente da Microsoft, e o app continua só avisando.
   quem usa, quando quer. Quem avisa que existe versão nova continua sendo o
   `versao.json` desta página — os dois se somam, um não substitui o outro;
 - **não emagrece o download.** Cada `winget upgrade` baixa o instalador inteiro,
-  1,59 GB, mesmo quando só o `MeetingApp.exe` de 18 MB mudou. É o mesmo problema
+  1,59 GB, mesmo quando só o `PulseMeet.exe` de 18 MB mudou. É o mesmo problema
   do degrau 2, com o mesmo remédio: separar a versão do app da versão dos
   motores.
 
@@ -111,18 +111,24 @@ tools/publicar.sh                         # se algum motor.py mudou
 tools/montar_instalador.sh
 ```
 
+**A primeira versão depois de 24/09/2026 troca o nome do executável**
+(`MeetingApp.exe` → `PulseMeet.exe`, docs/MARCA.md). O CHANGELOG dela diz, para
+quem usa, que o programa passou a se chamar PulseMeet, e que um ícone fixado à
+mão na barra de tarefas é repontado pelo instalador — e, se não abrir, se fixa
+de novo pelo menu Iniciar.
+
 Até aqui é o que sempre foi. O resto existe desde 19/08/2026, e é o que põe o
 instalador ao alcance de quem não recebe arquivo na mão:
 
 ```bash
 V=0.4.0
-sha256sum dist/instalador/MeetingApp-$V-instalador.exe   # anote: vai no manifesto
+sha256sum dist/instalador/PulseMeet-$V-instalador.exe    # anote: vai no manifesto
 
 gh release create v$V \
   --target "$(git rev-parse HEAD)" \
   --title "PulseMeet $V — <o título da seção do CHANGELOG>" \
   --notes-file <um .md com as notas, o SHA256 e o aviso de SmartScreen> \
-  dist/instalador/MeetingApp-$V-instalador.exe
+  dist/instalador/PulseMeet-$V-instalador.exe
 
 cp -r instalador/winget/<versão anterior> instalador/winget/$V
 $EDITOR instalador/winget/$V/*.yaml       # PackageVersion, InstallerUrl,
@@ -157,7 +163,7 @@ nele — desde a v0.3.0 há: a página do release.
 
 ## Os degraus que ficam para depois
 
-**2 — atualizar só o app.** O aviso vira botão: baixa o `MeetingApp.exe`
+**2 — atualizar só o app.** O aviso vira botão: baixa o `PulseMeet.exe`
 (18,5 MB), confere, troca e reabre. É o degrau que mais paga, porque **os
 motores são 4,1 GB e quase nunca mudam** — a maioria das versões novas é só o
 executável. Exige assinatura de código ou conferência de hash, e o segundo sem o
diff --git a/docs/INSTALAR.md b/docs/INSTALAR.md
--- a/docs/INSTALAR.md
+++ b/docs/INSTALAR.md
@@ -20,10 +20,12 @@ Não é preciso instalar Python, CUDA, .NET nem nada. Está tudo dentro.
 
 ## Instalar
 
-1. Rode o `MeetingApp-0.4.0-instalador.exe`. O arquivo ainda se chama
-   `MeetingApp` de propósito: o nome do produto mudou na 0.4.0, o do
-   executável não — é o que faz o Windows tratar a versão nova como
-   atualização, e não como um segundo programa.
+1. Rode o `PulseMeet-<versão>-instalador.exe`. Até a 0.7.1 ele se chamava
+   `MeetingApp-<versão>-instalador.exe`, e o programa, `MeetingApp.exe`: quem
+   já tem o app instalado recebe a versão nova como atualização, e os atalhos
+   passam a abrir o `PulseMeet.exe`. **Um ícone que você mesmo fixou na barra de
+   tarefas** também é repontado; se ele não abrir, fixe-o de novo pelo menu
+   Iniciar.
 
 2. **O Windows vai mostrar um aviso azul** dizendo que o editor é desconhecido.
    Isso acontece porque o instalador não é assinado — assinatura de código custa
diff --git a/docs/MARCA.md b/docs/MARCA.md
--- a/docs/MARCA.md
+++ b/docs/MARCA.md
@@ -1,8 +1,8 @@
 # A marca
 
 O app se chama **PulseMeet** e o símbolo é o monograma M dentro do círculo.
-Nenhum dos dois está fechado — este documento existe para que trocá-los custe
-uma edição, e não uma tarde.
+**O nome foi fechado pelo dono em 24/09/2026**; o símbolo, não. Este documento
+existe para que trocar qualquer um dos dois custe uma edição, e não uma tarde.
 
 ## Trocar o nome
 
@@ -26,8 +26,32 @@ De onde o nome sai, a partir do `Marca.Nome`:
   no campo `marca` do diagnóstico, junto da versão (`web/configuracoes.js`).
 
 E, a partir do `#define Marca`: o `AppName`, o nome do grupo no menu Iniciar, os
-dois atalhos, a tarefa "iniciar com o Windows", o botão do fim da instalação e a
-mensagem da desinstalação.
+dois atalhos, a tarefa "iniciar com o Windows", o botão do fim da instalação, a
+mensagem da desinstalação — e, desde 24/09/2026, o nome do executável que ele
+instala e o do próprio instalador.
+
+## O executável e o instalador (desde 24/09/2026)
+
+Com o nome fechado, os dois seguiram a marca: **`PulseMeet.exe`**, pelo
+`AssemblyName` do `App/MeetingApp.App.csproj` (com `AssemblyTitle` e `Product`,
+que são o que o Gerenciador de Tarefas e as propriedades do arquivo mostram), e
+**`PulseMeet-<versão>-instalador.exe`**, pelo `OutputBaseFilename`. O
+`MarcaTests` confere os três contra o `Marca.Nome`, e confere que o `.iss`, o
+`publicar.sh` e o `montar_instalador.sh` usam o nome novo.
+
+O que apontava para o `MeetingApp.exe` é repontado por
+[`tools/repontar_atalhos.ps1`](../tools/repontar_atalhos.ps1): o menu Iniciar, a
+área de trabalho, os fixados da barra de tarefas e do Iniciar, o "iniciar com
+o Windows" — levando junto a escolha do Gerenciador de Tarefas, para quem tinha
+desligado o início automático — e o ícone de "Aplicativos instalados". O instalador o roda no fim (e apaga o
+`MeetingApp.exe` e a pasta "MeetingApp" do menu Iniciar, de antes da 0.4.0); o
+`publicar.sh` o roda na primeira publicação depois da troca, e só então apaga o
+`.exe` velho. A prova dele roda numa pasta e numa chave de registro de mentira:
+[`tools/provar_repontar_atalhos.ps1`](../tools/provar_repontar_atalhos.ps1).
+
+**O que não se reponta**: um fixado da barra de tarefas pode guardar uma cópia
+do atalho que o Windows não relê. Se o ícone fixado não abrir depois da
+atualização, a pessoa o fixa de novo pelo menu Iniciar.
 
 ## O que não muda com a marca
 
@@ -37,14 +61,17 @@ deles é visto por quem usa o app; todos quebram alguma coisa se mudarem.
 | O quê | Se mudar |
 |---|---|
 | `AppId` do `.iss` | o Windows deixa de reconhecer a atualização: duas entradas em "Aplicativos Instalados" e duas pastas de 5 GB |
-| `MeetingApp.exe` e `%LOCALAPPDATA%\Programs\MeetingApp` | o atalho de quem já instalou passa a apontar para o vazio |
+| `%LOCALAPPDATA%\Programs\MeetingApp` | a atualização do Inno reaproveita a pasta anterior de qualquer jeito; mudar a das instalações novas separaria as máquinas em dois caminhos, e moveria 18 GB de motores sem ninguém ver a diferença |
+| `%LOCALAPPDATA%\MeetingApp\webview` (os dados do WebView2) | o que a página guarda some |
 | `Global\MeetingApp` (mutex) | o instalador volta a copiar por cima de um app que pode estar gravando |
-| namespaces, `AssemblyName`, `LogicalName` dos recursos | `Conteudo.cs` monta `"MeetingApp.web." + caminho` por texto — o app abre com a página em branco |
+| namespaces, `RootNamespace`, `LogicalName` dos recursos | `Conteudo.cs` monta `"MeetingApp.web." + caminho` por texto — o app abre com a página em branco. O `RootNamespace` fica escrito no `.csproj`, porque o padrão dele é o `AssemblyName`, que mudou |
+| os nomes dos arquivos de projeto e do `.iss` | nada visível; são caminhos que scripts e testes usam |
 | `PackageIdentifier` do winget | vira um pacote novo em vez de uma atualização |
 
-O `OutputBaseFilename` (`MeetingApp-<versão>-instalador.exe`) fica junto do
-`.exe` pelo mesmo motivo: é o nome do arquivo que os manifestos winget já
-publicados apontam.
+O `OutputBaseFilename` ficou no nome antigo até a 0.7.1 porque os manifestos
+winget apontam para ele — mas cada manifesto aponta para o arquivo da **sua**
+versão, que já está publicado e não muda. A versão nova sai com o nome novo, e o
+manifesto novo aponta para ele.
 
 **O winget é o único ponto com prazo.** Os manifestos em
 [`instalador/winget/`](../instalador/winget/) ainda não foram submetidos ao
diff --git a/docs/superpowers/specs/2026-09-23-ui-ux.md b/docs/superpowers/specs/2026-09-23-ui-ux.md
--- a/docs/superpowers/specs/2026-09-23-ui-ux.md
+++ b/docs/superpowers/specs/2026-09-23-ui-ux.md
@@ -190,7 +190,7 @@ Todo plano que sair daqui herda estas, e as tarefas não as repetem:
 | **4 · Ajustes por intenção** | as sete seções; "Por quê?"; clientes mestre-detalhe; tipo de ata padrão por projeto | **D-C** | a detalhar depois da revisão |
 | **5 · O que tem gatilho** | busca no conteúdo; atalhos de janela (`UI-5`) | uso | escrito, sem gatilho |
 | **6 · Vozes** | a tela do `UI-2` como no §3.4 — fila de revisão, saúde por perfil, fora de uso dobrado, sugestão de juntar | **D-C** (o lugar em Ajustes) | a detalhar depois da revisão |
-| **7 · A marca nos arquivos** | o instalador sai como `PulseMeet-<versão>-instalador.exe` e o `.exe` diz PulseMeet nas propriedades e no Gerenciador de Tarefas; **se o dono decidir**, o executável vira `PulseMeet.exe`. O que não muda nunca continua no nome antigo — ver o §7 | **D-E**, §6 | pedido pelo dono em 24/09/2026 |
+| **7 · A marca nos arquivos** | o executável vira `PulseMeet.exe` e o instalador, `PulseMeet-<versão>-instalador.exe`; o `.exe` diz PulseMeet nas propriedades e no Gerenciador de Tarefas; os atalhos de quem tinha o `MeetingApp.exe` são repontados. O que não muda nunca continua no nome antigo — ver o §7 | **D-E** (sim) | **feito em 24/09/2026** |
 
 A ordem é a do incômodo e a da dependência: o 1 já dói e não espera ninguém; o 2
 tira um destino e precisa do sim do dono; o 3 muda a tela da única hora em que o
@@ -219,7 +219,9 @@ jeito de provar tela funciona.
   do `.exe` mudam sem custo (§7, nível 1). Trocar o nome do arquivo custa o que
   o §7 diz no nível 2, e só vale se o nome for para valer — o
   [docs/MARCA.md](../../MARCA.md) ainda o dá como não fechado. Recomendado:
-  nível 1 agora; nível 2 só com o nome fechado, numa versão própria.
+  nível 1 agora; nível 2 só com o nome fechado, numa versão própria. **Decidida
+  em 24/09/2026: sim** — "sobre a marca já está decidido o PulseMeet sim, pode
+  mudar o executável também". Os dois níveis entram juntos no plano 7.
 
 ---
 
@@ -254,7 +256,7 @@ MeetingApp está em três níveis:
   (`AssemblyTitle` e `Product` no `.csproj`), sem trocar o nome do arquivo. O
   `MarcaTests` passa a conferir que eles dizem o mesmo que o `Marca.Nome`.
 
-**Nível 2 — possível, com custo, e só com a D-E: `MeetingApp.exe` vira `PulseMeet.exe`.**
+**Nível 2 — com custo, e decidido na D-E: `MeetingApp.exe` vira `PulseMeet.exe`.**
 
 - O instalador apaga o `.exe` velho (`[InstallDelete]`) e refaz o que aponta
   para ele: os atalhos do menu Iniciar e da área de trabalho, e o "iniciar com o
diff --git a/instalador/winget/LEIAME.md b/instalador/winget/LEIAME.md
--- a/instalador/winget/LEIAME.md
+++ b/instalador/winget/LEIAME.md
@@ -59,6 +59,9 @@ wingetcreate update decoworship.PulseMeet `
   --submit --token $env:GITHUB_TOKEN
 ```
 
+A partir da versão seguinte à 0.7.1, o arquivo do release é
+`PulseMeet-<versão>-instalador.exe` (docs/MARCA.md): a URL do `--urls` muda junto.
+
 Isso abre um PR em `microsoft/winget-pkgs`. Enquanto esse PR não for aceito, a
 instalação por uma linha só é a forma com `--manifest` acima.
 
diff --git a/tools/validar_bandeja.ps1 b/tools/validar_bandeja.ps1
--- a/tools/validar_bandeja.ps1
+++ b/tools/validar_bandeja.ps1
@@ -14,7 +14,7 @@
 #     de fazer à mão, porque exige cronometrar cliques.
 #
 # Uso:  powershell.exe -ExecutionPolicy Bypass -File validar_bandeja.ps1 `
-#           -Exe C:\Users\andre\MeetingUnificado\MeetingApp.exe -Segundos 20
+#           -Exe C:\Users\andre\AppData\Local\Programs\MeetingApp\PulseMeet.exe -Segundos 20
 
 param(
     [Parameter(Mandatory = $true)][string]$Exe,
diff --git a/tools/ver_ui.ps1 b/tools/ver_ui.ps1
--- a/tools/ver_ui.ps1
+++ b/tools/ver_ui.ps1
@@ -20,12 +20,12 @@ public static class V {
 # NUNCA matar um MeetingApp que já esteja rodando: ele pode estar no meio de
 # uma transcrição de meia hora, e derrubá-lo perde o trabalho sem aviso. Já
 # aconteceu — 11/08/2026, transcrição de 25 min morta na metade.
-if (Get-Process MeetingApp -ErrorAction SilentlyContinue) {
+if (Get-Process PulseMeet,MeetingApp -ErrorAction SilentlyContinue) {
   Write-Host "ja existe um MeetingApp aberto; feche-o antes (nao vou matar)."
   exit 1
 }
 $web = '\\wsl$\Ubuntu\home\andre\projects\meeting-transcription\app-net\App\web'
-$p = Start-Process 'C:\Users\andre\MeetingApp\MeetingApp.exe' -PassThru `
+$p = Start-Process 'C:\Users\andre\AppData\Local\Programs\MeetingApp\PulseMeet.exe' -PassThru `
      -ArgumentList (@('--web',$web,'--gravacoes',$Gravacoes) + $(if ($Tela) { @('--tela',$Tela) } else { @() }))
 Start-Sleep -Seconds $Espera
 if ($p.HasExited) { "MORREU: " + $p.ExitCode; exit 1 }
````

- [ ] **Step 2: Conferir**

```bash
dotnet test app-net/Tests/MeetingApp.Tests.csproj 2>&1 | tail -1          # 666
grep -n "MeetingApp\.exe" CLAUDE.md docs/MARCA.md docs/ATUALIZACAO.md docs/INSTALAR.md
```

Expected: 666; o `grep` só acha o `MeetingApp.exe` onde ele é o nome **antigo** (o que foi repontado, o de antes da 0.7.1).

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md docs/MARCA.md docs/ATUALIZACAO.md docs/INSTALAR.md instalador/winget/LEIAME.md \
        app-net/Nucleo/Marca.cs tools/ver_ui.ps1 tools/validar_bandeja.ps1 docs/superpowers/specs/2026-09-23-ui-ux.md
git commit -m "docs: o executável e o instalador seguem a marca"
```

---

### Task 5: A troca na máquina do dono

- [ ] **Step 1: Antes, só listando** — o comando do Task 1, Step 5. Expected: `a repontar: 4`.

- [ ] **Step 2: O dono fecha o app pela bandeja.** Pedir; nunca matar o processo.

- [ ] **Step 3: Publicar**

```bash
cd /home/andre/projects/mt-marca && tools/publicar.sh 2>&1 | tail -15
```

Expected: `==> o executável agora é PulseMeet.exe: repontando os atalhos`, as quatro linhas do Step 1 e `repontados: 4`.

- [ ] **Step 4: Depois**

```bash
ls /mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/*.exe
# e o mesmo comando do Step 1
```

Expected: só o `PulseMeet.exe` (sem o `MeetingApp.exe`), e `a repontar: 0`.

- [ ] **Step 5: O percurso do dono**

1. O menu Iniciar tem **PulseMeet** (a pasta "MeetingApp" sumiu), e abre o app.
2. O Gerenciador de Tarefas mostra **PulseMeet** na aba Processos, e em **Aplicativos de inicialização** o PulseMeet com o mesmo estado que o MeetingApp tinha.
3. Em Configurações › Aplicativos instalados, o PulseMeet tem ícone.
4. A bandeja, a janela e uma gravação curta funcionam como antes.
5. Reiniciar o Windows (quando for conveniente): o app sobe sozinho, se estava assim.
