#Requires -Version 5.1
<#
    Haku AI service launcher.

    Activates the venv, preflights Ollama, then starts uvicorn on 0.0.0.0:8000
    so the Quest can reach it over Wi-Fi.

        cd ai-service
        .\run.ps1

    First run, if the venv does not exist yet:
        python -m venv venv
        venv\Scripts\pip install -r requirements.txt
        venv\Scripts\python -m piper.download_voices en_US-lessac-medium --data-dir voices

    If PowerShell refuses to run this file at all:
        Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
#>

$ErrorActionPreference = 'Stop'

# config.py resolves exhibits/ and voices/ relative to the working directory,
# so run from this script's folder no matter where it was invoked from.
Set-Location $PSScriptRoot

Write-Host ''
Write-Host '=== Haku AI service ===' -ForegroundColor Cyan

# --- 1. Virtual environment -------------------------------------------------
$venvDir = if (Test-Path "$PSScriptRoot\.venv") { "$PSScriptRoot\.venv" } else { "$PSScriptRoot\venv" }
$venvPy  = Join-Path $venvDir 'Scripts\python.exe'

if (-not (Test-Path $venvPy)) {
    Write-Host "[FAIL] No virtual environment at $venvDir" -ForegroundColor Red
    Write-Host '       python -m venv venv'
    Write-Host '       venv\Scripts\pip install -r requirements.txt'
    exit 1
}

# Activation is what the setup guide asks for; the explicit $venvPy call below
# is what actually guarantees the right interpreter if activation is blocked by
# execution policy.
$activate = Join-Path $venvDir 'Scripts\Activate.ps1'
try {
    . $activate
    Write-Host "[ok]   venv active: $venvDir" -ForegroundColor Green
} catch {
    Write-Host "[warn] Could not dot-source Activate.ps1 ($($_.Exception.Message))." -ForegroundColor Yellow
    Write-Host '       Continuing with the venv interpreter directly.' -ForegroundColor Yellow
}

# --- 2. Is Ollama reachable? ------------------------------------------------
# Invoke-RestMethod, not curl. In PowerShell `curl` is an alias for
# Invoke-WebRequest and dies on -s, which makes a healthy Ollama look dead.
$ollamaUrl = if ($env:HAKU_OLLAMA) { $env:HAKU_OLLAMA } else { 'http://127.0.0.1:11434' }
try {
    $ver = Invoke-RestMethod -Uri "$ollamaUrl/api/version" -TimeoutSec 5
    Write-Host "[ok]   Ollama $($ver.version) responding at $ollamaUrl" -ForegroundColor Green
} catch {
    Write-Host "[warn] Ollama did NOT respond at $ollamaUrl" -ForegroundColor Yellow
    Write-Host '       The service will still start, but /ask and /converse will fail.' -ForegroundColor Yellow
    Write-Host '       Check the llama icon in the system tray, or run: ollama serve' -ForegroundColor Yellow
    Write-Host '       Logs: %LOCALAPPDATA%\Ollama\server.log' -ForegroundColor Yellow
}

# --- 3. OLLAMA_HOST sanity --------------------------------------------------
# The Ollama server is launched by the tray app and reads the USER-level
# variable at process start. A $env: variable set in this shell only retargets
# the CLI client and does not move the server's bind address.
$ollamaHostUser = [Environment]::GetEnvironmentVariable('OLLAMA_HOST', 'User')
if (-not $ollamaHostUser) {
    Write-Host '[warn] OLLAMA_HOST is not set as a user environment variable.' -ForegroundColor Yellow
    Write-Host '       Ollama is bound to 127.0.0.1 only. That is fine for the guide loop —' -ForegroundColor Yellow
    Write-Host '       this service reaches it over loopback and the Quest only ever talks' -ForegroundColor Yellow
    Write-Host '       to port 8000 — but nothing else on the LAN can reach Ollama directly.' -ForegroundColor Yellow
    Write-Host '       To expose it: set user variable OLLAMA_HOST=0.0.0.0:11434, QUIT Ollama' -ForegroundColor Yellow
    Write-Host '       from the system tray, then relaunch it from the Start menu.' -ForegroundColor Yellow
} elseif ($ollamaHostUser -like '0.0.0.0*') {
    Write-Host "[ok]   OLLAMA_HOST = $ollamaHostUser (LAN-exposed)" -ForegroundColor Green
    Write-Host '       Note: the ollama CLI reads the same variable as its target and will' -ForegroundColor DarkGray
    Write-Host "       hang. Before using it: `$env:OLLAMA_HOST='127.0.0.1:11434'" -ForegroundColor DarkGray
} else {
    Write-Host "[ok]   OLLAMA_HOST = $ollamaHostUser" -ForegroundColor Green
}

# --- 4. Start the service ---------------------------------------------------
$port = if ($env:HAKU_PORT) { $env:HAKU_PORT } else { '8000' }

$lanIp = (Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
          Where-Object { $_.IPAddress -ne '127.0.0.1' -and $_.PrefixOrigin -ne 'WellKnown' } |
          Select-Object -First 1).IPAddress
if (-not $lanIp) { $lanIp = '<laptop-lan-ip>' }

Write-Host ''
Write-Host "Local : http://127.0.0.1:$port/health"
Write-Host "Quest : http://${lanIp}:$port"  -ForegroundColor Cyan
Write-Host ''
Write-Host 'Startup loads Whisper and Piper and pins the LLM in VRAM. Expect a pause;' -ForegroundColor DarkGray
Write-Host 'a cold LLM load alone was measured at 50 seconds. Wait for "[haku] ready".' -ForegroundColor DarkGray
Write-Host ''

# No --reload. A reload reloads every model, and models are the expensive part.
& $venvPy -m uvicorn main:app --host 0.0.0.0 --port $port
