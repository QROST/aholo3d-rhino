#!/usr/bin/env python3
"""Install/check the optional, private OpenUSD decoder runtime. Never changes Rhino or system Python."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import venv

VERSION = '26.11'

def data_root():
    if os.name == 'nt':
        parent = Path(os.environ['LOCALAPPDATA'])
    elif sys.platform == 'darwin':
        parent = Path.home() / 'Library/Application Support'
    else:
        parent = Path(os.environ.get('XDG_DATA_HOME', Path.home() / '.local/share'))
    current, legacy = parent / 'Aholo3D-Rhino', parent / 'Aholo-Rhino'
    if current.is_dir() and legacy.is_dir() and not current.samefile(legacy):
        raise ValueError('Separate current and legacy data directories exist. Resolve the conflict or pass --runtime explicitly.')
    if current.is_dir():
        return current
    if current.exists() or current.is_symlink():
        raise ValueError('The Aholo3D data directory is blocked or its link is broken.')
    return legacy if legacy.is_dir() else current

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--runtime', type=Path)
    parser.add_argument('--check', action='store_true', help='Only verify; do not install anything.')
    parser.add_argument('--wheel-dir', type=Path, help='Install offline from a pre-downloaded wheel directory.')
    args = parser.parse_args()
    runtime = (args.runtime or data_root() / 'usd-runtime').expanduser().resolve()
    python = runtime / ('Scripts/python.exe' if os.name == 'nt' else 'bin/python3')
    if not args.check:
        if not (3, 11) <= sys.version_info[:2] < (3, 15):
            parser.error('Run this script with Python 3.11–3.14. The macOS system Python may be older.')
        runtime.parent.mkdir(parents=True, exist_ok=True)
        if not python.exists():
            venv.EnvBuilder(with_pip=True).create(runtime)
        command = [str(python), '-I', '-m', 'pip', 'install', '--disable-pip-version-check', '--only-binary=:all:']
        if args.wheel_dir:
            command += ['--no-index', '--find-links', str(args.wheel_dir.resolve())]
        command += ['usd-core==' + VERSION]
        subprocess.run(command, check=True)
    if not python.is_file():
        parser.error('The private USD runtime is missing; run setup without --check.')
    probe = "from pxr import Usd,UsdVol; import json; assert Usd.GetVersion() == (0,26,11); assert hasattr(UsdVol,'ParticleField3DGaussianSplat'); print(json.dumps({'openUsdVersion':'.'.join(map(str,Usd.GetVersion())),'gaussianSchema':True}))"
    result = subprocess.run([str(python), '-I', '-c', probe], check=True, capture_output=True, text=True)
    receipt = json.loads(result.stdout)
    try:
        is_default = runtime == (data_root() / 'usd-runtime').resolve()
    except ValueError:
        is_default = False
    receipt.update({'runtime': str(runtime), 'python': str(python), 'configuredDefault': is_default})
    print(json.dumps(receipt, indent=2))
    if not receipt['configuredDefault']:
        print('Set AHOLO3D_USD_PYTHON to the Python path above before starting Rhino, or use the default runtime directory.')

if __name__ == '__main__':
    main()
