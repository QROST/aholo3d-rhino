#!/usr/bin/env python3
"""Deterministic direct-texture 3MF sample; no service calls or native host execution."""
import argparse
import io
from pathlib import Path
import struct
import zipfile
import zlib


def bitmap(pixels):
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
    # Explicit unfiltered scanlines; 2x2 RGBA, top row first.
    scanlines = b''.join(b'\0' + bytes(sum(pixels[row:row+2], ())) for row in (0, 2))
    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 2, 2, 8, 6, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(scanlines, 9)) + chunk(b'IEND', b''))


def payload():
    core = 'http://schemas.microsoft.com/3dmanufacturing/core/2015/02'
    materials = 'http://schemas.microsoft.com/3dmanufacturing/material/2015/02'
    relations = 'http://schemas.openxmlformats.org/package/2006/relationships'
    texture_rel = 'http://schemas.microsoft.com/3dmanufacturing/2013/01/3dtexture'
    vertices = '<vertices><vertex x="0" y="0" z="0"/><vertex x="2" y="0" z="0"/><vertex x="2" y="2" z="0"/><vertex x="0" y="2" z="0"/></vertices>'
    triangles = '<triangles><triangle v1="0" v2="1" v3="2" p1="0" p2="1" p3="2"/><triangle v1="0" v2="2" v3="3" p1="0" p2="2" p3="3"/></triangles>'
    resources = ''
    for image_id, group_id, name in ((1, 2, 'checker'), (3, 4, 'alternate')):
        resources += f'<m:texture2d id="{image_id}" path="/3D/Textures/{name}.png" contenttype="image/png" tilestyleu="clamp" tilestylev="clamp" filter="nearest"/>'
        resources += f'<m:texture2dgroup id="{group_id}" texid="{image_id}"><m:tex2coord u="0" v="0"/><m:tex2coord u="1" v="0"/><m:tex2coord u="1" v="1"/><m:tex2coord u="0" v="1"/></m:texture2dgroup>'
    resources += '<basematerials id="5"><base name="Blue half opacity" displaycolor="#0044FF80"/></basematerials>'
    for object_id, group_id in ((10, 2), (11, 4)):
        resources += f'<object id="{object_id}" type="surface" name="Textured square {object_id}" pid="{group_id}" pindex="0"><mesh>{vertices}{triangles}</mesh></object>'
    resources += '<object id="12" type="surface" pid="5" pindex="0"><mesh><vertices><vertex x="0" y="0" z="0"/><vertex x="1" y="0" z="0"/><vertex x="0" y="1" z="0"/></vertices><triangles><triangle v1="0" v2="1" v3="2"/></triangles></mesh></object>'
    resources += '<object id="20"><components><component objectid="10"/><component objectid="11" transform="-1 0 0 0 1 0 0 0 1 4 0 0"/><component objectid="12" transform="1 0 0 0 1 0 0 0 1 0 3 0"/></components></object>'
    model = f'<model xmlns="{core}" xmlns:m="{materials}" unit="centimeter" requiredextensions="m"><metadata name="Designer">CuriousArc / qrost</metadata><resources>{resources}</resources><build><item objectid="20" transform="1 0 0 0 1 0 0 0 1 0 1 0"/></build></model>'
    parts = {
        '[Content_Types].xml': '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="model" ContentType="application/vnd.ms-package.3dmanufacturing-3dmodel+xml"/><Default Extension="png" ContentType="image/png"/></Types>',
        '_rels/.rels': f'<Relationships xmlns="{relations}"><Relationship Id="start" Type="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel" Target="/3D/scene.model"/></Relationships>',
        '3D/scene.model': model,
        '3D/_rels/scene.model.rels': f'<Relationships xmlns="{relations}"><Relationship Id="first" Type="{texture_rel}" Target="Textures/checker.png"/><Relationship Id="second" Type="{texture_rel}" Target="Textures/alternate.png"/></Relationships>',
        '3D/Textures/checker.png': bitmap(((255,0,0,255), (0,255,0,128), (0,0,255,64), (255,255,0,0))),
        '3D/Textures/alternate.png': bitmap(((255,255,255,255), (0,255,255,255), (255,0,255,255), (0,0,0,255))),
    }
    output = io.BytesIO()
    with zipfile.ZipFile(output, 'w') as archive:
        for name, data in sorted(parts.items()):
            info = zipfile.ZipInfo(name, (2026, 11, 11, 0, 0, 0))
            info.create_system = 3
            info.external_attr = 0o100600 << 16
            archive.writestr(info, data.encode('utf-8') if isinstance(data, str) else data)
    return output.getvalue()


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    path = Path(__file__).with_name('mesh-textured.3mf')
    data = payload()
    if args.check:
        if not path.is_file() or path.read_bytes() != data:
            raise SystemExit('Textured 3MF fixture differs from generator.')
    else:
        path.write_bytes(data)
    print('Textured 3MF fixture bytes verified; native appearance remains pending.')
