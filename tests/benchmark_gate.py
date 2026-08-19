"""Fail a build when a bench_one JSONL summary misses explicit quality gates."""

from __future__ import annotations

import argparse
import json
from pathlib import Path


def read_summary(path: Path) -> dict:
    summary: dict | None = None
    for line in path.read_text(encoding="utf-8").splitlines():
        try:
            value = json.loads(line)
        except json.JSONDecodeError:
            continue
        if isinstance(value, dict) and value.get("event") == "summary":
            summary = value
    if summary is None:
        raise ValueError("benchmark output has no JSON summary event")
    return summary


def evaluate(
    summary: dict,
    max_wer: float,
    min_language_accuracy: float,
    max_p95: float,
    language_wer_limits: dict[str, float] | None = None,
) -> list[str]:
    failures: list[str] = []
    if int(summary.get("samples", 0)) < 2:
        failures.append("at least two samples are required")
    if float(summary.get("wer", 1.0)) > max_wer:
        failures.append(f"WER {float(summary.get('wer', 1.0)):.3f} > {max_wer:.3f}")
    if float(summary.get("language_accuracy", 0.0)) < min_language_accuracy:
        failures.append(
            f"language accuracy {float(summary.get('language_accuracy', 0.0)):.3f} < {min_language_accuracy:.3f}"
        )
    if float(summary.get("latency_p95_seconds", float("inf"))) > max_p95:
        failures.append(
            f"p95 latency {float(summary.get('latency_p95_seconds', 0.0)):.3f}s > {max_p95:.3f}s"
        )
    by_language = summary.get("by_language", {})
    for language, limit in (language_wer_limits or {}).items():
        values = by_language.get(language) if isinstance(by_language, dict) else None
        if not isinstance(values, dict):
            failures.append(f"missing {language} benchmark summary")
        elif float(values.get("wer", 1.0)) > limit:
            failures.append(f"{language} WER {float(values.get('wer', 1.0)):.3f} > {limit:.3f}")
    return failures


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("results", type=Path)
    parser.add_argument("--max-wer", type=float, default=0.15)
    parser.add_argument("--min-language-accuracy", type=float, default=0.95)
    parser.add_argument("--max-p95-seconds", type=float, default=2.0)
    parser.add_argument("--max-en-wer", type=float, default=0.10)
    parser.add_argument("--max-sl-wer", type=float, default=0.25)
    args = parser.parse_args()
    summary = read_summary(args.results)
    failures = evaluate(
        summary,
        args.max_wer,
        args.min_language_accuracy,
        args.max_p95_seconds,
        {"en": args.max_en_wer, "sl": args.max_sl_wer},
    )
    print(json.dumps({"ok": not failures, "failures": failures, "summary": summary}, ensure_ascii=False))
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
