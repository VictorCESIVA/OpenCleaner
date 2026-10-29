namespace OpenCleaner.Core.Tools;

public sealed record TreemapInput(string Key, long Value);

public sealed record TreemapTile(string Key, double X, double Y, double Width, double Height)
{
    public double Area => Width * Height;
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

/// <summary>
/// Algorithme « squarified treemap » pur : transforme des valeurs en pavés qui remplissent
/// exactement une surface, en privilégiant des proportions proches du carré.
/// </summary>
public static class TreemapLayout
{
    public static IReadOnlyList<TreemapTile> Squarify(
        IReadOnlyList<TreemapInput> items, double width, double height, double spacing = 0)
    {
        if (items == null || items.Count == 0 || width <= 0 || height <= 0) return [];

        var values = items
            .Where(item => item.Value > 0)
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToList();
        if (values.Count == 0) return [];

        double total = values.Sum(item => (double)item.Value);
        double scale = width * height / total;
        var areas = values.Select(item => (item.Key, Area: item.Value * scale)).ToList();

        var tiles = new List<TreemapTile>(areas.Count);
        double freeX = 0, freeY = 0, freeWidth = width, freeHeight = height;
        int index = 0;
        while (index < areas.Count && freeWidth > 0 && freeHeight > 0)
        {
            double shortSide = Math.Min(freeWidth, freeHeight);
            var row = new List<(string Key, double Area)>();
            double rowArea = 0;
            double bestWorst = double.PositiveInfinity;

            while (index < areas.Count)
            {
                double candidate = areas[index].Area;
                double candidateRowArea = rowArea + candidate;
                double worst = Worst(row, candidate, candidateRowArea, shortSide);
                if (row.Count > 0 && worst > bestWorst + 1e-12) break;
                row.Add(areas[index]);
                rowArea = candidateRowArea;
                bestWorst = worst;
                index++;
            }

            if (row.Count == 0) break;
            if (freeWidth >= freeHeight)
            {
                double thickness = rowArea / freeHeight;
                if (thickness <= 0) break;
                double y = freeY;
                foreach (var item in row)
                {
                    double itemHeight = Math.Min(item.Area / thickness, freeY + freeHeight - y);
                    Add(tiles, item.Key, freeX, y, thickness, itemHeight, width, height);
                    y += itemHeight;
                }
                freeX += thickness;
                freeWidth -= thickness;
            }
            else
            {
                double thickness = rowArea / freeWidth;
                if (thickness <= 0) break;
                double x = freeX;
                foreach (var item in row)
                {
                    double itemWidth = Math.Min(item.Area / thickness, freeX + freeWidth - x);
                    Add(tiles, item.Key, x, freeY, itemWidth, thickness, width, height);
                    x += itemWidth;
                }
                freeY += thickness;
                freeHeight -= thickness;
            }

            freeWidth = Math.Max(0, freeWidth);
            freeHeight = Math.Max(0, freeHeight);
        }

        if (spacing <= 0) return tiles;
        return tiles.Select(tile => Inset(tile, spacing / 2, width, height)).ToList();
    }

    private static void Add(List<TreemapTile> tiles, string key, double x, double y, double width,
        double height, double totalWidth, double totalHeight)
    {
        if (width <= 0 || height <= 0) return;
        if (x < 0) { width += x; x = 0; }
        if (y < 0) { height += y; y = 0; }
        if (x > totalWidth || y > totalHeight) return;
        width = Math.Min(width, totalWidth - x);
        height = Math.Min(height, totalHeight - y);
        if (width <= 0 || height <= 0) return;
        tiles.Add(new TreemapTile(key, x, y, width, height));
    }

    private static TreemapTile Inset(TreemapTile tile, double margin, double totalWidth, double totalHeight)
    {
        double x = Math.Min(tile.X + margin, totalWidth);
        double y = Math.Min(tile.Y + margin, totalHeight);
        double width = Math.Max(0, Math.Min(tile.Width - margin * 2, totalWidth - x));
        double height = Math.Max(0, Math.Min(tile.Height - margin * 2, totalHeight - y));
        return new TreemapTile(tile.Key, x, y, width, height);
    }

    /// <summary>Pire rapport d'aspect obtenu si l'on ajoute une surface à la rangée courante.</summary>
    private static double Worst(List<(string Key, double Area)> row, double candidate, double sum, double side)
    {
        double min = candidate;
        double max = candidate;
        foreach (var item in row)
        {
            if (item.Area < min) min = item.Area;
            if (item.Area > max) max = item.Area;
        }
        if (min <= 0 || sum <= 0) return double.PositiveInfinity;
        double sideSquared = side * side;
        double sumSquared = sum * sum;
        return Math.Max(sideSquared * max / sumSquared, sumSquared / (sideSquared * min));
    }
}
