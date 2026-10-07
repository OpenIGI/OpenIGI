namespace OpenIGI.Game.Igi2.Player;

/// <summary>The bits of the player's locomotion signal word, <c>tState.bSignal</c>.</summary>
/// <remarks>
/// <para>
/// The word every locomotion state tests to choose its next state. Some bits are packed from the channels
/// each tick by <c>HumanPlayer_StateSignalHandler</c> (<c>igi2 0x4A2800</c>); the rest are set by the
/// physics and damage code and survive the handler, which keeps <see cref="Carried"/> and rebuilds the
/// others. Names are from what sets them and what the states do with them.
/// </para>
/// </remarks>
[Flags]
public enum Igi2PlayerSignal : uint
{
    /// <summary>No bit.</summary>
    None = 0,

    /// <summary>The main animation controller has played its clip to the end (<c>nFlags &amp; 4</c>).</summary>
    AnimationEnded = 0x1,

    /// <summary>Standing on the ground, set by the ground collision. Its absence is what makes a state fall.</summary>
    Grounded = 0x2,

    /// <summary>Strafe left held.</summary>
    Left = 0x20,

    /// <summary>Strafe right held.</summary>
    Right = 0x40,

    /// <summary>Forward held.</summary>
    Forward = 0x80,

    /// <summary>Backward held.</summary>
    Backward = 0x100,

    /// <summary>Fire held.</summary>
    Fire = 0x200,

    /// <summary>Alternate fire held.</summary>
    AlternateFire = 0x400,

    /// <summary>Reload held.</summary>
    Reload = 0x800,

    /// <summary>Activate held.</summary>
    Activate = 0x1000,

    /// <summary>Crouch held.</summary>
    Crouch = 0x2000,

    /// <summary>Jump held.</summary>
    Jump = 0x4000,

    /// <summary>Struck this tick; the update clears it after the body has been updated.</summary>
    Hit = 0x10000,

    /// <summary>Dead.</summary>
    Dead = 0x20000,

    /// <summary>Walking rather than running.</summary>
    Walking = 0x40000,

    /// <summary>Next weapon held.</summary>
    NextWeapon = 0x80000,

    /// <summary>Deep enough in water to swim (5734.4 units).</summary>
    Swimming = 0x800000,

    /// <summary>Lower-stance pressed this tick.</summary>
    LowerStance = 0x1000000,

    /// <summary>Raise-stance pressed this tick.</summary>
    RaiseStance = 0x2000000,

    /// <summary>Modify-walk-run held.</summary>
    ModifyWalkRun = 0x4000000,

    /// <summary>Turning: the horizontal look channel is non-zero.</summary>
    Turning = 0x8000000,

    /// <summary>Stand/lie toggled while lying flat — get up.</summary>
    StandUp = 0x10000000,

    /// <summary>Stand/lie toggled while not lying flat — lie down.</summary>
    LieDown = 0x20000000,

    /// <summary>The bits the handler keeps from the previous word (<c>0xC0F7001E</c>).</summary>
    Carried = 0xC0F7001E,
}
