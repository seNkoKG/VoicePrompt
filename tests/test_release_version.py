"""Keep every user-visible release pointer aligned with the tray version."""

from __future__ import annotations

import re
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def project_value(path: str, name: str) -> str:
    root = ET.parse(ROOT / path).getroot()
    value = root.findtext(f".//{name}")
    if not value:
        raise AssertionError(f"{path} has no {name}")
    return value.strip()


class ReleaseVersionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.version = project_value("ui/VoicePromptTray/VoicePromptTray.csproj", "Version")
        if not re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?", cls.version):
            raise AssertionError(f"invalid release version: {cls.version}")

    def test_project_metadata_matches(self) -> None:
        expected_binary = f"{self.version}.0"
        for name in ("AssemblyVersion", "FileVersion"):
            self.assertEqual(
                expected_binary,
                project_value("ui/VoicePromptTray/VoicePromptTray.csproj", name),
            )
            self.assertEqual(
                expected_binary,
                project_value("ui/LayoutCheck/LayoutCheck.csproj", name),
            )
        self.assertEqual(
            self.version,
            project_value("ui/VoicePromptTray/VoicePromptTray.csproj", "InformationalVersion"),
        )
        self.assertEqual(
            self.version,
            project_value("ui/LayoutCheck/LayoutCheck.csproj", "Version"),
        )

    def test_runtime_and_release_workflows_match(self) -> None:
        ai = (ROOT / "scripts/ai_rewriter.py").read_text(encoding="utf-8")
        self.assertIn(f'"User-Agent": "VoicePrompt/{self.version}"', ai)

        pages = (ROOT / ".github/workflows/pages.yml").read_text(encoding="utf-8")
        self.assertIn(f"RELEASE_TAG: v{self.version}", pages)
        self.assertIn(f"VoicePrompt-v{self.version}-windows-x64.zip", pages)
        self.assertIn(f"VoicePrompt-v{self.version}-SHA256SUMS.txt", pages)

    def test_latest_download_pointers_match(self) -> None:
        expected = f"VoicePrompt-v{self.version}-windows-x64.zip"
        for path in ("README.md", "docs/index.html"):
            text = (ROOT / path).read_text(encoding="utf-8")
            versions = set(re.findall(r"VoicePrompt-v(\d+\.\d+\.\d+)-windows-x64\.zip", text))
            self.assertEqual({self.version}, versions, path)
            self.assertIn(expected, text)

    def test_changelog_starts_with_current_release(self) -> None:
        changelog = (ROOT / "CHANGELOG.md").read_text(encoding="utf-8")
        first = re.search(r"^## \[([^]]+)]", changelog, re.MULTILINE)
        self.assertIsNotNone(first)
        self.assertEqual(self.version, first.group(1))


if __name__ == "__main__":
    unittest.main()
