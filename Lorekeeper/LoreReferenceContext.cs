using System;

namespace Lorekeeper;

public sealed record LoreReferenceInfo(
    string PromptContext,
    string Fingerprint)
{
    public static LoreReferenceInfo None { get; } =
        new(
            string.Empty,
            string.Empty);

    public bool HasContext =>
        !string.IsNullOrWhiteSpace(PromptContext);
}

public static class LoreReferenceContext
{
    public static LoreReferenceInfo Resolve(
        string npcName,
        string sourceText)
    {
        npcName ??= string.Empty;
        sourceText ??= string.Empty;

        if (npcName.Contains(
                "Hildibrand",
                StringComparison.OrdinalIgnoreCase)
            && sourceText.Contains(
                "assistant",
                StringComparison.OrdinalIgnoreCase))
        {
            return new LoreReferenceInfo(
                "Kontekst fabularny dotyczący osoby trzeciej: " +
                "gdy Hildibrand mówi o swojej asystentce / 'my faithful assistant', " +
                "chodzi o Nashu Mhakaracca. Nashu jest kobietą. " +
                "W polskim tłumaczeniu stosuj wobec tej osoby naturalne formy żeńskie, " +
                "np. 'z moją wierną asystentką'. " +
                "Ta informacja służy wyłącznie do rozstrzygnięcia referencji i rodzaju " +
                "gramatycznego - nie zmieniaj przez nią pozostałego znaczenia kwestii.\n",
                "hildibrand-nashu-assistant-v1");
        }

        return LoreReferenceInfo.None;
    }
}
