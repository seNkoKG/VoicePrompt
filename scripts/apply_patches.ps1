# Applies the local Windows fixes to faster-whisper-dictation site-packages.
# Idempotent: safe to run repeatedly. Run AFTER any reinstall/upgrade of the pip package.
# Usage:  powershell -ExecutionPolicy Bypass -File scripts\apply_patches.ps1
param(
    [string]$Site = "$env:USERPROFILE\.voice-typing\venv\Lib\site-packages\whisper_dictation",
    [string]$RunnerTarget = "$env:USERPROFILE\.voice-typing\run_daemon.pyw"
)

$ErrorActionPreference = "Stop"
$site = $Site
if (-not (Test-Path $site)) { Write-Error "site-packages not found: $site"; exit 1 }

function Apply-Patch($path, $find, $replace, $name, $marker = $null) {
    $content = [System.IO.File]::ReadAllText($path)
    $patchedMarker = if ($marker) { $marker } else { $replace }
    if ($content.Contains($patchedMarker)) {
        Write-Output "[SKIPPED ] $name (already patched)"
    } elseif ($content.Contains($find)) {
        $content = $content.Replace($find, $replace)
        [System.IO.File]::WriteAllText($path, $content, (New-Object System.Text.UTF8Encoding($false)))
        Write-Output "[PATCHED ] $name"
    } else {
        Write-Error "$name -- expected source not found; installed package changed"
    }
}

$meterSource = Join-Path $PSScriptRoot "runtime_meter.py"
Copy-Item -LiteralPath $meterSource -Destination "$site\meter.py" -Force
Write-Output "[SYNCED  ] meter.py -- recording state and audio levels"
$runnerSource = Join-Path (Split-Path -Parent $PSScriptRoot) "run_daemon.pyw"
Copy-Item -LiteralPath $runnerSource -Destination $runnerTarget -Force
Write-Output "[SYNCED  ] run_daemon.pyw -- launcher settings"

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
            Write-Error "cli.py -- anchor 'def _cleanup_pid' not found"
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
'@ "engine/local.py -- detected-language logging" 'Detected language: %s (conf %.2f) [%d segments]'

# 5. Local engine - honor UI recognition and VAD settings
Apply-Patch "$site\engine\local.py" @'
from ..config import EngineConfig, ServerConfig
'@ @'
from ..config import EngineConfig, ServerConfig, VADConfig
'@ "engine/local.py -- VAD config import"
Apply-Patch "$site\engine\local.py" @'
    def __init__(self, server_config: ServerConfig, engine_config: EngineConfig):
'@ @'
    def __init__(
        self,
        server_config: ServerConfig,
        engine_config: EngineConfig,
        vad_config: VADConfig,
    ):
'@ "engine/local.py -- VAD constructor"
Apply-Patch "$site\engine\local.py" @'
        self._device = engine_config.device
'@ @'
        self._device = engine_config.device
        self._prompt = server_config.prompt
        self._temperature = server_config.temperature
        self._hotwords = server_config.hotwords
        self._vad_parameters = {
            "threshold": vad_config.threshold,
            "min_silence_duration_ms": vad_config.silence_ms,
            "min_speech_duration_ms": vad_config.min_speech_ms,
            "max_speech_duration_s": vad_config.max_speech_s,
        }
'@ "engine/local.py -- recognition settings"
Apply-Patch "$site\engine\local.py" @'
        segments, info = self._model.transcribe(
            audio,
            language=self._language,
            vad_filter=True,
        )
'@ @'
        segments, info = self._model.transcribe(
            audio,
            language=self._language,
            temperature=self._temperature,
            initial_prompt=self._prompt or None,
            hotwords=self._hotwords or None,
            vad_filter=True,
            vad_parameters=self._vad_parameters,
        )
'@ "engine/local.py -- pass recognition settings"
Apply-Patch "$site\engine\__init__.py" @'
        return LocalEngine(config.server, config.engine)
'@ @'
        return LocalEngine(config.server, config.engine, config.vad)
'@ "engine/__init__.py -- pass VAD settings"

# 6. Daemon - publish recording state and live microphone level to the tray overlay
Apply-Patch "$site\daemon.py" @'
from .notifier import notify
'@ @'
from .notifier import notify
from .meter import publish_level, publish_state
'@ "daemon.py -- meter import"
Apply-Patch "$site\daemon.py" @'
        self._recording = False
        self._recording_start: float = 0.0
'@ @'
        self._recording = False
        publish_state(False)
        self._recording_start: float = 0.0
'@ "daemon.py -- initialize meter"
Apply-Patch "$site\daemon.py" @'
        if not self._recording:
            return

        ws_engine = self._ws_engine  # snapshot to avoid race with deactivate
'@ @'
        if not self._recording:
            return

        publish_level(audio)
        ws_engine = self._ws_engine  # snapshot to avoid race with deactivate
'@ "daemon.py -- publish audio level"
Apply-Patch "$site\daemon.py" @'
        with self._lock:
            self._audio = audio
        notify("Recording", "Speak now...")
'@ @'
        with self._lock:
            self._audio = audio
        publish_state(True)
        notify("Recording", "Speak now...")
'@ "daemon.py -- publish recording start"
Apply-Patch "$site\daemon.py" @'
        if audio is not None:
            audio.stop()

        if self._use_ws and ws_engine is not None:
'@ @'
        if audio is not None:
            audio.stop()
        publish_state(False)

        if self._use_ws and ws_engine is not None:
'@ "daemon.py -- publish recording stop"

# 7. config.py - allow single keys (letters, digits, f1-f24, named keys like space/enter)
Apply-Patch "$site\config.py" @'
_HOTKEY_MODIFIERS = {"alt", "ctrl", "control", "shift", "cmd", "super", "meta"}
'@ @'
_HOTKEY_MODIFIERS = {"alt", "ctrl", "control", "shift", "cmd", "super", "meta"}
_HOTKEY_NAMED_KEYS = frozenset(
    {
        "space", "tab", "enter", "esc", "backspace", "insert", "delete",
        "home", "end", "page_up", "page_down", "up", "down", "left", "right",
        "print_screen", "pause", "caps_lock", "scroll_lock", "num_lock", "menu",
    }
)
'@ "config.py -- named keys set"
Apply-Patch "$site\config.py" @'
    key = parts[-1]
    modifiers = parts[:-1]
    single_letter = key.isalpha() and len(key) == 1
    function_key = re.fullmatch(r"f(?:[1-9]|1[0-9]|2[0-4])", key) is not None
    return (single_letter or function_key) and all(mod in _HOTKEY_MODIFIERS for mod in modifiers)
'@ @'
    key = parts[-1]
    modifiers = parts[:-1]
    if not all(mod in _HOTKEY_MODIFIERS for mod in modifiers):
        return False
    if len(key) == 1 and (key.isalpha() or key.isdigit()):
        return True
    if key in _HOTKEY_NAMED_KEYS or re.fullmatch(r"f(?:[1-9]|1[0-9]|2[0-4])", key):
        return True
    return False
'@ "config.py -- single/multi-key validation"

# 8. Windows hotkey listener - consume only the configured hotkey so apps never receive it
$listener = "$site\hotkey\listener.py"
Apply-Patch $listener @'
import os
import sys
'@ @'
import os
import queue
import sys
'@ "hotkey/listener.py -- event queue import"
Apply-Patch $listener @'
        pressed_modifiers: set = set()

        def _key_name(key) -> str:
'@ @'
        pressed_modifiers: set = set()
        win32_event_filter = None

        if sys.platform == "win32":
            import ctypes

            required_win_vks = {
                mod: {
                    key.value.vk
                    for key in modifier_map.get(mod, set())
                    if key.value.vk is not None
                }
                for mod in self._modifiers
            }
            modifier_vks = set().union(*required_win_vks.values())
            pressed_win_vks: set[int] = set()
            named_target = getattr(keyboard.Key, self._key, None)
            if named_target is not None:
                target_vk = named_target.value.vk
            else:
                vk_key_scan = ctypes.windll.user32.VkKeyScanW
                vk_key_scan.argtypes = [ctypes.c_wchar]
                vk_key_scan.restype = ctypes.c_short
                target_vk = vk_key_scan(self._key) & 0xFF

            events: queue.SimpleQueue[str] = queue.SimpleQueue()
            suppressing_target = False
            press_messages = {0x0100, 0x0104}
            release_messages = {0x0101, 0x0105}

            def dispatch_win32_events() -> None:
                while not self._stop_event.is_set():
                    try:
                        action = events.get(timeout=0.2)
                    except queue.Empty:
                        continue
                    if action == "press":
                        self._handle_press()
                    else:
                        self._handle_release()

            self._thread = threading.Thread(target=dispatch_win32_events, daemon=True)
            self._thread.start()

            def win32_modifiers_held() -> bool:
                return all(pressed_win_vks & keys for keys in required_win_vks.values())

            def win32_event_filter(msg, data):
                nonlocal suppressing_target

                if int(data.dwExtraInfo or 0) == 0x56505459:
                    return False

                vk = int(data.vkCode)
                is_press = msg in press_messages
                is_release = msg in release_messages
                if vk in modifier_vks:
                    if is_press:
                        pressed_win_vks.add(vk)
                    elif is_release:
                        pressed_win_vks.discard(vk)
                        if suppressing_target and self.mode == "hold":
                            events.put("release")

                if vk != target_vk:
                    return True
                if is_press and suppressing_target:
                    self._listener.suppress_event()
                if is_press and win32_modifiers_held():
                    if not suppressing_target:
                        suppressing_target = True
                        events.put("press")
                    self._listener.suppress_event()
                if is_release and suppressing_target:
                    suppressing_target = False
                    with self._lock:
                        self._key_released = True
                    if self.mode == "hold":
                        events.put("release")
                    self._listener.suppress_event()
                return True

        def _key_name(key) -> str:
'@ "hotkey/listener.py -- selective Windows hotkey filter" 'def win32_event_filter(msg, data):'
Apply-Patch $listener @'
                if data.flags & 0x10:
                    return False
'@ @'
                if int(data.dwExtraInfo or 0) == 0x56505459:
                    return False
'@ "hotkey/listener.py -- ignore only VoicePrompt paste events"
Apply-Patch $listener @'
                if vk != target_vk:
                    return True
                if is_press and win32_modifiers_held():
'@ @'
                if vk != target_vk:
                    return True
                if is_press and suppressing_target:
                    self._listener.suppress_event()
                if is_press and win32_modifiers_held():
'@ "hotkey/listener.py -- suppress held combo repeats"
Apply-Patch $listener @'
        self._listener = keyboard.Listener(on_press=on_press, on_release=on_release)
'@ @'
        self._listener = keyboard.Listener(
            on_press=on_press,
            on_release=on_release,
            win32_event_filter=win32_event_filter,
        )
'@ "hotkey/listener.py -- enable selective Windows filter"

Apply-Patch $typer @'
_KEYEVENTF_KEYUP = 0x0002
'@ @'
_KEYEVENTF_KEYUP = 0x0002
_VOICEPROMPT_INJECTED = 0x56505459
'@ "typer.py -- injected input marker"
Apply-Patch $typer @'
    user32 = ctypes.windll.user32
    user32.keybd_event(_VK_CONTROL, 0, 0, 0)
    user32.keybd_event(_VK_V, 0, 0, 0)
    user32.keybd_event(_VK_V, 0, _KEYEVENTF_KEYUP, 0)
    user32.keybd_event(_VK_CONTROL, 0, _KEYEVENTF_KEYUP, 0)
'@ @'
    user32 = ctypes.windll.user32
    user32.keybd_event.argtypes = [
        ctypes.c_ubyte, ctypes.c_ubyte, ctypes.c_uint, ctypes.c_size_t,
    ]
    user32.keybd_event.restype = None
    user32.keybd_event(_VK_CONTROL, 0, 0, _VOICEPROMPT_INJECTED)
    user32.keybd_event(_VK_V, 0, 0, _VOICEPROMPT_INJECTED)
    user32.keybd_event(_VK_V, 0, _KEYEVENTF_KEYUP, _VOICEPROMPT_INJECTED)
    user32.keybd_event(_VK_CONTROL, 0, _KEYEVENTF_KEYUP, _VOICEPROMPT_INJECTED)
'@ "typer.py -- mark injected paste events"

Write-Output "`nAll patches applied. Restart the daemon: Stop Voice Typing -> Start Voice Typing"
