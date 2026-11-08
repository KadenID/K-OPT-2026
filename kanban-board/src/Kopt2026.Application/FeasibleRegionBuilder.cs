using Kopt2026.Domain;

namespace Kopt2026.Application;

public readonly record struct PlotPoint(double X, double Y);

public static class FeasibleRegionBuilder
{
    public static IReadOnlyList<PlotPoint> Build(OptimizationModel model, double maxX, double maxY)
    {
        if (model.Variables.Count != 2 || maxX <= 0 || maxY <= 0) return [];
        IReadOnlyList<PlotPoint> polygon =
        [
            new(0, 0), new(maxX, 0), new(maxX, maxY), new(0, maxY)
        ];
        var xName = model.Variables[0].Name;
        var yName = model.Variables[1].Name;

        foreach (var constraint in model.Constraints)
        {
            var a = constraint.Expression.Coefficients.GetValueOrDefault(xName);
            var b = constraint.Expression.Coefficients.GetValueOrDefault(yName);
            if (Math.Abs(a) < 1e-15 && Math.Abs(b) < 1e-15) continue;
            polygon = constraint.Relation switch
            {
                Relation.LessOrEqual => Clip(polygon, a, b, constraint.RightHandSide, true),
                Relation.GreaterOrEqual => Clip(polygon, a, b, constraint.RightHandSide, false),
                _ => Clip(Clip(polygon, a, b, constraint.RightHandSide + 1e-8, true), a, b, constraint.RightHandSide - 1e-8, false)
            };
            if (polygon.Count == 0) break;
        }
        return polygon;
    }

    public static bool Contains(OptimizationModel model, PlotPoint point, double tolerance = 1e-7)
    {
        if (model.Variables.Count != 2) return false;
        var xName = model.Variables[0].Name;
        var yName = model.Variables[1].Name;
        return point.X >= -tolerance && point.Y >= -tolerance && model.Constraints.All(c =>
        {
            var value = c.Expression.Coefficients.GetValueOrDefault(xName) * point.X + c.Expression.Coefficients.GetValueOrDefault(yName) * point.Y;
            return c.Relation switch
            {
                Relation.LessOrEqual => value <= c.RightHandSide + tolerance,
                Relation.GreaterOrEqual => value >= c.RightHandSide - tolerance,
                _ => Math.Abs(value - c.RightHandSide) <= tolerance
            };
        });
    }

    private static IReadOnlyList<PlotPoint> Clip(IReadOnlyList<PlotPoint> input, double a, double b, double rhs, bool keepLess)
    {
        if (input.Count == 0) return [];
        var output = new List<PlotPoint>();
        var previous = input[^1];
        var previousInside = Inside(previous);
        foreach (var current in input)
        {
            var currentInside = Inside(current);
            if (currentInside != previousInside) output.Add(Intersection(previous, current));
            if (currentInside) output.Add(current);
            previous = current;
            previousInside = currentInside;
        }
        return output;

        bool Inside(PlotPoint point)
        {
            var residual = a * point.X + b * point.Y - rhs;
            return keepLess ? residual <= 1e-9 : residual >= -1e-9;
        }

        PlotPoint Intersection(PlotPoint from, PlotPoint to)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var denominator = a * dx + b * dy;
            if (Math.Abs(denominator) < 1e-15) return from;
            var t = (rhs - a * from.X - b * from.Y) / denominator;
            return new(from.X + t * dx, from.Y + t * dy);
        }
    }
}
