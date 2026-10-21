namespace OpenIGI.Game.Igi2.Humans;

/// <summary>A human's posture, <c>Human_s.ePose</c>.</summary>
public enum Igi2HumanPose
{
    /// <summary>Standing.</summary>
    Standing = 0,

    /// <summary>Crouching.</summary>
    Crouching = 1,

    /// <summary>Lying flat.</summary>
    LyingFlat = 2,
}

/// <summary>The postures a human is allowed into, <c>Human_s.bAllowedStance</c>.</summary>
/// <remarks>Cleared bit by bit by whatever forbids a posture this tick: water depth, a held launcher, a jump cooldown.</remarks>
[Flags]
public enum Igi2AllowedStance : byte
{
    /// <summary>Nothing allowed.</summary>
    None = 0,

    /// <summary>Standing.</summary>
    Stand = 0x1,

    /// <summary>Crouching.</summary>
    Crouch = 0x2,

    /// <summary>Lying flat.</summary>
    Lie = 0x4,

    /// <summary>Jumping.</summary>
    Jump = 0x8,

    /// <summary>Everything.</summary>
    All = Stand | Crouch | Lie | Jump,
}
