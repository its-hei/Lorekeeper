using System;
using System.Collections.Generic;

namespace Lorekeeper;

internal static class KnownNpcSexOverrides
{
    // Kuratorowane fallbacki są używane tylko wtedy, gdy Dalamud nie potrafi
    // rozpoznać aktywnego obiektu NPC (np. w części scen/cutscenek).
    // Dane gry wykryte przez NpcSexResolver nadal mają pierwszy priorytet.
    private static readonly IReadOnlyDictionary<string, PlayerSex> Overrides =
        new Dictionary<string, PlayerSex>(StringComparer.OrdinalIgnoreCase)
        {
            ["Visna"] = PlayerSex.Female,
            ["Tataru"] = PlayerSex.Female,
            ["Tataru Taru"] = PlayerSex.Female,
            ["Momodi"] = PlayerSex.Female
        };

    public static bool TryResolve(
        string npcName,
        out PlayerSex sex)
    {
        string name =
            (npcName ?? string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(name)
            && Overrides.TryGetValue(
                name,
                out sex))
        {
            return true;
        }

        sex = PlayerSex.Unknown;
        return false;
    }
}
