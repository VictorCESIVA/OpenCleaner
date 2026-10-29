using OpenCleaner.Core.Tools;
using Xunit;

namespace OpenCleaner.Core.Tests.Analysis;

public sealed class TreemapNavigatorTests
{
    private static readonly IReadOnlyList<TreemapBounds> Grid =
    [
        new(0, 0, 50, 50),
        new(50, 0, 50, 50),
        new(0, 50, 50, 50),
        new(50, 50, 50, 50)
    ];

    [Theory]
    [InlineData(0, TreemapDirection.Right, 1)]
    [InlineData(0, TreemapDirection.Down, 2)]
    [InlineData(1, TreemapDirection.Left, 0)]
    [InlineData(1, TreemapDirection.Down, 3)]
    [InlineData(2, TreemapDirection.Right, 3)]
    [InlineData(2, TreemapDirection.Up, 0)]
    [InlineData(3, TreemapDirection.Left, 2)]
    [InlineData(3, TreemapDirection.Up, 1)]
    public void MovesToTheAdjacentTile(int current, TreemapDirection direction, int expected) =>
        Assert.Equal(expected, TreemapNavigator.Move(Grid, current, direction));

    [Fact]
    public void StaysOnEdgeWhenNoCandidateExists()
    {
        Assert.Equal(1, TreemapNavigator.Move(Grid, 1, TreemapDirection.Right));
        Assert.Equal(2, TreemapNavigator.Move(Grid, 2, TreemapDirection.Left));
        Assert.Equal(0, TreemapNavigator.Move(Grid, 0, TreemapDirection.Up));
        Assert.Equal(3, TreemapNavigator.Move(Grid, 3, TreemapDirection.Down));
    }

    [Fact]
    public void PrefersTheAlignedCandidateOverADiagonalOne()
    {
        var tiles = new[]
        {
            new TreemapBounds(0, 0, 10, 10),
            new TreemapBounds(100, 0, 10, 10),
            new TreemapBounds(50, 80, 10, 10)
        };

        Assert.Equal(1, TreemapNavigator.Move(tiles, 0, TreemapDirection.Right));
        Assert.Equal(2, TreemapNavigator.Move(tiles, 0, TreemapDirection.Down));
    }

    [Fact]
    public void SingleTileOrInvalidSelectionIsHandled()
    {
        Assert.Equal(0, TreemapNavigator.Move([new TreemapBounds(0, 0, 10, 10)], 0, TreemapDirection.Left));
        Assert.Equal(0, TreemapNavigator.Move(Grid, -1, TreemapDirection.Right));
        Assert.Equal(0, TreemapNavigator.Move(Grid, 99, TreemapDirection.Right));
        Assert.Equal(-1, TreemapNavigator.Move([], 0, TreemapDirection.Right));
    }

    [Fact]
    public void UsesCentersNotEdges()
    {
        // Le pavé 1 touche le bord droit du pavé 0, mais son centre est à droite.
        var tiles = new[]
        {
            new TreemapBounds(0, 0, 100, 100),
            new TreemapBounds(100, 200, 20, 20)
        };

        Assert.Equal(1, TreemapNavigator.Move(tiles, 0, TreemapDirection.Right));
    }
}
