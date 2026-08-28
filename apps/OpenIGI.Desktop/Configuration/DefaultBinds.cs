namespace OpenIGI.Desktop.Configuration;

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
