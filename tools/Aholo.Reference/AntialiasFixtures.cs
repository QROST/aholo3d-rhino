using System.Text.Json;
using Aholo.Core;

internal static class AntialiasFixtures
{
    internal static int Run(string directory)
    {
        Directory.CreateDirectory(directory);var entries=new List<object>();
        void Sample(double x,double b,double y,double det)=>entries.Add(new{X=x,B=b,Y=y,FilteredDeterminant=det,Actual=GaussianAntialiasing.Compensation(x,b,y,det)});
        Sample(1,1,Math.BitIncrement(1),3);
        Sample(Math.ScaleB(1,-700),0,Math.ScaleB(1,-700),1);
        Sample(Math.ScaleB(1,500),0,double.Epsilon,Math.ScaleB(1,500));
        foreach(int exponent in new[]{-1000,-700,-200,-2,0,2,100,400})foreach(double correlation in new[]{0,.25,-.75,.99999})
        {
            double x=Math.ScaleB(1.25,exponent),y=Math.ScaleB(1.75,exponent),b=Math.Sqrt(x)*Math.Sqrt(y)*correlation;
            double det=(x+.3)*(y+.3)-b*b;if(double.IsFinite(det)&&det>0)Sample(x,b,y,det);
        }
        var projections=new List<object>();
        foreach(bool perspective in new[]{false,true})foreach(bool reflected in new[]{false,true})foreach(double focal in new[]{1d,10,100})foreach(double depth in new[]{2d,10})
        {
            var splat=new GaussianSplat(new(.25,.125,depth),new(Math.Log(.1),Math.Log(.2),Math.Log(.3)),new(1,0,0,0),0,0,new float[]{.5f,.25f,.125f});
            var transform=new GaussianTransform(new(reflected?-2:2,.25,0,0,1.5,.2,.1,0,1),new(.125,-.25,.5));
            var camera=new GaussianCamera(new(0,0,0),new(1,0,0),new(0,1,0),new(0,0,1),perspective,1001,1001,focal,focal*1.2,500,500,.01,10000);
            var slow=GaussianProjector.Project(splat,camera,transform,antialiased:true)??throw new InvalidDataException("Fixture was clipped.");
            var asset=GaussianPreparedAsset.Prepare(new[]{splat});
            if(!GaussianProjectionContext.Create(camera,transform,antialiased:true).TryProject(in asset[0],0,0,out var fast))throw new InvalidDataException("Fixture was clipped.");
            projections.Add(new{Perspective=perspective,Fx=camera.Fx,Fy=camera.Fy,Center=new[]{splat.Center.X,splat.Center.Y,splat.Center.Z},
                Scales=new[]{Math.Exp(splat.LogScale.X),Math.Exp(splat.LogScale.Y),Math.Exp(splat.LogScale.Z)},
                Matrix=new[]{new[]{transform.Linear.M11,transform.Linear.M12,transform.Linear.M13},new[]{transform.Linear.M21,transform.Linear.M22,transform.Linear.M23},new[]{transform.Linear.M31,transform.Linear.M32,transform.Linear.M33}},
                Translation=new[]{transform.Translation.X,transform.Translation.Y,transform.Translation.Z},Filter=.3,OriginalOpacity=asset[0].Opacity,
                Reference=new[]{slow.CovXX,slow.CovXY,slow.CovYY,slow.Opacity},Batch=new[]{fast.CovXX,fast.CovXY,fast.CovYY,fast.Opacity}});
        }
        File.WriteAllText(Path.Combine(directory,"antialias.json"),JsonSerializer.Serialize(new{Determinants=entries,Projections=projections},new JsonSerializerOptions{WriteIndented=true}));
        return 0;
    }
    internal static int WriteScene(string path)
    {
        double dc=1/Math.Sqrt(4*Math.PI);
        var splats=Enumerable.Range(0,4).Select(i=>new GaussianSplat(new(0,-1.2+.8*i,0),new(Math.Log(.02*Math.Pow(2,i)),Math.Log(.02*Math.Pow(2,i)),Math.Log(.01)),new(1,0,0,0),0,0,
            new[]{(float)((.1-.5)/dc),(float)((.8-.5)/dc),(float)((1-.5)/dc)})).ToArray();
        var scene=new GaussianScene(new[]{new GaussianPart("Classic — left",splats,new(GaussianMatrix.Identity,new(-1.25,0,0))),
            new GaussianPart("Antialiased — right",splats,new(GaussianMatrix.Identity,new(1.25,0,0)),true)},"Z",1);
        UsdGaussianWriter.Write(path,scene);
        // Canonical fixture text has one final newline; production USD payloads are otherwise unchanged.
        File.WriteAllText(path,File.ReadAllText(path).TrimEnd()+"\n");
        var loaded=UsdGaussianReader.Read(path);
        if(loaded.Parts.Count!=2||loaded.Parts[0].Antialiased||!loaded.Parts[1].Antialiased||loaded.Parts.Any(p=>p.Splats.Length!=4||p.Splats.Any(s=>s.OpacityLogit!=0)))
            throw new InvalidDataException("Antialias scene roundtrip changed flags or original opacity.");
        Console.WriteLine("Paired GS scene written and reread: separate flags, original opacity preserved; no native acceptance.");return 0;
    }
}
