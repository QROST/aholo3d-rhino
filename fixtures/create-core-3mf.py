#!/usr/bin/env python3
"""Create/check a deterministic synthetic Core 3MF for manual Rhino acceptance."""
import argparse
import io
from pathlib import Path
import zipfile

def payload():
    core = 'http://schemas.microsoft.com/3dmanufacturing/core/2015/02'
    vertices = '<vertices><vertex x="0" y="0" z="0"/><vertex x="1" y="0" z="0"/><vertex x="0" y="1" z="0"/><vertex x="0" y="0" z="1"/></vertices>'
    triangles = '<triangles><triangle v1="0" v2="2" v3="1"/><triangle v1="0" v2="1" v3="3"/><triangle v1="0" v2="3" v3="2"/><triangle v1="1" v2="2" v3="3"/></triangles>'
    model = f'''<model xmlns="{core}" unit="centimeter" xml:lang="en-US">
<resources><basematerials id="1"><base name="Blue" displaycolor="#0080FFFF"/><base name="Orange" displaycolor="#FF8000FF"/></basematerials>
<object id="2" type="model" pid="1" pindex="0"><mesh>{vertices}{triangles}</mesh></object>
<object id="3" type="model" pid="1" pindex="1"><mesh>{vertices}{triangles}</mesh></object>
<object id="4" type="model"><components><component objectid="2"/><component objectid="3" transform="-1 0 0 0 1 0 0 0 1 3 0 0"/></components></object>
</resources><build><item objectid="4" transform="1 0 0 0 1 0 0 0 1 0 2 0"/></build></model>'''
    parts = {
        '[Content_Types].xml': '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="model" ContentType="application/vnd.ms-package.3dmanufacturing-3dmodel+xml"/></Types>',
        '_rels/.rels': '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="start" Type="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel" Target="/3D/scene.model"/></Relationships>',
        '3D/scene.model': model,
    }
    output=io.BytesIO()
    with zipfile.ZipFile(output, 'w') as archive:
        for name, text in parts.items():
            info=zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0));info.compress_type=zipfile.ZIP_DEFLATED
            archive.writestr(info, text.encode('utf-8'))
    return output.getvalue()

if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--check',action='store_true');args=parser.parse_args()
    path=Path(__file__).with_name('mesh-core.3mf');data=payload()
    if args.check:
        if not path.exists() or path.read_bytes()!=data: raise SystemExit('Core 3MF fixture differs from generator.')
    else: path.write_bytes(data)
    print('Core 3MF fixture bytes verified; native Rhino appearance remains a manual check.')
