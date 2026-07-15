using System.Text.Json;
using DirectorPrompt.Domain;

namespace DirectorPrompt.Agents;

public static class ToolResult
{
    public static string Error(string message) =>
        JsonSerializer.Serialize(new { error = message }, JsonOptions.Compact);

    public static string Data<T>(T data) =>
        JsonSerializer.Serialize(data, JsonOptions.Compact);

    public static string Success(string? message = null) =>
        message is null ?
            JsonSerializer.Serialize(new { success = true },          JsonOptions.Compact) :
            JsonSerializer.Serialize(new { success = true, message }, JsonOptions.Compact);
}
