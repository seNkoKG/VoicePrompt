"""Tests for mixed English and Slovenian slang language routing."""

import unittest
from dataclasses import dataclass

from scripts.slang_retry import (
    bilingual_retry_hotwords,
    bilingual_retry_language,
    bilingual_retry_prompt,
    prefer_bilingual_retry,
    prefer_slovenian_retry,
    recognition_hotwords,
    recognition_language,
    recognition_prompt,
    should_retry_as_slovenian,
    slovenian_retry_hotwords,
    slovenian_retry_prompt,
    transcript_score,
)


@dataclass
class Segment:
    text: str
    avg_logprob: float
    tokens: list[int]


class SlangRetryTests(unittest.TestCase):
    def test_hybrid_mode_keeps_automatic_detection(self) -> None:
        self.assertIsNone(recognition_language("sl-slang"))
        self.assertIsNone(recognition_language("auto"))
        self.assertEqual(recognition_language("en"), "en")
        self.assertEqual(recognition_language("sl"), "sl")

    def test_confident_english_and_slovenian_stay_single_pass(self) -> None:
        self.assertFalse(should_retry_as_slovenian("", "en", 0.75))
        self.assertFalse(should_retry_as_slovenian("auto", "en", 0.95))
        self.assertFalse(should_retry_as_slovenian("", "sl", 0.20, -0.90))

    def test_low_confidence_english_retries_for_slang(self) -> None:
        self.assertTrue(should_retry_as_slovenian("", "en", 0.74))
        self.assertTrue(should_retry_as_slovenian("auto", "en", 0.20))
        self.assertFalse(should_retry_as_slovenian("en", "en", 0.20))

    def test_strong_english_transcript_skips_an_unnecessary_retry(self) -> None:
        self.assertIsNone(bilingual_retry_language("", "en", 0.60, -0.20))
        self.assertEqual(bilingual_retry_language("", "en", 0.60, -0.70), "sl")

    def test_auto_primary_pass_stays_language_neutral(self) -> None:
        prompt = recognition_prompt("", "Python in API.")
        self.assertEqual(prompt, "Python in API.")
        self.assertNotIn("Dej, a lohk", prompt)
        self.assertEqual(recognition_prompt("auto", prompt), "Python in API.")
        self.assertEqual(recognition_prompt("en", "English prompt"), "English prompt")
        hotwords = recognition_hotwords("auto", "OpenAI, dej")
        self.assertEqual(hotwords, "OpenAI, dej")
        self.assertEqual([word.strip() for word in hotwords.split(",")].count("dej"), 1)
        self.assertEqual(recognition_hotwords("en", "OpenAI"), "OpenAI")

    def test_third_language_mistakes_retry_as_slovenian(self) -> None:
        probabilities = [("en", 0.1), ("sl", 0.4)]
        for detected in ("lt", "nn", "hr", "de"):
            with self.subTest(detected=detected):
                self.assertEqual(
                    bilingual_retry_language("", detected, 0.99, 0.0, probabilities),
                    "sl",
                )
        self.assertFalse(should_retry_as_slovenian("en", "lt"))

    def test_third_language_mistake_can_retry_as_english(self) -> None:
        probabilities = [("en", 0.6), ("sl", 0.1)]
        self.assertEqual(
            bilingual_retry_language("auto", "de", 0.8, 0.0, probabilities),
            "en",
        )

    def test_retry_hints_are_added_once(self) -> None:
        prompt = slovenian_retry_prompt("Python in API.")
        self.assertIn("Dej, a lohk", prompt)
        self.assertEqual(slovenian_retry_prompt(prompt), prompt)
        hotwords = slovenian_retry_hotwords("Python, dej")
        self.assertIn("Python", hotwords)
        self.assertEqual([word.strip() for word in hotwords.split(",")].count("dej"), 1)
        self.assertEqual(bilingual_retry_prompt("en", "English prompt"), "English prompt")
        self.assertEqual(bilingual_retry_hotwords("en", "OpenAI"), "OpenAI")

    def test_score_is_weighted_by_decoder_tokens(self) -> None:
        segments = [
            Segment("short", -0.2, [1]),
            Segment("long", -0.8, [1, 2, 3]),
        ]
        self.assertAlmostEqual(transcript_score(segments), -0.65)

    def test_better_slovenian_retry_is_accepted(self) -> None:
        english_guess = [Segment("Day low hook.", -0.72, [1, 2, 3])]
        slovenian = [Segment("Dej, a lohk?", -0.31, [1, 2, 3])]
        self.assertTrue(prefer_slovenian_retry("en", english_guess, slovenian))

    def test_worse_slovenian_retry_cannot_translate_real_english(self) -> None:
        english = [Segment("Please open the file.", -0.25, [1, 2, 3, 4])]
        forced_slovenian = [Segment("Prosim odpri datoteko.", -0.64, [1, 2, 3, 4])]
        self.assertFalse(prefer_slovenian_retry("en", english, forced_slovenian))

    def test_supported_english_retry_replaces_unrelated_language(self) -> None:
        unrelated = [Segment("Guten Tag", -0.50, [1, 2])]
        english = [Segment("Good day", -0.53, [1, 2])]
        self.assertTrue(prefer_bilingual_retry("en", "de", unrelated, english))

    def test_supported_retry_replaces_a_higher_scoring_unrelated_language(self) -> None:
        finnish_guess = [Segment("Avaa tiedosto.", -0.20, [1, 2, 3])]
        accented_english = [Segment("Open the file.", -0.62, [1, 2, 3])]
        self.assertTrue(prefer_bilingual_retry("en", "fi", finnish_guess, accented_english))

    def test_empty_retry_is_never_selected(self) -> None:
        original = [Segment("nekaj", -0.7, [1])]
        self.assertFalse(prefer_slovenian_retry("nn", original, []))


if __name__ == "__main__":
    unittest.main(verbosity=2)
