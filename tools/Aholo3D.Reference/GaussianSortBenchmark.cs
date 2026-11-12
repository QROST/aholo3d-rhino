using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aholo3D.Core;

internal static class GaussianSortBenchmark
{
    public static int Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var cases=new List<object>();
        foreach(int count in new[]{4096,65536,262144,1048576})
        foreach(string distribution in new[]{"random","depth-ties","coherent","reverse-depth","shuffled-ordinals"})
        {
            var random=new Random(731);var values=new GaussianProjectedValue[count];
            var original=Enumerable.Range(0,count).ToArray();var actual=new int[count];var baseline=new int[count];var scratch=new int[count];
            for(int i=0;i<count;i++)
            {
                double depth=distribution switch {"depth-ties"=>1+i%137,"coherent"=>1000.0-i/(double)count,"reverse-depth"=>1+i/(double)count,_=>1+random.NextDouble()*1000};
                long ordinal=distribution=="shuffled-ordinals"?random.NextInt64():i;
                values[i]=new(0,0,depth,1,0,1,new(1,1,1),.5,1,0,0,1,3,i%3,i,ordinal);
            }
            int[] expected=original.OrderByDescending(i=>values[i].Depth).ThenByDescending(i=>values[i].GlobalOrdinal).ToArray();
            // Warm both complete implementations on the exact case before sampling, alternate sample order.
            original.CopyTo(actual,0);GaussianDepthSorter.SortBackToFront(actual,values,scratch);
            original.CopyTo(baseline,0);GaussianDepthSorterAlpha24.SortBackToFront(baseline,values,scratch);
            var currentTimes=new List<double>();var baselineTimes=new List<double>();long allocated=0;
            for(int run=0;run<5;run++)
            {
                void Current(){original.CopyTo(actual,0);long before=GC.GetAllocatedBytesForCurrentThread();long start=Stopwatch.GetTimestamp();GaussianDepthSorter.SortBackToFront(actual,values,scratch);double ms=Stopwatch.GetElapsedTime(start).TotalMilliseconds;allocated+=GC.GetAllocatedBytesForCurrentThread()-before;currentTimes.Add(ms);}
                void Previous(){original.CopyTo(baseline,0);long start=Stopwatch.GetTimestamp();GaussianDepthSorterAlpha24.SortBackToFront(baseline,values,scratch);baselineTimes.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);}
                if(run%2==0){Current();Previous();}else{Previous();Current();}
                if(!actual.SequenceEqual(expected)||!baseline.SequenceEqual(expected))throw new InvalidDataException("Sort disagrees with independent stable LINQ ordering.");
            }
            cases.Add(new {Count=count,Distribution=distribution,CurrentMedianMs=currentTimes.Order().ElementAt(2),PreviousMedianMs=baselineTimes.Order().ElementAt(2),CurrentSamplesMs=currentTimes,PreviousSamplesMs=baselineTimes,SteadyStateAllocatedBytes=allocated,IndexSha256=Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(actual.AsSpan())))});
        }
        var report=new {Scope="Synthetic offline CPU sort only; no Rhino, GPU upload/draw or navigation FPS claim.",Runtime=RuntimeInformation.FrameworkDescription,TieredCompilation=Environment.GetEnvironmentVariable("DOTNET_TieredCompilation")??"runtime default",Architecture=RuntimeInformation.ProcessArchitecture.ToString(),OS=RuntimeInformation.OSDescription,CountOfProjectedValueBytes=Marshal.SizeOf<GaussianProjectedValue>(),Samples=5,Cases=cases};
        string path=Path.Combine(directory,"gaussian-sort.json");File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(path);return 0;
    }
}
