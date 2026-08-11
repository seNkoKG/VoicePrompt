"""Production decoding defaults for low-latency dictation."""

from __future__ import annotations


def decoding_options(temperature: float) -> dict[str, object]:
    """Return accurate, latency-bounded options for every local decode.

    A temperature tuple makes faster-whisper decode the same 30-second window
    again whenever a quality threshold fires. That made release-to-paste time
    unpredictable and could stack with the bilingual recovery pass. A scalar
    keeps the configured beam-search result while the native repetition guards
    still prevent failure loops.
    """
    return {
        "temperature": temperature,
        "condition_on_previous_text": False,
        "repetition_penalty": 1.1,
        "no_repeat_ngram_size": 3,
    }
