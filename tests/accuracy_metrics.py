"""Deterministic speech-quality metrics for local VoicePrompt evaluations."""

from __future__ import annotations

import math
import re
import unicodedata
from collections.abc import Sequence


def _words(text: str) -> list[str]:
    normalized = unicodedata.normalize("NFKC", text).casefold()
    return re.findall(r"[^\W_]+(?:['’][^\W_]+)?", normalized, re.UNICODE)


def _distance(reference: Sequence[str], hypothesis: Sequence[str]) -> int:
    previous = list(range(len(hypothesis) + 1))
    for row, expected in enumerate(reference, 1):
        current = [row]
        for column, actual in enumerate(hypothesis, 1):
            current.append(min(
                current[-1] + 1,
                previous[column] + 1,
                previous[column - 1] + (expected != actual),
            ))
        previous = current
    return previous[-1]


def word_error_rate(reference: str, hypothesis: str) -> float:
    expected = _words(reference)
    actual = _words(hypothesis)
    return _distance(expected, actual) / max(1, len(expected))


def character_error_rate(reference: str, hypothesis: str) -> float:
    expected = list(unicodedata.normalize("NFKC", reference).casefold())
    actual = list(unicodedata.normalize("NFKC", hypothesis).casefold())
    return _distance(expected, actual) / max(1, len(expected))


def repeated_phrase_rate(text: str, size: int = 3) -> float:
    words = _words(text)
    if len(words) < size * 2:
        return 0.0
    repeated = sum(
        words[index:index + size] == words[index - size:index]
        for index in range(size, len(words) - size + 1)
    )
    return repeated / max(1, len(words) - size + 1)


def percentile(values: Sequence[float], fraction: float) -> float:
    ordered = sorted(values)
    if not ordered:
        return 0.0
    index = max(0, min(len(ordered) - 1, math.ceil(len(ordered) * fraction) - 1))
    return ordered[index]


def summarize(records: Sequence[dict]) -> dict[str, float | int]:
    scored = [record for record in records if record.get("reference")]
    language_records = [record for record in records if record.get("expected_language")]
    latencies = [float(record["latency_seconds"]) for record in records]
    return {
        "samples": len(records),
        "wer": sum(word_error_rate(record["reference"], record["transcript"]) for record in scored) / max(1, len(scored)),
        "cer": sum(character_error_rate(record["reference"], record["transcript"]) for record in scored) / max(1, len(scored)),
        "language_accuracy": sum(
            record["expected_language"] == record.get("detected_language")
            for record in language_records
        ) / max(1, len(language_records)),
        "repeated_phrase_rate": sum(repeated_phrase_rate(record["transcript"]) for record in records) / max(1, len(records)),
        "latency_p50_seconds": percentile(latencies, 0.50),
        "latency_p95_seconds": percentile(latencies, 0.95),
    }
