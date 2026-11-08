using System.IO;
using Kopt2026.Application;
using Kopt2026.Export;
using Kopt2026.Language;
using Kopt2026.Solver.Abstractions;
using Kopt2026.Solver.Highs;

namespace Kopt2026.Tests;

public sealed class AlternativeOptimaExportTests
{
    [Theory]
    [InlineData(".txt", "[복수 최적해 분석]")]
    [InlineData(".csv", "최적범위")]
    [InlineData(".json", "AlternativeOptima")]
    public async Task Exports_Alternative_Optima(string extension, string expected)
    {
        var engine = new KoptEngine(new OptParser(), new HighsSolverAdapter());
        var source = SourceDocument.FromText("MAX\nX1 + X2\nST\nX1 + X2 <= 10\nEND\n");
        var run = await engine.RunAsync(source, new SolveOptions(), CancellationToken.None);
        var directory = Path.Combine(Path.GetTempPath(), "kopt2026-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "result" + extension);

        try
        {
            Directory.CreateDirectory(directory);
            await new ResultExporter().ExportAsync(run.Solve!, path);
            var content = await File.ReadAllTextAsync(path);
            Assert.Contains(expected, content);
            Assert.Contains("Multiple", content);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
