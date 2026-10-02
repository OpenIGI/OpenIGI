using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

internal sealed partial class GameHost
{
    // Whether the lite profile's overrides are in force, so they are applied and cleared once per change
    // rather than every frame.
    private bool _backdropLiteApplied;

    /// <summary>
    /// Whether the level on screen is the menu's backdrop and is to be drawn without the preset's expensive
    /// parts. See <see cref="RenderCVars.MenuBackdropLite"/>.
    /// </summary>
    /// <remarks>
    /// A backdrop is the level when no mission is: <c>_backdrop</c> is dropped the moment one is promoted
    /// or a mission loads cold, so Play turns this off on the same frame the level becomes the mission.
    /// </remarks>
    private bool BackdropLite =>
        RenderCVars.MenuBackdropLite.Value && _activeMission is null && _backdrop is not null;

    /// <summary>Applies or clears the lite profile to match <see cref="BackdropLite"/>. Cheap; call freely.</summary>
    /// <remarks>
    /// <para>
    /// What the lite profile removes is everything a backdrop swap had to BUILD on the render thread --
    /// the surface-cache capture, which ends in a device wait, and the distance-field upload -- and the
    /// per-frame work whose only output is detail the blur takes away: shadows, traces and screen-space
    /// gathers. The level then draws on its own bakes, which is what it looked like before any of that.
    /// </para>
    /// <para>
    /// Through overrides rather than assignments, deliberately. These are preset-managed, archived
    /// variables: writing them would flip the preset to Custom while the menu is up and save the menu's
    /// numbers as the player's own the next time the settings are written. An override is seen only by
    /// the code reading <c>Value</c>.
    /// </para>
    /// <para>
    /// Clearing it is all Play has to do. The per-frame <see cref="EnsureRealtimeGiStructures"/> sees the
    /// structures wanted again and builds them, and the mission's warm-up holds the picture until they are
    /// ready (<see cref="RealtimeGiPending"/>), so the cost moves behind the going-in sequence.
    /// </para>
    /// <para>
    /// Nothing in a lite backdrop traces, so nothing is admitted to the traced world either: every mesh it
    /// took in would be a structure built for a level that is gone at the next swap.
    /// </para>
    /// </remarks>
    private void UpdateBackdropProfile()
    {
        bool lite = BackdropLite;

        // Every time, not only on a change: switching renderer builds a new device, which starts out
        // tracing whatever the profile says.
        _device.SetTracePaused(lite);

        if (lite == _backdropLiteApplied)
        {
            return;
        }

        _backdropLiteApplied = lite;
        if (lite)
        {
            RenderCVars.Lighting.Override(LightingModel.Baked);
            RenderCVars.SurfaceCache.Override(false);
            RenderCVars.DistanceFields.Override(false);
            RenderCVars.Shadows.Override(0);
            RenderCVars.LocalLightShadows.Override(0);
            RenderCVars.TracedSunShadows.Override(false);
            RenderCVars.TracedLampShadows.Override(false);
            RenderCVars.TracedReflections.Override(false);
            RenderCVars.Reflections.Override(0);
            RenderCVars.ScreenGather.Override(0f);
            RenderCVars.RadianceCache.Override(0);
        }
        else
        {
            RenderCVars.Lighting.ClearOverride();
            RenderCVars.SurfaceCache.ClearOverride();
            RenderCVars.DistanceFields.ClearOverride();
            RenderCVars.Shadows.ClearOverride();
            RenderCVars.LocalLightShadows.ClearOverride();
            RenderCVars.TracedSunShadows.ClearOverride();
            RenderCVars.TracedLampShadows.ClearOverride();
            RenderCVars.TracedReflections.ClearOverride();
            RenderCVars.Reflections.ClearOverride();
            RenderCVars.ScreenGather.ClearOverride();
            RenderCVars.RadianceCache.ClearOverride();
        }

        _logger.Information(
            "Preview",
            lite
                ? "The backdrop is drawn lite: no real-time GI, shadows, traces or screen-space gathers."
                : "The full preset is back for the level.");
    }
}
