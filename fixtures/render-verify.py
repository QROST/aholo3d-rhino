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
print("OpenUSD production render patches passed: typed paths, world pose, light, environment, independent camera and GS.")
