import unittest

from accuracy_metrics import (
    character_error_rate,
    repeated_phrase_rate,
    summarize,
    word_error_rate,
)


class AccuracyMetricTests(unittest.TestCase):
    def test_word_and_character_error_rates_are_deterministic(self):
        self.assertEqual(word_error_rate("Hello, Slovenija", "hello Slovenija"), 0.0)
        self.assertAlmostEqual(word_error_rate("one two three", "one four three"), 1 / 3)
        self.assertGreater(character_error_rate("čudovito", "cudovito"), 0.0)

    def test_repeated_phrase_rate_flags_immediate_loops(self):
        self.assertEqual(repeated_phrase_rate("this is normal spoken text"), 0.0)
        self.assertGreater(repeated_phrase_rate("make it work make it work make it work"), 0.0)

    def test_summary_tracks_language_accuracy_and_tail_latency(self):
        report = summarize([
            {
                "reference": "hello world",
                "transcript": "hello world",
                "expected_language": "en",
                "detected_language": "en",
                "latency_seconds": 0.4,
            },
            {
                "reference": "dober dan",
                "transcript": "dober dan",
                "expected_language": "sl",
                "detected_language": "en",
                "latency_seconds": 1.2,
            },
        ])
        self.assertEqual(report["samples"], 2)
        self.assertEqual(report["wer"], 0.0)
        self.assertEqual(report["language_accuracy"], 0.5)
        self.assertEqual(report["latency_p50_seconds"], 0.4)
        self.assertEqual(report["latency_p95_seconds"], 1.2)


if __name__ == "__main__":
    unittest.main()
