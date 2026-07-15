using System.Text.Json;
using DirectorPrompt.Domain;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Agents.Config;

public static class EventDataSerializer
{
    public static IReadOnlyList<DirectiveItem> ParseDirectives(string jsonData)
    {
        var items = JsonSerializer.Deserialize<List<DirectiveEventData>>(jsonData, JsonOptions.Compact);

        if (items is null || items.Count == 0)
            return [];

        var result = new List<DirectiveItem>();
        var order  = 1;

        foreach (var item in items)
        {
            if (item.IsSystem)
                continue;

            result.Add(new DirectiveItem(ParseDirectiveType(item.Type), item.Content, order++));
        }

        return result;
    }

    public static List<(DirectiveType Type, string Content)> ParseDirectiveBlocks(string json)
    {
        try
        {
            var items = JsonSerializer.Deserialize<List<DirectiveEventData>>(json, JsonOptions.Compact);

            if (items is null || items.Count == 0)
                return [(DirectiveType.Plot, json)];

            var result = new List<(DirectiveType Type, string Content)>();

            foreach (var item in items)
            {
                if (item.IsSystem)
                    continue;

                result.Add((ParseDirectiveType(item.Type), item.Content));
            }

            return result;
        }
        catch
        {
            return [(DirectiveType.Plot, json)];
        }
    }

    private static DirectiveType ParseDirectiveType(string typeStr) =>
        typeStr switch
        {
            "Tone"                => DirectiveType.Tone,
            "TemporaryConstraint" => DirectiveType.TemporaryConstraint,
            "SceneChange"         => DirectiveType.SceneChange,
            _                     => DirectiveType.Plot
        };
}
