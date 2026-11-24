using System.Reflection;
using System.Text.RegularExpressions;
using OpenIGI.Audio;
using OpenIGI.Core.Configuration;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;
using OpenIGI.Game.Player.Aim;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Every console variable the simulation reads is flagged <see cref="CVarOptions.Simulation"/>, unless it is named here as
/// changing nothing the level does: a replay records exactly the flagged ones, so one read and not flagged would let the same
/// inputs play differently under another player's settings.
/// </summary>
/// <remarks>Checked by reading the simulation assemblies' sources, because the rule is about reads nobody has written yet.</remarks>
public sealed partial class SimulationCVarTests
{
    // The containers whose variables the simulation may read.
    private static readonly Type[] Containers =
    [
        typeof(GameplayCVars), typeof(ModernAimCVars), typeof(AdsTuningCVars), typeof(PlayerCheatCVars), typeof(CoopCVars),
        typeof(NetCVars), typeof(LanguageCVars), typeof(AudioCVars), typeof(PlayerControlCVars),
    ];

    // Read by the simulation, and changing nothing it does: each with the reason.
    private static readonly Dictionary<string, string> Unaffecting = new(StringComparer.Ordinal)
    {
        ["AudioCVars.Preset"] = "names which file a death cry plays; the draw that picks it is the same either way",
        ["GameplayCVars.Igi1Saves"] = "offers the save menus; a recorded run that loads a save is no longer ranked anyway",
        ["GameplayCVars.ImpactSounds"] = "only whether a round's landing is played; its picks are drawn either way",
        ["NetCVars.ShotLog"] = "logs the shots a server resolved",
        ["PlayerCheatCVars.CheatsEnabled"] = "only lets the cheats be set; each cheat is flagged itself",
        ["PlayerCheatCVars.UnlockAllLevels"] = "opens the campaign's missions in the menu",
    };

    private static readonly string[] Simulation =
    [
        Path.Combine("src", "Engine", "OpenIGI.Engine"),
        Path.Combine("src", "Game", "OpenIGI.Game"),
        Path.Combine("src", "Game", "OpenIGI.Game.Igi1"),
        Path.Combine("src", "Game", "OpenIGI.Game.Igi2"),
    ];

    [Fact]
    public void Every_variable_the_simulation_reads_is_flagged()
    {
        Dictionary<string, ICVar> variables = [];
        foreach (Type container in Containers)
        {
            foreach (FieldInfo field in container.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is ICVar variable)
                {
                    variables[$"{container.Name}.{field.Name}"] = variable;
                }
            }
        }

        SortedSet<string> offenders = new(StringComparer.Ordinal);
        Regex read = Read();
        foreach (string assembly in Simulation)
        {
            foreach (string file in Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), assembly), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (string line in File.ReadLines(file))
                {
                    string code = line.TrimStart();
                    if (code.StartsWith("//", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (System.Text.RegularExpressions.Match match in read.Matches(code))
                    {
                        string name = match.Value;
                        if (variables.TryGetValue(name, out ICVar? variable)
                            && (variable.Flags & CVarOptions.Simulation) == 0
                            && !Unaffecting.ContainsKey(name))
                        {
                            offenders.Add($"{name} ({variable.Name}), read in {Path.GetFileName(file)}");
                        }
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "The simulation reads console variables a replay would not record; flag them CVarOptions.Simulation, or name them in "
            + "Unaffecting with the reason they change nothing it does:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void Every_unaffecting_name_is_a_variable_that_is_not_flagged()
    {
        foreach (string name in Unaffecting.Keys)
        {
            string[] parts = name.Split('.');
            Type container = Containers.Single(type => type.Name == parts[0]);
            ICVar? variable = container.GetField(parts[1], BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as ICVar;
            Assert.True(variable is not null, $"{name} names no console variable.");
            Assert.True((variable.Flags & CVarOptions.Simulation) == 0, $"{name} is flagged as changing the simulation and named as not.");
        }
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenIGI.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("The tests run outside the repository.");
    }

    [GeneratedRegex(@"\b(GameplayCVars|ModernAimCVars|AdsTuningCVars|PlayerCheatCVars|CoopCVars|NetCVars|LanguageCVars|AudioCVars|PlayerControlCVars)\.[A-Z]\w*")]
    private static partial Regex Read();
}
