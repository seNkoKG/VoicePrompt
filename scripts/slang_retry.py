"""Language routing for mixed English and colloquial Slovenian dictation."""

from __future__ import annotations

import math
from collections.abc import Iterable
from typing import Any


_LOW_CONFIDENCE_ENGLISH = 0.75
_AUTO_MODES = {"", "auto", "sl-slang"}

SLOVENIAN_SLANG_PROMPT = (
    "Dej, a lohk tole zrihtaš? Kva tle ne štima? Zdej sam poglej, pol pa dej nazaj. "
    "Ful je fajn, čist kul, itak, ziher. Rabim neki na hitrco, tko da bo delal."
)
SLOVENIAN_SLANG_HOTWORDS = (
    "dej, lohk, kva, tko, tle, zdej, pol, sam, ful, čist, kul, ziher, itak, fajn, "
    "štima, zrihtaj, pejt, rabim, neki, nič, mal, hitrco"
)


def recognition_language(configured: str) -> str | None:
    """Return the faster-whisper language, leaving hybrid modes on detection."""
    return None if configured in {"", "auto", "sl-slang"} else configured


def recognition_prompt(configured: str, base_prompt: str) -> str:
    """Add bilingual vocabulary to Auto without pinning the decoded language."""
    return slovenian_retry_prompt(base_prompt) if configured in _AUTO_MODES else base_prompt


def recognition_hotwords(configured: str, base_hotwords: str) -> str:
    """Boost colloquial Slovenian words in Auto while language ID stays automatic."""
    return slovenian_retry_hotwords(base_hotwords) if configured in _AUTO_MODES else base_hotwords


def bilingual_retry_language(
    configured: str,
    detected: str,
    confidence: float = 1.0,
    primary_score: float = 0.0,
    language_probabilities: Iterable[tuple[str, float]] | None = None,
) -> str | None:
    """Choose an English/Slovenian retry for ambiguous Auto-mode speech."""
    if configured not in _AUTO_MODES:
        return None
    if detected == "sl":
        # Auto's primary pass already uses the Slovenian vocabulary profile.
        # Repeating the same deterministic decode would only add latency.
        return None
    if detected == "en":
        return "sl" if confidence < _LOW_CONFIDENCE_ENGLISH else None

    # VoicePrompt Auto is intentionally bilingual. This mirrors established
    # dictation apps that constrain automatic detection to the languages the
    # user actually speaks instead of accepting an unrelated 99-language guess.
    probabilities = dict(language_probabilities or ())
    return "sl" if probabilities.get("sl", 0.0) >= probabilities.get("en", 0.0) else "en"


def should_retry_as_slovenian(
    configured: str,
    detected: str,
    confidence: float = 1.0,
    primary_score: float = 0.0,
) -> bool:
    """Backward-compatible predicate for callers interested in Slovenian only."""
    return (
        bilingual_retry_language(configured, detected, confidence, primary_score)
        == "sl"
    )


def slovenian_retry_prompt(base_prompt: str) -> str:
    """Add colloquial Slovenian examples once."""
    clean = base_prompt.strip()
    if SLOVENIAN_SLANG_PROMPT in clean:
        return clean
    return f"{clean} {SLOVENIAN_SLANG_PROMPT}".strip()


def slovenian_retry_hotwords(base_hotwords: str) -> str:
    """Add deduplicated Slovenian slang terms while preserving original case."""
    words = [
        word.strip()
        for word in f"{base_hotwords}, {SLOVENIAN_SLANG_HOTWORDS}".split(",")
        if word.strip()
    ]
    result: list[str] = []
    seen: set[str] = set()
    for word in words:
        key = word.casefold()
        if key not in seen:
            seen.add(key)
            result.append(word)
    return ", ".join(result)


def bilingual_retry_prompt(language: str, base_prompt: str) -> str:
    """Return language-specific hints for an ambiguity retry."""
    return slovenian_retry_prompt(base_prompt) if language == "sl" else base_prompt


def bilingual_retry_hotwords(language: str, base_hotwords: str) -> str:
    """Return language-specific hotwords for an ambiguity retry."""
    return slovenian_retry_hotwords(base_hotwords) if language == "sl" else base_hotwords


def transcript_score(segments: Iterable[Any]) -> float:
    """Return Whisper's token-weighted average log probability for a transcript."""
    total = 0.0
    weight_total = 0
    for segment in segments:
        if not str(getattr(segment, "text", "")).strip():
            continue
        score = float(getattr(segment, "avg_logprob", float("-inf")))
        if not math.isfinite(score):
            continue
        tokens = getattr(segment, "tokens", None) or ()
        weight = max(1, len(tokens))
        total += score * weight
        weight_total += weight
    return total / weight_total if weight_total else float("-inf")


def prefer_bilingual_retry(
    retry_language: str,
    detected: str,
    original_segments: Iterable[Any],
    retry_segments: Iterable[Any],
) -> bool:
    """Choose a forced bilingual pass only when its decoder score supports it."""
    original_score = transcript_score(original_segments)
    retry_score = transcript_score(retry_segments)
    if not math.isfinite(retry_score):
        return False
    if not math.isfinite(original_score):
        return True

    # Requiring a real gain when switching away from English prevents a short
    # genuine English phrase from being translated. Same-language Slovenian
    # retries need a small improvement; unrelated detections slightly favor the
    # best supported bilingual candidate over an unsupported-language output.
    if detected == "en" and retry_language == "sl":
        required_gain = 0.03
    elif detected == retry_language:
        required_gain = 0.01
    else:
        required_gain = -0.05
    return retry_score >= original_score + required_gain


def prefer_slovenian_retry(
    detected: str,
    original_segments: Iterable[Any],
    retry_segments: Iterable[Any],
) -> bool:
    """Backward-compatible wrapper for a forced-Slovenian retry."""
    return prefer_bilingual_retry("sl", detected, original_segments, retry_segments)
