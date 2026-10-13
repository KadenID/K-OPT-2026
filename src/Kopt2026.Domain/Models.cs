namespace Kopt2026.Domain;

public enum ObjectiveSense { Maximize, Minimize }
public enum VariableType { Continuous, Integer }
public enum Relation { LessOrEqual, GreaterOrEqual, Equal }
public enum DiagnosticSeverity { Warning, Error }

public sealed record SourceSpan(int Line, int Column, int Length);
public sealed record Diagnostic(string Code, DiagnosticSeverity Severity, SourceSpan Span, string Message, string Hint);
public sealed record Variable(string Id, string Name, VariableType Type, double Lower = 0, double Upper = double.PositiveInfinity);
public sealed record LinearExpression(IReadOnlyDictionary<string, double> Coefficients, double Constant = 0);
public sealed record Constraint(string Id, string Name, LinearExpression Expression, Relation Relation, double RightHandSide, SourceSpan Span);

public sealed record OptimizationModel(
    ObjectiveSense Sense,
    LinearExpression Objective,
    IReadOnlyList<Variable> Variables,
    IReadOnlyList<Constraint> Constraints,
    string SourceEncoding,
    string SourceHash);

public sealed record ParseResult(OptimizationModel? Model, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Model is not null && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
}
