# Builds the self-contained VoicePrompt tray UI for Windows x64 (requires .NET SDK 10+).
# Usage:  powershell -ExecutionPolicy Bypass -File scripts\build_ui.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "ui\VoicePromptTray\VoicePromptTray.csproj"
$out = Join-Path $root "ui\publish"
if (Test-Path -LiteralPath $out) {
    $resolvedRoot = [System.IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
    $resolvedOut = [System.IO.Path]::GetFullPath($out)
    $expectedOut = [System.IO.Path]::GetFullPath((Join-Path $root "ui\publish"))
    $outItem = Get-Item -LiteralPath $resolvedOut -Force -ErrorAction Stop
    if (-not $resolvedOut.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
        -not $resolvedOut.Equals($expectedOut, [System.StringComparison]::OrdinalIgnoreCase) -or
        (($outItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)) {
        throw "Refusing to clean publish directory outside the repository: $resolvedOut"
    }
    Remove-Item -LiteralPath $resolvedOut -Recurse -Force
}
dotnet publish $proj -c Release -r win-x64 --self-contained true -o $out --nologo -v minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output "Built: $out\VoicePromptTray.exe"
