"""Strict local static mesh extraction. Loaded only from the installed bridge directory."""
import array
import hashlib
import json
import math
import shutil
from pathlib import Path
import sys


def require(value, message):
    if not value:
        raise ValueError(message)


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def values(value, length):
    result = list(value)
    require(len(result) == length and all(math.isfinite(float(x)) for x in result), 'Invalid mesh numeric tuple.')
    return [float(x) for x in result]


def dependencies(source, Ar, Sdf, UsdUtils):
    source = source.resolve()
    layers, images, missing = UsdUtils.ComputeAllDependencies(Sdf.AssetPath(str(source)))
    require(not missing, 'USD contains unresolved dependencies.')
    files = {source}
    for identifier in [str(x.realPath) for x in layers if x.realPath] + [str(x) for x in images]:
        physical = Ar.SplitPackageRelativePathOuter(identifier)[0] if Ar.IsPackageRelativePath(identifier) else identifier
        require('://' not in physical, 'USD mesh import supports local dependencies only.')
        path = Path(physical).resolve()
        require(path.is_file(), 'USD dependency is not a local file.')
        require(source.suffix.lower() != '.usdz' or path == source, 'USDZ dependency escapes its package.')
        files.add(path)
    return files


def freeze_source(source, output, Ar, Sdf, UsdUtils):
    """Parse only private copies; source hashes identify those exact bytes, not a before/after race."""
    snapshot = output / 'source-snapshot'
    snapshot.mkdir()
    if source.suffix.lower() == '.usdz':
        copied = snapshot / 'source.usdz'
        shutil.copyfile(source, copied)
        sha = digest(copied)
        dependencies(copied, Ar, Sdf, UsdUtils)  # Requires every package dependency to remain enclosed.
        return copied, sha, [sha]
    files = dependencies(source, Ar, Sdf, UsdUtils)
    layers, _, missing = UsdUtils.ComputeAllDependencies(Sdf.AssetPath(str(source)))
    require(not missing, 'USD dependencies changed during discovery.')
    require(all(not Ar.IsPackageRelativePath(str(layer.realPath)) for layer in layers),
            'A plain USD referencing an external USD package is not supported yet; flatten or package the scene first.')
    layer_paths = {Path(layer.realPath).resolve() for layer in layers if layer.realPath}
    copied, hashes = {}, {}
    for index, original in enumerate(sorted(files)):
        target = snapshot / (f'asset-{index:06d}' + original.suffix.lower())
        shutil.copyfile(original, target)
        copied[original], hashes[original] = target, digest(target)
    # Only path opinions change in working layers. Hashes retain the exact original copied bytes.
    for original in sorted(layer_paths):
        require(original in copied, 'USD dependency set changed during snapshot creation.')
        layer = Sdf.Layer.FindOrOpen(str(copied[original]))
        require(bool(layer), 'A copied USD layer could not be read.')
        def relocate(value):
            if not value: return value
            require('://' not in value and not Ar.IsPackageRelativePath(value), 'Unsupported external USD asset identifier.')
            authored = Path(value)
            candidate = (authored if authored.is_absolute() else original.parent / authored).resolve()
            if candidate not in copied:
                identifier = Ar.GetResolver().CreateIdentifier(value, Ar.ResolvedPath(str(original)))
                resolved = str(Ar.GetResolver().Resolve(identifier))
                require(bool(resolved), 'A USD dependency was not captured in the private snapshot.')
                candidate = Path(resolved).resolve()
            require(candidate in copied, 'A USD dependency changed or was not captured in the private snapshot.')
            return str(copied[candidate])
        UsdUtils.ModifyAssetPaths(layer, relocate, True)
        require(layer.Save(), 'Could not localize a private USD layer.')
    root = copied[source]
    enclosed = dependencies(root, Ar, Sdf, UsdUtils)
    require(all(path.is_relative_to(snapshot.resolve()) for path in enclosed), 'USD dependency escaped its private snapshot.')
    return root, hashes[source], sorted(hashes.values())


def constant(shader, name, default):
    value = shader.GetInput(name)
    if not value:
        return default
    require(not value.GetAttr().HasAuthoredConnections(), 'Unsupported connected material input: ' + name)
    result = value.Get()
    return default if result is None else result


def source_input(value, UsdShade):
    if not value or not value.GetAttr().HasAuthoredConnections():
        return None
    connections = value.GetAttr().GetConnections()
    require(len(connections) == 1, 'Multiple material connections are unsupported.')
    result = value.GetConnectedSource()
    require(result and result[2] == UsdShade.AttributeType.Output, 'Unresolved or interface material connections are unsupported.')
    shader = UsdShade.Shader(result[0].GetPrim())
    require(bool(shader), 'Material connection must point directly to a shader.')
    return shader, str(result[1])


def material_for(prim, output, Ar, Sdf, UsdGeom, UsdShade):
    material, _ = UsdShade.MaterialBindingAPI(prim).ComputeBoundMaterial()
    if not material:
        api = UsdGeom.PrimvarsAPI(prim)
        color, opacity = [.18, .18, .18], 1.0
        for name in ('displayColor', 'displayOpacity'):
            pv = api.FindPrimvarWithInheritance(name)
            if pv and pv.HasValue():
                data = pv.ComputeFlattened()
                require(pv.GetInterpolation() == 'constant' and len(data) == 1, 'Only constant display color/opacity is supported without a material.')
                if name == 'displayColor': color = values(data[0], 3)
                else: opacity = float(data[0])
        return dict(color=color + [opacity], metallic=0.0, roughness=.5, texture=None)
    for out in material.GetOutputs():
        require(str(out.GetAttr().GetName()) == 'outputs:surface' or not out.GetAttr().HasAuthoredConnections(), 'Non-universal, volume or displacement material output is unsupported.')
    terminal = source_input(material.GetSurfaceOutput(), UsdShade)
    require(terminal and terminal[0].GetIdAttr().Get() == 'UsdPreviewSurface' and terminal[1] == 'surface', 'A direct universal UsdPreviewSurface material is required.')
    shader = terminal[0]
    defaults = dict(useSpecularWorkflow=0, emissiveColor=(0.,0.,0.), clearcoat=0., clearcoatRoughness=.01,
                    opacityThreshold=0., ior=1.5, normal=(0.,0.,1.), displacement=0., occlusion=1., specularColor=(0.,0.,0.))
    for inp in shader.GetInputs():
        name = str(inp.GetBaseName())
        if name in ('diffuseColor', 'opacity', 'metallic', 'roughness'):
            continue
        require(name in defaults, 'Unsupported material input: ' + name)
        actual = constant(shader, name, defaults[name])
        expected = defaults[name]
        equal = all(abs(a-b) < 1e-7 for a,b in zip(values(actual,len(expected)),expected)) if isinstance(expected,tuple) else abs(float(actual)-expected) < 1e-7
        require(equal, 'Unsupported non-default material input: ' + name)
    color_source = source_input(shader.GetInput('diffuseColor'), UsdShade)
    opacity_source = source_input(shader.GetInput('opacity'), UsdShade)
    metallic, roughness = float(constant(shader,'metallic',0.)), float(constant(shader,'roughness',.5))
    if color_source is None:
        require(opacity_source is None, 'An independent opacity texture is unsupported.')
        return dict(color=values(constant(shader,'diffuseColor',(.18,.18,.18)),3)+[float(constant(shader,'opacity',1.))], metallic=metallic, roughness=roughness, texture=None)
    sampler, channel = color_source
    require(sampler.GetIdAttr().Get() == 'UsdUVTexture' and channel == 'rgb', 'Only direct UsdUVTexture RGB base color is supported.')
    allowed = {'file','st','sourceColorSpace','wrapS','wrapT','scale','bias','fallback'}
    require(all(str(x.GetBaseName()) in allowed for x in sampler.GetInputs()), 'Unsupported bitmap sampler inputs.')
    require(constant(sampler,'sourceColorSpace','auto') == 'sRGB', 'Texture color space must be explicitly sRGB.')
    uv_source = source_input(sampler.GetInput('st'), UsdShade)
    require(uv_source and uv_source[1] == 'result' and uv_source[0].GetIdAttr().Get() == 'UsdPrimvarReader_float2', 'Bitmap UVs require a direct float2 primvar reader.')
    reader = uv_source[0]
    require(constant(reader,'varname','') == 'st', 'Only the st UV set is supported.')
    require(all(str(x.GetBaseName()) in ('varname','fallback') for x in reader.GetInputs()), 'Unsupported UV reader input.')
    scale, bias = values(constant(sampler,'scale',(1.,1.,1.,1.)),4), values(constant(sampler,'bias',(0.,0.,0.,0.)),4)
    require(bias[:3] == [0.,0.,0.], 'Nonzero texture color bias is unsupported.')
    use_alpha = False
    if opacity_source is None:
        alpha = float(constant(shader,'opacity',1.))
    else:
        require(opacity_source[0].GetPath() == sampler.GetPath() and opacity_source[1] == 'a', 'Opacity must use the same bitmap alpha output.')
        if scale[3] == 0: alpha = bias[3]
        else:
            require(bias[3] == 0, 'Texture alpha bias is unsupported.')
            alpha, use_alpha = scale[3], True
    texture = dict(useAlpha=use_alpha)
    for axis in ('S','T'):
        wrap = str(constant(sampler,'wrap'+axis,'useMetadata'))
        require(wrap in ('repeat','clamp'), 'Texture wrap must explicitly be repeat or clamp.')
        texture['wrap'+axis] = 10497 if wrap == 'repeat' else 33071
    for name in ('minFilter','magFilter'):
        value = sampler.GetPrim().GetCustomDataByKey('sourceSampler:'+name)
        value = 9729 if value is None else value
        require(type(value) is int and value in (9728,9729), 'Unsupported source filter metadata.')
        texture[name] = value
    asset_path = constant(sampler,'file',None)
    require(isinstance(asset_path,Sdf.AssetPath) and asset_path.resolvedPath, 'Bitmap is unresolved.')
    asset = Ar.GetResolver().OpenAsset(Ar.ResolvedPath(asset_path.resolvedPath))
    require(asset is not None, 'Bitmap could not be opened.')
    encoded = bytes(asset.GetBuffer())
    suffix = '.png' if encoded.startswith(b'\x89PNG\r\n\x1a\n') else '.jpg' if encoded.startswith(b'\xff\xd8') else ''
    require(bool(suffix), 'Only PNG/JPEG base-color images are supported.')
    filename = hashlib.sha256(encoded).hexdigest()+suffix
    path = output/filename
    if not path.exists(): path.write_bytes(encoded)
    texture['file'] = filename
    return dict(color=scale[:3]+[alpha], metallic=metallic, roughness=roughness, texture=texture)


def attribute(prim, name, fallback_attr, fallback_interp, UsdGeom):
    pv = UsdGeom.PrimvarsAPI(prim).FindPrimvarWithInheritance(name)
    if pv and pv.HasValue():
        require(pv.GetElementSize() == 1, 'Primvar elementSize must be one.')
        require(pv.GetUnauthoredValuesIndex() < 0, 'Unauthored primvar entries are unsupported.')
        result = pv.ComputeFlattened()
        require(result is not None, 'Primvar indices are invalid.')
        return result, str(pv.GetInterpolation())
    if fallback_attr and fallback_attr.HasValue():
        return fallback_attr.Get(), str(fallback_interp)
    return None, None


def write_mesh(prim, output, index, cache, UsdGeom, UsdShade, Ar, Sdf):
    mesh = UsdGeom.Mesh(prim); label = str(prim.GetPath())
    require(mesh.GetSubdivisionSchemeAttr().Get() == 'none', label+': subdivision must be explicitly none; bake the evaluated surface first.')
    require(mesh.ComputeVisibility() != 'invisible' and mesh.ComputePurpose() in ('default','render'), label+': hidden, guide or proxy mesh cannot be silently imported.')
    require(not mesh.GetHoleIndicesAttr().Get(), label+': mesh holes must be baked first.')
    require(not UsdGeom.Subset.GetAllGeomSubsets(mesh), label+': face subsets are not yet supported; split material parts first.')
    points, counts, indices = mesh.GetPointsAttr().Get(), mesh.GetFaceVertexCountsAttr().Get(), mesh.GetFaceVertexIndicesAttr().Get()
    require(points is not None and len(points)>0 and counts is not None and len(counts)>0 and indices is not None, label+': empty mesh.')
    require(all(n in (3,4) for n in counts), label+': triangulate polygons with more than four corners first.')
    require(sum(counts) == len(indices) and all(0 <= i < len(points) for i in indices), label+': invalid face topology.')
    normals, ni = attribute(prim,'normals',mesh.GetNormalsAttr(),mesh.GetNormalsInterpolation(),UsdGeom)
    uv, ui = attribute(prim,'st',None,None,UsdGeom)
    for data,interp,width in ((normals,ni,3),(uv,ui,2)):
        if data is None: continue
        expected = {'constant':1,'uniform':len(counts),'vertex':len(points),'varying':len(points),'faceVarying':len(indices)}.get(interp)
        require(expected is not None and len(data)==expected, label+': invalid normal/UV interpolation or cardinality.')
        for item in data:
            value = values(item,width)
            if width == 3: require(math.hypot(*value)>0, label+': zero normal.')
    def attribute_index(interp,face,corner,vertex):
        return {'constant':0,'uniform':face,'vertex':vertex,'varying':vertex,'faceVarying':corner}.get(interp,-1)
    positions_out, normals_out, uv_out = array.array('f'),array.array('f'),array.array('f')
    indices_out, mapping = array.array('i'),{}
    corner = 0
    for face,count in enumerate(counts):
        for _ in range(count):
            vertex = indices[corner]; normal_index=attribute_index(ni,face,corner,vertex); uv_index=attribute_index(ui,face,corner,vertex)
            key=(vertex,normal_index,uv_index)
            if key not in mapping:
                mapping[key]=len(positions_out)//3;positions_out.extend(values(points[vertex],3))
                if normals is not None: normals_out.extend(values(normals[normal_index],3))
                if uv is not None: uv_out.extend(values(uv[uv_index],2))
            indices_out.append(mapping[key]);corner+=1
    matrix=cache.GetLocalToWorldTransform(prim)
    require(all(math.isfinite(float(matrix[r][c])) for r in range(4) for c in range(4)), label+': non-finite transform.')
    require(all(matrix[r][3]==0 for r in range(3)) and matrix[3][3]==1 and matrix.GetDeterminant()!=0, label+': non-affine or singular transform.')
    transform=[float(matrix[c][r]) for r in range(3) for c in range(3)]+[float(matrix[3][c]) for c in range(3)]
    orientation=str(mesh.GetOrientationAttr().Get());require(orientation in ('leftHanded','rightHanded'),label+': invalid orientation.')
    material=material_for(prim,output,Ar,Sdf,UsdGeom,UsdShade)
    require(material['texture'] is None or uv is not None,label+': textured mesh is missing st UVs.')
    require(all(math.isfinite(v) and 0<=v<=1 for v in material['color']+[material['metallic'],material['roughness']]),label+': invalid material factors.')
    filename=f'mesh-{index:06d}.bin'
    with (output/filename).open('wb') as stream:
        for data in (positions_out,array.array('i',counts),indices_out,normals_out,uv_out):
            require(data.itemsize==4,'Unsupported platform scalar size.')
            if sys.byteorder!='little': data.byteswap()
            data.tofile(stream)
    return dict(primPath=label,name=prim.GetDisplayName() or str(prim.GetName()),file=filename,vertices=len(positions_out)//3,
                faces=len(counts),indices=len(indices_out),normals=normals is not None,uv=uv is not None,
                transform=transform,leftHanded=orientation=='leftHanded',doubleSided=bool(mesh.GetDoubleSidedAttr().Get()),**material)


def validate_prim(prim, allowed=('', 'Xform', 'Scope', 'Mesh', 'Material', 'Shader', 'NodeGraph')):
    label=str(prim.GetPath());typename=str(prim.GetTypeName())
    require(typename in allowed,label+': unsupported scene primitive '+typename+'; no geometry was silently omitted.')
    if typename != 'ParticleField3DGaussianSplat':
        suspicious = bool(prim.GetAttribute('radiance:sphericalHarmonicsCoefficients')) or bool(prim.GetAttribute('radiance:sphericalHarmonicsCoefficientsh'))
        suspicious = suspicious or any(str(api).startswith('ParticleField') or any(word in str(api).lower() for word in ('gaussian','splat','nurec')) for api in prim.GetAppliedSchemas())
        require(not suspicious,label+': unsupported Gaussian schema on a non-Gaussian primitive; no data was silently omitted.')
    require(not prim.HasMetadata('clips'),label+': value clips must be baked first.')
    require(not any('Skel' in str(api) for api in prim.GetAppliedSchemas()),label+': skinning must be baked first.')
    for attr in prim.GetAttributes():
        require(attr.GetNumTimeSamples()==0,label+': animated USD must be baked at the intended time first.')
        if typename not in ('Material','Shader','NodeGraph'):
            require(not attr.HasAuthoredConnections(),label+': connected geometry attributes are unsupported.')
        if str(attr.GetName()) in ('velocities','accelerations'):
            require(not attr.Get(),label+': motion attributes must be baked first.')


def convert(input_path, output):
    from pxr import Ar,Sdf,Usd,UsdGeom,UsdShade,UsdUtils
    require(Usd.GetVersion()==(0,26,11),'Mesh exchange requires usd-core==26.11.')
    source=input_path.resolve();require(source.is_file(),'USD source is missing.')
    captured, source_hash, dependency_hashes = freeze_source(source,output,Ar,Sdf,UsdUtils)
    stage=Usd.Stage.Open(str(captured),load=Usd.Stage.LoadAll);require(bool(stage),'USD could not be opened.')
    require(not stage.GetCompositionErrors(),'USD has composition errors.')
    axis=str(UsdGeom.GetStageUpAxis(stage));units=float(UsdGeom.GetStageMetersPerUnit(stage))
    require(axis in ('Y','Z') and math.isfinite(units) and units>0,'Invalid USD axis or units.')
    cache=UsdGeom.XformCache(Usd.TimeCode.Default());parts=[];warnings=[]
    for prim in Usd.PrimRange.Stage(stage,Usd.TraverseInstanceProxies()):
        validate_prim(prim)
        if prim.IsA(UsdGeom.Mesh): parts.append(write_mesh(prim,output,len(parts),cache,UsdGeom,UsdShade,Ar,Sdf))
    require(parts,'USD contains no static mesh geometry.')
    if not stage.HasAuthoredMetadata('metersPerUnit'): warnings.append('USD units were unauthored; the OpenUSD 0.01 metre fallback was used.')
    if not stage.HasAuthoredMetadata('upAxis'): warnings.append('USD upAxis was unauthored; the OpenUSD fallback axis was used.')
    warnings.append('Mesh import flattens instance hierarchy. Display is two-sided; Rhino materials are a preview of the supported USD shading subset.')
    if any(p['texture'] for p in parts): warnings.append('Texture filter settings are source metadata; final sampling is controlled by the viewer.')
    manifest=dict(protocol=1,openUsdVersion='26.11',upAxis=axis,metersPerUnit=units,sourceSha256=source_hash,dependencyHashes=dependency_hashes,parts=parts,warnings=warnings)
    (output/'mesh-import.json').write_text(json.dumps(manifest,ensure_ascii=False,allow_nan=False,separators=(',',':')),encoding='utf-8')
