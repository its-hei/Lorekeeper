using System;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;

namespace Lorekeeper.Dalamud;

internal sealed record NpcResolvedIdentity(
    string Name,
    uint BaseId,
    ulong GameObjectId,
    PlayerSex Sex);

internal sealed class NpcSexResolver
{
    private readonly IObjectTable objectTable;
    private readonly IPluginLog log;

    public NpcSexResolver(
        IObjectTable objectTable,
        IPluginLog log)
    {
        this.objectTable = objectTable
            ?? throw new ArgumentNullException(nameof(objectTable));

        this.log = log
            ?? throw new ArgumentNullException(nameof(log));
    }

    public bool TryResolve(
        string npcName,
        out PlayerSex sex)
    {
        if (TryResolve(
                npcName,
                out NpcResolvedIdentity identity))
        {
            sex = identity.Sex;
            return true;
        }

        sex = PlayerSex.Unknown;
        return false;
    }

    public bool TryResolve(
        string npcName,
        out NpcResolvedIdentity identity)
    {
        identity = null!;

        if (string.IsNullOrWhiteSpace(npcName))
        {
            return false;
        }

        string expectedName = npcName.Trim();

        foreach (IGameObject gameObject in objectTable)
        {
            string objectName =
                gameObject.Name.TextValue.Trim();

            if (!string.Equals(
                    objectName,
                    expectedName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            log.Information(
                $"KNOWLEDGE MATCH: {npcName} -> " +
                $"{gameObject.GetType().Name}, " +
                $"BaseId={gameObject.BaseId}, " +
                $"GameObjectId={gameObject.GameObjectId}");

            if (gameObject is not ICharacter character)
            {
                log.Information(
                    $"KNOWLEDGE: Znaleziono obiekt {npcName}, " +
                    $"ale nie jest ICharacter.");

                continue;
            }

            byte rawSex = character.CustomizeData.Sex;

            PlayerSex sex = rawSex switch
            {
                0 => PlayerSex.Male,
                1 => PlayerSex.Female,
                _ => PlayerSex.Unknown
            };

            if (sex == PlayerSex.Unknown)
            {
                log.Information(
                    $"KNOWLEDGE: NPC {npcName} ma nieznaną " +
                    $"wartość Sex={rawSex}.");

                return false;
            }

            identity = new NpcResolvedIdentity(
                objectName,
                gameObject.BaseId,
                gameObject.GameObjectId,
                sex);

            log.Information(
                $"KNOWLEDGE: Rozpoznano NPC " +
                $"{identity.Name}: " +
                $"BaseId={identity.BaseId}, " +
                $"Sex={identity.Sex}.");

            return true;
        }

        log.Information(
            $"KNOWLEDGE: Nie znaleziono żadnego obiektu " +
            $"o nazwie: {npcName}.");

        return false;
    }
}
