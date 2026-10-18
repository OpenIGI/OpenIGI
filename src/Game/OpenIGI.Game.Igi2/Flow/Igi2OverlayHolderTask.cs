using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>The overlays a holder can put up, <c>OVERLAY_*</c>.</summary>
public enum Igi2OverlayType
{
    /// <summary>The binoculars' view, <c>BinocularOverlay</c>.</summary>
    Binocular = 0,

    /// <summary>A zoomed view, <c>ZoomOverlay</c>.</summary>
    Zoom = 1,

    /// <summary>The sniper scope, <c>SniperOverlay</c>.</summary>
    Sniper = 2,

    /// <summary>The laser designator's view, <c>LaserDesignatorOverlay</c>.</summary>
    LaserDesignator = 3,
}

/// <summary>
/// The overlay the scripts have put on the game screen, and whether the view is infrared (<c>isInfraEnabled</c>, <c>isDrawInfra</c>),
/// which the HUD draws.
/// </summary>
public sealed class Igi2ScreenOverlay
{
    /// <summary>Gets the overlay up, or <see langword="null"/> for none.</summary>
    public Igi2OverlayType? Overlay { get; private set; }

    /// <summary>Gets a value indicating whether a binocular overlay shows the distance.</summary>
    public bool ShowsDistance { get; private set; }

    /// <summary>Gets or sets a value indicating whether the view is drawn infrared.</summary>
    public bool IsInfrared { get; set; }

    /// <summary>Puts an overlay up.</summary>
    /// <param name="overlay">The overlay, or <see langword="null"/> for one of no known type.</param>
    /// <param name="showsDistance">Whether a binocular overlay shows the distance.</param>
    public void Show(Igi2OverlayType? overlay, bool showsDistance)
    {
        Overlay = overlay;
        ShowsDistance = showsDistance;
    }

    /// <summary>Takes the overlay down and the infrared view with it.</summary>
    public void Hide()
    {
        Overlay = null;
        ShowsDistance = false;
        IsInfrared = false;
    }

    /// <summary>Registers the overlay types' constants.</summary>
    /// <param name="natives">The namespace.</param>
    public static void RegisterConstants(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);
        natives.RegisterConstant("OVERLAY_ZOOM", (int)Igi2OverlayType.Zoom);
        natives.RegisterConstant("OVERLAY_LASERDESIGNATOR", (int)Igi2OverlayType.LaserDesignator);
    }
}

/// <summary>
/// A scripted overlay, I.G.I. 2's <c>OverlayHolder</c> (overlayholder.c): while its visible expression holds, its overlay is up on the
/// game screen, and its infrared expression, where it has one, says whether the view is infrared.
/// </summary>
/// <remarks>
/// <c>OverlayHolder_RunHandler</c>: the visible expression, chopped, where it has one, says whether it is up; up, the infrared expression
/// sets the view's infrared, and an overlay not yet up is put up, of its type, a binocular one showing the distance as asked; down, its
/// overlay is taken down and the infrared view with it. Its children do not run.
/// </remarks>
public sealed partial class Igi2OverlayHolderTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "OverlayHolder";

    private LevelExpression? _visible;
    private LevelExpression? _infrared;
    private bool _isUp;

    /// <summary>Gets or sets the expression that puts it up.</summary>
    [ScriptParam(ScriptParamType.VarString, "Visible expression")]
    public string VisibleExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that makes the view infrared.</summary>
    [ScriptParam(ScriptParamType.VarString, "Infrared expression")]
    public string InfraredExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets its overlay, an <c>OVERLAY_*</c> value.</summary>
    [ScriptParam(ScriptParamType.EnumInt32, "Overlay type")]
    public int OverlayType { get; set; }

    /// <summary>Gets or sets a value indicating whether a binocular overlay shows the distance.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Show Distance")]
    public bool ShowDistance { get; set; } = true;

    /// <summary>Gets a value indicating whether its visible expression holds, <c>isVisible</c>.</summary>
    public bool IsVisible { get; private set; }

    /// <summary>Gets or sets the game screen's overlay.</summary>
    public Igi2ScreenOverlay? Screen { get; set; }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _visible ??= Igi2LevelExpressions.Create(VisibleExpression, Name, level.Natives);
        if (!_visible.IsEmpty)
        {
            IsVisible = LevelExpression.Truncate(level.Evaluate(_visible)) != 0;
        }

        if (!IsVisible)
        {
            if (_isUp)
            {
                Screen?.Hide();
                _isUp = false;
            }

            return;
        }

        _infrared ??= Igi2LevelExpressions.Create(InfraredExpression, Name, level.Natives);
        if (!_infrared.IsEmpty && Screen is { } infraredScreen)
        {
            infraredScreen.IsInfrared = LevelExpression.Truncate(level.Evaluate(_infrared)) != 0;
        }

        if (!_isUp)
        {
            Screen?.Show((uint)OverlayType <= (uint)Igi2OverlayType.LaserDesignator ? (Igi2OverlayType)OverlayType : null, ShowDistance);
            _isUp = true;
        }
    }
}
