using System.Text.RegularExpressions;
using Xunit;

namespace OpenIGI.Core.Tests.Architecture;

/// <summary>
/// Rule 3 of <c>docs/architecture.md</c>: <c>OpenIGI.Presentation</c> and <c>OpenIGI.Host</c> hold no
/// content belonging to one game.
/// </summary>
/// <remarks>
/// <para>
/// A ratchet rather than a gate, because the rule does not hold yet. Phase 0 moved the game out of the
/// executable; it did not finish separating the two titles, and thirteen files still name an asset path,
/// a task type or a weapon id. Those are listed below and are allowed to. Anything <em>else</em> that
/// starts naming one fails, so the debt is bounded at what phase 0 inherited and cannot quietly grow
/// while phase 1 is in progress.
/// </para>
/// <para>
/// The list is also checked for staleness: clean a file up and the test tells you to remove its entry.
/// That is what makes it a ratchet instead of a permanent excuse — the number only goes down, and it
/// reaches zero when the title projects take this content.
/// </para>
/// <para>
/// <c>Presentation/Igi1</c> is exempt wholesale rather than listed file by file. It is I.G.I. 1's
/// renderers parked where they can compile until the host stops constructing each of them by name. The
/// exemption disappears with the folder, and the folder empties one pass at a time — terrain, then the
/// world, then the first-person and projectile passes — each once an interface in
/// <c>OpenIGI.Presentation</c> gave the host something to hold it by and
/// <c>ITitlePresentationProvider</c> gave it something to build it with. 37 files at the start of
/// phase 1, four now.
/// </para>
/// <para>
/// <b>Not everything in there needed a seam.</b> Auditing the folder against the rule's own words —
/// does this name an asset path, model id, task type, weapon, HUD element or mission of one game? —
/// found eight files that were only ever there because phase 0 moved the executable wholesale: the
/// showcase points and their file, the menu camera library and its orbit camera, the sun gizmo, the
/// day-night cycle and the world title cards are all title-agnostic and moved <em>up</em> into
/// <c>OpenIGI.Presentation</c>, and the electric-fence damage rule was not presentation at all and
/// moved to <c>OpenIGI.Game</c> beside the zone table it names. Reclassifying is cheaper than
/// abstracting, and it is the first thing to try on each remaining cluster.
/// </para>
/// <para>
/// <b>How the last of the folder left.</b> The interface for a pass is a <em>census</em> of what the host already uses on
/// that renderer, never an invention. The two large ones were a character system and a first-person player state wearing
/// renderers' names, and an interface over all of either would have been the class copied out with the word
/// <c>interface</c> in front, shaped end to end by one game. They were split first: the player state moved whole into
/// <c>OpenIGI.Game.Igi1</c> as <c>PlayerCamera</c>, and the soldiers' simulation became <c>SoldierSimulation</c> there,
/// leaving a renderer whose 32 public members are all drawing. Its interface, <c>ISoldierPresenter</c>, is declared in
/// that title project beside the simulation it speaks of -- one game's seam, not a shared one -- and the title registers
/// it in its <c>PresenterRegistry</c>, as it does the impact effects and the retail menus. The prompt wording and the
/// cutscene framing were title-agnostic and moved up into <c>OpenIGI.Presentation</c>.
/// </para>
/// </remarks>
public sealed class TitleContentTests
{
    /// <summary>Markers that a file knows which game it is part of.</summary>
    private static readonly Regex TitleMarker = new(
        @"LOCAL:|MISSION:|TASKTYPE_|WEAPON_ID_",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The files that already break rule 3, and are allowed to until phase 1 moves them.</summary>
    private static readonly string[] Baseline =
    [
        "src/Engine/OpenIGI.Host/GameHost.Audio.cs",
        "src/Engine/OpenIGI.Host/GameHost.Menu.cs",
        "src/Engine/OpenIGI.Host/GameHost.Mission.cs",
        "src/Engine/OpenIGI.Host/GameHost.Startup.cs",
        "src/Engine/OpenIGI.Host/Menu/MissionPreview.cs",
        "src/Engine/OpenIGI.Host/Menu/MissionPreviewLoader.cs",
        "src/Game/OpenIGI.Presentation/Rendering/SkyRenderer.cs",
        "src/Game/OpenIGI.Presentation/Rendering/TextureDecodeCache.cs",
    ];

    /// <summary>No file outside the baseline may name content belonging to one game.</summary>
    [Fact]
    public void No_new_title_content_reaches_the_shared_projects()
    {
        List<string> offenders = [.. Scan().Except(Baseline, StringComparer.Ordinal)];

        Assert.True(
            offenders.Count == 0,
            "These files name content belonging to one game, in a project shared by both.\n"
            + "Move the content to the title project, or -- if it is genuinely title-agnostic -- widen\n"
            + "the marker set with a comment saying why:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>A baseline entry that no longer breaks the rule has to be removed, so the list shrinks.</summary>
    [Fact]
    public void The_baseline_holds_no_entries_that_are_already_clean()
    {
        HashSet<string> current = [.. Scan()];
        List<string> stale = [.. Baseline.Where(b => !current.Contains(b))];

        Assert.True(
            stale.Count == 0,
            "These files no longer hold title content, or no longer exist. Delete them from Baseline --\n"
            + "the list is meant to shrink to nothing:\n  "
            + string.Join("\n  ", stale));
    }

    private static IEnumerable<string> Scan()
    {
        foreach (string project in (string[])["OpenIGI.Presentation", "OpenIGI.Host"])
        {
            ProjectInfo info = RepositoryLayout.Projects[project];

            foreach (string path in info.Sources())
            {
                string relative = Path.GetRelativePath(RepositoryLayout.Root, path).Replace('\\', '/');

                // I.G.I. 1's renderers are parked in Presentation/Igi1 until Game.Igi1 exists.
                if (relative.Contains("/Igi1/", StringComparison.Ordinal))
                {
                    continue;
                }

                if (TitleMarker.IsMatch(File.ReadAllText(path)))
                {
                    yield return relative;
                }
            }
        }
    }
}
