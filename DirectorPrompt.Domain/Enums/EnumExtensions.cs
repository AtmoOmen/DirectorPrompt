using System.ComponentModel;
using System.Reflection;

namespace DirectorPrompt.Domain.Enums;

public static class EnumExtensions
{
    public static string GetDescription(this Enum value)
    {
        var field = value.GetType().GetField(value.ToString());

        if (field?.GetCustomAttribute<DescriptionAttribute>() is { } attr)
        {
            return attr.Description;
        }

        return value.ToString();
    }
}
