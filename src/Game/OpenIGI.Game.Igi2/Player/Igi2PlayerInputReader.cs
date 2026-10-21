using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>One tick of the player's input port, as the channel reader sees it.</summary>
/// <param name="MouseX">The horizontal analog delta, before sensitivity.</param>
/// <param name="MouseY">The vertical analog delta, before sensitivity and inversion.</param>
/// <param name="Current">The buttons held this tick (<c>Input_s.bCurrentButton</c>).</param>
/// <param name="Pressed">The buttons pressed this tick (<c>Input_s.bEdgeButton</c>).</param>
public readonly record struct Igi2PlayerInputState(
    float MouseX,
    float MouseY,
    Igi2PlayerAction Current,
    Igi2PlayerAction Pressed);

/// <summary>Fills a channel frame from the player's input, as <c>HumanPlayerInput_ReadChannels</c> does.</summary>
/// <remarks>
/// <para>
/// <c>igi2 0x5296C0</c>, the single-player path. The frame starts zeroed and each binding writes 1 or 0 to
/// its channel; the look channels carry the analog deltas scaled by the profile's mouse sensitivity,
/// clamped to [0.05, 1], with the turn keys nudging the horizontal one by 0.05 either way.
/// </para>
/// <para>
/// Most channels read the <em>held</em> word. The weapon-category selectors and the inventory read the
/// <em>pressed</em> word, so holding a category key selects once; the computer pages and the stand/lie
/// toggle read the held high word and leave the edge to the state machine's own last-tick test.
/// </para>
/// </remarks>
public sealed class Igi2PlayerInputReader
{
    /// <summary>The lowest mouse sensitivity the reader honours.</summary>
    public const float MinimumSensitivity = 0.050000001f;

    /// <summary>The highest mouse sensitivity the reader honours.</summary>
    public const float MaximumSensitivity = 1.0f;

    /// <summary>How far each turn key moves the horizontal look channel per tick.</summary>
    public const float TurnKeyStep = 0.050000001f;

    private static readonly (Igi2PlayerAction Action, Igi2HumanChannel Channel)[] HeldBindings =
    [
        (Igi2PlayerAction.MoveLeft, Igi2HumanChannel.MoveLeft),
        (Igi2PlayerAction.MoveRight, Igi2HumanChannel.MoveRight),
        (Igi2PlayerAction.MoveForwards, Igi2HumanChannel.MoveForwards),
        (Igi2PlayerAction.MoveBackwards, Igi2HumanChannel.MoveBackwards),
        (Igi2PlayerAction.Fire, Igi2HumanChannel.Fire),
        (Igi2PlayerAction.AlternateFire, Igi2HumanChannel.AlternateFire),
        (Igi2PlayerAction.Reload, Igi2HumanChannel.Reload),
        (Igi2PlayerAction.Activate, Igi2HumanChannel.Activate),
        (Igi2PlayerAction.Crouch, Igi2HumanChannel.Crouch),
        (Igi2PlayerAction.RaiseStance, Igi2HumanChannel.RaiseStance),
        (Igi2PlayerAction.LowerStance, Igi2HumanChannel.LowerStance),
        (Igi2PlayerAction.ToggleStandLie, Igi2HumanChannel.ToggleStandLie),
        (Igi2PlayerAction.ModifyWalkRun, Igi2HumanChannel.ModifyWalkRun),
        (Igi2PlayerAction.Jump, Igi2HumanChannel.Jump),
        (Igi2PlayerAction.Peek, Igi2HumanChannel.Peek),
        (Igi2PlayerAction.ZoomIn, Igi2HumanChannel.ZoomIn),
        (Igi2PlayerAction.ZoomOut, Igi2HumanChannel.ZoomOut),
        (Igi2PlayerAction.DropWeapon, Igi2HumanChannel.DropWeapon),
        ((Igi2PlayerAction)0x40000000, Igi2HumanChannel.Button30),
        ((Igi2PlayerAction)0x80000000, Igi2HumanChannel.Button31),
        (Igi2PlayerAction.ToggleWalkRun, Igi2HumanChannel.ToggleWalkRun),
        (Igi2PlayerAction.NextWeapon, Igi2HumanChannel.NextWeapon),
        (Igi2PlayerAction.PrevWeapon, Igi2HumanChannel.PrevWeapon),
        (Igi2PlayerAction.Binoculars, Igi2HumanChannel.Binoculars),
        (Igi2PlayerAction.Thermal, Igi2HumanChannel.Thermal),
        ((Igi2PlayerAction)0x2_0000_0000_0000, Igi2HumanChannel.Button49),
        (Igi2PlayerAction.ComputerMap, Igi2HumanChannel.ComputerMap),
        (Igi2PlayerAction.ComputerLog, Igi2HumanChannel.ComputerLog),
        (Igi2PlayerAction.ComputerObjectives, Igi2HumanChannel.ComputerObjectives),
        (Igi2PlayerAction.ComputerIgiHq, Igi2HumanChannel.ComputerIgiHq),
    ];

    private static readonly Igi2PlayerAction[] Categories =
    [
        Igi2PlayerAction.WeaponCategory1,
        Igi2PlayerAction.WeaponCategory2,
        Igi2PlayerAction.WeaponCategory3,
        Igi2PlayerAction.WeaponCategory4,
        Igi2PlayerAction.WeaponCategory5,
        Igi2PlayerAction.WeaponCategory6,
        Igi2PlayerAction.WeaponCategory7,
        (Igi2PlayerAction)0x80_0000_0000,
        Igi2PlayerAction.WeaponCategory9,
    ];

    /// <summary>Gets or sets the profile's mouse sensitivity.</summary>
    public float MouseSensitivity { get; set; } = 1f;

    /// <summary>Gets or sets a value indicating whether the profile inverts the vertical look.</summary>
    public bool InvertMouse { get; set; }

    /// <summary>Gets or sets a value indicating whether fire is held at zero until it is released.</summary>
    /// <remarks>
    /// <c>HumanPlayerInput_t.isFreezeFire</c>: set when something consumes a click — leaving a menu with
    /// the mouse, say — so the same press does not also shoot. The reader zeroes the fire channel until
    /// it reads zero on its own, then clears the latch.
    /// </remarks>
    public bool FreezeFire { get; set; }

    /// <summary>Writes one frame of channels.</summary>
    /// <param name="input">This tick's port state.</param>
    /// <param name="frame">Receives <see cref="Igi2HumanChannels.Count"/> channels.</param>
    /// <exception cref="ArgumentException">The frame is not <see cref="Igi2HumanChannels.Count"/> values.</exception>
    public void Read(in Igi2PlayerInputState input, Span<float> frame)
    {
        if (frame.Length != Igi2HumanChannels.Count)
        {
            throw new ArgumentException(
                $"A channel frame is {Igi2HumanChannels.Count} values, not {frame.Length}.", nameof(frame));
        }

        frame.Clear();

        float sensitivity = Math.Clamp(MouseSensitivity, MinimumSensitivity, MaximumSensitivity);
        float lookX = ((Fpu)sensitivity * input.MouseX).F;
        if ((input.Current & Igi2PlayerAction.TurnLeft) != 0)
        {
            lookX = ((Fpu)lookX - TurnKeyStep).F;
        }

        if ((input.Current & Igi2PlayerAction.TurnRight) != 0)
        {
            lookX = ((Fpu)lookX + TurnKeyStep).F;
        }

        frame[(int)Igi2HumanChannel.LookX] = lookX;
        frame[(int)Igi2HumanChannel.LookY] = ((Fpu)sensitivity * (InvertMouse ? -input.MouseY : input.MouseY)).F;

        foreach ((Igi2PlayerAction action, Igi2HumanChannel channel) in HeldBindings)
        {
            frame[(int)channel] = (input.Current & action) != 0 ? 1f : 0f;
        }

        frame[(int)Igi2HumanChannel.SelectWeapon] = -1f;

        for (int i = 0; i < Categories.Length; i++)
        {
            frame[(int)Igi2HumanChannel.WeaponCategory1 + i] = (input.Pressed & Categories[i]) != 0 ? 1f : 0f;
        }

        frame[(int)Igi2HumanChannel.Inventory] = (input.Pressed & Igi2PlayerAction.Inventory) != 0 ? 1f : 0f;

        if (FreezeFire)
        {
            if (frame[(int)Igi2HumanChannel.Fire] == 0f)
            {
                FreezeFire = false;
            }
            else
            {
                frame[(int)Igi2HumanChannel.Fire] = 0f;
            }
        }
    }
}
