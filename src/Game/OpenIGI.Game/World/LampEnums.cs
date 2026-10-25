namespace OpenIGI.Game.World;

/// <summary>What one of a level's lamps has been told to do.</summary>
public enum LampMode
{
    /// <summary>Lit, as the level ships it.</summary>
    On,

    /// <summary>Switched off at the wall.</summary>
    Off,

    /// <summary>Failing: the tube strikes, runs, stutters and drops out on its own.</summary>
    Broken,
}

/// <summary>What sort of lamp a fitting is, which decides how it comes on.</summary>
/// <remarks>
/// The level does not say -- I.G.I. declares no lights at all -- so each fixture is given one when the board
/// is built, leaning on its colour: an amber lamp is more likely a sodium lantern, a white one a tube.
/// The point is not to be right about any one fitting. It is that a yard of lamps coming on at dusk does
/// not all do the same thing, because real ones do not.
/// </remarks>
public enum LampCharacter
{
    /// <summary>A fluorescent tube on a glow starter: cathode glow, a flash, maybe a few tries, then light.</summary>
    Tube,

    /// <summary>A filament: no ignition at all, it simply heats up.</summary>
    Filament,

    /// <summary>A high-intensity discharge lantern: an ignitor's crackle, a dim arc, a long warm-up.</summary>
    Discharge,
}

/// <summary>Something a lamp did that can be heard.</summary>
/// <remarks>
/// Raised at the instant the light does it, from the same state machine that moves the light, so the
/// sound cannot drift from the picture: the starter's tick lands on the frame of the flash.
/// </remarks>
public enum LampSound
{
    /// <summary>A wall switch was flipped, either way.</summary>
    Switch,

    /// <summary>A glow starter opened and the tube tried to strike: the flash.</summary>
    Starter,

    /// <summary>A discharge lamp's ignitor began a burst of high-voltage pulses.</summary>
    Ignitor,

    /// <summary>The arc caught: the ballast takes the load and hums.</summary>
    Arc,

    /// <summary>A cold filament took the current.</summary>
    Filament,

    /// <summary>The supply to a lit lamp was cut.</summary>
    Extinguish,

    /// <summary>A failing tube began a burst of restrikes.</summary>
    Sputter,
}
