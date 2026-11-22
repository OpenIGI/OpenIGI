namespace OpenIGI.Game;

/// <summary>Whose movement and look I.G.I. 2's player has (<see cref="GameplayCVars.Igi2Movement"/>).</summary>
public enum Igi2PlayerMovement
{
    /// <summary>I.G.I. 2's own: its gaits, its jump and its filtered mouse, as the game shipped.</summary>
    Igi2,

    /// <summary>Project I.G.I.'s: its body's walk, run, strafe, crouch, jump and fall, and its look (<see cref="Igi1Look"/>).</summary>
    Igi1,
}

/// <summary>How the mouse turns a player on Project I.G.I.'s movement (<see cref="GameplayCVars.Igi2Igi1Look"/>).</summary>
public enum Igi1Look
{
    /// <summary>The remaster's: one mouse count one step, at any pacing.</summary>
    Linear,

    /// <summary>The original's: small turns eased and the pitch averaged with the last tick.</summary>
    Retail,
}

/// <summary>What <see cref="GameplayCVars.Igi2MovementCompare"/> stands in for the chosen movement with.</summary>
public enum Igi2MovementCompare
{
    /// <summary>Nothing: the chosen movement and look.</summary>
    Off,

    /// <summary>I.G.I. 2's own movement.</summary>
    Igi2,

    /// <summary>Project I.G.I.'s movement with the remaster's linear look.</summary>
    Igi1Linear,

    /// <summary>Project I.G.I.'s movement with the original's eased look.</summary>
    Igi1Retail,
}

/// <summary>How I.G.I. 2's player moves and looks this tick.</summary>
/// <param name="Movement">Whose movement.</param>
/// <param name="Look">How the mouse turns it on Project I.G.I.'s movement.</param>
/// <param name="Weight">Whether its weapons slow Project I.G.I.'s walk.</param>
public readonly record struct Igi2MovementChoice(Igi2PlayerMovement Movement, Igi1Look Look, bool Weight)
{
    /// <summary>Gets the choice the settings make now, the session's comparison standing in where one is set.</summary>
    public static Igi2MovementChoice Current => From(
        GameplayCVars.Igi2Movement.Value, GameplayCVars.Igi2Igi1Look.Value, GameplayCVars.Igi2Igi1Weight.Value, GameplayCVars.Igi2MovementCompare.Value);

    /// <summary>Gets a value indicating whether the mouse turns as Project I.G.I.'s original does.</summary>
    public bool RetailIgi1Look => Movement == Igi2PlayerMovement.Igi1 && Look == Igi1Look.Retail;

    /// <summary>Gets a value indicating whether the mouse turns one count one step, as the remaster's Project I.G.I. does.</summary>
    public bool LinearIgi1Look => Movement == Igi2PlayerMovement.Igi1 && Look == Igi1Look.Linear;

    /// <summary>Resolves the settings into a choice.</summary>
    /// <param name="movement">The chosen movement.</param>
    /// <param name="look">The chosen look.</param>
    /// <param name="weight">Whether weapons slow Project I.G.I.'s walk.</param>
    /// <param name="compare">The session's comparison, which stands in for the movement and the look where it is set.</param>
    /// <returns>The choice.</returns>
    public static Igi2MovementChoice From(Igi2PlayerMovement movement, Igi1Look look, bool weight, Igi2MovementCompare compare) => compare switch
    {
        Igi2MovementCompare.Igi2 => new(Igi2PlayerMovement.Igi2, look, weight),
        Igi2MovementCompare.Igi1Linear => new(Igi2PlayerMovement.Igi1, Igi1Look.Linear, weight),
        Igi2MovementCompare.Igi1Retail => new(Igi2PlayerMovement.Igi1, Igi1Look.Retail, weight),
        _ => new(movement, look, weight),
    };

    /// <summary>The choice in words, for the line the player is shown when a comparison steps it.</summary>
    /// <returns>The text.</returns>
    public string Describe() => Movement == Igi2PlayerMovement.Igi2
        ? "Movement: I.G.I. 2"
        : $"Movement: Project I.G.I., {(Look == Igi1Look.Retail ? "retail" : "linear")} look{(Weight ? ", weapon weight" : string.Empty)}";
}
