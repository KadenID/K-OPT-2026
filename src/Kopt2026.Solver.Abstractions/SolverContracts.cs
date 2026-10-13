using Kopt2026.Domain;

namespace Kopt2026.Solver.Abstractions;

public enum SolveStatus { Optimal, Feasible, Infeasible, Unbounded, InfeasibleOrUnbounded, TimeLimit, IterationLimit, Cancelled, NumericalIssue, Error }
public sealed record SolveOptions(
    double TimeLimitSeconds = 30,
    double MipRelativeGap = 1e-6,
    double PrimalTolerance = 1e-7,
    int AlternativeSolutionLimit = 10,
    double AlternativeAnalysisTimeLimitSeconds = 10,
    bool ComputeSensitivity = true);
public sealed record SolverIdentity(string Name, string Version);
public sealed record VariableResult(string Name, VariableType Type, double Value, double ReducedCost, double Lower, double Upper);
public sealed record ConstraintResult(string Name, Relation Relation, double Activity, double RightHandSide, double Slack, double Dual);
public sealed record MipInfo(double? BestBound, double? RelativeGap, long? NodeCount);
public enum OptimumMultiplicity { Unique, Multiple, Undetermined }
public sealed record OptimalVariableRange(string Name, double? Minimum, double? Maximum);
public sealed record OptimalVariableValue(string Name, double Value);
public sealed record RepresentativeOptimalSolution(int Number, IReadOnlyList<OptimalVariableValue> Variables);
public sealed record AlternativeOptimaInfo(
    OptimumMultiplicity Multiplicity,
    bool IsContinuousFamily,
    bool IsComplete,
    IReadOnlyList<OptimalVariableRange> VariableRanges,
    IReadOnlyList<RepresentativeOptimalSolution> RepresentativeSolutions,
    int RequestedSolutionLimit,
    TimeSpan AnalysisTime);
public sealed record SensitivityRange(string Name, double CurrentValue, double LowerValue, double UpperValue);
public sealed record SensitivityAnalysisInfo(
    bool IsAvailable,
    string Message,
    IReadOnlyList<SensitivityRange> ObjectiveCoefficients,
    IReadOnlyList<SensitivityRange> RightHandSides);
public sealed record SolveResult(
    SolveStatus Status,
    double? ObjectiveValue,
    IReadOnlyList<VariableResult> Variables,
    IReadOnlyList<ConstraintResult> Constraints,
    MipInfo? Mip,
    TimeSpan SolveTime,
    string ModelHash,
    SolverIdentity Solver,
    SolveOptions Options,
    IReadOnlyList<string> Warnings,
    AlternativeOptimaInfo? AlternativeOptima = null,
    int? SimplexIterationCount = null,
    SensitivityAnalysisInfo? Sensitivity = null);

public interface ISolverAdapter
{
    SolverIdentity Identity { get; }
    Task<SolveResult> SolveAsync(OptimizationModel model, SolveOptions options, CancellationToken cancellationToken);
}
