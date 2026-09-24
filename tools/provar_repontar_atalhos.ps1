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
