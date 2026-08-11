# Changelog

## [1.1.0] - 2026-08-11

### Added

- Optional AI cleanup with Off, Grammar, and Prompt modes.
- Slovenian slang recognition profile that pins the language and boosts common colloquial words.
- OpenAI-compatible local or cloud provider settings and a built-in connection test.
- Windows account encryption for saved API keys.
- Strict request deadlines with automatic original-transcript fallback.

### Changed

- Reused HTTP connections and capped responses for fast text cleanup.
- Moved all control reads onto the UI thread before background saving.
- Restored the GitHub architecture graphic as an aligned fixed-width diagram.

### Fixed

- AI waits never hold the clipboard or discard a successful local transcription.

## [1.0.0] - 2026-08-11

- First public Windows release with local GPU dictation, tray settings, selective global hotkeys, and the live microphone overlay.
