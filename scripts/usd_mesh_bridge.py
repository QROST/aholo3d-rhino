#!/usr/bin/env python3
"""Convert the private static mesh transport to USD using pinned OpenUSD, without cloud calls."""
import argparse
import array
import hashlib
import json
import math
from pathlib import Path
import re
import sys


def require(ok, message):
    if not ok:
        raise ValueError(message)


def numbers(value, count):
    require(isinstance(value, list) and len(value) == count, 'Invalid mesh numeric tuple.')
    require(all(type(v) in (int, float) and math.isfinite(v) for v in value), 'Non-finite mesh values.')
    return value


def read_array(stream, code, count):
    values = array.array(code)
    require(values.itemsize == 4, 'Unsupported native transport scalar size.')
    values.fromfile(stream, count)
    if sys.byteorder != 'little':
        values.byteswap()
    if code == 'f':
        require(all(math.isfinite(v) for v in values), 'Non-finite mesh attribute.')
    return values


def vectors(values, components, constructor):
    return constructor.FromBuffer(memoryview(values).cast('B').cast('f', shape=(len(values) // components, components)))


def texture_file(output, assets, texture):
    name = texture['file']
    require(isinstance(name, str) and re.fullmatch(re.escape(assets) + r'/[a-f0-9]{64}\.(png|jpg)', name), 'Invalid mesh image path.')
    path = output / name
    require(path.is_file() and not path.is_symlink(), 'Missing mesh image snapshot.')
    require(path.parent.resolve() == (output / assets).resolve() and not path.parent.is_symlink(), 'Mesh image path escapes workspace.')
    with path.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    require(digest == path.stem, 'Mesh image hash does not match its filename.')
    return name


def create_material(stage, path, part, output, assets, Gf, Sdf, UsdShade):
    material = UsdShade.Material.Define(stage, path)
    shader = UsdShade.Shader.Define(stage, path + '/PreviewSurface')
    shader.CreateIdAttr('UsdPreviewSurface')
    color = numbers(part['color'], 4)
    require(all(0 <= x <= 1 for x in color), 'Invalid base color factor.')
    for field in ('metallic', 'roughness'):
        require(type(part[field]) in (int, float) and math.isfinite(part[field]) and 0 <= part[field] <= 1, 'Invalid material factor.')
        shader.CreateInput(field, Sdf.ValueTypeNames.Float).Set(part[field])
    shader.CreateInput('diffuseColor', Sdf.ValueTypeNames.Color3f).Set(Gf.Vec3f(*color[:3]))
    shader.CreateInput('opacity', Sdf.ValueTypeNames.Float).Set(color[3])
    shader.CreateInput('opacityThreshold', Sdf.ValueTypeNames.Float).Set(0)
    shader.CreateInput('useSpecularWorkflow', Sdf.ValueTypeNames.Int).Set(0)
    shader.CreateOutput('surface', Sdf.ValueTypeNames.Token)
    material.CreateSurfaceOutput().ConnectToSource(shader.ConnectableAPI(), 'surface')
    texture = part.get('texture')
    if texture is not None:
        require(part['uv'] and type(texture['useAlpha']) is bool, 'Textured meshes require UVs and an alpha policy.')
        image = texture_file(output, assets, texture)
        reader = UsdShade.Shader.Define(stage, path + '/UV')
        reader.CreateIdAttr('UsdPrimvarReader_float2')
        reader.CreateInput('varname', Sdf.ValueTypeNames.Token).Set('st')
        reader.CreateOutput('result', Sdf.ValueTypeNames.Float2)
        sampler = UsdShade.Shader.Define(stage, path + '/Bitmap')
        sampler.CreateIdAttr('UsdUVTexture')
        sampler.CreateInput('file', Sdf.ValueTypeNames.Asset).Set(Sdf.AssetPath(image))
        sampler.CreateInput('sourceColorSpace', Sdf.ValueTypeNames.Token).Set('sRGB')
        sampler.CreateInput('st', Sdf.ValueTypeNames.Float2).ConnectToSource(reader.ConnectableAPI(), 'result')
        for axis in ('S', 'T'):
            wrap = texture['wrap' + axis]
            require(wrap in (10497, 33071), 'Unsupported texture wrapping.')
            sampler.CreateInput('wrap' + axis, Sdf.ValueTypeNames.Token).Set('repeat' if wrap == 10497 else 'clamp')
        # USD Preview Surface has no standard nearest/linear filter inputs. Retain source intent as metadata.
        for field in ('minFilter', 'magFilter'):
            require(texture[field] in (9728, 9729), 'Unsupported source texture filter.')
            sampler.GetPrim().SetCustomDataByKey('sourceSampler:' + field, texture[field])
        use_alpha = texture['useAlpha']
        sampler.CreateInput('scale', Sdf.ValueTypeNames.Float4).Set(Gf.Vec4f(*color[:3], color[3] if use_alpha else 0))
        sampler.CreateInput('bias', Sdf.ValueTypeNames.Float4).Set(Gf.Vec4f(0, 0, 0, 0 if use_alpha else color[3]))
        sampler.CreateOutput('rgb', Sdf.ValueTypeNames.Float3)
        sampler.CreateOutput('a', Sdf.ValueTypeNames.Float)
        shader.GetInput('diffuseColor').ConnectToSource(sampler.ConnectableAPI(), 'rgb')
        shader.GetInput('opacity').ConnectToSource(sampler.ConnectableAPI(), 'a')
    return material


def export(input_path, output, fmt):
    from pxr import Ar, Gf, Sdf, Usd, UsdGeom, UsdShade, UsdUtils, Vt
    require(Usd.GetVersion() == (0, 26, 11), 'Mesh exchange requires usd-core==26.11.')
    data = json.loads(input_path.read_text(encoding='utf-8'))
    require(data['protocol'] == 1 and data['openUsdVersion'] == '26.11', 'Unsupported mesh transport protocol.')
    assets = data['assets']
    require(isinstance(assets, str) and re.fullmatch(r'usd-assets-[a-f0-9]{32}', assets), 'Invalid asset directory.')
    require(fmt in ('usd', 'usda', 'usdc', 'usdz'), 'Invalid USD export format.')
    require(isinstance(data['parts'], list) and data['parts'], 'No mesh parts to export.')
    layer_path = output / ('scene.usdc' if fmt == 'usdz' else 'result.' + fmt)
    stage = Usd.Stage.CreateNew(str(layer_path))
    UsdGeom.SetStageUpAxis(stage, UsdGeom.Tokens.z)
    UsdGeom.SetStageMetersPerUnit(stage, .001)
    root = UsdGeom.Xform.Define(stage, '/Scene')
    stage.SetDefaultPrim(root.GetPrim())
    UsdGeom.Scope.Define(stage, '/Scene/Materials')
    files = set()
    for index, part in enumerate(data['parts']):
        file = part['file']
        require(isinstance(file, str) and re.fullmatch(r'mesh-[0-9]{6,}\.bin', file) and file not in files, 'Invalid or duplicate mesh payload.')
        files.add(file)
        vertices, indices = part['vertices'], part['indices']
        require(type(vertices) is int and vertices > 0 and type(indices) is int and indices > 0 and indices % 3 == 0, 'Invalid mesh counts.')
        require(type(part['normals']) is bool and type(part['uv']) is bool, 'Invalid mesh attribute flags.')
        expected = 4 * (vertices * 3 + indices + (vertices * 3 if part['normals'] else 0) + (vertices * 2 if part['uv'] else 0))
        binary = output / file
        require(not binary.is_symlink() and binary.is_file() and binary.stat().st_size == expected, 'Incomplete mesh payload.')
        with binary.open('rb') as stream:
            positions = read_array(stream, 'f', vertices * 3)
            triangles = read_array(stream, 'i', indices)
            require(all(0 <= v < vertices for v in triangles), 'Mesh triangle index is out of range.')
            normals = read_array(stream, 'f', vertices * 3) if part['normals'] else None
            uv = read_array(stream, 'f', vertices * 2) if part['uv'] else None
        mesh = UsdGeom.Mesh.Define(stage, f'/Scene/Mesh_{index:06d}')
        require(isinstance(part['name'], str), 'Invalid mesh display name.')
        mesh.GetPrim().SetDisplayName(part['name'])
        mesh.CreateSubdivisionSchemeAttr(UsdGeom.Tokens.none)
        mesh.CreateOrientationAttr(UsdGeom.Tokens.rightHanded)
        mesh.CreateDoubleSidedAttr(True)
        points = vectors(positions, 3, Vt.Vec3fArray)
        mesh.CreatePointsAttr(points)
        mesh.CreateExtentAttr(UsdGeom.PointBased.ComputeExtent(points))
        mesh.CreateFaceVertexCountsAttr(Vt.IntArray.FromBuffer(array.array('i', [3]) * (indices // 3)))
        mesh.CreateFaceVertexIndicesAttr(Vt.IntArray.FromBuffer(triangles))
        mesh.AddTranslateOp(UsdGeom.XformOp.PrecisionDouble).Set(Gf.Vec3d(*numbers(part['origin'], 3)))
        if normals is not None:
            for i in range(0, len(normals), 3):
                length = math.hypot(*normals[i:i+3])
                require(math.isfinite(length) and abs(length - 1) < 1e-5, 'Invalid mesh normal.')
            mesh.CreateNormalsAttr(vectors(normals, 3, Vt.Vec3fArray))
            mesh.SetNormalsInterpolation(UsdGeom.Tokens.vertex)
        if uv is not None:
            for i in range(1, len(uv), 2):
                uv[i] = 1 - uv[i]
            UsdGeom.PrimvarsAPI(mesh).CreatePrimvar('st', Sdf.ValueTypeNames.TexCoord2fArray, UsdGeom.Tokens.vertex).Set(vectors(uv, 2, Vt.Vec2fArray))
        material = create_material(stage, f'/Scene/Materials/Material_{index:06d}', part, output, assets, Gf, Sdf, UsdShade)
        UsdShade.MaterialBindingAPI.Apply(mesh.GetPrim()).Bind(material)
    stage.GetRootLayer().Save()
    stage = None
    if fmt == 'usdz':
        require(UsdUtils.CreateNewUsdzPackage(Sdf.AssetPath(str(layer_path)), str(output / 'result.usdz')), 'OpenUSD packaging failed.')
    result = output / ('result.' + fmt)
    verified = Usd.Stage.Open(str(result))
    require(verified and not verified.GetCompositionErrors(), 'The exported USD stage could not be reopened.')
    layers, images, missing = UsdUtils.ComputeAllDependencies(Sdf.AssetPath(str(result)))
    require(not missing, 'Exported USD has unresolved dependencies.')
    for identifier in [str(x.realPath) for x in layers if x.realPath] + [str(x) for x in images]:
        physical = Ar.SplitPackageRelativePathOuter(identifier)[0] if Ar.IsPackageRelativePath(identifier) else identifier
        resolved = Path(physical).resolve()
        require('://' not in physical and (resolved == result.resolve() if fmt == 'usdz' else resolved.is_relative_to(output.resolve())), 'Exported USD dependency escapes its output bundle.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--format', choices=('usd', 'usda', 'usdc', 'usdz'))
    parser.add_argument('--export', action='store_true')
    args = parser.parse_args()
    try:
        if args.export:
            export(args.input, args.output, args.format)
        else:
            # Isolated Python excludes script-directory imports; load only our installed sibling.
            import importlib.util
            spec = importlib.util.spec_from_file_location('aholo_mesh_reader', Path(__file__).with_name('usd_mesh_reader.py'))
            reader = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(reader)
            reader.convert(args.input, args.output)
    except Exception as error:
        try:
            (args.output / 'error.json').write_text(json.dumps({'message': str(error)}, ensure_ascii=False), encoding='utf-8')
        except OSError:
            print(str(error), file=sys.stderr)
        raise SystemExit(2)
