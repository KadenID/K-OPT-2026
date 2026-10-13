using System.Globalization;
using System.Windows.Data;
using Kopt2026.Application;

namespace Kopt2026.App;

public sealed class ResultNumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        double number => ResultNumberFormatter.Format(number),
        float number => ResultNumberFormatter.Format(number),
        long number => ResultNumberFormatter.Format(number),
        int number => ResultNumberFormatter.Format(number),
        _ => "-"
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
