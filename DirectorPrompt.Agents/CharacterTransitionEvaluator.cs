using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using Serilog;

namespace DirectorPrompt.Agents;

public sealed class CharacterTransitionEvaluator
(
    ICharacterRepository characterRepository,
    ISceneRepository     sceneRepository
) : ITransitionSource
{
    public string SourceName => "Character";

    public EventType EventType => EventType.CharacterTransition;

    public async Task<TransitionResult> EvaluateAsync
    (
        long                   projectID,
        long                   sessionID,
        IReadOnlyList<string>? previousKeys,
        CancellationToken      cancellationToken = default
    )
    {
        var activeScene = await sceneRepository.GetActiveSceneAsync(sessionID, cancellationToken);

        if (activeScene is null)
        {
            return new TransitionResult
            {
                EnterDirectives = [],
                ExitDirectives  = [],
                ActiveKeys      = []
            };
        }

        var sceneCharacters = await characterRepository.GetBySceneAsync(activeScene.ID, cancellationToken);

        var activeCharacters = sceneCharacters
                               .Where(c => c.Status == CharacterStatus.Active)
                               .ToList();

        var currentKeys = activeCharacters
                          .Select(c => c.ID.ToString())
                          .ToList();

        var currentSet = new HashSet<string>(currentKeys);

        var previousSet = previousKeys is not null ?
                              new HashSet<string>(previousKeys) :
                              [];

        var enterDirectives = activeCharacters
                              .Where(c => !previousSet.Contains(c.ID.ToString()))
                              .SelectMany(c => c.EnterDirectives)
                              .ToList();

        var exitDirectives = previousKeys is not null ?
                                 await GetExitDirectivesAsync
                                 (
                                     sessionID,
                                     activeScene.ID,
                                     previousSet,
                                     currentSet,
                                     cancellationToken
                                 ) :
                                 [];

        if (enterDirectives.Count > 0 || exitDirectives.Count > 0)
        {
            Log.Information
            (
                "Character 转换: 进入指令数={EnterCount}, 退出指令数={ExitCount}, 在场人物数={CharacterCount}",
                enterDirectives.Count,
                exitDirectives.Count,
                activeCharacters.Count
            );
        }

        return new TransitionResult
        {
            EnterDirectives = enterDirectives,
            ExitDirectives  = exitDirectives,
            ActiveKeys      = currentKeys
        };
    }

    private async Task<List<DirectiveConfig>> GetExitDirectivesAsync
    (
        long              sessionID,
        long              sceneID,
        HashSet<string>   previousSet,
        HashSet<string>   currentSet,
        CancellationToken cancellationToken
    )
    {
        var exitedIDs = previousSet
                        .Except(currentSet)
                        .Select
                        (s => long.TryParse(s, out var id) ?
                                  id :
                                  0
                        )
                        .Where(id => id > 0)
                        .ToList();

        if (exitedIDs.Count == 0)
            return [];

        var allSessionCharacters = await characterRepository.GetBySessionAsync(sessionID, cancellationToken);

        return allSessionCharacters
               .Where(c => exitedIDs.Contains(c.ID))
               .SelectMany(c => c.ExitDirectives)
               .ToList();
    }
}
