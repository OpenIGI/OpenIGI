namespace OpenIGI.Game.World;

/// <summary>Which curve, if any, a lamp is currently on.</summary>
internal enum LampTransition
{
    Steady,
    Cooling,
    Warming,
}

/// <summary>What a failing lamp is doing at the moment.</summary>
internal enum LampBrokenPhase
{
    Run,
    Strike,
    Glow,
    Dark,
}

/// <summary>One lamp's switch, its supply, its filament and its fault.</summary>
internal struct Lamp
{
    public LampMode Mode;

    /// <summary>Whether the daylight's side of the circuit is closed.</summary>
    public bool Supplied;

    public LampCharacter Character;

    /// <summary>Whether the fixture's light is amber, which is what a sodium lantern starts pink towards.</summary>
    public bool Sodium;

    /// <summary>Whether the fault is still there behind a lamp that is switched off.</summary>
    public bool WasBroken;

    public float Level;

    public LampTransition Transition;

    /// <summary>Seconds into the current curve.</summary>
    public float Elapsed;

    /// <summary>Where the curve started, so an interrupted fade resumes from where it got to.</summary>
    public float From;

    /// <summary>Whether this start goes through an ignition at all, rather than simply warming.</summary>
    public bool Strikes;

    /// <summary>Whether this start is from a day standing dark, which is longer and less certain.</summary>
    public bool Cold;

    /// <summary>Whether the arc has caught, after which the lamp only climbs.</summary>
    public bool Arc;

    /// <summary>Whether the current attempt has raised its sound yet.</summary>
    public bool Fired;

    /// <summary>How many more tries the starter or the ignitor makes before one holds.</summary>
    public int Attempts;

    /// <summary>Seconds into the warm-up at which the current attempt began.</summary>
    public float AttemptStart;

    /// <summary>A tube's preheat before its flash; a discharge lamp's pause between ignitor bursts.</summary>
    public float Preheat;

    /// <summary>How bright the current attempt's flash is.</summary>
    public float Peak;

    /// <summary>How bright the cathodes glow while they preheat.</summary>
    public float Glow;

    /// <summary>Seconds into the warm-up at which the arc caught.</summary>
    public float ArcAt;

    /// <summary>The time constant the lamp climbs to full on once the arc has caught, in seconds.</summary>
    public float Ramp;

    /// <summary>Where a cold tube's light lands when its arc catches.</summary>
    public float ColdStart;

    public LampBrokenPhase Broken;

    public float BrokenElapsed;

    public float BrokenLeft;

    public float BrokenRate;

    public float BrokenPeak;

    public uint Rng;
}
