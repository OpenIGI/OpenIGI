namespace OpenIGI.Game.Player.Aim;

/// <summary>Modern aim's settings for one player's tick, as one immutable value.</summary>
/// <param name="Enabled">Whether modern aim is on; off, everything plays as the original does.</param>
/// <param name="Toggle">Whether the aim button toggles the sights rather than holding them.</param>
/// <param name="Sensitivity">The look speed through the sights, as a share of the zoom-matched speed.</param>
/// <param name="Zoom">How much of each weapon's magnification is used, nought to one.</param>
/// <param name="AutoMount">Whether aiming at a surface braces the weapon on it without the mount key.</param>
/// <remarks>
/// Read from <see cref="ModernAimCVars"/> once a tick (<see cref="Current"/>) rather than from the variables mid-tick, so a tick
/// sees one set, and handed to a session's players by the server so all of them play by its values.
/// </remarks>
public readonly record struct ModernAimOptions(bool Enabled, bool Toggle, float Sensitivity, float Zoom, bool AutoMount)
{
    /// <summary>Gets the settings with modern aim off: the original game.</summary>
    public static ModernAimOptions Off { get; } = new(false, false, 1f, 1f, true);

    /// <summary>Gets the settings the variables hold now.</summary>
    public static ModernAimOptions Current => new(
        ModernAimCVars.Enabled.Value,
        ModernAimCVars.Toggle.Value,
        ModernAimCVars.Sensitivity.Value,
        ModernAimCVars.Zoom.Value,
        ModernAimCVars.AutoMount.Value);
}
