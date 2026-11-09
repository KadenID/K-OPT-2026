using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Kopt2026.Application;
using Kopt2026.Domain;
using Kopt2026.Solver.Abstractions;
namespace Kopt2026.App;
public sealed class SolutionGraph : Control
{
    public static readonly StyledProperty<OptimizationModel?> ModelProperty = AvaloniaProperty.Register<SolutionGraph, OptimizationModel?>(nameof(Model));
    public static readonly StyledProperty<SolveResult?> ResultProperty = AvaloniaProperty.Register<SolutionGraph, SolveResult?>(nameof(Result));
    public static readonly StyledProperty<bool> UseKoreanTermsProperty = AvaloniaProperty.Register<SolutionGraph, bool>(nameof(UseKoreanTerms), true);
    public static readonly StyledProperty<bool> ShowInterceptsProperty = AvaloniaProperty.Register<SolutionGraph, bool>(nameof(ShowIntercepts), true);
    public static readonly StyledProperty<int> DecimalPlacesProperty = AvaloniaProperty.Register<SolutionGraph, int>(nameof(DecimalPlaces), ResultDisplayPrecision.Default,
        coerce: (_, value) => ResultDisplayPrecision.Normalize(value));
    static SolutionGraph()
    {
        AffectsRender<SolutionGraph>(ModelProperty, ResultProperty, UseKoreanTermsProperty, ShowInterceptsProperty, DecimalPlacesProperty);
        AffectsMeasure<SolutionGraph>(ModelProperty, ResultProperty, UseKoreanTermsProperty, ShowInterceptsProperty, DecimalPlacesProperty);
    }
    public OptimizationModel? Model { get => GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    public SolveResult? Result { get => GetValue(ResultProperty); set => SetValue(ResultProperty, value); }
    public bool UseKoreanTerms { get => GetValue(UseKoreanTermsProperty); set => SetValue(UseKoreanTermsProperty, value); }
    public bool ShowIntercepts { get => GetValue(ShowInterceptsProperty); set => SetValue(ShowInterceptsProperty, value); }
    public int DecimalPlaces { get => GetValue(DecimalPlacesProperty); set => SetValue(DecimalPlacesProperty, value); }
    private string DisplayNumber(double? value) => ResultNumberFormatter.Format(value, DecimalPlaces);

    private const double BarRowHeight = 32;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 640d;
        var height = 210d;
        if (Model is not null && Model.Variables.Count != 2 && Result is { } result)
        {
            var (nameWidth, valueWidth) = BarTextWidths(result);
            width = Math.Max(width, nameWidth + valueWidth + 280);
            height = Math.Max(height, 70 + result.Variables.Count * BarRowHeight);
        }
        return new Size(
            double.IsFinite(availableSize.Width) ? Math.Max(width, availableSize.Width) : width,
            double.IsFinite(availableSize.Height) ? Math.Max(height, availableSize.Height) : height);
    }

    private (double Name, double Value) BarTextWidths(SolveResult result)
    {
        var nameWidth = 80d;
        var valueWidth = 40d;
        foreach (var variable in result.Variables)
        {
            nameWidth = Math.Max(nameWidth, Format(variable.Name, Brushes.Black, 12, FontWeight.Normal).WidthIncludingTrailingWhitespace);
            valueWidth = Math.Max(valueWidth, Format(DisplayNumber(variable.Value), Brushes.Black, 12, FontWeight.Normal).WidthIncludingTrailingWhitespace);
        }
        return (nameWidth, valueWidth);
    }

    public override void Render(DrawingContext dc)
    {
        base.Render(dc);
        dc.DrawRectangle(Brushes.White, null, new Rect(Bounds.Size));
        if (Model is null || Result is null || Result.Status is not (SolveStatus.Optimal or SolveStatus.Feasible or SolveStatus.TimeLimit))
        {
            DrawCentered(dc, UseKoreanTerms ? "모델을 실행하면 그래프가 표시됩니다." : "Run the model to display a graph.", Brushes.SlateGray, 15);
            return;
        }
        if (Model.Variables.Count == 2) DrawFeasibleRegion(dc, Model, Result);
        else DrawVariableBars(dc, Result);
    }

    private void DrawFeasibleRegion(DrawingContext dc, OptimizationModel model, SolveResult result)
    {
        var summaryWidth = ShowIntercepts ? Math.Clamp(Bounds.Width * 0.25, 230, 310) : 0;
        var plot = new Rect(65, 28, Math.Max(100, Bounds.Width - summaryWidth - 105), Math.Max(100, Bounds.Height - 92));
        var xName = model.Variables[0].Name;
        var yName = model.Variables[1].Name;
        var xValue = result.Variables.First(v => v.Name.Equals(xName, StringComparison.OrdinalIgnoreCase)).Value;
        var yValue = result.Variables.First(v => v.Name.Equals(yName, StringComparison.OrdinalIgnoreCase)).Value;
        var intercepts = GraphInterceptCalculator.Calculate(model, result.ObjectiveValue);
        var maxX = AxisMaximum(model, xName, xValue, intercepts.Select(x => x.XIntercept));
        var maxY = AxisMaximum(model, yName, yValue, intercepts.Select(x => x.YIntercept));

        DrawGridAndAxes(dc, plot, maxX, maxY, xName, yName);
        var region = FeasibleRegionBuilder.Build(model, maxX, maxY);
        if (region.Count >= 3)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(Map(region[0]), true);
                foreach (var point in region.Skip(1)) context.LineTo(Map(point));
                context.EndFigure(true);
            }

            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(90, 37, 99, 235)), new Pen(new SolidColorBrush(Color.FromRgb(37, 99, 235)), 1.5), geometry);
        }

        var colors = new[] { Color.FromRgb(220, 38, 38), Color.FromRgb(5, 150, 105), Color.FromRgb(147, 51, 234), Color.FromRgb(234, 88, 12) };
        for (var i = 0; i < model.Constraints.Count; i++)
        {
            var constraint = model.Constraints[i];
            var a = constraint.Expression.Coefficients.GetValueOrDefault(xName);
            var b = constraint.Expression.Coefficients.GetValueOrDefault(yName);
            var points = BoundaryPoints(a, b, constraint.RightHandSide, maxX, maxY);
            if (points.Count == 2) dc.DrawLine(new Pen(new SolidColorBrush(colors[i % colors.Length]), 1.5), Map(points[0]), Map(points[1]));
        }

        var cx = model.Objective.Coefficients.GetValueOrDefault(xName);
        var cy = model.Objective.Coefficients.GetValueOrDefault(yName);
        if (result.ObjectiveValue is double objective)
        {
            var objectivePoints = BoundaryPoints(cx, cy, objective - model.Objective.Constant, maxX, maxY);
            if (objectivePoints.Count == 2)
            {
                var objectivePen = new Pen(new SolidColorBrush(Color.FromRgb(245, 158, 11)), 2) { DashStyle = DashStyle.Dash };
                dc.DrawLine(objectivePen, Map(objectivePoints[0]), Map(objectivePoints[1]));
            }
        }

        if (ShowIntercepts)
        {
            var reservedOptimum = Map(new PlotPoint(xValue, yValue));
            var optimumLabel = Format($"({DisplayNumber(xValue)}, {DisplayNumber(yValue)})", Brushes.Black, 12, FontWeight.Normal);
            var occupiedAreas = new List<Rect>
            {
                new(reservedOptimum.X - 8, reservedOptimum.Y - 8, 16, 16),
                new(reservedOptimum.X + 7, reservedOptimum.Y - 23, optimumLabel.Width + 4, optimumLabel.Height + 2)
            };
            DrawInterceptMarkers(dc, plot, maxX, maxY, intercepts, colors, occupiedAreas);
            DrawInterceptSummary(dc, new Rect(plot.Right + 14, plot.Top, summaryWidth - 22, plot.Height), intercepts, colors);
        }

        var optimum = Map(new PlotPoint(xValue, yValue));
        dc.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(15, 23, 42)), 3), optimum, 6, 6);
        DrawText(dc, $"({DisplayNumber(xValue)}, {DisplayNumber(yValue)})", optimum.X + 9, optimum.Y - 21, Brushes.Black, 12);
        DrawLegend(dc, plot, model.Constraints.Count);
        return;

        Point Map(PlotPoint point) => new(plot.Left + point.X / maxX * plot.Width, plot.Bottom - point.Y / maxY * plot.Height);
    }

    private void DrawVariableBars(DrawingContext dc, SolveResult result)
    {
        var title = UseKoreanTerms ? "변수 최적값" : "Variable solution values";
        DrawText(dc, title, 24, 18, Brushes.Black, 16, FontWeight.SemiBold);
        if (result.Variables.Count == 0) return;
        var maxValue = Math.Max(1, result.Variables.Max(v => Math.Abs(v.Value)));
        var (nameWidth, valueWidth) = BarTextWidths(result);
        var barLeft = 24 + nameWidth + 16;
        var availableWidth = Math.Max(100, Bounds.Width - barLeft - valueWidth - 48);
        for (var i = 0; i < result.Variables.Count; i++)
        {
            var variable = result.Variables[i];
            var y = 58 + i * BarRowHeight;
            DrawText(dc, variable.Name, 24, y, Brushes.Black, 12);
            var width = Math.Abs(variable.Value) / maxValue * availableWidth;
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(37, 99, 235)), null, new Rect(barLeft, y - 2, width, 20), 3, 3);
            DrawText(dc, DisplayNumber(variable.Value), barLeft + width + 10, y, Brushes.Black, 12);
        }
    }

    private void DrawGridAndAxes(DrawingContext dc, Rect plot, double maxX, double maxY, string xName, string yName)
    {
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(226, 232, 240)), 1);
        for (var i = 0; i <= 5; i++)
        {
            var x = plot.Left + plot.Width * i / 5;
            var y = plot.Bottom - plot.Height * i / 5;
            dc.DrawLine(gridPen, new(x, plot.Top), new(x, plot.Bottom));
            dc.DrawLine(gridPen, new(plot.Left, y), new(plot.Right, y));
            DrawText(dc, DisplayNumber(maxX * i / 5), x - 8, plot.Bottom + 8, Brushes.SlateGray, 10);
            DrawText(dc, DisplayNumber(maxY * i / 5), 12, y - 7, Brushes.SlateGray, 10);
        }
        var axisPen = new Pen(new SolidColorBrush(Color.FromRgb(51, 65, 85)), 1.5);
        dc.DrawLine(axisPen, new(plot.Left, plot.Bottom), new(plot.Right, plot.Bottom));
        dc.DrawLine(axisPen, new(plot.Left, plot.Bottom), new(plot.Left, plot.Top));
        DrawText(dc, xName, plot.Right - 18, plot.Bottom + 27, Brushes.Black, 12, FontWeight.SemiBold);
        DrawText(dc, yName, plot.Left - 42, plot.Top - 27, Brushes.Black, 12, FontWeight.SemiBold);
    }

    private void DrawLegend(DrawingContext dc, Rect plot, int constraintCount)
    {
        var text = UseKoreanTerms
            ? $"파랑: 실행가능영역  ·  색 선: 제약 경계({constraintCount})  ·  주황 점선: 목적함수  ·  ● 최적점"
            : $"Blue: feasible region  ·  Colored: constraints ({constraintCount})  ·  Orange dashed: objective  ·  ● optimum";
        DrawText(dc, text, plot.Left, 5, Brushes.SlateGray, 11);
    }

    private static double AxisMaximum(OptimizationModel model, string variableName, double solutionValue, IEnumerable<double?> lineIntercepts)
    {
        var candidates = new List<double> { 1, Math.Abs(solutionValue) * 1.35 };
        foreach (var constraint in model.Constraints)
        {
            var coefficient = constraint.Expression.Coefficients.GetValueOrDefault(variableName);
            if (Math.Abs(coefficient) > 1e-12)
            {
                var intercept = constraint.RightHandSide / coefficient;
                if (intercept > 0 && double.IsFinite(intercept)) candidates.Add(intercept * 1.15);
            }
        }
        candidates.AddRange(lineIntercepts.Where(value => value is > 0 && double.IsFinite(value.Value)).Select(value => value!.Value * 1.15));
        return candidates.Max();
    }

    private void DrawInterceptMarkers(
        DrawingContext dc,
        Rect plot,
        double maxX,
        double maxY,
        IReadOnlyList<GraphLineIntercepts> intercepts,
        IReadOnlyList<Color> colors,
        ICollection<Rect> occupiedAreas)
    {
        var markers = new List<InterceptMarker>();
        var xMarkers = intercepts
            .Where(item => item.XIntercept is >= 0 && item.XIntercept <= maxX)
            .OrderBy(item => item.XIntercept)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToList();
        foreach (var item in xMarkers)
        {
            var point = Map(item.XIntercept!.Value, 0);
            var brush = new SolidColorBrush(LineColor(item, intercepts, colors));
            var label = $"{ShortName(item)} ({DisplayNumber(item.XIntercept)}, 0)";
            markers.Add(new(item, point, label, brush, GraphLabelAxis.X));
        }

        var yMarkers = intercepts
            .Where(item => item.YIntercept is >= 0 && item.YIntercept <= maxY)
            .OrderByDescending(item => item.YIntercept)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToList();
        foreach (var item in yMarkers)
        {
            var point = Map(0, item.YIntercept!.Value);
            var brush = new SolidColorBrush(LineColor(item, intercepts, colors));
            var label = $"{ShortName(item)} (0, {DisplayNumber(item.YIntercept)})";
            markers.Add(new(item, point, label, brush, GraphLabelAxis.Y));
        }

        foreach (var marker in markers)
        {
            dc.DrawEllipse(Brushes.White, new Pen(marker.Brush, 2), marker.Point, 4, 4);
            occupiedAreas.Add(new(marker.Point.X - 5, marker.Point.Y - 5, 10, 10));
        }

        foreach (var marker in markers)
        {
            var formatted = Format(marker.Label, marker.Brush, 10, FontWeight.SemiBold);
            var size = new Size(formatted.Width + 4, formatted.Height + 2);
            var box = GraphLabelLayout.Place(size, marker.Point, plot, marker.Axis, occupiedAreas);
            if (box is null) continue;
            DrawLeaderLine(dc, marker.Point, box.Value, marker.Brush);
            DrawLabelBox(dc, formatted, box.Value);
        }

        Point Map(double x, double y) => new(plot.Left + x / maxX * plot.Width, plot.Bottom - y / maxY * plot.Height);
    }

    private void DrawInterceptSummary(
        DrawingContext dc,
        Rect panel,
        IReadOnlyList<GraphLineIntercepts> intercepts,
        IReadOnlyList<Color> colors)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(238, 248, 250, 252)), new Pen(new SolidColorBrush(Color.FromRgb(203, 213, 225)), 1), panel, 5, 5);
        DrawText(dc, UseKoreanTerms ? "X·Y절편" : "X/Y intercepts", panel.Left + 12, panel.Top + 10, Brushes.Black, 13, FontWeight.SemiBold);
        var rowHeight = Math.Max(16, Math.Min(28, (panel.Height - 42) / Math.Max(1, intercepts.Count)));
        for (var i = 0; i < intercepts.Count; i++)
        {
            var item = intercepts[i];
            var y = panel.Top + 39 + i * rowHeight;
            if (y + 14 > panel.Bottom) break;
            var color = LineColor(item, intercepts, colors);
            dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(panel.Left + 12, y + 2, 9, 9), 2, 2);
            var xText = item.XIntercept is double x ? $"({DisplayNumber(x)}, 0)" : (UseKoreanTerms ? "없음" : "none");
            var yText = item.YIntercept is double yValue ? $"(0, {DisplayNumber(yValue)})" : (UseKoreanTerms ? "없음" : "none");
            var name = item.IsObjective ? (UseKoreanTerms ? "목적식" : "Objective") : ShortName(item, 12);
            var text = UseKoreanTerms ? $"{name}  X {xText}  Y {yText}" : $"{name}  X {xText}  Y {yText}";
            DrawText(dc, text, panel.Left + 27, y, Brushes.Black, rowHeight < 20 ? 9 : 10);
        }
    }

    private static void DrawLeaderLine(DrawingContext dc, Point anchor, Rect box, IBrush brush)
    {
        var target = new Point(Math.Clamp(anchor.X, box.Left, box.Right), Math.Clamp(anchor.Y, box.Top, box.Bottom));
        if (new Vector(target.X - anchor.X, target.Y - anchor.Y).Length <= 5) return;
        dc.DrawLine(new Pen(brush, 0.75), anchor, target);
    }

    private static void DrawLabelBox(DrawingContext dc, FormattedText formatted, Rect box)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(218, 255, 255, 255)), null,
            box, 2, 2);
        dc.DrawText(formatted, new Point(box.Left + 2, box.Top + 1));
    }

    private static string ShortName(GraphLineIntercepts item, int maxLength = 8)
    {
        var name = item.IsObjective ? "OBJ" : item.Name;
        return name.Length <= maxLength ? name : name[..(maxLength - 1)] + "…";
    }

    private static Color LineColor(GraphLineIntercepts item, IReadOnlyList<GraphLineIntercepts> all, IReadOnlyList<Color> colors)
    {
        if (item.IsObjective) return Color.FromRgb(245, 158, 11);
        var index = 0;
        for (var i = 0; i < all.Count; i++)
        {
            if (ReferenceEquals(all[i], item) || all[i] == item) { index = i; break; }
        }
        return colors[index % colors.Count];
    }

    private static IReadOnlyList<PlotPoint> BoundaryPoints(double a, double b, double rhs, double maxX, double maxY)
    {
        var points = new List<PlotPoint>();
        Add(0, b == 0 ? double.NaN : rhs / b);
        Add(maxX, b == 0 ? double.NaN : (rhs - a * maxX) / b);
        Add(a == 0 ? double.NaN : rhs / a, 0);
        Add(a == 0 ? double.NaN : (rhs - b * maxY) / a, maxY);
        return points.DistinctBy(p => (Math.Round(p.X, 9), Math.Round(p.Y, 9))).Take(2).ToList();

        void Add(double x, double y)
        {
            if (double.IsFinite(x) && double.IsFinite(y) && x >= -1e-9 && x <= maxX + 1e-9 && y >= -1e-9 && y <= maxY + 1e-9)
                points.Add(new(Math.Clamp(x, 0, maxX), Math.Clamp(y, 0, maxY)));
        }
    }

    private void DrawCentered(DrawingContext dc, string text, IBrush brush, double size)
    {
        var formatted = Format(text, brush, size, FontWeight.Normal);
        dc.DrawText(formatted, new Point((Bounds.Width - formatted.Width) / 2, (Bounds.Height - formatted.Height) / 2));
    }

    private void DrawText(DrawingContext dc, string text, double x, double y, IBrush brush, double size, FontWeight? weight = null) =>
        dc.DrawText(Format(text, brush, size, weight ?? FontWeight.Normal), new Point(x, y));

    private FormattedText Format(string text, IBrush brush, double size, FontWeight weight) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, weight, FontStretch.Normal), size, brush);

    private sealed record InterceptMarker(GraphLineIntercepts Item, Point Point, string Label, IBrush Brush, GraphLabelAxis Axis);
}
