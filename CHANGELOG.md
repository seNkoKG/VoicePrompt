# Changelog

## [1.19.0] - 2026-08-12

### Added

- Added three instant, locally persisted dark interface themes: Graphite, Evergreen, and Ember.
- Added a scalable waveform brand mark shared by the app sidebar, executable, desktop shortcut, tray, repository, and product site.
- Added theme selection and screenshot-gallery coverage to the automated Windows layout harness.

### Changed

- Rebuilt the settings visual system with a calmer neutral palette, stronger hierarchy, roomier navigation, refined cards, controls, typography, and clearer Engine & AI and System workspaces.
- Redesigned the GitHub Pages presentation around current client-only screenshots, dark Windows-style frames, an interactive palette preview, and a four-view product gallery.
- Generate the application icon from reusable vector geometry so every embedded ICO size remains crisp and consistent.

### Fixed

- Apply native Windows dark-mode attributes to the caption, border, controls, and scroll surfaces, removing the bright frame around the dark application.
- Stop diagnostic screenshot generation from capturing light operating-system chrome around the app.
- Stop the sidebar brand from inheriting the generic executable icon used by diagnostic builds.

## [1.18.2] - 2026-08-12

### Added

- Added a responsive GitHub Pages product site and matching repository hero with a charcoal, accessible design, interactive installation helper, and clear local-first data flow.
- Added regression coverage for weak Slovenian detections that are actually English and for genuine Slovenian that must not be replaced by a marginal English decode.

### Changed

- Restore faster-whisper's neutral repetition defaults while retaining independent-window decoding, improving legitimate repeated wording without reintroducing the previous-window failure loop.
- Present the project, installation path, bilingual behavior, performance context, and privacy choices consistently across GitHub and the product website.

### Fixed

- Verify weak Slovenian detections against English instead of trusting every Slovenian top guess unconditionally. This prevents a low-confidence tail of an English recording from being emitted as Slovenian.
- Require a real decoder-score gain before switching either supported language, protecting genuine English and genuine Slovenian in Auto mode.
- Correct mobile website overflow and keep the complete EN, Auto, and SL language visual aligned on narrow screens.

## [1.18.1] - 2026-08-12

### Fixed

- Remove only recognized VoicePrompt-owned legacy desktop and Start Menu shortcuts during upgrade.
- Keep one canonical `VoicePrompt` shortcut targeting the installed executable and its embedded icon, instead of leaving development or daemon-only links behind.
- Preserve unrelated same-name shortcuts and verify repeated shortcut migration is safe and idempotent.

## [1.18.0] - 2026-08-12

### Added

- Added an opt-in OpenAI-compatible recognition-server mode for shared, separate, or self-hosted speech hardware.
- Added a bounded `/health` connection test that sends no microphone audio, credentials, transcript text, or request body.
- Added server URL, timeout, privacy, portable-backup, transport, UI, accessibility, and migration coverage.

### Changed

- Keep the private local GPU engine as the default and visually disable local-only processor, precision, and long-recording controls in server mode.
- Show explicit local, remote HTTPS, and unencrypted remote HTTP privacy guidance before settings are saved.
- Route the internal Slovenian slang profile as standard `sl` to compatible servers and omit the language field for Auto detection.

### Fixed

- Expose the tested upstream REST engine without requiring users to hand-edit TOML, while preserving exact local behavior for existing installs.

## [1.17.0] - 2026-08-12

### Added

- Added optional application-aware writing and output profiles using exact executable-name matches.
- Added a running-application picker that inserts safe filename-only rules without saving process paths.
- Added local parser, foreground-process, AI override, delivery, backup, UI, packaging, and migration coverage.

### Changed

- Load profile rules once when the runtime starts; an empty profile set performs no foreground-process lookup.
- Allow a matched profile to enable or disable Clean, Grammar, or Prompt while retaining the configured provider deadline and raw-text fallback.

### Fixed

- Keep unmatched, inaccessible, malformed, duplicate, and path-like application rules from changing global behavior.

## [1.16.0] - 2026-08-12

### Added

- Added Stable and Preview update channels, with Stable retained as the default.
- Added semantic prerelease ordering, draft exclusion, preference persistence, and UI behavior coverage.

### Changed

- Keep update checks manual-only and construct every destination from the official VoicePrompt GitHub release path.
- Preserve the user's UI preference file byte-for-byte while automated layout tests run.

### Fixed

- Prevent preview builds from being invisible to users who explicitly opt into prerelease checks, while never exposing them to Stable users.

## [1.15.0] - 2026-08-12

### Added

- Added a side-by-side Recovery comparison for the delivered result and untouched original transcript.
- Added separate Copy result and Copy original actions with screen-reader labels and responsive layout coverage.

### Changed

- Make unchanged Verbatim transcripts explicit instead of combining both versions in one ambiguous text box.

### Fixed

- Preserve access to the exact pre-cleanup wording when optional AI cleanup changes a transcript.

## [1.14.0] - 2026-08-12

### Added

- Added a validated JSON backup for portable dictation, recognition, audio-detection, writing, recovery, correction, and snippet settings.
- Added Advanced-page Export backup and Import backup actions with review-first import behavior.
- Added Unicode round-trip, atomic file, privacy exclusion, invalid-hotkey, embedded-credential, UI interaction, accessibility, and scrolled-layout coverage.

### Changed

- Preserve the current encrypted API key during import and require Save & restart before imported values affect the runtime.
- Exclude transcript history, API keys, microphone identity, startup state, window preferences, logs, and machine paths from every backup.

### Fixed

- Reject oversized or malformed backups and unsafe provider URLs containing embedded credentials, query strings, or fragments.

## [1.13.0] - 2026-08-12

### Added

- Added up to 50 reusable local text snippets with 4,000 characters per snippet and escaped multi-line content.
- Added exact English `Insert snippet name` and Slovenian `Vstavi predlogo name` commands.
- Added a dedicated Reusable text settings card plus parser, storage, command integration, packaging, migration, UI interaction, and scrolled-layout coverage.

### Changed

- Load snippets once at runtime startup so insertion adds no file I/O, AI request, or network delay to dictation.
- Route snippet content through the same verified paste or Copy-only delivery path while excluding command phrases from Recovery.

### Fixed

- Fail closed on malformed, non-text, oversized, duplicate, or unknown snippets instead of inserting ambiguous content.

## [1.12.0] - 2026-08-12

### Added

- Added opt-in exact whole-utterance voice commands in English and Slovenian for new line, new paragraph, bullet point, Undo, and Cancel.
- Added a clear Dictation-page switch plus command resolver, config, packaging, clean-install, upgrade, accessibility, interaction, and layout coverage.

### Changed

- Run commands locally before optional AI cleanup and exclude them from transcript Recovery.
- Mark the injected Undo shortcut so VoicePrompt's global hotkey filter ignores its own input.

### Fixed

- Require an exact normalized utterance, preventing command phrases embedded in normal dictated sentences from firing accidentally.

## [1.11.0] - 2026-08-12

### Added

- Added a conservative **Clean** writing mode between Verbatim and Grammar for filler words, immediate repetitions, punctuation, and capitalization.
- Added runtime prompt-contract, incomplete-response fallback, settings-validation, UI interaction, and scrolled writing-panel layout coverage.

### Changed

- Renamed the visible **Off** writing choice to **Verbatim** while preserving the existing `off` setting and zero-delay local behavior.
- Present all four writing levels together: Verbatim, Clean, Grammar, and Prompt.

### Fixed

- Apply the same completeness guard to Clean and Grammar so an overly short provider response falls back to the full original transcript.

## [1.10.0] - 2026-08-12

### Added

- Added an opt-in **Copy only** transcript output mode for applications that block or mishandle synthetic paste.
- Added deterministic routing, failure-propagation, persisted-setting, clean-install, upgrade, and UI layout coverage for both output modes.

### Changed

- Keep **Paste into app** as the default while making the selected delivery behavior explicit on the Dictation page.
- Verify the complete Windows Unicode clipboard payload in Copy-only mode and send no paste keystroke.
- Report paste and clipboard completion separately in privacy-safe performance diagnostics.

### Fixed

- Use delivery-neutral recovery notifications when a transcript was recognized successfully but its selected output route fails.

## [1.9.0] - 2026-08-12

### Added

- Added portable language-profile export and import for the selected language, recognition context, hotwords, and personal corrections.
- Added strict profile schema, version, language, correction, field-length, and 128 KB file-size validation with atomic exports.
- Added clean round-trip, Unicode, privacy-exclusion, Auto-normalization, unsupported-language, and oversized-input tests.

### Changed

- Import only updates the visible language fields for review; users still choose **Save & restart** before the live runtime changes.
- Exclude API credentials, AI provider settings, transcript history, microphone names, hotkeys, audio settings, and hardware choices from every exported profile.
- Keep English + Slovenian Auto as the default; imported languages remain explicit opt-in choices and reuse the installed multilingual model.

## [1.8.0] - 2026-08-12

### Added

- Added **Copy last transcript** directly to the tray menu so a missed or blocked paste can be recovered without opening Settings.
- Added deterministic latest-entry coverage that skips malformed empty items and fails closed when recovery history is unavailable.

### Changed

- Use Windows clipboard retry support for tray recovery, Recovery-page copying, and copied diagnostics so another application briefly owning the clipboard is less likely to interrupt the action.
- Keep clipboard balloons privacy-safe: they confirm success without showing transcript contents.

## [1.7.0] - 2026-08-12

### Added

- Added a live microphone input test to the Audio workspace with clear quiet, good-signal, and very-loud feedback while the configured hotkey is held.
- Added an automated shared-memory signal test and a dedicated rendered Audio-page screenshot for the active input meter.

### Changed

- Share one bounds-checked audio-meter reader between the overlay and settings UI instead of opening another microphone capture, polling disk, or running additional model work.
- Start the 33 Hz settings meter only while the Audio page is visible and stop it immediately when the page or window is hidden.

## [1.6.0] - 2026-08-12

### Added

- Added an in-app manual update check with clear current/latest version status and a direct link to the official stable GitHub release.
- Added deterministic tests for stable-version parsing, request privacy headers, newer/current releases, HTTP failures, and oversized responses.
- Added a scrolled Advanced-tools screenshot to the Windows layout gate so controls below the fold are visually reviewable.

### Changed

- Keep update checks explicitly user-triggered: VoicePrompt makes no hidden startup request and never downloads or installs an update silently.
- Bound the public GitHub response to 512 KB and the complete request to three seconds so unavailable networking cannot slow the settings app.

### Fixed

- Prevent fill-docked status labels from covering adjacent action buttons after normal window layout, including performance Refresh and update Check now.

## [1.5.1] - 2026-08-12

### Fixed

- Install NumPy in the clean GitHub release test environment so buffered-transcription tests run before any downloadable assets are published.

## [1.5.0] - 2026-08-12

### Added

- Added lossless buffered transcription for long local recordings, decoding complete speech blocks in the background while retaining the entire microphone stream.
- Added a Fast long recordings control and privacy-safe diagnostics for background batch count, compute time, release wait, and full-audio fallback use.
- Added orchestration, aggregate-performance, clean-install, legacy-upgrade, and byte-for-byte patch idempotence tests.

### Changed

- Keep short and uninterrupted recordings on the proven one-pass batch path; long recordings still paste exactly once and never type partial text while the hotkey is held.
- Serialize all model work on one worker so speech blocks, fallback, and final paste remain ordered without increasing concurrent GPU load.

### Fixed

- Reduced measured post-release wait for a real-time 99.2-second English sample from a full after-release decode to 0.49 seconds without changing its measured 7.04% word error rate.
- Fall back automatically to one complete-audio decode after any empty, failed, incomplete, VAD, or tail-batching result instead of risking a partial prompt.
- Prevent repeated installer runs from duplicating buffered transcription handlers in the patched runtime.

## [1.4.0] - 2026-08-12

### Added

- Added searchable opt-in recognition profiles for all 100 languages supported by Whisper large-v3 while keeping English + Slovenian Auto as the default.
- Added a privacy-safe Recent performance panel with latest recognition time, median, p95, microphone readiness, fallback frequency, and real-time decoding speed.
- Added bounded log-tail parsing and automated catalog, percentile, throughput, large-log, pinned-language, and Windows UI behavior tests.

### Changed

- Selecting an additional language automatically pins recognition to that language, bypassing auto-detection without downloading another model or adding GPU overhead.
- Copied diagnostics now include aggregate timing metadata but continue to exclude audio and transcript text.
- Run the Windows layout checker on an STA thread so searchable native controls are tested under the same contract as the shipped app.

### Fixed

- Reject unsupported hand-entered language codes before restarting the runtime instead of allowing a delayed faster-whisper failure.
- Kept the multilingual selector visually consistent with the dark interface and clarified that the installed multilingual model already contains every language profile.

## [1.3.0] - 2026-08-12

### Added

- Added bounded local transcript recovery with inspection, copy, delete, clear, enable, and retention controls.
- Added explicit personal corrections using `misheard => intended` rules, applied locally before optional AI cleanup.
- Added a dedicated Recovery workspace and automated Unicode, corruption, disabled-retention, and bounded-history tests.

### Changed

- Store the final completed text before automatic paste so a target-application or clipboard failure cannot erase the prompt.
- Keep only the newest configured 5 to 100 entries, with 20 as the default, and never store microphone audio.
- Expanded settings navigation and keyboard shortcuts to six focused workspaces while preserving minimum-window layout support.

### Fixed

- Made clean installs and upgrades deploy the same recovery and correction modules through the idempotent runtime patcher.
- Corrected sidebar docking order after adding the Recovery workspace.

## [1.2.4] - 2026-08-12

### Changed

- Made the primary Auto decode language-neutral and apply colloquial Slovenian hints only to a Slovenian recovery pass.
- Skip a low-confidence English retry when Whisper's transcript score is already strong, removing unnecessary second-pass latency.
- Start new installations with an empty recognition prompt so personal vocabulary remains useful without biasing English/Slovenian detection.

### Fixed

- Replace unsupported Finnish, Spanish, Latin, and similar Auto detections with Whisper's most likely English/Slovenian candidate instead of comparing uncalibrated cross-language decoder scores.
- Keep English recovery prompts and hotwords isolated from Slovenian slang hints, preventing English dictation from drifting or translating into Slovenian.
- Migrate the exact older Slovenian-heavy default prompt to the neutral default while preserving every custom user prompt.

## [1.2.3] - 2026-08-12

### Changed

- Restored the recording overlay's neutral graphite and gray palette, reduced its width, and refined the microphone and waveform proportions.
- Added microphone activation timing to privacy-safe diagnostics so cold device starts can be distinguished from hotkey and overlay delays.

### Fixed

- Publish recording feedback as soon as F1 activates instead of waiting for Windows to open an idle microphone device.
- Removed the overlay's slow hidden polling and multi-frame fade-in, reducing measured cold display activation to about 31 ms.
- Keep activation feedback coherent when WebSocket or microphone initialization fails.

## [1.2.2] - 2026-08-12

### Changed

- Raised the recommended VAD segment size to 180 seconds while keeping recording duration independent from that internal segmentation setting.
- Sized optional AI cleanup output for multi-minute dictation and reject provider truncation instead of pasting a partial rewrite.
- Added privacy-safe successful-paste telemetry with character counts, without logging transcript content.

### Fixed

- Removed the batch recorder's silent 90-second buffer cap, which discarded every word after 90 seconds while the overlay kept recording.
- Added bounded Windows clipboard contention retries, verified full Unicode clipboard writes, and explicit failures instead of silently reporting a failed paste as successful.
- Preserve the completed transcript on the clipboard when automatic paste injection fails so it remains recoverable with `Ctrl+V`.

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
