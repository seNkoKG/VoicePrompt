import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
LEGAL_FILES = (
    "LICENSE.txt",
    "THIRD_PARTY_NOTICES.txt",
    "PRIVACY.md",
    "TERMS.md",
)


class LegalDocumentTests(unittest.TestCase):
    def test_every_pinned_runtime_package_is_disclosed(self) -> None:
        requirements = (ROOT / "requirements.txt").read_text(encoding="utf-8")
        notices = (ROOT / "THIRD_PARTY_NOTICES.txt").read_text(encoding="utf-8")
        pins = re.findall(r"^([A-Za-z0-9_.-]+==[^\s]+)$", requirements, re.MULTILINE)
        self.assertGreater(len(pins), 0)
        for pin in pins:
            with self.subTest(pin=pin):
                self.assertIn(pin + " | ", notices)

    def test_release_install_and_updater_require_notices(self) -> None:
        sources = "\n".join(
            (ROOT / path).read_text(encoding="utf-8")
            for path in (
                "scripts/package_release.ps1",
                "install.ps1",
                "ui/VoicePromptTray/UpdateInstaller.cs",
                ".github/workflows/release.yml",
            )
        )
        for legal_file in LEGAL_FILES:
            with self.subTest(legal_file=legal_file):
                self.assertGreaterEqual(sources.count(legal_file), 4)

    def test_current_notices_have_no_setup_placeholders(self) -> None:
        for legal_file in ("PRIVACY.md", "TERMS.md"):
            with self.subTest(legal_file=legal_file):
                notice = (ROOT / legal_file).read_text(encoding="utf-8")
                self.assertNotIn("[[", notice)

    def test_website_does_not_claim_voiceprompt_is_mit_licensed(self) -> None:
        website = (ROOT / "docs/index.html").read_text(encoding="utf-8")
        self.assertNotIn('"license": "https://opensource.org/license/mit"', website)
        self.assertNotIn("MIT licensed ·", website)


if __name__ == "__main__":
    unittest.main()
