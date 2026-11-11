"""Strict local static mesh extraction. Loaded only from the installed bridge directory."""
import array
import hashlib
from fractions import Fraction
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


def material_for(prim, output, Ar, Sdf, UsdGeom, UsdShade, geometry=None):
    # Prefer the viewer's preview purpose (which includes allPurpose fallback), then full-only shading.
    binding_api = UsdShade.MaterialBindingAPI(prim)
    material, binding = binding_api.ComputeBoundMaterial('preview')
    require(material or not binding or not binding.GetTargets(), 'Unresolved material binding.')
    if not material:
        material, binding = binding_api.ComputeBoundMaterial('full')
        require(material or not binding or not binding.GetTargets(), 'Unresolved material binding.')
    if not material:
        api = UsdGeom.PrimvarsAPI(geometry if geometry is not None else prim)
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


def orient(a, b, c):
    """One shared orientation sign; exact binary-rational fallback near cancellation."""
    left = (a[0]-c[0])*(b[1]-c[1]); right = (a[1]-c[1])*(b[0]-c[0])
    determinant = left-right
    normal_products = (left == 0 or abs(left) >= sys.float_info.min) and (right == 0 or abs(right) >= sys.float_info.min)
    if normal_products and abs(determinant) > (abs(left)+abs(right))*8*sys.float_info.epsilon:
        return 1 if determinant > 0 else -1
    ax, ay, bx, by, cx, cy = map(Fraction, (a[0],a[1],b[0],b[1],c[0],c[1]))
    exact = (ax-cx)*(by-cy)-(ay-cy)*(bx-cx)
    return (exact > 0)-(exact < 0)


def between(p, a, b):
    return min(a[0],b[0]) <= p[0] <= max(a[0],b[0]) and min(a[1],b[1]) <= p[1] <= max(a[1],b[1])


def intersects(a, b, c, d):
    if max(a[0],b[0]) < min(c[0],d[0]) or max(c[0],d[0]) < min(a[0],b[0]) or max(a[1],b[1]) < min(c[1],d[1]) or max(c[1],d[1]) < min(a[1],b[1]):
        return False
    ab_c, ab_d, cd_a, cd_b = orient(a,b,c), orient(a,b,d), orient(c,d,a), orient(c,d,b)
    return ((ab_c == 0 and between(c,a,b)) or (ab_d == 0 and between(d,a,b)) or
            (cd_a == 0 and between(a,c,d)) or (cd_b == 0 and between(b,c,d)) or
            (ab_c*ab_d < 0 and cd_a*cd_b < 0))


def triangulate_polygon(points, preserve_convex_quad=False):
    """Return source-corner triples for a simple planar n-gon; never invent or discard a corner."""
    points = [values(p,3) for p in points]; count = len(points)
    require(count >= 3, 'A polygon needs at least three corners.')
    local = [[p[i]-points[0][i] for i in range(3)] for p in points]
    extent = max(abs(v) for p in local for v in p)
    require(math.isfinite(extent) and extent > 0, 'Degenerate polygon extent.')
    local = [[v/extent for v in p] for p in local]
    a = max(local, key=lambda p: sum(v*v for v in p))
    crosses = [(a[1]*p[2]-a[2]*p[1],a[2]*p[0]-a[0]*p[2],a[0]*p[1]-a[1]*p[0]) for p in local]
    normal = max(crosses,key=lambda p:max(abs(v) for v in p)); largest = max(abs(v) for v in normal)
    if largest <= 64*sys.float_info.epsilon:
        # Subtracting a small origin from a huge point can erase the only non-collinear component.
        exact = [[Fraction(p[i])-Fraction(points[0][i]) for i in range(3)] for p in points]
        a = max(exact,key=lambda p:max(abs(v) for v in p))
        crosses = [(a[1]*p[2]-a[2]*p[1],a[2]*p[0]-a[0]*p[2],a[0]*p[1]-a[1]*p[0]) for p in exact]
        normal = max(crosses,key=lambda p:max(abs(v) for v in p));largest=max(abs(v) for v in normal)
        require(largest > 0, 'Collinear polygon has no area.')
        normal = [float(v/largest) for v in normal];largest=1
    require(largest > 0, 'Collinear polygon has no area.')
    normal = [v/largest for v in normal]; length = math.hypot(*normal)
    require(all(abs(sum(normal[i]*p[i] for i in range(3))) <= 1e-6*length for p in local), 'Nonplanar polygons must be tessellated before import.')
    axis = max(range(3),key=lambda i:abs(normal[i])); axes = [i for i in range(3) if i != axis]
    projected = [(p[axes[0]],p[axes[1]]) for p in points]
    require(len(set(projected)) == count, 'Polygon corners repeat or collapse in projection.')
    for i, point in enumerate(projected):
        previous, following = projected[(i-1)%count], projected[(i+1)%count]
        require(orient(previous,point,following) != 0 or between(point,previous,following), 'Polygon boundary doubles back.')
        for j in range(i+1,count):
            if j == i+1 or (i == 0 and j == count-1): continue
            require(not intersects(point,following,projected[j],projected[(j+1)%count]), 'Polygon boundary intersects or touches itself.')
    extreme = min(range(count),key=lambda i:projected[i])
    winding = orient(projected[(extreme-1)%count],projected[extreme],projected[(extreme+1)%count])
    require(winding != 0, 'Polygon has no stable winding.')
    if preserve_convex_quad and count == 4 and all(orient(projected[i-1],projected[i],projected[(i+1)%count]) == winding for i in range(count)):
        return None
    remaining = list(range(count)); result = []
    while len(remaining) > 3:
        for position, b in enumerate(remaining):
            a, c = remaining[position-1], remaining[(position+1)%len(remaining)]
            if orient(projected[a],projected[b],projected[c]) != winding: continue
            blocked = any(all(winding*orient(projected[x],projected[y],projected[p]) >= 0 for x,y in ((a,b),(b,c),(c,a)))
                          for p in remaining if p not in (a,b,c))
            if blocked: continue
            result.append((a,b,c));del remaining[position];break
        else: raise ValueError('Polygon cannot be triangulated without losing source corners.')
    require(orient(*(projected[i] for i in remaining)) == winding, 'Polygon triangulation ended with a degenerate face.')
    result.append(tuple(remaining));return result


def coplanar_quad(points):
    """Exact coplanarity near zero; a tiny real warp must not be rejected as a projected crossing."""
    if any(all(p[i] == points[0][i] for p in points[1:]) for i in range(3)):
        return True
    a,b,c = [[p[i]-points[0][i] for i in range(3)] for p in points[1:]]
    terms = [a[0]*b[1]*c[2],a[1]*b[2]*c[0],a[2]*b[0]*c[1],-a[2]*b[1]*c[0],-a[1]*b[0]*c[2],-a[0]*b[2]*c[1]]
    if all(math.isfinite(v) and (v == 0 or abs(v) >= sys.float_info.min) for v in terms):
        try:
            determinant = math.fsum(terms)
            if abs(determinant) > math.fsum(abs(v) for v in terms)*64*sys.float_info.epsilon:
                return False
        except OverflowError:
            pass  # Finite individual terms can still overflow their sum; exact arithmetic remains available.
    a,b,c = [[Fraction(p[i])-Fraction(points[0][i]) for i in range(3)] for p in points[1:]]
    return a[0]*(b[1]*c[2]-b[2]*c[1])+a[1]*(b[2]*c[0]-b[0]*c[2])+a[2]*(b[0]*c[1]-b[1]*c[0]) == 0


def quad_topology(points):
    """Retain native convex/warped quads; tessellate only a validated planar concave/collinear boundary."""
    points = [values(p,3) for p in points]
    require(len(points) == 4 and len({tuple(p) for p in points}) == 4, 'Quad corners must be distinct.')
    if not coplanar_quad(points):
        return None
    return triangulate_polygon(points,preserve_convex_quad=True)


def facet_normal(points, left_handed=False):
    """Original-face area vector, normalized after summation; never smooth across faces."""
    points = [values(p,3) for p in points]
    require(len(points) >= 3, 'A face needs at least three corners.')
    local = [[p[i]-points[0][i] for i in range(3)] for p in points]
    extent = max(abs(v) for p in local for v in p)
    require(math.isfinite(extent) and extent > 0, 'Cannot generate a normal for a degenerate face.')
    local = [[v/extent for v in p] for p in local]
    sums, errors = [], []
    for x,y in ((1,2),(2,0),(0,1)):
        terms = [term for a,b in zip(local[1:-1],local[2:]) for term in (a[x]*b[y],-a[y]*b[x])]
        sums.append(math.fsum(terms));errors.append(math.fsum(abs(t) for t in terms)*16*sys.float_info.epsilon)
    if max(errors) >= max(abs(v) for v in sums) * 2**-24:
        # Resolve cancellation before its directional error exceeds stored float-normal precision.
        # Exact input arithmetic precedes normalization; there is no fixed face-size cap.
        exact = [[Fraction(p[i])-Fraction(points[0][i]) for i in range(3)] for p in points]
        sums = [sum((a[x]*b[y]-a[y]*b[x] for a,b in zip(exact[1:-1],exact[2:])),Fraction()) for x,y in ((1,2),(2,0),(0,1))]
        largest = max(abs(v) for v in sums)
        require(largest > 0, 'Cannot generate a normal for a zero-area face.')
        sums = [float(v/largest) for v in sums]
    length = math.hypot(*sums)
    require(math.isfinite(length) and length > 0, 'Cannot generate a stable face normal.')
    sign = -1 if left_handed else 1
    return [sign*v/length for v in sums]


def material_groups(prim, face_count, UsdGeom, UsdShade):
    mesh = UsdGeom.Mesh(prim); subsets = sorted(UsdGeom.Subset.GetAllGeomSubsets(mesh),key=lambda s:str(s.GetPath()))
    if not subsets: return [(prim, range(face_count))]
    family = UsdShade.MaterialBindingAPI(prim).GetMaterialBindSubsetsFamilyType()
    require(family in ('partition','nonOverlapping'), str(prim.GetPath())+': materialBind family must be partition or nonOverlapping.')
    owners = [None]*face_count; groups = []
    for subset in subsets:
        validate_prim(subset.GetPrim())
        indices = subset.GetIndicesAttr().Get()
        require(indices is not None, str(subset.GetPath())+': missing subset indices.')
        for face in indices:
            require(0 <= face < face_count and owners[face] is None, str(subset.GetPath())+': overlapping, duplicate or out-of-range material face.')
            owners[face] = str(subset.GetPath())
        if len(indices): groups.append((subset.GetPrim(),sorted(indices)))
    remaining = [i for i,owner in enumerate(owners) if owner is None]
    require(family != 'partition' or not remaining, str(prim.GetPath())+': material partition does not cover every face.')
    valid, reason = UsdGeom.Subset.ValidateFamily(mesh,'face','materialBind')
    require(valid, str(prim.GetPath())+': invalid material family: '+reason)
    if remaining: groups.insert(0,(prim,remaining))
    return groups


def write_mesh(prim, output, index, cache, UsdGeom, UsdShade, Ar, Sdf):
    mesh = UsdGeom.Mesh(prim); label = str(prim.GetPath())
    require(mesh.GetSubdivisionSchemeAttr().Get() == 'none', label+': subdivision must be explicitly none; bake the evaluated surface first.')
    require(mesh.ComputeVisibility() != 'invisible' and mesh.ComputePurpose() in ('default','render'), label+': hidden, guide or proxy mesh cannot be silently imported.')
    require(not mesh.GetHoleIndicesAttr().Get(), label+': mesh holes must be baked first.')
    points, counts, indices = mesh.GetPointsAttr().Get(), mesh.GetFaceVertexCountsAttr().Get(), mesh.GetFaceVertexIndicesAttr().Get()
    require(points is not None and len(points)>0 and counts is not None and len(counts)>0 and indices is not None, label+': empty mesh.')
    require(all(n >= 3 for n in counts), label+': each face needs at least three corners.')
    require(sum(counts) == len(indices) and all(0 <= i < len(points) for i in indices), label+': invalid face topology.')
    groups = material_groups(prim,len(counts),UsdGeom,UsdShade)
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
    orientation=str(mesh.GetOrientationAttr().Get());require(orientation in ('leftHanded','rightHanded'),label+': invalid orientation.')
    # Compact per-face data; generated normals are uniform on the original face, before splitting.
    generated = normals is None
    generated_normals=array.array('f')
    if generated: ni='uniform'
    corner_offsets=array.array('q',[0]);triangulated={}
    for face,count in enumerate(counts):
        corner=corner_offsets[-1]
        try:
            face_points = [points[indices[corner+i]] for i in range(count)] if count >= 4 or generated else None
            if count > 4: triangulated[face]=triangulate_polygon(face_points)
            elif count == 4:
                quad=quad_topology(face_points)
                if quad is not None:triangulated[face]=quad
            if generated: generated_normals.extend(facet_normal(face_points,orientation=='leftHanded'))
        except ValueError as error: raise ValueError(label+f': face {face}: '+str(error)) from error
        corner_offsets.append(corner+count)
    matrix=cache.GetLocalToWorldTransform(prim)
    require(all(math.isfinite(float(matrix[r][c])) for r in range(4) for c in range(4)), label+': non-finite transform.')
    require(all(matrix[r][3]==0 for r in range(3)) and matrix[3][3]==1 and matrix.GetDeterminant()!=0, label+': non-affine or singular transform.')
    transform=[float(matrix[c][r]) for r in range(3) for c in range(3)]+[float(matrix[3][c]) for c in range(3)]
    parts=[]
    for material_prim,faces in groups:
        material=material_for(material_prim,output,Ar,Sdf,UsdGeom,UsdShade,geometry=prim)
        require(material['texture'] is None or uv is not None,label+': textured mesh is missing st UVs.')
        require(all(math.isfinite(v) and 0<=v<=1 for v in material['color']+[material['metallic'],material['roughness']]),label+': invalid material factors.')
        positions_out,normals_out,uv_out=array.array('f'),array.array('f'),array.array('f')
        counts_out,indices_out,mapping=array.array('i'),array.array('i'),{}
        for face in faces:
            offset=corner_offsets[face]
            polygons=triangulated.get(face)
            if polygons is None: polygons=(range(counts[face]),)  # Existing native triangle/quad route.
            for polygon in polygons:
                counts_out.append(len(polygon))
                for source_corner in polygon:
                    corner=offset+source_corner
                    vertex=indices[corner];normal_index=attribute_index(ni,face,corner,vertex);uv_index=attribute_index(ui,face,corner,vertex)
                    key=(vertex,normal_index,uv_index)
                    if key not in mapping:
                        mapping[key]=len(positions_out)//3;positions_out.extend(values(points[vertex],3))
                        if generated:normals_out.extend(generated_normals[normal_index*3:normal_index*3+3])
                        elif normals is not None:normals_out.extend(values(normals[normal_index],3))
                        if uv is not None:uv_out.extend(values(uv[uv_index],2))
                    indices_out.append(mapping[key])
        filename=f'mesh-{index+len(parts):06d}.bin'
        with (output/filename).open('wb') as stream:
            for data in (positions_out,counts_out,indices_out,normals_out,uv_out):
                require(data.itemsize==4,'Unsupported platform scalar size.')
                if sys.byteorder!='little':data.byteswap()
                data.tofile(stream)
        part_name=prim.GetDisplayName() or str(prim.GetName())
        if material_prim != prim:part_name+=' / '+(material_prim.GetDisplayName() or str(material_prim.GetName()))
        parts.append(dict(primPath=str(material_prim.GetPath()),sourcePrimPath=label,name=part_name,file=filename,vertices=len(positions_out)//3,
                faces=len(counts_out),indices=len(indices_out),normals=True,generatedNormals=generated,uv=uv is not None,
                transform=transform,leftHanded=orientation=='leftHanded',doubleSided=bool(mesh.GetDoubleSidedAttr().Get()),**material))
    return parts


def validate_prim(prim, allowed=('', 'Xform', 'Scope', 'Mesh', 'GeomSubset', 'Material', 'Shader', 'NodeGraph')):
    label=str(prim.GetPath());typename=str(prim.GetTypeName())
    require(typename in allowed,label+': unsupported scene primitive '+typename+'; no geometry was silently omitted.')
    if typename == 'GeomSubset':
        require(str(prim.GetParent().GetTypeName()) == 'Mesh' and prim.GetAttribute('elementType').Get() == 'face' and prim.GetAttribute('familyName').Get() == 'materialBind',
                label+': only materialBind face subsets directly under a mesh are supported.')
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
        if prim.IsA(UsdGeom.Mesh): parts.extend(write_mesh(prim,output,len(parts),cache,UsdGeom,UsdShade,Ar,Sdf))
    require(parts,'USD contains no static mesh geometry.')
    if not stage.HasAuthoredMetadata('metersPerUnit'): warnings.append('USD units were unauthored; the OpenUSD 0.01 metre fallback was used.')
    if not stage.HasAuthoredMetadata('upAxis'): warnings.append('USD upAxis was unauthored; the OpenUSD fallback axis was used.')
    warnings.append('Mesh import flattens instance hierarchy. Display is two-sided; Rhino materials are a preview of the supported USD shading subset.')
    if any(p['texture'] for p in parts): warnings.append('Texture filter settings are source metadata; final sampling is controlled by the viewer.')
    manifest=dict(protocol=1,openUsdVersion='26.11',upAxis=axis,metersPerUnit=units,sourceSha256=source_hash,dependencyHashes=dependency_hashes,parts=parts,warnings=warnings)
    (output/'mesh-import.json').write_text(json.dumps(manifest,ensure_ascii=False,allow_nan=False,separators=(',',':')),encoding='utf-8')
