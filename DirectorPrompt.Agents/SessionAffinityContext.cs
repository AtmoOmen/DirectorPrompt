namespace DirectorPrompt.Agents;

public static class SessionAffinityContext
{
    private static readonly AsyncLocal<long?> SessionIDHolder = new();

    public static long? CurrentSessionID => SessionIDHolder.Value;

    public static IDisposable Push(long sessionID)
    {
        var previous = SessionIDHolder.Value;

        SessionIDHolder.Value = sessionID;

        return new Scope(previous);
    }

    private sealed class Scope(long? previous) : IDisposable
    {
        public void Dispose() =>
            SessionIDHolder.Value = previous;
    }
}
