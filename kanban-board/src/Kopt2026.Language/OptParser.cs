using System.Globalization;
using System.Text.RegularExpressions;
using Kopt2026.Domain;

namespace Kopt2026.Language;

public sealed partial class OptParser
{
    private const int MaxDocumentLength = 4 * 1024 * 1024;

    public ParseResult Parse(SourceDocument source)
    {
        var diagnostics = new List<Diagnostic>();
        if (source.Text.Length > MaxDocumentLength)
        {
            diagnostics.Add(Error("KOP090", 1, 1, "문서가 4MB 제한을 초과했습니다.", "문서를 나누거나 불필요한 내용을 제거하십시오."));
            return new(null, diagnostics);
        }

        var lines = source.Text.Split('\n');
        var effective = lines.Select((text, index) => (Text: text.Trim(), Line: index + 1))
            .Where(x => x.Text.Length > 0 && !x.Text.StartsWith('#')).ToList();
        if (effective.Count == 0)
        {
            diagnostics.Add(Error("KOP001", 1, 1, "MAX 또는 MIN이 없습니다.", "첫 줄에 MAX 또는 MIN을 입력하십시오."));
            return new(null, diagnostics);
        }

        ObjectiveSense sense;
        if (effective[0].Text.Equals("MAX", StringComparison.OrdinalIgnoreCase)) sense = ObjectiveSense.Maximize;
        else if (effective[0].Text.Equals("MIN", StringComparison.OrdinalIgnoreCase)) sense = ObjectiveSense.Minimize;
        else
        {
            diagnostics.Add(Error("KOP001", effective[0].Line, 1, "첫 유효 줄은 MAX 또는 MIN이어야 합니다.", "MAX 또는 MIN을 별도 줄에 입력하십시오."));
            return new(null, diagnostics);
        }

        if (effective.Count < 2)
        {
            diagnostics.Add(Error("KOP011", effective[0].Line + 1, 1, "목적식이 없습니다.", "목적식을 입력하십시오."));
            return new(null, diagnostics);
        }

        var stIndex = -1;
        for (var index = 1; index < effective.Count; index++)
        {
            if (effective[index].Text.Equals("END", StringComparison.OrdinalIgnoreCase)) break;
            if (effective[index].Text.Equals("ST", StringComparison.OrdinalIgnoreCase)) { stIndex = index; break; }
        }
        if (stIndex < 0)
        {
            diagnostics.Add(Error("KOP002", effective[1].Line, 1, "ST가 없습니다.", "목적식 마지막 줄 다음에 ST를 별도 줄로 입력하십시오."));
            return new(null, diagnostics);
        }
        if (stIndex == 1)
        {
            diagnostics.Add(Error("KOP011", effective[stIndex].Line, 1, "목적식이 없습니다.", "MAX/MIN과 ST 사이에 목적식을 입력하십시오."));
            return new(null, diagnostics);
        }

        // Preserve physical lines and indentation so joined-expression diagnostics point to the editor.
        var objectiveLines = lines.Skip(effective[0].Line)
            .Take(effective[stIndex].Line - effective[0].Line - 1)
            .Select(text => text.TrimStart().StartsWith('#') ? "" : text.TrimEnd('\r')).ToArray();
        var objectiveText = string.Join('\n', objectiveLines);
        var lineStarts = new int[objectiveLines.Length];
        for (var index = 1; index < lineStarts.Length; index++)
            lineStarts[index] = lineStarts[index - 1] + objectiveLines[index - 1].Length + 1;
        (int Line, int Column) ObjectivePosition(int offset)
        {
            var index = Array.BinarySearch(lineStarts, offset);
            if (index < 0) index = ~index - 1;
            return (effective[0].Line + index + 1, offset - lineStarts[index] + 1);
        }

        var variables = new Dictionary<string, Variable>(StringComparer.OrdinalIgnoreCase);
        var objective = ParseExpression(objectiveText, effective[0].Line + 1, diagnostics, variables, ObjectivePosition);

        var constraints = new List<Constraint>();
        var integerNames = new List<(string Name, int Line)>();
        var sawEnd = false;
        foreach (var item in effective.Skip(stIndex + 1))
        {
            if (item.Text.Equals("END", StringComparison.OrdinalIgnoreCase)) { sawEnd = true; break; }
            if (item.Text.StartsWith("INTEGER", StringComparison.OrdinalIgnoreCase))
            {
                var rest = item.Text[7..].Trim();
                foreach (var name in rest.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries)) integerNames.Add((name, item.Line));
                continue;
            }

            var matches = RelationRegex().Matches(item.Text);
            if (matches.Count != 1)
            {
                diagnostics.Add(Error("KOP012", item.Line, 1, "제약식에는 관계 연산자 하나가 필요합니다.", "<=, >=, = 중 하나를 사용하십시오."));
                continue;
            }

            var match = matches[0];
            var left = item.Text[..match.Index].Trim();
            var label = $"C{constraints.Count + 1}";
            var colon = left.IndexOf(':');
            if (colon >= 0) { label = left[..colon].Trim(); left = left[(colon + 1)..].Trim(); }
            var right = item.Text[(match.Index + match.Length)..].Trim();
            if (!double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var rhs))
            {
                diagnostics.Add(Error("KOP013", item.Line, match.Index + match.Length + 1, "제약식 우변은 숫자여야 합니다.", "변수 항을 좌변으로 이동하십시오."));
                continue;
            }
            var expr = ParseExpression(left, item.Line, diagnostics, variables);
            var relation = match.Value switch { "<=" => Relation.LessOrEqual, ">=" => Relation.GreaterOrEqual, _ => Relation.Equal };
            if (expr is not null)
            {
                var normalizedRightHandSide = rhs - expr.Constant;
                var normalizedExpression = expr.Constant == 0 ? expr : expr with { Constant = 0 };
                constraints.Add(new($"row-{constraints.Count + 1}", label, normalizedExpression, relation, normalizedRightHandSide, new(item.Line, 1, item.Text.Length)));
            }
        }

        if (!sawEnd) diagnostics.Add(Error("KOP003", lines.Length, 1, "END가 없습니다.", "문서 끝에 END를 입력하십시오."));
        var seenInteger = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in integerNames)
        {
            if (!seenInteger.Add(declaration.Name)) diagnostics.Add(Error("KOP022", declaration.Line, 1, $"정수변수 {declaration.Name}이(가) 중복되었습니다.", "한 번만 선언하십시오."));
            else if (!variables.TryGetValue(declaration.Name, out var variable)) diagnostics.Add(Error("KOP021", declaration.Line, 1, $"정수변수 {declaration.Name}이(가) 모델에 없습니다.", "모델에서 사용하거나 선언을 제거하십시오."));
            else variables[declaration.Name] = variable with { Type = VariableType.Integer };
        }

        if (objective is null || diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error)) return new(null, diagnostics);
        return new(new OptimizationModel(sense, objective, variables.Values.ToList(), constraints, source.EncodingName, source.Hash), diagnostics);
    }

    private static LinearExpression? ParseExpression(string text, int line, List<Diagnostic> diagnostics, Dictionary<string, Variable> variables,
        Func<int, (int Line, int Column)>? sourcePosition = null)
    {
        (int Line, int Column) Position(int offset)
        {
            while (offset < text.Length && char.IsWhiteSpace(text[offset])) offset++;
            return sourcePosition is null ? (line, offset + 1) : sourcePosition(offset);
        }
        Diagnostic ExpressionError(int offset, string message, string hint)
        {
            var position = Position(offset);
            return Error("KOP011", position.Line, position.Column, message, hint);
        }
        var coefficients = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var constant = 0d;
        var cursor = 0;
        var isFirstTerm = true;
        foreach (Match match in TermRegex().Matches(text))
        {
            if (!string.IsNullOrWhiteSpace(text[cursor..match.Index]))
            {
                diagnostics.Add(ExpressionError(cursor, "선형식의 연산자 또는 항이 올바르지 않습니다.", "항 사이에 + 또는 -를 사용하십시오."));
                return null;
            }
            if (!isFirstTerm && match.Groups[1].Length == 0)
            {
                diagnostics.Add(ExpressionError(match.Index, "선형식의 항 사이에 연산자가 없습니다.", "항 사이에 + 또는 -를 사용하십시오."));
                return null;
            }
            cursor = match.Index + match.Length;
            var sign = match.Groups[1].Value == "-" ? -1d : 1d;
            var name = match.Groups[3].Value;
            if (name.Length > 0)
            {
                var numberText = match.Groups[2].Value;
                var coefficient = string.IsNullOrEmpty(numberText) ? 1d : double.Parse(numberText, CultureInfo.InvariantCulture);
                if (coefficients.TryGetValue(name, out var prior))
                {
                    coefficients[name] = prior + sign * coefficient;
                    var position = Position(match.Groups[3].Index);
                    diagnostics.Add(new("KOP040", DiagnosticSeverity.Warning, new(position.Line, position.Column, name.Length), $"{name}의 중복 항을 합산했습니다.", "읽기 쉬운 식으로 정리하는 것을 권장합니다."));
                }
                else coefficients[name] = sign * coefficient;
                if (!variables.ContainsKey(name)) variables[name] = new($"col-{variables.Count + 1}", name, VariableType.Continuous);
            }
            else constant += sign * double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
            isFirstTerm = false;
        }
        if (!string.IsNullOrWhiteSpace(text[cursor..]) || coefficients.Count == 0)
        {
            diagnostics.Add(ExpressionError(cursor, "선형식을 해석할 수 없습니다.", "예: 3 X1 + 2 X2"));
            return null;
        }
        return new(coefficients, constant);
    }

    private static Diagnostic Error(string code, int line, int column, string message, string hint) => new(code, DiagnosticSeverity.Error, new(Math.Max(1, line), Math.Max(1, column), 1), message, hint);

    [GeneratedRegex(@"<=|>=|=")]
    private static partial Regex RelationRegex();
    [GeneratedRegex(@"\G\s*([+-]?)\s*(?:(?:(?:((?>(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?))\s*)?([A-Za-z][A-Za-z0-9_]*))|((?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?))", RegexOptions.CultureInvariant)]
    private static partial Regex TermRegex();
}
