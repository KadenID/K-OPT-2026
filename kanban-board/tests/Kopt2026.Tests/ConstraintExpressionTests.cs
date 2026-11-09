using Kopt2026.App;
using Kopt2026.Application;
using Kopt2026.Domain;
using Kopt2026.Language;
using Kopt2026.Persistence;
using Kopt2026.Solver.Abstractions;
using Kopt2026.Solver.Highs;

namespace Kopt2026.Tests;

public sealed class ConstraintExpressionTests
{
    [Theory]
    [InlineData("2 X + 5 <= X + 12 - 3 Y", 1, 3, 7, Relation.LessOrEqual)]
    [InlineData("X + 5 >= Y - 2", 1, -1, -7, Relation.GreaterOrEqual)]
    [InlineData("5 <= 2 X + 1", -2, 0, -4, Relation.LessOrEqual)]
    [InlineData("X = 2 Y + 3", 1, -2, 3, Relation.Equal)]
    [InlineData("2 X + Y <= X + y + 10", 1, 0, 10, Relation.LessOrEqual)]
    [InlineData("X + 1e-2 <= -2.5e-1 X + 3e0", 1.25, 0, 2.99, Relation.LessOrEqual)]
    [InlineData("X <= X + 1", 0, 0, 1, Relation.LessOrEqual)]
    [InlineData("2 X + 5 <= 2 + 3", 2, 0, 0, Relation.LessOrEqual)]
    public void Normalizes_Variables_And_Constants_On_Both_Sides(string constraint, double x, double y, double rhs, Relation relation)
    {
        var parsed = new OptParser().Parse(SourceDocument.FromText($"MAX\nX\nST\nLIMIT: {constraint}\nEND\n"));
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Diagnostics.Select(d => d.Message)));
        var row = Assert.Single(parsed.Model!.Constraints);
        Assert.Equal("LIMIT", row.Name);
        Assert.Equal(x, row.Expression.Coefficients.GetValueOrDefault("X"), 12);
        Assert.Equal(y, row.Expression.Coefficients.GetValueOrDefault("Y"), 12);
        Assert.Equal(0, row.Expression.Constant);
        Assert.Equal(rhs, row.RightHandSide, 12);
        Assert.Equal(relation, row.Relation);
    }

    [Fact]
    public void Registers_Rhs_Only_Variables_And_Preserves_Integer_Declarations()
    {
        var parsed = new OptParser().Parse(SourceDocument.FromText("MAX\nX\nST\nX <= 2 Y + 3\nINTEGER Y\nEND\n"));
        Assert.True(parsed.IsValid);
        Assert.Equal(new[] { "X", "Y" }, parsed.Model!.Variables.Select(v => v.Name));
        Assert.Equal(VariableType.Integer, parsed.Model.Variables[1].Type);
    }

    [Theory]
    [InlineData("X <=")]
    [InlineData("<= X")]
    [InlineData("X <= Y * Z")]
    [InlineData("X <= Y +")]
    [InlineData("X <= 2 Y 3 Z")]
    [InlineData("X <= Y / 2")]
    [InlineData("X <= 1e309")]
    [InlineData("1e308 X <= -1e308 X")]
    [InlineData("-1e308 + X <= 1e308")]
    public void Rejects_Incomplete_Nonlinear_And_Overflowing_Constraints(string constraint)
    {
        var parsed = new OptParser().Parse(SourceDocument.FromText($"MAX\nX\nST\n{constraint}\nEND\n"));
        Assert.False(parsed.IsValid);
        Assert.Null(parsed.Model);
        Assert.Contains(parsed.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Span.Line == 4);
    }

    [Theory]
    [InlineData("  LIMIT: X <= 2 Y @")]
    [InlineData("  LIMIT: 2 X @ Y <= 10")]
    public void Reports_Errors_At_The_Original_Indented_Column(string constraint)
    {
        var parsed = new OptParser().Parse(SourceDocument.FromText($"MAX\nX\nST\n{constraint}\nEND\n"));
        var error = Assert.Single(parsed.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(4, error.Span.Line);
        Assert.Equal(constraint.IndexOf('@') + 1, error.Span.Column);
    }

    [Theory]
    [InlineData("MAX", "3 X + 2 Y", "2 X <= 10 - Y\n3 Y <= 12 - X", "2 X + Y <= 10\nX + 3 Y <= 12", "", 16.4)]
    [InlineData("MAX", "5 X + 3 Y", "2 X <= 7 - Y\n2 Y <= 8 - X", "2 X + Y <= 7\nX + 2 Y <= 8", "INTEGER X, Y\n", 19)]
    [InlineData("MIN", "2 X + Y", "X >= 5 - Y\n2 >= Y", "X + Y >= 5\n-Y >= -2", "", 8)]
    [InlineData("MAX", "X", "X = Y + 1\nX <= 5 - Y", "X - Y = 1\nX + Y <= 5", "", 3)]
    public async Task Solves_Equivalently_To_Manually_Rearranged_Lp_And_Mip(string sense, string objective, string input, string normalized, string integer, double expected)
    {
        var engine = new KoptEngine(new OptParser(), new HighsSolverAdapter());
        var actual = await engine.RunAsync(SourceDocument.FromText($"{sense}\n{objective}\nST\n{input}\n{integer}END\n"), new SolveOptions(), CancellationToken.None);
        var baseline = await engine.RunAsync(SourceDocument.FromText($"{sense}\n{objective}\nST\n{normalized}\n{integer}END\n"), new SolveOptions(), CancellationToken.None);
        Assert.True(actual.Parse.IsValid);
        Assert.Equal(SolveStatus.Optimal, actual.Solve!.Status);
        Assert.Equal(expected, actual.Solve.ObjectiveValue!.Value, 7);
        foreach (var value in baseline.Solve!.Variables)
            Assert.Equal(value.Value, actual.Solve.Variables.Single(v => v.Name == value.Name).Value, 7);
        Assert.Equal(baseline.Solve.Sensitivity!.IsAvailable, actual.Solve.Sensitivity!.IsAvailable);
        var values = actual.Solve.Variables.ToDictionary(v => v.Name, v => v.Value);
        Assert.True(FeasibleRegionBuilder.Contains(actual.Parse.Model!, new PlotPoint(values["X"], values["Y"])));
    }

    [Theory]
    [InlineData(1, SolveStatus.Optimal)]
    [InlineData(-1, SolveStatus.Infeasible)]
    public async Task Keeps_Constant_Constraints_After_Variables_Cancel(int constant, SolveStatus status)
    {
        var engine = new KoptEngine(new OptParser(), new HighsSolverAdapter());
        var run = await engine.RunAsync(SourceDocument.FromText($"MAX\nX\nST\nX <= 3\nX <= X + {constant}\nEND\n".Replace("+ -1", "- 1")), new SolveOptions(), CancellationToken.None);
        Assert.True(run.Parse.IsValid);
        Assert.Equal(status, run.Solve!.Status);
    }

    [Fact]
    public async Task Opening_Solving_And_Saving_Rhs_Variables_Preserves_The_Authored_Text()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kopt-rhs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "우변 변수.opt");
            const string source = "MAX\n3 X + 2 Y\nST\n2 X <= 10 - Y\n3 Y <= 12 - X\nEND\n";
            await File.WriteAllTextAsync(path, source);
            var workspace = new DesktopWorkspace(new TestDialogs(), new UserSettingsStore(Path.Combine(directory, "settings.json")));
            await workspace.OpenPathAsync(path);
            Assert.Equal(path, workspace.CurrentPath);
            Assert.False(workspace.IsDirty);
            await workspace.RunAsync();
            Assert.Equal(16.4, workspace.ViewModel.LatestResult!.ObjectiveValue!.Value, 7);
            await workspace.SaveAsync();
            Assert.Equal(source, workspace.ViewModel.EditorText);
            Assert.Equal(source, await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(directory, true); }
    }
}
