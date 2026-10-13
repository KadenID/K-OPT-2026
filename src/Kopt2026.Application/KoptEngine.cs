using System.Diagnostics;
using Kopt2026.Domain;
using Kopt2026.Language;
using Kopt2026.Solver.Abstractions;

namespace Kopt2026.Application;

public sealed record RunResult(ParseResult Parse, SolveResult? Solve, TimeSpan ParseTime, TimeSpan TotalTime);

public sealed class KoptEngine(OptParser parser, ISolverAdapter solver)
{
    public ParseResult Validate(SourceDocument source) => parser.Parse(source);

    public async Task<RunResult> RunAsync(SourceDocument source, SolveOptions options, CancellationToken cancellationToken)
    {
        var total = Stopwatch.StartNew();
        var parseClock = Stopwatch.StartNew();
        var parsed = parser.Parse(source);
        parseClock.Stop();
        if (!parsed.IsValid || parsed.Model is null) return new(parsed, null, parseClock.Elapsed, total.Elapsed);
        var result = await solver.SolveAsync(parsed.Model, options, cancellationToken);
        var alternativeOptima = await new AlternativeOptimaAnalyzer(solver).AnalyzeAsync(parsed.Model, result, cancellationToken);
        if (alternativeOptima is not null) result = result with { AlternativeOptima = alternativeOptima };
        return new(parsed, result, parseClock.Elapsed, total.Elapsed);
    }
}
