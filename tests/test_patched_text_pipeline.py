"""Product-path tests for generated command routing and smart formatting."""

from __future__ import annotations

import importlib
import os
from pathlib import Path
import sys
import tempfile
import types
import unittest
from unittest.mock import patch


@unittest.skipUnless(os.environ.get("VOICEPROMPT_PATCHED_SITE"), "patched runtime not supplied")
class PatchedTextPipelineTests(unittest.TestCase):
    def setUp(self) -> None:
        module = Path(os.environ["VOICEPROMPT_PATCHED_SITE"])
        sys.path.insert(0, str(module.parent))
        self.addCleanup(sys.path.remove, str(module.parent))
        for name in list(sys.modules):
            if name == "whisper_dictation" or name.startswith("whisper_dictation."):
                del sys.modules[name]

        self.data_dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.data_dir.cleanup)
        self.environment = patch.dict(
            os.environ,
            {
                "VOICEPROMPT_DATA_DIR": self.data_dir.name,
                "VOICEPROMPT_VOICE_COMMANDS": "1",
                "VOICEPROMPT_SMART_FORMATTING": "1",
                "VOICEPROMPT_CONTEXT_AWARENESS": "0",
            },
        )
        self.environment.start()
        self.addCleanup(self.environment.stop)

        self.typer = importlib.import_module("whisper_dictation.typer")
        context_module = importlib.import_module("whisper_dictation.windows_context")
        self.context_type = context_module.DictationContext
        self.typer.capture_context = lambda: context_module.DictationContext()
        self.typer.resolve_app_profile = lambda _executable: None
        self.typer.rewrite_text = lambda text, mode_override=None: text
        self.typer.remember_transcript = lambda *_args: None
        self.delivered: list[tuple[str, str | None]] = []

        def deliver(text: str, *_args: object, mode: str | None = None) -> str:
            self.delivered.append((text, mode))
            return "paste"

        self.typer.deliver_text = deliver

    def test_exact_commands_route_before_smart_formatting(self) -> None:
        self.assertEqual(self.typer.type_text("new line"), "paste")
        self.assertEqual(self.typer.type_text("Nov odstavek!"), "paste")
        self.assertEqual(self.delivered, [("\n", None), ("\n\n", None)])

    def test_layout_mark_still_delivers_when_voice_commands_are_disabled(self) -> None:
        with patch.dict(os.environ, {"VOICEPROMPT_VOICE_COMMANDS": "0"}):
            self.assertEqual(self.typer.type_text("new line."), "paste")
            self.assertEqual(self.typer.type_text("nov odstavek!"), "paste")
        self.assertEqual(self.delivered, [("\n", None), ("\n\n", None)])

    def test_empty_formatter_result_is_a_safe_no_op(self) -> None:
        self.assertEqual(self.typer.type_text("um,"), "paste")
        self.assertEqual(self.delivered, [])

    def test_corrections_can_form_commands_but_normal_text_is_formatted(self) -> None:
        self.typer.apply_corrections = lambda text: "new line" if text == "fresh row" else text
        self.assertEqual(self.typer.type_text("fresh row"), "paste")
        self.assertEqual(self.typer.type_text("hello comma world"), "paste")
        self.assertEqual(self.delivered, [("\n", None), ("Hello, world. ", None)])

    def test_paste_keeps_boundaries_but_copy_and_streams_stay_exact(self) -> None:
        self.typer.type_text("hello there")
        self.typer.type_text("world again")
        self.assertEqual("".join(text for text, _mode in self.delivered), "Hello there. World again. ")

        self.delivered.clear()
        with patch.dict(os.environ, {"VOICEPROMPT_OUTPUT_MODE": "clipboard"}):
            self.typer.type_text("exact copy")
        self.assertEqual(self.delivered, [("Exact copy.", None)])

        self.delivered.clear()
        self.typer.type_text("streamed fragment", paste_boundary=False)
        self.assertEqual(self.delivered, [("Streamed fragment.", None)])

    def test_paste_boundary_does_not_insert_space_before_known_punctuation(self) -> None:
        for following in (",", ")"):
            with self.subTest(following=following):
                self.delivered.clear()
                self.typer.capture_context = lambda value=following: self.context_type(
                    after_text=value,
                    app_kind="general",
                )
                self.typer.type_text("hello")
                self.assertEqual(self.delivered, [("Hello", None)])

    def test_paste_boundary_separates_existing_word_when_formatting_is_off(self) -> None:
        self.typer.capture_context = lambda: self.context_type(
            after_text="world",
            app_kind="general",
        )
        with patch.dict(os.environ, {"VOICEPROMPT_SMART_FORMATTING": "0"}):
            self.typer.type_text("hello")
        self.assertEqual(self.delivered, [("hello ", None)])

    def test_command_delivery_keeps_application_output_override(self) -> None:
        self.typer.resolve_app_profile = lambda _executable: types.SimpleNamespace(
            executable="Code.exe",
            writing_mode="inherit",
            output_mode="clipboard",
            writing_override=None,
            output_override="clipboard",
        )
        self.assertEqual(self.typer.type_text("new line"), "paste")
        self.assertEqual(self.delivered, [("\n", "clipboard")])


if __name__ == "__main__":
    unittest.main(verbosity=2)
