using Aholo3D.Core;
namespace Aholo3D.Tests;

public sealed class TextureMappingSelectionTests
{
    public static IEnumerable<object?[]> Choices()
    {
        yield return new object?[]{7,false,new[]{7,1},Array.Empty<int>(),7};
        yield return new object?[]{7,false,new[]{1},Array.Empty<int>(),1};
        yield return new object?[]{7,false,new[]{3},Array.Empty<int>(),null};
        yield return new object?[]{1,false,Array.Empty<int>(),Array.Empty<int>(),null};
        yield return new object?[]{7,false,new[]{7},new[]{1},7};
        yield return new object?[]{7,false,new[]{7},new[]{3},7};
        yield return new object?[]{7,false,new[]{1},new[]{3},1};
        yield return new object?[]{7,true,new[]{7,1},new[]{7,1},null};
        yield return new object?[]{-2,true,Array.Empty<int>(),new[]{1},null};
        yield return new object?[]{-100,false,new[]{-100},Array.Empty<int>(),-100};
    }
    [Theory][MemberData(nameof(Choices))]
    public void ChoosesTheRequestedFallbackOrExplicitSurfaceSource(int requested,bool surface,int[] own,int[] parents,int? expected)
    {Assert.Equal(expected,TextureMappingSelection.Select(requested,surface,own,parents));}
    [Theory]
    [InlineData(7,new[]{7},new[]{7})]
    [InlineData(7,new[]{1},new[]{7})]
    [InlineData(7,new[]{1},new[]{1})]
    [InlineData(1,new int[]{},new[]{1})]
    [InlineData(0,new[]{1},new[]{1})]
    public void ParentAssignedEffectiveChannelCannotSilentlyBecomeLeafUv(int request,int[] own,int[] parents)
    {Assert.Throws<InvalidOperationException>(()=>TextureMappingSelection.Select(request,false,own,parents));}
    [Fact]
    public void ChoiceDoesNotMutateEitherInstancesChannelList()
    {
        int[] own={7,1},parent={3};_ = TextureMappingSelection.Select(7,false,own,parent);
        Assert.Equal(new[]{7,1},own);Assert.Equal(new[]{3},parent);
    }
    [Fact]
    public void LegacyChannelZeroIsNotSilentlyReinterpretedAsCustomChannelOne()
    {Assert.Throws<InvalidOperationException>(()=>TextureMappingSelection.Select(0,false,new[]{1},Array.Empty<int>()));}
}
