import json
import tempfile
import unittest
from pathlib import Path

from scripts import app_profiles


class AppProfileTests(unittest.TestCase):
    def test_release_and_installer_ship_profile_runtime(self):
        root = Path(__file__).parents[1]
        installer = (root / "install.ps1").read_text(encoding="utf-8")
        packager = (root / "scripts" / "package_release.ps1").read_text(encoding="utf-8")
        patcher = (root / "scripts" / "apply_patches.ps1").read_text(encoding="utf-8")
        self.assertIn("$packageAppProfiles", installer)
        self.assertIn('"scripts\\app_profiles.py"', packager)
        self.assertIn('Destination "$site\\app_profiles.py"', patcher)

    def test_exact_case_insensitive_match_exposes_only_requested_overrides(self):
        profiles = {
            "code.exe": app_profiles.AppProfile("Code.exe", "prompt", "inherit"),
        }
        profile = app_profiles.resolve_app_profile("CODE.EXE", profiles)
        self.assertEqual(profile.writing_override, "prompt")
        self.assertIsNone(profile.output_override)
        self.assertIsNone(app_profiles.resolve_app_profile("code-helper.exe", profiles))

    def test_no_profiles_skip_foreground_process_lookup(self):
        original = app_profiles.foreground_executable
        app_profiles.foreground_executable = lambda: self.fail("foreground lookup should not run")
        try:
            self.assertIsNone(app_profiles.resolve_app_profile(profiles={}))
        finally:
            app_profiles.foreground_executable = original

    def test_valid_unicode_profiles_load_once(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "app-profiles.json"
            path.write_text(json.dumps({
                "version": 1,
                "items": [
                    {"executable": "Code.exe", "writingMode": "prompt", "outputMode": "paste"},
                    {"executable": "Beležke.exe", "writingMode": "off", "outputMode": "clipboard"},
                ],
            }, ensure_ascii=False), encoding="utf-8")
            profiles = app_profiles.load_app_profiles(path)
            self.assertEqual(len(profiles), 2)
            self.assertEqual(profiles["beležke.exe"].output_override, "clipboard")

    def test_malformed_duplicate_or_path_rules_fail_closed(self):
        bad_items = [
            [{"executable": "Code.exe", "writingMode": "magic", "outputMode": "paste"}],
            [
                {"executable": "Code.exe", "writingMode": "off", "outputMode": "paste"},
                {"executable": "code.EXE", "writingMode": "prompt", "outputMode": "paste"},
            ],
            [{"executable": "C:\\Windows\\app.exe", "writingMode": "off", "outputMode": "paste"}],
        ]
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "app-profiles.json"
            for items in bad_items:
                path.write_text(json.dumps({"version": 1, "items": items}), encoding="utf-8")
                self.assertEqual(app_profiles.load_app_profiles(path), {})


if __name__ == "__main__":
    unittest.main()
