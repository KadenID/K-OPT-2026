using Kopt2026.Application;
using Kopt2026.Language;
using Kopt2026.Solver.Abstractions;
using Kopt2026.Solver.Highs;

namespace Kopt2026.Tests;

public sealed class SensitivityAnalysisTests
{
    private static KoptEngine CreateEngine() => new(new OptParser(), new HighsSolverAdapter());

    [Fact]
    public async Task Calculates_Known_Objective_And_RightHandSide_Ranges()
    {
        var run = await CreateEngine().RunAsync(SourceDocument.FromText(
            "MAX\n3 X1 + 2 X2\nST\nC1: 2 X1 + X2 <= 10\nC2: X1 + 3 X2 <= 12\nEND\n"),
            new SolveOptions(), CancellationToken.None);

        var sensitivity = Assert.IsType<SensitivityAnalysisInfo>(run.Solve!.Sensitivity);
        Assert.True(sensitivity.IsAvailable, sensitivity.Message);

        AssertRange(sensitivity.ObjectiveCoefficients, "X1", 3, 2d / 3d, 4);
        AssertRange(sensitivity.ObjectiveCoefficients, "X2", 2, 1.5, 9);
        AssertRange(sensitivity.RightHandSides, "C1", 10, 4, 24);
        AssertRange(sensitivity.RightHandSides, "C2", 12, 5, 30);
    }

    [Fact]
    public async Task Calculates_Minimization_Ranges_And_Contains_Current_Values()
    {
        var run = await CreateEngine().RunAsync(SourceDocument.FromText(
            "MIN\n3 X1 + 2 X2\nST\nC1: 2 X1 + X2 >= 10\nC2: X1 + 3 X2 >= 12\nEND\n"),
            new SolveOptions(), CancellationToken.None);

        var sensitivity = Assert.IsType<SensitivityAnalysisInfo>(run.Solve!.Sensitivity);
        Assert.True(sensitivity.IsAvailable, sensitivity.Message);
        Assert.All(sensitivity.ObjectiveCoefficients, AssertContainsCurrent);
        Assert.All(sensitivity.RightHandSides, AssertContainsCurrent);
    }

    [Fact]
    public async Task Reports_Mip_As_Unsupported()
    {
        var run = await CreateEngine().RunAsync(SourceDocument.FromText(
            "MAX\nX1\nST\nX1 <= 5\nINTEGER X1\nEND\n"),
            new SolveOptions(), CancellationToken.None);

        var sensitivity = Assert.IsType<SensitivityAnalysisInfo>(run.Solve!.Sensitivity);
        Assert.False(sensitivity.IsAvailable);
        Assert.Contains("연속형 선형계획", sensitivity.Message);
        Assert.Empty(sensitivity.ObjectiveCoefficients);
        Assert.Empty(sensitivity.RightHandSides);
    }

    [Fact]
    public async Task Preserves_Infinite_Range_For_Inactive_Constraint()
    {
        var run = await CreateEngine().RunAsync(SourceDocument.FromText(
            "MAX\nX1\nST\nACTIVE: X1 <= 10\nINACTIVE: X1 <= 20\nEND\n"),
            new SolveOptions(), CancellationToken.None);

        var sensitivity = Assert.IsType<SensitivityAnalysisInfo>(run.Solve!.Sensitivity);
        Assert.True(sensitivity.IsAvailable, sensitivity.Message);
        var inactive = Assert.Single(sensitivity.RightHandSides, item => item.Name == "INACTIVE");
        Assert.Equal(10, inactive.LowerValue, 7);
        Assert.True(double.IsPositiveInfinity(inactive.UpperValue));
    }

    [Fact]
    public async Task Preserves_Infinite_Range_For_Inactive_GreaterOrEqual_Constraint()
    {
        var run = await CreateEngine().RunAsync(SourceDocument.FromText(
            "MIN\nX1\nST\nACTIVE: X1 >= 10\nINACTIVE: X1 >= 5\nEND\n"),
            new SolveOptions(), CancellationToken.None);

        var sensitivity = Assert.IsType<SensitivityAnalysisInfo>(run.Solve!.Sensitivity);
        Assert.True(sensitivity.IsAvailable, sensitivity.Message);
        var inactive = Assert.Single(sensitivity.RightHandSides, item => item.Name == "INACTIVE");
        Assert.True(double.IsNegativeInfinity(inactive.LowerValue));
        Assert.Equal(10, inactive.UpperValue, 7);
    }

    [Fact]
    public async Task Handles_Equality_Constraint_Ranges()
    {
        var run = await CreateEngine().RunAsync(SourceDocument.FromText(
            "MIN\nX1\nST\nFIXED: X1 = 5\nEND\n"),
            new SolveOptions(), CancellationToken.None);

        var sensitivity = Assert.IsType<SensitivityAnalysisInfo>(run.Solve!.Sensitivity);
        Assert.True(sensitivity.IsAvailable, sensitivity.Message);
        Assert.All(sensitivity.ObjectiveCoefficients, AssertContainsCurrent);
        Assert.All(sensitivity.RightHandSides, AssertContainsCurrent);
    }

    [Fact]
    public async Task Reports_NonOptimal_Result_As_Unavailable()
    {
        var run = await CreateEngine().RunAsync(SourceDocument.FromText(
            "MAX\nX1\nST\nX1 >= 0\nEND\n"),
            new SolveOptions(), CancellationToken.None);

        var sensitivity = Assert.IsType<SensitivityAnalysisInfo>(run.Solve!.Sensitivity);
        Assert.False(sensitivity.IsAvailable);
        Assert.Contains("최적성이 증명", sensitivity.Message);
    }

    [Fact]
    public async Task Can_Disable_Sensitivity_For_Auxiliary_Solves()
    {
        var run = await CreateEngine().RunAsync(SourceDocument.FromText(
            "MAX\nX1\nST\nX1 <= 5\nEND\n"),
            new SolveOptions(ComputeSensitivity: false), CancellationToken.None);

        Assert.Null(run.Solve!.Sensitivity);
    }

    private static void AssertRange(IReadOnlyList<SensitivityRange> ranges, string name, double current, double lower, double upper)
    {
        var range = Assert.Single(ranges, item => item.Name == name);
        Assert.Equal(current, range.CurrentValue, 7);
        Assert.Equal(lower, range.LowerValue, 7);
        Assert.Equal(upper, range.UpperValue, 7);
    }

    private static void AssertContainsCurrent(SensitivityRange range)
    {
        Assert.True(range.LowerValue <= range.CurrentValue + 1e-7,
            $"{range.Name}: lower {range.LowerValue} exceeds current {range.CurrentValue}.");
        Assert.True(range.UpperValue >= range.CurrentValue - 1e-7,
            $"{range.Name}: upper {range.UpperValue} is below current {range.CurrentValue}.");
    }
}
