using Kopt2026.Application;
using Kopt2026.Language;
using Kopt2026.Solver.Abstractions;
using Kopt2026.Solver.Highs;

namespace Kopt2026.Tests;

public sealed class SolverTests
{
    [Fact]
    public async Task Solves_Basic_Lp()
    {
        var engine = new KoptEngine(new OptParser(), new HighsSolverAdapter());
        var source = SourceDocument.FromText("MAX\n3 X1 + 2 X2\nST\n2 X1 + X2 <= 10\nX1 + 3 X2 <= 12\nEND\n");
        var result = await engine.RunAsync(source, new SolveOptions(), CancellationToken.None);
        Assert.Equal(SolveStatus.Optimal, result.Solve!.Status);
        Assert.Equal(16.4, result.Solve.ObjectiveValue!.Value, 7);
        Assert.NotNull(result.Solve.SimplexIterationCount);
        Assert.True(result.Solve.SimplexIterationCount >= 0);
        Assert.Contains("목적함수 값 : 16.40000", LegacyKoptReportFormatter.Format(result.Solve));
        Assert.Equal(OptimumMultiplicity.Unique, result.Solve.AlternativeOptima!.Multiplicity);
    }

    [Theory]
    [InlineData("17 X1 + 10 X2 + 21")]
    [InlineData("17 X1 +\n  10 X2\n# constant\n + 21")]
    public async Task Solves_User_Model_With_Objective_Constant(string objective)
    {
        var engine = new KoptEngine(new OptParser(), new HighsSolverAdapter());
        var source = SourceDocument.FromText($"MAX\n{objective}\nST\n2 X1 + X2 <= 1497\nX1 + X2 <= 1199\nX1 <= 499\nEND\n");
        var result = await engine.RunAsync(source, new SolveOptions(), CancellationToken.None);

        Assert.True(result.Parse.IsValid);
        Assert.Equal(21, result.Parse.Model!.Objective.Constant, 10);
        Assert.Equal(SolveStatus.Optimal, result.Solve!.Status);
        Assert.Equal(14_097, result.Solve.ObjectiveValue!.Value, 7);
        Assert.Equal(298, result.Solve.Variables.Single(value => value.Name == "X1").Value, 7);
        Assert.Equal(901, result.Solve.Variables.Single(value => value.Name == "X2").Value, 7);
    }

    [Fact]
    public async Task Detects_Continuous_Alternative_Optima_And_Ranges()
    {
        var engine = new KoptEngine(new OptParser(), new HighsSolverAdapter());
        var source = SourceDocument.FromText("MAX\nX1 + X2 + 21\nST\nX1 + X2 <= 10\nEND\n");
        var result = await engine.RunAsync(source, new SolveOptions(), CancellationToken.None);

        Assert.Equal(31, result.Solve!.ObjectiveValue!.Value, 7);
        var alternatives = result.Solve!.AlternativeOptima!;
        Assert.Equal(OptimumMultiplicity.Multiple, alternatives.Multiplicity);
        Assert.True(alternatives.IsContinuousFamily);
        Assert.True(alternatives.IsComplete);
        Assert.Collection(alternatives.VariableRanges.OrderBy(range => range.Name),
            range => { Assert.Equal(0, range.Minimum!.Value, 7); Assert.Equal(10, range.Maximum!.Value, 7); },
            range => { Assert.Equal(0, range.Minimum!.Value, 7); Assert.Equal(10, range.Maximum!.Value, 7); });
        Assert.True(alternatives.RepresentativeSolutions.Count >= 2);
        Assert.All(alternatives.RepresentativeSolutions, solution =>
            Assert.Equal(10, solution.Variables.Sum(value => value.Value), 6));
    }

    [Fact]
    public async Task Objective_Constant_Does_Not_Change_Optimizer()
    {
        var engine = new KoptEngine(new OptParser(), new HighsSolverAdapter());
        var baseline = await engine.RunAsync(SourceDocument.FromText(
            "MAX\n17 X1 + 10 X2\nST\n2 X1 + X2 <= 1497\nX1 + X2 <= 1199\nX1 <= 499\nEND\n"), new SolveOptions(), CancellationToken.None);
        var offset = await engine.RunAsync(SourceDocument.FromText(
            "MAX\n17 X1 + 10 X2 + 21\nST\n2 X1 + X2 <= 1497\nX1 + X2 <= 1199\nX1 <= 499\nEND\n"), new SolveOptions(), CancellationToken.None);

        Assert.Equal(baseline.Solve!.Variables.Select(value => value.Value), offset.Solve!.Variables.Select(value => value.Value));
        Assert.Equal(21, offset.Solve.ObjectiveValue!.Value - baseline.Solve.ObjectiveValue!.Value, 7);
    }

    [Fact]
    public async Task Detects_And_Samples_Multiple_Integer_Optima()
    {
        var engine = new KoptEngine(new OptParser(), new HighsSolverAdapter());
        var source = SourceDocument.FromText("MAX\nX1 + X2\nST\nX1 + X2 <= 2\nINTEGER X1, X2\nEND\n");
        var result = await engine.RunAsync(source, new SolveOptions(), CancellationToken.None);

        var alternatives = result.Solve!.AlternativeOptima!;
        Assert.Equal(OptimumMultiplicity.Multiple, alternatives.Multiplicity);
        Assert.False(alternatives.IsContinuousFamily);
        Assert.True(alternatives.IsComplete);
        Assert.True(alternatives.RepresentativeSolutions.Count >= 3);
        Assert.All(alternatives.RepresentativeSolutions, solution =>
        {
            Assert.Equal(2, solution.Variables.Sum(value => value.Value), 7);
            Assert.All(solution.Variables, value => Assert.Equal(Math.Round(value.Value), value.Value, 7));
        });
    }

    [Fact]
    public async Task Reports_Undetermined_When_Alternative_Analysis_Has_No_Time_Budget()
    {
        var engine = new KoptEngine(new OptParser(), new HighsSolverAdapter());
        var source = SourceDocument.FromText("MAX\nX1 + X2\nST\nX1 + X2 <= 10\nEND\n");
        var options = new SolveOptions(AlternativeAnalysisTimeLimitSeconds: 0);
        var result = await engine.RunAsync(source, options, CancellationToken.None);

        Assert.Equal(OptimumMultiplicity.Undetermined, result.Solve!.AlternativeOptima!.Multiplicity);
        Assert.False(result.Solve.AlternativeOptima.IsComplete);
    }

    [Fact]
    public async Task Solves_Basic_Mip()
    {
        var engine = new KoptEngine(new OptParser(), new HighsSolverAdapter());
        var source = SourceDocument.FromText("MAX\n5 X1 + 3 X2\nST\n2 X1 + X2 <= 7\nX1 + 2 X2 <= 8\nINTEGER X1, X2\nEND\n");
        var result = await engine.RunAsync(source, new SolveOptions(), CancellationToken.None);
        Assert.Equal(SolveStatus.Optimal, result.Solve!.Status);
        Assert.Equal(19, result.Solve.ObjectiveValue!.Value, 7);
        Assert.All(result.Solve.Variables, variable => Assert.Equal(Math.Round(variable.Value), variable.Value, 7));
    }

    [Fact]
    public async Task Detects_Infeasible_Model()
    {
        var engine = new KoptEngine(new OptParser(), new HighsSolverAdapter());
        var source = SourceDocument.FromText("MAX\nX\nST\nX <= 1\nX >= 2\nEND\n");
        var result = await engine.RunAsync(source, new SolveOptions(), CancellationToken.None);
        Assert.Equal(SolveStatus.Infeasible, result.Solve!.Status);
    }

    [Fact]
    public async Task Detects_Unbounded_Model()
    {
        var engine = new KoptEngine(new OptParser(), new HighsSolverAdapter());
        var source = SourceDocument.FromText("MAX\nX\nST\nX >= 0\nEND\n");
        var result = await engine.RunAsync(source, new SolveOptions(), CancellationToken.None);
        Assert.Equal(SolveStatus.Unbounded, result.Solve!.Status);
    }
}
