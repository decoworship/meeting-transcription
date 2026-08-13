# Validação por execução do app unificado (critérios C, D e E da Fase 2.5).
#
# "Medir tamanho sem executar não vale nada": o primeiro binário trimado tinha
# 11,9 MB e morria na primeira linha. Este script executa o .exe publicado de
# verdade e exige o meta.json como prova.
#
# O que ele exercita, sem ninguém olhando a tela:
#   - registro da classe de janela e criação do ícone a partir do .ico embutido;
#   - o contrato de callback da NOTIFYICON_VERSION_4 (clique = NIN_SELECT);
#   - o laço de mensagens único, com os dois HWND no mesmo processo;
#   - a captura das duas faixas e a escrita do meta.json pelo caminho normal;
#   - **o critério C**: abrir a janela durante a gravação, fechá-la, e a
#     gravação continuar. É o teste que a Fase 2.5 mais precisa e o mais chato
#     de fazer à mão, porque exige cronometrar cliques.
#
# Uso:  powershell.exe -ExecutionPolicy Bypass -File validar_bandeja.ps1 `
#           -Exe C:\Users\andre\MeetingUnificado\MeetingApp.exe -Segundos 20

param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [int]$Segundos = 20,
    # Onde gravar. Padrão: uma pasta temporária, para o teste não encher a lista
    # de reuniões de verdade com gravações de 20 segundos.
    [string]$Saida = (Join-Path $env:TEMP "meetingapp-validacao")
)

$ErrorActionPreference = "Stop"

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class U {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowW(string cls, string title);
    [DllImport("user32.dll")]
    public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr h);
}
"@

$WM_BANDEJA = 0x0401   # WM_APP + 1, o callback do ícone
$NIN_SELECT = 0x0400   # clique, no contrato da versão 4
$WM_CLOSE   = 0x0010
$MOSTRAR    = [U]::RegisterWindowMessageW("MeetingApp.MostrarJanela")

New-Item -ItemType Directory -Force -Path $Saida | Out-Null
Get-ChildItem $Saida -Directory | Remove-Item -Recurse -Force

# --bandeja: sobe sem janela, como no início com o Windows. A janela entra
# depois, no meio da gravação, que é o cenário do critério C.
$proc = Start-Process -FilePath $Exe -ArgumentList "--bandeja","--gravacoes",$Saida -PassThru
Write-Host "processo $($proc.Id) iniciado (sem janela)"

# A janela invisível é criada logo no começo; se em 10 s não existe, o binário
# morreu.
$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 100 -and $hwnd -eq [IntPtr]::Zero; $i++) {
    Start-Sleep -Milliseconds 100
    if ($proc.HasExited) { throw "o processo morreu com código $($proc.ExitCode)" }
    # Classe e título: o $null do PowerShell chega ao Win32 como string vazia,
    # não como NULL, e a busca só por classe nunca acha nada.
    $hwnd = [U]::FindWindowW("MeetingApp.JanelaDaBandeja", "Reuniões")
}
if ($hwnd -eq [IntPtr]::Zero) { throw "a janela da bandeja nunca apareceu" }
Write-Host "bandeja de pé: $hwnd"

# Clique no ícone: com o gravador parado, inicia a gravação.
[void][U]::PostMessageW($hwnd, $WM_BANDEJA, [IntPtr]0, [IntPtr]$NIN_SELECT)
Write-Host "gravando por $Segundos s..."
Start-Sleep -Seconds ([Math]::Max(2, [int]($Segundos / 2)))

# ── critério C ────────────────────────────────────────────────────────────
# Abrir a janela no meio da gravação, esperar o WebView2 subir, e fechá-la.
[void][U]::PostMessageW($hwnd, $MOSTRAR, [IntPtr]0, [IntPtr]0)
$janela = [IntPtr]::Zero
for ($i = 0; $i -lt 150 -and $janela -eq [IntPtr]::Zero; $i++) {
    Start-Sleep -Milliseconds 100
    $janela = [U]::FindWindowW("MeetingApp.Janela", "Reuniões")
}
if ($janela -eq [IntPtr]::Zero) { throw "a janela do app não abriu" }
Write-Host "janela aberta durante a gravação: $janela"

Start-Sleep -Seconds 3
[void][U]::PostMessageW($janela, $WM_CLOSE, [IntPtr]0, [IntPtr]0)
Start-Sleep -Seconds 2

if ($proc.HasExited) {
    throw "CRITÉRIO C FALHOU: fechar a janela encerrou o processo e a gravação."
}
if ([U]::IsWindowVisible($janela)) { throw "a janela não escondeu ao fechar" }
Write-Host "janela fechada, processo vivo (critério C ok)"

Start-Sleep -Seconds ([Math]::Max(2, [int]($Segundos / 2)))
if ($proc.HasExited) { throw "o processo morreu durante a gravação" }

# Sair pelo caminho normal: WM_CLOSE na janela da BANDEJA -> WM_DESTROY -> fim
# do laço -> Parar(), que é quem escreve o meta.json.
[void][U]::PostMessageW($hwnd, $WM_CLOSE, [IntPtr]0, [IntPtr]0)
if (-not $proc.WaitForExit(20000)) { throw "o processo não saiu depois do WM_CLOSE" }
Write-Host "saiu limpo com código $($proc.ExitCode)"

# A prova: a gravação chegou ao disco inteira.
$pasta = Get-ChildItem $Saida -Directory | Sort-Object Name | Select-Object -Last 1
if (-not $pasta) { throw "nenhuma pasta de gravação foi criada em $Saida" }

$meta = Join-Path $pasta.FullName "meta.json"
if (-not (Test-Path $meta)) { throw "sem meta.json em $($pasta.FullName)" }

$m = Get-Content $meta -Raw | ConvertFrom-Json
Write-Host ("meta.json: {0:N1}s, mic {1} quadros, system {2} quadros" -f `
    $m.duration_s, $m.tracks.mic.frames, $m.tracks.system.frames)

foreach ($faixa in "mic","system") {
    $wav = Join-Path $pasta.FullName "$faixa.wav"
    if (-not (Test-Path $wav)) { throw "sem $faixa.wav" }
    if ((Get-Item $wav).Length -lt 1000) { throw "$faixa.wav saiu vazio" }
}

# A duração tem que bater com o tempo pedido. Uma faixa que sai curta demais é
# exatamente o que a fusão poderia ter quebrado sem quebrar nada visível.
if ($m.duration_s -lt ($Segundos * 0.8)) {
    throw ("a gravação saiu curta: {0:N1}s para {1}s pedidos" -f $m.duration_s, $Segundos)
}

Write-Host "OK — gravação íntegra em $($pasta.FullName)"
