namespace OpenIGI.Rendering;

/// <summary>Which of a level's lamps the daylight switches.</summary>
public enum LampDaylight
{
    /// <summary>None: every lamp burns day and night, as in the original.</summary>
    Off,

    /// <summary>Lamps with open sky over them; those under a roof stay lit, as a building's do.</summary>
    Outdoors,

    /// <summary>Every lamp in the level.</summary>
    Everywhere,
}
