namespace OpenIGI.Presentation.Hud;

/// <summary>
/// How much of the local player's health a planted bomb would take where it stands: the portion every health bar -- the
/// original's power bar and both remastered gauges -- flashes, so a player can tell at a glance whether it is clear.
/// </summary>
/// <remarks>
/// <para>
/// One value for the frame, set by the host from the match's HUD before the HUD is drawn and read by whichever health bar
/// the player's settings draw. A whole health is one; anything at or over the health left reads as death, and the whole bar
/// flashes.
/// </para>
/// <para>
/// CS2 added the same read-out to its bomb in July 2026 (the health the blast would take, shown under the health as the
/// fuse runs); this is the port's own drawing of it, on each title's own bar.
/// </para>
/// </remarks>
public static class HudBlastRisk
{
    /// <summary>Gets or sets the share of a whole health the bomb would take; nought for none.</summary>
    public static float Share { get; set; }

    /// <summary>The part of a health bar to flash: from where the health would fall to, up to where it is now.</summary>
    /// <param name="health">The health left, nought to one.</param>
    /// <returns>The bottom and top of the flashing part, nought to one; equal where there is none.</returns>
    public static (float From, float To) Span(float health)
    {
        float top = Math.Clamp(health, 0f, 1f);
        float share = Math.Clamp(Share, 0f, 1f);
        return share <= 0f || top <= 0f ? (top, top) : (Math.Max(0f, top - share), top);
    }

    /// <summary>Gets a value indicating whether the blast would kill.</summary>
    /// <param name="health">The health left.</param>
    /// <returns>Whether it would.</returns>
    public static bool IsLethal(float health) => Share > 0f && Share >= health - 0.0001f;

    /// <summary>How bright the flash is now: a quick pulse, quicker where it would kill.</summary>
    /// <param name="seconds">The clock, in seconds.</param>
    /// <param name="lethal">Whether it would kill.</param>
    /// <returns>Nought to one.</returns>
    public static float Pulse(double seconds, bool lethal)
    {
        double rate = lethal ? 7.0 : 4.0;
        return 0.35f + (0.65f * (float)(0.5 + (0.5 * Math.Sin(seconds * rate * Math.PI))));
    }
}
