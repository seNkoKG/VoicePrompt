# Builds the Voice Prompt tray UI (requires .NET SDK 8+).
# Usage:  powershell -ExecutionPolicy Bypass -File scripts\build_ui.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$proj = Join-Path $root "ui\VoicePromptTray\VoicePromptTray.csproj"
$out = Join-Path $root "ui\publish"
dotnet publish $proj -c Release -o $out --nologo -v minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output "Built: $out\VoicePromptTray.exe"
