#!/usr/bin/env python3
"""Verify the production mixed writer with pinned OpenUSD; no Rhino or service calls."""
import argparse
import hashlib
import math
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
import zipfile
from pxr import Ar, Gf, Sdf, Usd, UsdGeom, UsdShade, UsdUtils, UsdVol

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--reference',type=Path,default=ROOT/'tools/Aholo.Reference/bin/Release/net8.0/Aholo.Reference.dll')
args=parser.parse_args()
NAMES=['Duplicate 用户','Duplicate 用户','','a / not a path','Mixed degree']

def close(actual,expected,absolute=1e-12):
    assert len(actual)==len(expected)
    for a,b in zip(actual,expected):
        assert math.isfinite(a) and abs(a-b)<=max(absolute,8*math.ulp(b)), (actual,expected)


def source_center(index):
    return (1e9+.01+index*.013,-2e8+.04-index*.027,3e7+.05+index*.031)


def coefficient(index):
    return (index//3)*.125-.5+(index%3)*.0625


def verify_gs(stage):
    particles=[UsdVol.ParticleField3DGaussianSplat(p) for p in stage.Traverse() if p.IsA(UsdVol.ParticleField3DGaussianSplat)]
    assert len(particles)==5
    for part,splat in enumerate(particles):
        prim=splat.GetPrim();assert str(prim.GetPath()).startswith('/Scene/Gaussians/')
        assert prim.GetCustomDataByKey('gaussianSplatting:sourceName')==NAMES[part]
        assert prim.GetCustomDataByKey('gaussianSplatting:antialiased')==(part<4 and part%2==0)
        count=2 if part<4 else 4;degree=min(part,3)
        assert splat.GetRadianceSphericalHarmonicsDegreeAttr().Get()==degree
        positions=splat.GetPositionsAttr().Get();assert len(positions)==count
        scales=splat.GetScalesAttr().Get();orientations=splat.GetOrientationsAttr().Get();opacities=splat.GetOpacitiesAttr().Get()
        assert len(scales)==len(orientations)==len(opacities)==count
        sh=splat.GetRadianceSphericalHarmonicsCoefficientsAttr().Get();assert len(sh)==count*(degree+1)**2
        matrix=UsdGeom.XformCache().GetLocalToWorldTransform(prim)
        linear=((1,0,0),(0,1,0),(0,0,1)) if part==4 else ((-2,.25,0),(.5,3,.75),(0,-.125,4))
        for row in range(3):close(tuple(matrix[row])[:3],tuple(linear[col][row] for col in range(3)))
        for i in range(count):
            center=source_center(i);translation=(0,0,0) if part==4 else (10,-5,20)
            expected=[sum(linear[row][col]*center[col] for col in range(3))+translation[row] for row in range(3)]
            close(tuple(matrix.Transform(Gf.Vec3d(*positions[i]))),expected,2e-6)
            close(tuple(scales[i]),(2e-5,7e-6,3e-5),2e-12)
            close((orientations[i].GetReal(),*orientations[i].GetImaginary()),(.5,.5,-.5,.5))
            assert opacities[i]==(0 if i==0 else 1)
            original_degree=part if part<4 else i;original_count=(original_degree+1)**2
            for k in range((degree+1)**2):
                expected=tuple(coefficient(ch) for ch in range(3)) if k==0 else tuple(coefficient(k*3+ch) for ch in range(3)) if k<original_count else (0,0,0)
                assert tuple(sh[i*(degree+1)**2+k])==expected, (part,i,k,tuple(sh[i*(degree+1)**2+k]),expected)


def convert(point,axis,units):
    x,y,z=point
    return tuple(v*(.001/units) for v in ((x,z,-y) if axis=='Y' else (x,y,z)))


def verify_mesh(stage,axis,units):
    meshes=[UsdGeom.Mesh(p) for p in stage.Traverse() if p.IsA(UsdGeom.Mesh)]
    assert len(meshes)==3
    root=UsdGeom.Xformable(stage.GetPrimAtPath('/Scene/Meshes')).GetLocalTransformation()
    close(root.Transform(Gf.Vec3d(304.8,609.6,914.4)),convert((304.8,609.6,914.4),axis,units))
    assert root.GetDeterminant()>0
    for index,mesh in enumerate(meshes):
        assert str(mesh.GetPath()).startswith('/Scene/Meshes/')
        assert list(mesh.GetFaceVertexCountsAttr().Get())==[3] and list(mesh.GetFaceVertexIndicesAttr().Get())==[0,1,2]
        assert mesh.GetSubdivisionSchemeAttr().Get()=='none' and mesh.GetOrientationAttr().Get()=='rightHanded'
        points=mesh.GetPointsAttr().Get();assert list(map(tuple,points))==[(0,0,0),(.125,0,0),(0,.25,.5)]
        origin=[(1e12+.125,-2e12+.25,3e12+.5),(0,0,10),(304.8,609.6,914.4)][index]
        matrix=UsdGeom.XformCache().GetLocalToWorldTransform(mesh.GetPrim())
        for p in points:close(matrix.Transform(Gf.Vec3d(*p)),convert(tuple(p[i]+origin[i] for i in range(3)),axis,units))
        if index<2:
            assert mesh.GetNormalsInterpolation()=='vertex'
            normal=mesh.GetNormalsAttr().Get()[0]
            # Uniform root scale + proper rotation: directions and winding must agree.
            normal=root.TransformDir(Gf.Vec3d(*normal)).GetNormalized()
            edge1=root.TransformDir(Gf.Vec3d(*(points[1]-points[0])))
            edge2=root.TransformDir(Gf.Vec3d(*(points[2]-points[0])))
            assert abs(Gf.Dot(normal,edge1))<1e-7 and abs(Gf.Dot(normal,edge2))<1e-7
            assert Gf.Dot(Gf.Cross(edge1,edge2),normal)>0
            expected=(0,1,2) if axis=='Y' else (0,-2,1)
            close(normal,Gf.Vec3d(*expected).GetNormalized(),1e-7)
            uv=UsdGeom.PrimvarsAPI(mesh).GetPrimvar('st');assert uv.GetInterpolation()=='vertex'
            assert list(map(tuple,uv.Get()))==[(0,0),(1,0),(0,1)]
        material,_=UsdShade.MaterialBindingAPI(mesh.GetPrim()).ComputeBoundMaterial();assert material
        assert str(material.GetPath()).startswith('/Scene/Meshes/Materials/')
        surface=UsdShade.Shader(stage.GetPrimAtPath(str(material.GetPath())+'/PreviewSurface'))
        assert surface.GetIdAttr().Get()=='UsdPreviewSurface'
        assert abs(surface.GetInput('metallic').Get()-.2)<1e-6 and abs(surface.GetInput('roughness').Get()-.4)<1e-6
        if index<2:
            bitmap=UsdShade.Shader(stage.GetPrimAtPath(str(material.GetPath())+'/Bitmap'))
            assert bitmap.GetInput('sourceColorSpace').Get()=='sRGB'
            assert tuple(bitmap.GetInput('scale').Get())[:3]==(.5,.25,.125)
            assert abs(bitmap.GetInput('scale').Get()[3]-(.6 if index==0 else 0))<1e-6
            assert abs(bitmap.GetInput('bias').Get()[3]-(0 if index==0 else .6))<1e-6
            assert surface.GetInput('diffuseColor').GetConnectedSource()[1]=='rgb'
            assert surface.GetInput('opacity').GetConnectedSource()[1]=='a'
            assert bitmap.GetInput('wrapT').Get()=='repeat'
            assert bitmap.GetInput('wrapS').Get()==('clamp' if index==0 else 'repeat')
            assert bitmap.GetPrim().GetCustomDataByKey('sourceSampler:minFilter')==(9728 if index==0 else 9729)
            image=bitmap.GetInput('file').Get();assert image.resolvedPath and not image.path.startswith('/')
            asset=Ar.GetResolver().OpenAsset(Ar.ResolvedPath(image.resolvedPath))
            assert hashlib.sha256(asset.GetBuffer()).hexdigest()==Path(image.path).stem=='ce261ebea422e4b82e9aa5f2e86a4b1f90ebae7d267d16ebf0cb6002604117c6'
        else:
            assert not mesh.GetNormalsAttr().HasAuthoredValueOpinion()
            assert not UsdGeom.PrimvarsAPI(mesh).GetPrimvar('st')
            assert tuple(surface.GetInput('diffuseColor').Get())==(.5,.25,.125)
            assert abs(surface.GetInput('opacity').Get()-.6)<1e-6


def verify_bundle(path,moved,has_mesh):
    layers,images,missing=UsdUtils.ComputeAllDependencies(Sdf.AssetPath(str(path)))
    assert not missing
    assert all(not layer.subLayerPaths for layer in layers)
    if path.suffix=='.usdz':
        with zipfile.ZipFile(path) as archive:
            assert archive.testzip() is None and archive.namelist()[0].endswith('.usdc')
            assert len([n for n in archive.namelist() if n.endswith('.png')])==int(has_mesh)
            with path.open('rb') as raw:
                for info in archive.infolist():
                    assert info.compress_type==zipfile.ZIP_STORED
                    raw.seek(info.header_offset+26);name_length,extra_length=struct.unpack('<HH',raw.read(4))
                    assert (info.header_offset+30+name_length+extra_length)%64==0
        for image in images:
            assert Ar.IsPackageRelativePath(str(image))
            assert Path(Ar.SplitPackageRelativePathOuter(str(image))[0]).resolve()==path.resolve()
    else:
        assert len(images)==int(has_mesh)
        assert all(Path(str(image)).resolve().is_relative_to(moved.resolve()) for image in images)
        assert path.read_bytes().startswith(b'#usda 1.') if path.suffix=='.usda' else path.read_bytes().startswith(b'PXR-USDC')


with tempfile.TemporaryDirectory(prefix='aholo-scene-usd-check-') as temporary:
    work=Path(temporary);original=work/'original';moved=work/'relocated'
    subprocess.run(['dotnet',str(args.reference),'--scene-fixtures',str(original)],env=dict(os.environ,AHOLO_USD_PYTHON=sys.executable),check=True)
    shutil.copytree(original,moved);shutil.rmtree(original)
    cases=[]
    for axis in ('Y','Z'):
        for unitname,units in (('mm',.001),('ft',.3048)):
            for extension in ('usda','usd','usdc','usdz'):cases.append((f'mixed-{axis}-{unitname}.{extension}',axis,units,True,True))
    for extension in ('usda','usd','usdc','usdz'):
        cases.extend([(f'gs-only.{extension}','Z',.001,True,False),(f'mesh-only.{extension}','Z',.001,False,True)])
    for filename,axis,units,has_gs,has_mesh in cases:
        path=moved/filename;stage=Usd.Stage.Open(str(path));assert stage and not stage.GetCompositionErrors()
        assert stage.GetDefaultPrim().GetPath()=='/Scene'
        assert UsdGeom.GetStageUpAxis(stage)==axis and UsdGeom.GetStageMetersPerUnit(stage)==units
        assert all(not prim.HasAuthoredReferences() and not prim.HasAuthoredPayloads() for prim in stage.Traverse())
        if has_gs:verify_gs(stage)
        else:assert not any(p.IsA(UsdVol.ParticleField3DGaussianSplat) for p in stage.Traverse())
        if has_mesh:verify_mesh(stage,axis,units)
        else:assert not any(p.IsA(UsdGeom.Mesh) for p in stage.Traverse())
        verify_bundle(path,moved,has_mesh)
print('24 production scenes verified across four formats: full SH 0–3, mixed-degree padding, placement, units/axes, materials, relocated dependencies and atomic publication. No Rhino/GPU acceptance.')
