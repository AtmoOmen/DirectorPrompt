using System.Text.Json;

namespace DirectorPrompt.Infrastructure;

internal static class JsonHelper
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null
    };

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;

        return JsonSerializer.Deserialize<T>(json, Options);
    }

    public static string[] DeserializeStringArray(string json) =>
        Deserialize<string[]>(json) ?? [];

    public static long[] DeserializeInt64Array(string json) =>
        Deserialize<long[]>(json) ?? [];
}
