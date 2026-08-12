from __future__ import annotations

import os
import unittest
from unittest.mock import patch

from scripts.voice_commands import commands_enabled, execute_voice_command, resolve_voice_command


class VoiceCommandTests(unittest.TestCase):
    def test_commands_are_off_by_default(self) -> None:
        with patch.dict(os.environ, {}, clear=True):
            self.assertFalse(commands_enabled())
            self.assertIsNone(resolve_voice_command("new line"))

    def test_exact_english_commands_ignore_terminal_punctuation(self) -> None:
        command = resolve_voice_command('“New line.”', True)
        self.assertIsNotNone(command)
        self.assertEqual((command.name, command.text), ("new-line", "\n"))

    def test_exact_slovenian_commands_are_supported(self) -> None:
        paragraph = resolve_voice_command("Nov odstavek!", "true")
        undo = resolve_voice_command("Razveljavi.", "yes")
        cancel = resolve_voice_command("Prekliči.", "1")
        self.assertIsNotNone(paragraph)
        self.assertIsNotNone(undo)
        self.assertIsNotNone(cancel)
        self.assertEqual((paragraph.name, paragraph.text), ("new-paragraph", "\n\n"))
        self.assertEqual((undo.name, undo.text), ("undo", None))
        self.assertEqual((cancel.name, cancel.text), ("cancel", None))

    def test_commands_never_trigger_inside_normal_dictation(self) -> None:
        self.assertIsNone(resolve_voice_command("Add a new line after this sentence", True))
        self.assertIsNone(resolve_voice_command("Ne naredi nove vrstice", True))
        self.assertIsNone(resolve_voice_command("undo the previous migration", True))
        self.assertIsNone(resolve_voice_command("cancel the download", True))

    def test_bullet_command_preserves_unicode_output(self) -> None:
        command = resolve_voice_command("alineja", True)
        self.assertIsNotNone(command)
        self.assertEqual((command.name, command.text), ("bullet-point", "• "))

    def test_command_effects_are_routed_exactly_once(self) -> None:
        delivered: list[str] = []
        undos: list[bool] = []

        def deliver(value: str) -> str:
            delivered.append(value)
            return "paste"

        def undo() -> None:
            undos.append(True)

        paragraph = resolve_voice_command("new paragraph", True)
        undo_command = resolve_voice_command("undo", True)
        cancel = resolve_voice_command("cancel", True)
        self.assertIsNotNone(paragraph)
        self.assertIsNotNone(undo_command)
        self.assertIsNotNone(cancel)
        self.assertEqual(execute_voice_command(paragraph, deliver, undo), "paste")
        self.assertEqual(execute_voice_command(undo_command, deliver, undo), "command")
        self.assertEqual(execute_voice_command(cancel, deliver, undo), "cancelled")
        self.assertEqual(delivered, ["\n\n"])
        self.assertEqual(undos, [True])


if __name__ == "__main__":
    unittest.main()
