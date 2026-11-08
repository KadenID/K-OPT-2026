using System.IO;
using Kopt2026.Application;
using Kopt2026.Export;
using Kopt2026.Language;
using Kopt2026.Solver.Abstractions;
using Kopt2026.Solver.Highs;

namespace Kopt2026.Tests;

public sealed class SensitivityExportTests
{
    [Theory]
    [InlineData(".txt", "[민감도 분석]", "목적계수", "우변")]
    [InlineData(".csv", "목적계수민감도", "우변민감도", "하한")]
    [InlineData(".json", "Sensitivity", "ObjectiveCoefficients", "RightHandSides")]
    public async Task Exports_Sensitivity_Ranges(string extension, string expected1, string expected2, string expected3)
    {
        var engine = new KoptEngine(new OptParser(), new HighsSolverAdapter());
        var run = await engine.RunAsync(SourceDocument.FromText(
            "MAX\n3 X1 + 2 X2\nST\nC1: 2 X1 + X2 <= 10\nC2: X1 + 3 X2 <= 12\nEND\n"),
            new SolveOptions(), CancellationToken.None);
        var directory = Path.Combine(Path.GetTempPath(), "kopt2026-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "sensitivity" + extension);

        try
        {
            Directory.CreateDirectory(directory);
            await new ResultExporter().ExportAsync(run.Solve!, path);
            var content = await File.ReadAllTextAsync(path);
            Assert.Contains(expected1, content);
            Assert.Contains(expected2, content);
            Assert.Contains(expected3, content);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
