using System.Windows;

namespace Kopt2026.App;

public enum GraphLabelAxis { X, Y }

public static class GraphLabelLayout
{
    public static Rect? Place(
        Size labelSize,
        Point anchor,
        Rect bounds,
        GraphLabelAxis axis,
        ICollection<Rect> occupiedAreas,
        double clearance = 3)
    {
        if (labelSize.Width <= 0 || labelSize.Height <= 0 ||
            labelSize.Width > bounds.Width || labelSize.Height > bounds.Height) return null;

        foreach (var candidate in Candidates(labelSize, anchor, bounds, axis, clearance))
        {
            var collisionProbe = candidate;
            collisionProbe.Inflate(clearance, clearance);
            if (occupiedAreas.Any(area => area.IntersectsWith(collisionProbe))) continue;
            occupiedAreas.Add(candidate);
            return candidate;
        }

        return null;
    }

    private static IEnumerable<Rect> Candidates(
        Size size,
        Point anchor,
        Rect bounds,
        GraphLabelAxis axis,
        double clearance)
    {
        var seen = new HashSet<(long Left, long Top)>();
        if (axis == GraphLabelAxis.X)
        {
            var preferredLeft = Math.Clamp(anchor.X - size.Width / 2, bounds.Left, bounds.Right - size.Width);
            var horizontalStep = Math.Max(12, size.Width * 0.55 + clearance);
            var horizontalCount = Math.Max(1, (int)Math.Ceiling(bounds.Width / horizontalStep) + 1);
            var rowStep = size.Height + clearance * 2;
            var rowCount = Math.Max(1, (int)Math.Ceiling(bounds.Height / rowStep) + 1);
            foreach (var offset in AlternatingOffsets(horizontalCount))
            {
                var left = Math.Clamp(preferredLeft + offset * horizontalStep, bounds.Left, bounds.Right - size.Width);
                for (var row = 0; row < rowCount; row++)
                {
                    var top = bounds.Bottom - size.Height - clearance - row * rowStep;
                    if (top < bounds.Top) top = bounds.Top;
                    if (TryRemember(left, top)) yield return new(left, top, size.Width, size.Height);
                }
            }
        }
        else
        {
            var preferredTop = Math.Clamp(anchor.Y - size.Height / 2, bounds.Top, bounds.Bottom - size.Height);
            var verticalStep = size.Height + clearance * 2;
            var verticalCount = Math.Max(1, (int)Math.Ceiling(bounds.Height / verticalStep) + 1);
            var columnStep = size.Width + clearance * 2;
            var columnCount = Math.Max(1, (int)Math.Ceiling(bounds.Width / columnStep) + 1);
            foreach (var offset in AlternatingOffsets(verticalCount))
            {
                var top = Math.Clamp(preferredTop + offset * verticalStep, bounds.Top, bounds.Bottom - size.Height);
                for (var column = 0; column < columnCount; column++)
                {
                    var left = bounds.Left + clearance + column * columnStep;
                    if (left + size.Width > bounds.Right) left = bounds.Right - size.Width;
                    if (TryRemember(left, top)) yield return new(left, top, size.Width, size.Height);
                }
            }
        }

        bool TryRemember(double left, double top) =>
            seen.Add(((long)Math.Round(left * 1000), (long)Math.Round(top * 1000)));
    }

    private static IEnumerable<int> AlternatingOffsets(int count)
    {
        yield return 0;
        for (var offset = 1; offset <= count; offset++)
        {
            yield return -offset;
            yield return offset;
        }
    }
}
