#!/usr/bin/env python3
"""Deterministic, local-only OBJ native acceptance sample. No cloud calls or Rhino automation."""
import argparse
import base64
import io
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parent
OBJ = '''# CuriousArc / qrost — units are explicitly selected during import.
mtllib materials.mtl
v 0 0 0
v 2 0 0
v 2 2 0
v 0 2 0
v 0 2 1
v 2 2 1
v 0 0 1
v 1 0 0
vt 0 0
vt 1 0
vt 1 1
vt 0 1
s off
o Floor
g floor
usemtl checker
f 1/1 2/2 3/3 4/4
o Panel
g rear panel
usemtl blue
f 4 3 6 5
o Triangle
g front
usemtl red
f 1 7 8
'''
MTL = '''# RGB image alpha must not control OBJ dissolve.
newmtl checker
Kd 1 1 1
map_Kd -clamp on checker.png
newmtl blue
Kd .05 .25 .8
d .6
Pr .5
newmtl red
Kd .8 .05 .02
Pr .8
'''
# This is a structurally validated 2x2 RGBA reference image used by the mesh tests.
PNG = base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAF0lEQVR4nAXBAQEAAACCIKb33EAkQxUHQdUHepwfNGcAAAAASUVORK5CYII=')
ALTERNATE = b'o Alternate\nv 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n'

def payloads():
    assets = {'mesh-obj/scene.obj': OBJ.encode(), 'mesh-obj/materials.mtl': MTL.encode(), 'mesh-obj/checker.png': PNG}
    memory = io.BytesIO()
    with zipfile.ZipFile(memory, 'w', compression=zipfile.ZIP_STORED) as archive:
        for name, data in sorted({**assets, 'alternate.obj': ALTERNATE}.items()):
            entry = zipfile.ZipInfo(name, (2026, 11, 11, 0, 0, 0))
            entry.create_system = 3
            entry.external_attr = 0o100600 << 16
            archive.writestr(entry, data)
    return {**assets, 'mesh-obj-multiple.zip': memory.getvalue()}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    for name, data in payloads().items():
        target = ROOT / name
        if args.check:
            if not target.is_file() or target.read_bytes() != data:
                raise SystemExit('OBJ fixture differs: ' + name)
        else:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
    print('OBJ fixture bytes verified.' if args.check else 'OBJ fixtures written.')

if __name__ == '__main__':
    main()
