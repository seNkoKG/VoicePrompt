from __future__ import annotations

import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).parents[1]
sys.path.insert(0, str(ROOT / "scripts"))

from decoding_options import decoding_options


class DecodingOptionsTests(unittest.TestCase):
    def test_default_enables_native_failure_loop_protection(self) -> None:
        options = decoding_options(0.0)

        self.assertEqual(options["temperature"], (0.0, 0.2, 0.4, 0.6, 0.8, 1.0))
        self.assertIs(options["condition_on_previous_text"], False)
        self.assertEqual(options["repetition_penalty"], 1.1)
        self.assertEqual(options["no_repeat_ngram_size"], 3)

    def test_nonzero_temperature_remains_user_controlled(self) -> None:
        self.assertEqual(decoding_options(0.3)["temperature"], 0.3)

    def test_runtime_patch_applies_options_to_both_language_passes(self) -> None:
        patch = (ROOT / "scripts" / "apply_patches.ps1").read_text(encoding="utf-8")
        installer = (ROOT / "install.ps1").read_text(encoding="utf-8")

        self.assertIn("from ..decoding_options import decoding_options", patch)
        self.assertEqual(patch.count("**decoding_options(self._temperature)"), 2)
        self.assertIn("$packageDecodingOptions", installer)
        self.assertIn('"decoding_options.py") -Force', installer)
        self.assertIn('Invoke-Checked $daemonExe @("stop")', installer)


if __name__ == "__main__":
    unittest.main(verbosity=2)
