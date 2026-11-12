using System.Runtime.CompilerServices;
using Aholo.Core;
namespace Aholo.Tests;

[Collection("Allocation measurement isolation")]
public sealed class GaussianAllocationTests
{
    [Fact]
    public void SharedAssetCanHaveDifferentModesWithoutCumulativeCompensationOrAllocation()
    {
        var splat=new GaussianSplat(new(0,0,2),new(Math.Log(.1),Math.Log(.2),Math.Log(.3)),new(1,0,0,0),0,0,new float[]{.5f,.25f,.125f});
        var asset=GaussianPreparedAsset.Prepare(new[]{splat});
        var camera=new GaussianCamera(new(0,0,0),new(1,0,0),new(0,1,0),new(0,0,1),false,101,101,1,1,50,50,.01,10000);
        var original=GaussianProjectionContext.Create(camera,GaussianTransform.Identity,1);
        var adjusted=GaussianProjectionContext.Create(camera,GaussianTransform.Identity,2,antialiased:true);
        var warmed=ProjectBatch(adjusted,asset);_=PositiveAllocation();
        // .NET 8 background-GC accounting can change a thread's counter without application allocations.
        // Isolate only this measurement; a failed entry/exit is a failure, never a skipped check or retry.
        Assert.True(GC.TryStartNoGCRegion(16L*1024*1024,disallowFullBlockingGC:false),"Cannot establish the allocation measurement's no-GC region.");
        (int Visible,double Opacity,long Allocated) measured;long positive;
        try{measured=ProjectBatch(adjusted,asset);positive=PositiveAllocation();}
        finally{GC.EndNoGCRegion();}
        Assert.Equal(10000,measured.Visible);Assert.Equal(warmed.Opacity,measured.Opacity);Assert.Equal(0,measured.Allocated);
        Assert.True(positive>=37,"The positive allocation control was not observed.");
        Assert.True(original.TryProject(in asset[0],0,0,out var a));Assert.True(adjusted.TryProject(in asset[0],0,1,out var b));Assert.True(adjusted.TryProject(in asset[0],0,1,out var again));
        Assert.Equal(.5,a.Opacity);Assert.True(b.Opacity<a.Opacity);Assert.Equal(b,again);Assert.Equal(.5,asset[0].Opacity);Assert.Same(splat,asset[0].Source);
    }
    [MethodImpl(MethodImplOptions.NoInlining|MethodImplOptions.AggressiveOptimization)]
    private static (int Visible,double Opacity,long Allocated) ProjectBatch(GaussianProjectionContext context,GaussianPreparedAsset asset)
    {
        int visible=0;double opacity=0;long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++)if(context.TryProject(in asset[0],0,0,out var projected)){visible++;opacity+=projected.Opacity;}
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        return(visible,opacity,allocated);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long PositiveAllocation()
    {
        long before=GC.GetAllocatedBytesForCurrentThread();var value=new byte[37];GC.KeepAlive(value);
        return GC.GetAllocatedBytesForCurrentThread()-before;
    }
}
[CollectionDefinition("Allocation measurement isolation",DisableParallelization=true)]
public sealed class AllocationMeasurementCollection { }
