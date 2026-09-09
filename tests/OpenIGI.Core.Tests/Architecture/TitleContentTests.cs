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
/// phase 1, 28 now.
/// </para>
/// <para>
/// <b>How the order of the remaining moves is decided, and why two of them are not moving.</b> The
/// interface for a pass is a <em>census</em> of what the host already uses on that renderer, never an
/// invention — the same method that retyped <c>_platform</c> and <c>_window</c> in phase 0. Over what is
/// left, that census says:
/// </para>
/// <list type="bullet">
/// <item><description>showcase, 3 files, 3 members — statics and a record, the cheapest;</description></item>
/// <item><description>effects ~32, HUD ~38, map computer ~39, cameras ~43;</description></item>
/// <item><description><c>SoldierRenderer</c>, <b>68 of its 75 public members</b>;</description></item>
/// <item><description><c>PlayerCamera</c>, <b>105 of its 118</b>.</description></item>
/// </list>
/// <para>
/// The last two do not get interfaces. An interface covering 68 or 105 members is the class copied out
/// with the word <c>interface</c> in front, shaped end to end by one game, which is the failure the seam
/// exists to prevent — they are a character system and a first-person player state wearing renderers'
/// names. They move wholesale when they can, and a shared abstraction is extracted at M2-6 against
/// I.G.I. 2's, which is the same call the plan already makes for <c>Ai/AiSoldier.cs</c>. Only three of
/// the 34 are blocked by anything but the host naming them: those two and the adapter over the first.
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
        "src/OpenIGI.Host/GameHost.Audio.cs",
        "src/OpenIGI.Host/GameHost.Effects.cs",
        "src/OpenIGI.Host/GameHost.Menu.cs",
        "src/OpenIGI.Host/GameHost.Mission.cs",
        "src/OpenIGI.Host/GameHost.cs",
        "src/OpenIGI.Host/Infrastructure/MissionStringResolver.cs",
        "src/OpenIGI.Host/Menu/MissionPreview.cs",
        "src/OpenIGI.Host/Menu/MissionPreviewLoader.cs",
        "src/OpenIGI.Presentation/Rendering/ExplosionEffectRenderer.cs",
        "src/OpenIGI.Presentation/Rendering/ImpactParticleRenderer.cs",
        "src/OpenIGI.Presentation/Rendering/SkyRenderer.cs",
        "src/OpenIGI.Presentation/Rendering/SmokeSpriteRenderer.cs",
        "src/OpenIGI.Presentation/Rendering/TextureDecodeCache.cs",
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
