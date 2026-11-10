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

    /// <summary>What a gameplay or engine project may not name: the picture, the window and the presentation above it.</summary>
    /// <remarks>
    /// <c>OpenIGI.Audio</c> and <c>OpenIGI.Input</c> are the device-neutral layers (their backends are <c>.OpenAL</c>,
    /// <c>.Web</c> and <c>.Desktop</c>): the engine's sound system stands on the one, and the game reads its keys from the
    /// other, as each title's gameplay already may. The game reaches sound through the engine's sound system.
    /// </remarks>
    private static readonly Dictionary<string, string[]> ForbiddenReferences = new(StringComparer.Ordinal)
    {
        ["OpenIGI.Engine"] = ["OpenIGI.Rendering", "OpenIGI.Platform", "OpenIGI.Presentation"],
        ["OpenIGI.Game"] = ["OpenIGI.Rendering", "OpenIGI.Audio", "OpenIGI.Platform", "OpenIGI.Presentation"],
        ["OpenIGI.Game.Igi1"] = ["OpenIGI.Rendering", "OpenIGI.Presentation", "OpenIGI.Audio", "OpenIGI.Platform"],
        ["OpenIGI.Game.Igi2"] = ["OpenIGI.Rendering", "OpenIGI.Presentation", "OpenIGI.Audio", "OpenIGI.Platform"],
        ["OpenIGI.Modding"] = ["OpenIGI.Rendering", "OpenIGI.Presentation", "OpenIGI.Host", "OpenIGI.Game.Igi1", "OpenIGI.Game.Igi2"],
        ["OpenIGI.Editor.Core"] = ["OpenIGI.Rendering", "OpenIGI.Presentation", "OpenIGI.Host", "OpenIGI.Platform", "OpenIGI.Game.Igi1", "OpenIGI.Game.Igi2", "OpenIGI.UI"],
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

    /// <summary>A task runs itself: nothing binds work to another task's node in the level's run.</summary>
    /// <remarks>
    /// The original gives every type its own run handler. Work bound to a node from outside is how a registry stands in for a
    /// task class that was never written; each file here is one to replace with the class.
    /// </remarks>
    [Fact]
    public void Level_run_binds()
    {
        Regex bind = new(@"\b_?(?:walker|walk|level)\.Bind\(", RegexOptions.CultureInvariant);
        Check("level-run-binds", Scan(IsGameplay, (file, text) => bind.IsMatch(text) ? [file] : []));
    }

    /// <summary>A task is told apart by its class, not by the spelling of its type's name.</summary>
    [Fact]
    public void Task_type_name_tests()
    {
        Regex test = new(
            @"Type\.Name\s+(?:is|==|!=|switch)\b|switch\s*\(\s*[\w.]*Type\.Name\s*\)|Type\.Name\.(?:Equals|StartsWith)\(",
            RegexOptions.CultureInvariant);
        Check("task-type-name-tests", Scan(IsGameplay, (file, text) => test.IsMatch(text) ? [file] : []));
    }

    /// <summary>Gameplay reads a level's task through its class, not through the placeholder a type without one is given.</summary>
    [Fact]
    public void Declared_task_readers()
    {
        Regex declared = new(@"\bDeclaredTask\b", RegexOptions.CultureInvariant);
        Check("declared-task-readers", Scan(IsGameplay, (file, text) => declared.IsMatch(text) ? [file] : []));
    }

    // The projects that hold the games and what shows them; the engine, which defines the task model, is not one.
    private static bool IsGameplay(ProjectInfo project) =>
        project.Name.StartsWith("OpenIGI.Game", StringComparison.Ordinal)
        || project.Name.StartsWith("OpenIGI.Presentation", StringComparison.Ordinal)
        || project.Name == "OpenIGI.Host";

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
