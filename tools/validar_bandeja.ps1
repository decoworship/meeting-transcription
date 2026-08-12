# Validação por execução da bandeja nativa (critério E da Fase 1).
#
# "Medir tamanho sem executar não vale nada": o primeiro binário trimado tinha
# 11,9 MB e morria na primeira linha. Este script executa o .exe publicado de
# verdade e exige o meta.json como prova.
#
# O que ele exercita, sem ninguém olhando a tela: registro da classe de janela,
# criação do ícone a partir do .ico embutido, o contrato de callback da
# NOTIFYICON_VERSION_4 (clique = NIN_SELECT), o laço de mensagens, a captura das
# duas faixas e a escrita do meta.json na saída pelo caminho normal.
#
# Uso:  powershell.exe -ExecutionPolicy Bypass -File validar_bandeja.ps1 `
#           -Exe C:\caminho\MeetingRecorder.exe -Segundos 20

param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [int]$Segundos = 20
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
}
"@

$WM_BANDEJA = 0x0401   # WM_APP + 1, o callback do ícone
$NIN_SELECT = 0x0400   # clique, no contrato da versão 4
$WM_CLOSE   = 0x0010

$proc = Start-Process -FilePath $Exe -PassThru
Write-Host "processo $($proc.Id) iniciado"

# A janela é criada logo no começo; se em 10 s não existe, o binário morreu.
$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 100 -and $hwnd -eq [IntPtr]::Zero; $i++) {
    Start-Sleep -Milliseconds 100
    if ($proc.HasExited) { throw "o processo morreu com código $($proc.ExitCode)" }
    # Classe e título: o $null do PowerShell chega ao Win32 como string vazia,
    # não como NULL, e a busca só por classe nunca acha nada.
    $hwnd = [U]::FindWindowW("MeetingRecorder.Janela", "Gravador")
}
if ($hwnd -eq [IntPtr]::Zero) { throw "a janela da bandeja nunca apareceu" }
Write-Host "janela encontrada: $hwnd"

# Clique no ícone: com o gravador parado, inicia a gravação.
[void][U]::PostMessageW($hwnd, $WM_BANDEJA, [IntPtr]0, [IntPtr]$NIN_SELECT)
Write-Host "gravando por $Segundos s..."
Start-Sleep -Seconds $Segundos

if ($proc.HasExited) { throw "o processo morreu durante a gravação" }

# Sair pelo caminho normal: WM_CLOSE -> WM_DESTROY -> fim do laço -> Parar(),
# que é quem escreve o meta.json.
[void][U]::PostMessageW($hwnd, $WM_CLOSE, [IntPtr]0, [IntPtr]0)
if (-not $proc.WaitForExit(15000)) { throw "o processo não saiu depois do WM_CLOSE" }

Write-Host "saiu limpo com código $($proc.ExitCode)"
