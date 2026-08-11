"""Language routing for mixed English and colloquial Slovenian dictation."""

from __future__ import annotations


def recognition_language(configured: str) -> str | None:
    """Return the faster-whisper language, leaving hybrid modes on detection."""
    return None if configured in {"", "auto", "sl-slang"} else configured


def should_retry_as_slovenian(configured: str, detected: str) -> bool:
    """Retry only third-language mistakes; never reinterpret English as Slovenian."""
    return configured == "sl-slang" and detected not in {"en", "sl"}
