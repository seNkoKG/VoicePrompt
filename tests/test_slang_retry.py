"""Tests for mixed English and Slovenian slang language routing."""

import unittest

from scripts.slang_retry import recognition_language, should_retry_as_slovenian


class SlangRetryTests(unittest.TestCase):
    def test_hybrid_mode_keeps_automatic_detection(self) -> None:
        self.assertIsNone(recognition_language("sl-slang"))
        self.assertIsNone(recognition_language("auto"))
        self.assertEqual(recognition_language("en"), "en")
        self.assertEqual(recognition_language("sl"), "sl")

    def test_english_and_slovenian_are_never_reinterpreted(self) -> None:
        self.assertFalse(should_retry_as_slovenian("sl-slang", "en"))
        self.assertFalse(should_retry_as_slovenian("sl-slang", "sl"))

    def test_third_language_mistakes_retry_as_slovenian(self) -> None:
        for detected in ("lt", "nn", "hr", "de"):
            with self.subTest(detected=detected):
                self.assertTrue(should_retry_as_slovenian("sl-slang", detected))
        self.assertFalse(should_retry_as_slovenian("auto", "lt"))


if __name__ == "__main__":
    unittest.main(verbosity=2)
