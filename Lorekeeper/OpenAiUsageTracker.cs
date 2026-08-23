using System;
using System.Globalization;
using System.IO;

namespace Lorekeeper;

public sealed class OpenAiUsageTracker
{
    private readonly object sync = new();
    private readonly string filePath;
    private readonly ILorekeeperLogger logger;

    private decimal sessionCostUsd;
    private decimal totalCostUsd;

    public OpenAiUsageTracker(
        string configDirectory,
        ILorekeeperLogger logger)
    {
        this.logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        filePath = Path.Combine(
            configDirectory,
            "openai-usage.txt");

        totalCostUsd =
            LoadTotalCost();
    }

    public decimal SessionCostUsd
    {
        get
        {
            lock (sync)
            {
                return sessionCostUsd;
            }
        }
    }

    public decimal TotalCostUsd
    {
        get
        {
            lock (sync)
            {
                return totalCostUsd;
            }
        }
    }

    public void Record(
        decimal costUsd)
    {
        if (costUsd <= 0m)
        {
            return;
        }

        lock (sync)
        {
            sessionCostUsd +=
                costUsd;

            totalCostUsd +=
                costUsd;

            SaveTotalCost();
        }
    }

    private decimal LoadTotalCost()
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return 0m;
            }

            string raw =
                File.ReadAllText(filePath)
                    .Trim();

            return decimal.TryParse(
                raw,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal value)
                    ? Math.Max(
                        0m,
                        value)
                    : 0m;
        }
        catch (Exception exception)
        {
            logger.Error(
                exception,
                "OPENAI USAGE: Nie udało się odczytać łącznego kosztu.");

            return 0m;
        }
    }

    private void SaveTotalCost()
    {
        try
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(filePath)
                ?? ".");

            File.WriteAllText(
                filePath,
                totalCostUsd.ToString(
                    "0.00000000",
                    CultureInfo.InvariantCulture));
        }
        catch (Exception exception)
        {
            logger.Error(
                exception,
                "OPENAI USAGE: Nie udało się zapisać łącznego kosztu.");
        }
    }
}
