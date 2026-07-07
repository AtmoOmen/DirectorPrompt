using System.Globalization;
using System.Windows.Data;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Converters;

public sealed class EventTypeToDisplayConverter : IValueConverter
{
    private static readonly Dictionary<EventType, string> DisplayNames = new()
    {
        [EventType.DirectorInput]   = "导演",
        [EventType.NarrativeOutput] = "叙事",
        [EventType.StateChange]     = "状态变更",
        [EventType.MemoryUpdate]    = "记忆更新",
        [EventType.CharacterUpdate] = "人物更新",
        [EventType.SceneChange]     = "场景切换",
        [EventType.DirectiveChange] = "指令变更",
        [EventType.PhaseTransition] = "Phase 转换"
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is EventType type)
        {
            return DisplayNames.TryGetValue(type, out var name) ?
                       name :
                       type.ToString();
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        EventType.DirectorInput;
}
