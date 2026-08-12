from __future__ import annotations

import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).parents[1]
sys.path.insert(0, str(ROOT / "scripts"))

from decoding_options import decoding_options


class DecodingOptionsTests(unittest.TestCase):
    def test_default_is_latency_bounded_and_repetition_safe(self) -> None:
        options = decoding_options(0.0)

        self.assertEqual(options["temperature"], 0.0)
        self.assertIs(options["condition_on_previous_text"], False)
        self.assertEqual(options["repetition_penalty"], 1.1)
        self.assertEqual(options["no_repeat_ngram_size"], 3)

    def test_nonzero_temperature_remains_user_controlled(self) -> None:
        self.assertEqual(decoding_options(0.3)["temperature"], 0.3)

    def test_runtime_patch_applies_options_to_both_language_passes(self) -> None:
        patch = (ROOT / "scripts" / "apply_patches.ps1").read_text(encoding="utf-8")
        installer = (ROOT / "install.ps1").read_text(encoding="utf-8")
        runner = (ROOT / "run_daemon.pyw").read_text(encoding="utf-8")

        self.assertIn("from ..decoding_options import decoding_options", patch)
        self.assertIn("bilingual_retry_language", patch)
        self.assertIn("prefer_bilingual_retry", patch)
        self.assertIn("recognition_prompt(server_config.language", patch)
        self.assertIn("recognition_hotwords(server_config.language", patch)
        self.assertIn("self._base_prompt = server_config.prompt", patch)
        self.assertIn("bilingual_retry_prompt(retry_language, self._base_prompt)", patch)
        self.assertNotIn("bilingual_retry_prompt(retry_language, self._prompt)", patch)
        self.assertIn("info.language_probability", patch)
        self.assertIn('Remove-Patch "$site\\engine\\local.py"', patch)
        self.assertIn('"engine/local.py -- obsolete selective retry"', patch)
        self.assertIn("$packageDecodingOptions", installer)
        self.assertIn('"decoding_options.py") -Force', installer)
        self.assertIn('Invoke-Checked $daemonExe @("stop")', installer)
        self.assertIn("$normalizedContent", patch)
        self.assertIn("$normalizedFind", patch)
        self.assertIn('DICTATION_PASTE_DELAY", "0.35"', runner)


if __name__ == "__main__":
    unittest.main(verbosity=2)
