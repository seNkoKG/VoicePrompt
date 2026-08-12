#Requires -Version 5.1
<#
.SYNOPSIS
Installs VoicePrompt and its tested local dictation runtime for the current user.
#>
[CmdletBinding()]
param(
    [switch]$NoLaunch,
    [switch]$NoShortcuts
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Step([string]$Message) {
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Invoke-Checked([string]$FilePath, [string[]]$ArgumentList) {
    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE`: $FilePath $($ArgumentList -join ' ')"
    }
}

if ($env:OS -ne "Windows_NT" -or -not [Environment]::Is64BitOperatingSystem) {
    throw "VoicePrompt requires 64-bit Windows."
}

$packageRoot = $PSScriptRoot
$packageExe = Join-Path $packageRoot "VoicePromptTray.exe"
$packagePatch = Join-Path $packageRoot "scripts\apply_patches.ps1"
$packageMeter = Join-Path $packageRoot "scripts\runtime_meter.py"
$packageAi = Join-Path $packageRoot "scripts\ai_rewriter.py"
$packageHistory = Join-Path $packageRoot "scripts\transcript_history.py"
$packageCorrections = Join-Path $packageRoot "scripts\text_corrections.py"
$packageSlangRetry = Join-Path $packageRoot "scripts\slang_retry.py"
$packageDecodingOptions = Join-Path $packageRoot "scripts\decoding_options.py"
$packageRunner = Join-Path $packageRoot "run_daemon.pyw"
foreach ($required in @($packageExe, $packagePatch, $packageMeter, $packageAi, $packageHistory, $packageCorrections, $packageSlangRetry, $packageDecodingOptions, $packageRunner)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "The release package is incomplete. Missing: $required"
    }
}

Write-Step "Checking Python"
$pythonExe = $null
$launcher = Get-Command "py.exe" -ErrorAction SilentlyContinue
if ($launcher) {
    $pythonExe = (& $launcher.Source -3 -c "import sys; print(sys.executable)" 2>$null | Select-Object -Last 1).Trim()
}
if (-not $pythonExe) {
    $python = Get-Command "python.exe" -ErrorAction SilentlyContinue
    if ($python) {
        $pythonExe = $python.Source
    }
}
if (-not $pythonExe -or -not (Test-Path -LiteralPath $pythonExe)) {
    throw "Python 3.10 or newer was not found. Install it from https://www.python.org/downloads/windows/ and run this installer again."
}
$pythonVersionText = (& $pythonExe -c "import sys; print('.'.join(map(str, sys.version_info[:3])))").Trim()
if ($LASTEXITCODE -ne 0 -or [version]$pythonVersionText -lt [version]"3.10") {
    throw "Python 3.10 or newer is required; found $pythonVersionText at $pythonExe."
}
Write-Host "Python ${pythonVersionText}: $pythonExe"

if (-not (Get-Command "nvidia-smi.exe" -ErrorAction SilentlyContinue)) {
    Write-Warning "nvidia-smi was not found. VoicePrompt's default local engine requires an NVIDIA GPU and current driver."
}

$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
$installRoot = Join-Path $localAppData "Programs\VoicePrompt"
$installScripts = Join-Path $installRoot "scripts"
$installedExe = Join-Path $installRoot "VoicePromptTray.exe"
$runtimeRoot = Join-Path $userProfile ".voice-typing"
$venvRoot = Join-Path $runtimeRoot "venv"
$venvPython = Join-Path $venvRoot "Scripts\python.exe"

Write-Step "Installing VoicePrompt application files"
New-Item -ItemType Directory -Path $installScripts -Force | Out-Null
Get-Process -Name "VoicePromptTray" -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        if ($_.Path -and [System.IO.Path]::GetFullPath($_.Path) -eq [System.IO.Path]::GetFullPath($installedExe)) {
            Stop-Process -Id $_.Id -Force
            $_.WaitForExit(5000)
        }
    } catch {
        Write-Warning "Could not stop an existing VoicePrompt process: $($_.Exception.Message)"
    }
}
Copy-Item -LiteralPath $packageExe -Destination $installedExe -Force
Copy-Item -LiteralPath $packagePatch -Destination (Join-Path $installScripts "apply_patches.ps1") -Force
Copy-Item -LiteralPath $packageMeter -Destination (Join-Path $installScripts "runtime_meter.py") -Force
Copy-Item -LiteralPath $packageAi -Destination (Join-Path $installScripts "ai_rewriter.py") -Force
Copy-Item -LiteralPath $packageHistory -Destination (Join-Path $installScripts "transcript_history.py") -Force
Copy-Item -LiteralPath $packageCorrections -Destination (Join-Path $installScripts "text_corrections.py") -Force
Copy-Item -LiteralPath $packageSlangRetry -Destination (Join-Path $installScripts "slang_retry.py") -Force
Copy-Item -LiteralPath $packageDecodingOptions -Destination (Join-Path $installScripts "decoding_options.py") -Force
Copy-Item -LiteralPath $packageRunner -Destination (Join-Path $installRoot "run_daemon.pyw") -Force
Copy-Item -LiteralPath (Join-Path $packageRoot "install.ps1") -Destination (Join-Path $installRoot "install.ps1") -Force
if (Test-Path -LiteralPath (Join-Path $packageRoot "README.md")) {
    Copy-Item -LiteralPath (Join-Path $packageRoot "README.md") -Destination (Join-Path $installRoot "README.md") -Force
}
if (Test-Path -LiteralPath (Join-Path $packageRoot "assets")) {
    Copy-Item -LiteralPath (Join-Path $packageRoot "assets") -Destination $installRoot -Recurse -Force
}
if (Test-Path -LiteralPath (Join-Path $packageRoot "config.toml")) {
    Copy-Item -LiteralPath (Join-Path $packageRoot "config.toml") -Destination (Join-Path $installRoot "config.example.toml") -Force
}
if (Test-Path -LiteralPath (Join-Path $packageRoot "version.txt")) {
    Copy-Item -LiteralPath (Join-Path $packageRoot "version.txt") -Destination (Join-Path $installRoot "version.txt") -Force
}

Write-Step "Preparing the local speech-to-text runtime"
New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
if (-not (Test-Path -LiteralPath $venvPython)) {
    Invoke-Checked $pythonExe @("-m", "venv", $venvRoot)
}
$daemonExe = Join-Path $venvRoot "Scripts\faster-whisper-dictation.exe"
if (Test-Path -LiteralPath $daemonExe) {
    Write-Step "Stopping the dictation runtime for a clean upgrade"
    Invoke-Checked $daemonExe @("stop")
}
Invoke-Checked $venvPython @("-m", "pip", "install", "--disable-pip-version-check", "--upgrade", "pip")
Invoke-Checked $venvPython @(
    "-m", "pip", "install", "--disable-pip-version-check", "--upgrade",
    "faster-whisper-dictation[local-gpu]==0.2.0",
    "nvidia-cublas-cu12", "nvidia-cudnn-cu12", "nvidia-cuda-runtime-cu12", "nvidia-cuda-nvrtc-cu12"
)

Write-Step "Applying tested Windows integration fixes"
$hostExe = (Get-Process -Id $PID).Path
Invoke-Checked $hostExe @(
    "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", (Join-Path $installScripts "apply_patches.ps1"),
    "-Site", (Join-Path $venvRoot "Lib\site-packages\whisper_dictation"),
    "-RunnerTarget", (Join-Path $runtimeRoot "run_daemon.pyw")
)

if (-not $NoShortcuts) {
    Write-Step "Creating shortcuts"
    $shell = New-Object -ComObject WScript.Shell
    $shortcuts = @(
        (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)) "VoicePrompt.lnk"),
        (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) "VoicePrompt.lnk")
    )
    foreach ($shortcutPath in $shortcuts) {
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $installedExe
        $shortcut.WorkingDirectory = $installRoot
        $shortcut.IconLocation = "$installedExe,0"
        $shortcut.Description = "VoicePrompt local voice typing"
        $shortcut.Save()
    }
    Start-Process -FilePath "$env:SystemRoot\System32\ie4uinit.exe" -ArgumentList "-show" -WindowStyle Hidden -ErrorAction SilentlyContinue
}

Write-Host "`nVoicePrompt installed successfully." -ForegroundColor Green
Write-Host "Application: $installedExe"
Write-Host "Runtime:     $runtimeRoot"
Write-Host "The first start downloads the selected Whisper model; this can take several minutes."

if (-not $NoLaunch) {
    Start-Process -FilePath $installedExe -WorkingDirectory $installRoot
}
