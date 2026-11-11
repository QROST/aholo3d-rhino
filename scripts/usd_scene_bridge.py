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


def convert(input_path, output):
    """Freeze dependencies once, validate the whole stage, then publish typed payload manifests."""
    from pxr import Ar, Sdf, Usd, UsdGeom, UsdShade, UsdUtils, UsdVol
    mesh, gs = sibling('usd_mesh_reader'), sibling('usd_bridge')
    mesh.require(Usd.GetVersion() == (0, 26, 11), 'Scene exchange requires usd-core==26.11.')
    source = input_path.resolve()
    mesh.require(source.is_file() and source.suffix.lower() in ('.usd','.usda','.usdc','.usdz'), 'Choose an existing local USD scene.')
    captured, source_hash, dependency_hashes = mesh.freeze_source(source, output, Ar, Sdf, UsdUtils)
    stage = Usd.Stage.Open(str(captured), load=Usd.Stage.LoadAll)
    mesh.require(bool(stage) and not stage.GetCompositionErrors(), 'USD scene could not be composed from its captured dependencies.')
    axis, units = str(UsdGeom.GetStageUpAxis(stage)), float(UsdGeom.GetStageMetersPerUnit(stage))
    mesh.require(axis in ('Y','Z') and math.isfinite(units) and units > 0, 'Invalid USD axis or units.')
    cache = UsdGeom.XformCache(Usd.TimeCode.Default())
    mesh_parts, gs_parts, warnings = [], [], []
    allowed = ('','Xform','Scope','Mesh','Material','Shader','NodeGraph','ParticleField3DGaussianSplat')
    for prim in Usd.PrimRange.Stage(stage, Usd.TraverseInstanceProxies()):
        mesh.validate_prim(prim, allowed)
        if prim.IsA(UsdGeom.Mesh):
            mesh_parts.append(mesh.write_mesh(prim,output,len(mesh_parts),cache,UsdGeom,UsdShade,Ar,Sdf))
        elif str(prim.GetTypeName()) == 'ParticleField3DGaussianSplat':
            mesh.require(UsdGeom.Imageable(prim).ComputePurpose() in ('default','render'), str(prim.GetPath())+': guide/proxy GS cannot be silently imported.')
            part = gs.write_part(prim,output,len(gs_parts),cache,UsdGeom,UsdVol,UsdShade)
            gs_parts.append(part)
            if part['projectionModeHint'] != 'perspective' or part['sortingModeHint'] != 'zDepth':
                warnings.append(part['name']+': renderer hints differ from the viewport camera projection and center-z sorting.')
    # Empty GS prims carry no particles; do not manufacture proxy geometry for them.
    nonempty = [p for p in gs_parts if p['count'] > 0]
    if len(nonempty) != len(gs_parts): warnings.append('Empty Gaussian parts were skipped; no particle data was removed.')
    gs_parts = nonempty
    mesh.require(mesh_parts or gs_parts, 'USD contains no supported static scene geometry.')
    if not stage.HasAuthoredMetadata('metersPerUnit'): warnings.append('USD units were unauthored; the OpenUSD 0.01 metre fallback was used.')
    if not stage.HasAuthoredMetadata('upAxis'): warnings.append('USD upAxis was unauthored; the OpenUSD fallback axis was used.')
    if len(dependency_hashes)>1: warnings.append('Source archival retains the root file only; complete GS caches and embedded mesh textures retain the imported scene. Share original USD with all dependencies.')
    if mesh_parts:
        warnings.append('Mesh import flattens instance hierarchy. Display is two-sided; materials use the supported USD shading subset.')
        if any(p['texture'] for p in mesh_parts): warnings.append('Texture filter settings are source metadata; final sampling is controlled by the viewer.')
    metadata = dict(protocol=1,openUsdVersion='26.11',upAxis=axis,metersPerUnit=units,sourceSha256=source_hash,dependencyHashes=dependency_hashes,warnings=warnings)
    def write(name, data):
        (output/name).write_text(json.dumps(data,ensure_ascii=False,allow_nan=False,separators=(',',':')),encoding='utf-8')
    if gs_parts: write('manifest.json',dict(metadata,parts=gs_parts))
    if mesh_parts: write('mesh-import.json',dict(metadata,parts=mesh_parts))
    # Last publication means no partial scene can be accepted after a failed extraction.
    write('scene-import.json',dict(metadata,gaussians=bool(gs_parts),meshes=bool(mesh_parts)))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--format', choices=('usd', 'usda', 'usdc', 'usdz'))
    parser.add_argument('--export', action='store_true')
    args = parser.parse_args()
    try:
        if args.export: export(args.input, args.output, args.format)
        else: convert(args.input, args.output)
    except Exception as error:
        try:
            (args.output / 'error.json').write_text(json.dumps({'message': str(error)}, ensure_ascii=False), encoding='utf-8')
        except OSError:
            print(str(error), file=sys.stderr)
        raise SystemExit(2)
