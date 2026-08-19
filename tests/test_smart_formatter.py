from __future__ import annotations

import unittest

from scripts.smart_formatter import format_dictation, smart_formatting_enabled
from scripts.windows_context import DictationContext, classify_application, context_awareness_enabled


class SmartFormatterTests(unittest.TestCase):
    def test_spoken_punctuation_and_paragraphs_work_in_both_languages(self) -> None:
        self.assertEqual(
            format_dictation("hello comma world new paragraph how are you question mark"),
            "Hello, world\n\nHow are you?",
        )
        self.assertEqual(format_dictation("živjo vejica kako si vprašaj"), "Živjo, kako si?")

    def test_only_boundary_fillers_and_immediate_filler_repeats_are_cleaned(self) -> None:
        self.assertEqual(format_dictation("um, send umami and uh uh wait"), "Send umami and uh wait")

    def test_context_adds_only_the_missing_joining_space(self) -> None:
        context = DictationContext(before_text="Existing sentence", app_kind="general")
        self.assertEqual(format_dictation("continues here", context), " Continues here")
        punctuated = DictationContext(before_text="Existing sentence ", app_kind="general")
        self.assertEqual(format_dictation("continues here", punctuated), "Continues here")
        surrounded = DictationContext(before_text="Before", after_text="after", app_kind="general")
        self.assertEqual(format_dictation(" middle ", surrounded), " Middle ")

    def test_ordinary_use_of_period_is_not_destroyed(self) -> None:
        self.assertEqual(format_dictation("during this period we improved"), "During this period we improved")

    def test_document_style_finishes_sentence_but_chat_and_code_do_not(self) -> None:
        self.assertEqual(
            format_dictation("this is a complete thought", DictationContext(app_kind="document")),
            "This is a complete thought.",
        )
        self.assertEqual(
            format_dictation("this is a chat message", DictationContext(app_kind="chat")),
            "This is a chat message",
        )
        self.assertEqual(
            format_dictation("const value equals one", DictationContext(app_kind="code")),
            "const value equals one",
        )

    def test_feature_flags_default_on_and_can_fail_closed(self) -> None:
        self.assertTrue(smart_formatting_enabled(None))
        self.assertTrue(context_awareness_enabled(None))
        self.assertEqual(format_dictation("hello comma world", enabled=False), "hello comma world")

    def test_application_classification_is_exact_and_local(self) -> None:
        self.assertEqual(classify_application("Code.exe"), "code")
        self.assertEqual(classify_application("Slack.exe"), "chat")
        self.assertEqual(classify_application("firefox.exe", "Inbox - Gmail"), "email")
        self.assertEqual(classify_application("browser.exe"), "general")


if __name__ == "__main__":
    unittest.main()
