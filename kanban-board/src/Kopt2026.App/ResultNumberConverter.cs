using System.Globalization;
using System.Windows.Data;
using Kopt2026.Application;
using Kopt2026.Domain;

namespace Kopt2026.App;

public sealed class ResultNumberConverter : IValueConverter, IMultiValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Format(value, ResultDisplayPrecision.Default);

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        Format(values[0], values.Length > 1 && values[1] is int digits ? ResultDisplayPrecision.Normalize(digits) : ResultDisplayPrecision.Default);

    private static string Format(object value, int decimalPlaces) => value switch
    {
        double number => ResultNumberFormatter.Format(number, decimalPlaces),
        float number => ResultNumberFormatter.Format(number, decimalPlaces),
        long number => ResultNumberFormatter.Format(number),
        int number => ResultNumberFormatter.Format(number),
        _ => "-"
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
