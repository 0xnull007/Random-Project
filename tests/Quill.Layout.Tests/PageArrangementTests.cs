using Quill.Layout;
using Xunit;

namespace Quill.Layout.Tests;

public class PageArrangementTests
{
    private static readonly SizeD Letter = new(816, 1056);
    private static readonly SizeD Landscape = new(1056, 816);

    [Fact]
    public void One_column_stacks_pages_and_centres_narrower_ones()
    {
        PageArrangement.Result result = PageArrangement.Compute([Letter, Landscape, Letter], columns: 1, padding: 24, gap: 24);
        Assert.Equal([24, 24 + 1056 + 24, 24 + 1056 + 24 + 816 + 24], result.Tops);
        Assert.Equal([24 + 120, 24, 24 + 120], result.Lefts);
        Assert.Equal(1056 + 48, result.Width);
        Assert.Equal(24 + 1056 + 24 + 816 + 24 + 1056 + 24, result.Height);
    }

    [Fact]
    public void Two_columns_make_rows_as_tall_as_their_tallest_page()
    {
        PageArrangement.Result result = PageArrangement.Compute([Letter, Landscape, Letter], columns: 2, padding: 10, gap: 20);
        Assert.Equal([10, 10, 10 + 1056 + 20], result.Tops);
        Assert.Equal([10 + 120, 10 + 1056 + 20, 10 + 120], result.Lefts); // cells are as wide as the widest page; portrait pages are centred in theirs
        Assert.Equal(10 + 1056 + 20 + 1056 + 10, result.Width);
        Assert.Equal(10 + 1056 + 20 + 1056 + 10, result.Height);
    }

    [Fact]
    public void Fewer_pages_than_columns_and_no_pages_stay_well_formed()
    {
        PageArrangement.Result single = PageArrangement.Compute([Letter], columns: 3, padding: 24, gap: 24);
        Assert.Equal([24.0], single.Tops);
        Assert.Equal(816 + 48, single.Width);
        Assert.Equal(1056 + 48, single.Height);

        PageArrangement.Result none = PageArrangement.Compute([], columns: 2, padding: 24, gap: 24);
        Assert.Empty(none.Tops);
        Assert.Equal(1, none.Width);
        Assert.Equal(1, none.Height);
    }
}
