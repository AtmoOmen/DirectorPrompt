using System.Text.Json;
using DirectorPrompt.Domain;

namespace DirectorPrompt.Agents;

public static class ToolResult
{
    private sealed record ToolErrorPayload(string Error);

    private sealed record ToolSuccessPayload
    (
        bool             Success,
        string?          Message = null
    );

    public static string Error(string message) =>
        JsonSerializer.Serialize(new ToolErrorPayload(message), JsonOptions.Compact);

    public static string Data<T>(T data) =>
        JsonSerializer.Serialize(data, JsonOptions.Compact);

    public static string Success(string? message = null) =>
        JsonSerializer.Serialize(new ToolSuccessPayload(true, message), JsonOptions.Compact);
}
