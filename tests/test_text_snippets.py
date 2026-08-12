from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from scripts.text_snippets import load_snippets, resolve_snippet


class TextSnippetTests(unittest.TestCase):
    def test_unicode_snippets_load_and_resolve_in_both_languages(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "snippets.json"
            path.write_text(json.dumps({
                "version": 1,
                "items": [{"name": "Podpis", "content": "Lep pozdrav,\nŽan"}],
            }), encoding="utf-8")
            snippets = load_snippets(path)
            self.assertEqual(resolve_snippet("Insert snippet podpis.", snippets).content, "Lep pozdrav,\nŽan")
            self.assertEqual(resolve_snippet("Vstavi predlogo PODPIS!", snippets).content, "Lep pozdrav,\nŽan")

    def test_normal_dictation_and_unknown_names_do_not_trigger(self) -> None:
        snippets = {"reply": type("Snippet", (), {"content": "Thanks"})()}
        self.assertIsNone(resolve_snippet("Please insert snippet reply here", snippets))
        self.assertIsNone(resolve_snippet("Insert snippet missing", snippets))

    def test_malformed_and_oversized_entries_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "snippets.json"
            path.write_text("not json", encoding="utf-8")
            self.assertEqual(load_snippets(path), {})
            path.write_text(json.dumps({"items": [
                {"name": "", "content": "empty name"},
                {"name": None, "content": "null name"},
                {"name": "large", "content": "x" * 4_001},
                {"name": "valid", "content": "ok"},
                {"name": "VALID", "content": "duplicate"},
            ]}), encoding="utf-8")
            snippets = load_snippets(path)
            self.assertEqual(list(snippets), ["valid"])
            self.assertEqual(snippets["valid"].content, "ok")


if __name__ == "__main__":
    unittest.main()
