# VoicePrompt Privacy Notice

Effective date: 20 August 2026

This notice describes the current VoicePrompt application and project website.
VoicePrompt is currently distributed as a free preview with no account or
payment system.

## Project contact

VoicePrompt is maintained by seNkoKG. Use the project issue tracker for
non-sensitive questions:

https://github.com/seNkoKG/VoicePrompt/issues

Do not post recordings, transcripts, API keys, or other sensitive information
in a public issue.

## Default application behavior

VoicePrompt does not require an account. It contains no first-party analytics,
advertising SDK, telemetry, or crash-reporting service. Speech recognition runs
on the user's computer by default. VoicePrompt does not save microphone audio.

The application stores settings, optional transcript recovery history, personal
corrections, snippets, application profiles, and local logs on the user's
computer. API keys entered for optional AI cleanup are encrypted for the current
Windows account. Exported backups exclude API keys, transcript history,
microphone identity, logs, and machine paths.

## Network activity

VoicePrompt can make these user-initiated or setup-related connections:

- Installation downloads pinned Python packages from their package hosts.
- First use downloads the selected Whisper model from Hugging Face.
- Manual update checks and confirmed downloads contact GitHub release services.
- Optional AI cleanup sends completed transcript text to the provider configured
  by the user. Selected text is included only after an explicit Command or Ukaz
  action.
- Optional compatible-server recognition sends recorded audio to the server
  configured by the user.

VoicePrompt does not control an optional provider or server. Its operator's
privacy terms apply to data sent there.

## Local data control

Users can disable recovery history, clear saved history, edit or remove
corrections and snippets, export settings, or delete VoicePrompt's local files.
Uninstall instructions and exact storage locations appear in the project
documentation.

## Project website

The current project website uses no first-party analytics, forms, accounts, or
advertising cookies. It stores only the selected visual theme in browser local
storage. GitHub currently hosts the site and downloads, so GitHub may process
ordinary request data under its own privacy statement.

## Payments

VoicePrompt currently accepts no payments and collects no card, billing, or
order information. If a paid version is offered later, the checkout page will
identify the seller or payment provider and show the terms and privacy notice
that apply before payment.

## Changes

Material changes will be dated and published with the application or project
website. Mandatory privacy rights under applicable law are not limited by this
notice.
