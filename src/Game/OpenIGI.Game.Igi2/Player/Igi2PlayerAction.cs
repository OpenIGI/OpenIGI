namespace OpenIGI.Game.Igi2.Player;

/// <summary>The bindable player actions, as bits of the input port's 64-bit button words.</summary>
/// <remarks>
/// <para>
/// From the remap table <c>GOInRemap</c> resolves action names against (<c>igi2 0x63EC88</c>): forty-byte
/// records of <c>{ char name[32]; u64 mask; }</c>, grouped under <c>TAB-</c> headings the controls menu
/// shows. <c>config.qsc</c> binds keys to these names (<c>GOInRemap(MoveForwards, …, KEY_W)</c>), so the
/// names are the stable contract and the masks are where the port's button words put them.
/// </para>
/// <para>
/// The low dword is <c>Input_s.bCurrentButton</c>'s low word, the high dword its high word; the edge word
/// (pressed this frame) has the same layout. Multiplayer-only actions are listed for completeness: the
/// single-player channel reader never looks at them.
/// </para>
/// </remarks>
[Flags]
public enum Igi2PlayerAction : ulong
{
    /// <summary>No action.</summary>
    None = 0,

    /// <summary>Strafe left.</summary>
    MoveLeft = 0x1,

    /// <summary>Strafe right.</summary>
    MoveRight = 0x2,

    /// <summary>Move forward.</summary>
    MoveForwards = 0x4,

    /// <summary>Move backward.</summary>
    MoveBackwards = 0x8,

    /// <summary>Turn left by keyboard.</summary>
    TurnLeft = 0x100,

    /// <summary>Turn right by keyboard.</summary>
    TurnRight = 0x200,

    /// <summary>Crouch, held.</summary>
    Crouch = 0x400,

    /// <summary>Jump.</summary>
    Jump = 0x800,

    /// <summary>Raise the stance one step.</summary>
    RaiseStance = 0x1000,

    /// <summary>Lower the stance one step.</summary>
    LowerStance = 0x2000,

    /// <summary>Toggle walking and running.</summary>
    ToggleWalkRun = 0x4000,

    /// <summary>Walk while held, or run while held when walking is toggled on.</summary>
    ModifyWalkRun = 0x8000,

    /// <summary>Peek.</summary>
    Peek = 0x10000,

    /// <summary>Use what is in front.</summary>
    Activate = 0x20000,

    /// <summary>Reload.</summary>
    Reload = 0x40000,

    /// <summary>Fire.</summary>
    Fire = 0x80000,

    /// <summary>The weapon's alternate fire.</summary>
    AlternateFire = 0x100000,

    /// <summary>Next weapon.</summary>
    NextWeapon = 0x200000,

    /// <summary>Previous weapon.</summary>
    PrevWeapon = 0x400000,

    /// <summary>Drop the held weapon.</summary>
    DropWeapon = 0x800000,

    /// <summary>Binoculars.</summary>
    Binoculars = 0x1000000,

    /// <summary>Zoom in.</summary>
    ZoomIn = 0x2000000,

    /// <summary>Zoom out.</summary>
    ZoomOut = 0x4000000,

    /// <summary>Thermal sight.</summary>
    Thermal = 0x8000000,

    /// <summary>The map computer.</summary>
    ComputerMap = 0x10000000,

    /// <summary>Select weapon category 1.</summary>
    WeaponCategory1 = 0x1_0000_0000,

    /// <summary>Select weapon category 2.</summary>
    WeaponCategory2 = 0x2_0000_0000,

    /// <summary>Select weapon category 3.</summary>
    WeaponCategory3 = 0x4_0000_0000,

    /// <summary>Select weapon category 4.</summary>
    WeaponCategory4 = 0x8_0000_0000,

    /// <summary>Select weapon category 5.</summary>
    WeaponCategory5 = 0x10_0000_0000,

    /// <summary>Select weapon category 6.</summary>
    WeaponCategory6 = 0x20_0000_0000,

    /// <summary>Select weapon category 7.</summary>
    WeaponCategory7 = 0x40_0000_0000,

    /// <summary>Select weapon category 9. There is no category 8 binding.</summary>
    WeaponCategory9 = 0x100_0000_0000,

    /// <summary>Quick-load.</summary>
    QuickLoad = 0x400_0000_0000,

    /// <summary>The computer's log page.</summary>
    ComputerLog = 0x1000_0000_0000,

    /// <summary>The computer's objectives page.</summary>
    ComputerObjectives = 0x2000_0000_0000,

    /// <summary>The computer's IGI HQ page.</summary>
    ComputerIgiHq = 0x4000_0000_0000,

    /// <summary>The inventory.</summary>
    Inventory = 0x4_0000_0000_0000,

    /// <summary>Toggle between standing and lying flat.</summary>
    ToggleStandLie = 0x40_0000_0000_0000,
}
