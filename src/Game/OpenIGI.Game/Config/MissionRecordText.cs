using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using OpenIGI.Game.Scoring;

namespace OpenIGI.Game.Config;

/// <summary>A mission record as the configuration file writes it: its counts, and its figures, each a line of words.</summary>
public static class MissionRecordText
{
    /// <summary>The counts, as written.</summary>
    /// <param name="record">The record.</param>
    /// <returns>The words.</returns>
    public static string Counts(TitleMissionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return string.Join(' ', record.Counts.Select(static n => n.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>The figures, as written: each exactly, so a figure read back is the one kept.</summary>
    /// <param name="record">The record.</param>
    /// <returns>The words.</returns>
    public static string Figures(TitleMissionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return string.Join(' ', record.Figures.Select(static f => f.ToString("R", CultureInfo.InvariantCulture)));
    }

    /// <summary>Reads a record back.</summary>
    /// <param name="counts">The counts' words.</param>
    /// <param name="figures">The figures' words.</param>
    /// <param name="record">Receives the record.</param>
    /// <returns>Whether every word was a number.</returns>
    public static bool TryParse(string? counts, string? figures, [NotNullWhen(true)] out TitleMissionRecord? record)
    {
        record = null;
        string[] countWords = (counts ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] figureWords = (figures ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int[] n = new int[countWords.Length];
        float[] f = new float[figureWords.Length];
        for (int i = 0; i < n.Length; i++)
        {
            if (!int.TryParse(countWords[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out n[i]))
            {
                return false;
            }
        }

        for (int i = 0; i < f.Length; i++)
        {
            if (!float.TryParse(figureWords[i], NumberStyles.Float, CultureInfo.InvariantCulture, out f[i]))
            {
                return false;
            }
        }

        record = new TitleMissionRecord(n, f);
        return true;
    }
}
