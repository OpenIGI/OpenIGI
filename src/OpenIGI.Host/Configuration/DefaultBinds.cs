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

        // The wall switch for whichever of the level's lamps the crosshair is on, and the fault. Neither
        // writes a setting -- they act on the level that is loaded and nothing survives it -- so the rule
        // this table exists for has nothing to check on them.
        ("l", "lamp_switch"),
        ("k", "lamp_break"),

        // Swaps between looking through the player's eyes and looking over their shoulder. It obeys the
        // rule above rather than being an exception to it: g_thirdperson is deliberately NOT archived, so
        // a view flipped to while lining up a jump is not what the game starts as tomorrow.
        ("v", "toggle g_thirdperson"),
    ];

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
