#!/usr/bin/env python3
"""Actual pinned-OpenUSD mesh import regression checks; no Rhino or cloud calls."""
from pathlib import Path
import array
import hashlib
import importlib.util
import json
import os
import shutil
import subprocess
import sys
import tempfile
from pxr import Gf, Sdf, Usd, UsdGeom, UsdShade, UsdUtils, Vt

root = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('reader', root/'scripts/usd_mesh_reader.py')
reader = importlib.util.module_from_spec(spec); spec.loader.exec_module(reader)


def stage_file(path, edit=None):
    stage = Usd.Stage.CreateNew(str(path)); UsdGeom.SetStageUpAxis(stage, 'Y'); UsdGeom.SetStageMetersPerUnit(stage, 1)
    top = UsdGeom.Xform.Define(stage,'/World'); stage.SetDefaultPrim(top.GetPrim())
    mesh = UsdGeom.Mesh.Define(stage,'/World/Mesh')
    mesh.CreatePointsAttr([(0,0,0),(1,0,0),(1,1,0),(0,1,0)])
    mesh.CreateFaceVertexCountsAttr([3,3]);mesh.CreateFaceVertexIndicesAttr([0,1,2,0,2,3]);mesh.CreateSubdivisionSchemeAttr('none')
    if edit: edit(stage,mesh)
    stage.GetRootLayer().Save();return stage


def extract(path, target):
    target.mkdir();reader.convert(path,target);return json.loads((target/'mesh-import.json').read_text())


def binary(target, part):
    data = array.array('f');data.frombytes((target/part['file']).read_bytes()[:part['vertices']*12]);return data.tolist()


def rejected(work,name,edit):
    path=work/(name+'.usda');stage=stage_file(path,edit)
    try: extract(path,work/(name+'-out'))
    except (ValueError, RuntimeError): return
    raise AssertionError('Unsupported scene accepted: '+name)


def surface(stage,mesh):
    mat=UsdShade.Material.Define(stage,'/World/Mat');shader=UsdShade.Shader.Define(stage,'/World/Mat/P')
    shader.CreateIdAttr('UsdPreviewSurface');shader.CreateOutput('surface',Sdf.ValueTypeNames.Token)
    mat.CreateSurfaceOutput().ConnectToSource(shader.ConnectableAPI(),'surface');UsdShade.MaterialBindingAPI.Apply(mesh.GetPrim()).Bind(mat)
    return mat,shader


with tempfile.TemporaryDirectory(prefix='aholo3d-mesh-usd-import-') as temporary:
    work=Path(temporary).resolve();env=os.environ.copy();env['AHOLO3D_USD_PYTHON']=sys.executable
    exported=work/'exported'
    subprocess.run(['dotnet','run','--project',str(root/'tools/Aholo3D.Reference'),'-c','Release','--no-build','--','--mesh-fixtures',str(exported)],check=True,env=env)
    for fmt in ('usda','usd','usdc','usdz'):
        path=exported/('meshes.'+fmt);out=work/('read-'+fmt);data=extract(path,out);parts=data['parts'];assert len(parts)==3
        assert data['sourceSha256']==hashlib.sha256(path.read_bytes()).hexdigest()
        assert data['upAxis']=='Z' and data['metersPerUnit']==.001
        assert parts[0]['transform'][9:]==[1e12+.125,-2e12+.25,3e12+.5]
        assert parts[0]['texture']['useAlpha'] and not parts[1]['texture']['useAlpha']
        assert abs(parts[1]['color'][3]-.6)<1e-6 and parts[2]['texture'] is None
        assert parts[0]['texture']['file']==parts[1]['texture']['file'] and parts[0]['doubleSided']
        assert binary(out,parts[0])==[0,0,0,.125,0,0,0,.25,.5]
        # The public C# reader independently validates protocol/bitmap bytes and physical transforms.
        subprocess.run(['dotnet','run','--project',str(root/'tools/Aholo3D.Reference'),'-c','Release','--no-build','--','--mesh-import-fixture',str(path)],check=True,env=env)

    def seams(stage,mesh):
        mesh.CreateNormalsAttr([(0,0,1),(0,1,1)]);mesh.SetNormalsInterpolation('uniform')
        st=UsdGeom.PrimvarsAPI(mesh).CreatePrimvar('st',Sdf.ValueTypeNames.TexCoord2fArray,'faceVarying')
        st.Set([(0,0),(1,0),(1,1),(0.5,0),(0.5,1),(0,1)]);st.SetIndices([0,1,2,3,4,5])
        mesh.CreateOrientationAttr('leftHanded');mesh.AddScaleOp().Set(Gf.Vec3f(-2,3,4));mesh.AddTranslateOp(UsdGeom.XformOp.PrecisionDouble).Set((1e9+.125,2,3))
    p=work/'seams.usda';stage=stage_file(p,seams);data=extract(p,work/'seams-out');part=data['parts'][0]
    assert part['vertices']==6 and part['normals'] and part['uv'] and part['leftHanded']
    assert part['transform'][0]==-2 and not part['doubleSided']

    for interp,length in [('constant',1),('vertex',4),('varying',4),('uniform',2),('faceVarying',6)]:
        def normals(stage,mesh): mesh.CreateNormalsAttr([(0,0,2)]*length);mesh.SetNormalsInterpolation(interp)
        path=work/(interp+'.usda');stage=stage_file(path,normals);assert extract(path,work/(interp+'-out'))['parts'][0]['normals']
    def quad(stage,mesh): mesh.CreateFaceVertexCountsAttr([4]);mesh.CreateFaceVertexIndicesAttr([0,1,2,3])
    path=work/'quad.usda';stage=stage_file(path,quad);part=extract(path,work/'quad-out')['parts'][0];assert part['faces']==1 and part['indices']==4

    # An absolute bitmap reference is captured and localized without depending on the original after parsing.
    absolute_source=exported/'absolute.usda';shutil.copyfile(exported/'meshes.usda',absolute_source)
    layer=Sdf.Layer.FindOrOpen(str(absolute_source))
    UsdUtils.ModifyAssetPaths(layer,lambda value:str((exported/value).resolve()) if value else value)
    layer.Save();assert len(extract(absolute_source,work/'absolute-out')['parts'])==3
    # External sublayers are captured and localized.

    child=work/'child.usda';stage=stage_file(child)
    parent=work/'parent.usda';layer=Sdf.Layer.CreateNew(str(parent));layer.subLayerPaths=[str(child)];layer.Save()
    assert len(extract(parent,work/'parent-out')['parts'])==1
    # After capture, changing/restoring an original file cannot substitute the parsed content.
    frozen_dir=work/'frozen-out';frozen_dir.mkdir();from pxr import Ar
    captured,sha,hashes=reader.freeze_source(child,frozen_dir,Ar,Sdf,UsdUtils);original=child.read_bytes()
    changed=original.replace(b'(1, 0, 0)',b'(9, 0, 0)');assert changed!=original;child.write_bytes(changed)
    frozen_stage=Usd.Stage.Open(str(captured));assert UsdGeom.Mesh(frozen_stage.GetPrimAtPath('/World/Mesh')).GetPointsAttr().Get()[1][0]==1
    child.write_bytes(original);assert sha==hashlib.sha256(original).hexdigest()

    def instance(stage,mesh):
        x=UsdGeom.Xform.Define(stage,'/Copy');x.GetPrim().GetReferences().AddInternalReference('/World');x.GetPrim().SetInstanceable(True)
    path=work/'instance.usda';stage=stage_file(path,instance);assert len(extract(path,work/'instance-out')['parts'])==2

    rejected(work,'default-subdivision',lambda s,m:m.GetSubdivisionSchemeAttr().Clear())
    rejected(work,'animated',lambda s,m:m.GetPointsAttr().Set([(0,0,0)]*4,1))
    rejected(work,'hidden',lambda s,m:m.CreateVisibilityAttr('invisible'))
    rejected(work,'guide',lambda s,m:m.CreatePurposeAttr('guide'))
    rejected(work,'hole',lambda s,m:m.CreateHoleIndicesAttr([0]))
    rejected(work,'polygon',lambda s,m:(m.CreateFaceVertexCountsAttr([6]),m.CreateFaceVertexIndicesAttr([0,1,2,3,0,1])))
    rejected(work,'bad-index',lambda s,m:m.CreateFaceVertexIndicesAttr([0,1,99,0,2,3]))
    rejected(work,'singular',lambda s,m:m.AddScaleOp().Set((1,0,1)))
    rejected(work,'normal-count',lambda s,m:m.CreateNormalsAttr([(0,0,1)]))
    rejected(work,'non-mesh',lambda s,m:UsdGeom.Sphere.Define(s,'/Sphere'))
    rejected(work,'subset',lambda s,m:UsdGeom.Subset.CreateGeomSubset(m,'part','face',[0]))
    rejected(work,'point-instancer',lambda s,m:UsdGeom.PointInstancer.Define(s,'/Instances'))
    rejected(work,'emissive',lambda s,m:surface(s,m)[1].CreateInput('emissiveColor',Sdf.ValueTypeNames.Color3f).Set((1,0,0)))
    rejected(work,'context-surface',lambda s,m:surface(s,m)[0].CreateSurfaceOutput('ri').ConnectToSource(surface(s,m)[1].ConnectableAPI(),'surface'))
    rejected(work,'missing-reference',lambda s,m:m.GetPrim().GetReferences().AddReference('absent.usd'))
    print('Mesh USD import verified: four formats, immutable dependency snapshots, materials/alpha, seams, five interpolations, quads, instances and 15 strict rejection cases. No native Rhino acceptance.')
