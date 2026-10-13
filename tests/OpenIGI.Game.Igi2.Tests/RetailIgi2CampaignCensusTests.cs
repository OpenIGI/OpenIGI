using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// Every campaign mission measured against everything its scripts need from the engine.
/// </summary>
/// <remarks>
/// <para>
/// The goal is <b>zero gaps</b> in all nineteen missions: no type a mission spawns left as a placeholder,
/// no native, constant or exported variable its scripts or conditions name left unresolved. The ledger
/// beside this file is the list still open, one line per gap with the missions that need it, and the
/// test pins it in both directions — a new gap fails it, and so does a gap closed without being struck
/// off, so the ledger is always the true remaining work. A mission is done when no line names it.
/// </para>
/// <para>
/// When the ledger is out of date the test writes the measured list next to the test assembly as
/// <c>Igi2CampaignGaps.actual.txt</c>, so updating it is a copy rather than a transcription.
/// </para>
/// </remarks>
public sealed class RetailIgi2CampaignCensusTests
{
    private const string LedgerName = "Igi2CampaignGaps.txt";

    /// <summary>The campaign's missions as <c>(location, level)</c>, in the order the game numbers them.</summary>
    /// <remarks>The game's mission id is <c>location * 10 + level</c>: 11–17, 21–26, 31–36.</remarks>
    public static readonly (int Location, int Level)[] Missions =
    [
        (1, 1), (1, 2), (1, 3), (1, 4), (1, 5), (1, 6), (1, 7),
        (2, 1), (2, 2), (2, 3), (2, 4), (2, 5), (2, 6),
        (3, 1), (3, 2), (3, 3), (3, 4), (3, 5), (3, 6),
    ];

    /// <summary>Every exported variable the census takes as retail-unresolved names a task id its level does not have.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Retail_unresolved_variables_name_tasks_their_levels_lack()
    {
        foreach ((string name, string where) in Igi2MissionCensus.RetailUnresolved.Where(static n => n.Key.Contains('.', StringComparison.Ordinal)))
        {
            string[] place = where.Split('/');
            int id = int.Parse(name[(name.IndexOf('_', StringComparison.Ordinal) + 1)..name.IndexOf('.', StringComparison.Ordinal)], System.Globalization.CultureInfo.InvariantCulture);
            Igi2Level level = Igi2Level.Find(place[0], place[1]);
            Assert.DoesNotContain(RetailIgi2Mission1.All(level.Root), t => t.Id == id);
        }
    }

    /// <summary>The level expressions retail cannot parse are, here too, never run.</summary>
    [Fact]
    public void Retail_unparsable_expressions_are_never_run()
    {
        foreach (string expression in Igi2MissionCensus.RetailUnparsable)
        {
            Assert.True(new Flow.Igi2LevelExpression(expression, "Task_1", new OpenIGI.Scripting.Runtime.NativeRegistry()).IsEmpty, expression);
        }
    }

    /// <summary>The measured gaps of every mission are exactly the ledger's.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Campaign_gaps_match_the_ledger()
    {
        // gap -> (missions needing it, first few scripts it was found in)
        SortedDictionary<string, (SortedSet<int> Missions, SortedSet<string> Sources)> measured = new(StringComparer.Ordinal);
        foreach ((int location, int level) in Missions)
        {
            int mission = (location * 10) + level;
            foreach ((string gap, SortedSet<string> sources) in Igi2MissionCensus.Survey(location, level))
            {
                if (!measured.TryGetValue(gap, out (SortedSet<int> Missions, SortedSet<string> Sources) entry))
                {
                    measured[gap] = entry = (new SortedSet<int>(), new SortedSet<string>(StringComparer.Ordinal));
                }

                entry.Missions.Add(mission);
                entry.Sources.UnionWith(sources);
            }
        }

        Dictionary<string, string> ledger = ReadLedger(Path.Combine(AppContext.BaseDirectory, LedgerName));
        List<string> lines = measured
            .Select(static g => (Gap: g.Key, Missions: MissionList(g.Value.Missions), Sources: g.Value.Sources))
            .Select(static g => $"{g.Gap}    # {g.Missions} | {string.Join(", ", g.Sources.Take(2))}{(g.Sources.Count > 2 ? ", …" : string.Empty)}")
            .ToList();

        IEnumerable<string> differences = measured
            .Where(g => !ledger.TryGetValue(g.Key, out string? missions) || missions != MissionList(g.Value.Missions))
            .Select(static g => $"{g.Key}  [{MissionList(g.Value.Missions)}]")
            .Concat(ledger.Keys.Where(k => !measured.ContainsKey(k)).Select(static k => $"{k}  [closed]"))
            .Take(60)
            .ToList();

        if (!differences.Any())
        {
            return;
        }

        string actualPath = Path.Combine(AppContext.BaseDirectory, "Igi2CampaignGaps.actual.txt");
        File.WriteAllLines(actualPath, Header.Concat(lines));
        Assert.Fail(
            $"The ledger is out of date ({measured.Count} measured, {ledger.Count} listed); measured list written to "
            + $"{actualPath}.\nChanged lines (gap [missions now]):\n  {string.Join("\n  ", differences)}");
    }

    private static string MissionList(IEnumerable<int> missions) => string.Join(' ', missions);

    /// <summary>Reads the ledger as gap -> its mission list (the text between <c>#</c> and <c>|</c>).</summary>
    private static Dictionary<string, string> ReadLedger(string path)
    {
        Dictionary<string, string> ledger = new(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int comment = line.IndexOf("    #", StringComparison.Ordinal);
            string gap = (comment >= 0 ? line[..comment] : line).Trim();
            string tail = comment >= 0 ? line[(comment + 5)..] : string.Empty;
            int bar = tail.IndexOf('|', StringComparison.Ordinal);
            ledger[gap] = (bar >= 0 ? tail[..bar] : tail).Trim();
        }

        return ledger;
    }

    private static readonly string[] Header =
    [
        "# I.G.I. 2 campaign: every gap still open between what the nineteen missions' scripts need and what",
        "# the title implements. Pinned by RetailIgi2CampaignCensusTests in both directions -- update it when",
        "# a gap closes. Done is an empty ledger; a mission is done when no line names it.",
        "#",
        "#   type X    a spawned task type with no class (loads as a DeclaredTask placeholder)",
        "#   name X    a native, constant or exported variable a script or VarString expression names",
        "#   survey X  a script that could not be run far enough to read its spawns",
        "#",
        "# After \"    #\": the missions (location*10+level) that need the gap | where it was found.",
        string.Empty,
    ];
}
