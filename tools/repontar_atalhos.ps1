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
