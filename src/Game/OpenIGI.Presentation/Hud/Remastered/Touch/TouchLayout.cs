using System.Globalization;
using System.Text;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered.Touch;

/// <summary>A control a touch screen's layout places: the stick, each fire button, and every other button.</summary>
public enum TouchControlId
{
    /// <summary>The stick the player walks with.</summary>
    Stick,

    /// <summary>The right thumb's fire, which also turns the view as it is dragged.</summary>
    Fire,

    /// <summary>The left thumb's fire, for shooting while the right one aims.</summary>
    FireLeft,

    /// <summary>The weapon's second use.</summary>
    Scope,

    /// <summary>Reload.</summary>
    Reload,

    /// <summary>Jump.</summary>
    Jump,

    /// <summary>Crouch.</summary>
    Crouch,

    /// <summary>Lean out.</summary>
    Peek,

    /// <summary>Use.</summary>
    Use,

    /// <summary>The weapon's card: the next weapon.</summary>
    Weapon,

    /// <summary>The pause menu.</summary>
    Pause,

    /// <summary>The map computer.</summary>
    Map,

    /// <summary>The binoculars.</summary>
    Binoculars,

    /// <summary>Zoom in.</summary>
    ZoomIn,

    /// <summary>Zoom out.</summary>
    ZoomOut,

    /// <summary>Held to talk, in a co-op room.</summary>
    Talk,

    /// <summary>A line of chat, in a multiplayer room.</summary>
    Chat,

    /// <summary>A match's scoreboard.</summary>
    Scoreboard,

    /// <summary>A match's shop.</summary>
    Buy,

    /// <summary>A team match's team menu.</summary>
    Team,

    /// <summary>Drop what is in hand, in a versus match.</summary>
    Drop,

    /// <summary>The flashlight. Off the screen until the player adds it.</summary>
    Flashlight,

    /// <summary>Walk or run. Off the screen until the player adds it.</summary>
    WalkRun,

    /// <summary>The previous weapon. Off the screen until the player adds it.</summary>
    PreviousWeapon,

    /// <summary>The weapon wheel, held open and pointed by dragging. Off the screen until the player adds it.</summary>
    WeaponWheel,

    /// <summary>A quick save, where the mission keeps save games. Off the screen until the player adds it.</summary>
    QuickSave,

    /// <summary>A quick load. Off the screen until the player adds it.</summary>
    QuickLoad,
}

/// <summary>Where a control stands, how large, how faint, and whether it is on the screen at all.</summary>
/// <param name="EdgeX">The edge of the safe screen it is measured from, across.</param>
/// <param name="X">How far its centre is from that edge, in design units of a 1080-line screen.</param>
/// <param name="EdgeY">The edge it is measured from, down.</param>
/// <param name="Y">How far its centre is from that edge, in design units.</param>
/// <param name="Scale">Its size against the size it was designed at.</param>
/// <param name="Opacity">How much of it shows, nought to one.</param>
/// <param name="Visible">Whether it is on the screen.</param>
public readonly record struct TouchPlacement(HudEdgeX EdgeX, float X, HudEdgeY EdgeY, float Y, float Scale = 1f, float Opacity = 1f, bool Visible = true);

/// <summary>How a readout of the display is moved from where its title's theme stands it.</summary>
/// <param name="Dx">How far right of the theme's place, in design units of a 1080-line screen.</param>
/// <param name="Dy">How far below it, in design units.</param>
/// <param name="Scale">Its size against the theme's.</param>
/// <param name="Opacity">How much of it shows, nought to one.</param>
/// <param name="Visible">Whether it is drawn.</param>
public readonly record struct HudPlacement(float Dx = 0f, float Dy = 0f, float Scale = 1f, float Opacity = 1f, bool Visible = true)
{
    /// <summary>Gets the placement that changes nothing.</summary>
    public static HudPlacement Default => new(0f, 0f, 1f, 1f, true);
}

/// <summary>A button the player added: another of a control, standing where they put it.</summary>
/// <param name="Kind">The control it is another of; never the stick.</param>
/// <param name="Placement">Where it stands.</param>
public readonly record struct TouchExtra(TouchControlId Kind, TouchPlacement Placement);

/// <summary>How the stick takes a thumb.</summary>
public enum TouchStickMode
{
    /// <summary>The stick is wherever the thumb lands on its side of the screen.</summary>
    Floating,

    /// <summary>The stick stays where it is drawn, and a thumb must land on it.</summary>
    Fixed,
}

/// <summary>
/// A player's own arrangement of a touch screen: what they moved, resized, faded, hid or added, over the layout the game ships.
/// </summary>
/// <remarks>
/// Only what differs from the shipped layout is kept, so a control the player never touched follows the game's own layout as
/// that changes, and the rows along the top close up over a button a session does not offer. Places are an edge of the safe
/// screen and a distance from it in design units, so an arrangement made on one screen's shape holds on another's.
/// </remarks>
public sealed class TouchLayout
{
    /// <summary>The smallest a control or readout is scaled to.</summary>
    public const float MinScale = 0.5f;

    /// <summary>The largest a control or readout is scaled to.</summary>
    public const float MaxScale = 2f;

    /// <summary>The faintest a control or readout is drawn: still findable.</summary>
    public const float MinOpacity = 0.1f;

    /// <summary>The readouts of the display a layout can move: those a theme places and a widget stands on.</summary>
    public static readonly IReadOnlyList<HudElement> Readouts =
    [
        HudElement.Vitals, HudElement.Weapon, HudElement.Inventory, HudElement.Actions,
        HudElement.Progress, HudElement.Timer, HudElement.Messages,
    ];

    // The rows along the top, which close up over what is not offered: the corner's from the right, a match's from the left.
    private static readonly TouchControlId[] CornerRow =
        [TouchControlId.Pause, TouchControlId.Map, TouchControlId.Binoculars, TouchControlId.Talk, TouchControlId.Chat];

    private static readonly TouchControlId[] MatchRow = [TouchControlId.Scoreboard, TouchControlId.Buy, TouchControlId.Team];

    private const float CornerRadius = 36f;
    private const float MatchRadius = 40f;

    /// <summary>Gets the controls the player placed themselves.</summary>
    public Dictionary<TouchControlId, TouchPlacement> Controls { get; } = [];

    /// <summary>Gets the readouts the player moved.</summary>
    public Dictionary<HudElement, HudPlacement> Hud { get; } = [];

    /// <summary>Gets or sets how the stick takes a thumb.</summary>
    public TouchStickMode StickMode { get; set; }

    /// <summary>The most buttons a player may add to a layout.</summary>
    public const int MaxExtras = 24;

    /// <summary>Gets the buttons the player added: more of the controls, the same button in a second place.</summary>
    public List<TouchExtra> Extras { get; } = [];

    /// <summary>Gets a value indicating whether nothing differs from the shipped layout.</summary>
    public bool IsDefault => Controls.Count == 0 && Hud.Count == 0 && Extras.Count == 0 && StickMode == TouchStickMode.Floating;

    /// <summary>Adds another of a control at a place, if the layout has room for one.</summary>
    /// <param name="kind">The control; not the stick, of which there is one.</param>
    /// <param name="placement">Where it stands.</param>
    /// <returns>Its place among the added buttons, or <c>-1</c> when none was added.</returns>
    public int Add(TouchControlId kind, TouchPlacement placement)
    {
        if (kind == TouchControlId.Stick || Extras.Count >= MaxExtras)
        {
            return -1;
        }

        Extras.Add(new TouchExtra(kind, Bounded(placement) with { Visible = true }));
        return Extras.Count - 1;
    }

    /// <summary>Places an added button, within what a layout allows.</summary>
    /// <param name="index">Its place among the added buttons.</param>
    /// <param name="placement">Where it stands now.</param>
    public void SetExtra(int index, TouchPlacement placement)
    {
        if (index >= 0 && index < Extras.Count)
        {
            Extras[index] = Extras[index] with { Placement = Bounded(placement) };
        }
    }

    private static TouchPlacement Bounded(TouchPlacement placement) => placement with
    {
        Scale = Math.Clamp(placement.Scale, MinScale, MaxScale),
        Opacity = Math.Clamp(placement.Opacity, MinOpacity, 1f),
    };

    /// <summary>Every control, in the order they are laid out.</summary>
    public static IReadOnlyList<TouchControlId> AllControls { get; } = Enum.GetValues<TouchControlId>();

    /// <summary>The radius a control is designed at, in design units; the stick's is its reach, the card's half its height.</summary>
    /// <param name="id">The control.</param>
    /// <returns>The radius.</returns>
    public static float DesignRadius(TouchControlId id) => id switch
    {
        TouchControlId.Stick => TouchControls.StickReach,
        TouchControlId.Fire => 110f,
        TouchControlId.FireLeft => 68f,
        TouchControlId.Scope or TouchControlId.Reload or TouchControlId.Jump => 62f,
        TouchControlId.Crouch or TouchControlId.Peek or TouchControlId.Use or TouchControlId.Drop => 50f,
        TouchControlId.Weapon => 52f,
        TouchControlId.Scoreboard or TouchControlId.Buy or TouchControlId.Team => MatchRadius,
        TouchControlId.Flashlight or TouchControlId.WalkRun => 46f,
        TouchControlId.PreviousWeapon or TouchControlId.WeaponWheel => 42f,
        _ => CornerRadius,
    };

    /// <summary>Whether a control is a wide card rather than a round button.</summary>
    /// <param name="id">The control.</param>
    /// <returns>Whether it is the weapon's card.</returns>
    public static bool IsCard(TouchControlId id) => id == TouchControlId.Weapon;

    /// <summary>The button a control holds, or <see langword="null"/> for the stick.</summary>
    /// <param name="id">The control.</param>
    /// <returns>The button.</returns>
    public static TouchButton? ButtonOf(TouchControlId id) => id switch
    {
        TouchControlId.Stick => null,
        TouchControlId.Fire or TouchControlId.FireLeft => TouchButton.Fire,
        TouchControlId.Scope => TouchButton.Scope,
        TouchControlId.Reload => TouchButton.Reload,
        TouchControlId.Jump => TouchButton.Jump,
        TouchControlId.Crouch => TouchButton.Crouch,
        TouchControlId.Peek => TouchButton.Peek,
        TouchControlId.Use => TouchButton.Use,
        TouchControlId.Weapon => TouchButton.Weapon,
        TouchControlId.Pause => TouchButton.Pause,
        TouchControlId.Map => TouchButton.Map,
        TouchControlId.Binoculars => TouchButton.Binoculars,
        TouchControlId.ZoomIn => TouchButton.ZoomIn,
        TouchControlId.ZoomOut => TouchButton.ZoomOut,
        TouchControlId.Talk => TouchButton.Talk,
        TouchControlId.Chat => TouchButton.Chat,
        TouchControlId.Scoreboard => TouchButton.Scoreboard,
        TouchControlId.Buy => TouchButton.Buy,
        TouchControlId.Team => TouchButton.Team,
        TouchControlId.Drop => TouchButton.Drop,
        TouchControlId.Flashlight => TouchButton.Flashlight,
        TouchControlId.WalkRun => TouchButton.WalkRun,
        TouchControlId.PreviousWeapon => TouchButton.PreviousWeapon,
        TouchControlId.WeaponWheel => TouchButton.WeaponWheel,
        TouchControlId.QuickSave => TouchButton.QuickSave,
        TouchControlId.QuickLoad => TouchButton.QuickLoad,
        _ => null,
    };

    /// <summary>Whether dragging a control also turns the view: the right thumb's fire, and the weapon wheel it points.</summary>
    /// <param name="id">The control.</param>
    /// <returns>Whether it aims.</returns>
    public static bool Aims(TouchControlId id) => id is TouchControlId.Fire or TouchControlId.WeaponWheel;

    /// <summary>Whether the player may take a control off the screen: all but the pause button, the only way to the menu.</summary>
    /// <param name="id">The control.</param>
    /// <returns>Whether it can be hidden.</returns>
    public static bool CanHide(TouchControlId id) => id != TouchControlId.Pause;

    /// <summary>A control's name, as the editor says it.</summary>
    /// <param name="id">The control.</param>
    /// <returns>The name.</returns>
    public static string Name(TouchControlId id) => id switch
    {
        TouchControlId.Stick => "Move stick",
        TouchControlId.Fire => "Fire",
        TouchControlId.FireLeft => "Left fire",
        TouchControlId.Scope => "Sight",
        TouchControlId.Use => "Use",
        TouchControlId.Weapon => "Weapon",
        TouchControlId.Map => "Map computer",
        TouchControlId.ZoomIn => "Zoom in",
        TouchControlId.ZoomOut => "Zoom out",
        TouchControlId.Talk => "Voice",
        TouchControlId.Buy => "Shop",
        TouchControlId.WalkRun => "Walk / run",
        TouchControlId.PreviousWeapon => "Previous weapon",
        TouchControlId.WeaponWheel => "Weapon wheel",
        TouchControlId.QuickSave => "Quick save",
        TouchControlId.QuickLoad => "Quick load",
        _ => id.ToString(),
    };

    /// <summary>A readout's name, as the editor says it.</summary>
    /// <param name="element">The readout.</param>
    /// <returns>The name.</returns>
    public static string Name(HudElement element) => element switch
    {
        HudElement.Vitals => "Health",
        HudElement.Weapon => "Weapon readout",
        HudElement.Inventory => "Weapons carried",
        HudElement.Actions => "Action prompt",
        HudElement.Progress => "Progress bar",
        HudElement.Timer => "Mission clock",
        HudElement.Messages => "Messages",
        _ => element.ToString(),
    };

    /// <summary>Where the game itself stands a control, among the controls a session offers.</summary>
    /// <param name="id">The control.</param>
    /// <param name="offered">Whether a control is offered now, for the rows that close up; null offers every one.</param>
    /// <returns>The shipped placement.</returns>
    public TouchPlacement DefaultOf(TouchControlId id, Func<TouchControlId, bool>? offered = null)
    {
        static TouchPlacement Right(float right, float bottom) => new(HudEdgeX.Right, right, HudEdgeY.Bottom, bottom);
        static TouchPlacement Left(float left, float bottom) => new(HudEdgeX.Left, left, HudEdgeY.Bottom, bottom);
        static TouchPlacement Centre(float x, float bottom) => new(HudEdgeX.Centre, x, HudEdgeY.Bottom, bottom);

        switch (id)
        {
            case TouchControlId.Stick: return Left(250f, 230f);

            // The right thumb's cluster, as shooters on a phone lay it out: fire large in the corner, the sight and the reload
            // within a thumb's roll of it, jump above, and the stances in a column along the edge.
            case TouchControlId.Fire: return Right(300f, 230f);
            case TouchControlId.Scope: return Right(470f, 400f);
            case TouchControlId.Reload: return Right(505f, 190f);
            case TouchControlId.Jump: return Right(300f, 430f);
            case TouchControlId.Crouch: return Right(110f, 170f);
            case TouchControlId.Peek: return Right(110f, 315f);
            case TouchControlId.ZoomIn: return Right(110f, 600f);
            case TouchControlId.ZoomOut: return Right(110f, 495f);

            // The left thumb's fire, above the stick.
            case TouchControlId.FireLeft: return Left(250f, 540f);

            // The weapon in hand on a card at the bottom's middle, and the use button above it, where a prompt would be.
            case TouchControlId.Weapon: return Centre(0f, 90f);
            case TouchControlId.Use: return Centre(0f, 250f);

            // The drop small beside the reload, where a thumb rolls to it.
            case TouchControlId.Drop: return Right(660f, 330f);

            // Those the player adds: off the screen until then, and out of the others' way when they come.
            case TouchControlId.Flashlight: return Right(650f, 170f) with { Visible = false };
            case TouchControlId.WalkRun: return Left(480f, 150f) with { Visible = false };
            case TouchControlId.PreviousWeapon: return Centre(-200f, 90f) with { Visible = false };
            case TouchControlId.WeaponWheel: return Centre(200f, 90f) with { Visible = false };
            case TouchControlId.QuickSave: return new TouchPlacement(HudEdgeX.Right, 80f, HudEdgeY.Top, 190f, Visible: false);
            case TouchControlId.QuickLoad: return new TouchPlacement(HudEdgeX.Right, 174f, HudEdgeY.Top, 190f, Visible: false);
        }

        // A row along the top: each stands after those before it that are offered and still where the game put them.
        bool corner = Array.IndexOf(CornerRow, id) >= 0;
        TouchControlId[] row = corner ? CornerRow : MatchRow;
        float step = (corner ? CornerRadius : MatchRadius) * 2.6f;
        int before = 0;
        foreach (TouchControlId other in row)
        {
            if (other == id)
            {
                break;
            }

            if (!Controls.ContainsKey(other) && (offered is null || offered(other)))
            {
                before++;
            }
        }

        return new TouchPlacement(corner ? HudEdgeX.Right : HudEdgeX.Left, 80f + (before * step), HudEdgeY.Top, 80f);
    }

    /// <summary>Where a control stands: where the player put it, or where the game does.</summary>
    /// <param name="id">The control.</param>
    /// <param name="offered">Whether a control is offered now; null offers every one.</param>
    /// <returns>The placement.</returns>
    public TouchPlacement Resolve(TouchControlId id, Func<TouchControlId, bool>? offered = null) =>
        Controls.TryGetValue(id, out TouchPlacement placed) ? placed : DefaultOf(id, offered);

    /// <summary>How a readout is moved, or the placement that changes nothing.</summary>
    /// <param name="element">The readout.</param>
    /// <returns>The placement.</returns>
    public HudPlacement Resolve(HudElement element) => Hud.TryGetValue(element, out HudPlacement placed) ? placed : HudPlacement.Default;

    /// <summary>Places a control, within what a layout allows: a size and an opacity in their ranges, and the pause button kept on.</summary>
    /// <param name="id">The control.</param>
    /// <param name="placement">Where it stands now.</param>
    public void Set(TouchControlId id, TouchPlacement placement)
    {
        placement = placement with
        {
            Scale = Math.Clamp(placement.Scale, MinScale, MaxScale),
            Opacity = Math.Clamp(placement.Opacity, MinOpacity, 1f),
            Visible = placement.Visible || !CanHide(id),
        };
        Controls[id] = placement;
    }

    /// <summary>Moves a readout, or forgets it when nothing about it is changed.</summary>
    /// <param name="element">The readout.</param>
    /// <param name="placement">How it is moved now.</param>
    public void Set(HudElement element, HudPlacement placement)
    {
        placement = placement with
        {
            Scale = Math.Clamp(placement.Scale, MinScale, MaxScale),
            Opacity = Math.Clamp(placement.Opacity, MinOpacity, 1f),
        };
        if (placement == HudPlacement.Default)
        {
            Hud.Remove(element);
        }
        else
        {
            Hud[element] = placement;
        }
    }

    /// <summary>Where a placement's centre is on a screen.</summary>
    /// <param name="placement">The placement.</param>
    /// <param name="context">The screen's layout, in back-buffer pixels.</param>
    /// <returns>The centre, in pixels.</returns>
    public static UiVec2 CentreOf(TouchPlacement placement, LayoutContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        UiRect safe = context.SafeRect;
        float x = placement.EdgeX switch
        {
            HudEdgeX.Left => safe.X + context.Px(placement.X),
            HudEdgeX.Centre => safe.X + (safe.Width * 0.5f) + context.Px(placement.X),
            _ => safe.Right - context.Px(placement.X),
        };
        float y = placement.EdgeY switch
        {
            HudEdgeY.Top => safe.Y + context.Px(placement.Y),
            HudEdgeY.Middle => safe.Y + (safe.Height * 0.5f) + context.Px(placement.Y),
            _ => safe.Bottom - context.Px(placement.Y),
        };
        return new UiVec2(x, y);
    }

    /// <summary>A placement moved to stand at a point: measured from the edge of the third of the screen the point is in.</summary>
    /// <param name="placement">The placement, whose size, opacity and visibility are kept.</param>
    /// <param name="centre">Where its centre is to be, in pixels.</param>
    /// <param name="context">The screen's layout, in back-buffer pixels.</param>
    /// <returns>The placement there.</returns>
    public static TouchPlacement MovedTo(TouchPlacement placement, UiVec2 centre, LayoutContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        UiRect safe = context.SafeRect;
        float unit = MathF.Max(context.Scale, 1e-4f);
        float alongX = (centre.X - safe.X) / MathF.Max(1f, safe.Width);
        float alongY = (centre.Y - safe.Y) / MathF.Max(1f, safe.Height);
        HudEdgeX edgeX = alongX < 1f / 3f ? HudEdgeX.Left : alongX > 2f / 3f ? HudEdgeX.Right : HudEdgeX.Centre;
        HudEdgeY edgeY = alongY < 1f / 3f ? HudEdgeY.Top : alongY > 2f / 3f ? HudEdgeY.Bottom : HudEdgeY.Middle;
        float x = edgeX switch
        {
            HudEdgeX.Left => (centre.X - safe.X) / unit,
            HudEdgeX.Centre => (centre.X - safe.X - (safe.Width * 0.5f)) / unit,
            _ => (safe.Right - centre.X) / unit,
        };
        float y = edgeY switch
        {
            HudEdgeY.Top => (centre.Y - safe.Y) / unit,
            HudEdgeY.Middle => (centre.Y - safe.Y - (safe.Height * 0.5f)) / unit,
            _ => (safe.Bottom - centre.Y) / unit,
        };
        return placement with { EdgeX = edgeX, X = x, EdgeY = edgeY, Y = y };
    }

    /// <summary>A copy that shares nothing with this one.</summary>
    /// <returns>The copy.</returns>
    public TouchLayout Clone()
    {
        TouchLayout copy = new() { StickMode = StickMode };
        foreach ((TouchControlId id, TouchPlacement placement) in Controls)
        {
            copy.Controls[id] = placement;
        }

        foreach ((HudElement element, HudPlacement placement) in Hud)
        {
            copy.Hud[element] = placement;
        }

        copy.Extras.AddRange(Extras);

        return copy;
    }
}

/// <summary>A layout as a line of text: what a settings file keeps, and what one player can hand another.</summary>
/// <remarks>
/// <c>v1/stick=fixed/c.Fire=r300,b230,s100,o100,v1/x.Jump=l400,b200,s100,o100,v1/h.Vitals=x12,y-40,s100,o100,v1</c> (a control
/// moved, a button added, a readout moved): no spaces, quotes or semicolons, so a
/// console or a settings file carries it whole. Numbers are whole design units and whole percents. A part that is not understood
/// is left out and the rest still read, so a layout written by a later build still mostly stands.
/// </remarks>
public static class TouchLayoutCodec
{
    private const string Version = "v1";

    /// <summary>Writes a layout.</summary>
    /// <param name="layout">The layout.</param>
    /// <returns>The text; empty for the shipped layout.</returns>
    public static string Encode(TouchLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (layout.IsDefault)
        {
            return string.Empty;
        }

        StringBuilder text = new(Version);
        if (layout.StickMode == TouchStickMode.Fixed)
        {
            text.Append("/stick=fixed");
        }

        foreach (TouchControlId id in TouchLayout.AllControls)
        {
            if (layout.Controls.TryGetValue(id, out TouchPlacement p))
            {
                text.Append("/c.").Append(id).Append('=')
                    .Append(EdgeLetter(p.EdgeX)).Append(Whole(p.X)).Append(',')
                    .Append(EdgeLetter(p.EdgeY)).Append(Whole(p.Y)).Append(',')
                    .Append('s').Append(Percent(p.Scale)).Append(',')
                    .Append('o').Append(Percent(p.Opacity)).Append(',')
                    .Append('v').Append(p.Visible ? '1' : '0');
            }
        }

        foreach (TouchExtra extra in layout.Extras)
        {
            TouchPlacement p = extra.Placement;
            text.Append("/x.").Append(extra.Kind).Append('=')
                .Append(EdgeLetter(p.EdgeX)).Append(Whole(p.X)).Append(',')
                .Append(EdgeLetter(p.EdgeY)).Append(Whole(p.Y)).Append(',')
                .Append('s').Append(Percent(p.Scale)).Append(',')
                .Append('o').Append(Percent(p.Opacity)).Append(',')
                .Append('v').Append(p.Visible ? '1' : '0');
        }

        foreach (HudElement element in TouchLayout.Readouts)
        {
            if (layout.Hud.TryGetValue(element, out HudPlacement p))
            {
                text.Append("/h.").Append(element).Append('=')
                    .Append('x').Append(Whole(p.Dx)).Append(',')
                    .Append('y').Append(Whole(p.Dy)).Append(',')
                    .Append('s').Append(Percent(p.Scale)).Append(',')
                    .Append('o').Append(Percent(p.Opacity)).Append(',')
                    .Append('v').Append(p.Visible ? '1' : '0');
            }
        }

        return text.ToString();
    }

    /// <summary>Reads a layout; text that is not one gives the shipped layout.</summary>
    /// <param name="text">The text, or null.</param>
    /// <returns>The layout.</returns>
    public static TouchLayout Decode(string? text)
    {
        TouchLayout layout = new();
        if (string.IsNullOrWhiteSpace(text))
        {
            return layout;
        }

        string[] parts = text.Trim().Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts[0] != Version)
        {
            return layout;
        }

        for (int i = 1; i < parts.Length; i++)
        {
            int equals = parts[i].IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
            {
                continue;
            }

            string key = parts[i][..equals];
            string[] fields = parts[i][(equals + 1)..].Split(',');
            if (key == "stick")
            {
                layout.StickMode = fields[0] == "fixed" ? TouchStickMode.Fixed : TouchStickMode.Floating;
            }
            else if (key.StartsWith("c.", StringComparison.Ordinal)
                && Enum.TryParse(key[2..], ignoreCase: false, out TouchControlId id) && Enum.IsDefined(id)
                && ReadControl(fields) is { } control)
            {
                layout.Set(id, control);
            }
            else if (key.StartsWith("x.", StringComparison.Ordinal)
                && Enum.TryParse(key[2..], ignoreCase: false, out TouchControlId kind) && Enum.IsDefined(kind)
                && ReadControl(fields) is { } added)
            {
                // Added buttons come in the order they were written; each is one more of its control.
                int at = layout.Add(kind, added);
                if (at >= 0)
                {
                    layout.SetExtra(at, added);
                }
            }
            else if (key.StartsWith("h.", StringComparison.Ordinal)
                && Enum.TryParse(key[2..], ignoreCase: false, out HudElement element) && TouchLayout.Readouts.Contains(element)
                && ReadReadout(fields) is { } readout)
            {
                layout.Set(element, readout);
            }
        }

        return layout;
    }

    private static TouchPlacement? ReadControl(string[] fields)
    {
        if (fields.Length != 5 || fields[0].Length < 2 || fields[1].Length < 2
            || !Number(fields[0][1..], out float x) || !Number(fields[1][1..], out float y)
            || !Tagged(fields[2], 's', out float scale) || !Tagged(fields[3], 'o', out float opacity) || !Tagged(fields[4], 'v', out float visible))
        {
            return null;
        }

        HudEdgeX? edgeX = fields[0][0] switch { 'l' => HudEdgeX.Left, 'c' => HudEdgeX.Centre, 'r' => HudEdgeX.Right, _ => null };
        HudEdgeY? edgeY = fields[1][0] switch { 't' => HudEdgeY.Top, 'm' => HudEdgeY.Middle, 'b' => HudEdgeY.Bottom, _ => null };
        return edgeX is { } ex && edgeY is { } ey ? new TouchPlacement(ex, x, ey, y, scale / 100f, opacity / 100f, visible != 0f) : null;
    }

    private static HudPlacement? ReadReadout(string[] fields) =>
        fields.Length == 5
        && Tagged(fields[0], 'x', out float x) && Tagged(fields[1], 'y', out float y)
        && Tagged(fields[2], 's', out float scale) && Tagged(fields[3], 'o', out float opacity) && Tagged(fields[4], 'v', out float visible)
            ? new HudPlacement(x, y, scale / 100f, opacity / 100f, visible != 0f)
            : null;

    private static bool Tagged(string field, char tag, out float value)
    {
        value = 0f;
        return field.Length >= 2 && field[0] == tag && Number(field[1..], out value);
    }

    private static bool Number(string text, out float value) =>
        float.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);

    private static char EdgeLetter(HudEdgeX edge) => edge switch { HudEdgeX.Left => 'l', HudEdgeX.Centre => 'c', _ => 'r' };

    private static char EdgeLetter(HudEdgeY edge) => edge switch { HudEdgeY.Top => 't', HudEdgeY.Middle => 'm', _ => 'b' };

    private static string Whole(float value) => ((int)MathF.Round(value)).ToString(CultureInfo.InvariantCulture);

    private static string Percent(float value) => ((int)MathF.Round(value * 100f)).ToString(CultureInfo.InvariantCulture);
}
