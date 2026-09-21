using OpenIGI.Engine.Level;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Igi1;
using OpenIGI.Game.Igi2;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>The two titles' data layouts.</summary>
public sealed class TitleDataLayoutTests
{
    /// <summary>I.G.I. 1's layout produces exactly the strings its classes used to hold as constants.</summary>
    /// <remarks>
    /// The guard against the seam changing behaviour while it was being introduced. Every value here was
    /// a <c>const</c> on the class that read it; moving it behind an interface has to leave it identical.
    /// </remarks>
    [Fact]
    public void The_igi1_layout_is_the_constants_it_replaced()
    {
        Igi1DataLayout layout = Igi1DataLayout.Instance;

        Assert.Equal(MissionRegistry.MissionRoot, layout.MissionRoot);
        Assert.Equal(MissionRegistry.MissionScriptName, layout.MissionScriptName);
        Assert.Equal(MissionRegistry.FirstMissionId, layout.FirstMissionId);
        Assert.Equal(LevelLoader.ObjectsScriptName, layout.LevelScriptName);
        Assert.Equal(MissionLoader.CommonSearchDirectory, layout.CommonSearchDirectory);

        Assert.Equal(
            [MissionLoader.CommonSpritesArchive, MissionLoader.CommonTexturesArchive],
            layout.SharedArchives);

        Assert.Equal(
            [MissionLoader.CommonSoundDirectory, MissionLoader.MissionSoundDirectory],
            layout.SoundDirectories);

        Assert.Equal(EngineTaskTypes.StructuralNames, layout.StructuralTaskTypes);
    }

    /// <summary>The two titles differ in exactly one structural task type, and it is <c>Container</c>.</summary>
    /// <remarks>
    /// Measured by running every level of both games through the task census: <c>Container</c> is the
    /// only name I.G.I. 2's editor declares that I.G.I. 1's leaves structural. Pinning it here means a
    /// later change to either set has to be deliberate.
    /// </remarks>
    [Fact]
    public void The_titles_differ_by_one_structural_type()
    {
        IReadOnlyList<string> igi1 = Igi1DataLayout.Instance.StructuralTaskTypes;
        IReadOnlyList<string> igi2 = Igi2DataLayout.Instance.StructuralTaskTypes;

        Assert.Equal([EngineTaskTypes.Container], igi1.Except(igi2, StringComparer.Ordinal));
        Assert.Empty(igi2.Except(igi1, StringComparer.Ordinal));
    }

    /// <summary>Both titles use the same alias vocabulary, which is why the file system needs no change.</summary>
    [Fact]
    public void Both_titles_bind_the_same_kinds_of_alias()
    {
        IEnumerable<string> igi2Aliases = Igi2DataLayout.Instance.StartupAliases.Select(static a => a.Key);
        IEnumerable<string> igi1Aliases = Igi1DataLayout.Instance.StartupAliases.Select(static a => a.Key);

        Assert.Empty(igi2Aliases.Except(igi1Aliases, StringComparer.Ordinal));
    }

    /// <summary>I.G.I. 2's mission layout matches what a retail installation actually holds.</summary>
    [Fact]
    public void The_igi2_layout_names_the_paths_the_game_ships()
    {
        Igi2DataLayout layout = Igi2DataLayout.Instance;

        Assert.Equal("LOCAL:missions", layout.MissionRoot);
        Assert.Equal("mission.qvm", layout.MissionScriptName);
        Assert.Equal("objects.qvm", layout.LevelScriptName);
    }
}
