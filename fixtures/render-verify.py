#!/usr/bin/env python3
"""Parse and compose production-generated render patches with OpenUSD; no service calls."""
import argparse
from pathlib import Path
import subprocess
import tempfile

from pxr import Gf, Sdf, Usd, UsdGeom, UsdLux

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--reference", type=Path, default=ROOT / "tools/Aholo.Reference/bin/Release/net8.0/Aholo.Reference.dll")
args = parser.parse_args()
with tempfile.TemporaryDirectory(prefix="aholo-render-check-") as temporary:
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
print("OpenUSD production render patches passed: typed paths, world pose, light, environment, independent camera and GS.")
