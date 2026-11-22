namespace OpenIGI.Game;

/// <summary>Whose movement and look I.G.I. 2's player has (<see cref="GameplayCVars.Igi2Movement"/>).</summary>
public enum Igi2PlayerMovement
{
    /// <summary>I.G.I. 2's own: its gaits, its jump and its filtered mouse, as the game shipped.</summary>
    Igi2,

    /// <summary>Project I.G.I.'s: its body's walk, run, strafe, crouch, jump and fall, and its one-to-one mouse look.</summary>
    Igi1,
}
