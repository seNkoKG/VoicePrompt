# Verifies clean upstream plus legacy and immediately previous release upgrades.
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
    $cli = Join-Path $Module "cli.py"
    $audio = Join-Path $Module "audio.py"
    $localEngine = Join-Path $Module "engine\local.py"
    $serverEngine = Join-Path $Module "engine\server.py"
    $typer = Join-Path $Module "typer.py"
    $daemon = Join-Path $Module "daemon.py"
    $history = Join-Path $Module "transcript_history.py"
    $corrections = Join-Path $Module "text_corrections.py"
    $buffered = Join-Path $Module "buffered_transcription.py"
    $outputMode = Join-Path $Module "output_mode.py"
    $appProfiles = Join-Path $Module "app_profiles.py"
    $textSnippets = Join-Path $Module "text_snippets.py"
    $voiceCommands = Join-Path $Module "voice_commands.py"
    $smartFormatter = Join-Path $Module "smart_formatter.py"
    $windowsContext = Join-Path $Module "windows_context.py"
    $selectionCommands = Join-Path $Module "selection_commands.py"
    $windowsHotkey = Join-Path $Module "windows_hotkey.py"
    $listener = Join-Path $Module "hotkey\listener.py"
    $config = Join-Path $Module "config.py"
    & $Python -m py_compile $cli $audio $localEngine $serverEngine $typer $daemon $listener (Join-Path $Module "slang_retry.py") $history $corrections $buffered $outputMode $appProfiles $textSnippets $voiceCommands $smartFormatter $windowsContext $selectionCommands $windowsHotkey
    if ($LASTEXITCODE -ne 0) {
        throw "$Name runtime does not compile."
    }

    $cliSource = [System.IO.File]::ReadAllText($cli)
    if (-not $cliSource.Contains("GetExitCodeProcess") -or
        -not $cliSource.Contains('force_signal = signal.SIGTERM if sys.platform == "win32" else signal.SIGKILL') -or
        $cliSource.Contains("os.kill(pid, signal.SIGKILL)")) {
        throw "$Name does not stop and classify Windows runtime processes safely."
    }

    $listenerSource = [System.IO.File]::ReadAllText($listener)
    $windowsHotkeySource = [System.IO.File]::ReadAllText($windowsHotkey)
    $configSource = [System.IO.File]::ReadAllText($config)
    if ([regex]::Matches($listenerSource, "from \.\.windows_hotkey import WindowsHotkeyBackend").Count -ne 1 -or
        [regex]::Matches($listenerSource, "self\._win32_backend = WindowsHotkeyBackend").Count -ne 1 -or
        -not $listenerSource.Contains('if sys.platform == "win32":') -or
        $listenerSource.Contains("win32_event_filter") -or
        $listenerSource.Contains("suppressing_target") -or
        [regex]::Matches($listenerSource, "def _handle_native_release\(self\)").Count -ne 1 -or
        [regex]::Matches($listenerSource, "self\._handle_native_release,").Count -ne 1 -or
        -not $listenerSource.Contains("self._key_released = True")) {
        throw "$Name does not select the canonical native Windows hotkey backend."
    }
    if (-not $windowsHotkeySource.Contains("RegisterHotKey") -or
        -not $windowsHotkeySource.Contains("MOD_NOREPEAT") -or
        -not $windowsHotkeySource.Contains("GetAsyncKeyState") -or
        -not $windowsHotkeySource.Contains("Hotkey %s callback failed; listener is still active")) {
        throw "$Name is missing reliable native Windows hotkey lifecycle handling."
    }
    if ([regex]::Matches($configSource, "_HOTKEY_NAMED_KEYS = frozenset").Count -ne 1 -or
        [regex]::Matches($configSource, "_HOTKEY_MODIFIERS =").Count -ne 1 -or
        -not $configSource.Contains('key.isascii()') -or
        -not $configSource.Contains('len(set(modifiers)) != len(modifiers)') -or
        $configSource.Contains('"cmd", "super", "meta"') -or
        $configSource.Contains('1[0-9]|2[0-4]')) {
        throw "$Name does not have one canonical native Windows hotkey contract."
    }

    $source = [System.IO.File]::ReadAllText($localEngine)
    if ($source.Contains("if should_retry_as_slovenian(")) {
        throw "$Name retained the obsolete retry block."
    }
    if ([regex]::Matches($source, "Bilingual retry accepted:").Count -ne 1) {
        throw "$Name has an invalid bilingual retry block count."
    }
    if ([regex]::Matches($source, "recognition_prompt\(server_config\.language").Count -ne 1) {
        throw "$Name does not configure the language-neutral primary pass exactly once."
    }
    if ($source.Contains("self._prompt = server_config.prompt")) {
        throw "$Name retained recognition settings that override Auto vocabulary."
    }
    if ([regex]::Matches($source, "self\._base_prompt = server_config\.prompt").Count -ne 1 -or
        [regex]::Matches($source, "self\._base_hotwords = server_config\.hotwords").Count -ne 1) {
        throw "$Name does not preserve unbiased base recognition hints."
    }
    if ($source.Contains("bilingual_retry_prompt(retry_language, self._prompt)") -or
        $source.Contains("bilingual_retry_hotwords(retry_language, self._hotwords)")) {
        throw "$Name leaks primary-pass language hints into a forced retry."
    }
    if ([regex]::Matches($source, "Transcription latency:").Count -ne 1) {
        throw "$Name does not have exactly one latency instrumentation block."
    }
    if ([regex]::Matches($source, "self\._recent_language = None").Count -ne 1 -or
        [regex]::Matches($source, "self\._recent_language = remember_recent_language\(").Count -ne 1 -or
        $source.Contains("self._recent_language = info.language")) {
        throw "$Name does not preserve recent-language evidence through the canonical runtime."
    }
    if ([regex]::Matches($source, "self\.last_language = None").Count -ne 1 -or
        [regex]::Matches($source, "self\.last_language = info\.language").Count -ne 1) {
        throw "$Name does not expose the final language for buffered consistency checks."
    }
    if ([regex]::Matches($source, '"Bilingual evidence: en %\.2f, sl %\.2f, recent=%s"').Count -ne 1 -or
        [regex]::Matches($source, "language_probabilities=language_probabilities").Count -ne 1 -or
        [regex]::Matches($source, "recent_language=self\._recent_language").Count -ne 1 -or
        [regex]::Matches($source, "            audio_seconds,").Count -ne 1) {
        throw "$Name does not compare ambiguous short English and Slovenian candidates."
    }
    if ([regex]::Matches($source, "transcript_is_plausible\(").Count -ne 4 -or
        [regex]::Matches($source, "decoding_options\(0\.2\)").Count -ne 1) {
        throw "$Name does not guard impossible transcript expansion with one same-language recovery."
    }
    if ([regex]::Matches($source, "def prepare\(self\)").Count -ne 1 -or
        [regex]::Matches($source, "def release_if_idle\(self, idle_seconds: float\)").Count -ne 1 -or
        [regex]::Matches($source, "self\._model\.model\.unload_model\(\)").Count -ne 2 -or
        [regex]::Matches($source, "self\._model\.model\.load_model\(\)").Count -ne 1 -or
        -not $source.Contains('find_spec("faster_whisper") is not None') -or
        $source.Contains("def is_available(self) -> bool:`n        try:`n            self._ensure_model()")) {
        throw "$Name does not implement one serialized native model lifecycle."
    }

    $serverSource = [System.IO.File]::ReadAllText($serverEngine)
    if ([regex]::Matches($serverSource, '"sl" if self\.config\.language == "sl-slang"').Count -ne 1 -or
        [regex]::Matches($serverSource, '"language": \(').Count -ne 1) {
        throw "$Name does not map the local Slovenian slang profile safely for compatible servers."
    }

    $typerSource = [System.IO.File]::ReadAllText($typer)
    if ([regex]::Matches($typerSource, "def _win_clipboard_api\(").Count -ne 1) {
        throw "$Name does not have exactly one canonical clipboard API helper."
    }
    if ([regex]::Matches($typerSource, "user32\.OpenClipboard\.argtypes").Count -ne 1) {
        throw "$Name retained duplicate Win32 clipboard declarations."
    }
    if ($typerSource.Contains('log.debug("Failed to open clipboard for writing")')) {
        throw "$Name can still silently ignore a failed clipboard write."
    }
    if ([regex]::Matches($typerSource, "def _win_clipboard_open\(").Count -ne 1) {
        throw "$Name does not have exactly one clipboard contention helper."
    }
    if (-not $typerSource.Contains("Clipboard verification failed")) {
        throw "$Name does not verify the complete transcript before paste."
    }
    if ([regex]::Matches($typerSource, "remember_transcript\(original_text, text\)").Count -ne 1) {
        throw "$Name does not save exactly one recovery entry before paste."
    }
    if ([regex]::Matches($typerSource, "apply_corrections\(text\)").Count -ne 1) {
        throw "$Name does not apply personal corrections exactly once."
    }
    if (-not (Test-Path -LiteralPath $history) -or -not (Test-Path -LiteralPath $corrections)) {
        throw "$Name is missing a local text pipeline module."
    }
    if (-not ([System.IO.File]::ReadAllText($history)).Contains("_MAX_HISTORY_BYTES") -or
        -not ([System.IO.File]::ReadAllText($corrections)).Contains("_MAX_FILE_BYTES") -or
        -not ([System.IO.File]::ReadAllText($textSnippets)).Contains("_MAX_FILE_BYTES")) {
        throw "$Name can load an unbounded local text file in the dictation path."
    }
    if (-not (Test-Path -LiteralPath $outputMode) -or
        [regex]::Matches($typerSource, "deliver_text\(text, _copy_text_impl, _type_text_impl, mode=output_override\)").Count -ne 1 -or
        [regex]::Matches($typerSource, "def _copy_text_impl\(").Count -ne 1) {
        throw "$Name is missing the exactly-once transcript output router."
    }
    if (-not (Test-Path -LiteralPath $appProfiles) -or
        [regex]::Matches($typerSource, "from \.app_profiles import resolve_app_profile").Count -ne 1 -or
        [regex]::Matches($typerSource, "profile = resolve_app_profile\(\)").Count -ne 1 -or
        [regex]::Matches($typerSource, "rewrite_text\(text, mode_override=writing_override\)").Count -ne 1) {
        throw "$Name is missing the exact application-aware override router."
    }
    if (-not (Test-Path -LiteralPath $textSnippets) -or
        -not (Test-Path -LiteralPath $voiceCommands) -or
        [regex]::Matches($typerSource, "from \.voice_commands import execute_voice_command, resolve_voice_command").Count -ne 1 -or
        [regex]::Matches($typerSource, "command = resolve_voice_command\(text\)").Count -ne 1 -or
        [regex]::Matches($typerSource, "def _send_ctrl_z\(").Count -ne 1 -or
        [regex]::Matches($typerSource, "return execute_voice_command\(command, deliver_command, _send_ctrl_z\)").Count -ne 1) {
        throw "$Name is missing the exact opt-in voice-command router."
    }
    if (-not (Test-Path -LiteralPath $smartFormatter) -or
        -not (Test-Path -LiteralPath $windowsContext) -or
        [regex]::Matches($typerSource, "context = capture_context\(\)").Count -ne 1 -or
        [regex]::Matches($typerSource, "text = format_dictation\(text, context\)").Count -ne 1) {
        throw "$Name is missing the context-aware local formatting pipeline."
    }
    if (-not (Test-Path -LiteralPath $selectionCommands) -or
        [regex]::Matches($typerSource, "selection_instruction = resolve_selection_command\(text\)").Count -ne 1 -or
        [regex]::Matches($typerSource, "replacement = rewrite_selection\(selected_text, selection_instruction\)").Count -ne 1 -or
        [regex]::Matches($typerSource, "def _capture_selected_text\(\)").Count -ne 1) {
        throw "$Name is missing selected-text command mode."
    }

    $audioSource = [System.IO.File]::ReadAllText($audio)
    if ([regex]::Matches($audioSource, "Using default audio input").Count -ne 2 -or
        -not $audioSource.Contains("sd.query_devices(device)")) {
        throw "$Name does not recover a stale microphone selection."
    }

    $daemonSource = [System.IO.File]::ReadAllText($daemon)
    if ($daemonSource.Contains("_max_batch_chunks") -or $daemonSource.Contains("drop chunk silently")) {
        throw "$Name still truncates held recordings."
    }
    if ([regex]::Matches($daemonSource, "self\._recorded_chunks\.append\(audio\.copy\(\)\)").Count -ne 2) {
        throw "$Name does not retain each batch or buffered audio chunk exactly once per route."
    }
    if (-not (Test-Path -LiteralPath $buffered) -or
        -not $daemonSource.Contains("self._buffered_streaming") -or
        -not $daemonSource.Contains("full audio will be used") -or
        -not $daemonSource.Contains("Short or uninterrupted recordings keep the proven exact batch path") -or
        -not $daemonSource.Contains('getattr(self._engine, "last_language", None)') -or
        -not $daemonSource.Contains("Buffered language conflict detected")) {
        throw "$Name is missing the lossless buffered transcription contract."
    }
    $bufferedSource = [System.IO.File]::ReadAllText($buffered)
    if (-not $bufferedSource.Contains("len(self._languages) > 1") -or
        -not $bufferedSource.Contains("def language_conflict(self)")) {
        throw "$Name does not reject mixed-language buffered output."
    }
    if (-not $daemonSource.Contains("Paste shortcut sent: %d chars")) {
        throw "$Name does not expose a privacy-safe successful-paste signal."
    }
    if (-not $daemonSource.Contains("Transcript copied to clipboard: %d chars")) {
        throw "$Name does not expose a privacy-safe copy-only success signal."
    }
    if (-not $daemonSource.Contains("Voice command cancelled transcript")) {
        throw "$Name does not handle a cancelled voice command without delivery."
    }
    if (-not $daemonSource.Contains("Voice command shortcut sent")) {
        throw "$Name does not report a completed shortcut voice command."
    }
    if (-not $daemonSource.Contains('notify("Transcript delivery failed"')) {
        throw "$Name does not report failed transcript delivery to the user."
    }
    $activateStart = $daemonSource.IndexOf("    def _on_activate(")
    $audioStart = $daemonSource.IndexOf("        audio = AudioStream(", $activateStart)
    $feedbackStart = $daemonSource.IndexOf("        publish_state(True)", $activateStart)
    if ($activateStart -lt 0 -or $feedbackStart -lt 0 -or $feedbackStart -gt $audioStart) {
        throw "$Name waits for the microphone before publishing activation feedback."
    }
    if (-not $daemonSource.Contains("Audio capture ready in %.0f ms")) {
        throw "$Name does not measure microphone cold-start latency."
    }
    if ([regex]::Matches($daemonSource, "def _prepare_engine\(self\)").Count -ne 1 -or
        [regex]::Matches($daemonSource, "def _schedule_idle_release\(self\)").Count -ne 1 -or
        [regex]::Matches($daemonSource, "self\._transcribe_pool\.submit\(self\._prepare_engine\)").Count -ne 1 -or
        [regex]::Matches($daemonSource, "self\._schedule_idle_release\(\)").Count -ne 1) {
        throw "$Name does not order model preparation and bounded idle release."
    }
    $deactivateStart = $daemonSource.IndexOf("    def _on_deactivate(")
    $stopAudio = $daemonSource.IndexOf("            audio.stop()", $deactivateStart)
    $snapshotAudio = $daemonSource.IndexOf("            chunks = list(self._recorded_chunks)", $deactivateStart)
    if ($deactivateStart -lt 0 -or $stopAudio -lt 0 -or $snapshotAudio -lt 0 -or $snapshotAudio -lt $stopAudio) {
        throw "$Name snapshots audio before the final capture callback has stopped."
    }
    $previousPatchedSite = $env:VOICEPROMPT_PATCHED_SITE
    try {
        $env:VOICEPROMPT_PATCHED_SITE = $Module
        & $Python -m unittest -v tests.test_patched_local_engine
        if ($LASTEXITCODE -ne 0) {
            throw "$Name product-path local engine test failed."
        }
    } finally {
        $env:VOICEPROMPT_PATCHED_SITE = $previousPatchedSite
    }
    Write-Output "PASS $Name"
}

$cleanModule = New-UpstreamRuntime "clean"
Invoke-RuntimePatch $currentPatch $cleanModule "clean-first"
$cleanFirstHash = (Get-FileHash -LiteralPath (Join-Path $cleanModule "daemon.py") -Algorithm SHA256).Hash
$cleanListenerHash = (Get-FileHash -LiteralPath (Join-Path $cleanModule "hotkey\listener.py") -Algorithm SHA256).Hash
Invoke-RuntimePatch $currentPatch $cleanModule "clean-second"
if ((Get-FileHash -LiteralPath (Join-Path $cleanModule "daemon.py") -Algorithm SHA256).Hash -ne $cleanFirstHash) {
    throw "Clean install patching is not byte-for-byte idempotent."
}
if ((Get-FileHash -LiteralPath (Join-Path $cleanModule "hotkey\listener.py") -Algorithm SHA256).Hash -ne $cleanListenerHash) {
    throw "Clean hotkey patching is not byte-for-byte idempotent."
}
Assert-CurrentRuntime $cleanModule "clean install"

$oldArchive = Join-Path $testRoot "v1.1.2-fixture.zip"
$releaseRoot = Join-Path $testRoot "v1.1.2"
& git -C $root archive --format=zip "--output=$oldArchive" v1.1.2 scripts run_daemon.pyw
if ($LASTEXITCODE -ne 0) {
    throw "Could not read the v1.1.2 upgrade fixture from git history."
}
Expand-Archive -LiteralPath $oldArchive -DestinationPath $releaseRoot
$oldPatch = Join-Path $releaseRoot "scripts\apply_patches.ps1"
if (-not (Test-Path -LiteralPath $oldPatch)) {
    throw "The v1.1.2 upgrade fixture is missing apply_patches.ps1."
}

$upgradeModule = New-UpstreamRuntime "upgrade"
Invoke-RuntimePatch $oldPatch $upgradeModule "v1.1.2"
Invoke-RuntimePatch $currentPatch $upgradeModule "upgrade-first"
$upgradeFirstHash = (Get-FileHash -LiteralPath (Join-Path $upgradeModule "daemon.py") -Algorithm SHA256).Hash
$upgradeListenerHash = (Get-FileHash -LiteralPath (Join-Path $upgradeModule "hotkey\listener.py") -Algorithm SHA256).Hash
Invoke-RuntimePatch $currentPatch $upgradeModule "upgrade-second"
if ((Get-FileHash -LiteralPath (Join-Path $upgradeModule "daemon.py") -Algorithm SHA256).Hash -ne $upgradeFirstHash) {
    throw "Upgrade patching is not byte-for-byte idempotent."
}
if ((Get-FileHash -LiteralPath (Join-Path $upgradeModule "hotkey\listener.py") -Algorithm SHA256).Hash -ne $upgradeListenerHash) {
    throw "Upgrade hotkey patching is not byte-for-byte idempotent."
}
Assert-CurrentRuntime $upgradeModule "v1.1.2 upgrade"

$previousArchive = Join-Path $testRoot "v1.21.1-fixture.zip"
$previousRoot = Join-Path $testRoot "v1.21.1"
& git -C $root archive --format=zip "--output=$previousArchive" v1.21.1 scripts run_daemon.pyw
if ($LASTEXITCODE -ne 0) {
    throw "Could not read the v1.21.1 upgrade fixture from git history."
}
Expand-Archive -LiteralPath $previousArchive -DestinationPath $previousRoot
$previousPatch = Join-Path $previousRoot "scripts\apply_patches.ps1"
$previousModule = New-UpstreamRuntime "previous"
Invoke-RuntimePatch $previousPatch $previousModule "v1.21.1"
Invoke-RuntimePatch $currentPatch $previousModule "previous-first"
$previousConfigHash = (Get-FileHash -LiteralPath (Join-Path $previousModule "config.py") -Algorithm SHA256).Hash
Invoke-RuntimePatch $currentPatch $previousModule "previous-second"
if ((Get-FileHash -LiteralPath (Join-Path $previousModule "config.py") -Algorithm SHA256).Hash -ne $previousConfigHash) {
    throw "Previous-release hotkey migration is not byte-for-byte idempotent."
}
Assert-CurrentRuntime $previousModule "v1.21.1 upgrade"

Write-Output "PATCH_MIGRATION_GATE=PASS"
