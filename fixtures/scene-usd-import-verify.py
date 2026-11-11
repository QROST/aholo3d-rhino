#!/usr/bin/env python3
"""Mixed scene import evidence using real pinned OpenUSD and the public C# reader; no Rhino/cloud."""
import argparse
import importlib.util
import json
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
from pxr import Gf, Sdf, Usd, UsdGeom, UsdShade, UsdVol, Vt

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--reference',type=Path,default=ROOT/'tools/Aholo.Reference/bin/Release/net8.0/Aholo.Reference.dll')
args=parser.parse_args()
spec=importlib.util.spec_from_file_location('aholo_scene_bridge',ROOT/'scripts/usd_scene_bridge.py')
bridge=importlib.util.module_from_spec(spec);spec.loader.exec_module(bridge)


def scene(path):
    stage=Usd.Stage.CreateNew(str(path));UsdGeom.SetStageMetersPerUnit(stage,.3048);UsdGeom.SetStageUpAxis(stage,'Y')
    root=UsdGeom.Xform.Define(stage,'/Scene');stage.SetDefaultPrim(root.GetPrim())
    g=UsdVol.ParticleField3DGaussianSplat.Define(stage,'/Scene/Gaussian')
    g.CreatePositionsAttr(Vt.Vec3fArray([Gf.Vec3f(1,2,3)]));g.CreateScalesAttr(Vt.Vec3fArray([Gf.Vec3f(1,.5,.25)]))
    g.CreateOrientationsAttr(Vt.QuatfArray([Gf.Quatf(.5,.5,-.5,.5)]));g.CreateOpacitiesAttr(Vt.FloatArray([.75]))
    g.CreateRadianceSphericalHarmonicsDegreeAttr(1);g.CreateRadianceSphericalHarmonicsCoefficientsAttr(Vt.Vec3fArray([Gf.Vec3f(k*.125,k*.25,k*.5) for k in range(4)]))
    m=UsdGeom.Mesh.Define(stage,'/Scene/Mesh');m.CreateSubdivisionSchemeAttr('none')
    m.CreatePointsAttr(Vt.Vec3fArray([Gf.Vec3f(0,0,0),Gf.Vec3f(1,0,0),Gf.Vec3f(0,1,0)]));m.CreateFaceVertexCountsAttr([3]);m.CreateFaceVertexIndicesAttr([0,1,2])
    return stage,g,m


with tempfile.TemporaryDirectory(prefix='aholo-scene-import-') as tmp:
    work=Path(tmp);original=work/'production';env=dict(os.environ,AHOLO_USD_PYTHON=sys.executable)
    subprocess.run(['dotnet',str(args.reference),'--scene-fixtures',str(original)],env=env,check=True)
    subprocess.run(['dotnet',str(args.reference),'--scene-import-fixtures',str(original)],env=env,check=True)
    # Once frozen, the complete source tree disappears. Neither data path may read it again.
    root=original/'mixed-Y-ft.usda';expected=hashlib.sha256(root.read_bytes()).hexdigest();output=work/'frozen';output.mkdir()
    sibling=bridge.sibling;mesh_reader=sibling('usd_mesh_reader');freeze=mesh_reader.freeze_source;calls=[]
    def freezing(*arguments):
        result=freeze(*arguments);calls.append(1);shutil.rmtree(original);return result
    mesh_reader.freeze_source=freezing
    bridge.sibling=lambda name:mesh_reader if name=='usd_mesh_reader' else sibling(name)
    try:bridge.convert(root,output)
    finally:bridge.sibling=sibling
    manifest=json.loads((output/'scene-import.json').read_text())
    assert len(calls)==1 and not original.exists() and manifest['sourceSha256']==expected
    assert manifest['gaussians'] and manifest['meshes']
    assert len(json.loads((output/'manifest.json').read_text())['parts'])==5
    assert len(json.loads((output/'mesh-import.json').read_text())['parts'])==3
    images=list(output.glob('*.png'));assert len(images)==1
    assert hashlib.sha256(images[0].read_bytes()).hexdigest()=='ce261ebea422e4b82e9aa5f2e86a4b1f90ebae7d267d16ebf0cb6002604117c6'

    defects={
        'bad-mesh-index':lambda s,g,m:m.GetFaceVertexIndicesAttr().Set([0,1,99]),
        'bad-gs-coefficients':lambda s,g,m:g.GetRadianceSphericalHarmonicsCoefficientsAttr().Set(Vt.Vec3fArray([Gf.Vec3f(1)])),
        'zero-gs-quaternion':lambda s,g,m:g.GetOrientationsAttr().Set(Vt.QuatfArray([Gf.Quatf(0,0,0,0)])),
        'hidden-gs':lambda s,g,m:UsdGeom.Imageable(g).CreateVisibilityAttr('invisible'),
        'guide-gs':lambda s,g,m:UsdGeom.Imageable(g).CreatePurposeAttr('guide'),
        'point-instancer':lambda s,g,m:UsdGeom.PointInstancer.Define(s,'/Scene/Instancer'),
        'unknown-geometry':lambda s,g,m:UsdGeom.Sphere.Define(s,'/Scene/Sphere'),
        'vendor-gs-on-xform':lambda s,g,m:UsdGeom.Xform.Define(s,'/Scene/Vendor').GetPrim().CreateAttribute('radiance:sphericalHarmonicsCoefficients',Sdf.ValueTypeNames.Float3Array).Set(Vt.Vec3fArray([Gf.Vec3f(1)])),
        'animated-gs':lambda s,g,m:g.GetPositionsAttr().Set(Vt.Vec3fArray([Gf.Vec3f(2,3,4)]),1),
        'animated-material':lambda s,g,m:UsdShade.Shader.Define(s,'/Scene/Shader').CreateInput('roughness',Sdf.ValueTypeNames.Float).Set(.5,1),
        'missing-layer':lambda s,g,m:s.GetRootLayer().subLayerPaths.append('missing.usda'),
        'connected-geometry':lambda s,g,m:m.GetPointsAttr().AddConnection('/Scene/Gaussian.positions'),
        'bound-gs-material':lambda s,g,m:UsdShade.MaterialBindingAPI.Apply(g.GetPrim()).Bind(UsdShade.Material.Define(s,'/Scene/Material')),
        'non-static-motion':lambda s,g,m:m.CreateVelocitiesAttr(Vt.Vec3fArray([Gf.Vec3f(1,0,0)]*3)),
    }
    for name,change in defects.items():
        path=work/(name+'.usda');stage,g,m=scene(path);change(stage,g,m)
        if name in ('bad-gs-coefficients','zero-gs-quaternion'):
            stage.GetPrimAtPath('/Scene').SetChildrenReorder(['Mesh','Gaussian'])
        stage.GetRootLayer().Save();stage=None
        out=work/(name+'-output');out.mkdir()
        try:bridge.convert(path,out)
        except (ValueError,RuntimeError):pass
        else:raise AssertionError(name+' was accepted')
        assert not (out/'scene-import.json').exists(),name+' published a partial scene'
        assert not (out/'mesh-import.json').exists() and not (out/'manifest.json').exists()
        if name in ('bad-gs-coefficients','zero-gs-quaternion'): assert (out/'mesh-000000.bin').is_file()

    demo=work/'demo-output';demo.mkdir();bridge.convert(ROOT/'fixtures/mixed-scene.usda',demo)
    metadata=json.loads((demo/'scene-import.json').read_text());assert metadata['metersPerUnit']==1 and metadata['upAxis']=='Z'
    demo_gs=json.loads((demo/'manifest.json').read_text())['parts'];demo_mesh=json.loads((demo/'mesh-import.json').read_text())['parts']
    assert len(demo_gs)==2 and sum(p['count'] for p in demo_gs)==2 and len(demo_mesh)==1
    assert demo_mesh[0]['vertices']==8 and demo_mesh[0]['faces']==6
    import struct
    points=struct.unpack('<24f',(demo/demo_mesh[0]['file']).read_bytes()[:96])
    assert max(points[2::3])-min(points[2::3])==2

    path=work/'instances.usda';stage,g,m=scene(path)
    copy=stage.DefinePrim('/Copy','Xform');copy.GetReferences().AddInternalReference('/Scene');copy.SetInstanceable(True)
    UsdGeom.Xformable(copy).AddTranslateOp().Set(Gf.Vec3d(10,20,30));stage.GetRootLayer().Save();stage=None
    out=work/'instances-output';out.mkdir();bridge.convert(path,out)
    gs=json.loads((out/'manifest.json').read_text())['parts'];meshes=json.loads((out/'mesh-import.json').read_text())['parts']
    assert len(gs)==len(meshes)==2
    assert gs[1]['transform'][-3:]==[10,20,30] and meshes[1]['transform'][-3:]==[10,20,30]
    assert gs[0]['name']!=gs[1]['name'] and meshes[0]['primPath']!=meshes[1]['primPath']
print('Mixed USD import verified: 24 public-reader round trips, one frozen snapshot after original deletion, instance placements, packaged 2-metre demo and 14 complete-scene rejection cases. Native Rhino acceptance remains separate.')
