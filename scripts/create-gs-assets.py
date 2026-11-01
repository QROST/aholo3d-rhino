#!/usr/bin/env python3
"""Generate the independently defined, straight-alpha 3-sigma Gaussian texture."""
import argparse
import math
from pathlib import Path
import struct
import zlib

ROOT = Path(__file__).resolve().parent.parent

def chunk(kind, data):
    return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))

def texture(atlas=False):
    size = 1040 if atlas else 257
    rows = bytearray()
    for y in range(size):
        rows.append(0)  # PNG filter: none
        for x in range(size):
            # The center texel is exactly zero; opposite texels are symmetric.
            if atlas:
                # UV row zero is the bottom tile row; PNG rows are top-down.
                level = (15 - y // 65) * 16 + x // 65
                radius2 = ((x % 65 - 32) * 3 / 32) ** 2 + ((y % 65 - 32) * 3 / 32) ** 2
            else:
                level = 255
                radius2 = ((x - 128) * 3 / 128) ** 2 + ((y - 128) * 3 / 128) ** 2
            alpha = round(level * math.exp(-0.5 * radius2)) if radius2 <= 9 else 0
            rows.extend((255, 255, 255, alpha))
    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(bytes(rows), 9)) + chunk(b'IEND', b''))

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    for name, atlas in [('gaussian-alpha.png', False), ('gaussian-opacity-atlas.png', True)]:
        path = ROOT / 'assets' / name
        data = texture(atlas)
        if args.check:
            if not path.exists() or path.read_bytes() != data:
                raise SystemExit(f'{name} differs from its deterministic definition.')
            print(f'{name}: deterministic straight RGBA, 3 sigma, verified.')
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
            print(f'Wrote assets/{name}')

if __name__ == '__main__':
    main()
