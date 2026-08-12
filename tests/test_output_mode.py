from __future__ import annotations

import os
import unittest
from unittest.mock import patch

from scripts.output_mode import CLIPBOARD, PASTE, deliver_text, normalize_output_mode


class OutputModeTests(unittest.TestCase):
    def test_missing_or_invalid_mode_keeps_default_paste(self) -> None:
        self.assertEqual(normalize_output_mode(None), PASTE)
        self.assertEqual(normalize_output_mode("unknown"), PASTE)

    def test_clipboard_mode_is_case_insensitive(self) -> None:
        self.assertEqual(normalize_output_mode(" CLIPBOARD "), CLIPBOARD)

    def test_copy_only_never_calls_paste(self) -> None:
        copied: list[str] = []
        pasted: list[str] = []
        result = deliver_text("Živjo world ", copied.append, pasted.append, CLIPBOARD)
        self.assertEqual(result, CLIPBOARD)
        self.assertEqual(copied, ["Živjo world "])
        self.assertEqual(pasted, [])

    def test_default_paste_never_calls_copy_only_route(self) -> None:
        copied: list[str] = []
        pasted: list[str] = []
        with patch.dict(os.environ, {}, clear=True):
            result = deliver_text("hello ", copied.append, pasted.append)
        self.assertEqual(result, PASTE)
        self.assertEqual(copied, [])
        self.assertEqual(pasted, ["hello "])

    def test_delivery_failure_is_not_hidden(self) -> None:
        def fail(_: str) -> None:
            raise RuntimeError("clipboard busy")

        with self.assertRaisesRegex(RuntimeError, "clipboard busy"):
            deliver_text("text", fail, lambda _: None, CLIPBOARD)


if __name__ == "__main__":
    unittest.main()
