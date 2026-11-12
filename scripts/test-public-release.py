#!/usr/bin/env python3
"""Offline checks for publication hygiene and isolated source preparation."""
import hashlib
import importlib.util
import os
from pathlib import Path
import shutil
import stat
import tarfile
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("public_export", ROOT / "scripts/prepare-public-source.py")
export = importlib.util.module_from_spec(spec)
spec.loader.exec_module(export)
check = export.public_check


class PublicationTests(unittest.TestCase):
    def setUp(self):
        self.temp = Path(tempfile.mkdtemp(prefix="publication-test-"))
        self.root = self.temp / "private"
        self.root.mkdir()
        export.git(self.root, "init", "--quiet", "-b", "main")
        for key, value in (("user.name", "legacy-author"), ("user.email", "legacy@example.invalid"),
                           ("commit.gpgsign", "false"), ("core.autocrlf", "false")):
            export.git(self.root, "config", key, value)
        self.write("docs/implementation-progress.md", "Private historical process record.\n")
        self.commit("Private development history")
        export.git(self.root, "tag", "private-start")
        (self.root / "docs/implementation-progress.md").unlink()
        for name in check.REQUIRED:
            self.write(name, "Public documentation.\n")
        self.write("docs/index.md", "[Readme](../readme.md)\n")
        self.write("source.bin", bytes(range(256)))
        self.write(".gitignore", "artifacts/\n")
        self.commit("Reviewed current tree")

    def tearDown(self):
        def remove_readonly(func, path, error):
            os.chmod(path, stat.S_IWRITE | stat.S_IREAD)
            func(path)
        shutil.rmtree(self.temp, onerror=remove_readonly)

    def write(self, name, text):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(text if isinstance(text, bytes) else text.encode())

    def commit(self, message):
        export.git(self.root, "add", "--all")
        export.git(self.root, "commit", "--quiet", "-m", message)

    def test_current_tree_is_clean_but_private_history_is_not_public(self):
        self.assertEqual([], check.check(self.root))
        errors = check.check(self.root, history=True)
        self.assertTrue(any("identities" in e for e in errors))
        self.assertTrue(any("remain reachable" in e for e in errors))

    def test_links_are_checked_without_fetching_external_urls(self):
        valid = b"[index](docs/index.md) [web](https://example.invalid/path) [section](#intro)"
        self.assertEqual([], check.file_errors(self.root, "readme.md", valid))
        self.assertTrue(any("broken local link" in e for e in check.file_errors(self.root, "readme.md", b"[bad](missing.md)")))
        self.assertTrue(any("leaves source tree" in e for e in check.file_errors(self.root, "readme.md", b"[bad](../outside.md)")))

    def test_sensitive_findings_never_print_the_matched_value(self):
        synthetic = "gh" + "p_" + "A" * 30
        errors = check.file_errors(self.root, "sample.txt", synthetic.encode())
        self.assertTrue(any("credential" in e for e in errors))
        self.assertNotIn(synthetic, str(errors))
        self.assertTrue(check.file_errors(self.root, "note.md", b"remote/" + b"glm-example"))
        self.assertTrue(check.file_errors(self.root, "note.md", b"/Users/" + b"sample/file"))

    def test_unsafe_archive_members_are_rejected(self):
        for name in ("../escape", "/absolute", "C:/absolute", "C:relative", "a\\b", ".git/config", "a/../../b"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                export.safe_member(tarfile.TarInfo(name))
        for kind in (tarfile.SYMTYPE, tarfile.LNKTYPE, tarfile.FIFOTYPE):
            item = tarfile.TarInfo("safe-name")
            item.type = kind
            with self.subTest(kind=kind), self.assertRaises(ValueError):
                export.safe_member(item)

    def test_export_preserves_original_and_creates_one_alias_only_commit(self):
        before_refs = export.git(self.root, "show-ref")
        before_log = export.git(self.root, "log", "--all", "--format=%H")
        with patch.dict(os.environ, {"GIT_AUTHOR_NAME": "unexpected-author", "GIT_COMMITTER_EMAIL": "unexpected@example.invalid", "GIT_DIR": str(self.temp / "wrong-git-dir")}):
            receipt = export.prepare(self.root, self.temp / "output")
        candidate = Path(receipt["sourceDirectory"])
        self.assertEqual(before_refs, export.git(self.root, "show-ref"))
        self.assertEqual(before_log, export.git(self.root, "log", "--all", "--format=%H"))
        self.assertEqual(b"", export.git(self.root, "status", "--porcelain"))
        self.assertEqual(b"1", export.git(candidate, "rev-list", "--all", "--count").strip())
        self.assertEqual(b"", export.git(candidate, "remote"))
        self.assertEqual([], check.check(candidate, history=True))
        self.assertFalse(receipt["pushed"])
        self.assertFalse(receipt["visibilityChanged"])
        names = export.git(self.root, "ls-tree", "-rz", "--name-only", "HEAD").decode().strip("\0").split("\0")
        with zipfile.ZipFile(receipt["archive"]) as archive:
            self.assertEqual({"aholo3d-rhino/" + n for n in names}, set(archive.namelist()))
            self.assertFalse(any(".git" in Path(n).parts for n in archive.namelist()))
            for name in names:
                original = export.git(self.root, "show", "HEAD:" + name)
                self.assertEqual(original, export.git(candidate, "show", "HEAD:" + name))
                self.assertEqual(original, archive.read("aholo3d-rhino/" + name))
        self.assertEqual(receipt["sha256"], hashlib.sha256(Path(receipt["archive"]).read_bytes()).hexdigest())

    def test_dirty_or_untracked_source_refuses_export(self):
        for name in ("readme.md", "unreviewed.txt"):
            with self.subTest(name=name):
                self.write(name, "Unreviewed change.\n")
                with self.assertRaisesRegex(ValueError, "Commit the reviewed"):
                    export.prepare(self.root, self.temp / "output")
                if name == "readme.md":
                    export.git(self.root, "restore", name)
                else:
                    (self.root / name).unlink()

    def test_output_cannot_write_git_metadata_or_unignored_source(self):
        for output in (self.root / ".git" / "refs", self.root / "public-copy", self.root):
            with self.subTest(output=output.name), self.assertRaises(ValueError):
                export.prepare(self.root, output)

    def test_export_pins_one_commit_if_head_changes(self):
        original = export.git(self.root, "rev-parse", "HEAD").decode().strip()
        actual_git = export.git
        changed = False
        def concurrent_git(root, *args):
            nonlocal changed
            if root.resolve() == self.root.resolve() and args[0] == "archive" and not changed:
                changed = True
                actual_git(self.root, "update-ref", "refs/heads/main", "private-start")
            return actual_git(root, *args)
        with patch.object(export, "git", side_effect=concurrent_git):
            receipt = export.prepare(self.root, self.temp / "output")
        self.assertTrue(changed)
        self.assertEqual(original, receipt["developmentSourceCommit"])
        self.assertEqual((self.root / "source.bin").read_bytes(),
                         (Path(receipt["sourceDirectory"]) / "source.bin").read_bytes())
        self.assertEqual([], check.check(Path(receipt["sourceDirectory"]), history=True))

    def test_private_file_refuses_export(self):
        self.write("docs/implementation-progress.md", "Do not publish.\n")
        self.commit("Accidental private file")
        with self.assertRaisesRegex(ValueError, "Public source checks failed"):
            export.prepare(self.root, self.temp / "output")

    def test_export_attributes_cannot_silently_drop_files(self):
        self.write(".gitattributes", "source.bin export-ignore\n")
        self.commit("Unexpected export attribute")
        with self.assertRaisesRegex(ValueError, "omitted tracked files"):
            export.prepare(self.root, self.temp / "output")


if __name__ == "__main__":
    unittest.main()
