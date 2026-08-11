# Changelog

## [1.2.1] - 2026-08-11

### Changed

- Kept accurate `large-v3` beam-5 decoding while replacing the six-step temperature fallback with one deterministic, latency-bounded pass.
- Added per-utterance primary, bilingual retry, and total transcription timing to the daemon log.
- Kept transcript text on the clipboard long enough for temporarily busy target apps to consume the paste before the original clipboard is restored.

### Fixed

- Removed intermittent multi-second release-to-paste spikes caused by stacking Whisper temperature fallbacks with a full bilingual recovery pass.
- Canonicalized upgraded runtime methods so stale recognition settings can no longer override the English/Slovenian Auto vocabulary profile.
- Removed duplicate Win32 clipboard declarations accumulated by earlier upgrades and configured the 64-bit signatures once per process.
- Added clean-install and v1.1.2-upgrade regression gates for latency instrumentation, recognition settings, clipboard declarations, and patch idempotence.

## [1.2.0] - 2026-08-11

### Added

- Added a setup overview with live runtime readiness, essential configuration summaries, health checks, and quick support actions.
- Added dedicated Dictation, Audio, Intelligence, and Advanced workspaces with guided descriptions and recommended presets.
- Added microphone refresh, direct Windows Sound Settings access, copied privacy-safe diagnostics, log/config shortcuts, runtime recovery, and release access.
- Added explicit unsaved-change and busy states, discard support, keyboard navigation, `Ctrl+S`, page shortcuts, and screen-reader names.
- Added automated visual coverage for every settings page at standard and minimum supported window sizes.

### Changed

- Rebuilt the settings interface from scratch as a responsive charcoal workspace with compact navigation, clearer grouping, and consistent custom controls.
- Reworked the hotkey recorder so keyboard focus alone never captures or changes a shortcut; Enter or Space starts keyboard capture.
- Moved every settings value onto the UI thread before background persistence and runtime restart.

### Fixed

- Prevented microphone refresh from reverting a newly selected input device.
- Preserved Windows startup errors instead of replacing them with a generic save-success message.
- Removed responsive text overflow, clipped controls, and paint-time font/brush resource leaks from the settings interface.

## [1.1.4] - 2026-08-11

### Fixed

- Removed an obsolete Slovenian retry block that could survive a v1.1.2 upgrade and stop every transcription before paste.
- Added release-gating tests for both clean upstream installs and real v1.1.2 runtime upgrades.

## [1.1.3] - 2026-08-11

### Added

- Added a fast bilingual Auto route tuned for English and Slovenian, including colloquial Slovenian forms such as `dej`, `lohk`, `kva`, `tko`, and `zdej`.
- Added score-gated fallback decoding for low-confidence English and unrelated-language detection mistakes.

### Changed

- Applied the Slovenian vocabulary profile during Auto's primary decode, improving slang recognition without a second pass or affecting English language detection.
- Made optional AI cleanup preserve the detected language, English/Slovenian code-switches, slang, requirements, and wording instead of translating or freely rewriting them.

### Fixed

- Prevented English speech in Auto from being translated into Slovenian by accepting a forced-language retry only when Whisper's decoder score supports it.
- Preserved user-entered hotword capitalization while deduplicating the built-in vocabulary.

## [1.1.2] - 2026-08-11

### Fixed

- Made the Windows patcher newline-independent so upgrades work across locally built LF files and GitHub-built CRLF release archives.
- Stopped the active dictation daemon before upgrading its Python runtime, preventing mixed old/new code during installation.

## [1.1.1] - 2026-08-11

### Fixed

- Prevented long dictation from getting stuck repeating the same sentence.
- Disabled previous-window transcript conditioning, which faster-whisper identifies as a cause of decoder failure loops.
- Restored temperature fallback when the deterministic first pass fails quality thresholds.
- Added native repetition penalty and 3-token no-repeat decoding used by production Whisper systems.
- Made upgrades restart the patched dictation runtime immediately instead of leaving old code in memory.

## [1.1.0] - 2026-08-11

### Added

- Optional AI cleanup with Off, Grammar, and Prompt modes.
- Mixed English/Slovenian slang profile that retries only third-language detection mistakes.
- OpenAI-compatible local or cloud provider settings and a built-in connection test.
- Windows account encryption for saved API keys.
- Strict request deadlines with automatic original-transcript fallback.

### Changed

- Reused HTTP connections and capped responses for fast text cleanup.
- Moved all control reads onto the UI thread before background saving.
- Restored the GitHub architecture graphic as an aligned fixed-width diagram.

### Fixed

- AI waits never hold the clipboard or discard a successful local transcription.
- Clean 0.2.0 installs patch the Windows hotkey validator reliably.

## [1.0.0] - 2026-08-11

- First public Windows release with local GPU dictation, tray settings, selective global hotkeys, and the live microphone overlay.
