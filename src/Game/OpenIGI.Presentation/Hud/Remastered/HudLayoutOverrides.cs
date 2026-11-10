using OpenIGI.Presentation.Hud.Remastered.Touch;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <summary>
/// A player's own arrangement of the display's readouts, as the display is drawn with it: each moved, resized, faded or left
/// out as their layout (<see cref="TouchLayout"/>) says, and where each came to stand, for the layout's editor to take hold of.
/// </summary>
/// <remarks>
/// The arrangement is a touch screen's: the host sets <see cref="Current"/> while one is in use and clears it otherwise, so a
/// display at a desk stands where its title's theme puts it. Distances are design units of a 1080-line screen, the touch
/// controls' own, so one layout moves both by the same measure on either title.
/// </remarks>
public sealed class HudLayoutOverrides
{
    /// <summary>Initializes a new instance of the <see cref="HudLayoutOverrides"/> class.</summary>
    /// <param name="layout">The layout the readouts follow.</param>
    public HudLayoutOverrides(TouchLayout layout)
    {
        Layout = layout ?? throw new ArgumentNullException(nameof(layout));
    }

    /// <summary>Gets or sets the arrangement the display is drawn with, or <see langword="null"/> for each title's own.</summary>
    public static HudLayoutOverrides? Current { get; set; }

    /// <summary>Gets or sets the layout the readouts follow.</summary>
    public TouchLayout Layout { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the layout's editor is up: a readout left out is then drawn faint rather than not
    /// at all, and one with nothing to show is given a box where it would stand.
    /// </summary>
    public bool Editing { get; set; }

    /// <summary>Gets where each readout stood on the screen at the last draw, in pixels.</summary>
    public Dictionary<HudElement, UiRect> Boxes { get; } = [];

    /// <summary>Gets the readouts that had nothing to show at the last draw, whose boxes only stand in for them.</summary>
    public HashSet<HudElement> Unshown { get; } = [];

    /// <summary>How many pixels one of a layout's design units is on a screen.</summary>
    /// <param name="height">The screen's height in pixels.</param>
    /// <returns>The pixels.</returns>
    public static float Unit(float height) => height / 1080f;

    /// <summary>The box a readout with nothing to show would stand in, from its anchor, as its widget grows from it.</summary>
    /// <param name="element">The readout.</param>
    /// <param name="anchor">Its anchor on the screen, in pixels.</param>
    /// <param name="px">How many pixels one of the display's design pixels is, at the readout's size.</param>
    /// <param name="centred">Whether the theme centres it on its anchor, for the messages.</param>
    /// <returns>The box, in pixels.</returns>
    public static UiRect NominalBox(HudElement element, UiVec2 anchor, float px, bool centred) => element switch
    {
        HudElement.Vitals => new UiRect(anchor.X, anchor.Y - (146f * px), 72f * px, 146f * px),
        HudElement.Weapon => new UiRect(anchor.X - (232f * px), anchor.Y - (146f * px), 232f * px, 146f * px),
        HudElement.Inventory => new UiRect(anchor.X, anchor.Y, 300f * px, 112f * px),
        HudElement.Actions => new UiRect(anchor.X - (34f * px), anchor.Y - (4f * px), 68f * px, 64f * px),
        HudElement.Progress => new UiRect(anchor.X - (186f * px), anchor.Y - (6f * px), 372f * px, 22f * px),
        HudElement.Timer => new UiRect(anchor.X - (52f * px), anchor.Y, 104f * px, 32f * px),
        _ => new UiRect(centred ? anchor.X - (190f * px) : anchor.X, anchor.Y, 380f * px, 26f * px),
    };
}
