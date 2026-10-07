using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// Mission 1 (<c>location1/level1</c>) measured against everything its scripts need from the engine.
/// </summary>
/// <remarks>
/// <para>
/// The goal for the mission is <b>zero gaps</b>: no type it spawns left as a placeholder, no native,
/// constant or exported variable its scripts or conditions name left unresolved. The ledger beside this
/// file is the list still open, and the test pins it in both directions — a new gap fails it, and so
/// does a gap closed without being struck off, so the ledger is always the true remaining work.
/// </para>
/// <para>
/// When the ledger is out of date the test writes the measured list next to the test assembly as
/// <c>Igi2Mission1Gaps.actual.txt</c>, so updating it is a copy rather than a transcription.
/// </para>
/// </remarks>
public sealed class RetailIgi2Mission1CensusTests
{
    private const string LedgerName = "Igi2Mission1Gaps.txt";

    /// <summary>The measured gaps are exactly the ledger's.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Mission_1_gaps_match_the_ledger()
    {
        SortedDictionary<string, SortedSet<string>> gaps = Igi2MissionCensus.Survey(location: 1, level: 1);

        string ledgerPath = Path.Combine(AppContext.BaseDirectory, LedgerName);
        SortedSet<string> ledger = new(
            File.ReadAllLines(ledgerPath)
                .Select(static l => (l.IndexOf("    #", StringComparison.Ordinal) is int c and >= 0 ? l[..c] : l).Trim())
                .Where(static l => l.Length > 0 && !l.StartsWith('#')),
            StringComparer.Ordinal);

        SortedSet<string> measured = new(gaps.Keys, StringComparer.Ordinal);
        if (measured.SetEquals(ledger))
        {
            return;
        }

        string actualPath = Path.Combine(AppContext.BaseDirectory, "Igi2Mission1Gaps.actual.txt");
        File.WriteAllLines(
            actualPath,
            gaps.Select(static g => $"{g.Key}    # {string.Join(", ", g.Value.Take(3))}{(g.Value.Count > 3 ? ", …" : string.Empty)}"));

        IEnumerable<string> opened = measured.Except(ledger).Take(40);
        IEnumerable<string> closed = ledger.Except(measured).Take(40);
        Assert.Fail(
            $"The ledger is out of date ({measured.Count} measured, {ledger.Count} listed); measured list written to "
            + $"{actualPath}.\nNew gaps:\n  {string.Join("\n  ", opened)}\nClosed but still listed:\n  {string.Join("\n  ", closed)}");
    }
}
