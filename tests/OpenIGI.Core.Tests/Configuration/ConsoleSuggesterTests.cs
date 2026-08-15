using OpenIGI.Core.Configuration;
using Xunit;

namespace OpenIGI.Core.Tests.Configuration;

/// <summary>
/// Covers the ranked completion the developer console's dropdown reads. Ordering is the whole
/// contract here, so these assert the order and the reason for it, not merely that a name appeared.
/// </summary>
public sealed class ConsoleSuggesterTests
{
    private enum Quality
    {
        Off,
        Low,
        High,
    }

    private static ConsoleSystem Build(out CVarRegistry registry)
    {
        registry = new CVarRegistry();
        return new ConsoleSystem(registry);
    }

    // ---- Scoring ----

    [Fact]
    public void Exact_match_outranks_a_prefix_match()
    {
        int? exact = ConsoleSuggester.Score("r_fog", "r_fog");
        int? prefix = ConsoleSuggester.Score("r_fog_quality", "r_fog");

        Assert.NotNull(exact);
        Assert.NotNull(prefix);
        Assert.True(exact > prefix);
    }

    [Fact]
    public void Shorter_names_win_among_prefix_matches()
    {
        int? shorter = ConsoleSuggester.Score("r_fog", "r_f");
        int? longer = ConsoleSuggester.Score("r_fog_quality_extra", "r_f");

        Assert.True(shorter > longer);
    }

    /// <summary>
    /// The behaviour that makes a prefixed registry searchable: the reader knows the variable is about
    /// fog without knowing which prefix it hides behind.
    /// </summary>
    [Fact]
    public void A_segment_match_beats_a_bare_substring_match()
    {
        int? segment = ConsoleSuggester.Score("r_fog_quality", "fog");
        int? substring = ConsoleSuggester.Score("r_thickfoggy", "fog");

        Assert.NotNull(segment);
        Assert.NotNull(substring);
        Assert.True(segment > substring);
    }

    [Fact]
    public void Scattered_characters_still_match_as_a_subsequence()
    {
        Assert.NotNull(ConsoleSuggester.Score("r_fog_quality", "rfq"));
        Assert.Null(ConsoleSuggester.Score("r_fog_quality", "zzz"));
    }

    [Fact]
    public void An_empty_query_matches_everything()
    {
        Assert.Equal(0, ConsoleSuggester.Score("anything", string.Empty));
    }

    // ---- Name completion ----

    [Fact]
    public void Names_are_ranked_best_first()
    {
        ConsoleSystem console = Build(out CVarRegistry registry);
        CVar.Int("r_fog_quality", 1, registry: registry);
        CVar.Int("r_fog", 1, registry: registry);

        IReadOnlyList<ConsoleSuggestion> ranked = console.Suggest("r_fog");

        Assert.Equal("r_fog", ranked[0].Text);
        Assert.Equal("r_fog_quality", ranked[1].Text);
    }

    [Fact]
    public void A_suggestion_carries_the_value_range_and_help_the_dropdown_shows()
    {
        ConsoleSystem console = Build(out CVarRegistry registry);
        CVar.Int("r_shadows", 2, 0, 4, CVarOptions.None, "How sharp shadows are.", registry);

        ConsoleSuggestion suggestion = Assert.Single(console.Suggest("r_shadows"));

        Assert.Equal("2", suggestion.Detail);
        Assert.Equal("0 .. 4", suggestion.Range);
        Assert.Equal("How sharp shadows are.", suggestion.Help);
        Assert.Equal(ConsoleSuggestionKind.Variable, suggestion.Kind);
    }

    [Fact]
    public void Hidden_variables_and_commands_are_never_suggested()
    {
        ConsoleSystem console = Build(out CVarRegistry registry);
        CVar.Int("r_secret", 1, flags: CVarOptions.Hidden, registry: registry);
        console.Commands.Add("cmd_secret", "", static _ => { }, flags: CommandOptions.Hidden);

        Assert.Empty(console.Suggest("secret"));
    }

    [Fact]
    public void A_cheat_is_offered_but_marked_blocked_while_cheats_are_off()
    {
        ConsoleSystem console = Build(out CVarRegistry registry);
        CVar.Int("g_god", 0, flags: CVarOptions.Cheat, registry: registry);

        Assert.True(Assert.Single(console.Suggest("g_god")).IsBlocked);

        console.CheatsEnabled = true;
        Assert.False(Assert.Single(console.Suggest("g_god")).IsBlocked);
    }

    [Fact]
    public void Commands_and_aliases_are_suggested_alongside_variables()
    {
        ConsoleSystem console = Build(out _);
        console.Commands.Add("reload", "Reloads.", static _ => { });
        console.SetAlias("reloadall", "reload");

        IReadOnlyList<ConsoleSuggestion> ranked = console.Suggest("reload");

        Assert.Contains(ranked, s => s.Kind == ConsoleSuggestionKind.Command && s.Text == "reload");
        Assert.Contains(ranked, s => s.Kind == ConsoleSuggestionKind.Alias && s.Text == "reloadall");
    }

    // ---- Value completion, the thing Complete() cannot do ----

    [Fact]
    public void A_boolean_offers_its_two_canonical_values()
    {
        ConsoleSystem console = Build(out CVarRegistry registry);
        CVar.Bool("r_vsync", true, registry: registry);

        IReadOnlyList<ConsoleSuggestion> ranked = console.Suggest("r_vsync ");

        Assert.Equal(2, ranked.Count);
        Assert.All(ranked, s => Assert.Equal(ConsoleSuggestionKind.Value, s.Kind));
        Assert.Contains(ranked, s => s.Text == "0");
        Assert.Contains(ranked, s => s.Text == "1");
    }

    [Fact]
    public void An_enum_offers_its_members_and_marks_the_current_one()
    {
        ConsoleSystem console = Build(out CVarRegistry registry);
        CVar.Choice("r_quality", Quality.Low, registry: registry);

        IReadOnlyList<ConsoleSuggestion> ranked = console.Suggest("r_quality ");

        Assert.Equal(3, ranked.Count);
        Assert.Contains("current", Assert.Single(ranked, s => s.Text == "low").Detail);
    }

    [Fact]
    public void A_half_typed_value_narrows_the_choices()
    {
        ConsoleSystem console = Build(out CVarRegistry registry);
        CVar.Choice("r_quality", Quality.Low, registry: registry);

        Assert.Equal("high", Assert.Single(console.Suggest("r_quality hi")).Text);
    }

    [Fact]
    public void A_number_offers_no_values_because_a_range_is_not_a_set()
    {
        ConsoleSystem console = Build(out CVarRegistry registry);
        CVar.Int("r_shadows", 2, 0, 4, registry: registry);

        Assert.Empty(console.Suggest("r_shadows "));
    }

    [Fact]
    public void Only_the_last_statement_on_a_line_is_completed()
    {
        ConsoleSystem console = Build(out CVarRegistry registry);
        CVar.Bool("r_vsync", true, registry: registry);

        Assert.Equal("r_vsync", Assert.Single(console.Suggest("echo hello; r_vsy")).Text);
    }

    [Fact]
    public void The_limit_is_honoured()
    {
        ConsoleSystem console = Build(out CVarRegistry registry);
        for (int i = 0; i < 20; i++)
        {
            CVar.Int($"r_thing{i}", i, registry: registry);
        }

        Assert.Equal(5, console.Suggest("r_thing", limit: 5).Count);
    }
}
