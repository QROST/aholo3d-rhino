using Aholo3D.Core;

internal static class MeshShadingFixtures
{
    internal static int Run(string path,bool mixed)
    {
        var complete=mixed?UsdSceneReader.Read(path):null;
        if(mixed&&complete!.Gaussians is null)throw new InvalidDataException("Mixed shading fixture lost GS geometry.");
        var scene=mixed?complete!.Meshes!:UsdMeshReader.Read(path);
        if(scene.Parts.Count!=19||scene.UpAxis!="Z"||scene.MetersPerUnit!=1)throw new InvalidDataException("Shading fixture scene changed.");
        foreach(var part in scene.Parts)
        {
            string name=part.SourcePrimPath.Split('/').Last();bool namedAuthor=name.StartsWith("Authored",StringComparison.Ordinal),authored=namedAuthor||name=="OffsetQuad";
            if(part.Normals is null||part.GeneratedFacetNormals==authored)throw new InvalidDataException("Normal provenance changed.");
            if(name.StartsWith("Faceted",StringComparison.Ordinal)||namedAuthor)
            {
                if(!part.FaceCounts.SequenceEqual(new[]{3,3})||part.Positions.Length!=18)throw new InvalidDataException("Face or UV seams were welded.");
                bool left=name.Contains("_L",StringComparison.Ordinal),mirror=name.EndsWith("_Mirror",StringComparison.Ordinal);
                if(part.LeftHanded!=left)throw new InvalidDataException("Handedness changed.");
                for(int i=0;i<6;i++)
                {
                    var normal=new GaussianVector(part.Normals[i*3],part.Normals[i*3+1],part.Normals[i*3+2]);
                    if(authored&&normal!=new GaussianVector(1,2,3))throw new InvalidDataException("Authored primvar normal was flipped or replaced.");
                    if(!authored&&normal!=(i<3?new GaussianVector(0,0,left?-1:1):new GaussianVector(0,left?-1:1,0)))throw new InvalidDataException("Original faces were smoothed or flipped twice.");
                    var actual=part.Transform.Linear.Inverse().Transpose().Transform(normal).Normalized();
                    var expected=authored?(mirror?new GaussianVector(-.5,2d/3,.75):new GaussianVector(1,2,3)).Normalized():normal;
                    Near(actual,expected);
                    if(name=="Faceted_R"?part.TexCoords is not null:part.TexCoords is null||part.TexCoords[i*2]!=i/8f||part.TexCoords[i*2+1]!=i/16f)throw new InvalidDataException("Source-corner UVs changed.");
                }
                // Independent world-space geometric oracle for the first face and host winding XOR rule.
                var p=Enumerable.Range(0,3).Select(i=>part.Transform.TransformPoint(new(part.Positions[i*3],part.Positions[i*3+1],part.Positions[i*3+2]))).ToArray();
                if(left^mirror)(p[1],p[2])=(p[2],p[1]);
                var a=p[1]-p[0];var b=p[2]-p[0];var geometric=new GaussianVector(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X).Normalized();
                if(!authored)Near(geometric,new(0,0,left?-1:1));
            }
            else if(name=="OffsetQuad")
            {
                if(!part.FaceCounts.SequenceEqual(new[]{3,3,3})||part.Positions.Length!=21||part.TexCoords is null)throw new InvalidDataException("Offset quad topology changed.");
                GaussianVector[] expected=[new(10,0,0),new(11,0,0),new(10,1,0),new(4,0,0),new(0,1,0),new(1,0,0),new(0,-1,0)];
                var corners=new HashSet<int>();
                for(int i=0;i<7;i++)
                {
                    int corner=Array.IndexOf(expected,new GaussianVector(part.Positions[i*3],part.Positions[i*3+1],part.Positions[i*3+2]));
                    if(corner<0||!corners.Add(corner)||part.Normals[i*3]!=7-corner||part.Normals[i*3+1]!=1||part.Normals[i*3+2]!=2||part.TexCoords[i*2]!=(6-corner)/8f||part.TexCoords[i*2+1]!=(6-corner)/16f)throw new InvalidDataException("Original quad corner attributes changed.");
                }
            }
            else if(name=="Split")
            {
                if(part.FaceCounts.Length!=1||part.Positions.Length!=9)throw new InvalidDataException("Material face ownership changed.");
                Near(new(part.Normals[0],part.Normals[1],part.Normals[2]),part.PrimPath.EndsWith("/Blue",StringComparison.Ordinal)?new(0,1,0):new(0,0,1));
            }
            else
            {
                bool triangles=name is "ConcaveQuad" or "CollinearQuad" or "DynamicRangeQuad";
                if(!part.FaceCounts.SequenceEqual(triangles?new[]{3,3}:new[]{4})||part.Positions.Length!=12)throw new InvalidDataException("Quad topology or corner retention changed.");
                var expected=name switch{"WarpedQuad"=>new GaussianVector(-1,-1,8).Normalized(),"CancellationQuad"=>new GaussianVector(1,-4,0).Normalized(),"DynamicRangeQuad"=>new GaussianVector(0,0,-1),"AlmostPlanarQuad" or "SmallestWarpedQuad"=>new GaussianVector(1,0,0),_=>new GaussianVector(0,0,1)};
                for(int i=0;i<4;i++)Near(new(part.Normals[i*3],part.Normals[i*3+1],part.Normals[i*3+2]),expected);
                if(name=="ConcaveQuad")
                {
                    double area=0;
                    for(int i=0;i<6;i+=3){int a=part.Indices[i]*3,b=part.Indices[i+1]*3,c=part.Indices[i+2]*3;double twice=(part.Positions[b]-part.Positions[a])*(part.Positions[c+1]-part.Positions[a+1])-(part.Positions[b+1]-part.Positions[a+1])*(part.Positions[c]-part.Positions[a]);if(twice<=0)throw new InvalidDataException("Concave quad triangle reversed.");area+=twice/2;}
                    if(area!=3)throw new InvalidDataException("Concave quad area changed.");
                }
            }
        }
        Console.WriteLine($"Public {(mixed?"mixed scene":"mesh")} reader verified 19 shading/topology parts {Path.GetExtension(path)}; no native host invoked.");return 0;
    }
    private static void Near(GaussianVector a,GaussianVector b){if((a-b).Length>1e-6)throw new InvalidDataException("Normal transform or direction changed.");}
}
