using OpenCleaner.Core.Tools;
using Xunit;

namespace OpenCleaner.Core.Tests.Analysis;

public sealed class TreemapLayoutTests
{
    [Fact]
    public void EmptyInputProducesNoTiles() =>
        Assert.Empty(TreemapLayout.Squarify([], 100, 100));

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-10, 100)]
    public void DegenerateSurfaceProducesNoTiles(double width, double height) =>
        Assert.Empty(TreemapLayout.Squarify([new TreemapInput("a", 10)], width, height));

    [Fact]
    public void SingleItemFillsTheSurface()
    {
        TreemapTile tile = Assert.Single(TreemapLayout.Squarify([new TreemapInput("seul", 1234)], 200, 100));

        Assert.Equal("seul", tile.Key);
        Assert.Equal(0, tile.X, 9);
        Assert.Equal(0, tile.Y, 9);
        Assert.Equal(200, tile.Width, 9);
        Assert.Equal(100, tile.Height, 9);
    }

    [Fact]
    public void AreasAreProportionalToValues()
    {
        var tiles = TreemapLayout.Squarify(
            [new TreemapInput("moitié", 50), new TreemapInput("quart-a", 25), new TreemapInput("quart-b", 25)],
            100, 100);

        Assert.Equal(3, tiles.Count);
        Assert.Equal(50 * 100, Area("moitié"), 3);
        Assert.Equal(25 * 100, Area("quart-a"), 3);
        Assert.Equal(25 * 100, Area("quart-b"), 3);

        double Area(string key) => tiles.Single(tile => tile.Key == key).Area;
    }

    [Fact]
    public void TilesFillTheWholeSurfaceWithoutOverlap()
    {
        var random = new Random(20261029);
        for (int iteration = 0; iteration < 8; iteration++)
        {
            var items = Enumerable.Range(0, 60)
                .Select(index => new TreemapInput("item" + index, random.Next(1, 100_000)))
                .ToList();
            const double width = 340;
            const double height = 210;
            var tiles = TreemapLayout.Squarify(items, width, height);

            Assert.Equal(items.Count, tiles.Count);
            Assert.Equal(width * height, tiles.Sum(tile => tile.Area), 3);
            AssertInside(tiles, width, height);
            AssertNoOverlap(tiles);
        }
    }

    [Fact]
    public void ManyEqualItemsRemainOrderedAndInside()
    {
        var tiles = TreemapLayout.Squarify(
            Enumerable.Range(0, 400).Select(index => new TreemapInput("f" + index, 7)).ToList(), 640, 480);

        Assert.Equal(400, tiles.Count);
        Assert.Equal(640 * 480, tiles.Sum(tile => tile.Area), 2);
        AssertInside(tiles, 640, 480);
        AssertNoOverlap(tiles);
    }

    [Fact]
    public void ZeroAndNegativeValuesAreIgnored()
    {
        var tiles = TreemapLayout.Squarify(
            [new TreemapInput("ok", 10), new TreemapInput("zéro", 0), new TreemapInput("négatif", -4)],
            50, 50);

        Assert.Equal("ok", Assert.Single(tiles).Key);
    }

    [Fact]
    public void SpacingKeepsTilesInsideAndSeparated()
    {
        var tiles = TreemapLayout.Squarify(
            [new TreemapInput("a", 40), new TreemapInput("b", 30), new TreemapInput("c", 30)],
            200, 200, spacing: 4);

        Assert.All(tiles, tile =>
        {
            Assert.True(tile.X >= -1e-9 && tile.Y >= -1e-9);
            Assert.True(tile.Right <= 200 + 1e-9);
            Assert.True(tile.Bottom <= 200 + 1e-9);
            Assert.True(tile.Width > 0 && tile.Height > 0);
        });
        AssertNoOverlap(tiles);
    }

    [Fact]
    public void LayoutIsDeterministic()
    {
        var items = new[] { new TreemapInput("a", 30), new TreemapInput("b", 20), new TreemapInput("c", 10) };
        Assert.Equal(TreemapLayout.Squarify(items, 90, 40), TreemapLayout.Squarify(items, 90, 40));
    }

    [Fact]
    public void TilesAreOrderedByDescendingValue()
    {
        var tiles = TreemapLayout.Squarify(
            [new TreemapInput("petit", 5), new TreemapInput("grand", 100), new TreemapInput("moyen", 20)],
            120, 80);

        Assert.Equal(new[] { "grand", "moyen", "petit" }, tiles.Select(tile => tile.Key));
    }

    private static void AssertInside(IReadOnlyList<TreemapTile> tiles, double width, double height)
    {
        Assert.All(tiles, tile =>
        {
            Assert.True(tile.X >= -1e-6, $"{tile.Key} sort à gauche ({tile.X})");
            Assert.True(tile.Y >= -1e-6, $"{tile.Key} sort en haut ({tile.Y})");
            Assert.True(tile.Right <= width + 1e-6, $"{tile.Key} sort à droite ({tile.Right} > {width})");
            Assert.True(tile.Bottom <= height + 1e-6, $"{tile.Key} sort en bas ({tile.Bottom} > {height})");
        });
    }

    private static void AssertNoOverlap(IReadOnlyList<TreemapTile> tiles)
    {
        for (int first = 0; first < tiles.Count; first++)
            for (int second = first + 1; second < tiles.Count; second++)
            {
                double overlapWidth = Math.Min(tiles[first].Right, tiles[second].Right) -
                                      Math.Max(tiles[first].X, tiles[second].X);
                double overlapHeight = Math.Min(tiles[first].Bottom, tiles[second].Bottom) -
                                       Math.Max(tiles[first].Y, tiles[second].Y);
                double overlap = Math.Max(0, overlapWidth) * Math.Max(0, overlapHeight);
                Assert.True(overlap <= 1e-6,
                    $"Chevauchement entre {tiles[first].Key} et {tiles[second].Key} : {overlap}");
            }
    }
}
