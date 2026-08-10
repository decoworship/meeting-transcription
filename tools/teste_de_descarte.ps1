# Toca um sinal com marcadores temporais e grava o loopback ao mesmo tempo.
#
# Serve para responder uma pergunta que a comparação entre gravadores não
# responde: quando a âncora de deriva descarta amostras, ela está tirando
# silêncio (correto, requisito 3.1) ou cortando conteúdo?
#
# O sinal tem um bipe a cada 1,000 s exato. Se a captura preservar o áudio, os
# bipes chegam a 1,000 s de distância; se descartar conteúdo, os intervalos
# encolhem — e isso não depende do volume do sistema, ao contrário de comparar
# energia.
#
# Uso:
#   powershell -ExecutionPolicy Bypass -File teste_de_descarte.ps1 `
#       -Sinal C:\...\teste-bipes.wav -Exe C:\...\Capture.exe -Saida C:\...\out

param(
    [Parameter(Mandatory = $true)][string]$Sinal,
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Saida,
    [int]$Segundos = 125
)

$ErrorActionPreference = "Stop"

# O volume precisa estar audível: o loopback captura o que sai do dispositivo de
# render, então som mudo grava silêncio e o teste não diz nada.
Write-Host "gravando $Segundos s e tocando o sinal..."

$captura = Start-Process -FilePath $Exe `
    -ArgumentList "--seconds", $Segundos, "--track", "system", "--out", $Saida `
    -PassThru -NoNewWindow

# Dois segundos de folga: a captura precisa estar de pé antes do primeiro bipe,
# senão o primeiro marcador se perde e a contagem começa errada.
Start-Sleep -Seconds 2

$player = New-Object Media.SoundPlayer $Sinal
$player.PlaySync()
Write-Host "sinal terminou; esperando a captura fechar o arquivo..."

if (-not $captura.WaitForExit(60000)) {
    $captura.Kill()
    throw "a captura não terminou sozinha"
}
Write-Host "captura encerrada com código $($captura.ExitCode)"
