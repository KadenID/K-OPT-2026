using System.Windows;
using Kopt2026.App;

namespace Kopt2026.Tests;

public sealed class GraphLabelLayoutTests
{
    [Fact]
    public void Places_Clustered_X_Labels_Without_Overlap()
    {
        var bounds = new Rect(0, 0, 420, 260);
        var occupied = new List<Rect>();
        var placements = Enumerable.Range(0, 12)
            .Select(_ => GraphLabelLayout.Place(new Size(82, 16), new Point(210, 260), bounds, GraphLabelAxis.X, occupied))
            .ToList();

        Assert.All(placements, placement => Assert.NotNull(placement));
        AssertNoOverlap(placements.Select(placement => placement!.Value));
        Assert.All(placements, placement => Assert.True(bounds.Contains(placement!.Value)));
    }

    [Fact]
    public void Shares_Occupied_Areas_Between_X_And_Y_Labels()
    {
        var bounds = new Rect(0, 0, 520, 300);
        var occupied = new List<Rect>();
        var placements = new List<Rect?>();
        for (var i = 0; i < 7; i++)
            placements.Add(GraphLabelLayout.Place(new Size(88, 16), new Point(30, 300), bounds, GraphLabelAxis.X, occupied));
        for (var i = 0; i < 7; i++)
            placements.Add(GraphLabelLayout.Place(new Size(88, 16), new Point(0, 265), bounds, GraphLabelAxis.Y, occupied));

        Assert.All(placements, placement => Assert.NotNull(placement));
        AssertNoOverlap(placements.Select(placement => placement!.Value));
    }

    [Fact]
    public void Returns_Null_Instead_Of_Overlapping_When_No_Space_Remains()
    {
        var bounds = new Rect(0, 0, 100, 30);
        var occupied = new List<Rect>();
        var first = GraphLabelLayout.Place(new Size(96, 26), new Point(50, 30), bounds, GraphLabelAxis.X, occupied);
        var second = GraphLabelLayout.Place(new Size(96, 26), new Point(50, 30), bounds, GraphLabelAxis.X, occupied);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    private static void AssertNoOverlap(IEnumerable<Rect> rectangles)
    {
        var items = rectangles.ToList();
        for (var i = 0; i < items.Count; i++)
            for (var j = i + 1; j < items.Count; j++)
                Assert.False(items[i].IntersectsWith(items[j]), $"Label rectangles overlap: {items[i]} and {items[j]}");
    }
}
