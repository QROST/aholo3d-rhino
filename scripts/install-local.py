#!/usr/bin/env python3
"""Install a verified Aholo3D Yak archive. Preserve legacy packages/data; never launch Rhino."""
import argparse
import csv
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import uuid
import zipfile
from package import manifest_values

ROOT = Path(__file__).resolve().parent.parent

def rhino_running():
    if sys.platform == 'darwin':
        output = subprocess.check_output(['ps', '-axo', 'comm='], text=True)
        return any(line.strip().endswith('/Contents/MacOS/Rhinoceros') for line in output.splitlines())
    if os.name == 'nt':
        output = subprocess.check_output(['tasklist', '/fo', 'csv', '/nh'], text=True)
        return any(row and row[0].lower() in ('rhino.exe', 'rhinoceros.exe') for row in csv.reader(output.splitlines()))
    raise RuntimeError('Local installation supports macOS and Windows only.')

def data_alias(parent):
    current, legacy = parent / 'Aholo3D-Rhino', parent / 'Aholo-Rhino'
    if current.is_dir():
        if legacy.is_dir() and not current.samefile(legacy):
            raise ValueError('Separate current and legacy data directories exist; no data was moved.')
        return current
    if current.exists() or current.is_symlink():
        raise ValueError('The Aholo3D data path is blocked or its link is broken.')
    if not legacy.is_dir():
        return current
    try:
        current.symlink_to(legacy, target_is_directory=True)
    except OSError:
        if current.is_dir() and current.samefile(legacy):
            return current
        if current.exists() or current.is_symlink():
            raise ValueError('The new data path changed during alias creation; inspect both directories.')
        # Windows can disallow unprivileged directory links. Runtime uses the same legacy authority.
        return legacy
    return current

def replace_legacy(package_root, install, running=rhino_running):
    legacy = package_root / 'aholo-rhino'
    if not legacy.exists():
        install()
        return None
    if running():
        raise RuntimeError('Close Rhino before replacing the legacy aholo-rhino package. No package was changed.')
    backup = package_root.parent / 'aholo3d-rename-backups' / uuid.uuid4().hex / legacy.name
    backup.parent.mkdir(parents=True)
    legacy.rename(backup)
    try:
        install()
    except BaseException:
        backup.rename(legacy)
        raise
    return backup

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive', type=Path)
    parser.add_argument('--yak', type=Path, default=Path('/Applications/Rhino 8.app/Contents/Resources/bin/yak') if sys.platform == 'darwin' else Path(os.environ.get('ProgramFiles', 'C:/Program Files'))/'Rhino 8/System/Yak.exe')
    args = parser.parse_args()
    archive = args.archive.resolve()
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    checksum = archive.with_suffix('.yak.sha256')
    if not checksum.is_file() or checksum.read_text().split()[0] != digest:
        raise ValueError('Missing or mismatched package checksum.')
    with zipfile.ZipFile(archive) as package:
        metadata = manifest_values(package.read('manifest.yml').decode())
        platform = 'mac' if sys.platform == 'darwin' else 'win' if os.name == 'nt' else ''
        if metadata.get('name') != 'aholo3d-rhino' or metadata.get('platform') != platform or not re.fullmatch(r'[0-9][0-9A-Za-z.+-]*', metadata.get('version', '')):
            raise ValueError('Package identity/platform/version mismatch.')
        for entry in package.infolist():
            if entry.filename.startswith('/') or '..' in Path(entry.filename).parts or '\\' in entry.filename:
                raise ValueError('Unsafe archive entry.')
        appdata = Path.home()/'Library/Application Support' if sys.platform == 'darwin' else Path(os.environ['APPDATA'])
        localdata = appdata if sys.platform == 'darwin' else Path(os.environ['LOCALAPPDATA'])
        package_root = appdata/'McNeel/Rhinoceros/packages/8.0'
        installed = package_root/metadata['name']/metadata['version']
        existed = installed.exists()
        def install():
            try:
                subprocess.run([str(args.yak), 'install', str(archive)], check=True)
                for entry in package.infolist():
                    if entry.is_dir():
                        continue
                    target = installed/entry.filename
                    if not target.is_file() or target.read_bytes() != package.read(entry):
                        raise ValueError('Installed bytes differ: '+entry.filename)
            except BaseException:
                if not existed and installed.is_dir():
                    shutil.rmtree(installed)
                raise
        # Creating an alias never moves records or changes old absolute file references.
        data_directory = data_alias(localdata)
        backup = replace_legacy(package_root, install)
    receipt = {'name': metadata['name'], 'version': metadata['version'], 'platform': platform,
               'archive': str(archive), 'sha256': digest, 'installedDirectory': str(installed),
               'installedBytesVerified': True, 'rhinoLaunched': False, 'hostAcceptance': 'pending',
               'dataDirectory': str(data_directory), 'legacyPackageBackup': str(backup) if backup else None}
    (ROOT/'artifacts').mkdir(exist_ok=True)
    (ROOT/'artifacts/latest-install.json').write_text(json.dumps(receipt, indent=2)+'\n')
    print(json.dumps(receipt, indent=2))
    print('Installation verified. Restart Rhino manually to load this version.')

if __name__ == '__main__':
    main()
