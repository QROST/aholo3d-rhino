#!/usr/bin/env python3
"""Real OpenUSD polygon/material-subset regression checks; no Rhino or service calls."""
import array
import base64
from collections import Counter
import importlib.util
import json
import math
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from pxr import Gf, Sdf, Usd, UsdGeom, UsdShade, UsdUtils, UsdVol

ROOT=Path(__file__).resolve().parents[1]
def module(name):
    spec=importlib.util.spec_from_file_location(name,ROOT/'scripts'/f'{name}.py')
    result=importlib.util.module_from_spec(spec);spec.loader.exec_module(result);return result
reader=module('usd_mesh_reader');bridge=module('usd_scene_bridge')
PNG=base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAF0lEQVR4nAXBAQEAAACCIKb33EAkQxUHQdUHepwfNGcAAAAASUVORK5CYII=')
CONCAVE=[(0,0),(3,0),(3,3),(1.5,1),(0,3)]
POINTS=[(10,0,0),(11,0,0),(10,1,0)]+[(x,y,0) for x,y in CONCAVE]+[(5,0,0),(6,0,0),(6,1,0),(5,1,0)]


def twice_area(points):
    return sum(a[0]*b[1]-a[1]*b[0] for a,b in zip(points,points[1:]+points[:1]))


def oracle(points,area):
    xyz=[(x,y,0) for x,y in points];triangles=reader.triangulate_polygon(xyz)
    assert len(triangles)==len(points)-2
    assert {i for t in triangles for i in t}==set(range(len(points)))
    signed=[twice_area([points[i] for i in t])/2 for t in triangles]
    assert all(a*area>0 for a in signed) and math.isclose(sum(signed),area,rel_tol=1e-12,abs_tol=0)
    edges=Counter(tuple(sorted((a,b))) for t in triangles for a,b in zip(t,t[1:]+t[:1]))
    boundary={tuple(sorted((i,(i+1)%len(points)))) for i in range(len(points))}
    assert all(edges[e]==1 for e in boundary)
    assert all(n==(1 if e in boundary else 2) for e,n in edges.items())
    # A ray-crossing oracle independently checks every triangle centroid lies inside.
    for t in triangles:
        x=sum(points[i][0] for i in t)/3;y=sum(points[i][1] for i in t)/3;inside=False
        for a,b in zip(points,points[1:]+points[:1]):
            if (a[1]>y)!=(b[1]>y) and x<(b[0]-a[0])*(y-a[1])/(b[1]-a[1])+a[0]:inside=not inside
        assert inside
    return triangles


assert oracle(CONCAVE,6)==[(1,2,3),(0,1,3),(0,3,4)]
oracle(list(reversed(CONCAVE)),-6)
oracle([(0,0),(1,0),(2,0),(2,1),(1,1),(0,1)],2)
oracle([(0,0),(4,0),(4,4),(3,4),(3,1),(1,1),(1,4),(0,4)],10)
# Shift area computation near the origin: the triangulator itself still sees the large coordinates.
large=[(x*256+1e9,y*256+1e9,0) for x,y in CONCAVE]
assert reader.triangulate_polygon(large)==reader.triangulate_polygon([(x,y,0) for x,y in CONCAVE])
for axis in range(3):
    plane=[tuple((x,y,0)[(i+axis)%3] for i in range(3)) for x,y in CONCAVE]
    assert reader.triangulate_polygon(plane)==[(1,2,3),(0,1,3),(0,3,4)]
for scale in (1e-20,1e20):oracle([(x*scale,y*scale) for x,y in CONCAVE],6*scale*scale)


def material(stage,name,color):
    mat=UsdShade.Material.Define(stage,'/World/'+name);shader=UsdShade.Shader.Define(stage,mat.GetPath().AppendChild('Surface'))
    shader.CreateIdAttr('UsdPreviewSurface');shader.CreateInput('diffuseColor',Sdf.ValueTypeNames.Color3f).Set(color)
    shader.CreateOutput('surface',Sdf.ValueTypeNames.Token);mat.CreateSurfaceOutput().ConnectToSource(shader.ConnectableAPI(),'surface')
    return mat,shader


def create(path):
    stage=Usd.Stage.CreateNew(str(path));UsdGeom.SetStageUpAxis(stage,'Z');UsdGeom.SetStageMetersPerUnit(stage,.001)
    root=UsdGeom.Xform.Define(stage,'/World');stage.SetDefaultPrim(root.GetPrim())
    mesh=UsdGeom.Mesh.Define(stage,'/World/Mesh');mesh.CreateSubdivisionSchemeAttr('none');mesh.CreatePointsAttr(POINTS)
    mesh.CreateFaceVertexCountsAttr([3,5,4]);mesh.CreateFaceVertexIndicesAttr(list(range(12)))
    mesh.CreateNormalsAttr([(0,0,1),(0,0,2),(0,0,3)]);mesh.SetNormalsInterpolation('uniform')
    st=UsdGeom.PrimvarsAPI(mesh).CreatePrimvar('st',Sdf.ValueTypeNames.TexCoord2fArray,'faceVarying')
    st.Set([(i/16,i/32) for i in range(12)]);st.SetIndices(list(reversed(range(12))))
    mesh.CreateOrientationAttr('leftHanded');mesh.CreateDoubleSidedAttr(True)
    mesh.AddTranslateOp(UsdGeom.XformOp.PrecisionDouble).Set((1e12+.125,-2e12+.25,3e12+.5));mesh.AddScaleOp().Set((-2,3,4))
    green,_=material(stage,'Green',(0,1,0));blue,_=material(stage,'Blue',(0,0,1));red,shader=material(stage,'Red',(1,0,0))
    binding=UsdShade.MaterialBindingAPI.Apply(mesh.GetPrim());binding.Bind(green)
    blue_subset=binding.CreateMaterialBindSubset('BlueFaces',[1]);red_subset=binding.CreateMaterialBindSubset('RedFaces',[2])
    UsdShade.MaterialBindingAPI.Apply(blue_subset.GetPrim()).Bind(blue);UsdShade.MaterialBindingAPI.Apply(red_subset.GetPrim()).Bind(red)
    # A bitmap in one material part must retain original corner UVs and its exact encoded bytes.
    (path.parent/'texture.png').write_bytes(PNG)
    tex=UsdShade.Shader.Define(stage,'/World/Red/Texture');tex.CreateIdAttr('UsdUVTexture')
    tex.CreateInput('file',Sdf.ValueTypeNames.Asset).Set(Sdf.AssetPath('texture.png'))
    tex.CreateInput('sourceColorSpace',Sdf.ValueTypeNames.Token).Set('sRGB')
    for axis in ('S','T'):tex.CreateInput('wrap'+axis,Sdf.ValueTypeNames.Token).Set('repeat')
    uv=UsdShade.Shader.Define(stage,'/World/Red/UV');uv.CreateIdAttr('UsdPrimvarReader_float2')
    uv.CreateInput('varname',Sdf.ValueTypeNames.Token).Set('st');uv.CreateOutput('result',Sdf.ValueTypeNames.Float2)
    tex.CreateInput('st',Sdf.ValueTypeNames.Float2).ConnectToSource(uv.ConnectableAPI(),'result')
    tex.CreateOutput('rgb',Sdf.ValueTypeNames.Float3);tex.CreateOutput('a',Sdf.ValueTypeNames.Float)
    shader.GetInput('diffuseColor').ConnectToSource(tex.ConnectableAPI(),'rgb')
    shader.CreateInput('opacity',Sdf.ValueTypeNames.Float).ConnectToSource(tex.ConnectableAPI(),'a')
    return stage,mesh,binding


def extract(path,out,mixed=False):
    out.mkdir();(bridge if mixed else reader).convert(path,out)
    return json.loads((out/'mesh-import.json').read_text())['parts']


def decode(out,part):
    data=(out/part['file']).read_bytes();offset=0;result=[]
    for kind,count in [('f',part['vertices']*3),('i',part['faces']),('i',part['indices']),('f',part['vertices']*3 if part['normals'] else 0),('f',part['vertices']*2 if part['uv'] else 0)]:
        a=array.array(kind);a.frombytes(data[offset:offset+4*count]);offset+=4*count
        if sys.byteorder!='little':a.byteswap()
        result.append(a.tolist())
    assert offset==len(data);return result


def verify(out,parts):
    assert [p['primPath'] for p in parts]==['/World/Mesh','/World/Mesh/BlueFaces','/World/Mesh/RedFaces']
    assert all(p['sourcePrimPath']=='/World/Mesh' for p in parts)
    for group,p in enumerate(parts):
        assert p['transform']==[-2,0,0,0,3,0,0,0,4,1e12+.125,-2e12+.25,3e12+.5]
        assert p['leftHanded'] and p['doubleSided']
        positions,counts,indices,normals,uv=decode(out,p)
        assert counts==([3],[3,3,3],[4])[group]
        for i in range(p['vertices']):
            point=tuple(positions[3*i:3*i+3]);source=POINTS.index(point)
            assert source in (range(3),range(3,8),range(8,12))[group]
            assert normals[3*i:3*i+3]==[0,0,group+1]
            assert uv[2*i:2*i+2]==[(11-source)/16,(11-source)/32]
        if group==1:
            assert sum(twice_area([positions[3*v:3*v+2] for v in indices[i:i+3]])/2 for i in range(0,len(indices),3))==6
    assert parts[0]['color']==[0,1,0,1] and parts[1]['color']==[0,0,1,1]
    assert parts[2]['texture']['useAlpha'] and (out/parts[2]['texture']['file']).read_bytes()==PNG


def gaussian(stage,name):
    gs=UsdVol.ParticleField3DGaussianSplat.Define(stage,'/World/'+name)
    gs.CreatePositionsAttr([(0,0,0)]);gs.CreateScalesAttr([(1,1,1)]);gs.CreateOrientationsAttr([Gf.Quatf(1,0,0,0)])
    gs.CreateOpacitiesAttr([.5]);gs.CreateRadianceSphericalHarmonicsDegreeAttr(0);gs.CreateRadianceSphericalHarmonicsCoefficientsAttr([(0,0,0)])


with tempfile.TemporaryDirectory(prefix='aholo-topology-') as temporary:
    work=Path(temporary);source=work/'topology.usda';stage,mesh,binding=create(source);stage.GetRootLayer().Save()
    for fmt in ('usda','usd','usdc','usdz'):
        path=work/('topology.'+fmt)
        if fmt=='usdz':assert UsdUtils.CreateNewUsdzPackage(Sdf.AssetPath(str(source)),str(path))
        elif fmt!='usda':assert stage.GetRootLayer().Export(str(path))
        out=work/('read-'+fmt);verify(out,extract(path,out))
        subprocess.run(['dotnet',str(ROOT/'tools/Aholo.Reference/bin/Release/net8.0/Aholo.Reference.dll'),'--mesh-topology-fixture',str(path)],check=True,env=dict(os.environ,AHOLO_USD_PYTHON=sys.executable))

    demo=ROOT/'fixtures/mesh-subsets.usda';demo_out=work/'packaged-demo';demo_parts=extract(demo,demo_out)
    assert [p['faces'] for p in demo_parts]==[1,3,1]
    assert [p['color'] for p in demo_parts]==[[0,1,0,1],[0,0,1,1],[1,0,0,1]]
    assert all(p['transform']==[1,0,0,0,1,0,0,0,1,0,0,0] for p in demo_parts)
    # Inherited stronger bindings override subset opinions; preview wins over full.
    for purpose in ('preview','full','allPurpose'):
        path=work/(purpose+'.usda');s,m,b=create(path);mat=UsdShade.Material(s.GetPrimAtPath('/World/Green'))
        parent=UsdShade.MaterialBindingAPI.Apply(s.GetPrimAtPath('/World'))
        parent.Bind(mat,bindingStrength='strongerThanDescendants',materialPurpose='' if purpose=='allPurpose' else purpose)
        if purpose=='full':
            # No allPurpose/preview target anywhere: exercise full-only fallback.
            b.UnbindAllBindings()
            for subset in b.GetMaterialBindSubsets():UsdShade.MaterialBindingAPI(subset.GetPrim()).UnbindAllBindings()
        s.GetRootLayer().Save();out=work/(purpose+'-out');parts=extract(path,out)
        assert all(p['color']==[0,1,0,1] and p['texture'] is None for p in parts)
    path=work/'preview-specific.usda';s,m,b=create(path)
    blue=UsdShade.Material(s.GetPrimAtPath('/World/Blue'));b.Bind(blue,materialPurpose='preview')
    b.Bind(UsdShade.Material(s.GetPrimAtPath('/World/Green')),materialPurpose='full');s.GetRootLayer().Save()
    parts=extract(path,work/'preview-specific-out');assert all(p['color']==[0,0,1,1] for p in parts)

    path=work/'partition.usda';s,m,b=create(path);subset=b.CreateMaterialBindSubset('Residual',[0]);b.SetMaterialBindSubsetsFamilyType('partition')
    s.GetRootLayer().Save();parts=extract(path,work/'partition-out');assert len(parts)==3 and all(p['primPath']!='/World/Mesh' for p in parts)
    assert next(p for p in parts if p['primPath'].endswith('/Residual'))['color']==[0,1,0,1]
    path=work/'empty.usda';s,m,b=create(path);b.CreateMaterialBindSubset('Empty',[]);s.GetRootLayer().Save()
    assert len(extract(path,work/'empty-out'))==3
    path=work/'instances.usda';s,m,b=create(path);copy=UsdGeom.Xform.Define(s,'/Copy').GetPrim()
    copy.GetReferences().AddInternalReference('/World');copy.SetInstanceable(True)
    UsdGeom.Xformable(copy).AddTranslateOp().Set((23,47,61));s.GetRootLayer().Save()
    parts=extract(path,work/'instances-out');assert len(parts)==6 and len({p['primPath'] for p in parts})==6
    assert all(p['sourcePrimPath']==('/Copy/Mesh' if p['primPath'].startswith('/Copy/') else '/World/Mesh') for p in parts)
    out=work/'instances-out'
    for original in [p for p in parts if p['primPath'].startswith('/World/')]:
        other=next(p for p in parts if p['primPath']==original['primPath'].replace('/World/','/Copy/',1))
        assert decode(out,original)==decode(out,other)
        assert all(original[k]==other[k] for k in ('color','metallic','roughness','texture','leftHanded','doubleSided'))
        assert other['transform'][:9]==original['transform'][:9]
        assert other['transform'][9:]==[x+d for x,d in zip(original['transform'][9:],(23,47,61))]
    # Shared source vertices have distinct indexed corner values across noncontiguous material faces.
    path=work/'shared-corners.usda';s,m,b=create(path)
    shared=[(0,0,0),(3,0,0),(3,3,0),(1.5,1,0),(0,3,0),(3,1,0),(0,1,0)]
    source_indices=[0,1,2,0,1,2,3,4,0,1,5,6]
    m.GetPointsAttr().Set(shared);m.GetFaceVertexIndicesAttr().Set(source_indices);m.GetNormalsAttr().Clear()
    normals=UsdGeom.PrimvarsAPI(m).CreatePrimvar('normals',Sdf.ValueTypeNames.Normal3fArray,'faceVarying')
    normals.Set([(i+1,1,1) for i in range(12)]);normals.SetIndices(list(reversed(range(12))))
    subsets={str(x.GetPrim().GetName()):x for x in b.GetMaterialBindSubsets()}
    subsets['BlueFaces'].GetIndicesAttr().Set([0,2]);subsets['RedFaces'].GetIndicesAttr().Set([1]);s.GetRootLayer().Save()
    out=work/'shared-corners-out';parts=extract(path,out);assert len(parts)==2
    for p in parts:
        positions,counts,indices,normals,uv=decode(out,p);corners=[]
        expected={0,1,2,8,9,10,11} if p['primPath'].endswith('/BlueFaces') else {3,4,5,6,7}
        assert counts==([3,4] if len(expected)==7 else [3,3,3])
        for i in range(p['vertices']):
            corner=12-int(normals[3*i]);corners.append(corner)
            assert corner in expected and normals[3*i+1:3*i+3]==[1,1]
            assert positions[3*i:3*i+3]==list(shared[source_indices[corner]])
            assert uv[2*i:2*i+2]==[(11-corner)/16,(11-corner)/32]
        assert set(corners)==expected and len(corners)==len(expected)
    # Missing normals are faceted on each original face, including all derived n-gon triangles.
    path=work/'no-normals.usda';s,m,b=create(path);m.GetNormalsAttr().Clear();s.GetRootLayer().Save()
    out=work/'no-normals-out'
    for p in extract(path,out):
        assert p['normals'] and p['generatedNormals']
        assert decode(out,p)[3]==[0,0,-1]*p['vertices']

    polygons={
      'crossing':[(0,0,0),(2,2,0),(0,2,0),(2,0,0),(3,0,0),(3,-1,0)],
      'nonplanar':[(x,y,.1 if i==3 else 0) for i,(x,y) in enumerate(CONCAVE)],
      'collinear':[(i,0,0) for i in range(5)],
      'repeat':[(0,0,0),(3,0,0),(3,3,0),(0,3,0),(0,0,0)],
      'backtrack':[(0,0,0),(2,0,0),(1,0,0),(2,2,0),(0,2,0)],
      'touch':[(0,0,0),(2,0,0),(2,2,0),(1,0,0),(0,2,0)],
    }
    defects={
      'overlap':lambda s,m,b:b.GetMaterialBindSubsets()[1].GetIndicesAttr().Set([1]),
      'duplicate':lambda s,m,b:b.GetMaterialBindSubsets()[0].GetIndicesAttr().Set([1,1]),
      'negative':lambda s,m,b:b.GetMaterialBindSubsets()[0].GetIndicesAttr().Set([-1]),
      'out-of-range':lambda s,m,b:b.GetMaterialBindSubsets()[0].GetIndicesAttr().Set([3]),
      'partition-gap':lambda s,m,b:b.SetMaterialBindSubsetsFamilyType('partition'),
      'unrestricted':lambda s,m,b:UsdGeom.Subset.SetFamilyType(m,'materialBind','unrestricted'),
      'wrong-family':lambda s,m,b:b.GetMaterialBindSubsets()[0].GetFamilyNameAttr().Set('other'),
      'non-face':lambda s,m,b:b.GetMaterialBindSubsets()[0].GetElementTypeAttr().Set('point'),
      'animated':lambda s,m,b:b.GetMaterialBindSubsets()[0].GetIndicesAttr().Set([0],1),
      'orphan':lambda s,m,b:UsdGeom.Subset.Define(s,'/Orphan'),
      'unresolved-material':lambda s,m,b:b.GetMaterialBindSubsets()[0].GetPrim().GetRelationship('material:binding').SetTargets(['/Missing']),
    }
    for name,change in defects.items():
        path=work/(name+'.usda');s,m,b=create(path);change(s,m,b);s.GetRootLayer().Save();out=work/(name+'-out');out.mkdir()
        try:reader.convert(path,out)
        except (ValueError,RuntimeError):pass
        else:raise AssertionError(name+' accepted')
        assert not (out/'mesh-import.json').exists()
    for name,points in polygons.items():
        path=work/(name+'.usda');s,m,b=create(path)
        for child in list(m.GetPrim().GetChildren()):s.RemovePrim(child.GetPath())
        m.GetPointsAttr().Set(points);m.GetFaceVertexCountsAttr().Set([len(points)]);m.GetFaceVertexIndicesAttr().Set(list(range(len(points))))
        m.GetNormalsAttr().Clear();UsdGeom.PrimvarsAPI(m).RemovePrimvar('st');s.GetRootLayer().Save();out=work/(name+'-out');out.mkdir()
        try:reader.convert(path,out)
        except (ValueError,RuntimeError) as error:assert 'polygon' in str(error).lower() or 'boundary' in str(error).lower(),str(error)
        else:raise AssertionError(name+' accepted')
        assert not (out/'mesh-import.json').exists()

    # Mixed scene failures after either data type may leave private binaries but publish no manifests.
    for order in (['Mesh','Gaussian'],['Gaussian','Mesh']):
        label=order[0];path=work/('mixed-'+label+'.usda');s,m,b=create(path);gaussian(s,'Gaussian')
        s.GetPrimAtPath('/World').SetChildrenReorder(order);s.GetRootLayer().Save();out=work/('mixed-'+label+'-out');verify(out,extract(path,out,True))
        assert json.loads((out/'scene-import.json').read_text())['gaussians']
        b.GetMaterialBindSubsets()[0].GetIndicesAttr().Set([1,1]);s.GetRootLayer().Save();bad=work/('mixed-'+label+'-bad');bad.mkdir()
        try:bridge.convert(path,bad)
        except (ValueError,RuntimeError):pass
        else:raise AssertionError('mixed material error accepted')
        assert (bad/'part-000000.ply').exists()==(label=='Gaussian')
        assert all(not (bad/name).exists() for name in ('mesh-import.json','manifest.json','scene-import.json'))
print('USD topology verified: four formats through public C# reader; concave/collinear/reversed/large-scale polygon oracles; indexed UVs, normals, texture bytes, subset provenance/partition/inheritance/purposes/instances; 17 strict rejection cases and mixed-scene atomicity in both traversal orders. Native Rhino acceptance remains separate.')
