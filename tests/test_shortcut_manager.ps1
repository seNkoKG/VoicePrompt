$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $root "scripts\shortcut_manager.ps1")

$installerSource = [System.IO.File]::ReadAllText((Join-Path $root "install.ps1"))
$packagerSource = [System.IO.File]::ReadAllText((Join-Path $root "scripts\package_release.ps1"))
if (-not $installerSource.Contains('$packageShortcutManager') -or
    -not $installerSource.Contains('Install-VoicePromptShortcuts') -or
    -not $packagerSource.Contains('scripts\shortcut_manager.ps1')) {
    throw "The release package or installer does not include shortcut migration."
}
if (-not $installerSource.Contains('function Stop-VoicePromptRuntime') -or
    -not $installerSource.Contains("run_daemon\.pyw") -or
    -not $installerSource.Contains("refusing to stop process")) {
    throw "The installer does not have a verified fallback for an unpatched Windows runtime."
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("voiceprompt-shortcuts-" + [guid]::NewGuid().ToString("N"))
$resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$resolvedTest = [System.IO.Path]::GetFullPath($testRoot)
if (-not $resolvedTest.StartsWith($resolvedTemp, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Shortcut test root escaped the temporary directory."
}

try {
    $desktop = Join-Path $testRoot "desktop"
    $programs = Join-Path $testRoot "programs"
    $startup = Join-Path $testRoot "startup"
    $installRoot = Join-Path $testRoot "installed"
    $runtime = Join-Path $testRoot "runtime"
    New-Item -ItemType Directory -Path $desktop, $programs, $startup, $installRoot, $runtime -Force | Out-Null
    $installedExe = Join-Path $installRoot "VoicePromptTray.exe"
    $pythonw = Join-Path $runtime "pythonw.exe"
    Copy-Item -LiteralPath "$env:SystemRoot\System32\notepad.exe" -Destination $installedExe
    Copy-Item -LiteralPath "$env:SystemRoot\System32\notepad.exe" -Destination $pythonw

    $shell = New-Object -ComObject WScript.Shell
    $settings = $shell.CreateShortcut((Join-Path $desktop "Voice Typing Settings.lnk"))
    $settings.TargetPath = $installedExe
    $settings.IconLocation = "$installedExe,0"
    $settings.Save()
    $start = $shell.CreateShortcut((Join-Path $desktop "Start Voice Typing.lnk"))
    $start.TargetPath = $pythonw
    $start.Arguments = '"C:\Users\example\.voice-typing\run_daemon.pyw"'
    $start.Save()
    $unrelated = $shell.CreateShortcut((Join-Path $desktop "Stop Voice Typing.lnk"))
    $unrelated.TargetPath = "$env:SystemRoot\System32\notepad.exe"
    $unrelated.Save()
    $legacyStartup = $shell.CreateShortcut((Join-Path $startup "Voice Typing (faster-whisper-dictation).lnk"))
    $legacyStartup.TargetPath = $installedExe
    $legacyStartup.Arguments = "--tray"
    $legacyStartup.Save()

    Install-VoicePromptShortcuts `
        -InstalledExe $installedExe `
        -InstallRoot $installRoot `
        -DesktopDirectory $desktop `
        -ProgramsDirectory $programs `
        -StartupDirectory $startup

    if (Test-Path -LiteralPath (Join-Path $desktop "Voice Typing Settings.lnk")) {
        throw "Owned legacy settings shortcut was not removed."
    }
    if (Test-Path -LiteralPath (Join-Path $desktop "Start Voice Typing.lnk")) {
        throw "Owned legacy runtime shortcut was not removed."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $desktop "Stop Voice Typing.lnk"))) {
        throw "Unrelated same-name shortcut was removed."
    }
    if (Test-Path -LiteralPath (Join-Path $startup "Voice Typing (faster-whisper-dictation).lnk")) {
        throw "Owned legacy startup shortcut was not removed."
    }

    foreach ($canonicalPath in @(
        (Join-Path $desktop "VoicePrompt.lnk"),
        (Join-Path $programs "VoicePrompt.lnk")
    )) {
        $canonical = $shell.CreateShortcut($canonicalPath)
        if ($canonical.TargetPath -ne $installedExe -or
            $canonical.WorkingDirectory -ne $installRoot -or
            $canonical.IconLocation -ne "$installedExe,0") {
            throw "Canonical shortcut does not target the installed application and icon exactly."
        }
    }

    $startupShortcut = $shell.CreateShortcut((Join-Path $startup "VoicePrompt.lnk"))
    if ($startupShortcut.TargetPath -ne $installedExe -or
        $startupShortcut.Arguments -ne "--tray" -or
        $startupShortcut.WorkingDirectory -ne $installRoot) {
        throw "Canonical startup shortcut does not preserve auto-start with the installed application."
    }

    Install-VoicePromptShortcuts `
        -InstalledExe $installedExe `
        -InstallRoot $installRoot `
        -DesktopDirectory $desktop `
        -ProgramsDirectory $programs `
        -StartupDirectory $startup
    if (-not (Test-Path -LiteralPath (Join-Path $desktop "Stop Voice Typing.lnk"))) {
        throw "Repeated migration removed an unrelated shortcut."
    }
    Write-Output "SHORTCUT_MIGRATION_GATE=PASS"
} finally {
    if (Test-Path -LiteralPath $resolvedTest) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force
    }
}
