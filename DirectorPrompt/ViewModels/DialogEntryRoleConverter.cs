using System.Globalization;
using System.Windows.Data;

namespace DirectorPrompt.ViewModels;

public sealed class DialogEntryRoleConverter : IValueConverter
{
    public static readonly DialogEntryRoleConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isDirector && isDirector)
        {
            return "导演";
        }

        return "AI";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
