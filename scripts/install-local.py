#!/usr/bin/env python3
"""Install a local verified Aholo Yak archive and compare every installed payload byte. Does not launch Rhino."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import zipfile
from package import manifest_values

ROOT = Path(__file__).resolve().parent.parent

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive',type=Path)
    parser.add_argument('--yak',type=Path,default=Path('/Applications/Rhino 8.app/Contents/Resources/bin/yak') if sys.platform=='darwin' else Path(os.environ.get('ProgramFiles','C:/Program Files'))/'Rhino 8/System/Yak.exe')
    args=parser.parse_args()
    archive=args.archive.resolve()
    checksum=archive.with_suffix('.yak.sha256')
    digest=hashlib.sha256(archive.read_bytes()).hexdigest()
    if not checksum.is_file() or checksum.read_text().split()[0]!=digest: raise ValueError('Missing or mismatched package checksum.')
    with zipfile.ZipFile(archive) as package:
        metadata=manifest_values(package.read('manifest.yml').decode())
        platform='mac' if sys.platform=='darwin' else 'win' if os.name=='nt' else ''
        if metadata.get('name')!='aholo-rhino' or metadata.get('platform')!=platform: raise ValueError('Package identity/platform mismatch.')
        for entry in package.infolist():
            name=entry.filename
            if name.startswith('/') or '..' in Path(name).parts or '\\' in name: raise ValueError('Unsafe archive entry.')
        subprocess.run([str(args.yak), 'install', str(archive)],check=True)
        appdata=Path.home()/'Library/Application Support' if sys.platform=='darwin' else Path(os.environ['APPDATA'])
        installed=appdata/'McNeel/Rhinoceros/packages/8.0'/metadata['name']/metadata['version']
        for entry in package.infolist():
            if entry.is_dir(): continue
            target=installed/entry.filename
            if not target.is_file() or target.read_bytes()!=package.read(entry): raise ValueError('Installed bytes differ: '+entry.filename)
    receipt={'name':'aholo-rhino','version':metadata['version'],'platform':platform,'archive':str(archive),'sha256':digest,
             'installedDirectory':str(installed),'installedBytesVerified':True,'rhinoLaunched':False,'hostAcceptance':'pending'}
    (ROOT/'artifacts').mkdir(exist_ok=True)
    (ROOT/'artifacts/latest-install.json').write_text(json.dumps(receipt,indent=2)+'\n')
    print(json.dumps(receipt,indent=2))
    print('Installation verified. Restart Rhino manually to load this version.')

if __name__=='__main__': main()
