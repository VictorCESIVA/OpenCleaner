namespace OpenCleaner.Core.Tools;

public enum TreemapDirection
{
    Left,
    Right,
    Up,
    Down
}

public readonly record struct TreemapBounds(double X, double Y, double Width, double Height)
{
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
}

/// <summary>
/// Déplacement clavier entre les pavés d'une carte : privilégie le même axe,
/// puis la distance, sans jamais quitter la carte.
/// </summary>
public static class TreemapNavigator
{
    private const double AxisEpsilon = 0.5;

    public static int Move(IReadOnlyList<TreemapBounds> tiles, int current, TreemapDirection direction)
    {
        if (tiles == null || tiles.Count == 0) return -1;
        if (current < 0 || current >= tiles.Count) return 0;

        TreemapBounds origin = tiles[current];
        int bestIndex = current;
        double bestScore = double.MaxValue;
        for (int index = 0; index < tiles.Count; index++)
        {
            if (index == current) continue;
            TreemapBounds candidate = tiles[index];
            double dx = candidate.CenterX - origin.CenterX;
            double dy = candidate.CenterY - origin.CenterY;
            bool inDirection = direction switch
            {
                TreemapDirection.Left => dx < -AxisEpsilon,
                TreemapDirection.Right => dx > AxisEpsilon,
                TreemapDirection.Up => dy < -AxisEpsilon,
                TreemapDirection.Down => dy > AxisEpsilon,
                _ => false
            };
            if (!inDirection) continue;

            bool horizontal = direction is TreemapDirection.Left or TreemapDirection.Right;
            double primary = Math.Abs(horizontal ? dx : dy);
            double secondary = Math.Abs(horizontal ? dy : dx);
            double score = primary + secondary * 2;
            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = index;
            }
        }
        return bestIndex;
    }
}
