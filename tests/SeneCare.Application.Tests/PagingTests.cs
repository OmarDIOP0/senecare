using SeneCare.Application.Common;

namespace SeneCare.Application.Tests;

public class PagingTests
{
    private static readonly IReadOnlyList<int> Hundred = [.. Enumerable.Range(1, 120)];

    [Fact]
    public void A_list_in_memory_is_cut_in_pages_of_fifty()
    {
        var first = Paging.Of(Hundred, 1);
        Assert.Equal(50, first.Count);
        Assert.Equal(1, first.Items[0]);
        Assert.True(first.HasNext);
        Assert.False(first.HasPrevious);

        var last = Paging.Of(Hundred, 3);
        Assert.Equal(20, last.Count);
        Assert.Equal(101, last.Items[0]);
        Assert.False(last.HasNext);
        Assert.True(last.HasPrevious);
    }

    [Fact]
    public void Out_of_range_pages_are_clamped_or_empty()
    {
        Assert.Equal(1, Paging.Of(Hundred, -4).Number);
        Assert.Empty(Paging.Of(Hundred, 9).Items);
        Assert.False(Paging.Of(Hundred, 9).HasNext);
    }
}
