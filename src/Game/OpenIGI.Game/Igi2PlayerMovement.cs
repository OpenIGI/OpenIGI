namespace OpenIGI.Game;

/// <summary>Whose movement and look I.G.I. 2's player has (<see cref="GameplayCVars.Igi2Movement"/>).</summary>
public enum Igi2PlayerMovement
{
    /// <summary>I.G.I. 2's own: its gaits, its jump and its filtered mouse, as the game shipped.</summary>
    Igi2,

    /// <summary>Project I.G.I.'s: its body's walk, run, strafe, crouch, jump and fall, and the remaster's one-to-one mouse look.</summary>
    Igi1,
}

/// <summary>What <see cref="GameplayCVars.Igi2MovementCompare"/> stands in for the chosen movement with.</summary>
public enum Igi2MovementCompare
{
    /// <summary>Nothing: the chosen movement.</summary>
    Off,

    /// <summary>I.G.I. 2's own movement.</summary>
    Igi2,

    /// <summary>Project I.G.I.'s movement.</summary>
    Igi1,
}

/// <summary>How I.G.I. 2's player moves this tick.</summary>
/// <param name="Movement">Whose movement.</param>
/// <param name="Weight">Whether its weapons slow Project I.G.I.'s walk.</param>
public readonly record struct Igi2MovementChoice(Igi2PlayerMovement Movement, bool Weight)
{
    /// <summary>Gets the choice the settings make now, the session's comparison standing in where one is set.</summary>
    public static Igi2MovementChoice Current =>
        From(GameplayCVars.Igi2Movement.Value, GameplayCVars.Igi2Igi1Weight.Value, GameplayCVars.Igi2MovementCompare.Value);

    /// <summary>Resolves the settings into a choice.</summary>
    /// <param name="movement">The chosen movement.</param>
    /// <param name="weight">Whether weapons slow Project I.G.I.'s walk.</param>
    /// <param name="compare">The session's comparison, which stands in for the movement where it is set.</param>
    /// <returns>The choice.</returns>
    public static Igi2MovementChoice From(Igi2PlayerMovement movement, bool weight, Igi2MovementCompare compare) => compare switch
    {
        Igi2MovementCompare.Igi2 => new(Igi2PlayerMovement.Igi2, weight),
        Igi2MovementCompare.Igi1 => new(Igi2PlayerMovement.Igi1, weight),
        _ => new(movement, weight),
    };

    /// <summary>The choice in words, for the line the player is shown when a comparison steps it.</summary>
    /// <returns>The text.</returns>
    public string Describe() => Movement == Igi2PlayerMovement.Igi2
        ? "Movement: I.G.I. 2"
        : $"Movement: Project I.G.I.{(Weight ? ", weapon weight" : string.Empty)}";
}
