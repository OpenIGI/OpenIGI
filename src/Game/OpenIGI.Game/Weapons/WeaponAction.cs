namespace OpenIGI.Game.Weapons;

/// <summary>
/// What a human is doing with its weapon, as <c>0x4137E0</c> switches on it.
/// </summary>
/// <remarks>
/// The caller passes this and the weapon supplies the pair of animations to play — see
/// <see cref="WeaponAnimations"/>. Zero is not an action: it stops whatever was playing.
/// </remarks>
public enum WeaponAction
{
    /// <summary>Play nothing, and clear the weapon animation.</summary>
    None = 0,

    /// <summary>Standing still.</summary>
    Stand = 1,

    /// <summary>Walking.</summary>
    Walk = 2,

    /// <summary>Running.</summary>
    Run = 3,

    /// <summary>Crouching.</summary>
    Crouch = 4,

    /// <summary>Moving while crouched.</summary>
    CrouchRun = 5,

    /// <summary>Firing, with the timed variant that scales the full-body clip.</summary>
    FireTimed = 6,

    /// <summary>Reloading.</summary>
    Reload = 7,

    /// <summary>Firing, cycling through the weapon's up-to-three firing clips.</summary>
    Fire = 8,

    /// <summary>Standing, entered from a different path than <see cref="Stand"/> but identical in effect.</summary>
    StandAlternate = 9,

    /// <summary>Standing, blended in immediately rather than eased.</summary>
    StandImmediate = 10,

    /// <summary>The medipack's sustained syringe loop.</summary>
    /// <remarks>
    /// This is not a weapon reload or a second fire press. IGI.EXE's medipack state plays
    /// FireAnimation1 once, then keeps FireAnimation2 looping while Fire remains held.
    /// </remarks>
    MedipackLoop = 11,
}
