"""Production decoding defaults that prevent Whisper repetition loops."""

from __future__ import annotations


def decoding_options(temperature: float) -> dict[str, object]:
    """Return faster-whisper options tuned for short-form dictation."""
    return {
        "temperature": (
            (0.0, 0.2, 0.4, 0.6, 0.8, 1.0) if temperature == 0.0 else temperature
        ),
        "condition_on_previous_text": False,
        "repetition_penalty": 1.1,
        "no_repeat_ngram_size": 3,
    }
