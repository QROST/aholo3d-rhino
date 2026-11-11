#!/usr/bin/env python3
"""Stage an allowlisted candidate, build via Yak, and validate all archived bytes. Never publish."""
import argparse
import hashlib
import json
import re
import pathlib
import shutil
import subprocess
import sys
import tempfile
import zipfile
from datetime import datetime, timezone
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent

def manifest_values(text):
    # Deliberately accept only the simple metadata subset emitted by this manifest and Yak.
    values, current = {}, None
    for raw in text.splitlines():
        if not raw.strip() or raw == '---' or raw.lstrip().startswith('#'): continue
        if raw.lstrip().startswith('- '):
            if current not in ('authors','keywords'): raise ValueError('Unexpected manifest list.')
            values[current].append(raw.strip()[2:]); continue
        if raw.startswith(' '):
            if current != 'description': raise ValueError('Unexpected manifest continuation.')
            values[current] = (values[current] + ' ' + raw.strip()).strip(); continue
        match = re.fullmatch(r'([A-Za-z]+):(?: (.*))?', raw)
        if not match or match[1] in values: raise ValueError('Invalid manifest field.')
        current, value = match[1], match[2] or ''
        if current in ('authors','keywords'): values[current] = []
        elif value == '>-': values[current] = ''
        elif value.startswith("'") and value.endswith("'"): values[current] = value[1:-1].replace("''", "'")
        else: values[current] = value
    return values

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--platform', choices=['mac', 'win'], required=True)
    parser.add_argument('--yak')
    parser.add_argument('--stage-only', action='store_true', help='Validate staged build payload without Yak, installation or publication.')
    parser.add_argument('--checks', choices=['passed', 'not-rerun'], default='not-rerun')
    args = parser.parse_args()
    if not args.stage_only and not args.yak:
        parser.error('--yak is required unless --stage-only is used.')
    subprocess.run([sys.executable, str(ROOT / "scripts/create-gs-assets.py"), "--check"], check=True)
    version = ET.parse(ROOT / 'Directory.Build.props').findtext('.//Version')
    manifest = (ROOT / 'packaging/manifest.yml').read_text()
    if f'version: {version}\n' not in manifest or 'name: aholo-rhino\n' not in manifest:
        raise ValueError('Manifest/project identities disagree.')
    artifacts = ROOT / 'artifacts'; artifacts.mkdir(exist_ok=True)
    stage = pathlib.Path(tempfile.mkdtemp(prefix='package-' + args.platform + '.', dir=artifacts))
    required = {
        'Aholo.Rhino.rhp': 'src/Aholo.Rhino/bin/Release/net7.0/Aholo.Rhino.rhp',
        'System.IO.Packaging.dll': 'src/Aholo.Rhino/bin/Release/net7.0/System.IO.Packaging.dll',
        'sidecar/System.IO.Packaging.dll': 'src/Aholo.Sidecar/bin/Release/net8.0/System.IO.Packaging.dll',
        'docs/licenses/System.IO.Packaging.txt': 'docs/licenses/System.IO.Packaging.txt',
        'ZstdSharp.dll': 'src/Aholo.Rhino/bin/Release/net7.0/ZstdSharp.dll',
        'sidecar/ZstdSharp.dll': 'src/Aholo.Sidecar/bin/Release/net8.0/ZstdSharp.dll',
        'docs/licenses/SPZ.txt': 'docs/licenses/SPZ.txt',
        'docs/licenses/ZstdSharp.txt': 'docs/licenses/ZstdSharp.txt',
        'Aholo.Core.dll': 'src/Aholo.Rhino/bin/Release/net7.0/Aholo.Core.dll',
        'sidecar/Aholo.Sidecar.dll': 'src/Aholo.Sidecar/bin/Release/net8.0/Aholo.Sidecar.dll',
        'sidecar/Aholo.Sidecar.deps.json': 'src/Aholo.Sidecar/bin/Release/net8.0/Aholo.Sidecar.deps.json',
        'sidecar/Aholo.Sidecar.runtimeconfig.json': 'src/Aholo.Sidecar/bin/Release/net8.0/Aholo.Sidecar.runtimeconfig.json',
        'sidecar/Aholo.Core.dll': 'src/Aholo.Sidecar/bin/Release/net8.0/Aholo.Core.dll',
        'sidecar/Aholo.Provider.dll': 'src/Aholo.Sidecar/bin/Release/net8.0/Aholo.Provider.dll',
        'manifest.yml': 'packaging/manifest.yml',
        'usd/usd_bridge.py': 'src/Aholo.Rhino/bin/Release/net7.0/usd/usd_bridge.py',
        'scripts/setup-usd.py': 'scripts/setup-usd.py',
        'docs/usd-exchange.md': 'docs/usd-exchange.md',
    }
    for name in ['LICENSE','NOTICE','readme.md','readme.zh-CN.md','fixtures/tiny-world.ply','fixtures/mesh-core.3mf',
                 'fixtures/gaussian-anisotropic.ply','fixtures/gaussian-sh3.ply','assets/gaussian-alpha.png','assets/gaussian-opacity-atlas.png',
                 'docs/gs-host-acceptance.md','docs/gs-freeze-repair.md','docs/gs-visibility-stability.md','docs/decisions/0001-native-gaussian-viewport.md',
                 'docs/architecture.md','docs/repository-rename.md','docs/host-acceptance.md','docs/validation.md','docs/api/README.md',
                 'docs/api/world.openapi.json','docs/api/rendercloud.openapi.json','docs/api/asset.openapi.json',
                 'docs/api/lux3d.openapi.json','docs/api/spatialunderstanding.openapi.json','docs/api-capabilities.md',
                 'docs/implementation-progress.md','docs/integrated-acceptance.md','docs/mesh-formats.md']:
        required[name] = name
    for target, source in required.items():
        destination = stage / target; destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / source, destination)
    config = json.loads((stage/'sidecar/Aholo.Sidecar.runtimeconfig.json').read_text())
    framework = config['runtimeOptions']['framework']
    if framework['name'] != 'Microsoft.NETCore.App' or not framework['version'].startswith('8.'):
        raise ValueError('Unexpected background runtime requirement.')
    if args.stage_only:
        actual = {p.relative_to(stage).as_posix() for p in stage.rglob('*') if p.is_file()}
        if actual != set(required): raise ValueError('Staged contents do not match the allowlist.')
        for target, source in required.items():
            if (stage / target).read_bytes() != (ROOT / source).read_bytes():
                raise ValueError('Staged bytes differ: ' + target)
        print(json.dumps({'stage': str(stage), 'platform': args.platform, 'files': len(required), 'archiveBuilt': False}))
        print('Staged payload verified. This is not a Yak archive or native host acceptance.')
        return
    subprocess.run([str(pathlib.Path(args.yak).resolve()),'build','--platform',args.platform],cwd=stage,check=True)
    archives = list(stage.glob('*.yak'))
    if len(archives) != 1: raise ValueError('Expected exactly one package.')
    archive = archives[0]
    if archive.name != f'aholo-rhino-{version}-rh8_32-{args.platform}.yak':
        raise ValueError('Unexpected Rhino target, platform or package identity: ' + archive.name)
    with zipfile.ZipFile(archive) as package:
        files = [i for i in package.infolist() if not i.is_dir()]
        names = [i.filename for i in files]
        if len(set(n.casefold() for n in names)) != len(names): raise ValueError('Duplicate archive entries.')
        expected = set(required)
        if set(names) != expected: raise ValueError('Unexpected archive contents: '+str(set(names)^expected))
        for entry in files:
            path = pathlib.PurePosixPath(entry.filename)
            if path.is_absolute() or '..' in path.parts or '\\' in entry.filename: raise ValueError('Unsafe archive path.')
            if entry.filename == 'manifest.yml':
                expected_manifest = manifest_values(manifest); expected_manifest['platform'] = args.platform
                if manifest_values(package.read(entry.filename).decode()) != expected_manifest:
                    raise ValueError('Archive metadata differs from source and requested platform.')
            elif package.read(entry.filename) != (stage / entry.filename).read_bytes():
                raise ValueError('Archive content mismatch: ' + entry.filename)
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    archive.with_suffix('.yak.sha256').write_text(digest+'  '+archive.name+'\n')
    commit = subprocess.check_output(['git','rev-parse','HEAD'], cwd=ROOT, text=True).strip()
    dirty = bool(subprocess.check_output(['git','status','--porcelain','--untracked-files=no'], cwd=ROOT, text=True).strip())
    receipt = {'sourceCommit':commit,'trackedSourceDirty':dirty,'name':'aholo-rhino','version':version,'platform':args.platform,'path':str(archive),
        'sha256':digest,'files':len(required),'checks':args.checks,'hostAcceptance':'pending',
        'serviceAcceptance':'pending','createdUtc':datetime.now(timezone.utc).isoformat()}
    (artifacts / f'latest-package-{args.platform}.json').write_text(json.dumps(receipt,indent=2)+'\n')
    print(json.dumps(receipt,indent=2))
    print('Local candidate only. No installation or publication performed.')

if __name__ == '__main__': main()
