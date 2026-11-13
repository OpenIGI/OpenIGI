namespace OpenIGI.Host.Configuration;

/// <summary>The keys the game binds for itself when the player has not bound them to something else.</summary>
/// <remarks>
/// <para>
/// Data rather than a run of <c>if</c> statements at start-up, because there is a rule about what may go
/// in here and a rule is only worth having if something checks it: <b>a default bind must not write a
/// setting the game saves.</b> These keys exist to flip between two implementations while looking at the
/// same frame, and a key pressed to look at something must not decide what the game starts as tomorrow.
/// </para>
/// <para>
/// That is not hypothetical. <c>r_rt</c> was archived while F6 toggled it, so comparing the hardware and
/// software trace once wrote the software path into <c>settings.cfg</c>: the next launch came up on it,
/// the graphics preset read as Custom because a managed value no longer matched, and the picture was
/// quietly worse with nothing on screen to say why. The setting is no longer archived, and
/// <c>DefaultBindTests</c> is what stops the next one being.
/// </para>
/// </remarks>
public static class DefaultBinds
{
    /// <summary>Each key and the console statement it runs, applied only where the key is unbound.</summary>
    public static IReadOnlyList<(string Key, string Statement)> All { get; } =
    [
        // Moves EVERY trace in the frame -- the sun's shadow, the indirect gather, the reflections, the
        // surface-cache relight -- between the hardware ray query and the software distance field.
        ("f6", "toggle r_rt"),

        // Moves the sun's shadow alone and leaves the rest of the frame tracing, so the ray-traced and
        // rasterised shadows can be flipped between without changing anything else in the picture.
        ("f7", "toggle r_rt_shadows"),

        // The two halves of the remaster's asset work, each taken out of the frame and put back while
        // looking at it. r_overrides swaps the installation's overrides folder in and out and reloads the
        // level's textures, so it costs a hitch each way; r_pbr is a multiplier the shader reads every
        // frame and is instant. Neither is archived -- see above.
        ("f8", "toggle r_overrides"),
        ("f9", "toggle r_pbr"),

        // Rebuilds the game over the next graphics API -- Direct3D 12, Vulkan, WebGPU -- and puts the player back
        // where they stood, so the two can be compared on one view. A command, not a setting: the API a game
        // starts on is r_renderer's (the settings page's Graphics API) or OPENIGI_RENDERER's, and stepping
        // through them here writes nothing to settings.cfg.
        ("f10", "r_switch_renderer"),

        // The wall switch for whichever of the level's lamps the crosshair is on, and the fault. Neither
        // writes a setting -- they act on the level that is loaded and nothing survives it -- so the rule
        // this table exists for has nothing to check on them.
        ("l", "lamp_switch"),
        ("k", "lamp_break"),

        // Swaps between looking through the player's eyes and looking over their shoulder. It obeys the
        // rule above rather than being an exception to it: g_thirdperson is deliberately NOT archived, so
        // a view flipped to while lining up a jump is not what the game starts as tomorrow.
        ("v", "toggle g_thirdperson"),

        // Co-op talk: the place under the crosshair marked for the others, a line typed to them, and four said at a press.
        // Each does nothing outside a co-op room.
        ("t", "coop_mark"),
        ("y", "coop_chat"),
        ("u", "coop_chat team"),
        ("f1", "coop_say Go, go, go!"),
        ("f2", "coop_say Hold position."),
        ("f3", "coop_say Enemy spotted."),
        ("f4", "coop_say Need help here!"),
    ];

    /// <summary>The statements a player rebinds from the Controls page, each with the row's name there.</summary>
    /// <remarks>
    /// The keys above that are a player's rather than a developer's. They are console binds, not profile actions -- the
    /// original's action mask has no bit left for them -- so the Controls page, which lists the profile, showed none of
    /// them: the third-person key could be neither found nor moved.
    /// </remarks>
    public static IReadOnlyList<(string Statement, string Label)> Listed { get; } =
    [
        ("toggle g_thirdperson", "ThirdPersonView"),
        ("coop_mark", "CoopMark"),
        ("coop_chat", "CoopChat"),
        ("coop_chat team", "CoopTeamChat"),
    ];

    /// <summary>The key a statement is bound to, by its bind name.</summary>
    /// <param name="binds">The console's binds, key to statement.</param>
    /// <param name="statement">The statement.</param>
    /// <returns>The first such key in name order, or <see langword="null"/> when no key runs it.</returns>
    public static string? KeyOf(IReadOnlyDictionary<string, string> binds, string statement)
    {
        ArgumentNullException.ThrowIfNull(binds);
        ArgumentNullException.ThrowIfNull(statement);

        return binds
            .Where(bind => string.Equals(bind.Value.Trim(), statement, StringComparison.OrdinalIgnoreCase))
            .Select(static bind => bind.Key)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>Moves a statement onto one key: every key that ran it lets go, and the new key runs it alone.</summary>
    /// <param name="console">The console whose binds change.</param>
    /// <param name="statement">The statement.</param>
    /// <param name="key">The new key's bind name.</param>
    public static void Rebind(OpenIGI.Core.Configuration.ConsoleSystem console, string statement, string key)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentException.ThrowIfNullOrWhiteSpace(statement);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        string[] old = [.. console.Binds
            .Where(bind => string.Equals(bind.Value.Trim(), statement, StringComparison.OrdinalIgnoreCase))
            .Select(static bind => bind.Key)];
        foreach (string held in old)
        {
            console.SetBind(held, string.Empty);
        }

        console.SetBind(key, statement);
    }

    /// <summary>Gives each default key its statement, where the player has bound neither the key nor the statement.</summary>
    /// <param name="console">The console, its saved binds already read.</param>
    /// <remarks>
    /// A key the player bound to something else is never stolen. And a statement they moved to another key is not given
    /// its old key back: it would then answer to both, and the move would last only until the next launch.
    /// </remarks>
    public static void Apply(OpenIGI.Core.Configuration.ConsoleSystem console)
    {
        ArgumentNullException.ThrowIfNull(console);

        foreach ((string key, string statement) in All)
        {
            if (!console.Binds.ContainsKey(key) && KeyOf(console.Binds, statement) is null)
            {
                console.SetBind(key, statement);
            }
        }
    }

    /// <summary>The variable a statement toggles, or <see langword="null"/> when it toggles nothing.</summary>
    /// <param name="statement">The bound statement.</param>
    /// <returns>The variable's name.</returns>
    /// <remarks>
    /// Deliberately only understands <c>toggle</c>. A bind that runs anything else is not making a
    /// setting follow a key press and has nothing for the rule above to check.
    /// </remarks>
    public static string? ToggledVariable(string statement)
    {
        ArgumentNullException.ThrowIfNull(statement);

        string[] parts = statement.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts[0].Equals("toggle", StringComparison.OrdinalIgnoreCase)
            ? parts[1]
            : null;
    }
}
