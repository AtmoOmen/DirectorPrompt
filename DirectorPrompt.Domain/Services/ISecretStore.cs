namespace DirectorPrompt.Domain.Services;

public interface ISecretStore
{
    string? Get(string key);

    void Set(string key, string value);

    void Remove(string key);
}
