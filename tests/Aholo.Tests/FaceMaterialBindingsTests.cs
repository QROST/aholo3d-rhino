using Aholo.Core;
namespace Aholo.Tests;

public sealed class FaceMaterialBindingsTests
{
    private sealed record Material(string Name,Dictionary<int,Guid> Channels);
    private static readonly Guid RedId=Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid BlueId=Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static Guid Channel(Material basis,int channel)=>basis.Channels.GetValueOrDefault(channel);
    [Fact]
    public void RepeatedFacesRetainOrderAndShareResolvedMaterials()
    {
        var basis=new Material("base",new(){[2]=RedId,[7]=RedId,[11]=BlueId});var red=new Material("red",new());var blue=new Material("blue",new());
        var lookups=new List<Guid>();var channels=new List<int>();
        var result=FaceMaterialBindings.Resolve(new[]{2,0,11,7,2,4},basis,(b,c)=>{channels.Add(c);return Channel(b,c);},id=>{lookups.Add(id);return id==RedId?red:blue;});
        Assert.Equal(new[]{red,basis,blue,red,red,basis},result);Assert.Same(result[0],result[3]);
        Assert.Equal(new[]{RedId,BlueId},lookups);Assert.Equal(new[]{2,11,7,4},channels);
    }
    [Fact]
    public void IdenticalDefinitionChannelsResolveAgainstEachInstancesEffectiveBase()
    {
        var first=new Material("parent A",new(){[1]=RedId});var second=new Material("parent B",new(){[1]=BlueId});
        var red=new Material("red",new());var blue=new Material("blue",new());
        Material? Find(Guid id)=>id==RedId?red:id==BlueId?blue:null;
        Assert.Equal(new[]{first,red,first},FaceMaterialBindings.Resolve(new[]{0,1,0},first,Channel,Find));
        Assert.Equal(new[]{second,blue,second},FaceMaterialBindings.Resolve(new[]{0,1,0},second,Channel,Find));
        Assert.Equal(new[]{first,red,first},FaceMaterialBindings.Resolve(new[]{0,1,0},first,Channel,Find));
    }
    [Fact]
    public void EmptyAndDefaultChannelsDoNotReadUnconsumedMaterialReferences()
    {
        var basis=new Material("base",new());
        Assert.Empty(FaceMaterialBindings.Resolve(Array.Empty<int>(),basis,(_,_)=>throw new Exception(),_=>throw new Exception()));
        Assert.Equal(new[]{basis,basis},FaceMaterialBindings.Resolve(new[]{0,0},basis,(_,_)=>throw new Exception(),_=>throw new Exception()));
    }
    [Fact]
    public void MissingNonemptyMaterialIdCannotBecomeTheBaseColor()
    {
        var basis=new Material("base",new(){[2]=RedId});
        var error=Assert.Throws<InvalidDataException>(()=>FaceMaterialBindings.Resolve(new[]{0,2},basis,Channel,_=>null));
        Assert.Contains("material is missing",error.Message);
    }
    [Fact]
    public void InvalidChannelDoesNotReachNativeLookup()
    {
        var basis=new Material("base",new());
        Assert.Throws<InvalidDataException>(()=>FaceMaterialBindings.Resolve(new[]{-1},basis,(_,_)=>throw new Exception(),_=>throw new Exception()));
    }
    [Fact]
    public void CancellationDuringResolutionReturnsNoPartialFacePlan()
    {
        var basis=new Material("base",new(){[2]=RedId});using var cts=new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(()=>FaceMaterialBindings.Resolve(new[]{2,0},basis,Channel,_=>{cts.Cancel();return basis;},cts.Token));
    }
    [Fact]
    public void PreCancelledPlanDoesNotReadMaterialTables()
    {
        using var cts=new CancellationTokenSource();cts.Cancel();var basis=new Material("base",new());
        Assert.Throws<OperationCanceledException>(()=>FaceMaterialBindings.Resolve(new[]{1},basis,(_,_)=>throw new Exception(),_=>throw new Exception(),cts.Token));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CancellationCannotPublishTheFinalOrEmptyPlan(bool empty)
    {
        using var cts=new CancellationTokenSource();var basis=new Material("base",new(){[1]=RedId});
        if(empty)cts.Cancel();
        Assert.Throws<OperationCanceledException>(()=>FaceMaterialBindings.Resolve(empty?Array.Empty<int>():new[]{1},basis,Channel,_=>{cts.Cancel();return basis;},cts.Token));
    }
    [Fact]
    public void AParentWithoutAssignedChannelsCannotBorrowTheLeafLayersOverrides()
    {
        var parent=new Material("unassigned parent",new());
        var result=FaceMaterialBindings.Resolve(new[]{2,0,2},parent,Channel,_=>throw new Exception("Must not read leaf layer"));
        Assert.All(result,m=>Assert.Same(parent,m));
    }
    [Fact]
    public void ResolvedFaceOrderSurvivesTheProductionGlbWriter()
    {
        var basis=new Material("base",new(){[2]=RedId,[7]=BlueId});var red=new Material("red",new());var blue=new Material("blue",new());
        var resolved=FaceMaterialBindings.Resolve(new[]{7,0,2,7},basis,Channel,id=>id==RedId?red:blue);
        var parts=resolved.Select((material,i)=>new MeshData(new[]{i*1000f,0,0,i*1000f+500,0,0,i*1000f,500,0},new[]{0,1,2},0,0,0)
        {Material=material==red?new(1,0,0,.5):material==blue?new(0,0,1,1,.7,.2):new(.25,.25,.25)}).ToArray();
        string path=Path.Combine(Path.GetTempPath(),"aholo-face-material-"+Guid.NewGuid()+".glb");
        try
        {
            GlbWriter.Write(path,parts);using var file=new BinaryReader(File.OpenRead(path));file.BaseStream.Position=12;
            int length=file.ReadInt32();file.ReadUInt32();using var json=System.Text.Json.JsonDocument.Parse(file.ReadBytes(length));
            var root=json.RootElement;
            for(int i=0;i<4;i++)
            {
                var primitive=root.GetProperty("meshes")[i].GetProperty("primitives")[0];
                int material=primitive.GetProperty("material").GetInt32();
                Assert.Equal(parts[i].Material!.LinearBaseColor(),root.GetProperty("materials")[material].GetProperty("pbrMetallicRoughness").GetProperty("baseColorFactor").EnumerateArray().Select(v=>v.GetDouble()));
                int accessor=primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32();
                Assert.Equal(i,root.GetProperty("accessors")[accessor].GetProperty("min")[0].GetDouble());
            }
            Assert.Equal("BLEND",root.GetProperty("materials")[2].GetProperty("alphaMode").GetString());
        }
        finally{File.Delete(path);}
    }
}
