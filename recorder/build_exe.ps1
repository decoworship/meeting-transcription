# Packages the tray recorder into a standalone Windows .exe.
#
# Uses --onedir rather than --onefile: onefile unpacks to a temp folder on every
# launch (slow start) and is a frequent antivirus false positive. The result is
# a folder you can copy anywhere -- no Python needed on the target machine.
#
# ASCII-only + saved with BOM/CRLF: Windows PowerShell 5.1 reads .ps1 as ANSI.
#
# Usage:
#     .\build_exe.ps1

$ErrorActionPreference = "Stop"

$Root    = Join-Path $env:USERPROFILE ".meeting-recorder"
$UvExe   = Join-Path $Root "uv.exe"
$Venv    = Join-Path $Root ".venv"
$Py      = Join-Path $Venv "Scripts\python.exe"
$RepoWin = "\\wsl$\Ubuntu\home\andre\projects\meeting-transcription"
$Src     = Join-Path $RepoWin "recorder"
# Build on the local disk: PyInstaller does heavy I/O and \\wsl$ makes it crawl.
$Work    = Join-Path $Root "build"
$Dist    = Join-Path $Root "dist"

if (-not (Test-Path $Py)) {
    throw "Environment not found. Run setup_windows.ps1 first."
}

Write-Host "[1/3] installing PyInstaller" -ForegroundColor Yellow
& $UvExe pip install --python $Py pyinstaller

Write-Host ""
Write-Host "[2/3] building (this takes a couple of minutes)" -ForegroundColor Yellow
& $Py -m PyInstaller `
    --name MeetingRecorder `
    --onedir `
    --noconsole `
    --clean `
    --noconfirm `
    --workpath $Work `
    --distpath $Dist `
    --specpath $Work `
    --paths $Src `
    --hidden-import pyaudiowpatch `
    --hidden-import soxr `
    --collect-binaries pyaudiowpatch `
    (Join-Path $Src "tray.py")

$Exe = Join-Path $Dist "MeetingRecorder\MeetingRecorder.exe"
Write-Host ""
Write-Host "[3/3] result" -ForegroundColor Yellow
if (Test-Path $Exe) {
    $sizeMb = [math]::Round((Get-ChildItem (Join-Path $Dist "MeetingRecorder") -Recurse |
                             Measure-Object -Property Length -Sum).Sum / 1MB, 1)
    Write-Host "  OK: $Exe" -ForegroundColor Green
    Write-Host "  folder size: $sizeMb MB"
    Write-Host ""
    Write-Host "Copy the whole MeetingRecorder folder to run it anywhere."
    Write-Host "To pin it to the taskbar, right-click MeetingRecorder.exe."
} else {
    throw "Build finished but $Exe was not produced."
}
