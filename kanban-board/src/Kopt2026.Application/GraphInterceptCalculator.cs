using Kopt2026.Domain;

namespace Kopt2026.Application;

public sealed record GraphLineIntercepts(string Name, double? XIntercept, double? YIntercept, bool IsObjective);

public static class GraphInterceptCalculator
{
    public static IReadOnlyList<GraphLineIntercepts> Calculate(OptimizationModel model, double? objectiveValue)
    {
        if (model.Variables.Count != 2) return [];
        var xName = model.Variables[0].Name;
        var yName = model.Variables[1].Name;
        var result = new List<GraphLineIntercepts>();

        foreach (var constraint in model.Constraints)
        {
            var a = constraint.Expression.Coefficients.GetValueOrDefault(xName);
            var b = constraint.Expression.Coefficients.GetValueOrDefault(yName);
            result.Add(CalculateLine(constraint.Name, a, b, constraint.RightHandSide, false));
        }

        if (objectiveValue is double value && double.IsFinite(value))
        {
            var a = model.Objective.Coefficients.GetValueOrDefault(xName);
            var b = model.Objective.Coefficients.GetValueOrDefault(yName);
            result.Add(CalculateLine("목적식", a, b, value - model.Objective.Constant, true));
        }
        return result;
    }

    public static GraphLineIntercepts CalculateLine(string name, double xCoefficient, double yCoefficient, double rightHandSide, bool isObjective)
    {
        var x = Math.Abs(xCoefficient) < 1e-12 ? null : FiniteOrNull(rightHandSide / xCoefficient);
        var y = Math.Abs(yCoefficient) < 1e-12 ? null : FiniteOrNull(rightHandSide / yCoefficient);
        return new(name, x, y, isObjective);
    }

    private static double? FiniteOrNull(double value) => double.IsFinite(value) ? value : null;
}
