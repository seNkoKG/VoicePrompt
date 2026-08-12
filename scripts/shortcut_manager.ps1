function Test-VoicePromptLegacyShortcut([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }
    try {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($Path)
        $targetName = [System.IO.Path]::GetFileName($shortcut.TargetPath)
        $arguments = [string]$shortcut.Arguments
        $icon = [string]$shortcut.IconLocation
        return (
            $targetName -ieq "VoicePromptTray.exe" -or
            ($targetName -ieq "pythonw.exe" -and $arguments -match '(?i)run_daemon\.pyw') -or
            ($targetName -in @("wscript.exe", "cscript.exe") -and $arguments -match '(?i)stop_voice_typing\.vbs') -or
            $icon -match '(?i)voiceprompt(?:\.ico|tray\.exe)'
        )
    } catch {
        return $false
    }
}

function Install-VoicePromptShortcuts(
    [string]$InstalledExe,
    [string]$InstallRoot,
    [string]$DesktopDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory),
    [string]$ProgramsDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs),
    [string]$StartupDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
) {
    foreach ($directory in @($DesktopDirectory, $ProgramsDirectory, $StartupDirectory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $legacyNames = @("Voice Typing Settings.lnk", "Start Voice Typing.lnk", "Stop Voice Typing.lnk")
    foreach ($directory in @($DesktopDirectory, $ProgramsDirectory)) {
        $resolvedDirectory = [System.IO.Path]::GetFullPath($directory).TrimEnd('\')
        foreach ($name in $legacyNames) {
            $legacyPath = [System.IO.Path]::GetFullPath((Join-Path $resolvedDirectory $name))
            if ([System.IO.Path]::GetDirectoryName($legacyPath) -ne $resolvedDirectory) {
                throw "Refusing to inspect a legacy shortcut outside its intended directory: $legacyPath"
            }
            if (Test-VoicePromptLegacyShortcut $legacyPath) {
                Remove-Item -LiteralPath $legacyPath -Force
                Write-Output "Removed legacy shortcut: $legacyPath"
            }
        }
    }

    $shell = New-Object -ComObject WScript.Shell

    $canonicalStartup = Join-Path $StartupDirectory "VoicePrompt.lnk"
    $legacyStartup = Join-Path $StartupDirectory "Voice Typing (faster-whisper-dictation).lnk"
    $canonicalStartupOwned = Test-VoicePromptLegacyShortcut $canonicalStartup
    $legacyStartupOwned = Test-VoicePromptLegacyShortcut $legacyStartup
    $startupEnabled = $canonicalStartupOwned -or $legacyStartupOwned
    if ($legacyStartupOwned) {
        Remove-Item -LiteralPath $legacyStartup -Force
        Write-Output "Removed legacy shortcut: $legacyStartup"
    }

    foreach ($shortcutPath in @(
        (Join-Path $DesktopDirectory "VoicePrompt.lnk"),
        (Join-Path $ProgramsDirectory "VoicePrompt.lnk")
    )) {
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $InstalledExe
        $shortcut.WorkingDirectory = $InstallRoot
        $shortcut.IconLocation = "$InstalledExe,0"
        $shortcut.Description = "VoicePrompt voice typing"
        $shortcut.Save()
    }

    if ($startupEnabled) {
        if ((Test-Path -LiteralPath $canonicalStartup) -and -not $canonicalStartupOwned) {
            Write-Warning "A non-VoicePrompt shortcut already uses the startup name VoicePrompt.lnk; it was left unchanged."
        } else {
            $shortcut = $shell.CreateShortcut($canonicalStartup)
            $shortcut.TargetPath = $InstalledExe
            $shortcut.Arguments = "--tray"
            $shortcut.WorkingDirectory = $InstallRoot
            $shortcut.IconLocation = "$InstalledExe,0"
            $shortcut.Description = "Start VoicePrompt with Windows"
            $shortcut.Save()
        }
    }
}
