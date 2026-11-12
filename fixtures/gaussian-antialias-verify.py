#!/usr/bin/env python3
"""Independent Decimal and matrix oracle for GS opacity compensation. No Rhino, GPU or network."""
import argparse
from decimal import Decimal, localcontext
import json
import math
from pathlib import Path
import subprocess
import tempfile

ROOT=Path(__file__).resolve().parents[1]


def close(actual, expected, tolerance=2e-12):
    assert math.isfinite(actual) and abs(actual-expected) <= abs(expected)*tolerance, (actual, expected)


def dot(a, b):
    return sum(x*y for x, y in zip(a, b))


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--reference', type=Path, default=ROOT/'tools/Aholo3D.Reference/bin/Release/net8.0/Aholo3D.Reference.dll')
    args=parser.parse_args()
    with tempfile.TemporaryDirectory(prefix='aholo3d-antialias-') as directory:
        subprocess.run(['dotnet', str(args.reference), '--antialias-fixtures', directory], check=True)
        document=json.loads((Path(directory)/'antialias.json').read_text())
        for sample in document['Determinants']:
            with localcontext() as context:
                context.prec=120
                x,b,y,f=(Decimal.from_float(float(sample[k])) for k in ('X','B','Y','FilteredDeterminant'))
                expected=float((max(Decimal(0),x*y-b*b)/f).sqrt())
            close(sample['Actual'], expected)
        for sample in document['Projections']:
            matrix=sample['Matrix'];scales=sample['Scales']
            # Form world covariance explicitly from transformed independent Gaussian axes.
            covariance=[[sum(matrix[i][k]*matrix[j][k]*scales[k]**2 for k in range(3)) for j in range(3)] for i in range(3)]
            position=[dot(row,sample['Center'])+t for row,t in zip(matrix,sample['Translation'])]
            x,y,z=position;fx,fy=sample['Fx'],sample['Fy']
            jx=[fx/z,0,-fx*x/z**2] if sample['Perspective'] else [fx,0,0]
            jy=[0,-fy/z,fy*y/z**2] if sample['Perspective'] else [0,-fy,0]
            def quadratic(a,b):
                return dot(a,[dot(row,b) for row in covariance])
            xx,xy,yy=quadratic(jx,jx),quadratic(jx,jy),quadratic(jy,jy)
            raw=xx*yy-xy*xy;filtered=(xx+sample['Filter'])*(yy+sample['Filter'])-xy*xy
            opacity=sample['OriginalOpacity']*math.sqrt(raw/filtered)
            for actual in (sample['Reference'],sample['Batch']):
                for a,e in zip(actual,(xx+sample['Filter'],xy,yy+sample['Filter'],opacity)):
                    close(a,e)
                # Area integral is alpha * 2*pi*sqrt(det); common 2*pi cancels.
                close(actual[3]*math.sqrt(filtered),sample['OriginalOpacity']*math.sqrt(raw))
        print(f"PASS: {len(document['Determinants'])} high-precision determinant cases and {len(document['Projections'])} affine projection/integral cases; no native/GPU visual acceptance.")


if __name__=='__main__':
    main()
