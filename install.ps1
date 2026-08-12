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

function Stop-VoicePromptRuntime([string]$DaemonExe, [string]$VenvRoot, [string]$LocalAppData) {
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "SilentlyContinue"
        $stopOutput = @(& $DaemonExe stop 2>&1)
        $stopExitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    if ($stopExitCode -eq 0) {
        $stopOutput | ForEach-Object { Write-Output $_ }
        return
    }

    Write-Warning "The existing runtime could not stop itself; checking its verified VoicePrompt process."
    $configRoot = Join-Path $LocalAppData "faster-whisper-dictation\faster-whisper-dictation"
    $pidFile = Join-Path $configRoot "daemon.pid"
    $stateFile = Join-Path $configRoot "state.json"
    if (-not (Test-Path -LiteralPath $pidFile)) {
        Write-Warning "No live VoicePrompt runtime PID remained; continuing the upgrade."
        return
    }

    $daemonPid = 0
    $pidText = (Get-Content -LiteralPath $pidFile -Raw).Trim()
    if (-not [int]::TryParse($pidText, [ref]$daemonPid) -or $daemonPid -le 0) {
        throw "The VoicePrompt runtime PID file is invalid; refusing to stop an unknown process."
    }

    $process = Get-CimInstance Win32_Process -Filter "ProcessId = $daemonPid" -ErrorAction SilentlyContinue
    if ($process) {
        $resolvedVenv = [System.IO.Path]::GetFullPath($VenvRoot).TrimEnd('\') + '\'
        $resolvedProcess = if ($process.ExecutablePath) { [System.IO.Path]::GetFullPath($process.ExecutablePath) } else { "" }
        $isManagedPython = $resolvedProcess.StartsWith($resolvedVenv, [System.StringComparison]::OrdinalIgnoreCase) -and
            [System.IO.Path]::GetFileName($resolvedProcess) -in @("python.exe", "pythonw.exe")
        $isVoicePromptCommand = [string]$process.CommandLine -match '(?i)run_daemon\.pyw'
        if (-not $isManagedPython -or -not $isVoicePromptCommand) {
            throw "The saved runtime PID does not belong to VoicePrompt; refusing to stop process $daemonPid."
        }
        Stop-Process -Id $daemonPid -Force -ErrorAction Stop
        Wait-Process -Id $daemonPid -Timeout 10 -ErrorAction SilentlyContinue
        if (Get-Process -Id $daemonPid -ErrorAction SilentlyContinue) {
            throw "VoicePrompt runtime process $daemonPid did not stop."
        }
        Write-Output "Stopped verified VoicePrompt runtime (PID $daemonPid)"
    }

    foreach ($statePath in @($pidFile, $stateFile)) {
        if (Test-Path -LiteralPath $statePath) {
            Remove-Item -LiteralPath $statePath -Force
        }
    }
}

if ($env:OS -ne "Windows_NT" -or -not [Environment]::Is64BitOperatingSystem) {
    throw "VoicePrompt requires 64-bit Windows."
}

$packageRoot = $PSScriptRoot
$packageExe = Join-Path $packageRoot "VoicePromptTray.exe"
$packagePatch = Join-Path $packageRoot "scripts\apply_patches.ps1"
$packageShortcutManager = Join-Path $packageRoot "scripts\shortcut_manager.ps1"
$packageMeter = Join-Path $packageRoot "scripts\runtime_meter.py"
$packageAi = Join-Path $packageRoot "scripts\ai_rewriter.py"
$packageHistory = Join-Path $packageRoot "scripts\transcript_history.py"
$packageCorrections = Join-Path $packageRoot "scripts\text_corrections.py"
$packageSlangRetry = Join-Path $packageRoot "scripts\slang_retry.py"
$packageDecodingOptions = Join-Path $packageRoot "scripts\decoding_options.py"
$packageBuffered = Join-Path $packageRoot "scripts\buffered_transcription.py"
$packageOutputMode = Join-Path $packageRoot "scripts\output_mode.py"
$packageAppProfiles = Join-Path $packageRoot "scripts\app_profiles.py"
$packageTextSnippets = Join-Path $packageRoot "scripts\text_snippets.py"
$packageVoiceCommands = Join-Path $packageRoot "scripts\voice_commands.py"
$packageRunner = Join-Path $packageRoot "run_daemon.pyw"
$packageRequirements = Join-Path $packageRoot "requirements.txt"
foreach ($required in @($packageExe, $packagePatch, $packageShortcutManager, $packageMeter, $packageAi, $packageHistory, $packageCorrections, $packageSlangRetry, $packageDecodingOptions, $packageBuffered, $packageOutputMode, $packageAppProfiles, $packageTextSnippets, $packageVoiceCommands, $packageRunner, $packageRequirements)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "The release package is incomplete. Missing: $required"
    }
}

Write-Step "Checking Python"
$pythonExe = $null
$launcher = Get-Command "py.exe" -ErrorAction SilentlyContinue
if ($launcher) {
    foreach ($selector in @("-3.12", "-3.13", "-3.11", "-3.14", "-3")) {
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = "SilentlyContinue"
            $candidate = & $launcher.Source $selector -c "import sys; print(sys.executable)" 2>$null | Select-Object -Last 1
            $candidateExitCode = $LASTEXITCODE
        } finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }
        if ($candidateExitCode -eq 0 -and $candidate) {
            $pythonExe = $candidate.Trim()
            if (Test-Path -LiteralPath $pythonExe) {
                break
            }
            $pythonExe = $null
        }
    }
}
if (-not $pythonExe) {
    $python = Get-Command "python.exe" -ErrorAction SilentlyContinue
    if ($python) {
        $pythonExe = $python.Source
    }
}
if (-not $pythonExe -or -not (Test-Path -LiteralPath $pythonExe)) {
    throw "Python 3.11 or newer was not found. Install it from https://www.python.org/downloads/windows/ and run this installer again."
}
$pythonVersionText = (& $pythonExe -c "import sys; print('.'.join(map(str, sys.version_info[:3])))").Trim()
if ($LASTEXITCODE -ne 0 -or [version]$pythonVersionText -lt [version]"3.11") {
    throw "Python 3.11 or newer is required; found $pythonVersionText at $pythonExe."
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
Copy-Item -LiteralPath $packageBuffered -Destination (Join-Path $installScripts "buffered_transcription.py") -Force
Copy-Item -LiteralPath $packageOutputMode -Destination (Join-Path $installScripts "output_mode.py") -Force
Copy-Item -LiteralPath $packageAppProfiles -Destination (Join-Path $installScripts "app_profiles.py") -Force
Copy-Item -LiteralPath $packageTextSnippets -Destination (Join-Path $installScripts "text_snippets.py") -Force
Copy-Item -LiteralPath $packageVoiceCommands -Destination (Join-Path $installScripts "voice_commands.py") -Force
Copy-Item -LiteralPath $packageRunner -Destination (Join-Path $installRoot "run_daemon.pyw") -Force
Copy-Item -LiteralPath $packageRequirements -Destination (Join-Path $installRoot "requirements.txt") -Force
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
$daemonExe = Join-Path $venvRoot "Scripts\faster-whisper-dictation.exe"
if (Test-Path -LiteralPath $daemonExe) {
    Write-Step "Stopping the dictation runtime for a clean upgrade"
    Stop-VoicePromptRuntime -DaemonExe $daemonExe -VenvRoot $venvRoot -LocalAppData $localAppData
}

$venvIsCompatible = $false
if (Test-Path -LiteralPath $venvPython) {
    $venvVersionOutput = & $venvPython -c "import sys; print('.'.join(map(str, sys.version_info[:3])))" 2>$null | Select-Object -Last 1
    if ($LASTEXITCODE -eq 0 -and $venvVersionOutput) {
        try {
            $venvIsCompatible = [version]$venvVersionOutput.Trim() -ge [version]"3.11"
        } catch {
            $venvIsCompatible = $false
        }
    }
}
if ((Test-Path -LiteralPath $venvRoot) -and -not $venvIsCompatible) {
    Write-Step "Rebuilding an outdated or incomplete local runtime"
    $resolvedRuntimeRoot = [System.IO.Path]::GetFullPath($runtimeRoot).TrimEnd('\') + '\'
    $resolvedVenvRoot = [System.IO.Path]::GetFullPath($venvRoot)
    if (-not $resolvedVenvRoot.StartsWith($resolvedRuntimeRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to rebuild a runtime outside the VoicePrompt data directory: $resolvedVenvRoot"
    }
    Remove-Item -LiteralPath $resolvedVenvRoot -Recurse -Force
}
if (-not (Test-Path -LiteralPath $venvPython)) {
    Invoke-Checked $pythonExe @("-m", "venv", $venvRoot)
}
Invoke-Checked $venvPython @("-m", "pip", "install", "--disable-pip-version-check", "--upgrade", "pip==26.2.1")
Invoke-Checked $venvPython @(
    "-m", "pip", "install", "--disable-pip-version-check", "--upgrade", "--only-binary=:all:",
    "--requirement", $packageRequirements
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
    . $packageShortcutManager
    Install-VoicePromptShortcuts -InstalledExe $installedExe -InstallRoot $installRoot
    Start-Process -FilePath "$env:SystemRoot\System32\ie4uinit.exe" -ArgumentList "-show" -WindowStyle Hidden -ErrorAction SilentlyContinue
}

Write-Host "`nVoicePrompt installed successfully." -ForegroundColor Green
Write-Host "Application: $installedExe"
Write-Host "Runtime:     $runtimeRoot"
Write-Host "The first start downloads the selected Whisper model; this can take several minutes."

if (-not $NoLaunch) {
    Start-Process -FilePath $installedExe -WorkingDirectory $installRoot
}
