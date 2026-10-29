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
}

/// <summary>Where a button sits and how big it is: a round one by its centre and radius, a chip along the top by its box.</summary>
/// <param name="Button">The button.</param>
/// <param name="Centre">Its centre, in window pixels.</param>
/// <param name="Radius">Its radius, or half the chip's height, in window pixels.</param>
/// <param name="Chip">Whether it is a chip along the top rather than a round button.</param>
public readonly record struct TouchButtonSpot(TouchButton Button, UiVec2 Centre, float Radius, bool Chip)
{
    /// <summary>Gets a chip's box.</summary>
    public UiRect Box => new(Centre.X - (Radius * 1.35f), Centre.Y - Radius, Radius * 2.7f, Radius * 2f);

    /// <summary>Whether a finger landing at a point presses it: a little generous, as a thumb is not a pointer.</summary>
    public bool Hit(float x, float y)
    {
        if (Chip)
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
    public const float StickDeadZone = 0.3f;

    /// <summary>The stick's reach, in design units.</summary>
    public const float StickReach = 120f;

    private const float FireRadius = 125f;
    private const float ButtonRadius = 72f;
    private const float ChipRadius = 38f;

    private readonly Dictionary<int, Role> _roles = [];
    private readonly List<TouchButtonSpot> _layout = [];
    private readonly HashSet<TouchButton> _held = [];
    private (float Width, float Height) _laidOutFor;
    private TouchControlMode _mode;

    private enum RoleKind
    {
        Stick,
        Look,
        Button,
        Pointer,
        Ignored,
    }

    /// <summary>Gets the buttons in play, where they are; laid out again when the screen changes size.</summary>
    public IReadOnlyList<TouchButtonSpot> Layout => _layout;

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

    /// <summary>Gets where the pointer finger is and whether it is down, or null when none is.</summary>
    public (UiVec2 At, bool Down)? Pointer { get; private set; }

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
        (UiVec2, bool)? pointer = null;

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
                    if (button == TouchButton.Fire)
                    {
                        look += new UiVec2(point.DeltaX, point.DeltaY);
                    }

                    break;
                case RoleKind.Pointer:
                    pointer = (new UiVec2(point.X, point.Y), true);
                    break;
            }
        }

        Stick = stick;
        StickOrigin = stickOrigin;
        Look = new UiVec2(look.X * LookCountsPerPixel, look.Y * LookCountsPerPixel);
        Pointer = pointer;
    }

    private Role Assign(in TouchPoint point, TouchControlMode mode, LayoutContext context)
    {
        if (mode == TouchControlMode.Pointer)
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
                return new Role(RoleKind.Button, spot.Button);
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

        StickReachPixels = context.Px(StickReach);
        StickRest = new UiVec2(safe.X + context.Px(260f), safe.Bottom - context.Px(250f));

        // Fire in the lower right corner, the buttons a thumb reaches from it fanned about it in two rows.
        float fire = context.Px(FireRadius);
        float small = context.Px(ButtonRadius);
        UiVec2 centre = new(safe.Right - context.Px(210f), safe.Bottom - context.Px(210f));
        _layout.Add(new TouchButtonSpot(TouchButton.Fire, centre, fire, Chip: false));

        void Around(TouchButton button, float distance, float degrees)
        {
            float a = degrees * MathF.PI / 180f;
            float d = context.Px(distance);
            _layout.Add(new TouchButtonSpot(button, new UiVec2(centre.X + (MathF.Cos(a) * d), centre.Y - (MathF.Sin(a) * d)), small, Chip: false));
        }

        Around(TouchButton.Scope, 225f, 90f);
        Around(TouchButton.Reload, 225f, 128f);
        Around(TouchButton.Jump, 225f, 166f);
        Around(TouchButton.Crouch, 225f, 204f);
        Around(TouchButton.Use, 395f, 112f);
        Around(TouchButton.Weapon, 395f, 140f);
        Around(TouchButton.Peek, 395f, 168f);

        // The chips along the top right, the pause menu in the corner.
        float chip = context.Px(ChipRadius);
        float step = chip * 3.1f;
        float y = safe.Y + context.Px(70f);
        TouchButton[] chips = [TouchButton.Pause, TouchButton.Map, TouchButton.Binoculars, TouchButton.ZoomIn, TouchButton.ZoomOut];
        for (int i = 0; i < chips.Length; i++)
        {
            _layout.Add(new TouchButtonSpot(chips[i], new UiVec2(safe.Right - context.Px(80f) - (i * step), y), chip, Chip: true));
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

    private readonly record struct Role(RoleKind Kind, TouchButton? Button);
}
