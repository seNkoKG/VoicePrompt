"""Tests for local recovery and explicit text corrections."""

from __future__ import annotations

import json
import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from scripts.text_corrections import apply_corrections
from scripts.transcript_history import remember_transcript


class LocalTextTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.environment = patch.dict(os.environ, {"VOICEPROMPT_DATA_DIR": str(self.root)})
        self.environment.start()

    def tearDown(self) -> None:
        self.environment.stop()
        self.temp.cleanup()

    def write_corrections(self, items: list[dict[str, str]]) -> None:
        (self.root / "corrections.json").write_text(
            json.dumps({"version": 1, "items": items}), encoding="utf-8"
        )

    def test_corrections_are_case_insensitive_and_word_bounded(self) -> None:
        self.write_corrections([{"heard": "polly market", "replacement": "Polymarket"}])
        self.assertEqual(
            apply_corrections("POLLY MARKET and notpolly market"),
            "Polymarket and notpolly market",
        )

    def test_longest_correction_runs_first(self) -> None:
        self.write_corrections([
            {"heard": "codecs", "replacement": "Codex"},
            {"heard": "codecs app", "replacement": "Codex desktop"},
        ])
        self.assertEqual(apply_corrections("open codecs app"), "open Codex desktop")

    def test_corrections_do_not_cascade(self) -> None:
        self.write_corrections([
            {"heard": "codecs", "replacement": "Codex"},
            {"heard": "Codex", "replacement": "incorrect second replacement"},
        ])
        self.assertEqual(apply_corrections("open codecs"), "open Codex")

    def test_missing_or_broken_corrections_leave_text_unchanged(self) -> None:
        self.assertEqual(apply_corrections("keep me"), "keep me")
        (self.root / "corrections.json").write_text("{broken", encoding="utf-8")
        self.assertEqual(apply_corrections("keep me"), "keep me")

    def test_history_keeps_newest_entries_and_unicode(self) -> None:
        (self.root / "history-settings.json").write_text(
            json.dumps({"enabled": True, "limit": 5}), encoding="utf-8"
        )
        for index in range(7):
            remember_transcript(f"raw {index}", f"čisto besedilo {index} ")
        payload = json.loads((self.root / "history.json").read_text(encoding="utf-8"))
        self.assertEqual(len(payload["items"]), 5)
        self.assertEqual(payload["items"][0]["text"], "čisto besedilo 6")
        self.assertEqual(payload["items"][-1]["text"], "čisto besedilo 2")
        self.assertEqual(payload["items"][0]["originalText"], "raw 6")

    def test_disabled_history_writes_nothing(self) -> None:
        (self.root / "history-settings.json").write_text(
            json.dumps({"enabled": False, "limit": 20}), encoding="utf-8"
        )
        remember_transcript("raw", "output")
        self.assertFalse((self.root / "history.json").exists())

    def test_broken_history_is_replaced_without_blocking(self) -> None:
        (self.root / "history.json").write_text("{broken", encoding="utf-8")
        remember_transcript("same", "same")
        payload = json.loads((self.root / "history.json").read_text(encoding="utf-8"))
        self.assertEqual(payload["items"][0]["text"], "same")
        self.assertEqual(payload["items"][0]["originalText"], "")


if __name__ == "__main__":
    unittest.main()
