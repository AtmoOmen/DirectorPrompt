using DirectorPrompt.Domain.Services;

namespace DirectorPrompt.Infrastructure.Security;

public sealed class SecretStore : ISecretStore
{
    private readonly ISecretStore store;

    public SecretStore()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
        {
            store = new WindowsCredentialStore();
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            store = new LinuxSecretService();
            return;
        }

        throw new PlatformNotSupportedException("当前操作系统不支持安全凭据存储");
    }

    public string? Get(string key) =>
        store.Get(key);

    public void Set(string key, string value) =>
        store.Set(key, value);

    public void Remove(string key) =>
        store.Remove(key);
}
