from __future__ import annotations

import unittest

from benchmark_gate import evaluate


class BenchmarkGateTests(unittest.TestCase):
    def test_passing_summary_has_no_failures(self) -> None:
        summary = {
            "samples": 20,
            "wer": 0.08,
            "language_accuracy": 1.0,
            "latency_p95_seconds": 0.8,
            "by_language": {"en": {"wer": 0.04}, "sl": {"wer": 0.12}},
        }
        self.assertEqual(evaluate(summary, 0.15, 0.95, 2.0, {"en": 0.1, "sl": 0.25}), [])

    def test_each_regression_is_reported(self) -> None:
        summary = {"samples": 1, "wer": 0.2, "language_accuracy": 0.5, "latency_p95_seconds": 3.0}
        failures = evaluate(summary, 0.15, 0.95, 2.0)
        self.assertEqual(len(failures), 4)

    def test_language_regression_cannot_hide_inside_aggregate(self) -> None:
        summary = {
            "samples": 20,
            "wer": 0.10,
            "language_accuracy": 1.0,
            "latency_p95_seconds": 0.8,
            "by_language": {"en": {"wer": 0.03}, "sl": {"wer": 0.31}},
        }
        self.assertEqual(
            evaluate(summary, 0.15, 0.95, 2.0, {"en": 0.1, "sl": 0.25}),
            ["sl WER 0.310 > 0.250"],
        )


if __name__ == "__main__":
    unittest.main()
