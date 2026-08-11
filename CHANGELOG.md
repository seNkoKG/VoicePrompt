# Changelog

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
