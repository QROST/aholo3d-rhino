using Aholo3D.Core;

internal static class MeshTopologyFixtures
{
    internal static int Run(string path)
    {
        var scene=UsdMeshReader.Read(path);
        if(scene.Parts.Count!=3||scene.UpAxis!="Z"||scene.MetersPerUnit!=.001)throw new InvalidDataException("Topology fixture stage mismatch.");
        string[] paths=["/World/Mesh","/World/Mesh/BlueFaces","/World/Mesh/RedFaces"];
        int[][] counts=[[3],[3,3,3],[4]];
        GaussianVector[] source=[new(10,0,0),new(11,0,0),new(10,1,0),new(0,0,0),new(3,0,0),new(3,3,0),new(1.5,1,0),new(0,3,0),new(5,0,0),new(6,0,0),new(6,1,0),new(5,1,0)];
        for(int group=0;group<3;group++)
        {
            var part=scene.Parts[group];
            if(part.PrimPath!=paths[group]||part.SourcePrimPath!="/World/Mesh"||!part.FaceCounts.SequenceEqual(counts[group]))throw new InvalidDataException("Subset topology or provenance changed.");
            if(!part.LeftHanded||!part.DoubleSided||part.Transform.Translation!=new GaussianVector(1e12+.125,-2e12+.25,3e12+.5))throw new InvalidDataException("Orientation or double placement changed.");
            if(part.Normals is null||part.TexCoords is null||part.Positions.Length/3!=(group==0?3:group==1?5:4))throw new InvalidDataException("Corner attributes missing.");
            var origin=part.Transform.TransformPoint(new(0,0,0));
            if(part.Transform.TransformPoint(new(1,1,1))-origin!=new GaussianVector(-2,3,4))throw new InvalidDataException("Signed linear placement changed.");
            for(int vertex=0;vertex<part.Positions.Length/3;vertex++)
            {
                if(part.Normals[3*vertex]!=0||part.Normals[3*vertex+1]!=0||part.Normals[3*vertex+2]!=group+1)throw new InvalidDataException("Original face normal was misindexed.");
                int corner=Array.IndexOf(source,new GaussianVector(part.Positions[3*vertex],part.Positions[3*vertex+1],part.Positions[3*vertex+2]));
                if(corner<0||part.TexCoords[2*vertex]!=(11-corner)/16f||part.TexCoords[2*vertex+1]!=(11-corner)/32f)throw new InvalidDataException("Original indexed UV changed.");
            }
            if(group==1)
            {
                double twiceArea=0;
                for(int i=0;i<part.Indices.Length;i+=3)
                {
                    int a=part.Indices[i]*3,b=part.Indices[i+1]*3,c=part.Indices[i+2]*3;
                    double area=(part.Positions[b]-part.Positions[a])*(part.Positions[c+1]-part.Positions[a+1])-(part.Positions[b+1]-part.Positions[a+1])*(part.Positions[c]-part.Positions[a]);
                    if(area<=0)throw new InvalidDataException("Polygon triangle winding changed.");twiceArea+=area;
                }
                if(twiceArea!=12)throw new InvalidDataException("Concave polygon area changed.");
            }
        }
        if(scene.Parts[0].Material.G!=1||scene.Parts[1].Material.B!=1||scene.Parts[2].Texture?.UseAlpha!=true)throw new InvalidDataException("Subset material changed.");
        Console.WriteLine("Public USD reader verified polygon/material subsets "+Path.GetExtension(path)+"; no native host invoked.");return 0;
    }
}
