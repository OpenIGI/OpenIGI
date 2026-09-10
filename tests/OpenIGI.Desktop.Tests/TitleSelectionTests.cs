using OpenIGI.Core.Titles;
using OpenIGI.Desktop;
using OpenIGI.Host.Configuration;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>Choosing which game to run, and where that game is installed.</summary>
/// <remarks>
/// <para>
/// Two questions that look like one. A machine may well have both games installed, so the answer to
/// "where is the game" depends on the answer to "which game" — and the second is settled by
/// <c>-title</c>, by <c>OPENIGI_TITLE</c>, or by a probe, none of which the command-line parser knows
/// about.
/// </para>
/// <para>
/// That is why <see cref="HostOptions.GamePath"/> is what <c>-game</c> said and nothing else. The
/// parser used to seed it from <c>IGI_GAME_PATH</c>, which meant a <c>-title igi2</c> run took the
/// first game's path and reported the sequel missing from an installation of Project I.G.I. — a
/// confusing failure, because both halves of it are true.
/// </para>
/// </remarks>
public sealed class TitleSelectionTests
{
    /// <summary>The parsed path is what the command line said, and nothing is filled in behind it.</summary>
    [Fact]
    public void The_parsed_game_path_is_only_what_the_command_line_said()
    {
        Assert.Null(HostOptions.Parse([]).GamePath);
        Assert.Equal("D:/games/igi", HostOptions.Parse(["-game", "D:/games/igi"]).GamePath);
    }

    /// <summary>Each title names its own path variable, and the two are different variables.</summary>
    /// <remarks>
    /// Distinct is the assertion. One variable cannot say where two installations are, and pointing a
    /// shared one at the wrong game is the mystery this arrangement exists to prevent.
    /// </remarks>
    [Fact]
    public void Each_title_names_its_own_path_variable()
    {
        string first = DesktopTitleCatalog.GamePathVariableFor(GameTitle.Igi1);
        string second = DesktopTitleCatalog.GamePathVariableFor(GameTitle.Igi2);

        Assert.Equal(HostOptions.GamePathVariable, first);
        Assert.Equal("IGI2_GAME_PATH", second);
        Assert.NotEqual(first, second);
    }

    /// <summary>A title id resolves to a runtime, and an unknown one resolves to nothing.</summary>
    /// <remarks>
    /// The negative half matters more than the positive: an id nobody recognises has to stop the run,
    /// because a run that asked for one game and was quietly given another is worse than one that
    /// refuses.
    /// </remarks>
    [Theory]
    [InlineData("igi1")]
    [InlineData("IGI2")]
    [InlineData("  igi2  ")]
    public void A_known_title_id_resolves(string id)
    {
        Assert.True(DesktopTitleCatalog.TryResolve(id, out Engine.Titles.IGameTitleRuntime? runtime));
        Assert.Equal(id.Trim(), runtime!.Title.Id, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>An id this build does not run resolves to nothing rather than to the default.</summary>
    [Theory]
    [InlineData("igi3")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unknown_title_id_resolves_to_nothing(string? id)
    {
        Assert.False(DesktopTitleCatalog.TryResolve(id, out Engine.Titles.IGameTitleRuntime? runtime));
        Assert.Null(runtime);

        // And the message the caller prints names what it would have accepted.
        Assert.Contains("igi1", DesktopTitleCatalog.KnownTitleIds, StringComparison.Ordinal);
        Assert.Contains("igi2", DesktopTitleCatalog.KnownTitleIds, StringComparison.Ordinal);
    }

    /// <summary>Every title this build lists has the four things a host needs from it.</summary>
    /// <remarks>
    /// A conformance check over the catalogue rather than over one title, so adding a third game fails
    /// here rather than at the first frame. The four lookups exist separately because the reference
    /// graph forbids joining them, which makes forgetting one entirely possible.
    /// </remarks>
    [Fact]
    public void Every_listed_title_has_a_runtime_a_walk_a_catalogue_and_a_presentation()
    {
        Assert.NotEmpty(DesktopTitleCatalog.All);

        foreach (Engine.Titles.IGameTitleRuntime runtime in DesktopTitleCatalog.All)
        {
            GameTitle title = runtime.Title;

            Assert.Same(runtime, DesktopTitleCatalog.For(title));
            Assert.NotNull(DesktopTitleCatalog.WorldBuilderFor(title));
            Assert.NotNull(DesktopTitleCatalog.MissionCatalogueFor(title));
            Assert.NotNull(DesktopTitleCatalog.PresentationFor(title));
            Assert.NotEmpty(DesktopTitleCatalog.GamePathVariableFor(title));
        }

        // The ids are distinct, which is what makes -title able to name one of them.
        Assert.Equal(
            DesktopTitleCatalog.All.Count,
            DesktopTitleCatalog.All.Select(static r => r.Title.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
