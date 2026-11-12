using Aholo3D.Core;
namespace Aholo3D.Tests;

public sealed class GaussianInstanceGraphTests
{
    private static Guid Id(int value)=>new(value,0,0,new byte[8]);
    private static GaussianTransform Move(double x,double y=0,double z=0)=>new(GaussianMatrix.Identity,new(x,y,z));
    private static GaussianInstanceNode Node(int id,int? definition=null,GaussianTransform? transform=null,bool visible=true)
        =>new(Id(id),definition.HasValue?Id(definition.Value):null,transform??GaussianTransform.Identity,definition is null,visible);
    private static GaussianInstanceGraphResult Expand(GaussianInstanceNode[] roots,Dictionary<Guid,IReadOnlyList<GaussianInstanceNode>> definitions,bool hidden=false)
        =>GaussianInstanceGraph.Expand(roots,id=>definitions.GetValueOrDefault(id),hidden);
    [Fact]
    public void RepeatedSharedDefinitionsKeepAllPathsIncludingCoincidentCopies()
    {
        var leaf=Node(30);var roots=new[]{Node(1,10),Node(2,10)};
        var definitions=new Dictionary<Guid,IReadOnlyList<GaussianInstanceNode>>
        {
            [Id(10)]=new[]{Node(11,20,Move(5)),Node(12,20,Move(5))},[Id(20)]=new[]{leaf}
        };
        var first=Expand(roots,definitions);Assert.Equal(4,first.Occurrences.Count);
        Assert.Equal(4,first.Occurrences.Select(o=>o.Path).Distinct().Count());
        Assert.All(first.Occurrences,o=>{Assert.Equal(Id(30),o.ObjectId);Assert.Equal(new GaussianVector(5,0,0),o.World!.Value.Translation);Assert.False(o.IsTopLevel);});
        definitions[Id(10)]=definitions[Id(10)].Reverse().ToArray();
        Assert.Equal(first.Occurrences,Expand(roots.Reverse().ToArray(),definitions).Occurrences);
    }
    [Fact]
    public void GaussianFreeSharedHierarchyIsExaminedOncePerDefinition()
    {
        int reads=0;const int depth=60;
        var result=GaussianInstanceGraph.Expand(new[]{Node(1,100),Node(2)},id=>
        {
            Assert.True(++reads<=depth+1,"GS-free definitions must not be expanded per instance path.");
            int value=BitConverter.ToInt32(id.ToByteArray());
            return value==100+depth?Array.Empty<GaussianInstanceNode>():new[]{Node(value*2,value+1),Node(value*2+1,value+1)};
        },includeHidden:true);
        Assert.Equal(depth+1,reads);Assert.Equal(Id(2),Assert.Single(result.Occurrences).ObjectId);
    }
    [Fact]
    public void CyclicComponentsAreIsolatedBeforeRepeatedPathsExpand()
    {
        using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        const int depth=60;int reads=0;
        var result=GaussianInstanceGraph.Expand(new[]{Node(1,100),Node(2)},id=>
        {
            Assert.True(++reads<=depth+1);
            int value=BitConverter.ToInt32(id.ToByteArray());
            if(value==100)return new[]{Node(3),Node(4,101)};
            if(value==100+depth)return new[]{Node(500,100)};
            return new[]{Node(value*2,value+1),Node(value*2+1,value+1)};
        },ct:ct.Token);
        Assert.Equal(depth+1,result.CyclicDefinitions);
        Assert.Equal(Id(2),Assert.Single(result.Occurrences).ObjectId);
    }
    [Fact]
    public void CycleIsolationMatchesIndependentReachabilityForEveryThreeNodeGraph()
    {
        for(int mask=0;mask<512;mask++)
        {
            var reachable=new bool[3,3];var definitions=new Dictionary<Guid,IReadOnlyList<GaussianInstanceNode>>();
            for(int i=0;i<3;i++)
            {
                var members=new List<GaussianInstanceNode>{Node(100+i)};
                for(int j=0;j<3;j++)if((mask&(1<<(i*3+j)))!=0){reachable[i,j]=true;members.Add(Node(200+i*3+j,10+j));}
                definitions[Id(10+i)]=members;
            }
            // Independent transitive closure detects cycle membership, including self-edges.
            for(int k=0;k<3;k++)for(int i=0;i<3;i++)for(int j=0;j<3;j++)reachable[i,j]|=reachable[i,k]&&reachable[k,j];
            var result=Expand(new[]{Node(1,10),Node(2,11),Node(3,12)},definitions);
            Assert.Equal(Enumerable.Range(0,3).Count(i=>reachable[i,i]),result.CyclicDefinitions);
            for(int i=0;i<3;i++)
            {
                var rootOccurrences=result.Occurrences.Where(o=>o.RootId==Id(i+1)).ToArray();
                if(reachable[i,i])Assert.Empty(rootOccurrences);
                else Assert.Contains(rootOccurrences,o=>o.ObjectId==Id(100+i));
            }
        }
    }
    [Fact]
    public void HiddenCycleCannotProduceVisibleObjectsMissingFromResourceDiscovery()
    {
        var roots=new[]{Node(1,10),Node(2)};
        var definitions=new Dictionary<Guid,IReadOnlyList<GaussianInstanceNode>>
        {
            [Id(10)]=new[]{Node(11),Node(12,20,visible:false)},[Id(20)]=new[]{Node(21,10)}
        };
        var visible=Expand(roots,definitions);var resources=GaussianInstanceGraph.FindResourceObjects(roots,id=>definitions.GetValueOrDefault(id));
        Assert.Equal(2,visible.CyclicDefinitions);Assert.Equal(Id(2),Assert.Single(visible.Occurrences).ObjectId);
        Assert.Equal(new[]{Id(2)},resources);Assert.All(visible.Occurrences,o=>Assert.Contains(o.ObjectId,resources));
    }
    [Fact]
    public void HiddenSharedGaussiansLoadByReferenceWithoutExpandingTheirCopies()
    {
        const int depth=60;int reads=0;
        var roots=new[]{Node(1,100)};
        IReadOnlyList<GaussianInstanceNode> Children(Guid id)
        {
            Assert.True(++reads<=depth+1);
            int value=BitConverter.ToInt32(id.ToByteArray());
            return value==100+depth?new[]{Node(900,visible:false)}:new[]{Node(value*2,value+1),Node(value*2+1,value+1)};
        }
        using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.Empty(GaussianInstanceGraph.Expand(roots,Children,ct:ct.Token).Occurrences);
        Assert.Equal(depth+1,reads);reads=0;
        Assert.Equal(new[]{Id(900)},GaussianInstanceGraph.FindResourceObjects(roots,Children,ct.Token));Assert.Equal(depth+1,reads);
    }
    [Theory]
    [InlineData(true,false)] [InlineData(false,true)] [InlineData(false,false)]
    public void VisibilityPrunesTheWholeBranchButHiddenResourcesRemainReachable(bool parentVisible,bool childVisible)
    {
        var roots=new[]{Node(1,10,visible:parentVisible)};var definitions=new Dictionary<Guid,IReadOnlyList<GaussianInstanceNode>>{[Id(10)]=new[]{Node(2,visible:childVisible)}};
        Assert.Empty(Expand(roots,definitions).Occurrences);
        Assert.Single(Expand(roots,definitions,true).Occurrences);
    }
    [Fact]
    public void AncestorCycleDoesNotSuppressValidSharedSiblingsOrOtherRoots()
    {
        var definitions=new Dictionary<Guid,IReadOnlyList<GaussianInstanceNode>>
        {
            [Id(10)]=new[]{Node(11,20),Node(12)},[Id(20)]=new[]{Node(21,30),Node(22)},[Id(30)]=new[]{Node(31,20)}
        };
        var result=Expand(new[]{Node(1,10),Node(2,10),Node(3,99)},definitions);
        Assert.Equal(2,result.Occurrences.Count);Assert.All(result.Occurrences,o=>Assert.Equal(Id(12),o.ObjectId));Assert.Equal(2,result.CyclicDefinitions);Assert.Equal(1,result.MissingDefinitions);
    }
    [Fact]
    public void DeepHierarchyUsesAnExplicitStackWithoutAReportedCountCap()
    {
        const int depth=20000;var roots=new[]{Node(1,100001)};
        var result=GaussianInstanceGraph.Expand(roots,id=>
        {
            int level=BitConverter.ToInt32(id.ToByteArray())-100000;
            return new[]{level==depth?Node(depth+1,transform:Move(1)):Node(level+1,100001+level,Move(1))};
        });
        var only=Assert.Single(result.Occurrences);Assert.Equal(depth+1,only.Path.Split('/').Length);
        Assert.Equal(depth,only.World!.Value.Translation.X);Assert.Equal(0,result.CyclicDefinitions);
    }
    [Fact]
    public void GaussianMarkerIsATerminalBoundaryEvenWithABrokenDefinition()
    {
        var root=Node(1) with{Definition=Id(99)};
        var result=GaussianInstanceGraph.Expand(new[]{root},_=>throw new Exception("Proxy definition must not be visited."));
        Assert.True(Assert.Single(result.Occurrences).IsTopLevel);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void ComposedReflectionShearAndRotationMatchPointAndProjectionOracle(int degree)
    {
        var parent=new GaussianTransform(new(0,-2,0,1,0,.2,0,0,1.5),new(3,-1,2));
        var child=new GaussianTransform(new(-1,.25,0,0,1,0,.1,0,1),new(1,2,-3));
        var roots=new[]{Node(1,10,parent)};var definitions=new Dictionary<Guid,IReadOnlyList<GaussianInstanceNode>>{[Id(10)]=new[]{Node(2,transform:child)}};
        var world=Assert.Single(Expand(roots,definitions).Occurrences).World!.Value;
        foreach(var point in new[]{new GaussianVector(1,2,3),new GaussianVector(-1,0,2)})
            Close(parent.TransformPoint(child.TransformPoint(point)),world.TransformPoint(point));
        // Explicit independent affine product: do not use the production Compose as the expected transform.
        var expected=new GaussianTransform(new(0,-2,0,-.98,.25,.2,.15,0,1.5),new(-1,-.6,-2.5));
        var splat=new GaussianSplat(new(.1,.2,.3),new(-1,-1.5,-2),new(1,.2,.3,.4),.3,degree,
            Enumerable.Range(0,3*(degree+1)*(degree+1)).Select(i=>(float)Math.Sin(i*.2)).ToArray());
        var camera=new GaussianCamera(new(0,0,30),new(1,0,0),new(0,1,0),new(0,0,-1),true,640,480,400,400,319.5,239.5,.1,100);
        var asset=GaussianPreparedAsset.Prepare(new[]{splat});var actual=new GaussianProjectedValue[1];
        Assert.Equal(1,GaussianProjectionContext.Create(camera,world).Project(asset,actual));
        var oracle=GaussianProjector.Project(splat,camera,expected)!;Assert.NotNull(oracle);
        Assert.Equal(oracle.CenterX,actual[0].CenterX,9);Assert.Equal(oracle.CenterY,actual[0].CenterY,9);
        Assert.Equal(oracle.CovXX,actual[0].CovXX,9);Assert.Equal(oracle.CovXY,actual[0].CovXY,9);Assert.Equal(oracle.CovYY,actual[0].CovYY,9);
        Assert.Equal(oracle.Color.R,actual[0].Color.R,9);Assert.Equal(oracle.Color.G,actual[0].Color.G,9);Assert.Equal(oracle.Color.B,actual[0].Color.B,9);
    }
    [Fact]
    public void ParentMotionChangesWorldBoundsWhileIdentityRemainsTheSame()
    {
        var definitions=new Dictionary<Guid,IReadOnlyList<GaussianInstanceNode>>{[Id(10)]=new[]{Node(2,transform:Move(3))}};
        var first=Assert.Single(Expand(new[]{Node(1,10,Move(1e12))},definitions).Occurrences);
        var second=Assert.Single(Expand(new[]{Node(1,10,Move(1e12+5))},definitions).Occurrences);
        Assert.Equal(first.Path,second.Path);Assert.NotEqual(first.World,second.World);
        var corners=from x in new[]{-1d,1d} from y in new[]{-2d,2d} from z in new[]{-3d,3d} select new GaussianVector(x,y,z);
        Assert.Equal(5,corners.Max(p=>second.World!.Value.TransformPoint(p).X)-corners.Max(p=>first.World!.Value.TransformPoint(p).X));
    }
    [Fact]
    public void InverseValidationWaitsForTheCompleteWorldTransform()
    {
        var parent=new GaussianTransform(new(1e-110,0,0,0,1e110,0,0,0,1),GaussianVector.Zero);
        var child=new GaussianTransform(new(1e110,0,0,0,1e-110,0,0,0,1),GaussianVector.Zero);
        var definitions=new Dictionary<Guid,IReadOnlyList<GaussianInstanceNode>>{[Id(10)]=new[]{Node(2,transform:child)}};
        var world=Assert.Single(Expand(new[]{Node(1,10,parent)},definitions).Occurrences).World;
        Assert.NotNull(world);Close(new(1,2,3),world.Value.TransformPoint(new(1,2,3)));
    }
    [Fact]
    public void InvalidPlacementsRemainReachableForResourceRetentionButCannotHideProxies()
    {
        var roots=new[]{Node(1) with{Local=new GaussianTransform(default,new(0,0,0))},Node(2) with{Local=null},Node(3,10) with{Local=new GaussianTransform(GaussianMatrix.Identity,new(double.PositiveInfinity,0,0))}};
        var definitions=new Dictionary<Guid,IReadOnlyList<GaussianInstanceNode>>{[Id(10)]=new[]{Node(4)}};
        var result=Expand(roots,definitions);Assert.Equal(3,result.Occurrences.Count);
        Assert.All(result.Occurrences,o=>{Assert.Null(o.World);Assert.False(GaussianInstanceGraph.CanSuppressWholeRoot(o,o.RootId,o.RootId));});
        var tiny=GaussianInstanceGraph.Compose(GaussianTransform.Identity,new GaussianTransform(new(1e-80,0,0,0,1e-80,0,0,0,1e-80),new(0,0,0)));
        Assert.NotNull(tiny);
    }
    [Fact]
    public void MixedContainerAndItsMeshNeverDisappearWhenItsGaussianIsReady()
    {
        var definitions=new Dictionary<Guid,IReadOnlyList<GaussianInstanceNode>>{[Id(10)]=new[]{Node(2)}};
        var nested=Assert.Single(Expand(new[]{Node(1,10)},definitions).Occurrences);
        foreach(var current in new[]{Id(1),Id(2),Id(3)})Assert.False(GaussianInstanceGraph.CanSuppressWholeRoot(nested,current,Id(1)));
        var top=Assert.Single(Expand(new[]{Node(4)},definitions).Occurrences);
        Assert.True(GaussianInstanceGraph.CanSuppressWholeRoot(top,Id(4),null));
        Assert.True(GaussianInstanceGraph.CanSuppressWholeRoot(top,Id(99),Id(4)));
        Assert.False(GaussianInstanceGraph.CanSuppressWholeRoot(top,Id(3),Id(1)));
    }
    [Fact]
    public void CancellationIsCheckedBeforeReadingRootsOrDuringExpansion()
    {
        using var ct=new CancellationTokenSource();ct.Cancel();
        Assert.Throws<OperationCanceledException>(()=>GaussianInstanceGraph.Expand(null!,_=>null,ct:ct.Token));
        using var during=new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(()=>GaussianInstanceGraph.Expand(new[]{Node(1,10)},_=>{during.Cancel();return new[]{Node(2)};},ct:during.Token));
    }
    private static void Close(GaussianVector expected,GaussianVector actual)
    {Assert.Equal(expected.X,actual.X,10);Assert.Equal(expected.Y,actual.Y,10);Assert.Equal(expected.Z,actual.Z,10);}
}
