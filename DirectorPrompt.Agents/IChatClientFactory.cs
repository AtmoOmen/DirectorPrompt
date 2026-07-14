using DirectorPrompt.Domain.Configurations;
using Microsoft.Extensions.AI;

namespace DirectorPrompt.Agents;

public interface IChatClientFactory
{
    IChatClient Create(ProviderConfig provider, ModelConfig model);

    void Reset();
}
