#!/usr/bin/env python3
"""Verify production mesh USD/USDZ output using actual OpenUSD, without Rhino or cloud calls."""
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
from pxr import Ar, Gf, Usd, UsdGeom, UsdShade, UsdUtils, Sdf

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--reference',type=Path,default=ROOT/'tools/Aholo3D.Reference/bin/Release/net8.0/Aholo3D.Reference.dll')
args=parser.parse_args()
with tempfile.TemporaryDirectory(prefix='aholo3d-mesh-usd-check-') as temporary:
    work=Path(temporary);original=work/'original';moved=work/'moved'
    env=dict(os.environ,AHOLO3D_USD_PYTHON=sys.executable)
    subprocess.run(['dotnet',str(args.reference),'--mesh-fixtures',str(original)],env=env,check=True)
    shutil.copytree(original,moved);shutil.rmtree(original)
    for extension in ('usda','usd','usdc','usdz'):
        path=moved/('meshes.'+extension)
        stage=Usd.Stage.Open(str(path));assert stage and not stage.GetCompositionErrors()
        assert UsdGeom.GetStageUpAxis(stage)=='Z' and UsdGeom.GetStageMetersPerUnit(stage)==.001
        assert stage.GetDefaultPrim().GetPath()=='/Scene'
        meshes=[UsdGeom.Mesh(prim) for prim in stage.Traverse() if prim.IsA(UsdGeom.Mesh)]
        assert len(meshes)==3
        for index,mesh in enumerate(meshes):
            assert list(mesh.GetFaceVertexCountsAttr().Get())==[3]
            assert list(mesh.GetFaceVertexIndicesAttr().Get())==[0,1,2]
            assert mesh.GetSubdivisionSchemeAttr().Get()=='none' and mesh.GetOrientationAttr().Get()=='rightHanded'
            assert mesh.GetDoubleSidedAttr().Get()
            points=mesh.GetPointsAttr().Get();assert tuple(points[1])==(.125,0,0) and tuple(points[2])==(0,.25,.5)
            if index==0:
                matrix=UsdGeom.XformCache().GetLocalToWorldTransform(mesh.GetPrim())
                expected=(1e12+.25,-2e12+.25,3e12+.5)
                assert tuple(matrix.Transform(Gf.Vec3d(*points[1])))==expected
                assert mesh.GetPrim().GetDisplayName()=='大坐标 / smooth'
            if index<2:
                normals=mesh.GetNormalsAttr().Get();assert len(normals)==3
                edge1=points[1]-points[0];edge2=points[2]-points[0]
                assert abs(Gf.Dot(normals[0],edge1))<1e-7 and abs(Gf.Dot(normals[0],edge2))<1e-7
                assert Gf.Dot(Gf.Cross(edge1,edge2),normals[0])>0
                assert mesh.GetNormalsInterpolation()=='vertex'
                uv=UsdGeom.PrimvarsAPI(mesh).GetPrimvar('st');assert uv.GetInterpolation()=='vertex'
                assert list(map(tuple,uv.Get()))==[(0,0),(1,0),(0,1)]
            else:
                assert not mesh.GetNormalsAttr().HasAuthoredValueOpinion()
                assert not UsdGeom.PrimvarsAPI(mesh).GetPrimvar('st')
            material,_=UsdShade.MaterialBindingAPI(mesh.GetPrim()).ComputeBoundMaterial();assert material
            surface=UsdShade.Shader(stage.GetPrimAtPath(str(material.GetPath())+'/PreviewSurface'))
            assert surface.GetIdAttr().Get()=='UsdPreviewSurface'
            assert abs(surface.GetInput('metallic').Get()-.2)<1e-6 and abs(surface.GetInput('roughness').Get()-.4)<1e-6
            if index<2:
                bitmap=UsdShade.Shader(stage.GetPrimAtPath(str(material.GetPath())+'/Bitmap'))
                assert bitmap.GetInput('sourceColorSpace').Get()=='sRGB'
                assert tuple(bitmap.GetInput('scale').Get())[:3]==(.5,.25,.125)
                alpha_scale=bitmap.GetInput('scale').Get()[3];alpha_bias=bitmap.GetInput('bias').Get()[3]
                assert abs(alpha_scale-(.6 if index==0 else 0))<1e-6
                assert abs(alpha_bias-(0 if index==0 else .6))<1e-6
                assert surface.GetInput('diffuseColor').GetConnectedSource()[1]=='rgb'
                assert surface.GetInput('opacity').GetConnectedSource()[1]=='a'
                assert bitmap.GetInput('wrapT').Get()=='repeat'
                assert bitmap.GetInput('wrapS').Get()==('clamp' if index==0 else 'repeat')
                assert bitmap.GetPrim().GetCustomDataByKey('sourceSampler:minFilter')==(9728 if index==0 else 9729)
                image=bitmap.GetInput('file').Get();assert image.resolvedPath and not image.path.startswith('/')
                asset=Ar.GetResolver().OpenAsset(Ar.ResolvedPath(image.resolvedPath));encoded=asset.GetBuffer()
                assert hashlib.sha256(encoded).hexdigest()==Path(image.path).stem
            else:
                assert tuple(surface.GetInput('diffuseColor').Get())==(.5,.25,.125)
                assert abs(surface.GetInput('opacity').Get()-.6)<1e-6
        _,images,missing=UsdUtils.ComputeAllDependencies(Sdf.AssetPath(str(path)))
        assert not missing
        if extension=='usdz':
            with zipfile.ZipFile(path) as archive:
                assert archive.testzip() is None
                assert len([n for n in archive.namelist() if n.endswith('.png')])==1
                with path.open('rb') as raw:
                    for info in archive.infolist():
                        assert info.compress_type==zipfile.ZIP_STORED
                        raw.seek(info.header_offset+26);name_length,extra_length=struct.unpack('<HH',raw.read(4))
                        assert (info.header_offset+30+name_length+extra_length)%64==0
            for image in images:
                assert Ar.IsPackageRelativePath(str(image))
                assert Path(Ar.SplitPackageRelativePathOuter(str(image))[0]).resolve()==path.resolve()
        else:
            assert len(images)==1 and Path(str(images[0])).resolve().is_relative_to(moved.resolve())
print('Four production mesh USD formats verified: geometry, world scale/origin, normals, UVs, materials, alpha and relocated textures/package.')
