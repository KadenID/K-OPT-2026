using System.Globalization;
using Kopt2026.Domain;

namespace Kopt2026.Application;

public static class ResultNumberFormatter
{
    public const string DisplayFormat = "#,##0.######";

    public static string Format(double? value, int decimalPlaces = ResultDisplayPrecision.Default)
    {
        if (!ResultDisplayPrecision.IsValid(decimalPlaces)) throw new ArgumentOutOfRangeException(nameof(decimalPlaces));
        if (value is null) return "-";
        if (double.IsNaN(value.Value)) return "NaN";
        if (double.IsPositiveInfinity(value.Value)) return "+∞";
        if (double.IsNegativeInfinity(value.Value)) return "-∞";
        var rounded = Math.Round(value.Value, decimalPlaces, MidpointRounding.AwayFromZero);
        if (rounded == 0) rounded = 0d;
        var text = rounded.ToString($"N{decimalPlaces}", CultureInfo.InvariantCulture);
        return decimalPlaces == 0 ? text : text.TrimEnd('0').TrimEnd('.');
    }

    public static string Format(long value) => value.ToString("#,##0", CultureInfo.InvariantCulture);
}
