using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Lorekeeper;

public sealed record CloudTranslationIdentity(
    string LookupKey,
    int ProfileVersion,
    string SourceLanguage,
    string TargetLanguage,
    string SourceText,
    string NpcName,
    string PlayerSex,
    string SpeakerSex,
    string TerminologyFingerprint);

public sealed class CloudTranslationIdentityBuilder
{
    public const int ProfileVersion = 1;

    private const string SourceLanguage = "en";
    private const string TargetLanguage = "pl";

    private readonly TerminologyStore? terminologyStore;

    public CloudTranslationIdentityBuilder(
        TerminologyStore? terminologyStore)
    {
        this.terminologyStore = terminologyStore;
    }

    public CloudTranslationIdentity Create(
        string text,
        string npcName,
        TranslationContext context)
    {
        context ??= TranslationContext.Default;

        string terminologyFingerprint =
            CreateTerminologyFingerprint(text);

        string canonical =
            Part(ProfileVersion.ToString()) +
            Part(SourceLanguage) +
            Part(TargetLanguage) +
            Part(context.PlayerCharacterSex.ToString()) +
            Part(context.SpeakerSex.ToString()) +
            Part(npcName ?? string.Empty) +
            Part(terminologyFingerprint) +
            Part(text ?? string.Empty);

        string lookupKey =
            Convert.ToHexString(
                    SHA256.HashData(
                        Encoding.UTF8.GetBytes(canonical)))
                .ToLowerInvariant();

        return new CloudTranslationIdentity(
            lookupKey,
            ProfileVersion,
            SourceLanguage,
            TargetLanguage,
            text ?? string.Empty,
            npcName ?? string.Empty,
            context.PlayerCharacterSex.ToString(),
            context.SpeakerSex.ToString(),
            terminologyFingerprint);
    }

    private string CreateTerminologyFingerprint(
        string text)
    {
        if (terminologyStore is null)
        {
            return "NO_TERMINOLOGY";
        }

        List<string> parts = new();

        foreach (TerminologyEntry entry in terminologyStore.GetAll())
        {
            if ((text ?? string.Empty).IndexOf(
                    entry.SourceTerm,
                    StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            parts.Add(
                $"{entry.SourceTerm}={entry.PreferredTranslation}|" +
                $"{entry.FeminineForm}|" +
                $"{entry.InflectForGender}");
        }

        if (parts.Count == 0)
        {
            return "NO_MATCH";
        }

        parts.Sort(StringComparer.OrdinalIgnoreCase);

        string source =
            string.Join(";", parts);

        return Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(source)))
            .ToLowerInvariant();
    }

    private static string Part(
        string value)
    {
        value ??= string.Empty;

        return $"{value.Length}:{value}|";
    }
}
