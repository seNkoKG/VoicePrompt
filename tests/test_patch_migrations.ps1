# Verifies the Windows runtime patch on both clean upstream and v1.1.2 upgrade layouts.
[CmdletBinding()]
param(
    [string]$Python = "python.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$currentPatch = Join-Path $root "scripts\apply_patches.ps1"
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("voiceprompt-patch-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null

function New-UpstreamRuntime([string]$Name) {
    $sitePackages = Join-Path $testRoot "$Name\site"
    New-Item -ItemType Directory -Path $sitePackages -Force | Out-Null
    & $Python -m pip install --quiet --no-deps --target $sitePackages "faster-whisper-dictation==0.2.0"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not install the upstream patch fixture."
    }
    return Join-Path $sitePackages "whisper_dictation"
}

function Invoke-RuntimePatch([string]$Patch, [string]$Module, [string]$Name) {
    $runner = Join-Path (Split-Path -Parent $Module) "$Name-run_daemon.pyw"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Patch -Site $Module -RunnerTarget $runner
    if ($LASTEXITCODE -ne 0) {
        throw "Runtime patch failed for $Name."
    }
}

function Assert-CurrentRuntime([string]$Module, [string]$Name) {
    $localEngine = Join-Path $Module "engine\local.py"
    $typer = Join-Path $Module "typer.py"
    & $Python -m py_compile $localEngine $typer (Join-Path $Module "slang_retry.py")
    if ($LASTEXITCODE -ne 0) {
        throw "$Name runtime does not compile."
    }

    $source = [System.IO.File]::ReadAllText($localEngine)
    if ($source.Contains("if should_retry_as_slovenian(")) {
        throw "$Name retained the obsolete retry block."
    }
    if ([regex]::Matches($source, "Bilingual retry accepted:").Count -ne 1) {
        throw "$Name has an invalid bilingual retry block count."
    }
    if ([regex]::Matches($source, "recognition_prompt\(server_config\.language").Count -ne 1) {
        throw "$Name does not apply Auto vocabulary to the primary pass."
    }
    if ($source.Contains("self._prompt = server_config.prompt")) {
        throw "$Name retained recognition settings that override Auto vocabulary."
    }
    if ([regex]::Matches($source, "Transcription latency:").Count -ne 1) {
        throw "$Name does not have exactly one latency instrumentation block."
    }

    $typerSource = [System.IO.File]::ReadAllText($typer)
    if ([regex]::Matches($typerSource, "def _win_clipboard_api\(").Count -ne 1) {
        throw "$Name does not have exactly one canonical clipboard API helper."
    }
    if ([regex]::Matches($typerSource, "user32\.OpenClipboard\.argtypes").Count -ne 1) {
        throw "$Name retained duplicate Win32 clipboard declarations."
    }
    Write-Output "PASS $Name"
}

$cleanModule = New-UpstreamRuntime "clean"
Invoke-RuntimePatch $currentPatch $cleanModule "clean-first"
Invoke-RuntimePatch $currentPatch $cleanModule "clean-second"
Assert-CurrentRuntime $cleanModule "clean install"

$releaseZip = Join-Path $testRoot "VoicePrompt-v1.1.2-windows-x64.zip"
$releaseRoot = Join-Path $testRoot "v1.1.2"
Invoke-WebRequest `
    -Uri "https://github.com/seNkoKG/VoicePrompt/releases/download/v1.1.2/VoicePrompt-v1.1.2-windows-x64.zip" `
    -OutFile $releaseZip
Expand-Archive -LiteralPath $releaseZip -DestinationPath $releaseRoot
$oldPatch = Join-Path $releaseRoot "scripts\apply_patches.ps1"
if (-not (Test-Path -LiteralPath $oldPatch)) {
    throw "The v1.1.2 upgrade fixture is missing apply_patches.ps1."
}

$upgradeModule = New-UpstreamRuntime "upgrade"
Invoke-RuntimePatch $oldPatch $upgradeModule "v1.1.2"
Invoke-RuntimePatch $currentPatch $upgradeModule "upgrade-first"
Invoke-RuntimePatch $currentPatch $upgradeModule "upgrade-second"
Assert-CurrentRuntime $upgradeModule "v1.1.2 upgrade"

Write-Output "PATCH_MIGRATION_GATE=PASS"
