#!/usr/bin/env python3
"""Decode synthetic source/production RGB PNGs and embedded GLB images with Pillow.

Offline oracle only. Requires Pillow (verified with 10.4.0); does not install it,
start Rhino, or call cloud services. Build Aholo3D.Reference Release first.
"""
import argparse
import io
import json
from pathlib import Path
import struct
import subprocess
import tempfile
import zlib
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
PASSES = ((0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4),
          (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2))


def pixel(x, y):
    return ((31+x*19+y*7) % 256, (197+x*3+y*11) % 256,
            (83+x*13+y*5) % 256, ((x+y) % 4)*85)


def chunk(kind, data):
    return struct.pack('>I', len(data))+kind+data+struct.pack('>I', zlib.crc32(kind+data))


def source_png(width, height, interlace, filter_kind):
    raw = bytearray()
    for sx, sy, dx, dy in PASSES if interlace else ((0, 0, 1, 1),):
        if sx >= width or sy >= height:
            continue
        previous = bytes(len(range(sx, width, dx))*4)
        for y in range(sy, height, dy):
            row = bytes(v for x in range(sx, width, dx) for v in pixel(x, y))
            kind = y % 5 if filter_kind < 0 else filter_kind
            raw.append(kind)
            for i, sample in enumerate(row):
                left, up, corner = (row[i-4] if i >= 4 else 0), previous[i], (previous[i-4] if i >= 4 else 0)
                p = left+up-corner
                paeth = min((left, up, corner), key=lambda v: abs(v-p))
                prediction = (0, left, up, (left+up)//2, paeth)[kind]
                raw.append((sample-prediction) % 256)
            previous = row
    compressed = zlib.compress(raw)
    # Many chunk boundaries, including zlib header/footer fragmentation and empty IDAT.
    encoded = b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, int(interlace)))
    encoded += chunk(b'sRGB', b'\x00')+chunk(b'IDAT', b'')
    encoded += b''.join(chunk(b'IDAT', compressed[i:i+7]) for i in range(0, len(compressed), 7))
    return encoded+chunk(b'IEND', b'')


def decode(encoded, mode, size, expected):
    with Image.open(io.BytesIO(encoded)) as image:
        image.load()
        assert image.mode == mode and image.size == size
        assert list(image.getdata()) == expected


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--reference', type=Path, default=ROOT/'tools/Aholo3D.Reference/bin/Release/net8.0/Aholo3D.Reference.dll')
    args = parser.parse_args()
    cases = [(19, 13, interlace, kind) for interlace in (False, True) for kind in range(5)]
    cases += [(w, h, True, -1) for w, h in ((1, 1), (1, 9), (9, 1), (2, 3), (3, 2), (7, 7))]
    cases += [(257, 129, interlace, -1) for interlace in (False, True)]
    with tempfile.TemporaryDirectory(prefix='aholo3d-png-alpha-') as temporary:
        work = Path(temporary)
        (work/'input').mkdir()
        for index, (w, h, interlace, kind) in enumerate(cases):
            encoded = source_png(w, h, interlace, kind)
            decode(encoded, 'RGBA', (w, h), [pixel(x, y) for y in range(h) for x in range(w)])
            (work/'input'/f'{index}.png').write_bytes(encoded)
        subprocess.run(['dotnet', str(args.reference), '--png-alpha-fixtures', str(work)], check=True)
        for index, (w, h, interlace, _) in enumerate(cases):
            source = (work/'input'/f'{index}.png').read_bytes()
            converted = (work/f'{index}-rgb.png').read_bytes()
            expected = [pixel(x, y)[:3] for y in range(h) for x in range(w)]
            decode(converted, 'RGB', (w, h), expected)
            assert converted[28] == int(interlace)
            data = (work/f'{index}.glb').read_bytes()
            assert struct.unpack_from('<III', data) == (0x46546C67, 2, len(data))
            length, kind = struct.unpack_from('<II', data, 12)
            assert kind == 0x4E4F534A
            document = json.loads(data[20:20+length])
            assert len(document['images']) == 2
            binary = 28+length
            for i, alpha in enumerate((.6, 1, .4, 0)):
                material = document['materials'][i]
                pbr = material['pbrMetallicRoughness']
                assert pbr['baseColorFactor'][3] == alpha
                assert material['alphaMode'] == ('OPAQUE' if i == 1 else 'BLEND')
                texture = document['textures'][pbr['baseColorTexture']['index']]
                view = document['bufferViews'][document['images'][texture['source']]['bufferView']]
                start = binary+view['byteOffset']
                image = data[start:start+view['byteLength']]
                if i < 2:
                    assert image == source
                else:
                    assert image == converted
                    decode(image, 'RGB', (w, h), expected)
                sampler = document['samplers'][texture['sampler']]
                assert sampler['wrapS'] == (33071 if i == 3 else 10497)
                assert sampler['minFilter'] == (9728 if i == 3 else 9729)
    print(f'PASS: {len(cases)} source/RGB pairs and {len(cases)*4} GLB material/image bindings checked with Pillow. No native or cloud visual acceptance.')


if __name__ == '__main__':
    main()
