#!/usr/bin/env python3
"""Explicit OpenUSD integration checks; run with the pinned USD interpreter, outside ordinary .NET unit tests."""
import argparse
import importlib.util
import json
import math
import struct
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile

from pxr import Gf, Sdf, Usd, UsdGeom, UsdUtils, UsdVol, Vt

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("gsrhino_usd_bridge", ROOT / "scripts" / "usd_bridge.py")
bridge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bridge)


def gs(stage, path, half=False, degree=3, count=2):
    splat = UsdVol.ParticleField3DGaussianSplat.Define(stage, path)
    positions = [(i + 1, i + 2, i + 3) for i in range(count)]
    scales = [(2, .5, 1) for _ in range(count)]
    orientations = [Gf.Quatf(.5, .5, -.5, .5) for _ in range(count)]
    opacity = [float(i % 2) for i in range(count)]
    coefficients = [(k * .125 - 1, k * -.0625 + .5, k * .03125) for i in range(count) for k in range((degree + 1) ** 2)]
    if half:
        splat.CreatePositionshAttr(Vt.Vec3hArray([Gf.Vec3h(*v) for v in positions]))
        splat.CreateScaleshAttr(Vt.Vec3hArray([Gf.Vec3h(*v) for v in scales]))
        splat.CreateOrientationshAttr(Vt.QuathArray([Gf.Quath(.5, .5, -.5, .5) for _ in range(count)]))
        splat.CreateOpacitieshAttr(Vt.HalfArray(opacity))
        splat.CreateRadianceSphericalHarmonicsCoefficientshAttr(Vt.Vec3hArray([Gf.Vec3h(*v) for v in coefficients]))
    else:
        splat.CreatePositionsAttr(Vt.Vec3fArray(positions))
        splat.CreateScalesAttr(Vt.Vec3fArray(scales))
        splat.CreateOrientationsAttr(Vt.QuatfArray(orientations))
        splat.CreateOpacitiesAttr(Vt.FloatArray(opacity))
        splat.CreateRadianceSphericalHarmonicsCoefficientsAttr(Vt.Vec3fArray(coefficients))
    splat.CreateRadianceSphericalHarmonicsDegreeAttr(degree)
    return splat


def stage(path):
    scene = Usd.Stage.CreateNew(str(path))
    UsdGeom.SetStageUpAxis(scene, UsdGeom.Tokens.z)
    UsdGeom.SetStageMetersPerUnit(scene, .001)
    return scene


def fixtures(directory):
    path = directory / "usd-two-parts.usda"
    scene = stage(path)
    world = UsdGeom.Xform.Define(scene, "/World")
    world.AddTranslateOp().Set(Gf.Vec3d(10, 20, 30))
    scene.SetDefaultPrim(world.GetPrim())
    group = UsdGeom.Xform.Define(scene, "/World/Group")
    group.AddRotateZOp().Set(90)
    group.AddScaleOp().Set(Gf.Vec3f(2, 3, 4))
    gs(scene, "/World/Group/Float")
    other = gs(scene, "/World/Half", half=True)
    UsdGeom.Xformable(other).AddTranslateOp().Set(Gf.Vec3d(5, -2, 1))
    scene.GetRootLayer().Save()
    binary = directory / "usd-two-parts.usdc"
    scene.GetRootLayer().Export(str(binary))
    archive = directory / "usd-two-parts.usdz"
    UsdUtils.CreateNewUsdzPackage(Sdf.AssetPath(str(binary)), str(archive))
    source = stage(directory / "usd-prototype.usda")
    asset = UsdGeom.Xform.Define(source, "/Asset")
    source.SetDefaultPrim(asset.GetPrim())
    gs(source, "/Asset/Splat", degree=1, count=1)
    source.GetRootLayer().Save()
    instanced = stage(directory / "usd-instanced.usda")
    for name, offset in (("A", 3), ("B", 9)):
        instance = UsdGeom.Xform.Define(instanced, "/" + name)
        instance.GetPrim().GetReferences().AddReference("usd-prototype.usda")
        instance.GetPrim().SetInstanceable(True)
        instance.AddTranslateOp().Set(Gf.Vec3d(offset, 0, 0))
    instanced.GetRootLayer().Save()
    return path


def export_fixture(directory):
    """Independent canonical transport fixture: all SH degrees, large origin and a reflected/sheared affine."""
    parts = []
    for degree in range(4):
        coefficients = (degree + 1) ** 2
        fields = ("x", "y", "z", "scale_0", "scale_1", "scale_2", "rot_0", "rot_1", "rot_2", "rot_3", "opacity")
        header = ["ply", "format binary_little_endian 1.0", "element vertex 2"]
        header += ["property double " + name for name in fields]
        header += ["property float f_dc_" + str(i) for i in range(3)]
        header += ["property float f_rest_" + str(i) for i in range(3 * (coefficients - 1))]
        origin = [1e9 + .01, -2e8 + .04, 3e7 + .05]
        file = f"part-{degree:06d}.ply"
        with open(directory / file, "wb") as stream:
            stream.write(("\n".join(header) + "\nend_header\n").encode())
            record = struct.Struct("<11d" + "f" * (3 * coefficients))
            for i in range(2):
                position = [origin[j] + i * (.013, -.027, .031)[j] for j in range(3)]
                sh = [k * .125 - .5 + c * .0625 for k in range(coefficients) for c in range(3)]
                channel_major = sh[:3] + [sh[3 * k + c] for c in range(3) for k in range(1, coefficients)]
                stream.write(record.pack(*position, *(math.log(v) for v in (2e-5, 7e-6, 3e-5)), .5, .5, -.5, .5,
                                         -sys.float_info.max if i == 0 else sys.float_info.max, *channel_major))
        parts.append({"name": ("Duplicate 用户", "Duplicate 用户", "", "a / not a path")[degree], "file": file, "count": 2,
                      "degree": degree, "origin": origin, "transform": [-2, .25, 0, .5, 3, .75, 0, -.125, 4, 10, -5, 20],
                      "antialiased": degree % 2 == 0})
    manifest = {"protocol": 1, "openUsdVersion": "26.11", "upAxis": "Y", "metersPerUnit": .3048, "parts": parts}
    (directory / "export.json").write_text(json.dumps(manifest))
    return manifest


def read_records(directory, part):
    stream, record = bridge.canonical_payload(directory, part)
    with stream:
        return [record.unpack(stream.read(record.size)) for _ in range(part["count"])]


def world_point(transform, point):
    return tuple(sum(transform[r * 3 + c] * point[c] for c in range(3)) + transform[9 + r] for r in range(3))


def covariance(transform, record):
    w, x, y, z = record[6:10]
    norm = math.sqrt(w*w + x*x + y*y + z*z)
    w, x, y, z = (v / norm for v in (w, x, y, z))
    rotation = ((1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)), (2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)), (2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)))
    axes = [[sum(transform[r*3+k]*rotation[k][c] for k in range(3))*math.exp(record[3+c]) for c in range(3)] for r in range(3)]
    return [sum(axes[r][k]*axes[c][k] for k in range(3)) for r in range(3) for c in range(3)]


class Integration(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="gsrhino-usd-check-")
        self.directory = Path(self.temporary.name)
        fixtures(self.directory)
        self.sequence = 0

    def tearDown(self):
        self.temporary.cleanup()

    def decode(self, file):
        self.sequence += 1
        output = self.directory / ("output-" + str(self.sequence))
        output.mkdir()
        bridge.convert(file, output)
        return json.loads((output / "manifest.json").read_text()), output

    def test_usda_usdc_usdz_same_complete_data_and_actual_usdc_root(self):
        results = [self.decode(self.directory / ("usd-two-parts." + ext)) for ext in ("usda", "usdc", "usdz")]
        for manifest, _ in results:
            self.assertEqual(2, len(manifest["parts"]))
            self.assertEqual(4, sum(part["count"] for part in manifest["parts"]))
            self.assertEqual([3, 3], [part["degree"] for part in manifest["parts"]])
            self.assertEqual("Z", manifest["upAxis"])
            self.assertEqual(.001, manifest["metersPerUnit"])
            self.assertEqual([10., 20., 30.], manifest["parts"][0]["transform"][9:])
            self.assertEqual([15., 18., 31.], manifest["parts"][1]["transform"][9:])
            linear = manifest["parts"][0]["transform"][:9]
            for actual, expected in zip(linear, (0, -3, 0, 2, 0, 0, 0, 0, 4)):
                self.assertAlmostEqual(expected, actual, places=12)
        for index in range(2):
            self.assertEqual(1, len({(out / manifest["parts"][index]["file"]).read_bytes() for manifest, out in results}))
        with zipfile.ZipFile(self.directory / "usd-two-parts.usdz") as package:
            root = package.namelist()[0]
            self.assertTrue(root.endswith(".usdc"))
            self.assertEqual(b"PXR-USDC", package.read(root)[:8])

    def test_all_instance_proxies_and_external_layer_warning(self):
        manifest, _ = self.decode(self.directory / "usd-instanced.usda")
        self.assertEqual(["/A/Splat", "/B/Splat"], [part["name"] for part in manifest["parts"]])
        self.assertEqual([3., 9.], [part["transform"][9] for part in manifest["parts"]])
        self.assertTrue(any("external dependencies" in warning for warning in manifest["warnings"]))

    def test_float_arrays_win_over_half_without_losing_sh(self):
        path = self.directory / "usd-float-priority.usda"
        scene = stage(path)
        splat = gs(scene, "/Splat", half=True, degree=1, count=1)
        splat.CreatePositionsAttr(Vt.Vec3fArray([(9, 8, 7)]))
        scene.GetRootLayer().Save()
        manifest, output = self.decode(path)
        import struct
        payload = (output / manifest["parts"][0]["file"]).read_bytes().split(b"end_header\n", 1)[1]
        self.assertEqual((9., 8., 7.), struct.unpack_from("<3d", payload))
        self.assertEqual(1, manifest["parts"][0]["degree"])

    def test_regular_points_are_not_gaussians(self):
        path = self.directory / "usd-points.usda"
        scene = stage(path); UsdGeom.Points.Define(scene, "/Points").CreatePointsAttr([(1, 2, 3)]); scene.GetRootLayer().Save()
        with self.assertRaisesRegex(bridge.UnsupportedUsd, "Ordinary Points"):
            self.decode(path)

    def test_mixed_mesh_requires_complete_scene_import(self):
        path = self.directory / "usd-mixed.usda"
        scene = stage(path); gs(scene, "/Splat"); UsdGeom.Mesh.Define(scene, "/Mesh"); scene.GetRootLayer().Save()
        with self.assertRaisesRegex(bridge.UnsupportedUsd, "AholoImportScene"):
            self.decode(path)

    def test_time_samples_fail_instead_of_importing_wrong_frame(self):
        path = self.directory / "usd-animated.usda"
        scene = stage(path); splat = gs(scene, "/Splat"); splat.GetPositionsAttr().Set([(0, 0, 0), (1, 1, 1)], 1); scene.GetRootLayer().Save()
        with self.assertRaisesRegex(bridge.UnsupportedUsd, "animated/time-sampled"):
            self.decode(path)

    def test_missing_reference_fails(self):
        path = self.directory / "usd-missing.usda"
        scene = stage(path); gs(scene, "/Splat"); scene.DefinePrim("/Missing").GetReferences().AddReference("not-present.usda"); scene.GetRootLayer().Save()
        with self.assertRaisesRegex(bridge.UnsupportedUsd, "dependenc"):
            self.decode(path)

    def test_malformed_sh_is_not_truncated(self):
        path = self.directory / "usd-bad-sh.usda"
        scene = stage(path); splat = gs(scene, "/Splat"); splat.GetRadianceSphericalHarmonicsCoefficientsAttr().Set([(1, 1, 1)]); scene.GetRootLayer().Save()
        with self.assertRaisesRegex(bridge.UnsupportedUsd, "SH coefficient count"):
            self.decode(path)

    def test_unsupported_degree_is_not_reduced(self):
        path = self.directory / "usd-sh4.usda"
        scene = stage(path); gs(scene, "/Splat", degree=4); scene.GetRootLayer().Save()
        with self.assertRaisesRegex(bridge.UnsupportedUsd, "SH degree 4"):
            self.decode(path)

    def test_point_instancer_is_not_a_single_prototype_import(self):
        path = self.directory / "usd-point-instancer.usda"
        scene = stage(path); gs(scene, "/Splat"); UsdGeom.PointInstancer.Define(scene, "/Instances"); scene.GetRootLayer().Save()
        with self.assertRaisesRegex(bridge.UnsupportedUsd, "PointInstancer"):
            self.decode(path)

    def test_vendor_gaussian_data_on_container_is_not_omitted(self):
        for field in ("radiance:sphericalHarmonicsCoefficients", "radiance:sphericalHarmonicsCoefficientsh"):
            with self.subTest(field=field):
                path = self.directory / ("container-" + field.split(":")[-1] + ".usda")
                scene = stage(path); gs(scene, "/Splat")
                prim = UsdGeom.Xform.Define(scene, "/Vendor").GetPrim()
                prim.CreateAttribute(field, Sdf.ValueTypeNames.Float3Array).Set(Vt.Vec3fArray([Gf.Vec3f(1)]))
                scene.GetRootLayer().Save()
                with self.assertRaisesRegex(bridge.UnsupportedUsd, "unsupported Gaussian schema"):
                    self.decode(path)

    def test_vendor_schema_is_not_a_pointcloud(self):
        path = self.directory / "usd-vendor.usda"
        scene = stage(path); scene.DefinePrim("/Vendor", "OmniNuRecFieldAsset"); scene.GetRootLayer().Save()
        with self.assertRaisesRegex(bridge.UnsupportedUsd, "unsupported Gaussian schema"):
            self.decode(path)

    def test_usdz_cannot_resolve_a_dependency_outside_its_package(self):
        root = self.directory / "usd-escaping-root.usda"
        scene = stage(root)
        scene.DefinePrim("/External").GetReferences().AddReference(str(self.directory / "usd-prototype.usda"))
        scene.GetRootLayer().Save()
        archive = self.directory / "usd-escaping.usdz"
        with Sdf.ZipFileWriter.CreateNew(str(archive)) as writer:
            writer.AddFile(str(root), "root.usda")
        with self.assertRaisesRegex(bridge.UnsupportedUsd, "escapes its package"):
            self.decode(archive)


    def test_export_all_containers_roundtrip_full_fields_names_units_and_large_local_origin(self):
        transport = self.directory / "transport"; transport.mkdir()
        original = export_fixture(transport)
        for extension in ("usd", "usda", "usdc", "usdz"):
            output = self.directory / ("export-" + extension); output.mkdir()
            bridge.export(transport / "export.json", output, extension)
            target = output / ("result." + extension)
            if extension in ("usd", "usdc"):
                self.assertEqual(b"PXR-USDC", target.read_bytes()[:8])
            elif extension == "usda":
                self.assertEqual(b"#usda 1.", target.read_bytes()[:8])
            else:
                with zipfile.ZipFile(target) as archive:
                    self.assertTrue(archive.namelist()[0].endswith(".usdc"))
                    self.assertEqual(b"PXR-USDC", archive.read(archive.namelist()[0])[:8])
                    self.assertTrue(all(info.compress_type == zipfile.ZIP_STORED for info in archive.infolist()))
            manifest, decoded = self.decode(target)
            self.assertEqual("Y", manifest["upAxis"]); self.assertEqual(.3048, manifest["metersPerUnit"])
            self.assertEqual(4, len(manifest["parts"])); self.assertEqual(8, sum(p["count"] for p in manifest["parts"]))
            direct = Usd.Stage.Open(str(target))
            for index, (before, after) in enumerate(zip(original["parts"], manifest["parts"])):
                self.assertEqual(before["name"], after["sourceName"])
                self.assertEqual(before["antialiased"], after["antialiased"])
                self.assertEqual(before["degree"], after["degree"])
                self.assertEqual(before["transform"][:9], after["transform"][:9])
                prim = direct.GetPrimAtPath(f"/GaussianScene/Part{index:06d}")
                self.assertEqual("ParticleField3DGaussianSplat", prim.GetTypeName())
                positions = prim.GetAttribute("positions").Get()
                self.assertEqual((0, 0, 0), tuple(positions[0]))
                self.assertGreater(abs(positions[1][0]), .01)  # no float collapse near 1e9
                standard_transform = bridge.affine_values(UsdGeom.XformCache().GetLocalToWorldTransform(prim), "test")
                for i, (a, b) in enumerate(zip(read_records(transport, before), read_records(decoded, after))):
                    for old, new in zip(world_point(before["transform"], a[:3]), world_point(after["transform"], b[:3])):
                        self.assertAlmostEqual(old, new, delta=2e-6)
                    for old, new in zip(world_point(before["transform"], a[:3]), world_point(standard_transform, positions[i])):
                        self.assertAlmostEqual(old, new, delta=2e-6)
                    for old, new in zip(covariance(before["transform"], a), covariance(after["transform"], b)):
                        self.assertAlmostEqual(old, new, delta=max(abs(old) * 2e-7, 1e-20))
                    self.assertEqual(a[6:10], b[6:10]); self.assertEqual(a[10], b[10]); self.assertEqual(a[11:], b[11:])

    def test_origin_hint_is_compatible_with_external_standard_position_and_transform_edits(self):
        transport = self.directory / "transport"; transport.mkdir(); export_fixture(transport)
        output = self.directory / "export"; output.mkdir(); bridge.export(transport / "export.json", output, "usda")
        path = output / "result.usda"
        edited = Usd.Stage.Open(str(path)); prim = edited.GetPrimAtPath("/GaussianScene/Part000003")
        positions = prim.GetAttribute("positions").Get(); positions[1] += Gf.Vec3f(.25, -.5, .125)
        prim.GetAttribute("positions").Set(positions)
        matrix = Gf.Matrix4d(-1, .75, 0, 0, .25, 2, -.5, 0, 0, .25, 3, 0, 10, 20, 30, 1)
        prim.GetAttribute("xformOp:transform").Set(matrix); edited.GetRootLayer().Save()
        manifest, decoded = self.decode(path)
        part = manifest["parts"][3]
        rows = read_records(decoded, part)
        native = bridge.affine_values(matrix, "edited")
        for row, point in zip(rows, positions):
            for actual, expected in zip(world_point(part["transform"], row[:3]), world_point(native, point)):
                self.assertAlmostEqual(actual, expected, delta=2e-6)
        self.assertEqual(native[:9], part["transform"][:9])  # same local directional/SH frame

    def test_export_rejects_truncated_and_trailing_payload_without_publishing_output(self):
        for extra in (b"extra", None):
            transport = self.directory / ("transport-" + str(extra)); transport.mkdir()
            manifest = export_fixture(transport); payload = transport / manifest["parts"][0]["file"]
            data = payload.read_bytes(); payload.write_bytes(data + extra if extra is not None else data[:-1])
            output = transport / "output"; output.mkdir()
            with self.assertRaisesRegex(bridge.UnsupportedUsd, "payload length"):
                bridge.export(transport / "export.json", output, "usdz")
            self.assertFalse((output / "result.usdz").exists())

    def test_export_rejects_scale_underflow_and_nonfinite_origin(self):
        transport = self.directory / "transport"; transport.mkdir(); manifest = export_fixture(transport)
        payload = transport / manifest["parts"][0]["file"]
        data = bytearray(payload.read_bytes()); offset = data.index(b"end_header\n") + len(b"end_header\n")
        struct.pack_into("<d", data, offset + 3 * 8, -1000); payload.write_bytes(data)
        output = self.directory / "export"; output.mkdir()
        with self.assertRaisesRegex(bridge.UnsupportedUsd, "float32"):
            bridge.export(transport / "export.json", output, "usd")
        manifest["parts"][0]["origin"][0] = float("inf")
        (transport / "export.json").write_text(json.dumps(manifest))
        with self.assertRaisesRegex(bridge.UnsupportedUsd, "non-finite"):
            bridge.export(transport / "export.json", output, "usd")

    def test_import_rejects_malformed_neutral_metadata_instead_of_moving_geometry(self):
        for key, value in (("localOrigin", Gf.Vec3d(float("nan"), 0, 0)), ("localOrigin", Gf.Vec2d(1, 2)), ("localOrigin", "123"), ("localOrigin", 123), ("antialiased", "true"), ("sourceName", 12)):
            path = self.directory / ("bad-metadata-" + str(self.sequence) + ".usda")
            scene = stage(path); splat = gs(scene, "/Splat")
            splat.GetPrim().SetCustomDataByKey("gaussianSplatting:" + key, value); scene.GetRootLayer().Save()
            with self.assertRaises(bridge.UnsupportedUsd):
                self.decode(path)

    def test_script_cli_reports_errors_without_traceback(self):
        output = self.directory / "cli"
        result = subprocess.run([sys.executable, "-I", str(ROOT / "scripts/usd_bridge.py"), "--input", str(self.directory / "missing.usdz"), "--output", str(output)], capture_output=True, text=True)
        self.assertEqual(1, result.returncode)
        self.assertIn("does not exist", json.loads((output / "error.json").read_text())["message"])
        self.assertNotIn("Traceback", result.stderr)


if __name__ == "__main__":
    if "--write-fixtures" in sys.argv:
        fixtures(ROOT / "fixtures")
    else:
        unittest.main()
