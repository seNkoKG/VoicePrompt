$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$installer = Join-Path $root "install.ps1"

$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    $installer,
    [ref]$tokens,
    [ref]$parseErrors
)
if ($parseErrors) {
    throw "install.ps1 does not parse."
}
$functionAst = $ast.Find({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq "Stop-VoicePromptRuntime"
}, $true)
if (-not $functionAst) {
    throw "Stop-VoicePromptRuntime was not found in install.ps1."
}
Invoke-Expression $functionAst.Extent.Text

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("voiceprompt-stop-" + [guid]::NewGuid().ToString("N"))
$resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$resolvedTest = [System.IO.Path]::GetFullPath($testRoot)
if (-not $resolvedTest.StartsWith($resolvedTemp, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Installer-stop test root escaped the temporary directory."
}

$managedProcess = $null
$unmanagedProcess = $null
try {
    $testPython = (Get-Command "python.exe" -ErrorAction Stop).Source
    $managedRoot = Split-Path -Parent $testPython
    if (-not (Test-Path -LiteralPath $testPython)) {
        throw "The Python test runtime is unavailable: $testPython"
    }

    $fakeLocal = Join-Path $testRoot "local"
    $configRoot = Join-Path $fakeLocal "faster-whisper-dictation\faster-whisper-dictation"
    New-Item -ItemType Directory -Path $configRoot -Force | Out-Null
    $pidFile = Join-Path $configRoot "daemon.pid"

    $managedProcess = Start-Process -FilePath $testPython `
        -ArgumentList '-c', '"import time; time.sleep(60)"', 'run_daemon.pyw' `
        -WindowStyle Hidden -PassThru
    [System.IO.File]::WriteAllText($pidFile, [string]$managedProcess.Id)
    Stop-VoicePromptRuntime `
        -DaemonExe $testPython `
        -VenvRoot $managedRoot `
        -LocalAppData $fakeLocal
    $managedProcess.Refresh()
    if (-not $managedProcess.HasExited -or (Test-Path -LiteralPath $pidFile)) {
        throw "The verified managed runtime fallback was not stopped and cleaned."
    }

    $unmanagedProcess = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
        -ArgumentList '-NoProfile', '-Command', 'Start-Sleep -Seconds 60 # run_daemon.pyw' `
        -WindowStyle Hidden -PassThru
    [System.IO.File]::WriteAllText($pidFile, [string]$unmanagedProcess.Id)
    $refused = $false
    try {
        Stop-VoicePromptRuntime `
            -DaemonExe $testPython `
            -VenvRoot $managedRoot `
            -LocalAppData $fakeLocal
    } catch {
        $refused = $_.Exception.Message -match "does not belong to VoicePrompt"
    }
    $unmanagedProcess.Refresh()
    if (-not $refused -or $unmanagedProcess.HasExited) {
        throw "The installer did not fail closed for an unmanaged process."
    }

    Write-Output "INSTALLER_RUNTIME_STOP_GATE=PASS"
} finally {
    foreach ($process in @($managedProcess, $unmanagedProcess)) {
        if ($process) {
            $process.Refresh()
            if (-not $process.HasExited) {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            }
            $process.Dispose()
        }
    }
    if (Test-Path -LiteralPath $resolvedTest) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force
    }
}

# The fallback intentionally runs a failing native command. PowerShell 7 can
# otherwise propagate that stale native exit code even after every assertion passes.
exit 0
