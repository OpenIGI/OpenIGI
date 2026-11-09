namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>
/// How much of a player a bomb's blast takes at an effective distance: a bell over the wave's travel, five healths at the
/// bomb, exactly one at the killing radius, and a few percent ten metres past it.
/// </summary>
/// <remarks>
/// The bell is the classic one (damage = peak · e<sup>−d²/2σ²</sup>), its width set so the peak falls to one whole health
/// at the mode's killing radius. Inside that, nobody lives in the open; a corner or a wall between adds to the effective
/// distance (<see cref="BlastWaveField"/>) and so takes the player down the bell. Nothing is taken below one percent: like
/// CS2's since July 2026, a far-off blast leaves no token scratch.
/// </remarks>
public static class BombDamage
{
    /// <summary>The share of a whole health the blast takes at the bomb itself.</summary>
    public const double PeakShare = 5.0;

    /// <summary>The least share it takes; below it, nothing.</summary>
    public const double LeastShare = 0.01;

    /// <summary>The share of a whole health the blast takes at an effective distance.</summary>
    /// <param name="effectiveMetres">How far the wave effectively travelled to the player.</param>
    /// <param name="lethalMetres">The killing radius in the open.</param>
    /// <returns>The share: one or more kills; nought for none.</returns>
    public static float Share(double effectiveMetres, double lethalMetres)
    {
        double lethal = Math.Max(lethalMetres, 1.0);
        double twoSigmaSquared = lethal * lethal / Math.Log(PeakShare);
        double share = PeakShare * Math.Exp(-(effectiveMetres * effectiveMetres) / twoSigmaSquared);
        return share < LeastShare ? 0f : (float)share;
    }
}
