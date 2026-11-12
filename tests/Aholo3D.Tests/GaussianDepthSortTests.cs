using Aholo3D.Core;
namespace Aholo3D.Tests;

public sealed class GaussianDepthSortTests
{
    private static GaussianProjectedValue Value(double depth,long ordinal,int instance=0)=>new(0,0,depth,1,0,1,new(.2,.4,.6),.5,1,0,0,1,3,instance,0,ordinal);

    [Theory][InlineData(4095)][InlineData(4096)][InlineData(4097)][InlineData(65536)]
    public void ExactPositiveDoubleOrderingMatchesStableIndependentOracle(int count)
    {
        foreach(string order in new[]{"ascending","descending","shuffled","duplicates"})
        {
            var random=new Random(923);var values=new GaussianProjectedValue[count];
            for(int i=0;i<count;i++)
            {
                double depth=BitConverter.UInt64BitsToDouble((ulong)random.NextInt64(1,0x7ff0000000000000L));
                if(i>0&&i%5==0)depth=values[i-1].Depth;
                long ordinal=order switch{"ascending"=>i,"descending"=>count-i,"shuffled"=>random.NextInt64(),_=>i%7};
                values[i]=Value(depth,ordinal,i%4);
            }
            if(order=="ascending"){values[0]=values[0] with{GlobalOrdinal=long.MinValue};values[^1]=values[^1] with{GlobalOrdinal=long.MaxValue};}
            if(order=="descending"){values[0]=values[0] with{GlobalOrdinal=long.MaxValue};values[^1]=values[^1] with{GlobalOrdinal=long.MinValue};}
            AssertOrder(values,Enumerable.Range(0,count).ToArray());
        }
    }
    [Theory][InlineData("same")][InlineData("adjacent")][InlineData("subnormal")][InlineData("extremes")]
    public void FullDepthBitsAndEqualDepthInsertionOrderArePreserved(string pattern)
    {
        const int count=8193;var values=new GaussianProjectedValue[count];
        for(int i=0;i<count;i++)
        {
            double depth=pattern switch
            {
                "same"=>2,
                "adjacent"=>BitConverter.UInt64BitsToDouble(BitConverter.DoubleToUInt64Bits(1)+(ulong)(i%17)),
                "subnormal"=>BitConverter.UInt64BitsToDouble((ulong)(i%991+1)),
                _=>(i%4) switch{0=>double.Epsilon,1=>Math.ScaleB(1,-1022),2=>Math.BitDecrement(double.MaxValue),_=>double.MaxValue}
            };
            values[i]=Value(depth,i*3L,i%3);
        }
        AssertOrder(values,Enumerable.Range(0,count).ToArray());
        AssertOrder(values,Enumerable.Range(0,count).Reverse().ToArray());
    }
    [Fact] public void DuplicateIndicesAndOrdinalsKeepStableOrderAndMultiplicity()
    {
        const int count=8192;var random=new Random(922);var values=new GaussianProjectedValue[count];
        for(int i=0;i<count;i++)values[i]=Value(i%5+1,i%11,i%3);
        var indices=Enumerable.Range(0,count).Select(_=>random.Next(count)).ToArray();
        AssertOrder(values,indices);
        Array.Fill(values,Value(2,7));AssertOrder(values,indices);
    }
    [Theory][InlineData(0)][InlineData(-1)][InlineData(double.NaN)][InlineData(double.PositiveInfinity)]
    public void AlreadyOrderedPrefixDoesNotBypassInvalidFinalDepth(double invalid)
    {
        var values=Enumerable.Range(0,8192).Select(i=>Value(8192-i,8192-i)).ToArray();
        values[^1]=Value(invalid,0);int[] indices=Enumerable.Range(0,values.Length).ToArray();
        Assert.Throws<ArgumentException>(()=>GaussianDepthSorter.SortBackToFront(indices,values,new int[indices.Length]));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void BothLargeSortPathsObserveCancellationAfterFirstOutput(bool arbitraryOrdinals)
    {
        const int count=262144;var values=new GaussianProjectedValue[count];var indices=new int[count];var scratch=new int[count];
        for(int i=0;i<count;i++){values[i]=Value(1+i%137,arbitraryOrdinals?i%79:i);indices[i]=i;scratch[i]=-1;}
        using var stop=new CancellationTokenSource();using var ready=new ManualResetEventSlim();int done=0;
        var observer=new Thread(()=>
        {
            ready.Set();while(Volatile.Read(ref scratch[0])<0&&Volatile.Read(ref done)==0)Thread.SpinWait(20);
            if(Volatile.Read(ref done)==0)stop.Cancel();
        }){IsBackground=true};observer.Start();ready.Wait();
        try{Assert.Throws<OperationCanceledException>(()=>GaussianDepthSorter.SortBackToFront(indices,values,scratch,stop.Token));}
        finally{Volatile.Write(ref done,1);Assert.True(observer.Join(TimeSpan.FromSeconds(5)));}
        Assert.True(scratch[0]>=0);
    }
    [Fact] public void LargeEqualDepthSortPreservesClosedFormTransparency()
    {
        const int count=8192;var values=Enumerable.Range(0,count).Select(i=>Value(2,i) with{Opacity=0}).ToArray();
        values[0]=values[0] with{Color=new(1,0,0),Opacity=.5};
        values[^1]=values[^1] with{Color=new(0,0,1),Opacity=.5};
        var indices=Enumerable.Range(0,count).ToArray();GaussianDepthSorter.SortBackToFront(indices,values,new int[count]);
        double red=0,blue=0;
        foreach(int index in indices){var p=values[index];red=p.Color.R*p.Opacity+red*(1-p.Opacity);blue=p.Color.B*p.Opacity+blue*(1-p.Opacity);}
        Assert.Equal(.5,red);Assert.Equal(.25,blue);
    }
    [Fact] public void UnreferencedInvalidValuesDoNotChangeAcceptedSubset()
    {
        var values=Enumerable.Range(0,8193).Select(i=>Value(1+i%17,i)).ToArray();values[^1]=Value(double.NaN,0);
        var indices=Enumerable.Range(0,8192).ToArray();var expected=indices.OrderByDescending(i=>values[i].Depth).ThenByDescending(i=>values[i].GlobalOrdinal).ToArray();
        GaussianDepthSorter.SortBackToFront(indices,values,new int[indices.Length]);Assert.Equal(expected,indices);
    }
    private static void AssertOrder(GaussianProjectedValue[] values,int[] input)
    {
        var source=values.ToArray();var actual=input.ToArray();
        var expected=input.OrderByDescending(i=>values[i].Depth).ThenByDescending(i=>values[i].GlobalOrdinal).ToArray();
        var scratch=Enumerable.Repeat(-987654321,actual.Length+13).ToArray();
        GaussianDepthSorter.SortBackToFront(actual,values,scratch);
        Assert.Equal(expected,actual);Assert.Equal(source,values);
        Assert.All(scratch.Skip(actual.Length),x=>Assert.Equal(-987654321,x));
    }
}
