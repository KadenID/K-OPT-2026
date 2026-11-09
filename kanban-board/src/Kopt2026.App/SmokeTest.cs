using System.Runtime.InteropServices;
using System.Text.Json;
using Kopt2026.App;
using Kopt2026.Application;
using Kopt2026.Language;
using Kopt2026.Solver.Abstractions;
using Kopt2026.Solver.Highs;

namespace Kopt2026.App;
internal static class SmokeTest
{
    public static async Task<int> RunAsync(TextWriter output)
    {
        var viewModel = new MainViewModel(new KoptEngine(new OptParser(), new HighsSolverAdapter()));
        viewModel.Load(SourceDocument.FromText(SampleModels.Lp));
        await viewModel.RunAsync();
        var lp = viewModel.LatestResult;
        viewModel.Load(SourceDocument.FromText(SampleModels.Mip));
        await viewModel.RunAsync();
        var mip = viewModel.LatestResult;
        viewModel.EditorText = "INVALID";
        await viewModel.RunAsync();
        var invalidRejected = viewModel.LatestResult is null && viewModel.Diagnostics.Count > 0;
        var passed = lp is { Status: SolveStatus.Optimal, ObjectiveValue: not null, Sensitivity.IsAvailable: true }
            && Math.Abs(lp.ObjectiveValue.Value - 16.4) < 1e-7
            && mip is { Status: SolveStatus.Optimal, ObjectiveValue: not null }
            && Math.Abs(mip.ObjectiveValue.Value - 19) < 1e-7 && invalidRejected;
        await output.WriteLineAsync(JsonSerializer.Serialize(new
        {
            passed, os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            lpObjective = lp?.ObjectiveValue, mipObjective = mip?.ObjectiveValue,
            sensitivityAvailable = lp?.Sensitivity?.IsAvailable, invalidRejected
        }));
        return passed ? 0 : 1;
    }
}
