#!/usr/bin/env python3
"""Offline migration checks using temporary directories; never calls Yak or Rhino."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('installer', Path(__file__).with_name('install-local.py'))
installer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(installer)

class MigrationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.packages = self.root/'packages/8.0'
        self.legacy = self.packages/'aholo-rhino'
        self.legacy.mkdir(parents=True)
        (self.legacy/'original').write_bytes(b'old package')
    def tearDown(self):
        self.temp.cleanup()
    def test_running_host_prevents_package_changes(self):
        with self.assertRaises(RuntimeError):
            installer.replace_legacy(self.packages, lambda: self.fail('must not install'), running=lambda: True)
        self.assertEqual(b'old package', (self.legacy/'original').read_bytes())
    def test_failed_install_restores_old_package(self):
        def fail():
            self.assertFalse(self.legacy.exists())
            raise RuntimeError('synthetic installation failure')
        with self.assertRaises(RuntimeError):
            installer.replace_legacy(self.packages, fail, running=lambda: False)
        self.assertEqual(b'old package', (self.legacy/'original').read_bytes())
    def test_success_archives_old_package_outside_scanned_directory(self):
        backup = installer.replace_legacy(self.packages, lambda: None, running=lambda: False)
        self.assertFalse(self.legacy.exists())
        self.assertFalse(backup.is_relative_to(self.packages))
        self.assertEqual(b'old package', (backup/'original').read_bytes())
    def test_data_alias_or_platform_fallback_keeps_one_original(self):
        legacy = self.root/'Aholo-Rhino'
        legacy.mkdir()
        (legacy/'task').write_text('unchanged')
        current = installer.data_alias(self.root)
        self.assertTrue(current.samefile(legacy))
        self.assertEqual('unchanged', (current/'task').read_text())
    def test_conflicting_directories_remain_untouched(self):
        for name in ('Aholo-Rhino', 'Aholo3D-Rhino'):
            (self.root/name).mkdir()
        with self.assertRaises(ValueError):
            installer.data_alias(self.root)
        self.assertTrue((self.root/'Aholo-Rhino').is_dir())
        self.assertTrue((self.root/'Aholo3D-Rhino').is_dir())

if __name__ == '__main__':
    unittest.main()
