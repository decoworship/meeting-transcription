# Sets up the recorder's Python environment on Windows, self-contained.
#
# The manager, the interpreter and the dependencies all live inside a single
# folder. Nothing touches the registry, the system PATH, or an .msi installer.
# To uninstall completely: delete $Root.
#
# The source code is NOT copied -- it runs straight from the repo over \\wsl$,
# so only one copy of it ever exists.
#
# ASCII-only on purpose: Windows PowerShell 5.1 reads .ps1 as ANSI unless the
# file carries a BOM, and mangled accents break the parser.
#
# Usage (PowerShell, no admin needed):
#     .\setup_windows.ps1

$ErrorActionPreference = "Stop"

$Root    = Join-Path $env:USERPROFILE ".meeting-recorder"
$UvExe   = Join-Path $Root "uv.exe"
$Venv    = Join-Path $Root ".venv"
$RepoWin = "\\wsl$\Ubuntu\home\andre\projects\meeting-transcription"
$Reqs    = Join-Path $RepoWin "recorder\requirements.txt"

# Keep interpreters inside $Root instead of %LOCALAPPDATA%\uv.
$env:UV_PYTHON_INSTALL_DIR = Join-Path $Root "python"
$env:UV_CACHE_DIR          = Join-Path $Root "cache"

Write-Host "environment root: $Root" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $Root | Out-Null

# --- 1. uv (single binary) -------------------------------------------------
if (-not (Test-Path $UvExe)) {
    Write-Host "[1/4] downloading uv..." -ForegroundColor Yellow
    $zip = Join-Path $env:TEMP "uv-x86_64-pc-windows-msvc.zip"
    Invoke-WebRequest -UseBasicParsing `
        -Uri "https://github.com/astral-sh/uv/releases/latest/download/uv-x86_64-pc-windows-msvc.zip" `
        -OutFile $zip
    Expand-Archive -Path $zip -DestinationPath $Root -Force
    Remove-Item $zip -Force
} else {
    Write-Host "[1/4] uv already present" -ForegroundColor DarkGray
}
& $UvExe --version

# --- 2. interpreter --------------------------------------------------------
Write-Host ""
Write-Host "[2/4] installing Python 3.12 into $($env:UV_PYTHON_INSTALL_DIR)" -ForegroundColor Yellow
& $UvExe python install 3.12

# --- 3. virtual environment ------------------------------------------------
Write-Host ""
Write-Host "[3/4] creating the venv at $Venv" -ForegroundColor Yellow
& $UvExe venv --python 3.12 $Venv

# --- 4. dependencies -------------------------------------------------------
if (-not (Test-Path $Reqs)) {
    throw "requirements.txt not found at $Reqs (is WSL running?)"
}
$Py = Join-Path $Venv "Scripts\python.exe"
Write-Host ""
Write-Host "[4/4] installing dependencies" -ForegroundColor Yellow
& $UvExe pip install --python $Py -r $Reqs

# --- verification ----------------------------------------------------------
# Kept in a separate .py on purpose: PowerShell 5.1 here-strings do not work
# in LF-only files, which is how the repo is edited from WSL.
Write-Host ""
Write-Host "--- verification ---" -ForegroundColor Cyan
& $Py (Join-Path $RepoWin "recorder\verify_env.py")

Write-Host ""
Write-Host "Ready. To run the device probe:" -ForegroundColor Green
Write-Host "  $Py `"$RepoWin\recorder\probe_devices.py`""
Write-Host ""
Write-Host "To remove everything: Remove-Item -Recurse -Force `"$Root`"" -ForegroundColor DarkGray
