using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Lorekeeper;

public enum TerminologyProposalStatus
{
    Pending,
    Accepted,
    Rejected
}

public sealed record TerminologyProposal(
    string SourceTerm,
    string ProposedTranslation,
    int Occurrences,
    double Confidence,
    TerminologyProposalStatus Status);

public sealed class TerminologyProposalStore
{
    private readonly string filePath;
    private readonly ILorekeeperLogger logger;
    private readonly object sync = new();

    private Dictionary<string, TerminologyProposal> proposals =
        new(StringComparer.OrdinalIgnoreCase);

    public TerminologyProposalStore(
        string filePath,
        ILorekeeperLogger logger)
    {
        this.filePath = filePath
            ?? throw new ArgumentNullException(nameof(filePath));

        this.logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        Load();
    }

    public IReadOnlyList<TerminologyProposal> GetPending()
    {
        lock (sync)
        {
            return proposals.Values
                .Where(proposal =>
                    proposal.Status ==
                    TerminologyProposalStatus.Pending)
                .OrderByDescending(proposal =>
                    proposal.Occurrences)
                .ThenByDescending(proposal =>
                    proposal.Confidence)
                .ThenBy(proposal =>
                    proposal.SourceTerm,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public int PendingCount
    {
        get
        {
            lock (sync)
            {
                return proposals.Values.Count(
                    proposal =>
                        proposal.Status ==
                        TerminologyProposalStatus.Pending);
            }
        }
    }

    public void Suggest(
        string sourceTerm,
        string proposedTranslation,
        double confidence)
    {
        if (string.IsNullOrWhiteSpace(sourceTerm)
            || string.IsNullOrWhiteSpace(proposedTranslation))
        {
            return;
        }

        confidence = Math.Clamp(confidence, 0.0, 1.0);

        string key = Normalize(sourceTerm);

        lock (sync)
        {
            if (proposals.TryGetValue(
                    key,
                    out TerminologyProposal? existing))
            {
                if (existing.Status ==
                    TerminologyProposalStatus.Rejected)
                {
                    return;
                }

                int occurrences =
                    existing.Occurrences + 1;

                string translation =
                    confidence >= existing.Confidence
                        ? proposedTranslation.Trim()
                        : existing.ProposedTranslation;

                double bestConfidence =
                    Math.Max(
                        existing.Confidence,
                        confidence);

                proposals[key] = existing with
                {
                    ProposedTranslation = translation,
                    Occurrences = occurrences,
                    Confidence = bestConfidence
                };
            }
            else
            {
                proposals[key] = new TerminologyProposal(
                    sourceTerm.Trim(),
                    proposedTranslation.Trim(),
                    1,
                    confidence,
                    TerminologyProposalStatus.Pending);
            }

            SaveLocked();
        }
    }

    public bool Accept(
        string sourceTerm,
        TerminologyStore terminologyStore,
        string? editedTranslation = null)
    {
        if (terminologyStore is null)
        {
            throw new ArgumentNullException(
                nameof(terminologyStore));
        }

        string key = Normalize(sourceTerm);

        lock (sync)
        {
            if (!proposals.TryGetValue(
                    key,
                    out TerminologyProposal? proposal))
            {
                return false;
            }

            string finalTranslation =
                string.IsNullOrWhiteSpace(editedTranslation)
                    ? proposal.ProposedTranslation
                    : editedTranslation.Trim();

            bool remembered =
                terminologyStore.Remember(
                    proposal.SourceTerm,
                    finalTranslation,
                    inflectForGender: false,
                    feminineForm: null,
                    TerminologySource.Manual,
                    confidence: 1.0);

            proposals[key] = proposal with
            {
                ProposedTranslation = finalTranslation,
                Status = TerminologyProposalStatus.Accepted,
                Confidence = 1.0
            };

            SaveLocked();

            return remembered;
        }
    }

    public bool Reject(string sourceTerm)
    {
        string key = Normalize(sourceTerm);

        lock (sync)
        {
            if (!proposals.TryGetValue(
                    key,
                    out TerminologyProposal? proposal))
            {
                return false;
            }

            proposals[key] = proposal with
            {
                Status = TerminologyProposalStatus.Rejected
            };

            SaveLocked();
            return true;
        }
    }

    public bool Restore(string sourceTerm)
    {
        string key = Normalize(sourceTerm);

        lock (sync)
        {
            if (!proposals.TryGetValue(
                    key,
                    out TerminologyProposal? proposal))
            {
                return false;
            }

            proposals[key] = proposal with
            {
                Status = TerminologyProposalStatus.Pending
            };

            SaveLocked();
            return true;
        }
    }

    private void Load()
    {
        lock (sync)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    proposals =
                        new Dictionary<string, TerminologyProposal>(
                            StringComparer.OrdinalIgnoreCase);

                    return;
                }

                string json =
                    File.ReadAllText(filePath);

                List<TerminologyProposal>? loaded =
                    JsonSerializer.Deserialize<
                        List<TerminologyProposal>>(json);

                proposals =
                    (loaded ?? new List<TerminologyProposal>())
                    .Where(proposal =>
                        !string.IsNullOrWhiteSpace(
                            proposal.SourceTerm))
                    .ToDictionary(
                        proposal =>
                            Normalize(proposal.SourceTerm),
                        proposal => proposal,
                        StringComparer.OrdinalIgnoreCase);

                logger.Information(
                    $"TERMINOLOGY PROPOSALS: Wczytano " +
                    $"{proposals.Count} propozycji.");
            }
            catch (Exception exception)
            {
                proposals =
                    new Dictionary<string, TerminologyProposal>(
                        StringComparer.OrdinalIgnoreCase);

                logger.Error(
                    exception,
                    "TERMINOLOGY PROPOSALS: Nie udało się " +
                    "wczytać pliku propozycji.");
            }
        }
    }

    private void SaveLocked()
    {
        try
        {
            string? directory =
                Path.GetDirectoryName(filePath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            List<TerminologyProposal> snapshot =
                proposals.Values
                    .OrderBy(
                        proposal => proposal.SourceTerm,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();

            string json =
                JsonSerializer.Serialize(
                    snapshot,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

            string temporaryPath =
                filePath + ".tmp";

            File.WriteAllText(
                temporaryPath,
                json);

            File.Move(
                temporaryPath,
                filePath,
                overwrite: true);
        }
        catch (Exception exception)
        {
            logger.Error(
                exception,
                "TERMINOLOGY PROPOSALS: Nie udało się " +
                "zapisać pliku propozycji.");
        }
    }

    private static string Normalize(
        string sourceTerm)
    {
        return sourceTerm.Trim();
    }
}
