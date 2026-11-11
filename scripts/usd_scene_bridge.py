#!/usr/bin/env python3
"""Author a self-contained static mesh + Gaussian stage using the installed sibling bridges."""
import argparse
import importlib.util
import json
import math
from pathlib import Path
import sys


def sibling(name):
    # Python -I disables arbitrary script-directory imports; accept only installed siblings.
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(name + '.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def export(input_path, output, fmt):
    from pxr import Gf, Usd, UsdGeom
    mesh = sibling('usd_mesh_bridge')
    mesh.require(Usd.GetVersion() == (0, 26, 11), 'Scene exchange requires usd-core==26.11.')
    data = json.loads(input_path.read_text(encoding='utf-8'))
    mesh.require(data.get('protocol') == 1 and data.get('openUsdVersion') == '26.11', 'Unsupported scene transport protocol.')
    gs, meshes = data.get('gaussians'), data.get('meshes')
    mesh.require(type(gs) is bool and type(meshes) is bool and (gs or meshes), 'An export must contain GS or meshes.')
    stage = Usd.Stage.CreateInMemory()
    root = UsdGeom.Xform.Define(stage, '/Scene')
    stage.SetDefaultPrim(root.GetPrim())
    axis, units = 'Z', .001
    if gs:
        _, axis, units = sibling('usd_bridge').author(input_path.with_name('export.json'), stage, '/Scene/Gaussians')
    UsdGeom.SetStageUpAxis(stage, axis)
    UsdGeom.SetStageMetersPerUnit(stage, units)
    if meshes:
        mesh_root = mesh.author(input_path.with_name('mesh-export.json'), output, stage, '/Scene/Meshes')
        scale = .001 / units
        mesh.require(math.isfinite(scale) and scale > 0, 'Mesh-to-stage scale is not representable.')
        # USD row vectors: Z-up -> Y-up is (x,y,z) -> (x,z,-y), a proper rotation.
        matrix = Gf.Matrix4d(scale,0,0,0, 0,scale,0,0, 0,0,scale,0, 0,0,0,1) if axis == 'Z' else Gf.Matrix4d(scale,0,0,0, 0,0,-scale,0, 0,scale,0,0, 0,0,0,1)
        mesh_root.AddTransformOp(UsdGeom.XformOp.PrecisionDouble).Set(matrix)
    stage.GetRootLayer().documentation = 'Static Gaussian splats and tessellated meshes. Full SH is retained in local GS frames; geometry uses standard float32 precision with double-precision placement. Meshes are converted from Z-up millimetres once at their root.'
    mesh.finish(stage, output, fmt)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--format', choices=('usd', 'usda', 'usdc', 'usdz'), required=True)
    parser.add_argument('--export', action='store_true', required=True)
    args = parser.parse_args()
    try:
        export(args.input, args.output, args.format)
    except Exception as error:
        try:
            (args.output / 'error.json').write_text(json.dumps({'message': str(error)}, ensure_ascii=False), encoding='utf-8')
        except OSError:
            print(str(error), file=sys.stderr)
        raise SystemExit(2)
