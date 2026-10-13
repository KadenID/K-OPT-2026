using System.Globalization;
using System.Text;
using Kopt2026.Domain;
using Kopt2026.Solver.Abstractions;

namespace Kopt2026.Application;

public static class LegacyKoptReportFormatter
{
    public const string EmptyReport = "최적화 실행 후 원본 K-OPT 형식의 호환 출력이 표시됩니다.";

    public static string Format(SolveResult result)
    {
        var report = new StringBuilder();
        report.AppendLine("*** 원본 K-OPT 형식 호환 출력 ***");
        report.AppendLine("※ HiGHS 계산 결과를 원본 K-OPT 표 형식으로 재구성했습니다.");
        report.AppendLine("※ 반복횟수는 원본 K-OPT가 아닌 HiGHS 심플렉스 반복횟수입니다.");
        report.AppendLine();
        report.AppendLine(StatusText(result.Status));
        report.AppendLine();
        report.AppendLine("*** 분 석 결 과 ***");
        report.AppendLine($"HiGHS 심플렉스 반복횟수 : {FormatIterations(result.SimplexIterationCount)}");
        report.AppendLine($"목적함수 값 : {FormatNumber(result.ObjectiveValue)}");

        if (result.Variables.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("변 수                 최 적 해              수 정 비 용");
            report.AppendLine(new string('-', 64));
            foreach (var variable in result.Variables)
                report.AppendLine($"{variable.Name,-14}{FormatNumber(variable.Value),22}{FormatNumber(variable.ReducedCost),22}");
        }

        if (result.Constraints.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("제약식      종류          여유/잉여변수             쌍대변수");
            report.AppendLine(new string('-', 64));
            for (var index = 0; index < result.Constraints.Count; index++)
            {
                var constraint = result.Constraints[index];
                report.AppendLine($"{index + 1,-12}{RelationText(constraint.Relation),-8}{FormatNumber(constraint.Slack),22}{FormatNumber(constraint.Dual),22}");
            }
        }

        if (result.Warnings.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("주의:");
            foreach (var warning in result.Warnings) report.AppendLine($"- {warning}");
        }

        AppendSensitivity(report, result.Sensitivity);
        return report.ToString();
    }

    private static void AppendSensitivity(StringBuilder report, SensitivityAnalysisInfo? sensitivity)
    {
        report.AppendLine();
        report.AppendLine();
        report.AppendLine("*** 민 감 도 분 석 ***");

        if (sensitivity is not { IsAvailable: true })
        {
            report.Append(sensitivity?.Message ?? "이 결과에는 민감도 분석 정보가 없습니다.");
            return;
        }

        report.AppendLine(sensitivity.Message);
        report.AppendLine();
        report.AppendLine("목적식 계수의 허용 범위");
        report.AppendLine("변 수                 현 재 계 수                  하 한                  상 한");
        report.AppendLine(new string('-', 80));
        foreach (var range in sensitivity.ObjectiveCoefficients)
            report.AppendLine($"{range.Name,-14}{FormatNumber(range.CurrentValue),22}{FormatNumber(range.LowerValue),22}{FormatNumber(range.UpperValue),22}");

        report.AppendLine();
        report.AppendLine("제약식 우변의 허용 범위");
        report.AppendLine("제 약 식               현 재 우 변                  하 한                  상 한");
        report.AppendLine(new string('-', 80));
        foreach (var range in sensitivity.RightHandSides)
            report.AppendLine($"{range.Name,-14}{FormatNumber(range.CurrentValue),22}{FormatNumber(range.LowerValue),22}{FormatNumber(range.UpperValue),22}");

        report.Append("※ 각 범위는 다른 항목을 고정하고 한 번에 한 항목만 변경하는 현재 최적기저 기준입니다.");
    }

    private static string StatusText(SolveStatus status) => status switch
    {
        SolveStatus.Optimal => "최적해(Optimal Solution)를 갖는 문제입니다.",
        SolveStatus.Feasible => "실현가능해(Feasible Solution)가 발견되었습니다.",
        SolveStatus.Infeasible => "실행가능해가 없는 문제(Infeasible Problem)입니다.",
        SolveStatus.Unbounded => "무한최적해(Unbounded Optimal Solution)를 갖는 문제입니다.",
        SolveStatus.InfeasibleOrUnbounded => "실행불가능 또는 무한(Infeasible or Unbounded) 문제입니다.",
        SolveStatus.TimeLimit => "제한 시간(Time Limit) 안에 해석을 완료하지 못했습니다.",
        SolveStatus.IterationLimit => "반복 제한(Iteration Limit)에 도달했습니다.",
        SolveStatus.Cancelled => "사용자가 해석을 취소했습니다.",
        SolveStatus.NumericalIssue => "수치 문제(Numerical Issue)로 해석을 완료하지 못했습니다.",
        _ => "해석 오류(Solver Error)가 발생했습니다."
    };

    private static string RelationText(Relation relation) => relation switch
    {
        Relation.LessOrEqual => "<=",
        Relation.GreaterOrEqual => ">=",
        _ => "="
    };

    private static string FormatIterations(int? iterations) => iterations is int value
        ? value.ToString("#,##0", CultureInfo.InvariantCulture)
        : "정보 없음";

    private static string FormatNumber(double? value)
    {
        if (value is null) return "-";
        if (double.IsPositiveInfinity(value.Value)) return "무한대";
        if (double.IsNegativeInfinity(value.Value)) return "-무한대";
        if (double.IsNaN(value.Value)) return "NaN";
        var normalized = Math.Abs(value.Value) < 5e-13 ? 0d : value.Value;
        return normalized.ToString("#,##0.00000", CultureInfo.InvariantCulture);
    }
}
