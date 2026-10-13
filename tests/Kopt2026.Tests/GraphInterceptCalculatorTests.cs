using Kopt2026.Application;
using Kopt2026.Language;

namespace Kopt2026.Tests;

public sealed class GraphInterceptCalculatorTests
{
    [Fact]
    public void Calculates_Constraint_And_Objective_Intercepts()
    {
        var model = new OptParser().Parse(SourceDocument.FromText(
            "MAX\n3 X1 + 2 X2 + 5\nST\nCAPACITY: 2 X1 + X2 <= 10\nDEMAND: X1 + 3 X2 <= 12\nEND\n")).Model!;

        var lines = GraphInterceptCalculator.Calculate(model, 21.4);

        Assert.Equal(3, lines.Count);
        Assert.Equal(5, lines[0].XIntercept!.Value, 10);
        Assert.Equal(10, lines[0].YIntercept!.Value, 10);
        Assert.Equal(12, lines[1].XIntercept!.Value, 10);
        Assert.Equal(4, lines[1].YIntercept!.Value, 10);
        Assert.Equal(16.4 / 3, lines[2].XIntercept!.Value, 10);
        Assert.Equal(8.2, lines[2].YIntercept!.Value, 10);
        Assert.True(lines[2].IsObjective);
    }

    [Fact]
    public void Reports_Missing_Intercept_For_Parallel_Axis()
    {
        var horizontal = GraphInterceptCalculator.CalculateLine("H", 0, 2, 10, false);
        var vertical = GraphInterceptCalculator.CalculateLine("V", 4, 0, 20, false);

        Assert.Null(horizontal.XIntercept);
        Assert.Equal(5, horizontal.YIntercept);
        Assert.Equal(5, vertical.XIntercept);
        Assert.Null(vertical.YIntercept);
    }

    [Fact]
    public void Preserves_Negative_Intercepts_For_Summary()
    {
        var line = GraphInterceptCalculator.CalculateLine("N", -2, 4, 8, false);
        Assert.Equal(-4, line.XIntercept);
        Assert.Equal(2, line.YIntercept);
    }
}
