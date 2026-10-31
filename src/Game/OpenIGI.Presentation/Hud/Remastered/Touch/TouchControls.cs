using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered.Touch;

/// <summary>What the game is showing, as a touch screen's controls need to know it.</summary>
public enum TouchControlMode
{
    /// <summary>Nothing to control: a load, say.</summary>
    None,

    /// <summary>A menu, the computer or anything else with a pointer: a finger is the pointer.</summary>
    Pointer,

    /// <summary>Play: the stick, the look and the buttons.</summary>
    Play,

    /// <summary>A cutscene: a finger held anywhere skips it, which the host reads from the touches themselves.</summary>
    Cutscene,

    /// <summary>The map computer: a finger is its pointer, and a button in the corner closes it.</summary>
    Computer,
}

/// <summary>A button on the screen in play, and the player's action it holds.</summary>
public enum TouchButton
{
    /// <summary>Fire; dragged, it turns the view, so a thumb aims and shoots at once.</summary>
    Fire,

    /// <summary>The weapon's second use: the Dragunov's and the MP5's sights.</summary>
    Scope,

    /// <summary>Reload.</summary>
    Reload,

    /// <summary>Jump.</summary>
    Jump,

    /// <summary>Crouch.</summary>
    Crouch,

    /// <summary>Use: a door, a ladder, a switch.</summary>
    Use,

    /// <summary>The next weapon.</summary>
    Weapon,

    /// <summary>Lean out.</summary>
    Peek,

    /// <summary>The pause menu.</summary>
    Pause,

    /// <summary>The map computer.</summary>
    Map,

    /// <summary>The binoculars.</summary>
    Binoculars,

    /// <summary>Zoom in, through the binoculars or a sight.</summary>
    ZoomIn,

    /// <summary>Zoom out.</summary>
    ZoomOut,

    /// <summary>Closes the map computer.</summary>
    Close,

    /// <summary>Held to talk to the others in a co-op room.</summary>
    Talk,
}

/// <summary>The finger that is the pointer: where it is, how far it moved this frame, and whether it came down this frame.</summary>
/// <param name="At">Where it is, in window pixels.</param>
/// <param name="Delta">How far it moved this frame, in window pixels.</param>
/// <param name="Began">Whether it came down this frame.</param>
public readonly record struct TouchPointer(UiVec2 At, UiVec2 Delta, bool Began);

/// <summary>Where a button sits and how big it is: a round one by its centre and radius, the weapon's card by its box.</summary>
/// <param name="Button">The button.</param>
/// <param name="Centre">Its centre, in window pixels.</param>
/// <param name="Radius">Its radius, or half the card's height, in window pixels.</param>
/// <param name="Card">Whether it is a wide card rather than a round button.</param>
/// <param name="Aims">Whether dragging it also turns the view: the right-hand fire button, so one thumb aims and shoots.</param>
public readonly record struct TouchButtonSpot(TouchButton Button, UiVec2 Centre, float Radius, bool Card = false, bool Aims = false)
{
    /// <summary>Gets a card's box.</summary>
    public UiRect Box => new(Centre.X - (Radius * 2.4f), Centre.Y - Radius, Radius * 4.8f, Radius * 2f);

    /// <summary>Whether a finger landing at a point presses it: a little generous, as a thumb is not a pointer.</summary>
    public bool Hit(float x, float y)
    {
        if (Card)
        {
            UiRect box = Box;
            float slack = Radius * 0.25f;
            return x >= box.X - slack && x <= box.Right + slack && y >= box.Y - slack && y <= box.Bottom + slack;
        }

        float dx = x - Centre.X, dy = y - Centre.Y;
        float reach = Radius * 1.15f;
        return (dx * dx) + (dy * dy) <= reach * reach;
    }
}

/// <summary>A touch screen's controls: the fingers on it read as a stick, a look, buttons, or a pointer, frame by frame.</summary>
/// <remarks>
/// <para>
/// What a finger does is settled as it comes down, and kept until it lifts: a thumb that lands on the stick's half of the screen
/// is the stick however far it wanders, one that lands on a button holds that button. Fire also turns the view as it is dragged.
/// </para>
/// <para>
/// Nothing here presses anything. The host reads <see cref="Held"/>, <see cref="Stick"/>, <see cref="Look"/> and
/// <see cref="Pointer"/> and presses the keys the player's actions are bound to, so the controls follow the player's bindings and
/// are the same for both titles. The layout is in back-buffer pixels from a 1080-line design (<see cref="LayoutContext"/>).
/// </para>
/// </remarks>
public sealed class TouchControls
{
    /// <summary>How far a drag turns the view: the mouse's counts per back-buffer pixel dragged.</summary>
    public const float LookCountsPerPixel = 4.5f;

    /// <summary>The share of its reach the stick moves before it holds a direction.</summary>
    public const float StickDeadZone = 0.35f;

    /// <summary>The share of its reach the stick comes back under before it lets a direction go: below <see cref="StickDeadZone"/>, so a
    /// thumb resting at the edge does not flick the key on and off from frame to frame.</summary>
    public const float StickReleaseZone = 0.22f;

    /// <summary>The stick's reach, in design units.</summary>
    public const float StickReach = 120f;

    private const float FireRadius = 110f;
    private const float LeftFireRadius = 68f;
    private const float ButtonRadius = 62f;
    private const float SmallRadius = 50f;
    private const float CornerRadius = 36f;

    private readonly Dictionary<int, Role> _roles = [];
    private readonly List<TouchButtonSpot> _layout = [];
    private readonly List<TouchButtonSpot> _computerLayout = [];
    private readonly HashSet<TouchButton> _held = [];
    private (float Width, float Height) _laidOutFor;
    private bool _talkShown;

    /// <summary>Gets or sets whether the button held to talk is offered: in a co-op room, with push to talk.</summary>
    public bool TalkShown
    {
        get => _talkShown;
        set
        {
            if (_talkShown != value)
            {
                _talkShown = value;
                _laidOutFor = default;
            }
        }
    }
    private TouchControlMode _mode;
    private UiVec2 _lastLook;

    private enum RoleKind
    {
        Stick,
        Look,
        Button,
        Pointer,
        Ignored,
    }

    /// <summary>Gets the buttons on the screen in the current mode, where they are; laid out again when the screen changes size.</summary>
    public IReadOnlyList<TouchButtonSpot> Layout => _mode == TouchControlMode.Computer ? _computerLayout : _layout;

    /// <summary>Gets the buttons held this frame. A tap is held for the frame it lands in.</summary>
    public IReadOnlySet<TouchButton> Held => _held;

    /// <summary>Gets where the stick points, each axis from −1 to 1 (down and right positive), or nought when no thumb is on it.</summary>
    public UiVec2 Stick { get; private set; }

    /// <summary>Gets where the stick's thumb came down, or null when there is none.</summary>
    public UiVec2? StickOrigin { get; private set; }

    /// <summary>Gets where the stick rests when no thumb is on it, drawn faintly so the player knows where to put one.</summary>
    public UiVec2 StickRest { get; private set; }

    /// <summary>Gets the stick's reach in back-buffer pixels.</summary>
    public float StickReachPixels { get; private set; }

    /// <summary>Gets how far the view is to turn this frame, in mouse counts.</summary>
    public UiVec2 Look { get; private set; }

    /// <summary>Gets the finger that is the pointer, or null when none is down.</summary>
    public TouchPointer? Pointer { get; private set; }

    /// <summary>Gets the mode the controls were last read in.</summary>
    public TouchControlMode Mode => _mode;

    /// <summary>Reads a frame's fingers.</summary>
    /// <param name="touch">The fingers.</param>
    /// <param name="mode">What the game is showing.</param>
    /// <param name="context">The screen's layout, in back-buffer pixels.</param>
    public void Update(TouchState touch, TouchControlMode mode, LayoutContext context)
    {
        ArgumentNullException.ThrowIfNull(touch);
        ArgumentNullException.ThrowIfNull(context);

        if ((context.Viewport.X, context.Viewport.Y) != _laidOutFor)
        {
            LayOut(context);
        }

        if (mode != _mode)
        {
            // A menu opening over play, play resuming: every finger down starts again from nothing, and lifts first.
            _mode = mode;
            _lastLook = default;
            _roles.Clear();
            foreach (TouchPoint point in touch.Points)
            {
                _roles[point.Id] = new Role(RoleKind.Ignored, null);
            }
        }

        // Fingers gone since the last frame let go of what they held.
        List<int>? gone = null;
        foreach (int id in _roles.Keys)
        {
            if (!Contains(touch, id))
            {
                (gone ??= []).Add(id);
            }
        }

        if (gone is not null)
        {
            foreach (int id in gone)
            {
                _roles.Remove(id);
            }
        }

        _held.Clear();
        UiVec2 look = default;
        UiVec2 stick = default;
        UiVec2? stickOrigin = null;
        TouchPointer? pointer = null;

        foreach (TouchPoint point in touch.Points)
        {
            if (!_roles.TryGetValue(point.Id, out Role role))
            {
                role = Assign(point, mode, context);
                _roles[point.Id] = role;
            }

            switch (role.Kind)
            {
                case RoleKind.Stick:
                    UiVec2 origin = new(point.StartX, point.StartY);
                    float dx = point.X - origin.X, dy = point.Y - origin.Y;
                    float length = MathF.Sqrt((dx * dx) + (dy * dy));
                    float scale = length > StickReachPixels ? StickReachPixels / length : 1f;
                    stick = new UiVec2(dx * scale / StickReachPixels, dy * scale / StickReachPixels);
                    stickOrigin = origin;
                    break;
                case RoleKind.Look:
                    look += new UiVec2(point.DeltaX, point.DeltaY);
                    break;
                case RoleKind.Button when role.Button is { } button:
                    _held.Add(button);
                    if (role.Aims)
                    {
                        look += new UiVec2(point.DeltaX, point.DeltaY);
                    }

                    break;
                case RoleKind.Pointer:
                    pointer = new TouchPointer(new UiVec2(point.X, point.Y), new UiVec2(point.DeltaX, point.DeltaY), point.Began);
                    break;
            }
        }

        Stick = stick;
        StickOrigin = stickOrigin;

        // A phone reports a finger's movement on its own clock, not the frame's: one frame takes two reports and the next none,
        // and turned as it came the view lurched and stalled by turns at a steady frame rate. Each frame turns by the mean of its
        // drag and the last one's, which evens those out and still turns the view by exactly as far as the finger went.
        UiVec2 smoothed = new((look.X + _lastLook.X) * 0.5f, (look.Y + _lastLook.Y) * 0.5f);
        _lastLook = look;
        Look = new UiVec2(smoothed.X * LookCountsPerPixel, smoothed.Y * LookCountsPerPixel);
        Pointer = pointer;
    }

    private Role Assign(in TouchPoint point, TouchControlMode mode, LayoutContext context)
    {
        if (mode == TouchControlMode.Computer)
        {
            foreach (TouchButtonSpot spot in _computerLayout)
            {
                if (spot.Hit(point.StartX, point.StartY))
                {
                    return new Role(RoleKind.Button, spot.Button);
                }
            }
        }

        if (mode is TouchControlMode.Pointer or TouchControlMode.Computer)
        {
            // One pointer: a second finger on a menu is ignored rather than clicking somewhere else.
            foreach (Role other in _roles.Values)
            {
                if (other.Kind == RoleKind.Pointer)
                {
                    return new Role(RoleKind.Ignored, null);
                }
            }

            return new Role(RoleKind.Pointer, null);
        }

        if (mode != TouchControlMode.Play)
        {
            return new Role(RoleKind.Ignored, null);
        }

        foreach (TouchButtonSpot spot in _layout)
        {
            if (spot.Hit(point.StartX, point.StartY))
            {
                return new Role(RoleKind.Button, spot.Button, spot.Aims);
            }
        }

        bool stickHalf = point.StartX < context.Viewport.X * 0.42f && point.StartY > context.Viewport.Y * 0.3f;
        bool stickFree = true;
        foreach (Role other in _roles.Values)
        {
            stickFree &= other.Kind != RoleKind.Stick;
        }

        return new Role(stickHalf && stickFree ? RoleKind.Stick : RoleKind.Look, null);
    }

    private void LayOut(LayoutContext context)
    {
        _laidOutFor = (context.Viewport.X, context.Viewport.Y);
        _layout.Clear();
        UiRect safe = context.SafeRect;
        float Px(float design) => context.Px(design);
        void Round(TouchButton button, float right, float bottom, float radius, bool aims = false) =>
            _layout.Add(new TouchButtonSpot(button, new UiVec2(safe.Right - Px(right), safe.Bottom - Px(bottom)), Px(radius), Aims: aims));

        StickReachPixels = Px(StickReach);
        StickRest = new UiVec2(safe.X + Px(250f), safe.Bottom - Px(230f));

        // The right thumb's cluster, as shooters on a phone lay it out: fire large in the corner, the sight and the reload
        // within a thumb's roll of it, jump above, and the stances in a column along the edge.
        Round(TouchButton.Fire, 300f, 230f, FireRadius, aims: true);
        Round(TouchButton.Scope, 470f, 400f, ButtonRadius);
        Round(TouchButton.Reload, 505f, 190f, ButtonRadius);
        Round(TouchButton.Jump, 300f, 430f, ButtonRadius);
        Round(TouchButton.Crouch, 110f, 170f, SmallRadius);
        Round(TouchButton.Peek, 110f, 315f, SmallRadius);
        Round(TouchButton.ZoomIn, 110f, 600f, CornerRadius);
        Round(TouchButton.ZoomOut, 110f, 495f, CornerRadius);

        // The left thumb's fire, above the stick, for shooting while the right one aims.
        _layout.Add(new TouchButtonSpot(TouchButton.Fire, new UiVec2(safe.X + Px(250f), safe.Bottom - Px(540f)), Px(LeftFireRadius)));

        // The weapon in hand on a card at the bottom's middle, and the use button above it, where a prompt would be.
        float cardRadius = Px(52f);
        _layout.Add(new TouchButtonSpot(TouchButton.Weapon, new UiVec2(safe.X + (safe.Width * 0.5f), safe.Bottom - Px(90f)), cardRadius, Card: true));
        _layout.Add(new TouchButtonSpot(TouchButton.Use, new UiVec2(safe.X + (safe.Width * 0.5f), safe.Bottom - Px(250f)), Px(SmallRadius)));

        // The computer's way out, where a window's close button is.
        _computerLayout.Clear();
        _computerLayout.Add(new TouchButtonSpot(TouchButton.Close, new UiVec2(safe.Right - Px(90f), safe.Y + Px(90f)), Px(ButtonRadius)));

        // The few that are not reached for in a fight, small along the top right.
        float y = safe.Y + Px(80f);
        TouchButton[] corner = _talkShown
            ? [TouchButton.Pause, TouchButton.Map, TouchButton.Binoculars, TouchButton.Talk]
            : [TouchButton.Pause, TouchButton.Map, TouchButton.Binoculars];
        for (int i = 0; i < corner.Length; i++)
        {
            _layout.Add(new TouchButtonSpot(corner[i], new UiVec2(safe.Right - Px(80f) - (i * Px(CornerRadius * 2.6f)), y), Px(CornerRadius)));
        }
    }

    private static bool Contains(TouchState touch, int id)
    {
        foreach (TouchPoint point in touch.Points)
        {
            if (point.Id == id)
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct Role(RoleKind Kind, TouchButton? Button, bool Aims = false);
}
