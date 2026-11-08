using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kopt2026.Solver.Abstractions;

namespace Kopt2026.Export;

public sealed class ResultExporter
{
    public async Task ExportAsync(SolveResult result, string path, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var content = extension switch
        {
            ".json" => ToJson(result),
            ".csv" => ToCsv(result),
            _ => ToText(result)
        };
        await File.WriteAllTextAsync(path, content, new UTF8Encoding(true), cancellationToken);
    }

    private static string ToText(SolveResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("K-OPT 2026 결과");
        builder.AppendLine($"상태: {result.Status}");
        builder.AppendLine($"목적값: {Format(result.ObjectiveValue)}");
        builder.AppendLine($"솔버: {result.Solver.Name} {result.Solver.Version}");
        builder.AppendLine($"모델 SHA-256: {result.ModelHash}");
        builder.AppendLine($"계산 시간: {result.SolveTime.TotalMilliseconds:F2} ms");
        builder.AppendLine();
        builder.AppendLine("[변수]");
        foreach (var item in result.Variables) builder.AppendLine($"{item.Name}\t{Format(item.Value)}\t{item.Type}\t수정비용 {Format(item.ReducedCost)}");
        builder.AppendLine();
        builder.AppendLine("[제약식]");
        foreach (var item in result.Constraints) builder.AppendLine($"{item.Name}\t활동 {Format(item.Activity)}\t여유/잉여 {Format(item.Slack)}\t쌍대값 {Format(item.Dual)}");
        if (result.AlternativeOptima is { } alternatives)
        {
            builder.AppendLine();
            builder.AppendLine("[복수 최적해 분석]");
            builder.AppendLine($"판정: {alternatives.Multiplicity}");
            builder.AppendLine($"연속 최적해 집합: {(alternatives.IsContinuousFamily ? "예" : "아니요")}");
            builder.AppendLine($"범위 분석: {(alternatives.IsComplete ? "완료" : "미완료")}");
            foreach (var range in alternatives.VariableRanges)
                builder.AppendLine($"범위\t{range.Name}\t{Format(range.Minimum)}\t{Format(range.Maximum)}");
            foreach (var solution in alternatives.RepresentativeSolutions)
                builder.AppendLine($"대표해 {solution.Number}\t{string.Join("\t", solution.Variables.Select(value => $"{value.Name}={Format(value.Value)}"))}");
        }
        if (result.Sensitivity is { } sensitivity)
        {
            builder.AppendLine();
            builder.AppendLine("[민감도 분석]");
            builder.AppendLine($"사용 가능: {(sensitivity.IsAvailable ? "예" : "아니요")}");
            builder.AppendLine($"안내: {sensitivity.Message}");
            foreach (var range in sensitivity.ObjectiveCoefficients)
                builder.AppendLine($"목적계수\t{range.Name}\t현재 {Format(range.CurrentValue)}\t하한 {Format(range.LowerValue)}\t상한 {Format(range.UpperValue)}");
            foreach (var range in sensitivity.RightHandSides)
                builder.AppendLine($"우변\t{range.Name}\t현재 {Format(range.CurrentValue)}\t하한 {Format(range.LowerValue)}\t상한 {Format(range.UpperValue)}");
        }
        return builder.ToString();
    }

    private static string ToCsv(SolveResult result)
    {
        var builder = new StringBuilder("종류,이름,값,부가값\r\n");
        foreach (var item in result.Variables) builder.AppendLine($"변수,{Safe(item.Name)},{Format(item.Value)},{Format(item.ReducedCost)}");
        foreach (var item in result.Constraints) builder.AppendLine($"제약식,{Safe(item.Name)},{Format(item.Activity)},{Format(item.Slack)}");
        if (result.AlternativeOptima is { } alternatives)
        {
            builder.AppendLine($"복수최적해,판정,{alternatives.Multiplicity},{(alternatives.IsComplete ? "완료" : "미완료")}");
            foreach (var range in alternatives.VariableRanges)
                builder.AppendLine($"최적범위,{Safe(range.Name)},{Format(range.Minimum)},{Format(range.Maximum)}");
            foreach (var solution in alternatives.RepresentativeSolutions)
                builder.AppendLine($"대표최적해,{solution.Number},{Safe(string.Join("; ", solution.Variables.Select(value => $"{value.Name}={Format(value.Value)}")))},");
        }
        if (result.Sensitivity is { } sensitivity)
        {
            builder.AppendLine($"민감도분석,사용 가능,{(sensitivity.IsAvailable ? "예" : "아니요")},{Safe(sensitivity.Message)}");
            foreach (var range in sensitivity.ObjectiveCoefficients)
                builder.AppendLine($"목적계수민감도,{Safe(range.Name)},{Format(range.CurrentValue)},{Safe($"하한 {Format(range.LowerValue)}; 상한 {Format(range.UpperValue)}")}");
            foreach (var range in sensitivity.RightHandSides)
                builder.AppendLine($"우변민감도,{Safe(range.Name)},{Format(range.CurrentValue)},{Safe($"하한 {Format(range.LowerValue)}; 상한 {Format(range.UpperValue)}")}");
        }
        return builder.ToString();
    }

    private static string Safe(string value)
    {
        var escaped = value.Replace("\"", "\"\"");
        if (escaped.Length > 0 && "=+-@".Contains(escaped[0])) escaped = "'" + escaped;
        return $"\"{escaped}\"";
    }
    private static string ToJson(SolveResult result)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.Serialize(result, options);
    }
    private static string Format(double? value) => value?.ToString("G15", CultureInfo.InvariantCulture) ?? "-";
}
