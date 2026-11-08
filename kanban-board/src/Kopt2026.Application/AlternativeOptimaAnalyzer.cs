using System.Diagnostics;
using Kopt2026.Domain;
using Kopt2026.Solver.Abstractions;

namespace Kopt2026.Application;

public sealed class AlternativeOptimaAnalyzer(ISolverAdapter solver)
{
    private static readonly double[] ProbeFractions = [0.5, 0.25, 0.75, 0.125, 0.375, 0.625, 0.875];

    public async Task<AlternativeOptimaInfo?> AnalyzeAsync(
        OptimizationModel model,
        SolveResult primary,
        CancellationToken cancellationToken)
    {
        if (primary.Status != SolveStatus.Optimal || primary.ObjectiveValue is not double optimum) return null;

        var clock = Stopwatch.StartNew();
        var solutionLimit = Math.Max(1, primary.Options.AlternativeSolutionLimit);
        var timeLimit = Math.Max(0, primary.Options.AlternativeAnalysisTimeLimitSeconds);
        var tolerance = Math.Max(primary.Options.PrimalTolerance * 10, 1e-8);
        var representatives = new List<IReadOnlyList<OptimalVariableValue>>();
        AddRepresentative(primary.Variables.Select(v => new OptimalVariableValue(v.Name, v.Value)).ToList());

        var ranges = new List<OptimalVariableRange>(model.Variables.Count);
        var complete = true;
        foreach (var variable in model.Variables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var minimum = await SolveExtremeAsync(variable, ObjectiveSense.Minimize);
            var maximum = await SolveExtremeAsync(variable, ObjectiveSense.Maximize);
            ranges.Add(new(variable.Name, minimum.Bound, maximum.Bound));
            complete &= minimum.Proven && maximum.Proven;
            AddRepresentative(minimum.Values);
            AddRepresentative(maximum.Values);
        }

        var hasDifferentRange = ranges.Any(range => IsDifferent(range.Minimum, range.Maximum));
        if (representatives.Count < solutionLimit && hasDifferentRange)
            await AddInteriorRepresentativesAsync();

        var hasMultiple = hasDifferentRange || representatives.Count > 1;
        var multiplicity = hasMultiple
            ? OptimumMultiplicity.Multiple
            : complete ? OptimumMultiplicity.Unique : OptimumMultiplicity.Undetermined;
        var continuousFamily = multiplicity == OptimumMultiplicity.Multiple && ranges.Any(range =>
            IsDifferent(range.Minimum, range.Maximum) &&
            model.Variables.First(v => v.Name.Equals(range.Name, StringComparison.OrdinalIgnoreCase)).Type == VariableType.Continuous);

        return new(
            multiplicity,
            continuousFamily,
            complete,
            ranges,
            representatives.Select((values, index) => new RepresentativeOptimalSolution(index + 1, values)).ToList(),
            solutionLimit,
            clock.Elapsed);

        async Task<(double? Bound, bool Proven, IReadOnlyList<OptimalVariableValue>? Values)> SolveExtremeAsync(
            Variable variable,
            ObjectiveSense sense)
        {
            var auxiliary = BuildAuxiliaryModel(model, optimum, variable.Name, sense);
            var result = await SolveWithinBudgetAsync(auxiliary);
            if (result is null) return (null, false, null);
            if (result.Status == SolveStatus.Unbounded)
                return (sense == ObjectiveSense.Minimize ? double.NegativeInfinity : double.PositiveInfinity, true, null);
            if (result.Status != SolveStatus.Optimal) return (null, false, null);
            var value = result.Variables.First(v => v.Name.Equals(variable.Name, StringComparison.OrdinalIgnoreCase)).Value;
            return (value, true, ToValues(result));
        }

        async Task AddInteriorRepresentativesAsync()
        {
            foreach (var range in ranges.Where(range => IsDifferent(range.Minimum, range.Maximum)))
            {
                if (representatives.Count >= solutionLimit || RemainingSeconds() <= 0) return;
                var variable = model.Variables.First(v => v.Name.Equals(range.Name, StringComparison.OrdinalIgnoreCase));
                foreach (var value in ProbeValues(variable, range, solutionLimit))
                {
                    if (representatives.Count >= solutionLimit || RemainingSeconds() <= 0) return;
                    var auxiliary = BuildAuxiliaryModel(model, optimum, null, ObjectiveSense.Minimize, variable.Name, value);
                    var result = await SolveWithinBudgetAsync(auxiliary);
                    if (result?.Status == SolveStatus.Optimal) AddRepresentative(ToValues(result));
                }
            }
        }

        async Task<SolveResult?> SolveWithinBudgetAsync(OptimizationModel auxiliary)
        {
            var remaining = RemainingSeconds();
            if (remaining <= 0)
            {
                complete = false;
                return null;
            }

            var perSolveLimit = Math.Max(0.05, Math.Min(2, remaining));
            var auxiliaryOptions = primary.Options with { TimeLimitSeconds = perSolveLimit, MipRelativeGap = 0, ComputeSensitivity = false };
            return await solver.SolveAsync(auxiliary, auxiliaryOptions, cancellationToken);
        }

        double RemainingSeconds() => timeLimit - clock.Elapsed.TotalSeconds;

        void AddRepresentative(IReadOnlyList<OptimalVariableValue>? values)
        {
            if (values is null || representatives.Count >= solutionLimit || !PreservesOriginalObjective(values)) return;
            if (representatives.Any(existing => SameSolution(existing, values, tolerance))) return;
            representatives.Add(values);
        }

        bool PreservesOriginalObjective(IReadOnlyList<OptimalVariableValue> values)
        {
            var lookup = values.ToDictionary(v => v.Name, v => v.Value, StringComparer.OrdinalIgnoreCase);
            var objective = model.Objective.Constant + model.Objective.Coefficients.Sum(pair => pair.Value * lookup.GetValueOrDefault(pair.Key));
            return NearlyEqual(objective, optimum, tolerance);
        }

        bool IsDifferent(double? minimum, double? maximum)
        {
            if (minimum is null || maximum is null) return false;
            if (double.IsInfinity(minimum.Value) || double.IsInfinity(maximum.Value)) return true;
            return !NearlyEqual(minimum.Value, maximum.Value, tolerance);
        }
    }

    private static OptimizationModel BuildAuxiliaryModel(
        OptimizationModel model,
        double optimum,
        string? targetVariable,
        ObjectiveSense sense,
        string? fixedVariable = null,
        double fixedValue = 0)
    {
        var constraints = model.Constraints.ToList();
        constraints.Add(new(
            "__KOPT_OPTIMUM_FACE",
            "최적 목적값 고정",
            new LinearExpression(new Dictionary<string, double>(model.Objective.Coefficients, StringComparer.OrdinalIgnoreCase)),
            Relation.Equal,
            optimum - model.Objective.Constant,
            new SourceSpan(0, 0, 0)));

        if (fixedVariable is not null)
        {
            constraints.Add(new(
                "__KOPT_REPRESENTATIVE_PROBE",
                $"대표해 탐색: {fixedVariable}",
                new LinearExpression(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { [fixedVariable] = 1 }),
                Relation.Equal,
                fixedValue,
                new SourceSpan(0, 0, 0)));
        }

        IReadOnlyDictionary<string, double> coefficients = targetVariable is null
            ? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { [targetVariable] = 1 };
        return model with
        {
            Sense = sense,
            Objective = new LinearExpression(coefficients),
            Constraints = constraints
        };
    }

    private static IReadOnlyList<double> ProbeValues(Variable variable, OptimalVariableRange range, int limit)
    {
        if (range.Minimum is not double minimum || range.Maximum is not double maximum ||
            !double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum <= minimum) return [];

        if (variable.Type == VariableType.Integer)
        {
            var first = (long)Math.Ceiling(minimum - 1e-9);
            var last = (long)Math.Floor(maximum + 1e-9);
            if (last - first <= 1) return [];
            if (last - first <= limit * 2L)
                return Enumerable.Range(1, checked((int)(last - first - 1))).Select(offset => (double)(first + offset)).ToList();
            return ProbeFractions
                .Select(fraction => (double)Math.Clamp((long)Math.Round(first + (last - first) * fraction), first + 1, last - 1))
                .Distinct()
                .ToList();
        }

        return ProbeFractions.Select(fraction => minimum + (maximum - minimum) * fraction).Distinct().ToList();
    }

    private static IReadOnlyList<OptimalVariableValue> ToValues(SolveResult result) =>
        result.Variables.Select(v => new OptimalVariableValue(v.Name, v.Value)).ToList();

    private static bool SameSolution(
        IReadOnlyList<OptimalVariableValue> left,
        IReadOnlyList<OptimalVariableValue> right,
        double tolerance)
    {
        if (left.Count != right.Count) return false;
        var lookup = right.ToDictionary(v => v.Name, v => v.Value, StringComparer.OrdinalIgnoreCase);
        return left.All(item => lookup.TryGetValue(item.Name, out var value) && NearlyEqual(item.Value, value, tolerance));
    }

    private static bool NearlyEqual(double left, double right, double tolerance) =>
        Math.Abs(left - right) <= tolerance * Math.Max(1, Math.Max(Math.Abs(left), Math.Abs(right)));
}
