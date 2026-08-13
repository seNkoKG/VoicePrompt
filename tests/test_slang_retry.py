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
    transcript_is_plausible,
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
        self.assertEqual(recognition_language("de"), "de")
        self.assertEqual(recognition_language("yue"), "yue")

    def test_confident_english_and_slovenian_stay_single_pass(self) -> None:
        self.assertFalse(should_retry_as_slovenian("", "en", 0.75))
        self.assertFalse(should_retry_as_slovenian("auto", "en", 0.95))
        self.assertFalse(should_retry_as_slovenian("", "sl", 0.20, -0.90))
        self.assertIsNone(bilingual_retry_language("", "sl", 0.75, -0.90))

    def test_ambiguous_short_supported_language_tests_the_other_candidate(self) -> None:
        probabilities = [("sl", 0.48), ("en", 0.34)]
        self.assertEqual(
            bilingual_retry_language(
                "auto", "sl", 0.48, -0.55, probabilities, "en", 2.9
            ),
            "en",
        )
        self.assertIsNone(
            bilingual_retry_language(
                "auto", "en", 0.48, -0.55,
                [("en", 0.48), ("sl", 0.34)], "sl", 2.9,
            )
        )

    def test_observed_seven_second_english_mislabel_is_cross_checked(self) -> None:
        self.assertEqual(
            bilingual_retry_language(
                "auto",
                "sl",
                0.54,
                -0.55,
                [("sl", 0.54), ("en", 0.15)],
                "en",
                7.264,
            ),
            "en",
        )

    def test_detected_english_is_never_replaced_by_slovenian(self) -> None:
        for seconds in (1.0, 4.5, 11.9):
            with self.subTest(seconds=seconds):
                self.assertIsNone(
                    bilingual_retry_language(
                        "auto",
                        "en",
                        0.30,
                        -0.70,
                        [("en", 0.30), ("sl", 0.28)],
                        "sl",
                        seconds,
                    )
                )

    def test_clear_or_long_supported_language_stays_single_pass(self) -> None:
        probabilities = [("sl", 0.82), ("en", 0.12)]
        self.assertIsNone(
            bilingual_retry_language(
                "auto", "sl", 0.82, -0.4, probabilities, "en", 2.9
            )
        )
        self.assertIsNone(
            bilingual_retry_language(
                "auto", "sl", 0.48, -0.4,
                [("sl", 0.48), ("en", 0.34)], "en", 13.0,
            )
        )
        self.assertFalse(should_retry_as_slovenian("", "en", 0.01, -1.50))

    def test_recent_english_cross_checks_ambiguous_short_slovenian(self) -> None:
        self.assertEqual(
            bilingual_retry_language(
                "auto", "sl", 0.36, -0.66,
                [("sl", 0.36), ("en", 0.10)], "en", 4.2,
            ),
            "en",
        )

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

    def test_ambiguous_third_language_keeps_recent_supported_language(self) -> None:
        probabilities = [("en", 0.10), ("sl", 0.11), ("nn", 0.23)]
        self.assertEqual(
            bilingual_retry_language(
                "auto", "nn", 0.23, -0.397, probabilities, "en"
            ),
            "en",
        )

    def test_decisive_language_evidence_can_switch_from_recent_language(self) -> None:
        probabilities = [("en", 0.08), ("sl", 0.20), ("hr", 0.24)]
        self.assertEqual(
            bilingual_retry_language(
                "auto", "hr", 0.24, -0.70, probabilities, "en"
            ),
            "sl",
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

    def test_normal_spoken_repetition_is_plausible(self) -> None:
        segments = [Segment("This is very, very, very important.", -0.2, [1, 2, 3])]
        self.assertTrue(transcript_is_plausible(segments, 2.0))

    def test_impossible_repetition_expansion_is_rejected(self) -> None:
        segments = [Segment("Treba, " * 75, -0.116, list(range(75)))]
        self.assertFalse(transcript_is_plausible(segments, 5.5))

    def test_decoder_score_cannot_override_detected_english(self) -> None:
        english_guess = [Segment("Day low hook.", -0.72, [1, 2, 3])]
        slovenian = [Segment("Dej, a lohk?", -0.31, [1, 2, 3])]
        self.assertFalse(prefer_slovenian_retry("en", english_guess, slovenian))

    def test_worse_slovenian_retry_cannot_translate_real_english(self) -> None:
        english = [Segment("Please open the file.", -0.25, [1, 2, 3, 4])]
        forced_slovenian = [Segment("Prosim odpri datoteko.", -0.64, [1, 2, 3, 4])]
        self.assertFalse(prefer_slovenian_retry("en", english, forced_slovenian))

    def test_decoder_score_cannot_override_detected_slovenian(self) -> None:
        translated = [Segment("Prosim, popravite to.", -0.66, [1, 2, 3, 4])]
        english = [Segment("Please fix this.", -0.40, [1, 2, 3, 4])]
        self.assertFalse(prefer_bilingual_retry("en", "sl", translated, english))

    def test_short_english_mislabeled_slovenian_uses_combined_evidence(self) -> None:
        wrong_slovenian = [Segment("Ampak verzija ni?", -0.55, [1, 2, 3, 4])]
        english = [Segment("But the version is not?", -0.30, [1, 2, 3, 4, 5])]
        self.assertTrue(
            prefer_bilingual_retry(
                "en",
                "sl",
                wrong_slovenian,
                english,
                [("sl", 0.48), ("en", 0.34)],
                "en",
            )
        )

    def test_real_short_slovenian_resists_english_translation(self) -> None:
        slovenian = [Segment("Prosim, popravi to.", -0.66, [1, 2, 3, 4])]
        english = [Segment("Please fix this.", -0.40, [1, 2, 3, 4])]
        self.assertFalse(
            prefer_bilingual_retry(
                "en",
                "sl",
                slovenian,
                english,
                [("sl", 0.36), ("en", 0.10)],
                "en",
            )
        )

    def test_marginal_english_retry_cannot_replace_real_slovenian(self) -> None:
        slovenian = [Segment("Prosim, popravi to.", -0.40, [1, 2, 3, 4])]
        english = [Segment("Please fix this.", -0.38, [1, 2, 3, 4])]
        self.assertFalse(prefer_bilingual_retry("en", "sl", slovenian, english))

    def test_modest_slovenian_gain_cannot_translate_ambiguous_english(self) -> None:
        english = [Segment("Please use this.", -0.513, [1, 2, 3, 4])]
        forced_slovenian = [Segment("Prosim, uporabi to.", -0.381, [1, 2, 3, 4])]
        self.assertFalse(
            prefer_bilingual_retry("sl", "en", english, forced_slovenian)
        )

    def test_live_long_prompt_chunk_stays_english(self) -> None:
        probabilities = [("en", 0.37), ("sl", 0.12)]
        self.assertIsNone(
            bilingual_retry_language("auto", "en", 0.37, -0.409, probabilities)
        )
        english = [Segment("Okay, this all sounds good.", -0.409, [1, 2, 3, 4])]
        forced_slovenian = [Segment("Ok, to vse zdi dobro.", -0.187, [1, 2, 3, 4])]
        self.assertFalse(
            prefer_bilingual_retry("sl", "en", english, forced_slovenian)
        )

    def test_supported_english_retry_replaces_unrelated_language(self) -> None:
        unrelated = [Segment("Guten Tag", -0.50, [1, 2])]
        english = [Segment("Good day", -0.53, [1, 2])]
        self.assertTrue(prefer_bilingual_retry("en", "de", unrelated, english))

    def test_much_worse_forced_retry_cannot_replace_an_unrelated_guess(self) -> None:
        finnish_guess = [Segment("Avaa tiedosto.", -0.20, [1, 2, 3])]
        accented_english = [Segment("Open the file.", -0.62, [1, 2, 3])]
        self.assertFalse(prefer_bilingual_retry("en", "fi", finnish_guess, accented_english))

    def test_worse_slovenian_retry_cannot_translate_an_unrelated_guess(self) -> None:
        nynorsk_guess = [Segment("Look at this.", -0.397, [1, 2, 3])]
        forced_slovenian = [Segment("Zagledaj to.", -0.864, [1, 2, 3])]
        self.assertFalse(
            prefer_bilingual_retry("sl", "nn", nynorsk_guess, forced_slovenian)
        )

    def test_empty_retry_is_never_selected(self) -> None:
        original = [Segment("nekaj", -0.7, [1])]
        self.assertFalse(prefer_slovenian_retry("nn", original, []))


if __name__ == "__main__":
    unittest.main(verbosity=2)
