using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Ai;
using OpenIGI.Host.Menu;
using OpenIGI.Rendering;
using OpenIGI.UI.Rhi;

namespace OpenIGI.Host.Services;

/// <summary>
/// The level behind the menu: the preview on screen, the soldiers it casts into the shadow map, the lite profile it is
/// drawn with, and the frosted glass the shell samples from it.
/// </summary>
/// <param name="device">The device the backdrop is drawn on.</param>
/// <param name="logger">Where profile changes are reported.</param>
internal sealed class MenuBackdrop(IGraphicsDevice device, IOpenIgiLogger logger) : IHostService
{
    // Whether the lite profile's overrides are in force, so they are applied and cleared once per change rather than
    // every frame.
    private bool _liteApplied;

    // The interface's view of the device's blurred frame, made again only when the device's levels or the window
    // change.
    private BackdropBlur? _blur;
    private RhiBackdrop? _glass;
    private int _glassWidth;
    private int _glassHeight;

    /// <summary>Gets the preview on screen, if one is.</summary>
    /// <remarks>
    /// Held rather than used: the renderers built from it read models, textures and the sky through ITS resource
    /// registry and file system, so it has to outlive the handover. Replaced whole when the next backdrop arrives.
    /// </remarks>
    public MissionPreview? Preview { get; private set; }

    /// <summary>Gets the mission on screen behind the menu, if one is.</summary>
    public int? MissionId => Preview?.MissionId;

    /// <summary>Gets the previewed mission's soldiers, cast into the shadow map but never drawn.</summary>
    /// <remarks>
    /// Built behind the menu so the driver has already met a skinned vertex layout by the time Play is pressed, and
    /// handed straight over when the backdrop is promoted, so the played mission never builds them at all.
    /// </remarks>
    public ISoldierBodies? Soldiers { get; private set; }

    /// <inheritdoc/>
    public string Name => "Menu backdrop";

    /// <summary>Puts a preview on screen, in place of the last.</summary>
    /// <param name="preview">The preview.</param>
    public void Show(MissionPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        Preview = preview;
    }

    /// <summary>Stops the level being scenery, once a real mission is up.</summary>
    /// <remarks>
    /// On a cold start that releases the previewed level's packs outright. On a promotion it releases nothing: the
    /// preview IS the mission now, reachable through the renderers built against it.
    /// </remarks>
    public void Clear() => Preview = null;

    /// <summary>Replaces the backdrop's soldiers.</summary>
    /// <param name="soldiers">The new soldiers, or <see langword="null"/> for none.</param>
    public void ArmSoldiers(ISoldierBodies? soldiers)
    {
        Soldiers?.Dispose();
        Soldiers = soldiers;
    }

    /// <summary>Hands the soldiers over to a promoted mission, which owns them from here.</summary>
    /// <returns>The soldiers, or <see langword="null"/> where none were built.</returns>
    public ISoldierBodies? TakeSoldiers()
    {
        ISoldierBodies? soldiers = Soldiers;
        Soldiers = null;
        return soldiers;
    }

    /// <summary>Frees the soldiers.</summary>
    public void ReleaseSoldiers() => ArmSoldiers(null);

    /// <summary>
    /// Gets whether the level on screen is the menu's backdrop and is to be drawn without the preset's expensive parts.
    /// See <see cref="FrameCVars.MenuBackdropLite"/>.
    /// </summary>
    /// <param name="missionActive">Whether a mission is being played.</param>
    /// <returns><see langword="true"/> for the lite profile.</returns>
    /// <remarks>
    /// A backdrop is the level when no mission is: the preview is dropped the moment one is promoted or a mission
    /// loads cold, so Play turns this off on the same frame the level becomes the mission.
    /// </remarks>
    public bool IsLite(bool missionActive) =>
        FrameCVars.MenuBackdropLite.Value && !missionActive && Preview is not null;

    /// <summary>Applies or clears the lite profile to match <see cref="IsLite"/>. Cheap; call freely.</summary>
    /// <param name="missionActive">Whether a mission is being played.</param>
    /// <remarks>
    /// <para>
    /// What the lite profile removes is everything a backdrop swap had to BUILD on the render thread -- the
    /// surface-cache capture, which ends in a device wait, and the distance-field upload -- and the per-frame work
    /// whose only output is detail the blur takes away: shadows, traces and screen-space gathers. The level then draws
    /// on its own bakes, which is what it looked like before any of that.
    /// </para>
    /// <para>
    /// Through overrides rather than assignments, deliberately. These are preset-managed, archived variables: writing
    /// them would flip the preset to Custom while the menu is up and save the menu's numbers as the player's own the
    /// next time the settings are written. An override is seen only by the code reading <c>Value</c>.
    /// </para>
    /// <para>
    /// Clearing it is all Play has to do. The per-frame <see cref="GlobalIllumination.EnsureStructures"/> sees the
    /// structures wanted again and builds them, and the mission's warm-up holds the picture until they are ready
    /// (<see cref="GlobalIllumination.Pending"/>), so the cost moves behind the going-in sequence.
    /// </para>
    /// <para>
    /// Nothing in a lite backdrop traces, so nothing is admitted to the traced world either: every mesh it took in
    /// would be a structure built for a level that is gone at the next swap.
    /// </para>
    /// </remarks>
    public void UpdateProfile(bool missionActive)
    {
        bool lite = IsLite(missionActive);

        // Every time, not only on a change: switching renderer builds a new device, which starts out tracing whatever
        // the profile says.
        device.SetTracePaused(lite);

        if (lite == _liteApplied)
        {
            return;
        }

        _liteApplied = lite;
        if (lite)
        {
            LightingCVars.Lighting.Override(LightingModel.Baked);
            SurfaceCacheCVars.SurfaceCache.Override(false);
            RayTracingCVars.DistanceFields.Override(false);
            ShadowCVars.Shadows.Override(0);
            LocalLightCVars.LocalLightShadows.Override(0);
            RayTracingCVars.TracedSunShadows.Override(false);
            RayTracingCVars.TracedLampShadows.Override(false);
            RayTracingCVars.TracedReflections.Override(false);
            ReflectionCVars.Reflections.Override(0);
            ScreenGatherCVars.ScreenGather.Override(0f);
            ScreenGatherCVars.RadianceCache.Override(0);
        }
        else
        {
            LightingCVars.Lighting.ClearOverride();
            SurfaceCacheCVars.SurfaceCache.ClearOverride();
            RayTracingCVars.DistanceFields.ClearOverride();
            ShadowCVars.Shadows.ClearOverride();
            LocalLightCVars.LocalLightShadows.ClearOverride();
            RayTracingCVars.TracedSunShadows.ClearOverride();
            RayTracingCVars.TracedLampShadows.ClearOverride();
            RayTracingCVars.TracedReflections.ClearOverride();
            ReflectionCVars.Reflections.ClearOverride();
            ScreenGatherCVars.ScreenGather.ClearOverride();
            ScreenGatherCVars.RadianceCache.ClearOverride();
        }

        logger.Information(
            "Preview",
            lite
                ? "The backdrop is drawn lite: no real-time GI, shadows, traces or screen-space gathers."
                : "The full preset is back for the level.");
    }

    /// <summary>Gets the menu's frosted glass: the device's blurred frame, restated in the interface's units.</summary>
    /// <param name="blur">This frame's blur, as the device returned it.</param>
    /// <param name="width">The window's width.</param>
    /// <param name="height">The window's height.</param>
    /// <returns>The backdrop the shell's renderer samples, or <see langword="null"/> when there is none.</returns>
    public RhiBackdrop? Glass(BackdropBlur blur, int width, int height)
    {
        if (!ReferenceEquals(blur, _blur) || _glassWidth != width || _glassHeight != height)
        {
            _blur = blur;
            _glassWidth = width;
            _glassHeight = height;
            _glass = RhiBackdrop.From(blur, width, height);
        }

        return _glass;
    }

    /// <inheritdoc/>
    public void Dispose() => ReleaseSoldiers();
}
