using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Data;

namespace DirectorPrompt.Converters;

public sealed class EnumFriendlyItem
{
    public object Value { get; init; } = default!;

    public string Display { get; init; } = string.Empty;

    public override string ToString() => Display;

    public override bool Equals(object? obj) =>
        obj is EnumFriendlyItem other && Value.Equals(other.Value);

    public override int GetHashCode() => Value.GetHashCode();
}

public sealed class EnumDisplayConverter : IValueConverter
{
    public static EnumDisplayConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return Array.Empty<EnumFriendlyItem>();
        }

        var enumType = value as Type ?? value.GetType();

        if (!enumType.IsEnum)
        {
            return Array.Empty<EnumFriendlyItem>();
        }

        var items = Enum.GetValues(enumType)
                        .Cast<object>()
                        .Select(v => new EnumFriendlyItem
                        {
                            Value = v,
                            Display = GetDescription(v)
                        })
                        .ToArray();

        if (!enumType.IsValueType || value is Type)
        {
            return items;
        }

        return items.FirstOrDefault(i => i.Value.Equals(value)) ?? items[0];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is EnumFriendlyItem item)
        {
            return item.Value;
        }

        return Binding.DoNothing;
    }

    private static string GetDescription(object enumValue)
    {
        var field = enumValue.GetType().GetField(enumValue.ToString()!);

        if (field?.GetCustomAttribute<DescriptionAttribute>() is { } attr)
        {
            return attr.Description;
        }

        return enumValue.ToString()!;
    }
}
