using Kopt2026.Application;
using Kopt2026.Domain;
using Kopt2026.Solver.Abstractions;

namespace Kopt2026.Tests;

public sealed class LegacyKoptReportFormatterTests
{
    [Fact]
    public void Formats_Optimal_Result_In_Legacy_Table_Shape()
    {
        var result = new SolveResult(
            SolveStatus.Optimal,
            2_053_333_333.33,
            [
                new("X1", VariableType.Continuous, 53_333.33333, 0, 0, double.PositiveInfinity),
                new("X2", VariableType.Continuous, 173_333.3333, -2.5, 0, double.PositiveInfinity)
            ],
            [
                new("CAPACITY", Relation.LessOrEqual, 10, 10, 0, 20),
                new("DEMAND", Relation.GreaterOrEqual, 12, 12, 0, 10)
            ],
            null,
            TimeSpan.Zero,
            "hash",
            new("HiGHS", "1.15.1"),
            new(),
            [],
            SimplexIterationCount: 4,
            Sensitivity: new(
                true,
                "현재 최적기저에서 한 번에 한 항목만 변경할 때의 허용 범위입니다.",
                [new("X1", 17, 10, double.PositiveInfinity), new("X2", 10, 0, 17)],
                [new("CAPACITY", 10, 4, 24), new("DEMAND", 12, 5, 30)]));

        var report = LegacyKoptReportFormatter.Format(result);

        Assert.Contains("원본 K-OPT 형식 호환 출력", report);
        Assert.Contains("HiGHS 계산 결과", report);
        Assert.Contains("최적해(Optimal Solution)", report);
        Assert.Contains("HiGHS 심플렉스 반복횟수 : 4", report);
        Assert.Contains("목적함수 값 : 2,053,333,333.33000", report);
        Assert.Contains("53,333.33333", report);
        Assert.Contains("수 정 비 용", report);
        Assert.Contains("여유/잉여변수", report);
        Assert.Contains("<=", report);
        Assert.Contains(">=", report);
        Assert.Contains("민 감 도 분 석", report);
        Assert.Contains("목적식 계수의 허용 범위", report);
        Assert.Contains("제약식 우변의 허용 범위", report);
        Assert.Contains("무한대", report);
        Assert.Contains("한 번에 한 항목만 변경", report);
    }

    [Theory]
    [InlineData(SolveStatus.Unbounded, "무한최적해(Unbounded Optimal Solution)")]
    [InlineData(SolveStatus.Infeasible, "실행가능해가 없는 문제(Infeasible Problem)")]
    [InlineData(SolveStatus.TimeLimit, "제한 시간(Time Limit)")]
    public void Formats_NonOptimal_Status_Without_Inventing_A_Solution(SolveStatus status, string expected)
    {
        var result = new SolveResult(status, null, [], [], null, TimeSpan.Zero, "hash", new("HiGHS", "1.15.1"), new(), []);

        var report = LegacyKoptReportFormatter.Format(result);

        Assert.Contains(expected, report);
        Assert.Contains("목적함수 값 : -", report);
        Assert.DoesNotContain("변 수                 최 적 해", report);
    }
}
