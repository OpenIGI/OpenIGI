using System.Text.RegularExpressions;
using Xunit;

namespace OpenIGI.Core.Tests.Architecture;

/// <summary>
/// The refactor's ratchets: rules the code breaks today, written down as a baseline that may only shrink.
/// </summary>
/// <remarks>
/// <para>
/// Each rule lists what it finds now and compares that with <c>Architecture/Baselines/&lt;rule&gt;.txt</c>. A finding that is
/// not in the baseline fails the build: nothing new may break the rule. A baseline line that is no longer found fails too, so
/// the file is kept as small as the code is clean and a fixed violation cannot quietly come back.
/// </para>
/// <para>
/// Set <c>OPENIGI_RATCHET_UPDATE=1</c> to rewrite the baselines from the code as it stands, after fixing violations. Never to
/// let a new one in.
/// </para>
/// </remarks>
public sealed class RatchetTests
{
    private const string UpdateVariable = "OPENIGI_RATCHET_UPDATE";

    /// <summary>The abstraction projects' concrete neighbours: a gameplay or engine project naming one is a layering break.</summary>
    private static readonly Dictionary<string, string[]> ForbiddenReferences = new(StringComparer.Ordinal)
    {
        ["OpenIGI.Engine"] = ["OpenIGI.Rendering", "OpenIGI.Audio", "OpenIGI.Platform", "OpenIGI.Presentation"],
        ["OpenIGI.Game"] = ["OpenIGI.Rendering", "OpenIGI.Audio", "OpenIGI.Input", "OpenIGI.Platform", "OpenIGI.Presentation"],
        ["OpenIGI.Game.Igi1"] = ["OpenIGI.Rendering", "OpenIGI.Presentation", "OpenIGI.Audio", "OpenIGI.Platform"],
        ["OpenIGI.Game.Igi2"] = ["OpenIGI.Rendering", "OpenIGI.Presentation", "OpenIGI.Audio", "OpenIGI.Platform"],
        ["OpenIGI.Modding"] = ["OpenIGI.Rendering", "OpenIGI.Presentation", "OpenIGI.Host", "OpenIGI.Game.Igi1", "OpenIGI.Game.Igi2"],
    };

    /// <summary>The helpers that were written again in file after file. One shared copy each is the goal.</summary>
    private static readonly string[] CopiedHelpers =
    [
        "Cross", "Dot", "Normalize", "Normalise", "Length", "Distance", "DistanceSquared", "Lerp",
        "Walk", "Descendants", "WalkTasks", "AllTasks",
    ];

    /// <summary>Gameplay and engine projects reference no concrete neighbour beyond what the baseline still allows.</summary>
    [Fact]
    public void Layering()
    {
        List<string> findings = [];
        foreach ((string project, string[] forbidden) in ForbiddenReferences)
        {
            if (RepositoryLayout.Projects.TryGetValue(project, out ProjectInfo? info))
            {
                findings.AddRange(info.References.Intersect(forbidden, StringComparer.Ordinal).Select(r => $"{project} -> {r}"));
            }
        }

        Check("layering", findings);
    }

    /// <summary>Title gameplay types are owned by their title package, not reached through a static <c>Instance</c>.</summary>
    [Fact]
    public void Static_instances()
    {
        Regex instance = new(@"public\s+static\s+(?:readonly\s+)?[\w.<>]+\s+Instance\b", RegexOptions.CultureInvariant);
        Check("static-instances", Scan(
            static p => p.Name.StartsWith("OpenIGI.Game", StringComparison.Ordinal) || p.Name.StartsWith("OpenIGI.Presentation", StringComparison.Ordinal),
            (file, text) => instance.IsMatch(text) ? [file] : []));
    }

    /// <summary>The host names no title's presentation namespace.</summary>
    [Fact]
    public void Host_title_presentation()
    {
        Check("host-title-presentation", Scan(
            static p => p.Name == "OpenIGI.Host",
            (file, text) => text.Contains("OpenIGI.Presentation.Igi1", StringComparison.Ordinal)
                || text.Contains("OpenIGI.Presentation.Igi2", StringComparison.Ordinal) ? [file] : []));
    }

    /// <summary>The helpers each file writes for itself: a private or internal static method under one of the shared names.</summary>
    [Fact]
    public void Copied_helpers()
    {
        Regex helper = new(
            @"(?:private|internal)\s+static\s+[\w.<>\[\],? ]+?\s+(" + string.Join('|', CopiedHelpers) + @")\s*(?:<[^>]*>)?\s*\(",
            RegexOptions.CultureInvariant);
        Check("copied-helpers", Scan(
            static p => p.Area == "src",
            (file, text) => helper.Matches(text).Select(m => $"{file}: {m.Groups[1].Value}").Distinct(StringComparer.Ordinal)));
    }

    private static List<string> Scan(Func<ProjectInfo, bool> projects, Func<string, string, IEnumerable<string>> rule)
    {
        List<string> findings = [];
        foreach (ProjectInfo project in RepositoryLayout.Projects.Values.Where(projects))
        {
            foreach (string path in project.Sources())
            {
                if (path.Contains($"{Path.DirectorySeparatorChar}Generated{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                string relative = Path.GetRelativePath(RepositoryLayout.Root, path).Replace('\\', '/');
                findings.AddRange(rule(relative, File.ReadAllText(path)));
            }
        }

        return findings;
    }

    private static void Check(string rule, IEnumerable<string> found)
    {
        string path = Path.Combine(RepositoryLayout.Root, "tests", "OpenIGI.Core.Tests", "Architecture", "Baselines", rule + ".txt");
        SortedSet<string> current = new(found, StringComparer.Ordinal);

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, current);
            return;
        }

        SortedSet<string> baseline = File.Exists(path)
            ? new(File.ReadAllLines(path).Where(static l => l.Length > 0 && !l.StartsWith('#')), StringComparer.Ordinal)
            : [];

        string[] added = [.. current.Except(baseline, StringComparer.Ordinal)];
        string[] fixedOnes = [.. baseline.Except(current, StringComparer.Ordinal)];

        Assert.True(
            added.Length == 0,
            $"Rule '{rule}' has new violations; fix them rather than baselining them:\n  " + string.Join("\n  ", added));
        Assert.True(
            fixedOnes.Length == 0,
            $"Rule '{rule}' no longer finds these; remove them from {Path.GetFileName(path)} (or run with {UpdateVariable}=1):\n  "
            + string.Join("\n  ", fixedOnes));
    }
}
