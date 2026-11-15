using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LinearBar.Infrastructure;

/// <summary>
/// Visible when the bound number is at least the converter parameter, otherwise collapsed.
/// Used for the "one dot per window" running indicators.
/// </summary>
public sealed class MinimumToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double actual = value is IConvertible number ? number.ToDouble(CultureInfo.InvariantCulture) : 0;
        double minimum = parameter is null ? 1 : System.Convert.ToDouble(parameter, CultureInfo.InvariantCulture);
        return actual >= minimum ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
