using System.Globalization;

namespace Kopt2026.Application;

public static class ResultNumberFormatter
{
    public const string DisplayFormat = "#,##0.##########";

    public static string Format(double? value)
    {
        if (value is null) return "-";
        if (double.IsNaN(value.Value)) return "NaN";
        if (double.IsPositiveInfinity(value.Value)) return "+∞";
        if (double.IsNegativeInfinity(value.Value)) return "-∞";
        var normalized = Math.Abs(value.Value) < 5e-13 ? 0d : value.Value;
        return normalized.ToString(DisplayFormat, CultureInfo.InvariantCulture);
    }

    public static string Format(long value) => value.ToString("#,##0", CultureInfo.InvariantCulture);
}
