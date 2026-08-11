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
    # Release archives are built on Windows (CRLF) while an existing Python
    # environment may contain LF files. Compare normalized text so upgrades do
    # not mistake an already-patched multiline block for missing source.
    $normalizedContent = $content.Replace("`r`n", "`n").Replace("`r", "`n")
    $normalizedFind = ([string]$find).Replace("`r`n", "`n").Replace("`r", "`n")
    $normalizedReplace = ([string]$replace).Replace("`r`n", "`n").Replace("`r", "`n")
    $patchedMarkers = if ($marker) { @($marker) } else { @($replace) }
    $alreadyPatched = @($patchedMarkers | Where-Object {
        $normalizedMarker = ([string]$_).Replace("`r`n", "`n").Replace("`r", "`n")
        $normalizedContent.Contains($normalizedMarker)
    }).Count -gt 0
    if ($alreadyPatched) {
        Write-Output "[SKIPPED ] $name (already patched)"
    } elseif ($normalizedContent.Contains($normalizedFind)) {
        $normalizedContent = $normalizedContent.Replace($normalizedFind, $normalizedReplace)
        [System.IO.File]::WriteAllText($path, $normalizedContent, (New-Object System.Text.UTF8Encoding($false)))
        Write-Output "[PATCHED ] $name"
    } else {
        Write-Error "$name -- expected source not found; installed package changed"
    }
}

function Remove-Patch($path, $remove, $name) {
    $content = [System.IO.File]::ReadAllText($path)
    $normalizedContent = $content.Replace("`r`n", "`n").Replace("`r", "`n")
    $normalizedRemove = ([string]$remove).Replace("`r`n", "`n").Replace("`r", "`n")
    if ($normalizedContent.Contains($normalizedRemove)) {
        $normalizedContent = $normalizedContent.Replace($normalizedRemove, "")
        [System.IO.File]::WriteAllText($path, $normalizedContent, (New-Object System.Text.UTF8Encoding($false)))
        Write-Output "[REMOVED ] $name"
    } else {
        Write-Output "[SKIPPED ] $name (not present)"
    }
}

function Replace-Block($path, $start, $end, $replacement, $name) {
    $content = [System.IO.File]::ReadAllText($path)
    $normalizedContent = $content.Replace("`r`n", "`n").Replace("`r", "`n")
    $startIndex = $normalizedContent.IndexOf([string]$start, [System.StringComparison]::Ordinal)
    if ($startIndex -lt 0) {
        Write-Error "$name -- start anchor not found; installed package changed"
    }
    $endIndex = $normalizedContent.IndexOf(
        [string]$end,
        $startIndex + ([string]$start).Length,
        [System.StringComparison]::Ordinal
    )
    if ($endIndex -lt 0) {
        Write-Error "$name -- end anchor not found; installed package changed"
    }

    $canonical = ([string]$replacement).Replace("`r`n", "`n").Replace("`r", "`n").TrimEnd() + "`n`n"
    $updated = $normalizedContent.Substring(0, $startIndex) + $canonical + $normalizedContent.Substring($endIndex)
    if ($updated -ceq $normalizedContent) {
        Write-Output "[SKIPPED ] $name (already canonical)"
        return
    }
    [System.IO.File]::WriteAllText($path, $updated, (New-Object System.Text.UTF8Encoding($false)))
    Write-Output "[NORMALIZED] $name"
}

$meterSource = Join-Path $PSScriptRoot "runtime_meter.py"
Copy-Item -LiteralPath $meterSource -Destination "$site\meter.py" -Force
Write-Output "[SYNCED  ] meter.py -- recording state and audio levels"
$aiSource = Join-Path $PSScriptRoot "ai_rewriter.py"
Copy-Item -LiteralPath $aiSource -Destination "$site\ai_rewriter.py" -Force
Write-Output "[SYNCED  ] ai_rewriter.py -- optional transcript cleanup"
$slangRetrySource = Join-Path $PSScriptRoot "slang_retry.py"
Copy-Item -LiteralPath $slangRetrySource -Destination "$site\slang_retry.py" -Force
Write-Output "[SYNCED  ] slang_retry.py -- mixed English/Slovenian routing"
$decodingOptionsSource = Join-Path $PSScriptRoot "decoding_options.py"
Copy-Item -LiteralPath $decodingOptionsSource -Destination "$site\decoding_options.py" -Force
Write-Output "[SYNCED  ] decoding_options.py -- latency-bounded Whisper decoding"
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

'@ "typer.py -- clipboard HANDLE fixes" 'def _win_clipboard_api():'

# 3. engine/local.py - "" / "auto" maps to None (true auto-detect); faster-whisper rejects "auto"
Apply-Patch "$site\engine\local.py" @'
        self._language = server_config.language
'@ @'
        self._language = (
            None if not server_config.language or server_config.language == "auto"
            else server_config.language
        )
'@ "engine/local.py -- auto-detect mapping" @(
    'None if not server_config.language or server_config.language == "auto"',
    'self._language_mode = server_config.language'
)
Apply-Patch "$site\engine\local.py" @'
        self._language = (
            None if not server_config.language or server_config.language == "auto"
            else server_config.language
        )
'@ @'
        self._language_mode = server_config.language
        self._language = recognition_language(server_config.language)
'@ "engine/local.py -- hybrid language mapping" 'self._language_mode = server_config.language'

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
        segments = list(segments)

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
from ..config import EngineConfig, ServerConfig, VADConfig
'@ @'
from ..config import EngineConfig, ServerConfig, VADConfig
from ..slang_retry import recognition_language, should_retry_as_slovenian
'@ "engine/local.py -- hybrid language helpers" 'recognition_language,'
Apply-Patch "$site\engine\local.py" @'
from ..slang_retry import recognition_language, should_retry_as_slovenian
'@ @'
from ..slang_retry import (
    bilingual_retry_hotwords,
    bilingual_retry_language,
    bilingual_retry_prompt,
    prefer_bilingual_retry,
    recognition_hotwords,
    recognition_language,
    recognition_prompt,
    transcript_score,
)
'@ "engine/local.py -- bilingual Auto helpers" 'prefer_bilingual_retry'
Apply-Patch "$site\engine\local.py" @'
from ..config import EngineConfig, ServerConfig, VADConfig
from ..slang_retry import (
    bilingual_retry_hotwords,
    bilingual_retry_language,
    bilingual_retry_prompt,
    prefer_bilingual_retry,
    recognition_hotwords,
    recognition_language,
    recognition_prompt,
    transcript_score,
)
'@ @'
from ..config import EngineConfig, ServerConfig, VADConfig
from ..decoding_options import decoding_options
from ..slang_retry import (
    bilingual_retry_hotwords,
    bilingual_retry_language,
    bilingual_retry_prompt,
    prefer_bilingual_retry,
    recognition_hotwords,
    recognition_language,
    recognition_prompt,
    transcript_score,
)
'@ "engine/local.py -- safe decoding options" 'from ..decoding_options import decoding_options'
Apply-Patch "$site\engine\local.py" @'
import logging
import threading
'@ @'
import logging
import threading
import time
'@ "engine/local.py -- latency clock" 'import time'
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
        self._prompt = recognition_prompt(server_config.language, server_config.prompt)
        self._temperature = server_config.temperature
        self._hotwords = recognition_hotwords(server_config.language, server_config.hotwords)
        self._vad_parameters = {
            "threshold": vad_config.threshold,
            "min_silence_duration_ms": vad_config.silence_ms,
            "min_speech_duration_ms": vad_config.min_speech_ms,
            "max_speech_duration_s": vad_config.max_speech_s,
        }
'@ "engine/local.py -- recognition settings" 'recognition_prompt(server_config.language'
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
'@ "engine/local.py -- pass recognition settings" 'initial_prompt=self._prompt or None'
Apply-Patch "$site\engine\__init__.py" @'
        return LocalEngine(config.server, config.engine)
'@ @'
        return LocalEngine(config.server, config.engine, config.vad)
'@ "engine/__init__.py -- pass VAD settings"
Apply-Patch "$site\engine\local.py" @'
        segments = list(segments)

        log.info(
'@ @'
        segments = list(segments)
        if should_retry_as_slovenian(self._language_mode, info.language):
            log.info(
                "Language %s looks wrong for slang mode; retrying as Slovenian",
                info.language,
            )
            segments, info = self._model.transcribe(
                audio,
                language="sl",
                temperature=self._temperature,
                initial_prompt=self._prompt or None,
                hotwords=self._hotwords or None,
                vad_filter=True,
                vad_parameters=self._vad_parameters,
            )
            segments = list(segments)

        log.info(
'@ "engine/local.py -- selective Slovenian retry" @('if should_retry_as_slovenian(', 'retry_language = bilingual_retry_language')
Apply-Patch "$site\engine\local.py" @'
            language=self._language,
            temperature=self._temperature,
            initial_prompt=self._prompt or None,
            hotwords=self._hotwords or None,
            vad_filter=True,
            vad_parameters=self._vad_parameters,
'@ @'
            language=self._language,
            initial_prompt=self._prompt or None,
            hotwords=self._hotwords or None,
            vad_filter=True,
            vad_parameters=self._vad_parameters,
            **decoding_options(self._temperature),
'@ "engine/local.py -- safe primary decoding"
$safeSlovenianRetryMarker = @'
                language="sl",
                initial_prompt=self._prompt or None,
                hotwords=self._hotwords or None,
                vad_filter=True,
                vad_parameters=self._vad_parameters,
                **decoding_options(self._temperature),
'@
Apply-Patch "$site\engine\local.py" @'
                language="sl",
                temperature=self._temperature,
                initial_prompt=self._prompt or None,
                hotwords=self._hotwords or None,
                vad_filter=True,
                vad_parameters=self._vad_parameters,
'@ @'
                language="sl",
                initial_prompt=self._prompt or None,
                hotwords=self._hotwords or None,
                vad_filter=True,
                vad_parameters=self._vad_parameters,
                **decoding_options(self._temperature),
'@ "engine/local.py -- safe Slovenian retry decoding" @($safeSlovenianRetryMarker, 'Bilingual retry accepted:')
Apply-Patch "$site\engine\local.py" @'
        segments = list(segments)
        if should_retry_as_slovenian(self._language_mode, info.language):
            log.info(
                "Language %s looks wrong for slang mode; retrying as Slovenian",
                info.language,
            )
            segments, info = self._model.transcribe(
                audio,
                language="sl",
                initial_prompt=self._prompt or None,
                hotwords=self._hotwords or None,
                vad_filter=True,
                vad_parameters=self._vad_parameters,
                **decoding_options(self._temperature),
            )
            segments = list(segments)
'@ @'
        primary_segments = list(segments)
        segments = primary_segments
        primary_score = transcript_score(primary_segments)
        retry_language = bilingual_retry_language(
            self._language_mode,
            info.language,
            info.language_probability,
            primary_score,
            getattr(info, "all_language_probs", None),
        )
        if retry_language:
            log.info(
                "Auto detected %s (conf %.2f, score %.3f); testing %s",
                info.language,
                info.language_probability,
                primary_score,
                retry_language,
            )
            retry_segments, retry_info = self._model.transcribe(
                audio,
                language=retry_language,
                initial_prompt=bilingual_retry_prompt(retry_language, self._prompt) or None,
                hotwords=bilingual_retry_hotwords(retry_language, self._hotwords) or None,
                vad_filter=True,
                vad_parameters=self._vad_parameters,
                **decoding_options(self._temperature),
            )
            retry_segments = list(retry_segments)
            retry_score = transcript_score(retry_segments)
            if prefer_bilingual_retry(
                retry_language,
                info.language,
                primary_segments,
                retry_segments,
            ):
                segments = retry_segments
                info = retry_info
                log.info(
                    "Bilingual retry accepted: %s score %.3f > primary %.3f",
                    retry_language,
                    retry_score,
                    primary_score,
                )
            else:
                log.info(
                    "Bilingual retry rejected: %s score %.3f; keeping %s %.3f",
                    retry_language,
                    retry_score,
                    info.language,
                    primary_score,
                )
'@ "engine/local.py -- scored bilingual Auto retry" 'Bilingual retry accepted:'
Remove-Patch "$site\engine\local.py" @'
        if should_retry_as_slovenian(self._language_mode, info.language):
            log.info(
                "Language %s looks wrong for slang mode; retrying as Slovenian",
                info.language,
            )
            segments, info = self._model.transcribe(
                audio,
                language="sl",
                initial_prompt=self._prompt or None,
                hotwords=self._hotwords or None,
                vad_filter=True,
                vad_parameters=self._vad_parameters,
                **decoding_options(self._temperature),
            )
            segments = list(segments)

'@ "engine/local.py -- obsolete selective retry"

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

# Batch recording must never silently discard speech. max_speech_s is passed to
# Silero VAD as an internal segmentation size; it is not a capture-duration cap.
$canonicalBatchBuffer = @'
        self._recording_start: float = 0.0
        # Batch mode keeps every chunk until release. Three minutes of 16 kHz
        # mono float32 audio is only about 11 MiB, and faster-whisper handles
        # longer input by splitting it into internal VAD/model segments.
        self._recorded_chunks: list[np.ndarray] = []
'@
Replace-Block `
    "$site\daemon.py" `
    '        self._recording_start: float = 0.0' `
    '        self._lock = threading.Lock()' `
    $canonicalBatchBuffer `
    "daemon.py -- preserve complete batch recordings"
Apply-Patch "$site\daemon.py" @'
            with self._lock:
                if len(self._recorded_chunks) >= self._max_batch_chunks:
                    return  # buffer full, drop chunk silently
                self._recorded_chunks.append(audio.copy())
'@ @'
            with self._lock:
                # Do not discard the tail of long held recordings.
                self._recorded_chunks.append(audio.copy())
'@ "daemon.py -- remove silent 90-second truncation" 'Do not discard the tail of long held recordings.'

Apply-Patch "$site\daemon.py" @'
            if text:
                type_text(text + " ")
                log.debug("Typed: %d chars", len(text))
'@ @'
            if text:
                type_text(text + " ")
                log.info("Paste shortcut sent: %d chars", len(text))
'@ "daemon.py -- observable successful paste" 'Paste shortcut sent: %d chars'

$canonicalTranscribeAndType = @'
    def _transcribe_and_type(self, audio: np.ndarray) -> None:
        """Transcribe audio, then paste it with separate failure reporting."""
        try:
            text = self._engine.transcribe(audio, self.config.audio.sample_rate)
        except Exception:
            log.error("Transcription failed", exc_info=True)
            notify("Transcription failed", "Open VoicePrompt diagnostics for details")
            return

        if not text:
            if not self.streaming:
                log.info("No speech detected")
            return

        try:
            type_text(text + " ")
            log.info("Paste shortcut sent: %d chars", len(text))
        except Exception:
            log.error("Paste failed after successful transcription", exc_info=True)
            notify("Paste failed", "Try Ctrl+V; the transcript was copied when possible")
'@
Replace-Block `
    "$site\daemon.py" `
    '    def _transcribe_and_type(' `
    '    def _deactivate_ws(' `
    $canonicalTranscribeAndType `
    "daemon.py -- separate transcription and paste failures"

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
    return key.isalpha() and len(key) == 1 and all(mod in _HOTKEY_MODIFIERS for mod in modifiers)
'@ @'
    if not all(mod in _HOTKEY_MODIFIERS for mod in modifiers):
        return False
    if len(key) == 1 and (key.isalpha() or key.isdigit()):
        return True
    if key in _HOTKEY_NAMED_KEYS or re.fullmatch(r"f(?:[1-9]|1[0-9]|2[0-4])", key):
        return True
    return False
'@ "config.py -- single/multi-key validation" 'if key in _HOTKEY_NAMED_KEYS or re.fullmatch'

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

# 9. Typer - optionally clean the transcript before touching the clipboard
Apply-Patch $typer @'
import time
'@ @'
import time

from .ai_rewriter import rewrite_text
'@ "typer.py -- AI cleanup import" 'from .ai_rewriter import rewrite_text'
Apply-Patch $typer @'
    if not text:
        return

    log.debug("Typing %d chars", len(text))
'@ @'
    if not text:
        return

    text = rewrite_text(text)
    log.debug("Typing %d chars", len(text))
'@ "typer.py -- AI cleanup before clipboard" 'text = rewrite_text(text)'

# Canonicalize methods that accumulated duplicate statements across upgrades.
# The dependency is pinned to 0.2.0, so replacing these small blocks is safer
# than carrying forward a chain of historical, order-dependent substitutions.
$canonicalLocalInit = @'
    def __init__(
        self,
        server_config: ServerConfig,
        engine_config: EngineConfig,
        vad_config: VADConfig,
    ):
        self._model = None
        self._model_lock = threading.Lock()
        self._model_name = server_config.model
        self._language_mode = server_config.language
        self._language = recognition_language(server_config.language)
        self._compute_type = engine_config.compute_type
        self._device = engine_config.device
        self._prompt = recognition_prompt(server_config.language, server_config.prompt)
        self._temperature = server_config.temperature
        self._hotwords = recognition_hotwords(server_config.language, server_config.hotwords)
        self._vad_parameters = {
            "threshold": vad_config.threshold,
            "min_silence_duration_ms": vad_config.silence_ms,
            "min_speech_duration_ms": vad_config.min_speech_ms,
            "max_speech_duration_s": vad_config.max_speech_s,
        }
'@
Replace-Block `
    "$site\engine\local.py" `
    '    def __init__(' `
    '    def _ensure_model(' `
    $canonicalLocalInit `
    "engine/local.py -- canonical recognition initialization"

$canonicalLocalTranscribe = @'
    def transcribe(self, audio: np.ndarray, sample_rate: int = 16000) -> str:
        total_started = time.perf_counter()
        self._ensure_model()

        # faster-whisper expects float32
        if audio.dtype != np.float32:
            audio = audio.astype(np.float32) / 32768.0

        primary_started = time.perf_counter()
        segments, info = self._model.transcribe(
            audio,
            language=self._language,
            initial_prompt=self._prompt or None,
            hotwords=self._hotwords or None,
            vad_filter=True,
            vad_parameters=self._vad_parameters,
            **decoding_options(self._temperature),
        )
        primary_segments = list(segments)
        primary_seconds = time.perf_counter() - primary_started
        retry_seconds = 0.0
        segments = primary_segments
        primary_score = transcript_score(primary_segments)
        retry_language = bilingual_retry_language(
            self._language_mode,
            info.language,
            info.language_probability,
            primary_score,
            getattr(info, "all_language_probs", None),
        )
        if retry_language:
            log.info(
                "Auto detected %s (conf %.2f, score %.3f); testing %s",
                info.language,
                info.language_probability,
                primary_score,
                retry_language,
            )
            retry_started = time.perf_counter()
            retry_segments, retry_info = self._model.transcribe(
                audio,
                language=retry_language,
                initial_prompt=bilingual_retry_prompt(retry_language, self._prompt) or None,
                hotwords=bilingual_retry_hotwords(retry_language, self._hotwords) or None,
                vad_filter=True,
                vad_parameters=self._vad_parameters,
                **decoding_options(self._temperature),
            )
            retry_segments = list(retry_segments)
            retry_seconds = time.perf_counter() - retry_started
            retry_score = transcript_score(retry_segments)
            if prefer_bilingual_retry(
                retry_language,
                info.language,
                primary_segments,
                retry_segments,
            ):
                segments = retry_segments
                info = retry_info
                log.info(
                    "Bilingual retry accepted: %s score %.3f > primary %.3f",
                    retry_language,
                    retry_score,
                    primary_score,
                )
            else:
                log.info(
                    "Bilingual retry rejected: %s score %.3f; keeping %s %.3f",
                    retry_language,
                    retry_score,
                    info.language,
                    primary_score,
                )

        total_seconds = time.perf_counter() - total_started
        log.info(
            "Transcription latency: primary %.3fs, retry %.3fs, total %.3fs",
            primary_seconds,
            retry_seconds,
            total_seconds,
        )
        log.info(
            "Detected language: %s (conf %.2f) [%d segments]",
            info.language,
            info.language_probability,
            len(segments),
        )
        text = " ".join(seg.text.strip() for seg in segments).strip()
        if text:
            log.debug("Transcribed: %d chars", len(text))
        return text
'@
Replace-Block `
    "$site\engine\local.py" `
    '    def transcribe(' `
    '    def is_available(' `
    $canonicalLocalTranscribe `
    "engine/local.py -- latency-bounded transcription"

$canonicalClipboardGet = @'
def _win_clipboard_api():
    """Return configured 64-bit-safe Win32 clipboard functions."""
    import ctypes

    user32 = ctypes.windll.user32
    kernel32 = ctypes.windll.kernel32
    if not getattr(_win_clipboard_api, "configured", False):
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
        user32.IsClipboardFormatAvailable.argtypes = [ctypes.c_uint]
        user32.IsClipboardFormatAvailable.restype = ctypes.c_int
        kernel32.GlobalAlloc.argtypes = [ctypes.c_uint, ctypes.c_size_t]
        kernel32.GlobalAlloc.restype = ctypes.c_void_p
        kernel32.GlobalLock.argtypes = [ctypes.c_void_p]
        kernel32.GlobalLock.restype = ctypes.c_void_p
        kernel32.GlobalUnlock.argtypes = [ctypes.c_void_p]
        kernel32.GlobalUnlock.restype = ctypes.c_int
        kernel32.GlobalFree.argtypes = [ctypes.c_void_p]
        kernel32.GlobalFree.restype = ctypes.c_void_p
        _win_clipboard_api.configured = True
    return ctypes, user32, kernel32


def _win_clipboard_open(user32, operation: str) -> None:
    """Open the shared clipboard with a short bounded contention retry."""
    for _ in range(20):
        if user32.OpenClipboard(0):
            return
        time.sleep(0.01)
    raise RuntimeError(f"Could not open Windows clipboard for {operation}")


def _win_clipboard_get() -> str | None:
    """Read clipboard text, or None when the clipboard has no Unicode text."""
    ctypes, user32, kernel32 = _win_clipboard_api()

    _win_clipboard_open(user32, "reading")
    try:
        if not user32.IsClipboardFormatAvailable(_CF_UNICODETEXT):
            return None
        handle = user32.GetClipboardData(_CF_UNICODETEXT)
        if not handle:
            raise RuntimeError("GetClipboardData returned no text handle")
        ptr = kernel32.GlobalLock(handle)
        if not ptr:
            raise RuntimeError("GlobalLock failed while reading clipboard")
        try:
            return ctypes.wstring_at(ptr)
        finally:
            kernel32.GlobalUnlock(handle)
    finally:
        user32.CloseClipboard()
'@
$clipboardReaderStart = if (
    [System.IO.File]::ReadAllText($typer).Contains('def _win_clipboard_api():')
) { 'def _win_clipboard_api():' } else { 'def _win_clipboard_get(' }
Replace-Block `
    $typer `
    $clipboardReaderStart `
    'def _win_clipboard_set(' `
    $canonicalClipboardGet `
    "typer.py -- canonical clipboard reader"

$canonicalClipboardSet = @'
def _win_clipboard_set(text: str) -> None:
    """Write all Unicode text or raise; clipboard failures are never silent."""
    ctypes, user32, kernel32 = _win_clipboard_api()

    encoded = text.encode("utf-16-le") + b"\x00\x00"
    handle = kernel32.GlobalAlloc(_GMEM_MOVEABLE, len(encoded))
    if not handle:
        raise RuntimeError("GlobalAlloc failed")
    ptr = kernel32.GlobalLock(handle)
    if not ptr:
        kernel32.GlobalFree(handle)
        raise RuntimeError("GlobalLock failed")
    ctypes.memmove(ptr, encoded, len(encoded))
    kernel32.GlobalUnlock(handle)

    try:
        _win_clipboard_open(user32, "writing")
    except Exception:
        kernel32.GlobalFree(handle)
        raise
    try:
        if not user32.EmptyClipboard():
            kernel32.GlobalFree(handle)
            raise RuntimeError("EmptyClipboard failed")
        result = user32.SetClipboardData(_CF_UNICODETEXT, handle)
        if not result:
            kernel32.GlobalFree(handle)
            raise RuntimeError("SetClipboardData failed")
    finally:
        user32.CloseClipboard()
'@
Replace-Block `
    $typer `
    'def _win_clipboard_set(' `
    'def _send_ctrl_v(' `
    $canonicalClipboardSet `
    "typer.py -- canonical clipboard writer"

$canonicalWindowsTyper = @'
def _type_windows(text: str) -> None:
    """Paste through a verified clipboard write and preserve prior text."""
    previous: str | None = None
    captured_previous = False
    try:
        previous = _win_clipboard_get()
        captured_previous = True
    except Exception:
        # Reading the old clipboard must not discard a completed transcript.
        log.warning("Could not preserve previous clipboard text", exc_info=True)

    paste_sent = False
    try:
        _win_clipboard_set(text)
        if _win_clipboard_get() != text:
            raise RuntimeError("Clipboard verification failed")
        _send_ctrl_v()
        paste_sent = True
        time.sleep(PASTE_DELAY)
    except Exception:
        # A successful fallback copy makes the completed transcript recoverable
        # with a manual Ctrl+V even if automatic input injection failed.
        try:
            _win_clipboard_set(text)
            log.error("Automatic paste failed; transcript left on clipboard", exc_info=True)
        except Exception:
            log.error("Windows typing and fallback clipboard copy failed", exc_info=True)
        raise
    finally:
        if paste_sent and captured_previous and previous is not None:
            try:
                _win_clipboard_set(previous)
            except Exception:
                log.warning("Could not restore previous clipboard text", exc_info=True)
'@
Replace-Block `
    $typer `
    'def _type_windows(' `
    '_CF_UNICODETEXT = 13' `
    $canonicalWindowsTyper `
    "typer.py -- reliable verified Windows paste"

Write-Output "`nAll patches applied. Restart the daemon: Stop Voice Typing -> Start Voice Typing"
