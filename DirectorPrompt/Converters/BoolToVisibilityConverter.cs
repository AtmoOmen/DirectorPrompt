using System.Globalization;
using System.Windows.Data;

namespace DirectorPrompt.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var boolValue = value is bool b && b;
        var invert = parameter is string s && s == "Invert";

        return (boolValue ^ invert) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visibility = value is System.Windows.Visibility v ? v : System.Windows.Visibility.Collapsed;
        var invert = parameter is string s && s == "Invert";

        return (visibility == System.Windows.Visibility.Visible) ^ invert;
    }
}
