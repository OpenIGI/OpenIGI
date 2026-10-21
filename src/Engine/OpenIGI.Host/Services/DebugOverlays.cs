using OpenIGI.Audio;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Frontend.Saves;
using OpenIGI.Frontend;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Host.Audio;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.DevTools;
using OpenIGI.Host.Diagnostics;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Host.Lighting;
using OpenIGI.Host.Menu;
using OpenIGI.Host.Titles;
using OpenIGI.Platform;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Lighting;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Presentation.Text;
using OpenIGI.Presentation;
using OpenIGI.Core.Configuration.Presets;
using OpenIGI.Rendering;
using OpenIGI.Scripting.Runtime;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using OpenIGI.UI;
using OpenIGI.Host.Services;

using System.Diagnostics;

namespace OpenIGI.Host.Services;

/// <summary>What the developer overlays read of the host.</summary>
internal interface IDebugOverlaysHost
{
    /// <summary>Gets what draws the world, or null with no level up.</summary>
    IWorldPresenter? World { get; }

    /// <summary>Gets the free camera, or null with no level up.</summary>
    FreeCamera? Camera { get; }

    /// <summary>Gets what draws the ground, or null for a level with none.</summary>
    ITerrainPresenter? Terrain { get; }

    /// <summary>Gets the level's placed collision, which the crosshair readout is cast against; null for none.</summary>
    ObjectCollisionIndex? Solids { get; }

    /// <summary>Gets the soldiers' bodies, or null before a mission has them.</summary>
    ISoldierBodies? Soldiers { get; }

    /// <summary>Gets a value indicating whether the frame is looked at through the player rather than the free camera.</summary>
    bool PlayerMode { get; }

    /// <summary>Gets the title's own player's viewpoint, or null for a title with none.</summary>
    IMissionViewpoint? Viewpoint { get; }

    /// <summary>Gets the panel the overlays are printed on, or null when it did not load.</summary>
    DebugOverlayRenderer? Overlay { get; }

    /// <summary>Gets the sun gizmo, which is drawn over everything while it is up.</summary>
    SunGizmo SunGizmo { get; }

    /// <summary>Gets the content layers, which the texture line reports on.</summary>
    ResourceOverrides Overrides { get; }

    /// <summary>Gets the resource registry, which counts what the overrides replaced.</summary>
    ResourceRegistry Resources { get; }

    /// <summary>Gets the window width the 2D pass projects to, never zero.</summary>
    int SafeWindowWidth { get; }

    /// <summary>Gets the window height the 2D pass projects to, never zero.</summary>
    int SafeWindowHeight { get; }

    /// <summary>Names the lamp under the crosshair, if any.</summary>
    /// <returns>The line to show, or null with no lamp aimed at.</returns>
    string? DescribeAimedLamp();
}

/// <summary>
/// The tools drawn over the frame: the sun gizmo, the F12 overlay with its crosshair readout, and the r_perflog panel, and
/// the one line that reports the world the first time it is drawn.
/// </summary>
/// <param name="device">The device the overlays are drawn through and whose timings they print.</param>
/// <param name="window">The window, whose size the panels are laid out against.</param>
/// <param name="options">The command line, which says whether this is a benchmark.</param>
/// <param name="logger">The log the world's first report goes to.</param>
/// <param name="host">What the overlays read of the host.</param>
internal sealed class DebugOverlays(
    IGraphicsDevice device, IGameWindow window, HostOptions options, IOpenIgiLogger logger, IDebugOverlaysHost host) : IHostService
{
    // The performance overlay's managed-allocation sampler. The GPU stage timers see none of this: a frame
    // that allocates megabytes churns Gen0 and, on the large-object heap, triggers the Gen2 collections that
    // stall the render thread -- a cost that shows only as a gap between the frame's wall time and the GPU's.
    private long _perfAllocBytes;
    private long _perfAllocTicks;
    private double _perfAllocRate;

    /// <inheritdoc/>
    public string Name => "Debug overlays";

    /// <summary>Draws the tools over the frame: the sun gizmo, the F12 overlay and its crosshair, and the r_perflog panel.</summary>
    /// <param name="state">What the frame loop keeps from one frame to the next.</param>
    /// <param name="eye">The eye the frame was drawn from, which the crosshair readout is cast from.</param>
    /// <param name="cameraIndoors">Whether the eye is under cover, as the weather was told.</param>
    public void Draw(FrameLoopState state, Camera3D eye, bool cameraIndoors)
    {
        if (host.World is not { } worldPresenter || host.Camera is not { } camera)
        {
            return;
        }

        ObjectCollisionIndex? solids = host.Solids;
        ISoldierBodies? soldiers = host.Soldiers;

        float debugPanelHeight = 0f;

        // The sun gizmo, over everything, whenever it is up -- not gated on the F12 overlay, since it
        // is a tool being used rather than an inspection panel.
        if (host.SunGizmo.Active)
        {
            device.BeginStageTiming(RenderStage.DebugRendering);
            state.Batch.Begin();
            host.SunGizmo.Draw(state.Batch);
            state.Batch.End();
            device.EndStageTiming();
        }

        // The F12 debug overlay and crosshair. Works in both the walking player and the free (flying)
        // camera: the state lines come from whichever is active, the crosshair readout and the reticle
        // are cast from the eye that is actually drawing.
        if (state.ShowDebug && host.Overlay is { IsReady: true } debugOverlay)
        {
            device.BeginStageTiming(RenderStage.DebugRendering);
            static string I(FormattableString s) => FormattableString.Invariant(s);

            Vector3d originU = new(
                eye.Position.X * WorldScene.UnitsPerMetre,
                eye.Position.Y * WorldScene.UnitsPerMetre,
                eye.Position.Z * WorldScene.UnitsPerMetre);
            Matrix3f orient = eye.Orientation;
            Vector3d fwd = new(orient.M31, orient.M32, orient.M33);
            const double ReachU = 300.0 * WorldScene.UnitsPerMetre;
            Vector3d endU = new(
                originU.X + (fwd.X * ReachU), originU.Y + (fwd.Y * ReachU), originU.Z + (fwd.Z * ReachU));

            string target = "target: (nothing / terrain / sky)";
            string? detect = null;
            double bestT = double.MaxValue;
            if (solids is not null
                && solids.TryIdentify(
                    originU,
                    endU,
                    out string objLabel,
                    out WorldPlacement? objPlacement,
                    out WorldRayHit objHit))
            {
                bestT = objHit.Distance;

                // The texture, from the RENDER mesh, because mat= cannot give it. The contact
                // above is on the collision mesh and names a game-material row -- SoftMetal,
                // Glass -- which drives bullets and decals and is read by nothing in the
                // renderer. What the renderer reads is materials.json, keyed by texture name,
                // and until this line there was no way to learn that name from inside the game:
                // somebody wanting to say "this pane is glass" had to guess which of a level's
                // hundred numbered textures they were looking at.
                string surface =
                    objPlacement is not null
                    && RenderSurfaceProbe.TryFindTexture(
                        objPlacement,
                        originU,
                        new Vector3d(endU.X - originU.X, endU.Y - originU.Y, endU.Z - originU.Z),
                        out string objTexture,
                        out _)
                        ? objTexture
                        : "?";

                target = I($"target: OBJECT {objLabel} @ {objHit.Distance / WorldScene.UnitsPerMetre:F1}m ({objHit.Position.X:F0}, {objHit.Position.Y:F0}, {objHit.Position.Z:F0}) mat={objHit.Material} tex={surface}");
            }

            if (soldiers is not null
                && soldiers.TryPick(originU, fwd, ReachU, out string botName, out double botT, out Vector3d feetU, out int botZone)
                && botT < bestT)
            {
                // The floor the game's collision puts under this soldier's feet — so a sunk guard shows
                // its feet Z below the floor Z, and a correct one shows them level.
                string floor = "?";
                if (solids is not null)
                {
                    Vector3d fTop = new(feetU.X, feetU.Y, feetU.Z + 2048.0);
                    Vector3d fBot = new(feetU.X, feetU.Y, feetU.Z - (10.0 * WorldScene.UnitsPerMetre));
                    if (solids.TryIdentify(fTop, fBot, out string floorLbl, out WorldRayHit floorHit))
                    {
                        floor = I($"{floorHit.Position.Z:F0} ({floorLbl} mat{floorHit.Material}) Δ={(feetU.Z - floorHit.Position.Z) / WorldScene.UnitsPerMetre:F2}m");
                    }
                }

                string describedBot = ModelNameRegistry.DescribeModel(botName);
                target = I($"target: SOLDIER {describedBot} zone={HumanDamage.ZoneName(botZone)} feetZ={feetU.Z:F0} floorZ={floor}");
            }

            // Whatever soldier the crosshair is on, ask its brain which detection gate is (not) passing
            // against the point the guards actually test -- the player's own eye in engine units, the same
            // point soldiers.Tick is fed.
            Vector3d watchedEye = host.PlayerMode && host.Viewpoint?.EyeUnits is { } playerEye
                ? playerEye
                : camera.Position;
            if (soldiers is not null
                && soldiers.TryDetectionInfo(originU, fwd, ReachU, watchedEye, out string detectInfo))
            {
                detect = detectInfo;
            }

            List<string> lines;
            if (host.PlayerMode && host.Viewpoint?.DebugLines() is { } walking)
            {
                lines = [.. walking, target];
            }
            else
            {
                Vector3d cm = camera.Position;
                lines =
                [
                    "mode: FREE CAM",
                    I($"pos units ({cm.X * WorldScene.UnitsPerMetre:F0}, {cm.Y * WorldScene.UnitsPerMetre:F0}, {cm.Z * WorldScene.UnitsPerMetre:F0})"),
                    I($"pos m     ({cm.X:F2}, {cm.Y:F2}, {cm.Z:F2})"),
                    I($"yaw {camera.Yaw * 180f / MathF.PI:F1}  pitch {camera.Pitch * 180f / MathF.PI:F1}"),
                    target,
                ];
            }

            if (detect is not null)
            {
                lines.Add(detect);
            }

            // Which of the level's lamps the crosshair is on, if any -- so the switch keys can be
            // aimed rather than guessed at, and so a press that appeared to do nothing can be told
            // apart from a press that landed on a lamp whose light is off screen.
            if (host.DescribeAimedLamp() is { } aimedLamp)
            {
                lines.Add(aimedLamp);
            }

            device.Set2DProjection(host.SafeWindowWidth, host.SafeWindowHeight);
            state.Batch.Begin();
            debugPanelHeight = debugOverlay.Draw(state.Batch, lines, window.Width, window.Height);

            // A centre reticle so the target readout can be aimed — the crosshair the free camera lacks.
            float cx = window.Width * 0.5f;
            float cy = window.Height * 0.5f;
            const float Len = 9f;
            const float Th = 1f;
            static Bgra32 White() => new(255, 255, 255, 220);
            void Bar(float x0, float y0, float x1, float y1) => state.Batch.DrawQuad(
                TextureHandle.None,
                new Vertex2D(x0, y0, 0f, 0f, White()),
                new Vertex2D(x1, y0, 1f, 0f, White()),
                new Vertex2D(x1, y1, 1f, 1f, White()),
                new Vertex2D(x0, y1, 0f, 1f, White()));
            Bar(cx - Len, cy - Th, cx + Len, cy + Th);
            Bar(cx - Th, cy - Len, cx + Th, cy + Len);

            state.Batch.End();
            device.EndStageTiming();
        }

        // r_perflog, its own overlay rather than a section of F12's: it answers a different question
        // and is read while the game is being played rather than while it is being inspected. Top
        // right, so the two can be up together. When F12 is also enabled, place this panel below
        // the measured F12 panel instead of letting both panels claim the same top-right corner.
        if (RenderCVars.PerformanceLog.Value
            && !options.IsBenchmark
            && host.Overlay is { IsReady: true } performanceOverlay)
        {
            device.BeginStageTiming(RenderStage.DebugRendering);
            static string I(FormattableString s) => FormattableString.Invariant(s);

            IReadOnlyList<RenderStageTiming> timings = device.StageTimings();
            GpuFrameTiming gpu = device.GpuTiming();
            FramePerformanceStatistics frame = state.FramePerformance.Statistics();
            GraphicsPreset preset = GraphicsPresetRegistry.Default.Current;
            List<string> perf =
            [
                I($"{preset}  {window.Width}x{window.Height}  vsync {(device.VSyncEnabled ? "on" : "off")}  indoor {(cameraIndoors ? "yes" : "no")}"),

                // Which of the two shadow systems is actually drawing, spelled out with the keys
                // that move it. Judging a shadow in a capture means knowing which one
                // produced it, and the answer is two cvars deep -- r_rt_shadows does nothing
                // while r_rt is off, and neither means anything with shadows turned off.
                I($"shadow   {ShadowModeLabel()}"),
                I($"texture  {TextureModeLabel()}"),
                I($"fps {frame.FramesPerSecond:0.0}  frame {frame.AverageMilliseconds:0.00} ms  1% {frame.OnePercentLowFramesPerSecond:0.0}"),

                // The whole GPU frame measured end to end, against the sum of the stages listed
                // below it. Everything else on this overlay is built from those stages, so a pass
                // recorded outside every one of them shows up nowhere -- and a millisecond of lamp
                // shadow did exactly that for a whole session while this panel read as though the
                // lamps were free. "lost" is that difference: a few hundredths is the timestamps'
                // own overhead, anything more is real work with nobody's name on it.
                MeasuredFrameLine(device, gpu),

                "gpu ms   avg    p95   peak  budget",
            ];
            foreach (RenderStageTiming timing in timings)
            {
                if (!timing.Active)
                {
                    perf.Add($"{timing.Stage,-24} off");
                    continue;
                }

                // The budget beside the measurement, because a number without the number it was
                // supposed to beat is a number nobody acts on.
                string budget = RenderBudget.For(timing.Stage) is { } allowed
                    ? I($" /{allowed:0.0}{(timing.Milliseconds > allowed ? " OVER" : string.Empty)}")
                    : string.Empty;

                perf.Add(I($"{timing.Stage,-24} {timing.Milliseconds,5:0.00} {timing.P95Milliseconds,6:0.00} {timing.PeakMilliseconds,6:0.00}{budget}"));
            }

            perf.Add(gpu.SampleCount > 0
                ? I($"{"gpu total",-24} {gpu.Milliseconds,5:0.00} {gpu.P95Milliseconds,6:0.00} {gpu.PeakMilliseconds,6:0.00}")
                : "measuring...");

            if (RenderCVars.PerformanceLogPasses.Value is > 0 and int shown)
            {
                IReadOnlyList<RenderPassTiming> passes = device.PassTimings();
                for (int p = 0; p < passes.Count && p < shown; p++)
                {
                    RenderPassTiming pass = passes[p];
                    perf.Add(I($"  {pass.Label,-22} {pass.Milliseconds,5:0.00}        {pass.PeakMilliseconds,6:0.00}"));
                }
            }

            // The CPU side the GPU timers cannot see. cpu is how much longer the frame's wall time
            // is than the GPU's -- submission, game logic and GC that did not overlap the GPU. alloc
            // is the managed allocation rate, eased so it is readable; a high figure is what feeds the
            // GC collections whose pauses read as the frame's stutter, and the LOH share of it drives
            // the Gen2 collections that stall hardest. gc shows the Gen0/1/2 collection counts so a
            // climbing Gen2 is visible while playing rather than only under a profiler.
            long allocNow = GC.GetTotalAllocatedBytes();
            long nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_perfAllocTicks != 0)
            {
                double seconds = (nowTicks - _perfAllocTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
                if (seconds > 1e-4)
                {
                    double sample = (allocNow - _perfAllocBytes) / seconds / (1024.0 * 1024.0);
                    _perfAllocRate = (_perfAllocRate * 0.9) + (sample * 0.1);
                }
            }

            _perfAllocBytes = allocNow;
            _perfAllocTicks = nowTicks;
            double cpuMilliseconds = Math.Max(0.0, frame.AverageMilliseconds - gpu.Milliseconds);
            perf.Add(I($"cpu {cpuMilliseconds,5:0.00} ms   alloc {_perfAllocRate,6:0} MB/s   gc {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}"));

            device.Set2DProjection(host.SafeWindowWidth, host.SafeWindowHeight);
            state.Batch.Begin();
            float performanceTopOffset = debugPanelHeight > 0f
                ? debugPanelHeight + DebugOverlayRenderer.PanelGapPixels(window.Width, window.Height)
                : 0f;
            performanceOverlay.DrawTopRight(
                state.Batch,
                perf,
                window.Width,
                window.Height,
                performanceTopOffset);
            state.Batch.End();
            device.EndStageTiming();
        }

        if (!state.ReportedWorld)
        {
            state.ReportedWorld = true;
            List<string> world = [];
            worldPresenter.Describe(world);
            logger.Information("World", $"{string.Join("; ", world)}, from {camera.Position}.");

            if (host.Terrain is { } terrainPresenter)
            {
                // The presenter formats its own counters: what is worth reporting about a
                // streamed octree is not what is worth reporting about a grid of tiles.
                List<string> terrain = [];
                terrainPresenter.Describe(terrain);
                logger.Information("Terrain", string.Join("; ", terrain));
            }
        }
    }

    /// <summary>The overlay's line comparing the measured GPU frame against the sum of its stages.</summary>
    /// <remarks>
    /// Reports "not measured" rather than a zero on a backend with no frame bracket, because a zero
    /// difference and an absent measurement mean opposite things and the whole point of the line is that
    /// it can be believed.
    /// </remarks>
    private static string MeasuredFrameLine(IGraphicsDevice frameDevice, GpuFrameTiming summed)
    {
        GpuFrameTiming measured = frameDevice.GpuMeasuredTiming();

        if (measured.Milliseconds <= 0.0)
        {
            return FormattableString.Invariant(
                $"gpu      {summed.Milliseconds:0.00} ms summed   (frame not measured)");
        }

        return FormattableString.Invariant(
            $"gpu      {measured.Milliseconds:0.00} measured   {summed.Milliseconds:0.00} summed   lost {measured.Milliseconds - summed.Milliseconds:0.00}");
    }

    /// <summary>What the remaster's two asset features are doing, and the keys that move them.</summary>
    /// <remarks>
    /// Both are comparison switches, so the thing worth saying is which side of the comparison is on
    /// screen right now — a frame is worth nothing as evidence if you cannot tell which picture it is.
    /// Spelled out with the keys for the same reason <see cref="ShadowModeLabel"/> is: the answer is two
    /// variables deep, and "no overrides present" looks exactly like "overrides turned off" in the picture.
    /// </remarks>
    private string TextureModeLabel()
    {
        int layers = host.Overrides.Layers.Count(layer => layer.Enabled && layer.Wins > 0);
        string upscaled = host.Overrides.Count == 0
            ? "original (no content)"
            : RenderCVars.OverridesEnabled.Value
                ? $"{host.Resources.OverriddenCount} override(s) from {layers} layer(s) [F8 for the originals]"
                : $"original [F8 for the {host.Overrides.Count} override(s)]";

        // Zero at every preset but high, so "off" here is usually the authored answer rather than a key
        // somebody pressed -- worth distinguishing, because the maps look identical to absent when the
        // strength is nothing.
        string detail = RenderCVars.MaterialDetail.Value <= 0f
            ? "no detail (r_material_detail is 0)"
            : RenderCVars.MaterialDetailEnabled.Value
                ? $"detail {RenderCVars.MaterialDetail.Value:0.##} [F9 off]"
                : $"detail off [F9 back to {RenderCVars.MaterialDetail.Value:0.##}]";

        return $"{upscaled}, {detail}";
    }

    /// <summary>Names the shadow system actually drawing this frame, for the performance overlay.</summary>
    /// <returns>The mode, and the keys that move it.</returns>
    /// <remarks>
    /// Reads the same three conditions the renderer does, in the same order, rather than reporting a
    /// cvar: r_rt_shadows means nothing while r_rt is off, r_rt means nothing on a backend that cannot
    /// trace, and neither means anything with shadows turned off. Reporting the cvar rather than the
    /// consequence is how someone ends up comparing two shadows they believe came from different systems.
    /// </remarks>
    private string ShadowModeLabel()
    {
        bool enabled = RenderCVars.Shadows.Value > 0
            && RenderCVars.OffscreenScene.Value
            && RenderCVars.Deferred.Value;

        if (!enabled)
        {
            return "off (r_shadows 0)";
        }

        if (!device.Capabilities.SupportsRayQuery)
        {
            return FormattableString.Invariant(
                $"cascaded map, {RenderCVars.ShadowCascades.Value} cascades at {RenderCVars.Shadows.Value} (no ray tracing on this backend)");
        }

        if (!RenderCVars.HardwareRayTracing.Value)
        {
            return FormattableString.Invariant(
                $"cascaded map, {RenderCVars.ShadowCascades.Value} cascades at {RenderCVars.Shadows.Value}  [F6 traces everything]");
        }

        if (!RenderCVars.TracedSunShadows.Value)
        {
            return FormattableString.Invariant(
                $"cascaded map, {RenderCVars.ShadowCascades.Value} cascades at {RenderCVars.Shadows.Value}  [F7 traces the sun; rest of the frame is traced]");
        }

        return FormattableString.Invariant(
            $"ray query, sun disc {RenderCVars.SunAngle.Value:0.0} deg  [F7 back to the map, F6 back to software]");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // The panel is the host's, released with the level; nothing here holds a resource.
    }
}
