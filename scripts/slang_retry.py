"""Language routing for mixed English and colloquial Slovenian dictation."""

from __future__ import annotations

import math
from collections.abc import Iterable
from typing import Any


_UNSUPPORTED_LANGUAGE_MAX_LOSS = 0.05
_RECENT_LANGUAGE_SWITCH_RATIO = 1.5
_SUPPORTED_RETRY_MAX_SECONDS = 12.0
_SUPPORTED_RETRY_MAX_CONFIDENCE = 0.70
_SUPPORTED_RETRY_RATIO = 0.60
_LANGUAGE_EVIDENCE_WEIGHT = 0.35
_RECENT_LANGUAGE_BONUS = 0.08
_SUPPORTED_RETRY_MIN_GAIN = 0.02
_AUTO_MODES = {"", "auto", "sl-slang"}
_SUPPORTED_LANGUAGES = {"en", "sl"}

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
    """Keep the primary Auto pass language-neutral."""
    return base_prompt


def recognition_hotwords(configured: str, base_hotwords: str) -> str:
    """Keep the primary Auto pass language-neutral."""
    return base_hotwords


def bilingual_retry_language(
    configured: str,
    detected: str,
    confidence: float = 1.0,
    primary_score: float = float("-inf"),
    language_probabilities: Iterable[tuple[str, float]] | None = None,
    recent_language: str | None = None,
    audio_seconds: float = float("inf"),
) -> str | None:
    """Choose an English/Slovenian retry for ambiguous Auto-mode speech."""
    if configured not in _AUTO_MODES:
        return None

    probabilities = dict(language_probabilities or ())
    if detected in _SUPPORTED_LANGUAGES:
        # Never turn a primary English decode into Slovenian. An English label
        # can be imperfect, but a forced Slovenian pass is the exact failure
        # Auto mode must avoid.
        if detected == "en":
            return None
        if audio_seconds > _SUPPORTED_RETRY_MAX_SECONDS:
            return None
        detected_probability = probabilities.get(detected, confidence)
        if detected_probability >= _SUPPORTED_RETRY_MAX_CONFIDENCE:
            return None
        other_language = "en"
        other_probability = probabilities.get(other_language, 0.0)
        if recent_language == other_language:
            return other_language
        if (
            detected_probability > 0
            and other_probability >= detected_probability * _SUPPORTED_RETRY_RATIO
        ):
            return other_language
        return None

    # VoicePrompt Auto is intentionally bilingual. Constrain unrelated
    # 100-language guesses to the two languages selected by the default profile.
    if recent_language in _SUPPORTED_LANGUAGES:
        other_language = "sl" if recent_language == "en" else "en"
        recent_probability = probabilities.get(recent_language, 0.0)
        other_probability = probabilities.get(other_language, 0.0)
        if other_probability <= recent_probability * _RECENT_LANGUAGE_SWITCH_RATIO:
            return recent_language
    return "sl" if probabilities.get("sl", 0.0) > probabilities.get("en", 0.0) else "en"


def should_retry_as_slovenian(
    configured: str,
    detected: str,
    confidence: float = 1.0,
    primary_score: float = float("-inf"),
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


def transcript_is_plausible(
    segments: Iterable[Any],
    audio_seconds: float,
) -> bool:
    """Reject text expansion that cannot fit in the recorded speech."""
    if not math.isfinite(audio_seconds) or audio_seconds < 0:
        return False
    text = " ".join(
        str(getattr(segment, "text", "")).strip()
        for segment in segments
    ).strip()
    if not text:
        return True
    return (
        len(text) <= max(120, int(audio_seconds * 50))
        and len(text.split()) <= max(24, int(audio_seconds * 8))
    )


def prefer_bilingual_retry(
    retry_language: str,
    detected: str,
    original_segments: Iterable[Any],
    retry_segments: Iterable[Any],
    language_probabilities: Iterable[tuple[str, float]] | None = None,
    recent_language: str | None = None,
) -> bool:
    """Choose the bilingual candidate with the stronger combined evidence."""
    original_score = transcript_score(original_segments)
    retry_score = transcript_score(retry_segments)
    if not math.isfinite(retry_score):
        return False
    if detected in _SUPPORTED_LANGUAGES:
        if retry_language not in _SUPPORTED_LANGUAGES or retry_language == detected:
            return False
        probabilities = dict(language_probabilities or ())
        detected_probability = probabilities.get(detected, 0.0)
        retry_probability = probabilities.get(retry_language, 0.0)
        if detected_probability <= 0 or retry_probability <= 0:
            return False
        if not math.isfinite(original_score):
            return True
        original_evidence = original_score + _LANGUAGE_EVIDENCE_WEIGHT * math.log(
            detected_probability
        )
        retry_evidence = retry_score + _LANGUAGE_EVIDENCE_WEIGHT * math.log(
            retry_probability
        )
        if recent_language == retry_language:
            retry_evidence += _RECENT_LANGUAGE_BONUS
        return retry_evidence >= original_evidence + _SUPPORTED_RETRY_MIN_GAIN
    if not math.isfinite(original_score):
        return True
    return retry_score >= original_score - _UNSUPPORTED_LANGUAGE_MAX_LOSS


def prefer_slovenian_retry(
    detected: str,
    original_segments: Iterable[Any],
    retry_segments: Iterable[Any],
) -> bool:
    """Backward-compatible wrapper for a forced-Slovenian retry."""
    return prefer_bilingual_retry("sl", detected, original_segments, retry_segments)
