#!/usr/bin/env python3
"""Independent geometry oracles and optional actual OpenUSD/public-reader shading checks. No Rhino or network."""
import argparse
from collections import Counter
from fractions import Fraction
import importlib.util
import math
import os
from pathlib import Path
import subprocess
import sys
import tempfile

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('usd_mesh_reader',ROOT/'scripts/usd_mesh_reader.py')
reader=importlib.util.module_from_spec(spec);spec.loader.exec_module(reader)
CONCAVE=[(4,0,0),(0,1,0),(1,0,0),(0,-1,0)]
QUADS={
 'ConcaveQuad':CONCAVE,
 'ConvexQuad':[(0,0,0),(2,0,0),(2,2,0),(0,2,0)],
 'CollinearQuad':[(0,0,0),(1,0,0),(2,0,0),(0,1,0)],
 'WarpedQuad':[(0,0,0),(2,0,0),(2,2,.5),(0,2,0)],
 'AlmostPlanarQuad':[(0,0,0),(2,2,0),(0,2,0),(2,0,1e-7)],
 'SmallestWarpedQuad':[(0,0,0),(2,2,0),(0,2,0),(2,0,2**-149)],
 'CancellationQuad':[(0,0,0),(1,2,0),(4,1,0),(5,3,2**-44)],
 'DynamicRangeQuad':[(1,0,0),(2**60,2**60,0),(2**61,2**61,0),(3*2**60,3*2**60,0)],
}

def area2(points):
    return sum(Fraction(a[0])*Fraction(b[1])-Fraction(a[1])*Fraction(b[0]) for a,b in zip(points,points[1:]+points[:1]))

def geometry_checks():
    cases=0
    for reverse in (False,True):
        points=list(reversed(CONCAVE)) if reverse else CONCAVE
        triangles=reader.quad_topology(points);assert len(triangles)==2
        areas=[area2([points[i] for i in t]) for t in triangles]
        assert all(a<0 if reverse else a>0 for a in areas) and sum(areas)==(-6 if reverse else 6)
        edges=Counter(tuple(sorted((a,b))) for t in triangles for a,b in zip(t,t[1:]+t[:1]))
        assert edges[(1,3) if reverse else (0,2)]==2
        cases+=1
    for name,points in QUADS.items():
        result=reader.quad_topology(points)
        if name in ('ConcaveQuad','CollinearQuad','DynamicRangeQuad'):
            assert len(result)==2 and {i for t in result for i in t}=={0,1,2,3}
        else:assert result is None
        cases+=1
    dynamic=QUADS['DynamicRangeQuad'];triangles=reader.quad_topology(dynamic)
    assert sum(area2([dynamic[i] for i in t]) for t in triangles)==-2**61
    assert all(area2([dynamic[i] for i in t])<0 for t in triangles)
    cases+=1
    # A true nonzero 3D warp must never be classified as a crossing 2D boundary.
    for height in (1e-7,2**-149,1e-200):
        points=[(0,0,0),(2,2,0),(0,2,0),(2,0,height)]
        assert reader.quad_topology(points) is None and reader.facet_normal(points)==[1,0,0]
        cases+=1
    invalid=[[(0,0,0),(2,2,0),(0,2,0),(2,0,0)],[(0,0,0),(1,0,0),(1,0,0),(0,1,0)],[(0,0,0),(2,0,0),(1,0,0),(0,1,0)],[(0,0,0),(1,1,1),(2,2,2),(3,3,3)]]
    for points in invalid:
        try:reader.quad_topology(points)
        except ValueError:pass
        else:raise AssertionError('Invalid planar quad admitted')
        cases+=1
    for scale in (1e-200,1e-160,1,1e150):
        points=[(x*scale,y*scale,z*scale) for x,y,z in CONCAVE]
        triangles=reader.triangulate_polygon(points)
        assert len(triangles)==2 and sum(area2([points[i] for i in t]) for t in triangles)==area2(points)
        assert reader.facet_normal(points)==[0,0,1] and reader.facet_normal(points,True)==[0,0,-1]
        cases+=1
    for axis in range(3):
        points=[tuple(p[(i+axis)%3] for i in range(3)) for p in CONCAVE]
        expected=[0,0,0];expected[(2-axis)%3]=1
        assert reader.facet_normal(points)==expected and len(reader.quad_topology(points))==2
        cases+=1
    thin=[(0,0,0),(1,0,0),(1,1e-200,0),(0,1e-200,0)]
    assert len(reader.triangulate_polygon(thin))==2 and reader.quad_topology(thin) is None
    assert reader.facet_normal([(0,0,0),(1,0,0),(2,0,0),(2,1,0),(0,1,0)])==[0,0,1]
    assert reader.facet_normal([(1e12+x,1e12+y,z) for x,y,z in CONCAVE])==[0,0,1]
    for power in (10,40,44,149):
        normal=reader.facet_normal([(0,0,0),(1,2,0),(4,1,0),(5,3,2**-power)])
        expected=[1/math.sqrt(17),-4/math.sqrt(17),0]
        assert all(math.isclose(a,b,rel_tol=0,abs_tol=1e-7) for a,b in zip(normal,expected))
        cases+=1
    size=5e102
    huge=[(0,0,0),(size,size,0),(0,size,size),(-size,0,size)]
    assert reader.coplanar_quad(huge) and reader.quad_topology(huge) is None
    cases+=1
    # Compare the shared orientation predicate with exact signs near subnormal products.
    for scale in (1e-200,1e-160,1e-155,1,1e150):
        for x in (0,1,2):
            a=(scale,scale);b=(2*scale,3*scale);c=(x*scale,2*scale)
            exact=(Fraction(a[0])-Fraction(c[0]))*(Fraction(b[1])-Fraction(c[1]))-(Fraction(a[1])-Fraction(c[1]))*(Fraction(b[0])-Fraction(c[0]))
            assert reader.orient(a,b,c)==(exact>0)-(exact<0)
            cases+=1
    print(f'Geometry oracles passed: {cases+3} cases; exact areas, diagonal, winding, subnormal signs, axes and warped-quad compatibility.')


def integration_checks():
    from pxr import Gf,Sdf,Usd,UsdGeom,UsdShade,UsdUtils,UsdVol
    assert Usd.GetVersion()==(0,26,11)
    with tempfile.TemporaryDirectory(prefix='aholo-shading-') as temporary:
        work=Path(temporary)
        demo=work/'demo';demo.mkdir();reader.convert(ROOT/'fixtures/mesh-shading.usda',demo)
        import json
        parts=json.loads((demo/'mesh-import.json').read_text())['parts']
        assert len(parts)==3 and sum(p['faces'] for p in parts)==6
        by_name={p['name']:p for p in parts}
        assert by_name['FacetedCorner']['generatedNormals'] and by_name['FacetedCorner']['vertices']==6
        assert not by_name['AuthoredSmoothCorner']['generatedNormals'] and by_name['AuthoredSmoothCorner']['vertices']==4
        assert by_name['ConcaveQuad']['faces']==2 and by_name['ConcaveQuad']['vertices']==4
        source=work/'shading.usda';stage=Usd.Stage.CreateNew(str(source))
        UsdGeom.SetStageUpAxis(stage,'Z');UsdGeom.SetStageMetersPerUnit(stage,1)
        root=UsdGeom.Xform.Define(stage,'/World');stage.SetDefaultPrim(root.GetPrim())
        points=[(0,0,0),(2,0,0),(0,2,0),(0,0,2)]
        def mesh(name,vertices,counts,indices):
            m=UsdGeom.Mesh.Define(stage,'/World/'+name);m.CreateSubdivisionSchemeAttr('none');m.CreatePointsAttr(vertices)
            m.CreateFaceVertexCountsAttr(counts);m.CreateFaceVertexIndicesAttr(indices);return m
        for authored in (False,True):
            for left in (False,True):
                for mirror in (False,True):
                    name=('Authored' if authored else 'Faceted')+('_L' if left else '_R')+('_Mirror' if mirror else '')
                    m=mesh(name,points,[3,3],[0,1,2,0,3,1]);m.CreateOrientationAttr('leftHanded' if left else 'rightHanded')
                    if mirror:m.AddScaleOp().Set((-2,3,4))
                    if name!='Faceted_R':
                        st=UsdGeom.PrimvarsAPI(m).CreatePrimvar('st',Sdf.ValueTypeNames.TexCoord2fArray,'faceVarying');st.Set([(i/8,i/16) for i in range(6)])
                    if authored:
                        m.CreateNormalsAttr([(0,0,-1)]);m.SetNormalsInterpolation('constant')
                        pv=UsdGeom.PrimvarsAPI(m).CreatePrimvar('normals',Sdf.ValueTypeNames.Normal3fArray,'vertex');pv.Set([(1,2,3)]*4)
        split=mesh('Split',points,[3,3],[0,1,2,0,3,1]);binding=UsdShade.MaterialBindingAPI.Apply(split.GetPrim())
        for index,name in enumerate(('Red','Blue')):
            mat=UsdShade.Material.Define(stage,'/World/'+name);shader=UsdShade.Shader.Define(stage,'/World/'+name+'/Surface');shader.CreateIdAttr('UsdPreviewSurface')
            shader.CreateInput('diffuseColor',Sdf.ValueTypeNames.Color3f).Set((1,0,0) if index==0 else (0,0,1))
            shader.CreateOutput('surface',Sdf.ValueTypeNames.Token);mat.CreateSurfaceOutput().ConnectToSource(shader.ConnectableAPI(),'surface')
            subset=binding.CreateMaterialBindSubset(name,[index]);UsdShade.MaterialBindingAPI.Apply(subset.GetPrim()).Bind(mat)
        for name,vertices in QUADS.items():mesh(name,vertices,[4],[0,1,2,3])
        offset=mesh('OffsetQuad',[(10,0,0),(11,0,0),(10,1,0)]+CONCAVE,[3,4],list(range(7)))
        pv=UsdGeom.PrimvarsAPI(offset).CreatePrimvar('normals',Sdf.ValueTypeNames.Normal3fArray,'faceVarying');pv.Set([(i+1,1,2) for i in range(7)]);pv.SetIndices(list(reversed(range(7))))
        st=UsdGeom.PrimvarsAPI(offset).CreatePrimvar('st',Sdf.ValueTypeNames.TexCoord2fArray,'faceVarying');st.Set([(i/8,i/16) for i in range(7)]);st.SetIndices(list(reversed(range(7))))
        stage.GetRootLayer().Save()
        def verify_formats(stem,mixed):
            stage.GetRootLayer().Save()
            for fmt in ('usda','usd','usdc','usdz'):
                path=work/(stem+'.'+fmt)
                if fmt=='usdz':assert UsdUtils.CreateNewUsdzPackage(Sdf.AssetPath(str(source)),str(path))
                elif path!=source:assert stage.GetRootLayer().Export(str(path))
                mode='--mesh-shading-scene-fixture' if mixed else '--mesh-shading-fixture'
                subprocess.run(['dotnet',str(ROOT/'tools/Aholo.Reference/bin/Release/net8.0/Aholo.Reference.dll'),mode,str(path)],check=True,env=dict(os.environ,AHOLO_USD_PYTHON=sys.executable))
        verify_formats('shading',False)
        gs=UsdVol.ParticleField3DGaussianSplat.Define(stage,'/World/Gaussian');gs.CreatePositionsAttr([(0,0,0)]);gs.CreateScalesAttr([(1,1,1)])
        gs.CreateOrientationsAttr([Gf.Quatf(1,0,0,0)]);gs.CreateOpacitiesAttr([.5]);gs.CreateRadianceSphericalHarmonicsDegreeAttr(0);gs.CreateRadianceSphericalHarmonicsCoefficientsAttr([(0,0,0)])
        verify_formats('mixed-shading',True)
        # Authored invalid normals must fail, never fall back to generated shading.
        defects=('empty-normals','zero-normals','cardinality','primvar-overrides','crossing-quad','repeat-quad','collinear-quad','nonfinite-points')
        for defect in defects:
            bad=work/(defect+'.usda');st=Usd.Stage.CreateNew(str(bad));UsdGeom.SetStageUpAxis(st,'Z');UsdGeom.SetStageMetersPerUnit(st,1)
            m=UsdGeom.Mesh.Define(st,'/Mesh');m.CreateSubdivisionSchemeAttr('none');m.CreatePointsAttr(points[:3]);m.CreateFaceVertexCountsAttr([3]);m.CreateFaceVertexIndicesAttr([0,1,2])
            m.CreateNormalsAttr([(0,0,1)]);m.SetNormalsInterpolation('constant')
            if defect=='empty-normals':m.GetNormalsAttr().Set([])
            elif defect=='zero-normals':m.GetNormalsAttr().Set([(0,0,0)])
            elif defect=='cardinality':m.SetNormalsInterpolation('vertex')
            elif defect=='primvar-overrides':UsdGeom.PrimvarsAPI(m).CreatePrimvar('normals',Sdf.ValueTypeNames.Normal3fArray,'constant').Set([(0,0,0)])
            elif defect=='nonfinite-points':m.GetPointsAttr().Set([(float('nan'),0,0),(1,0,0),(0,1,0)])
            else:
                vertices={'crossing-quad':[(0,0,0),(2,2,0),(0,2,0),(2,0,0)],'repeat-quad':[(0,0,0),(1,0,0),(1,0,0),(0,1,0)],'collinear-quad':[(0,0,0),(1,1,1),(2,2,2),(3,3,3)]}[defect]
                m.GetPointsAttr().Set(vertices);m.GetFaceVertexCountsAttr().Set([4]);m.GetFaceVertexIndicesAttr().Set([0,1,2,3])
            st.GetRootLayer().Save();out=work/(defect+'-out');out.mkdir()
            try:reader.convert(bad,out)
            except (ValueError,RuntimeError):pass
            else:raise AssertionError(defect+' was accepted')
            assert not (out/'mesh-import.json').exists()
        print('Eight malformed-normal/quad cases rejected without publishing a mesh manifest.')
    print('Actual OpenUSD verified: 4 mesh + 4 mixed-scene files through public C# readers. Native acceptance remains pending.')

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--geometry-only',action='store_true');args=parser.parse_args()
    geometry_checks()
    if not args.geometry_only:integration_checks()
