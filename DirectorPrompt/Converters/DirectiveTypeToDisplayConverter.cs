using System.Globalization;
using System.Windows.Data;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Converters;

public sealed class DirectiveTypeToDisplayConverter : IValueConverter
{
    private static readonly Dictionary<DirectiveType, string> DisplayNames = new()
    {
        [DirectiveType.Plot] = "剧情",
        [DirectiveType.Tone] = "基调",
        [DirectiveType.TemporaryConstraint] = "临时约束",
        [DirectiveType.SceneChange] = "时间/场景变更"
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DirectiveType type)
        {
            return DisplayNames.TryGetValue(type, out var name) ? name : type.ToString();
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s)
        {
            return DisplayNames.FirstOrDefault(kvp => kvp.Value == s).Key;
        }

        return DirectiveType.Plot;
    }
}
