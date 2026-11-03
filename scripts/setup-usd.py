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
        return Path(os.environ['LOCALAPPDATA']) / 'Aholo-Rhino'
    if sys.platform == 'darwin':
        return Path.home() / 'Library/Application Support/Aholo-Rhino'
    return Path(os.environ.get('XDG_DATA_HOME', Path.home() / '.local/share')) / 'Aholo-Rhino'

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--runtime', type=Path, default=data_root() / 'usd-runtime')
    parser.add_argument('--check', action='store_true', help='Only verify; do not install anything.')
    parser.add_argument('--wheel-dir', type=Path, help='Install offline from a pre-downloaded wheel directory.')
    args = parser.parse_args()
    runtime = args.runtime.expanduser().resolve()
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
    receipt.update({'runtime': str(runtime), 'python': str(python), 'configuredDefault': runtime == (data_root() / 'usd-runtime').resolve()})
    print(json.dumps(receipt, indent=2))
    if not receipt['configuredDefault']:
        print('Set AHOLO_USD_PYTHON to the Python path above before starting Rhino, or use the default runtime directory.')

if __name__ == '__main__':
    main()
