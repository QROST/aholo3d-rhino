#!/usr/bin/env python3
"""Parse and compose production-generated render patches with OpenUSD; no service calls."""
import argparse
from pathlib import Path
import subprocess
import tempfile

from pxr import Gf, Sdf, Usd, UsdGeom, UsdLux

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--reference", type=Path, default=ROOT / "tools/Aholo3D.Reference/bin/Release/net8.0/Aholo3D.Reference.dll")
args = parser.parse_args()
with tempfile.TemporaryDirectory(prefix="aholo3d-render-check-") as temporary:
    root = Path(temporary)
    subprocess.run(["dotnet", str(args.reference), "--render-fixtures", str(root)], check=True)
    layers = [Sdf.Layer.FindOrOpen(str(root / name)) for name in ("base.usda", "patch.usda", "camera.usda", "gs.usda")]
    assert all(layers)
    composed = Sdf.Layer.CreateAnonymous("composed.usda")
    composed.subLayerPaths = [layers[2].identifier, layers[1].identifier, layers[0].identifier]
    stage = Usd.Stage.Open(composed, load=Usd.Stage.LoadNone)
    pose = UsdGeom.Xformable(stage.GetPrimAtPath("/World/Model")).GetLocalTransformation()
    actual = pose.Transform(Gf.Vec3d(1, 2, 3))
    assert all(abs(a-b) < 1e-6 for a, b in zip(actual, (6010, 2020, 4030))), actual
    light = UsdLux.SphereLight(stage.GetPrimAtPath("/World/Lights/Light_0"))
    assert light.GetIntensityAttr().Get() == 8 and light.GetRadiusAttr().Get() == 50
    assert tuple(UsdGeom.Xformable(light).GetLocalTransformation().ExtractTranslation()) == (1000, 2000, 3000)
    environment = stage.GetPrimAtPath("/Render/MainRenderSettings").GetAttribute("manycore:environment:giIntensity").Get()
    assert all(abs(x-.8) < 1e-6 for x in environment)
    camera = UsdGeom.Xformable(stage.GetPrimAtPath("/World/Cameras/MainCamera")).GetLocalTransformation()
    assert tuple(camera.ExtractTranslation()) == (400, 500, 600)
    assert stage.GetPrimAtPath("/World/Model/Geom").GetTypeName() == "Mesh"
    gs = Usd.Stage.Open(layers[3], load=Usd.Stage.LoadNone)
    assert gs.GetPrimAtPath("/World/Model/Geom").GetTypeName() == "ParticleField3DGaussianSplat"
    assert tuple(UsdGeom.Xformable(gs.GetPrimAtPath("/World/Model")).GetLocalTransformation().ExtractTranslation()) == (1, 2, 3)
    names=["composition-base.usda", "composition-add.usda", "composition-edit.usda", "composition-remove.usda", "composition-empty.usda"]
    composition=[Sdf.Layer.FindOrOpen(str(root / name)) for name in names]
    assert all(composition)
    initial=Usd.Stage.Open(composition[0], load=Usd.Stage.LoadNone)
    assert initial.GetPrimAtPath("/World/Lights").GetTypeName()=="Xform"
    assert not list(initial.GetPrimAtPath("/World/Lights").GetChildren())
    cumulative=Sdf.Layer.CreateAnonymous("cumulative.usda")
    cumulative.subLayerPaths=[layer.identifier for layer in reversed(composition[:3])]
    multi=Usd.Stage.Open(cumulative, load=Usd.Stage.LoadNone)
    mesh=multi.GetPrimAtPath("/World/Model/Geom");gs=multi.GetPrimAtPath("/World/Model_gs/Geom")
    assert mesh.GetTypeName()=="Mesh" and gs.GetTypeName()=="ParticleField3DGaussianSplat"
    for prim,asset,path in ((mesh,"mesh-a","/mesh"),(gs,"gaussian-b","/GaussianSplatData")):
        payload=prim.GetMetadata("payload").GetAddedOrExplicitItems()
        assert len(payload)==1 and payload[0].assetPath=="manycore:/mesh/"+asset and str(payload[0].primPath)==path
    transform=UsdGeom.Xformable(gs.GetParent()).GetLocalTransformation()
    assert all(abs(a-b)<1e-6 for a,b in zip(transform.Transform(Gf.Vec3d(1,2,3)),(96,202,306)))
    transform=UsdGeom.Xformable(mesh.GetParent()).GetLocalTransformation()
    assert all(abs(a-b)<1e-6 for a,b in zip(transform.Transform(Gf.Vec3d(1,2,3)),(-510,1480,1040)))
    key=UsdLux.SphereLight(multi.GetPrimAtPath("/World/Lights/Light_key"))
    fill=UsdLux.RectLight(multi.GetPrimAtPath("/World/Lights/Light_fill"))
    assert key.GetIntensityAttr().Get()==3 and key.GetRadiusAttr().Get()==75
    assert tuple(UsdGeom.Xformable(key).GetLocalTransformation().ExtractTranslation())==(-100,-200,500)
    assert fill.GetWidthAttr().Get()==500 and fill.GetHeightAttr().Get()==700 and fill.GetIntensityAttr().Get()==4
    unchanged_camera=UsdGeom.Xformable(multi.GetPrimAtPath("/World/Cameras/MainCamera")).GetLocalTransformation()
    assert unchanged_camera==UsdGeom.Xformable(initial.GetPrimAtPath("/World/Cameras/MainCamera")).GetLocalTransformation()
    assert all(abs(x-.25)<1e-6 for x in multi.GetPrimAtPath("/Render/MainRenderSettings").GetAttribute("manycore:environment:giIntensity").Get())
    # Deletion is a provider operation marker, not standard USD composition/deactivation.
    # Verify its exact typed target without claiming OpenUSD simulates service deletion.
    for layer,targets in ((composition[3],{"/World/Model/Geom":"Mesh","/World/Lights/Light_fill":"RectLight"}),
                          (composition[4],{"/World/Model_gs/Geom":"ParticleField3DGaussianSplat","/World/Lights/Light_key":"SphereLight"})):
        stage=Usd.Stage.Open(layer,load=Usd.Stage.LoadNone)
        marked={str(prim.GetPath()):prim.GetTypeName() for prim in stage.TraverseAll() if prim.GetCustomData().get("manycore:operation:type")=="del"}
        assert marked==targets,marked
    advanced_base=Sdf.Layer.FindOrOpen(str(root/"advanced-base.usda"))
    advanced_patch=Sdf.Layer.FindOrOpen(str(root/"advanced-patch.usda"))
    assert advanced_base and advanced_patch
    before=Usd.Stage.Open(advanced_base,load=Usd.Stage.LoadNone)
    affine=UsdGeom.Xformable(before.GetPrimAtPath("/World/Model")).GetLocalTransformation()
    assert all(abs(a-b)<1e-6 for a,b in zip(affine.Transform(Gf.Vec3d(2,-3,5)),(-11990,-8980,14530)))
    sphere=UsdLux.SphereLight(before.GetPrimAtPath("/World/Lights/Light_point"))
    assert sphere.GetExposureAttr().Get()==-1 and sphere.GetSpecularAttr().Get()==2.5
    assert sphere.GetPrim().GetCustomData()["manycore:affectReflections"]==0
    assert "manycore:doubleFace" not in sphere.GetPrim().GetCustomData()
    rectangle=UsdLux.RectLight(before.GetPrimAtPath("/World/Lights/Light_area"))
    assert rectangle.GetExposureAttr().Get()==2 and abs(rectangle.GetSpecularAttr().Get()-.4)<1e-6
    assert rectangle.GetPrim().GetCustomData()["manycore:doubleFace"] is True
    assert rectangle.GetPrim().GetCustomData()["manycore:affectReflections"]==1
    full=Sdf.Layer.CreateAnonymous("advanced.usda");full.subLayerPaths=[advanced_patch.identifier,advanced_base.identifier]
    after=Usd.Stage.Open(full,load=Usd.Stage.LoadNone)
    affine=UsdGeom.Xformable(after.GetPrimAtPath("/World/Model")).GetLocalTransformation()
    assert all(abs(a-b)<1e-6 for a,b in zip(affine.Transform(Gf.Vec3d(2,-3,5)),(30,1,33.5)))
    rectangle=UsdLux.RectLight(after.GetPrimAtPath("/World/Lights/Light_area"))
    assert rectangle.GetExposureAttr().Get()==0 and rectangle.GetSpecularAttr().Get()==0
    assert rectangle.GetPrim().GetCustomData()["manycore:doubleFace"] is False
    assert rectangle.GetPrim().GetCustomData()["manycore:affectReflections"]==0
    pose=UsdGeom.Xformable(rectangle).GetLocalTransformation()
    assert tuple(pose.ExtractTranslation())==(1000,2000,3000)
    assert all(abs(a-b)<1e-6 for a,b in zip(pose.TransformDir(Gf.Vec3d(0,0,-1)),(-1,0,0)))
    assert rectangle.GetWidthAttr().Get()==500 and rectangle.GetHeightAttr().Get()==700
print("OpenUSD production render patches passed: multi-asset pose, signed affine/shear, independent light rotation, advanced light values/metadata reset, camera and typed deletion markers.")
