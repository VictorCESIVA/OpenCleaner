using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using OpenCleaner.Contracts;
using OpenCleaner.Core.Tools;

namespace OpenCleaner.UI;

/// <summary>
/// Carte proportionnelle des enfants d'un dossier. Les pavés sont calculés par
/// <see cref="TreemapLayout"/> ; ce contrôle ne fait que dessiner et router les clics.
/// </summary>
public sealed class TreemapControl : FrameworkElement
{
    private const string ResidualKey = "\u0000résidu";
    private const string AggregateKey = "\u0000agrégat";

    private static readonly Typeface LabelTypeface = new(
        new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private static readonly Typeface DetailTypeface = new(
        new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private sealed record Tile(Rect Rect, DiskUsageNode Node, bool Navigable, string Label, string Detail);

    private List<Tile> _tiles = [];
    private Tile? _hovered;
    private DiskUsageNode? _selectedNode;

    public TreemapControl()
    {
        Focusable = true;
        SnapsToDevicePixels = true;
        ClipToBounds = true;
        AutomationProperties.SetHelpText(this,
            "Flèches : parcourir les zones. Entrée : ouvrir le dossier ou le fichier.");
        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        LostKeyboardFocus += (_, _) => SetHovered(null);
        IsKeyboardFocusWithinChanged += (_, _) =>
        {
            if (IsKeyboardFocusWithin && _selectedNode == null && _tiles.Count > 0)
            {
                _selectedNode = _tiles[0].Node;
                HoveredNodeChanged?.Invoke(this, _selectedNode);
            }
            InvalidateVisual();
        };
        Loaded += (_, _) => InvalidateVisual();
    }

    public static readonly DependencyProperty NodeProperty = DependencyProperty.Register(
        nameof(Node), typeof(DiskUsageNode), typeof(TreemapControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
            (d, _) => ((TreemapControl)d)._selectedNode = null));

    public DiskUsageNode? Node
    {
        get => (DiskUsageNode?)GetValue(NodeProperty);
        set => SetValue(NodeProperty, value);
    }

    public event EventHandler<DiskUsageNode>? NodeActivated;
    public event EventHandler<DiskUsageNode?>? HoveredNodeChanged;

    internal int RenderedTileCount => _tiles.Count;

    public void RefreshTheme() => InvalidateVisual();

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        new PointHitTestResult(this, hitTestParameters.HitPoint);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        _tiles = BuildTiles(ActualWidth, ActualHeight);
        if (IsKeyboardFocusWithin &&
            (_selectedNode == null || !_tiles.Any(tile => ReferenceEquals(tile.Node, _selectedNode))))
            _selectedNode = _tiles.Count > 0 ? _tiles[0].Node : null;
        var bounds = new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight));

        if (Node == null)
        {
            DrawCentered(drawingContext, bounds, "Lancez une analyse pour afficher la carte.");
            return;
        }
        if (Node.Size <= 0 || _tiles.Count == 0)
        {
            DrawCentered(drawingContext, bounds, "Ce dossier est vide ou inaccessible.");
            return;
        }

        var accent = ResourceColor("Accent", Colors.SeaGreen);
        var warning = ResourceColor("Warning", Colors.Goldenrod);
        var secondary = ResourceColor("TextSecond", Colors.Gray);
        var border = ResourceColor("BgCard", Colors.Black);
        var text = ResourceColor("TextPrimary", Colors.White);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        foreach (var tile in _tiles)
        {
            bool hovered = ReferenceEquals(_hovered?.Node, tile.Node);
            Color tint = tile.Node.IsProtected ? warning
                : tile.Node.IsFile ? secondary
                : accent;
            int rank = Math.Clamp((int)(tile.Rect.Height / 22), 0, 3);
            double opacity = hovered ? 0.88
                : tile.Node.IsProtected ? 0.20 + rank * 0.025
                : 0.32 + rank * 0.07;
            drawingContext.DrawRectangle(Brush(WithAlpha(tint, opacity)), null, tile.Rect);

            var inner = Rect.Inflate(tile.Rect, -1, -1);
            if (inner.Width <= 2 || inner.Height <= 2) continue;

            if (hovered)
                drawingContext.DrawRectangle(null, new Pen(Brush(WithAlpha(accent, 0.95)), 2),
                    Rect.Inflate(tile.Rect, -1, -1));

            if (inner.Width >= 46 && inner.Height >= 18)
            {
                var label = new FormattedText(tile.Label, CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, LabelTypeface, 11.5, Brush(WithAlpha(text, 1)), dpi)
                {
                    MaxTextWidth = Math.Max(1, inner.Width - 8),
                    MaxTextHeight = inner.Height,
                    Trimming = TextTrimming.CharacterEllipsis
                };
                drawingContext.DrawText(label, new Point(inner.X + 5, inner.Y + 4));

                if (inner.Height >= 36 && inner.Width >= 70)
                {
                    var detail = new FormattedText(tile.Detail, CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight, DetailTypeface, 10.5, Brush(WithAlpha(text, 0.75)), dpi)
                    {
                        MaxTextWidth = Math.Max(1, inner.Width - 8),
                        MaxTextHeight = Math.Max(1, inner.Height - 20),
                        Trimming = TextTrimming.CharacterEllipsis
                    };
                    drawingContext.DrawText(detail, new Point(inner.X + 5, inner.Y + 20));
                }
            }

            if (!hovered)
                drawingContext.DrawRectangle(null, new Pen(Brush(WithAlpha(
                    tile.Node.IsProtected ? warning : border, tile.Node.IsProtected ? 0.8 : 0.9)),
                    tile.Node.IsProtected ? 2 : 1), tile.Rect);

            if (IsKeyboardFocusWithin && ReferenceEquals(_selectedNode, tile.Node))
                drawingContext.DrawRectangle(null, new Pen(Brush(WithAlpha(text, 0.95)), 2),
                    Rect.Inflate(tile.Rect, -3, -3));
        }
    }

    private List<Tile> BuildTiles(double width, double height)
    {
        if (Node == null || width < 8 || height < 8 || Node.Size <= 0) return [];

        var segments = new List<(string Key, string Label, string Detail, DiskUsageNode Node, long Value, bool Navigable)>();
        foreach (var child in Node.Directories)
            if (child.Size > 0)
                segments.Add((child.FullPath, child.Name, Prefix(child) + SizeFormatter.Format(child.Size),
                    child, child.Size, true));
        foreach (var file in Node.Files)
            if (file.Size > 0)
                segments.Add((file.FullPath, file.Name, SizeFormatter.Format(file.Size),
                    file, file.Size, false));

        long listed = segments.Sum(segment => segment.Value);
        long remaining = Math.Max(0, Node.Size - listed);
        if (remaining > 0)
            segments.Add((ResidualKey, "Autres éléments",
                $"{SizeFormatter.Format(remaining)} non détaillés", Aggregate(Node, remaining), remaining, false));

        // Au-delà d'une centaine de pavés, la carte devient illisible : on regroupe le reste.
        const int maxTiles = 80;
        var ordered = segments.OrderByDescending(segment => segment.Value).ToList();
        if (ordered.Count > maxTiles)
        {
            long restValue = ordered.Skip(maxTiles - 1).Sum(segment => segment.Value);
            ordered = ordered.Take(maxTiles - 1).ToList();
            ordered.Add((AggregateKey, "Autres éléments", SizeFormatter.Format(restValue),
                Aggregate(Node, restValue), restValue, false));
        }

        var layout = TreemapLayout.Squarify(
            ordered.Select(segment => new TreemapInput(segment.Key, segment.Value)).ToList(),
            width, height, spacing: 3);
        var byKey = ordered.DistinctBy(segment => segment.Key)
            .ToDictionary(segment => segment.Key, segment => segment);

        _tiles = [];
        foreach (var tile in layout)
        {
            if (!byKey.TryGetValue(tile.Key, out var segment)) continue;
            _tiles.Add(new Tile(
                new Rect(tile.X, tile.Y, Math.Max(0, tile.Width), Math.Max(0, tile.Height)),
                segment.Node, segment.Navigable, segment.Label, segment.Detail));
        }
        return _tiles;
    }

    private static DiskUsageNode Aggregate(DiskUsageNode parent, long value) =>
        new("Autres éléments", parent.FullPath, isFile: false, size: value, isProtected: parent.IsProtected);

    private static string Prefix(DiskUsageNode node)
    {
        if (node.IsProtected) return "protégé · ";
        if (node.SkippedEntries > 0) return $"{node.SkippedEntries} ignoré(s) · ";
        return "";
    }

    private void DrawCentered(DrawingContext drawingContext, Rect bounds, string message)
    {
        var text = ResourceColor("TextSecond", Colors.Gray);
        var formatted = new FormattedText(message, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            DetailTypeface, 12.5, Brush(text), VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            TextAlignment = TextAlignment.Center,
            MaxTextWidth = Math.Max(1, bounds.Width)
        };
        drawingContext.DrawText(formatted,
            new Point(bounds.X, bounds.Y + bounds.Height / 2 - formatted.Height / 2));
    }

    private void OnMouseMove(object sender, MouseEventArgs e) => SetHovered(FindTile(e.GetPosition(this)));

    private void OnMouseLeave(object sender, MouseEventArgs e) => SetHovered(null);

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        var tile = FindTile(e.GetPosition(this));
        if (tile == null) return;
        _selectedNode = tile.Node;
        SetHovered(tile);
        InvalidateVisual();
        if (e.ClickCount >= 2 && tile.Navigable) NodeActivated?.Invoke(this, tile.Node);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_tiles.Count == 0) return;

        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var bounds = _tiles
                .Select(tile => new TreemapBounds(tile.Rect.X, tile.Rect.Y, tile.Rect.Width, tile.Rect.Height))
                .ToList();
            int current = _selectedNode == null
                ? -1
                : _tiles.FindIndex(tile => ReferenceEquals(tile.Node, _selectedNode));
            TreemapDirection direction = e.Key switch
            {
                Key.Left => TreemapDirection.Left,
                Key.Right => TreemapDirection.Right,
                Key.Up => TreemapDirection.Up,
                _ => TreemapDirection.Down
            };
            int next = TreemapNavigator.Move(bounds, current, direction);
            if (next >= 0 && next < _tiles.Count)
            {
                _selectedNode = _tiles[next].Node;
                HoveredNodeChanged?.Invoke(this, _selectedNode);
                InvalidateVisual();
                e.Handled = true;
            }
            return;
        }

        if (e.Key is Key.Enter or Key.Space)
        {
            DiskUsageNode? node = _selectedNode ?? _hovered?.Node;
            if (node != null)
            {
                NodeActivated?.Invoke(this, node);
                e.Handled = true;
            }
        }
    }

    private void SetHovered(Tile? tile)
    {
        if (ReferenceEquals(tile, _hovered)) return;
        _hovered = tile;
        Cursor = tile is { Navigable: true } ? Cursors.Hand : Cursors.Arrow;
        HoveredNodeChanged?.Invoke(this, tile?.Node);
        InvalidateVisual();
    }

    private Tile? FindTile(Point point)
    {
        foreach (var tile in _tiles)
            if (tile.Rect.Contains(point)) return tile;
        return null;
    }

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color WithAlpha(Color color, double opacity) =>
        Color.FromArgb((byte)Math.Clamp((int)Math.Round(Math.Clamp(opacity, 0, 1) * 255), 0, 255),
            color.R, color.G, color.B);

    private Color ResourceColor(string key, Color fallback) =>
        TryFindResource(key) is SolidColorBrush brush ? brush.Color : fallback;
}
