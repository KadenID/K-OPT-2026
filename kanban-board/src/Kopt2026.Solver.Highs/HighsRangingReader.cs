using System.Reflection;
using System.Runtime.InteropServices;
using Highs;
using Kopt2026.Domain;
using Kopt2026.Solver.Abstractions;

namespace Kopt2026.Solver.Highs;

internal static class HighsRangingReader
{
    private const double NativeInfinity = 1e30;
    private static readonly FieldInfo? NativeHandleField = typeof(HighsLpSolver).GetField("highs", BindingFlags.Instance | BindingFlags.NonPublic);

    public static SensitivityAnalysisInfo Read(
        HighsLpSolver solver,
        OptimizationModel model,
        IReadOnlyList<ConstraintResult> constraintResults,
        out string? warning)
    {
        warning = null;
        try
        {
            if (NativeHandleField?.GetValue(solver) is not IntPtr handle || handle == IntPtr.Zero)
                return Unavailable("HiGHS 민감도 API 연결을 확인할 수 없습니다.");

            var columnCount = model.Variables.Count;
            var rowCount = model.Constraints.Count;
            var costUpper = new double[columnCount];
            var costLower = new double[columnCount];
            var rowUpper = new double[rowCount];
            var rowLower = new double[rowCount];

            var status = Highs_getRanging(
                handle,
                costUpper, null, null, null,
                costLower, null, null, null,
                null, null, null, null,
                null, null, null, null,
                rowUpper, null, null, null,
                rowLower, null, null, null);

            if (status < 0)
            {
                warning = "HiGHS가 민감도 범위를 반환하지 못했습니다.";
                return Unavailable(warning);
            }

            var objectiveRanges = model.Variables.Select((variable, index) => new SensitivityRange(
                variable.Name,
                model.Objective.Coefficients.GetValueOrDefault(variable.Name),
                NormalizeInfinity(costLower[index]),
                NormalizeInfinity(costUpper[index]))).ToList();
            var rightHandSideRanges = model.Constraints.Select((constraint, index) =>
            {
                var result = constraintResults[index];
                var tolerance = 1e-7 * Math.Max(1, Math.Abs(constraint.RightHandSide));

                // HiGHS reports bound-ranging data for both sides of a row. For a
                // nonbinding one-sided row, the finite endpoint is the current row
                // activity and the direction away from it is unbounded.
                if (result.Slack > tolerance && constraint.Relation == Relation.LessOrEqual)
                    return new SensitivityRange(constraint.Name, constraint.RightHandSide, result.Activity, double.PositiveInfinity);
                if (result.Slack > tolerance && constraint.Relation == Relation.GreaterOrEqual)
                    return new SensitivityRange(constraint.Name, constraint.RightHandSide, double.NegativeInfinity, result.Activity);

                return new SensitivityRange(
                    constraint.Name,
                    constraint.RightHandSide,
                    NormalizeInfinity(rowLower[index]),
                    NormalizeInfinity(rowUpper[index]));
            }).ToList();

            return new(
                true,
                "현재 최적기저에서 한 번에 한 목적계수 또는 우변만 변경할 때의 허용 범위입니다.",
                objectiveRanges,
                rightHandSideRanges);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException or TargetException or TargetInvocationException)
        {
            warning = $"민감도 API 연결 실패: {ex.GetType().Name}";
            return Unavailable("현재 환경에서 HiGHS 민감도 API를 사용할 수 없습니다.");
        }
    }

    private static SensitivityAnalysisInfo Unavailable(string message) => new(false, message, [], []);

    private static double NormalizeInfinity(double value)
    {
        if (value >= NativeInfinity / 2) return double.PositiveInfinity;
        if (value <= -NativeInfinity / 2) return double.NegativeInfinity;
        return Math.Abs(value) < 5e-13 ? 0 : value;
    }

    [DllImport("highs", EntryPoint = "Highs_getRanging")]
    private static extern int Highs_getRanging(
        IntPtr highs,
        [Out] double[]? colCostUpValue,
        [Out] double[]? colCostUpObjective,
        [Out] int[]? colCostUpInVar,
        [Out] int[]? colCostUpOutVar,
        [Out] double[]? colCostDownValue,
        [Out] double[]? colCostDownObjective,
        [Out] int[]? colCostDownInVar,
        [Out] int[]? colCostDownOutVar,
        [Out] double[]? colBoundUpValue,
        [Out] double[]? colBoundUpObjective,
        [Out] int[]? colBoundUpInVar,
        [Out] int[]? colBoundUpOutVar,
        [Out] double[]? colBoundDownValue,
        [Out] double[]? colBoundDownObjective,
        [Out] int[]? colBoundDownInVar,
        [Out] int[]? colBoundDownOutVar,
        [Out] double[]? rowBoundUpValue,
        [Out] double[]? rowBoundUpObjective,
        [Out] int[]? rowBoundUpInVar,
        [Out] int[]? rowBoundUpOutVar,
        [Out] double[]? rowBoundDownValue,
        [Out] double[]? rowBoundDownObjective,
        [Out] int[]? rowBoundDownInVar,
        [Out] int[]? rowBoundDownOutVar);
}
