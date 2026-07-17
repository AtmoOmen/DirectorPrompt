namespace DirectorPrompt.MCP;

public interface IDirectorPromptMCPStatus
{
    string Endpoint { get; }

    bool IsAvailable { get; }

    string? ErrorMessage { get; }
}
