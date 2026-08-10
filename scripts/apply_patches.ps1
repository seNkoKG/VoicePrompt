# Applies the local Windows fixes to faster-whisper-dictation site-packages.
# Idempotent: safe to run repeatedly. Run AFTER any reinstall/upgrade of the pip package.
# Usage:  powershell -ExecutionPolicy Bypass -File scripts\apply_patches.ps1
$ErrorActionPreference = "Stop"
$site = "$env:USERPROFILE\.voice-typing\venv\Lib\site-packages\whisper_dictation"
if (-not (Test-Path $site)) { Write-Error "site-packages not found: $site"; exit 1 }

function Apply-Patch($path, $find, $replace, $name) {
    $content = [System.IO.File]::ReadAllText($path)
    if ($content.Contains($find)) {
        $content = $content.Replace($find, $replace)
        [System.IO.File]::WriteAllText($path, $content, (New-Object System.Text.UTF8Encoding($false)))
        Write-Output "[PATCHED ] $name"
    } else {
        Write-Output "[SKIPPED ] $name (already patched or file changed)"
    }
}

# 1. cli.py - Windows-safe _pid_alive() (os.kill(pid, 0) raises WinError 87 on Win32)
$cli = "$site\cli.py"
$cliContent = [System.IO.File]::ReadAllText($cli)
if ($cliContent -match "def _pid_alive" -and $cliContent -match "OpenProcess") {
    Write-Output "[SKIPPED ] cli.py -- pid_alive already patched"
} else {
    $fn = @'
def _pid_alive(pid: int) -> bool:
    """Return True if the process with the given PID exists (Windows-safe)."""
    if sys.platform == "win32":
        import ctypes

        PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
        h = ctypes.windll.kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
        if not h:
            return False
        ctypes.windll.kernel32.CloseHandle(h)
        return True
    try:
        os.kill(pid, 0)
        return True
    except ProcessLookupError:
        return False
    except PermissionError:
        return True


'@
    if ($cliContent.Contains("def _pid_alive")) {
        # function exists but is not Windows-safe (e.g. reinstall restored original) -> replace body
        $replace = $cliContent -replace "(?s)def _pid_alive\(pid: int\) -> bool:.*?(?=\n\ndef )", ($fn.TrimEnd() + "`n")
        [System.IO.File]::WriteAllText($cli, $replace, (New-Object System.Text.UTF8Encoding($false)))
        Write-Output "[PATCHED ] cli.py -- pid_alive (replaced body)"
    } else {
        $anchor = "def _cleanup_pid"
        if ($cliContent.Contains($anchor)) {
            $cliContent = $cliContent.Replace($anchor, $fn + $anchor)
            [System.IO.File]::WriteAllText($cli, $cliContent, (New-Object System.Text.UTF8Encoding($false)))
            Write-Output "[PATCHED ] cli.py -- pid_alive (inserted)"
        } else {
            Write-Output "[ERROR  ] cli.py -- anchor 'def _cleanup_pid' not found"
        }
    }
}

# 2. typer.py - 64-bit clipboard HANDLE argtypes/restype (truncated 32-bit handles -> access violations)
$typer = "$site\typer.py"
Apply-Patch $typer "    user32 = ctypes.windll.user32`n    kernel32 = ctypes.windll.kernel32`n" @'
    user32 = ctypes.windll.user32
    kernel32 = ctypes.windll.kernel32
    user32.OpenClipboard.argtypes = [ctypes.c_void_p]
    user32.OpenClipboard.restype = ctypes.c_int
    user32.GetClipboardData.argtypes = [ctypes.c_uint]
    user32.GetClipboardData.restype = ctypes.c_void_p
    user32.CloseClipboard.argtypes = []
    user32.CloseClipboard.restype = ctypes.c_int
    user32.EmptyClipboard.argtypes = []
    user32.EmptyClipboard.restype = ctypes.c_int
    user32.SetClipboardData.argtypes = [ctypes.c_uint, ctypes.c_void_p]
    user32.SetClipboardData.restype = ctypes.c_void_p
    kernel32.GlobalAlloc.argtypes = [ctypes.c_uint, ctypes.c_size_t]
    kernel32.GlobalAlloc.restype = ctypes.c_void_p
    kernel32.GlobalLock.argtypes = [ctypes.c_void_p]
    kernel32.GlobalLock.restype = ctypes.c_void_p
    kernel32.GlobalUnlock.argtypes = [ctypes.c_void_p]
    kernel32.GlobalUnlock.restype = ctypes.c_int
    kernel32.GlobalFree.argtypes = [ctypes.c_void_p]
    kernel32.GlobalFree.restype = ctypes.c_void_p

'@ "typer.py -- clipboard HANDLE fixes"

# 3. engine/local.py - "" / "auto" maps to None (true auto-detect); faster-whisper rejects "auto"
Apply-Patch "$site\engine\local.py" @'
        self._language = server_config.language
'@ @'
        self._language = (
            None if not server_config.language or server_config.language == "auto"
            else server_config.language
        )
'@ "engine/local.py -- auto-detect mapping"

# 4. engine/local.py - log detected language + confidence per utterance (debugging auto-detect)
Apply-Patch "$site\engine\local.py" @'
        segments, _ = self._model.transcribe(
            audio,
            language=self._language,
            vad_filter=True,
        )

        text = " ".join(seg.text.strip() for seg in segments).strip()
'@ @'
        segments, info = self._model.transcribe(
            audio,
            language=self._language,
            vad_filter=True,
        )
        segments = list(segments)  # generator -> list (len() + reuse)

        log.info(
            "Detected language: %s (conf %.2f) [%d segments]",
            info.language,
            info.language_probability,
            len(segments),
        )
        text = " ".join(seg.text.strip() for seg in segments).strip()
'@ "engine/local.py -- detected-language logging"

Write-Output "`nAll patches applied. Restart the daemon: Stop Voice Typing -> Start Voice Typing"