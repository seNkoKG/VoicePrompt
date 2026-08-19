from __future__ import annotations

import unittest

from scripts.selection_commands import resolve_selection_command


class SelectionCommandTests(unittest.TestCase):
    def test_explicit_english_and_slovenian_prefixes_are_required(self) -> None:
        self.assertEqual(resolve_selection_command("Command make this shorter.", True), "make this shorter")
        self.assertEqual(resolve_selection_command("Ukaz spremeni v alineje", True), "spremeni v alineje")
        self.assertIsNone(resolve_selection_command("make this shorter", True))

    def test_commands_respect_global_opt_in_and_length_limit(self) -> None:
        self.assertIsNone(resolve_selection_command("command summarize", False))
        self.assertIsNone(resolve_selection_command("command " + "x" * 241, True))


if __name__ == "__main__":
    unittest.main()
