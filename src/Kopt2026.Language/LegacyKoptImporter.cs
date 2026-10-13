using System.Text.RegularExpressions;

namespace Kopt2026.Language;

public sealed record LegacyImportChange(int OriginalLine, string Description);

public sealed record LegacyKoptImportResult(
    SourceDocument Document,
    bool WasConverted,
    IReadOnlyList<LegacyImportChange> Changes,
    string Message);

/// <summary>
/// 구형 K-OPT 파일에서 확인된 명확한 문법 차이만 현대 문법으로 정규화한다.
/// 변환 결과가 현재 파서로 완전히 검증될 때만 정규화된 문서를 반환한다.
/// </summary>
public sealed partial class LegacyKoptImporter
{
    private readonly OptParser parser;

    public LegacyKoptImporter(OptParser? parser = null)
    {
        this.parser = parser ?? new OptParser();
    }

    public LegacyKoptImportResult Import(SourceDocument source)
    {
        if (parser.Parse(source).IsValid)
        {
            return new(source, false, [], "현재 K-OPT 2026 형식으로 열었습니다.");
        }

        var lines = source.Text.Split('\n');
        var normalizedLines = new List<string>(lines.Length + 2);
        var changes = new List<LegacyImportChange>();
        var inConstraints = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var original = lines[index];
            var trimmed = original.Trim();
            var lineNumber = index + 1;

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                normalizedLines.Add(original);
                continue;
            }

            var objective = ObjectiveWithExpressionRegex().Match(original);
            if (!inConstraints && objective.Success)
            {
                normalizedLines.Add(objective.Groups[1].Value.ToUpperInvariant());
                normalizedLines.Add(objective.Groups[2].Value.Trim());
                changes.Add(new(lineNumber, $"{objective.Groups[1].Value.ToUpperInvariant()} 뒤의 목적식을 새 줄로 분리했습니다."));
                continue;
            }

            if (!inConstraints && trimmed.Equals("ST", StringComparison.OrdinalIgnoreCase))
            {
                normalizedLines.Add("ST");
                inConstraints = true;
                continue;
            }

            var subjectTo = SubjectToWithConstraintRegex().Match(original);
            if (!inConstraints && subjectTo.Success)
            {
                normalizedLines.Add("ST");
                normalizedLines.Add(NormalizeLegacyRelation(subjectTo.Groups[1].Value.Trim(), lineNumber, changes));
                changes.Add(new(lineNumber, "ST 뒤의 첫 제약식을 새 줄로 분리했습니다."));
                inConstraints = true;
                continue;
            }

            if (inConstraints && trimmed.Equals("END", StringComparison.OrdinalIgnoreCase))
            {
                normalizedLines.Add("END");
                inConstraints = false;
                continue;
            }

            if (inConstraints && !trimmed.StartsWith("INTEGER", StringComparison.OrdinalIgnoreCase))
            {
                normalizedLines.Add(NormalizeLegacyRelation(original, lineNumber, changes));
                continue;
            }

            normalizedLines.Add(original);
        }

        if (changes.Count == 0)
        {
            return new(source, false, [], "자동 변환할 수 있는 명확한 구형 문법을 찾지 못했습니다.");
        }

        var normalized = SourceDocument.FromText(string.Join('\n', normalizedLines), "UTF-8");
        if (!parser.Parse(normalized).IsValid)
        {
            return new(source, false, [], "구형 문법 변환 결과를 안전하게 검증하지 못해 원문을 유지했습니다.");
        }

        return new(
            normalized,
            true,
            changes,
            $"구형 K-OPT 문법 {changes.Count}개 항목을 K-OPT 2026 형식으로 변환했습니다.");
    }

    private static string NormalizeLegacyRelation(
        string line,
        int lineNumber,
        List<LegacyImportChange> changes)
    {
        var lessThanCount = StandaloneLessThanRegex().Matches(line).Count;
        var greaterThanCount = StandaloneGreaterThanRegex().Matches(line).Count;
        var normalized = StandaloneLessThanRegex().Replace(line, "<=");
        normalized = StandaloneGreaterThanRegex().Replace(normalized, ">=");

        if (lessThanCount > 0)
        {
            changes.Add(new(lineNumber, "구형 '<' 관계식을 '<='로 변환했습니다."));
        }

        if (greaterThanCount > 0)
        {
            changes.Add(new(lineNumber, "구형 '>' 관계식을 '>='로 변환했습니다."));
        }

        return normalized;
    }

    [GeneratedRegex(@"^\s*(MAX|MIN)\b\s+(.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ObjectiveWithExpressionRegex();

    [GeneratedRegex(@"^\s*ST\b\s+(.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SubjectToWithConstraintRegex();

    [GeneratedRegex(@"(?<![<>=])<(?![=])", RegexOptions.CultureInvariant)]
    private static partial Regex StandaloneLessThanRegex();

    [GeneratedRegex(@"(?<![<>=])>(?![=])", RegexOptions.CultureInvariant)]
    private static partial Regex StandaloneGreaterThanRegex();
}
