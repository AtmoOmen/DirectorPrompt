using System.Text.Json;
using DirectorPrompt.Domain;

namespace DirectorPrompt.Agents;

public static class ToolResult
{
    public static string Error(string message) =>
        JsonSerializer.Serialize(new { error = message }, JsonOptions.Compact);

    public static bool IsError(object? result)
    {
        if (result is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
                return IsError(element.GetString());

            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null;
        }

        if (result is not string text)
            return false;

        try
        {
            using var document = JsonDocument.Parse(text);
            return IsError(document.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string Data<T>(T data) =>
        JsonSerializer.Serialize(data, JsonOptions.Compact);

    public static string Success(string? message = null) =>
        JsonSerializer.Serialize(new { success = true, message }, JsonOptions.Compact);
}
