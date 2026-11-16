"""Check release-note extraction and release-blocking validation."""

from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from release_notes import release_notes


class ReleaseNotesTests(unittest.TestCase):
    section = "## [0.3.3.0] - 2026-11-17\n\n### Added\n\n- Gates.\n"

    def test_exact_section_only(self):
        text = "## [Unreleased]\n\n- Pending.\n\n" + self.section + "\n## [0.3.2.0] - 2026-11-16\n\n- Old.\n"
        self.assertEqual(release_notes(text, "v0.3.3.0"), "### Added\n\n- Gates.\n")

    def test_windows_line_endings(self):
        self.assertIn("- Gates.", release_notes(self.section.replace("\n", "\r\n"), "0.3.3.0"))

    def test_missing_duplicate_empty_and_bad_date(self):
        for text in ("## [Unreleased]\n- Pending.\n", self.section * 2,
                     "## [0.3.3.0] - 2026-11-17\n\n### Added\n",
                     self.section.replace("2026-11-17", "2026-02-30"),
                     self.section.replace(" - 2026-11-17", "")):
            with self.subTest(text=text), self.assertRaises(ValueError):
                release_notes(text, "0.3.3.0")

    def test_invalid_version(self):
        with self.assertRaises(ValueError):
            release_notes(self.section, "Unreleased")


if __name__ == "__main__":
    unittest.main()
