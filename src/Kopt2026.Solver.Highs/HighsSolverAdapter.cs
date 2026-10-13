using System.Diagnostics;
using Highs;
using Kopt2026.Domain;
using Kopt2026.Solver.Abstractions;

namespace Kopt2026.Solver.Highs;

public sealed class HighsSolverAdapter : ISolverAdapter
{
    private const double Infinity = 1e30;
    public SolverIdentity Identity { get; } = new("HiGHS", "1.15.1");

    public Task<SolveResult> SolveAsync(OptimizationModel model, SolveOptions options, CancellationToken cancellationToken) =>
        Task.Run(() => Solve(model, options, cancellationToken), cancellationToken);

    private SolveResult Solve(OptimizationModel model, SolveOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clock = Stopwatch.StartNew();
        var variableIndex = model.Variables.Select((v, i) => (v.Name, i)).ToDictionary(x => x.Name, x => x.i, StringComparer.OrdinalIgnoreCase);
        var costs = model.Variables.Select(v => model.Objective.Coefficients.GetValueOrDefault(v.Name)).ToArray();
        var lower = model.Variables.Select(v => double.IsNegativeInfinity(v.Lower) ? -Infinity : v.Lower).ToArray();
        var upper = model.Variables.Select(v => double.IsPositiveInfinity(v.Upper) ? Infinity : v.Upper).ToArray();
        var rowLower = new double[model.Constraints.Count];
        var rowUpper = new double[model.Constraints.Count];
        var starts = new int[model.Constraints.Count + 1];
        var indices = new List<int>();
        var values = new List<double>();
        for (var row = 0; row < model.Constraints.Count; row++)
        {
            var constraint = model.Constraints[row];
            (rowLower[row], rowUpper[row]) = constraint.Relation switch
            {
                Relation.LessOrEqual => (-Infinity, constraint.RightHandSide),
                Relation.GreaterOrEqual => (constraint.RightHandSide, Infinity),
                _ => (constraint.RightHandSide, constraint.RightHandSide)
            };
            foreach (var coefficient in constraint.Expression.Coefficients)
            {
                if (Math.Abs(coefficient.Value) <= double.Epsilon) continue;
                indices.Add(variableIndex[coefficient.Key]);
                values.Add(coefficient.Value);
            }
            starts[row + 1] = indices.Count;
        }

        var hasIntegers = model.Variables.Any(v => v.Type == VariableType.Integer);
        var integrality = model.Variables.Select(v => v.Type == VariableType.Integer ? (int)HighsIntegrality.kInteger : (int)HighsIntegrality.kContinuous).ToArray();
        var nativeModel = new HighsModel(costs, lower, upper, rowLower, rowUpper, starts, indices.ToArray(), values.ToArray(), integrality,
            model.Objective.Constant, HighsMatrixFormat.kRowwise,
            model.Sense == ObjectiveSense.Maximize ? HighsObjectiveSense.kMaximize : HighsObjectiveSense.kMinimize);

        using var solver = new HighsLpSolver();
        solver.setBoolOptionValue("output_flag", 0);
        solver.setDoubleOptionValue("time_limit", options.TimeLimitSeconds);
        solver.setDoubleOptionValue("mip_rel_gap", options.MipRelativeGap);
        solver.setDoubleOptionValue("primal_feasibility_tolerance", options.PrimalTolerance);
        var passStatus = hasIntegers ? solver.passMip(nativeModel) : solver.passLp(nativeModel);
        if (passStatus == HighsStatus.kError) return Error(model, options, clock.Elapsed, "모델을 HiGHS에 전달하지 못했습니다.");
        var runStatus = solver.run();
        cancellationToken.ThrowIfCancellationRequested();
        var status = MapStatus(solver.GetModelStatus(), runStatus);
        var solution = solver.getSolution();
        var info = solver.getInfo();
        var variableResults = model.Variables.Select((v, i) => new VariableResult(v.Name, v.Type, Normalize(solution.colvalue[i]), Normalize(solution.coldual[i]), v.Lower, v.Upper)).ToList();
        var constraintResults = model.Constraints.Select((c, i) =>
        {
            var activity = Normalize(solution.rowvalue[i]);
            var slack = c.Relation switch { Relation.LessOrEqual => c.RightHandSide - activity, Relation.GreaterOrEqual => activity - c.RightHandSide, _ => Math.Abs(activity - c.RightHandSide) };
            return new ConstraintResult(c.Name, c.Relation, activity, c.RightHandSide, Normalize(slack), Normalize(solution.rowdual[i]));
        }).ToList();
        var mip = hasIntegers ? new MipInfo(double.IsNaN(info.DualBound) ? null : info.DualBound, double.IsNaN(info.MipGap) ? null : info.MipGap, info.NodeCount) : null;
        var warnings = new List<string>();
        SensitivityAnalysisInfo? sensitivity = null;
        if (options.ComputeSensitivity)
        {
            if (status != SolveStatus.Optimal)
                sensitivity = new(false, "최적성이 증명된 연속형 LP에서만 민감도 범위를 계산합니다.", [], []);
            else if (hasIntegers)
                sensitivity = new(false, "민감도 범위는 연속형 선형계획(LP)에서만 지원합니다.", [], []);
            else
            {
                sensitivity = HighsRangingReader.Read(solver, model, constraintResults, out var rangingWarning);
                if (rangingWarning is not null) warnings.Add(rangingWarning);
            }
        }
        return new(status, status is SolveStatus.Optimal or SolveStatus.Feasible or SolveStatus.TimeLimit ? solver.getObjectiveValue() : null,
            variableResults, constraintResults, mip, clock.Elapsed, model.SourceHash, Identity, options, warnings,
            SimplexIterationCount: info.SimplexIterationCount, Sensitivity: sensitivity);
    }

    private SolveResult Error(OptimizationModel model, SolveOptions options, TimeSpan time, string warning) => new(SolveStatus.Error, null, [], [], null, time, model.SourceHash, Identity, options, [warning]);
    private static double Normalize(double value) => Math.Abs(value) < 5e-13 ? 0 : value;
    private static SolveStatus MapStatus(HighsModelStatus status, HighsStatus runStatus) => status switch
    {
        HighsModelStatus.kOptimal => SolveStatus.Optimal,
        HighsModelStatus.kInfeasible => SolveStatus.Infeasible,
        HighsModelStatus.kUnbounded => SolveStatus.Unbounded,
        HighsModelStatus.kUnboundedOrInfeasible => SolveStatus.InfeasibleOrUnbounded,
        HighsModelStatus.kTimeLimit => SolveStatus.TimeLimit,
        HighsModelStatus.kIterationLimit => SolveStatus.IterationLimit,
        HighsModelStatus.kInterrupt or HighsModelStatus.kHighsInterrupt => SolveStatus.Cancelled,
        _ when runStatus == HighsStatus.kError => SolveStatus.Error,
        _ => SolveStatus.NumericalIssue
    };
}
