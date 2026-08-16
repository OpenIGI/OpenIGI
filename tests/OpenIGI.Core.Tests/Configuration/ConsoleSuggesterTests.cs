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

    // ---- Command arguments ----

    /// <summary>
    /// The case that started this: gfx_preset is a command, not a variable, so looking only at the
    /// variable registry meant pressing Tab after it did nothing at all.
    /// </summary>
    [Fact]
    public void A_command_completes_the_alternatives_its_usage_lists()
    {
        ConsoleSystem console = Build(out _);
        console.Commands.Add("gfx_preset", "Applies a preset.", static _ => { }, "[vanilla | enhanced | high | ultra]");

        IReadOnlyList<ConsoleSuggestion> ranked = console.Suggest("gfx_preset ");

        Assert.Equal(4, ranked.Count);
        Assert.Contains(ranked, s => s.Text == "vanilla");
        Assert.Contains(ranked, s => s.Text == "ultra");
    }

    [Fact]
    public void A_half_typed_command_argument_narrows_the_alternatives()
    {
        ConsoleSystem console = Build(out _);
        console.Commands.Add("gfx_preset", "Applies a preset.", static _ => { }, "[vanilla | enhanced | high | ultra]");

        Assert.Equal("enhanced", Assert.Single(console.Suggest("gfx_preset enh")).Text);
    }

    [Fact]
    public void Alternatives_written_without_spaces_are_understood_too()
    {
        ConsoleSystem console = Build(out _);
        console.Commands.Add("con_panel", "Shows a panel.", static _ => { }, "<console|cvars|watch> [on|off]");

        Assert.Equal(3, console.Suggest("con_panel ").Count);
        Assert.Equal(2, console.Suggest("con_panel cvars ").Count);
    }

    /// <summary>
    /// A usage of <c>&lt;variable&gt;</c> stands for the registry rather than a fixed list, which is
    /// what makes reset, toggle and inc complete usefully.
    /// </summary>
    [Fact]
    public void A_variable_placeholder_completes_from_the_registry()
    {
        ConsoleSystem console = Build(out CVarRegistry registry);
        CVar.Bool("r_vsync", true, registry: registry);
        CVar.Int("r_shadows", 2, registry: registry);
        console.Commands.Add("reset", "Restores a default.", static _ => { }, "<variable>");

        Assert.Equal("r_vsync", Assert.Single(console.Suggest("reset r_v")).Text);
        Assert.Equal(2, console.Suggest("reset r_").Count);
    }

    [Fact]
    public void A_command_placeholder_completes_from_the_command_registry()
    {
        ConsoleSystem console = Build(out _);
        console.Commands.Add("quit", "Quits.", static _ => { });
        console.Commands.Add("bind", "Binds a key.", static _ => { }, "<key> <command...>");

        Assert.Contains(console.Suggest("bind f5 qu"), s => s.Text == "quit");
    }

    [Fact]
    public void A_repeating_argument_keeps_completing_past_its_own_position()
    {
        ConsoleSystem console = Build(out _);
        console.Commands.Add("quit", "Quits.", static _ => { });
        console.Commands.Add("bind", "Binds a key.", static _ => { }, "<key> <command...>");

        Assert.Contains(console.Suggest("bind f5 echo qu"), s => s.Text == "quit");
    }

    [Fact]
    public void A_command_with_no_declared_arguments_offers_nothing()
    {
        ConsoleSystem console = Build(out _);
        console.Commands.Add("quit", "Quits.", static _ => { });

        Assert.Empty(console.Suggest("quit "));
    }

    [Fact]
    public void An_explicit_completer_overrides_the_usage_string()
    {
        ConsoleSystem console = Build(out _);
        console.Commands.Add(new ConsoleCommand(
            "exec",
            "Runs a script.",
            "<file>",
            static _ => { },
            CommandOptions.None,
            _ => ["autoexec.cfg", "keys.cfg"]));

        Assert.Equal(2, console.Suggest("exec ").Count);
        Assert.Equal("keys.cfg", Assert.Single(console.Suggest("exec k")).Text);
    }

    [Fact]
    public void Tab_completes_a_command_argument()
    {
        ConsoleModel model = Model(out _);
        model.Console.Commands.Add("gfx_preset", "Applies a preset.", static _ => { }, "[vanilla | enhanced | high | ultra]");

        model.SetInput("gfx_preset ult");
        model.Complete();

        Assert.Equal("gfx_preset ultra", model.Input);
    }

    // ---- Tab, which is what actually inserts a value ----

    private static ConsoleModel Model(out CVarRegistry registry)
    {
        registry = new CVarRegistry();
        return new ConsoleModel(new ConsoleSystem(registry));
    }

    /// <summary>
    /// The behaviour that was missing: the dropdown listed a variable's values but Tab did nothing,
    /// because the plain completion answers only for the first token.
    /// </summary>
    [Fact]
    public void Tab_completes_a_lone_value()
    {
        ConsoleModel model = Model(out CVarRegistry registry);
        CVar.Choice("r_quality", Quality.Low, registry: registry);

        model.SetInput("r_quality hi");
        model.Complete();

        Assert.Equal("r_quality high", model.Input);
    }

    [Fact]
    public void Tab_extends_several_values_to_their_common_prefix()
    {
        ConsoleModel model = Model(out CVarRegistry registry);
        CVar.Text("r_mode", "alpha", registry: registry);
        CVar.Choice("r_quality", Quality.Low, registry: registry);

        model.SetInput("r_quality ");
        model.Complete();

        // off | low | high share no common prefix, so the line is left alone and the first candidate
        // is highlighted instead, ready for a second Tab. With nothing typed every candidate scores
        // the same and the tie breaks on name, so "high" is first.
        Assert.Equal("r_quality ", model.Input);
        Assert.Equal(0, model.SuggestionIndex);
        Assert.Equal("high", model.RichSuggestions[0].Text);

        model.Complete();
        Assert.Equal("r_quality high", model.Input);
    }

    [Fact]
    public void Tab_completes_a_boolean_to_one_of_its_two_values()
    {
        ConsoleModel model = Model(out CVarRegistry registry);
        CVar.Bool("r_vsync", true, registry: registry);

        model.SetInput("r_vsync 1");
        model.Complete();

        Assert.Equal("r_vsync 1", model.Input);
    }

    /// <summary>A completed value ends the statement, so unlike a name it gets no trailing space.</summary>
    [Fact]
    public void A_completed_value_leaves_no_trailing_space()
    {
        ConsoleModel model = Model(out CVarRegistry registry);
        CVar.Choice("r_quality", Quality.Low, registry: registry);

        model.SetInput("r_quality lo");
        model.Complete();

        Assert.Equal("r_quality low", model.Input);
        Assert.DoesNotContain(" low ", model.Input, StringComparison.Ordinal);
    }

    [Fact]
    public void Tab_on_a_number_does_nothing_because_a_range_is_not_a_set()
    {
        ConsoleModel model = Model(out CVarRegistry registry);
        CVar.Int("r_shadows", 2, 0, 4, registry: registry);

        model.SetInput("r_shadows ");
        model.Complete();

        Assert.Equal("r_shadows ", model.Input);
    }

    /// <summary>
    /// A value completion must leave the variable it belongs to standing, and must not swallow
    /// anything typed after the caret.
    /// </summary>
    [Fact]
    public void Tab_replaces_only_the_token_under_the_caret()
    {
        ConsoleModel model = Model(out CVarRegistry registry);
        CVar.Choice("r_quality", Quality.Low, registry: registry);

        model.SetInput("r_quality hi");
        model.Complete();

        Assert.StartsWith("r_quality ", model.Input, StringComparison.Ordinal);
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
