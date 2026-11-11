using System.Globalization;
using System.Text;
using Aholo.Core;
namespace Aholo.Tests;

public sealed class GaussianAntialiasingTests
{
    private static GaussianCamera Camera(bool perspective=false)=>new(new(0,0,0),new(1,0,0),new(0,1,0),new(0,0,1),perspective,101,101,1,1,50,50,.01,10000);
    private static GaussianSplat Splat(double sx=.1,double sy=.2)=>new(new(0,0,2),new(Math.Log(sx),Math.Log(sy),Math.Log(.3)),new(1,0,0,0),0,0,new float[]{.5f,.25f,.125f});
    private static void Near(double expected,double actual,double relative=2e-12)
    {Assert.True(double.IsFinite(actual));Assert.InRange(actual,expected*(1-relative),expected*(1+relative));}

    [Theory]
    [InlineData(.01,.04,.3)]
    [InlineData(1,1,.3)]
    [InlineData(100,200,.3)]
    [InlineData(1e-200,1e-200,.3)]
    [InlineData(1e-300,1e100,.3)]
    [InlineData(1e100,1e-300,.3)]
    public void AreaFactorMatchesIndependentDiagonalSquareRoots(double x,double y,double filter)
    {
        double expected=(Math.Sqrt(x)/Math.Sqrt(x+filter))*(Math.Sqrt(y)/Math.Sqrt(y+filter));
        Near(expected,GaussianAntialiasing.Compensation(x,0,y,(x+filter)*(y+filter)));
    }
    [Fact]
    public void CorrelatedCovarianceUsesItsFullDeterminant()
    {
        // Binary-exact entries: determinant = 4*9 - 3*3 = 27, filtered determinant = 5*10 - 9 = 41.
        Near(Math.Sqrt(27d/41),GaussianAntialiasing.Compensation(4,3,9,41));
        Near(Math.Sqrt(27d/41),GaussianAntialiasing.Compensation(4,-3,9,41));
        Assert.Equal(0,GaussianAntialiasing.Compensation(1,1,1,3));
        Assert.Equal(0,GaussianAntialiasing.Compensation(1,Math.BitIncrement(1),1,3));
    }
    [Fact]
    public void ExponentBalancingPreservesNearlySingularAndExtremeBinaryAreas()
    {
        Near(Math.Sqrt(Math.ScaleB(1,-52))/Math.Sqrt(3),GaussianAntialiasing.Compensation(1,1,Math.BitIncrement(1),3));
        Near(Math.ScaleB(1,-700),GaussianAntialiasing.Compensation(Math.ScaleB(1,-700),0,Math.ScaleB(1,-700),1));
        Near(Math.ScaleB(1,-537),GaussianAntialiasing.Compensation(Math.ScaleB(1,500),0,double.Epsilon,Math.ScaleB(1,500)));
        Assert.Equal(.5,GaussianAntialiasing.Compensation(.3,0,.3,.6*.6));
    }
    [Fact]
    public void InvalidValuesNeverProduceNanOrNegativeOpacity()
    {
        foreach(double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity,-1d})
        {
            Assert.Throws<ArgumentException>(()=>GaussianAntialiasing.Compensation(bad,0,1,1));
            Assert.Throws<ArgumentException>(()=>GaussianAntialiasing.Compensation(1,0,bad,1));
            Assert.Throws<ArgumentException>(()=>GaussianAntialiasing.Compensation(1,0,1,bad));
        }
        Assert.Throws<ArgumentException>(()=>GaussianAntialiasing.Compensation(1,double.NaN,1,1));
        Assert.Throws<ArgumentException>(()=>GaussianAntialiasing.Compensation(1,0,1,0));
        Assert.Equal(0,GaussianAntialiasing.Compensation(0,0,0,.09));
    }
    [Fact]
    public void PreparingAxesStillRejectsSourceCovarianceOverflow()
    {
        var source=new GaussianSplat(new(0,0,2),new(500,0,0),new(1,0,0,0),0,0,new float[]{0,0,0});
        Assert.Throws<ArgumentException>(()=>GaussianPreparedAsset.Prepare(new[]{source}));
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void ProjectionChangesOnlyOpacityAndMatchesClosedFormIncludingNoFilter(bool perspective)
    {
        var splat=Splat();var camera=Camera(perspective);
        var asset=GaussianPreparedAsset.Prepare(new[]{splat});
        var classic=GaussianProjector.Project(splat,camera,GaussianTransform.Identity)!;
        var adjusted=GaussianProjector.Project(splat,camera,GaussianTransform.Identity,antialiased:true)!;
        double factor=perspective?.25:1,xx=.01*factor,yy=.04*factor;
        double expected=.5*Math.Sqrt(xx/(xx+.3))*Math.Sqrt(yy/(yy+.3));
        Near(expected,adjusted.Opacity);Assert.Equal(classic,adjusted with{Opacity=classic.Opacity});
        var context=GaussianProjectionContext.Create(camera,GaussianTransform.Identity,antialiased:true);
        Assert.True(context.Antialiased);Assert.True(context.TryProject(in asset[0],0,0,out var value));Near(expected,value.Opacity);
        Assert.Equal(.5,asset[0].Opacity);Assert.Equal(0,splat.OpacityLogit);
        var noFilter=GaussianProjector.Project(splat,camera,GaussianTransform.Identity,filterVariance:0,antialiased:true)!;
        Assert.Equal(.5,noFilter.Opacity);
        Assert.True(GaussianProjectionContext.Create(camera,GaussianTransform.Identity,filterVariance:0,antialiased:true).TryProject(in asset[0],0,0,out var unfiltered));Assert.Equal(.5,unfiltered.Opacity);
    }
    [Fact]
    public void InvertibleShearDoesNotCreateNegativeRawVarianceFromRoundoff()
    {
        double u=Math.ScaleB(1,-52),t=Math.Sqrt(.5);
        var transform=new GaussianTransform(new(1,1,0,1,1+3*u,0,0,0,1),new(0,0,0));
        var splat=new GaussianSplat(new(0,0,0),new(0,0,0),new(1,0,0,0),0,0,new float[]{0,0,0});
        var camera=new GaussianCamera(new(2*t,2*t,0),new(t,-t,0),new(0,0,1),new(-t,-t,0),false,101,101,1,1,50,50,.01,100);
        Assert.NotNull(GaussianProjector.Project(splat,camera,transform));
        var projected=GaussianProjector.Project(splat,camera,transform,antialiased:true)!;
        var asset=GaussianPreparedAsset.Prepare(new[]{splat});
        Assert.True(GaussianProjectionContext.Create(camera,transform,antialiased:true).TryProject(in asset[0],0,0,out var batch));
        Assert.True(projected.Opacity>0);Near(batch.Opacity,projected.Opacity);
    }
    [Fact]
    public void TinyFootprintKeepsRepresentableCompensationInsteadOfRawDeterminantUnderflow()
    {
        var splat=Splat(1e-100,1e-100);
        var expected=GaussianProjector.Project(splat,Camera(),GaussianTransform.Identity,antialiased:true)!;
        Near(.5e-200/.3,expected.Opacity,1e-11);
        var asset=GaussianPreparedAsset.Prepare(new[]{splat});
        Assert.True(GaussianProjectionContext.Create(Camera(),GaussianTransform.Identity,antialiased:true).TryProject(in asset[0],0,0,out var actual));
        Near(expected.Opacity,actual.Opacity);
    }
    [Fact]
    public void SharedAssetCanHaveDifferentModesWithoutCumulativeCompensationOrAllocation()
    {
        var splat=Splat();var asset=GaussianPreparedAsset.Prepare(new[]{splat});var camera=Camera();
        var original=GaussianProjectionContext.Create(camera,GaussianTransform.Identity,1);
        var adjusted=GaussianProjectionContext.Create(camera,GaussianTransform.Identity,2,antialiased:true);
        var warmed=ProjectBatch(adjusted,asset);
        var measured=ProjectBatch(adjusted,asset);
        Assert.Equal(10000,measured.Visible);Assert.Equal(warmed.Opacity,measured.Opacity);Assert.Equal(0,measured.Allocated);
        Assert.True(original.TryProject(in asset[0],0,0,out var a));Assert.True(adjusted.TryProject(in asset[0],0,1,out var b));Assert.True(adjusted.TryProject(in asset[0],0,1,out var again));
        Assert.Equal(.5,a.Opacity);Assert.True(b.Opacity<a.Opacity);Assert.Equal(b,again);Assert.Equal(.5,asset[0].Opacity);Assert.Same(splat,asset[0].Source);
    }
    // Warm and measure the same complete optimized batch. Keep xUnit and first-use
    // assertion/JIT work outside the allocation interval; retain a strict zero budget.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining|System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    private static (int Visible,double Opacity,long Allocated) ProjectBatch(GaussianProjectionContext context,GaussianPreparedAsset asset)
    {
        int visible=0;double opacity=0;long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++)if(context.TryProject(in asset[0],0,0,out var projected)){visible++;opacity+=projected.Opacity;}
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        return(visible,opacity,allocated);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void DpiNormalizationKeepsCompensationOnTheAcceptedPixelGrid(bool perspective)
    {
        var camera=Camera(perspective);var twice=camera with{Width=202,Height=202,Fx=2,Fy=2,Cx=100.5,Cy=100.5};
        var normalized=GaussianFramePolicy.NormalizeSampling(twice,new(101,101,101,101));
        var a=GaussianProjector.Project(Splat(),camera,GaussianTransform.Identity,antialiased:true);
        var b=GaussianProjector.Project(Splat(),normalized,GaussianTransform.Identity,antialiased:true);Assert.Equal(a,b);
    }
    [Fact]
    public void PlacementIdentityIncludesModePathHashAndFullTransformWithoutCultureDrift()
    {
        var initial=new GaussianPlacementIdentity("root/leaf","abc",new(GaussianMatrix.Identity,new(.1,.2,.3)),false);
        string Key(GaussianPlacementIdentity value){var builder=new StringBuilder();value.AppendTo(builder);return builder.ToString();}
        string key=Key(initial);Assert.NotEqual(key,Key(initial with{Antialiased=true}));Assert.NotEqual(key,Key(initial with{Path="root2/leaf"}));
        Assert.NotEqual(key,Key(initial with{Hash="def"}));Assert.NotEqual(key,Key(initial with{Transform=GaussianTransform.Identity}));
        var culture=CultureInfo.CurrentCulture;try{CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");Assert.Equal(key,Key(initial));}finally{CultureInfo.CurrentCulture=culture;}
        Assert.NotEqual(Key(initial with{Path="a:",Hash="b"}),Key(initial with{Path="a",Hash=":b"}));
    }
    [Theory]
    [InlineData("Aholo.GS")][InlineData("GSRhino.GS")][InlineData("LegacyViewer.GS")]
    public void ModeUsesOnlyTheResolvedLeafReferenceNamespace(string prefix)
    {
        var values=new Dictionary<string,string?>{{prefix+".Version","1"},{prefix+".Sha256",new string('a',64)},{"Unrelated.GS.Antialiased","true"}};
        GaussianReferenceMetadata Read()=>GaussianReferenceMetadata.Resolve(k=>values.GetValueOrDefault(k),()=>values.Keys)!;
        Assert.False(Read().Antialiased);values[prefix+".Antialiased"]="true";Assert.True(Read().Antialiased);
        if(prefix!="Aholo.GS")
        {
            values["Aholo.GS.Version"]="1";values["Aholo.GS.Sha256"]=new string('b',64);Assert.False(Read().Antialiased);
        }
    }
}
