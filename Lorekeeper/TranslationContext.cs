namespace Lorekeeper;

public enum PlayerSex
{
    Unknown,
    Male,
    Female
}

public sealed record TranslationContext(
    PlayerSex PlayerCharacterSex,
    PlayerSex SpeakerSex = PlayerSex.Unknown)
{
    public static TranslationContext Default { get; } =
        new(
            PlayerSex.Unknown,
            PlayerSex.Unknown);
}
