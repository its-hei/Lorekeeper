using System;
using Dalamud.Game.Player;
using Dalamud.Plugin.Services;

namespace Lorekeeper.Dalamud;

internal sealed class PlayerTranslationContextProvider
{
    private readonly IPlayerState playerState;

    public PlayerTranslationContextProvider(IPlayerState playerState)
    {
        this.playerState = playerState
            ?? throw new ArgumentNullException(nameof(playerState));
    }

    public TranslationContext GetCurrent()
    {
        if (!playerState.IsLoaded)
        {
            return TranslationContext.Default;
        }

        PlayerSex playerSex = playerState.Sex switch
        {
            Sex.Male => PlayerSex.Male,
            Sex.Female => PlayerSex.Female,
            _ => PlayerSex.Unknown
        };

        return new TranslationContext(playerSex);
    }
}
