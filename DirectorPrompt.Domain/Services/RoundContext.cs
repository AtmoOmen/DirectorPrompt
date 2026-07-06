namespace DirectorPrompt.Domain.Services;

public static class RoundContext
{
    private static readonly AsyncLocal<long?> current = new();

    public static long? Current => current.Value;

    public static IDisposable Enter(long roundID)
    {
        var previous = current.Value;
        current.Value = roundID;

        return new Scope(() => current.Value = previous);
    }

    private sealed class Scope(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
