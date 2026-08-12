# Builds the versioned Windows release assets used locally and by GitHub Actions.
[CmdletBinding()]
param(
    [string]$Version
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "ui\VoicePromptTray\VoicePromptTray.csproj"
$projectXml = [xml](Get-Content -Raw -LiteralPath $project)
$projectVersion = [string]$projectXml.Project.PropertyGroup.Version
if (-not $Version) {
    $Version = $projectVersion
}
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Version must be semantic (for example 1.0.0): $Version"
}
if ($Version -ne $projectVersion) {
    throw "Release version $Version does not match VoicePromptTray.csproj version $projectVersion."
}

$tag = "v$Version"
$dist = Join-Path $root "dist"
$publish = Join-Path $dist "publish"
$packageName = "VoicePrompt-$tag-windows-x64"
$packageRoot = Join-Path $dist $packageName
$zip = Join-Path $dist "$packageName.zip"
$standaloneExe = Join-Path $dist "VoicePromptTray.exe"
$checksums = Join-Path $dist "VoicePrompt-$tag-SHA256SUMS.txt"

$resolvedRoot = [System.IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
$resolvedDist = [System.IO.Path]::GetFullPath($dist)
if (-not $resolvedDist.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean release directory outside the repository: $resolvedDist"
}
if (Test-Path -LiteralPath $resolvedDist) {
    Remove-Item -LiteralPath $resolvedDist -Recurse -Force
}
New-Item -ItemType Directory -Path $publish, (Join-Path $packageRoot "scripts"), (Join-Path $packageRoot "assets") -Force | Out-Null

dotnet publish $project -c Release -r win-x64 --self-contained true -o $publish --nologo -v minimal
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$builtExe = Join-Path $publish "VoicePromptTray.exe"
if (-not (Test-Path -LiteralPath $builtExe)) {
    throw "Publish completed without VoicePromptTray.exe."
}
Copy-Item -LiteralPath $builtExe -Destination (Join-Path $packageRoot "VoicePromptTray.exe")
Copy-Item -LiteralPath $builtExe -Destination $standaloneExe
Copy-Item -LiteralPath (Join-Path $root "install.ps1") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $root "CHANGELOG.md") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $root "run_daemon.pyw") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $root "config.toml") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $root "assets\logo.png") -Destination (Join-Path $packageRoot "assets")
Copy-Item -LiteralPath (Join-Path $root "scripts\apply_patches.ps1") -Destination (Join-Path $packageRoot "scripts")
Copy-Item -LiteralPath (Join-Path $root "scripts\runtime_meter.py") -Destination (Join-Path $packageRoot "scripts")
Copy-Item -LiteralPath (Join-Path $root "scripts\ai_rewriter.py") -Destination (Join-Path $packageRoot "scripts")
Copy-Item -LiteralPath (Join-Path $root "scripts\transcript_history.py") -Destination (Join-Path $packageRoot "scripts")
Copy-Item -LiteralPath (Join-Path $root "scripts\text_corrections.py") -Destination (Join-Path $packageRoot "scripts")
Copy-Item -LiteralPath (Join-Path $root "scripts\slang_retry.py") -Destination (Join-Path $packageRoot "scripts")
Copy-Item -LiteralPath (Join-Path $root "scripts\decoding_options.py") -Destination (Join-Path $packageRoot "scripts")
Copy-Item -LiteralPath (Join-Path $root "scripts\buffered_transcription.py") -Destination (Join-Path $packageRoot "scripts")
Copy-Item -LiteralPath (Join-Path $root "scripts\output_mode.py") -Destination (Join-Path $packageRoot "scripts")
[System.IO.File]::WriteAllText((Join-Path $packageRoot "version.txt"), "$Version`r`n", [System.Text.UTF8Encoding]::new($false))

Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zip -CompressionLevel Optimal
$hashLines = @($zip, $standaloneExe) | ForEach-Object {
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_).Hash.ToLowerInvariant()
    "$hash  $([System.IO.Path]::GetFileName($_))"
}
[System.IO.File]::WriteAllLines($checksums, $hashLines, [System.Text.UTF8Encoding]::new($false))

Write-Output "Release assets:"
Get-Item -LiteralPath $zip, $standaloneExe, $checksums | Select-Object Name, Length, FullName
