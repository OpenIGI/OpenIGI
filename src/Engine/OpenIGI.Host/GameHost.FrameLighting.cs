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

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    // The air the running level is played in, when the title authors one. Null for a game that does
    // not, which is what keeps Project I.G.I. out of this entirely.
    private SceneAtmosphere? _atmosphere;

    /// <summary>Gets the air the world wears when the retail fog is on, preferring the level's own.</summary>
    /// <param name="fromGlobalLight">The fog this band of Project I.G.I.'s <c>GlobalLight</c> publishes.</param>
    /// <returns>The fog to draw into.</returns>
    /// <remarks>
    /// <para>
    /// <c>GlobalLight</c> is the FIRST game's task, and it is the only thing the frame used to ask. So
    /// on the vanilla tier -- the one tier that fogs through <see cref="SceneFog"/> rather than through
    /// the froxel volume -- I.G.I. 2's world was drawn with no fog at all: its own authored air was set
    /// once when the mission loaded and then overwritten by an empty <c>GlobalLight</c> on the first
    /// frame, and every frame after.
    /// </para>
    /// <para>
    /// It also hid the ground's per-pass fog colour, which had nothing to tint. That is worth saying
    /// because the two faults look identical from the outside -- far hills that are not hazed -- and
    /// fixing either alone leaves the other in place.
    /// </para>
    /// <para>
    /// Project I.G.I. cannot reach the new branch: <c>ITitlePresentationProvider.ReadAtmosphere</c>
    /// defaults to null and only the sequel answers it, so <see cref="_atmosphere"/> is null for the
    /// first game and this returns exactly what the frame asked for before.
    /// </para>
    /// </remarks>
    private SceneFog RetailAir(SceneFog fromGlobalLight) =>
        _atmosphere is { FogDensity: > 0f } authored
            ? new SceneFog(authored.FogColour, authored.FogDensity)
            : fromGlobalLight;

    /// <summary>Resolves every soldier-shaped thing's frame state once, so the shadow cascades and the geometry pass agree.</summary>
    /// <returns>What the shadow passes draw as characters, or <see langword="null"/> for nothing.</returns>
    private ICharacterPresenter? BeginCharacterFrame(float tickBlend, bool smoothPresentation)
    {
        Debug.Assert(_world is not null, "Begun only while a level is up; see RenderWorld.");

        // Everything soldier-shaped resolves its own frame state once, here, so the shadow
        // cascades and the geometry pass draw identical bodies in identical places.
        // The backdrop's soldiers resolve their frame state too, because the shadow pass below is
        // about to read their poses. Only one of the two is ever live: `_soldiers` belongs to a
        // played mission, `_menuBackdrop.Soldiers` to the menu's.
        ISoldierBodies? shadowCasters = _soldiers ?? _menuBackdrop.Soldiers;
        ITaskPresenter[] shadowTasks = [.. _taskPresenters.Where(static presenter => presenter.CastsShadow)];
        ICharacterPresenter? casterBodies = shadowCasters is null && shadowTasks.Length == 0
            ? null
            : new SoldierBodies(shadowCasters, shadowTasks, tickBlend);
        _world.BeginFrame(tickBlend, smoothPresentation);
        shadowCasters?.BeginFrame(tickBlend, smoothPresentation);
        _gunPickupRenderer?.BeginFrame(tickBlend, smoothPresentation);

        return casterBodies;
    }

    /// <summary>Describes the offscreen scene the frame is drawn into.</summary>
    private SceneTargetDescription DescribeSceneTarget(RenderResolution resolution, bool wantsTemporal)
    {
        // The modern path renders the scene offscreen so that it still exists, with its depth, once
        // it has been drawn -- which is what every effect from stage R3 on needs. With r_hdr off,
        // or on a backend that cannot offer a target, nothing here happens and the frame is drawn
        // straight to the back buffer exactly as the original draws it.
        SceneTargetDescription sceneTarget = new(
            resolution.RenderWidth,
            resolution.RenderHeight,
            FrameCVars.OffscreenScene.Value ? Math.Max(1, FrameCVars.MultiSampling.Value) : 1,
            Deferred: FrameCVars.OffscreenScene.Value && LightingCVars.Deferred.Value,

            // An 8-bit target under the vanilla preset: with no tone map and unit exposure the whole
            // offscreen detour is then a copy, which is what keeps that preset identical to the
            // build that had no offscreen path at all.
            HighDynamicRange: ExposureCVars.ToneMap.Value != ToneMapOperator.None,

            // Asked for only when something reconstructs to it -- the accumulation under a render scale,
            // or an NVIDIA reconstruction at any size: without one these are two buffers holding a
            // stretched copy. See RenderResolutionPolicy.
            OutputWidth: resolution.OutputWidth,
            OutputHeight: resolution.OutputHeight,
            GBuffer: (_gi.EffectiveLighting != LightingModel.Dynamic
                    ? GBufferLayout.BakedLighting
                    : GBufferLayout.Compact)
                | (wantsTemporal || PostCVars.MotionBlur.Value > 0f
                    ? GBufferLayout.MotionVectors
                    : GBufferLayout.Compact));

        return sceneTarget;
    }

    /// <summary>Settles the sun the frame lights with, publishes what the night is doing, and drags the sun gizmo.</summary>
    private void ResolveFrameSun(ref FrameView view)
    {
        Camera3D eye = view.Eye;
        Matrix4f unjitteredProjection = view.UnjitteredProjection;

        // The sun arrives in world space and the G-buffer's normals are in view space, so it is
        // rotated by the camera here. Transposed() is not decoration: the rows of the orientation
        // ARE the view axes, and Transform() applies the engine's row-vector convention which dots
        // the columns -- the inverse rotation. Getting it wrong lights the world from a direction
        // that swings as the player turns.
        // The authored sun swept to the current time of day. Held for the frame so every consumer
        // -- this deferred light, the shadows, the surface cache's relight and its radiance cache,
        // and the sky fill -- lights from the same swept sun rather than the raw authored one.
        // The sun -- or the moon -- this frame lights with. See CycleSun: the same answer the surface
        // cache is converged under, so the two cannot light the level differently.
        DirectionalLightSettings light = _timeOfDay.CycleSun();

        // What the night is doing to the picture, published for the exposure. The sweep is the only
        // thing that knows how far under the horizon the sun has gone, and the resolve is a long way
        // from here, so it travels as the one hidden variable rather than as another argument
        // threaded through every pass between. The fixed-sun path leaves it at nought, which is what
        // a comparison capture wants: a pinned sun and an unmodified exposure.
        ExposureCVars.Nightfall.Value = _timeOfDay.DayNight.Nightfall;

        // And what the LEVEL says about its own darkness, which the adaptation cannot work out
        // for itself: a scene lit dimly all over reads to it as a correctly-exposed dark subject.
        //
        // The SWEEP's reading, not the level's own constant. It used to be the constant, which
        // pinned the exposure to the hour the level opened at: scrub a night mission to noon and
        // the sun came up while the picture stayed exposed for midnight. A title that authors its
        // own night is bound to the cycle by it, so the cycle's own reading is the level's -- and
        // it moves with the clock, which is the whole point of having one.
        //
        // And ONLY under the dynamic model, because that is the only one it describes. The drop
        // answers an eye adaptation that misreads a scene whose light is COMPUTED at low levels
        // and lifts the whole thing back to mid grey. A model that reproduces the level's own
        // composited picture computes nothing to be misread: the picture is what the artists made,
        // and re-exposing it for a night it already contains is subtracting the same night twice.
        // Fitted for the dynamic model, it crushed the other two -- mission 1's baked ground came
        // out at 0.18 of vanilla where the dynamic one landed on 0.93.
        //
        // The REQUESTED model, not _gi.EffectiveLighting. That one reports Baked until the real-time
        // structures finish building, so reading it here would move the exposure partway through a
        // load -- and the adaptation that rides on it is temporal, so the step would be chased
        // rather than taken. What the exposure follows has to be a decision the player made, not a
        // state the loader is in.
        ExposureCVars.AuthoredNightfall.Value =
            _deferredLighting is null || LightingCVars.Lighting.Value != LightingModel.Dynamic
                ? 0f
                : _timeOfDay.DayNight.Nightfall;

        // And how much a full night of it is worth, which is the title's to say: see
        // DeferredLighting.NightExposureDrop.
        ExposureCVars.AuthoredNightDropFor.Value =
            _deferredLighting?.NightExposureDrop ?? ExposureCVars.AuthoredNightDrop;

        _timeOfDay.EffectiveSun = light;
        Vector3f viewSunDirection = eye.Orientation.Transposed().Transform(light.Direction);

        // The sun gizmo, if it is up: projected against this frame's camera and dragged this frame,
        // so there is no lag between the ring under the cursor and the sun it turns. Its rotation
        // feeds straight back into the cycle, the same knob the numpad keys turn.
        _sunGizmo.Update(
            eye,
            DepthUnprojection.From(unjitteredProjection, NearPlane, FarPlane),
            light.Direction,
            _window.Width,
            _window.Height,
            _window.Input.Mouse.X,
            _window.Input.Mouse.Y,
            _window.Input.Mouse.IsDown(Input.MouseButton.Left),
            (axis, delta) => _timeOfDay.DayNight.RotateAxis(axis, delta));

        view.Light = light;
        view.ViewSunDirection = viewSunDirection;
    }

    /// <summary>Fits the sun's shadow cascades to the eye and hands them to the device.</summary>
    private void FitSunShadows(FrameLoopState state, ref FrameView view)
    {
        Camera3D eye = view.Eye;
        DirectionalLightSettings light = view.Light;
        float verticalFieldOfView = view.VerticalFieldOfView;
        float aspectRatio = view.AspectRatio;
        Matrix4f unjitteredProjection = view.UnjitteredProjection;

        bool wantsShadows = FrameCVars.OffscreenScene.Value
            && LightingCVars.Deferred.Value
            && ShadowCVars.Shadows.Value > 0;

        ShadowSettings shadowSettings = wantsShadows
            ? new ShadowSettings(
                ShadowCVars.ShadowCascades.Value,
                ShadowCVars.Shadows.Value,
                ShadowCVars.ShadowDistance.Value)
            {
                ContactReach = ShadowCVars.ContactShadows.Value,
                FilterRadius = ShadowCVars.ShadowSoftness.Value,

                // The live software/hardware switch: off, a tracing backend leaves the sun to the
                // cascaded map like everyone else. r_rt_shadows moves the sun's shadow on its own,
                // leaving every other trace in the frame alone, which is what makes the two shadow
                // systems comparable in place.
                TracedSun = RayTracingCVars.HardwareRayTracing.Value && RayTracingCVars.TracedSunShadows.Value,

                // The day/night cycle fades the colour to exactly nought a tenth below the
                // horizon; from there every sun-shadow mask is multiplied by zero, and the whole
                // pipeline -- trace, denoise, cascades -- can stand down. Colour, not
                // IsBelowHorizon: through twilight a dim sun still lights and still shadows.
                SunLit = light.Colour.X > 0f || light.Colour.Y > 0f || light.Colour.Z > 0f,
            }
            : ShadowSettings.Off;

        int cascadeCount = ShadowCascades.Fit(
            eye,
            light.Direction,
            shadowSettings,
            verticalFieldOfView,
            aspectRatio,
            NearPlane,
            state.CascadeStorage);

        _device.SetShadows(
            shadowSettings,
            state.CascadeStorage.AsSpan(0, cascadeCount),
            DepthUnprojection.From(unjitteredProjection, NearPlane, FarPlane),

            // View space back into camera-relative world space, which is the frame the cascades
            // are fitted in. The same transpose, for the same reason.
            eye.Orientation.Transposed());

        view.ShadowSettings = shadowSettings;
        view.CascadeCount = cascadeCount;
    }

    /// <summary>Resolves the medium the frame's fog is marched through.</summary>
    /// <param name="state">What the frame loop keeps from one frame to the next.</param>
    /// <param name="fogWanted">Whether the froxel fog is drawn at all.</param>
    /// <param name="fogMissionId">The mission whose tuned fog the medium is.</param>
    /// <returns>The medium, or <see cref="VolumetricFogSettings.Off"/>.</returns>
    private VolumetricFogSettings ResolveFogMedium(FrameLoopState state, out bool fogWanted, out int fogMissionId)
    {
        Debug.Assert(_scene is not null, "Resolved only while a level is up; see RenderWorld.");

        // The medium. Density comes from the level's own FlatSky "Fog Amount" unless overridden:
        // a level that asked for thick fog gets thick fog, so the missions keep their own moods
        // instead of all being handed one number. The colours come from the sun, so a fog bank is
        // lit by the same light as the ground under it.
        // Lighter than it was: the authored FogAmount, read at full strength, laid a haze dense
        // enough to wash the yard white a few tens of metres out -- where the buildings should stay
        // crisp -- and it hung inside rooms besides. This carries the same shape at a
        // fraction of the extinction, so distance still reads but the far wall of a compound stays
        // legible.
        // A title that authors a real extinction gives it here, in the same units, and is
        // believed. I.G.I. 2's GlobalLight carries one per level -- mission 1 asks for 0.00174
        // per metre -- where the curve below is a fit over Project I.G.I.'s FlatSky "Fog Amount",
        // which is a 0-to-1 dial and not a length at all. Run through that fit the sequel's first
        // mission came out at 0.0070 per metre, four times its own figure, and the frame was a
        // blue wash with the buildings barely in it.
        float authoredDensity = _atmosphere is { FogDensity: > 0f } own
            ? own.FogDensity
            : _sky is { HasSky: true }
                ? 0.002f + (Math.Clamp(_sky.FogAmount, 0f, 1f) * 0.010f)
                : 0.006f;

        // Fog is an outdoor medium, and where it is outdoors is decided a froxel at a time inside
        // the march itself -- see the shelter lookup in the FogScatter shader module. It used to be
        // decided once for the whole frame, standing the march down while the camera was inside a
        // building, because the haze sitting between the eye and a window washed out the yard
        // beyond it: fog marched straight through the wall. Switching the march off answers that
        // by deleting the yard's fog too, so a compound seen from a doorway came up in clear air.
        // The room is now dry and the yard beyond the window is not, which is what is actually
        // out there.
        bool wantsFog = FogCVars.FogQuality.Value > 0
            && FrameCVars.OffscreenScene.Value
            && LightingCVars.Deferred.Value;

        // The mission actually on screen, as the colour grade reads it. This read -mission's id, so a
        // mission entered from the campaign -- where that is nought -- was drawn in the generic
        // default haze instead of its own tuned fog, and looked different from the same mission
        // started any other way, a renderer switch included.
        int fogMission = _session.Mission?.Id ?? _menuBackdrop.MissionId ?? _options.StartMission;

        VolumetricFogSettings medium = VolumetricFogSettings.Off;
        if (wantsFog)
        {
            // The sky plane's height is an offset above the ground, not a world Z: a level's
            // coordinates carry a kilometres-large absolute offset, so it has to be anchored to
            // the scene's own ground before the march can compare a sample against it.
            medium = state.FogPresets.Resolve(
                fogMission,
                authoredDensity,
                (float)(_scene.GroundHeight + (_sky is { HasSky: true } ? _sky.PlaneHeight : 0f)),
                FogCVars.FogQuality.Value);

            // A console override beats the authored preset, so a value can be dialled in live and
            // then written back into the file.
            //
            // "Overridden" is the variable having been moved off its own default, which is why
            // the graphics presets pin all four of these to their defaults on every tier: a
            // player choosing Ultra has not asked for one mission's authored atmosphere to be
            // replaced by a global number, and a preset that assigned one could not be told from
            // a player who had. Picking a preset therefore CLEARS an override and hands the
            // missions back their own air, which is the behaviour the settings page implies.
            // The haze is the colour the level says it is. Without this the medium scatters a
            // near-white droplet albedo lit by the sun, which for a night mission whose own fog is
            // a dark blue (0.00, 0.16, 0.29) is a pale daylight haze over a dark scene.
            if (_atmosphere is { } tint && tint.FogColour.LengthSquared > 0f)
            {
                medium = medium with { Albedo = tint.FogColour };
            }

            if (FogCVars.FogDensity.Value > 0f)
            {
                medium = medium with { Density = FogCVars.FogDensity.Value };
            }

            if (!FogCVars.FogHeightFalloff.IsDefault)
            {
                medium = medium with { HeightFalloff = FogCVars.FogHeightFalloff.Value };
            }

            if (!FogCVars.FogAnisotropy.IsDefault)
            {
                medium = medium with { Anisotropy = FogCVars.FogAnisotropy.Value };
            }

            if (!FogCVars.FogDistance.IsDefault)
            {
                medium = medium with { Distance = FogCVars.FogDistance.Value };
            }
        }

        fogWanted = wantsFog;
        fogMissionId = fogMission;
        return medium;
    }

    /// <summary>Hands the device the frame's surface settings: the alpha hash, the detail, wetness, the probes, AO, TAA and the fog.</summary>
    private void ApplyFrameSurfaces(FrameLoopState state, in FrameView view, VolumetricFogSettings medium, bool wantsFog, int fogMission)
    {
        Camera3D eye = view.Eye;
        DirectionalLightSettings light = view.Light;
        bool wantsTemporal = view.WantsTemporal;
        float jitterX = view.JitterX;
        float jitterY = view.JitterY;
        Matrix4f unjitteredProjection = view.UnjitteredProjection;

        // The hashed alpha test needs the temporal resolve to average its dither, so it follows the
        // temporal path: a per-frame seed above zero turns it on, zero leaves the hard cut-out. The
        // seed only has to change each frame for the dither to differ; the jitter counter does that.
        _device.SetAlphaHash(
            MaterialCVars.HashedAlpha.Value && wantsTemporal
                ? 1f + (float)(state.TemporalFrame & 63)
                : 0f);

        // The derived detail is a deferred-path feature: it changes the normal and the roughness
        // written into the G-buffer, and without one there is nothing to write them into.
        //
        // r_pbr (F9) multiplies the authored strength by one or zero rather than replacing it, so
        // the maps can be taken out of the frame and put back without disturbing what the preset
        // chose. Read here, every frame, because that is what makes it instant: nothing is
        // unloaded and nothing is rebuilt -- the shader simply stops scaling its slope by it.
        _device.SetMaterialDetail(
            FrameCVars.OffscreenScene.Value
            && LightingCVars.Deferred.Value
            && MaterialCVars.MaterialDetailEnabled.Value
                ? MaterialCVars.MaterialDetail.Value
                : 0f);
        _device.SetFloorGloss(
            FrameCVars.OffscreenScene.Value && LightingCVars.Deferred.Value
                ? MaterialCVars.FloorGloss.Value
                : 1f);
        _device.SetReflections(
            FrameCVars.OffscreenScene.Value && LightingCVars.Deferred.Value
                ? new ReflectionSettings(
                    ReflectionCVars.Reflections.Value,
                    ReflectionCVars.ReflectionIntensity.Value,
                    ReflectionCVars.ReflectionMaxRoughness.Value)
                : ReflectionSettings.Off);

        // How wet the ground is. Driven by the storm rather than by a number of its own: it is
        // raining or it is not, and a road that stays dry through a downpour is the single thing
        // that most gives away rain as an effect rather than as weather. Off the moment the storm
        // is not drawing, so a level with no rain is not quietly shaded damp.
        bool wetGround = WeatherCVars.Wetness.Value
            && FrameCVars.OffscreenScene.Value
            && LightingCVars.Deferred.Value
            && _storm is { IsActive: true };
        _device.SetWetness(
            wetGround
                ? WetnessSettings.Storm.AtIntensity(
                    _storm!.Settings.Intensity,
                    _loop.TimeSource.Milliseconds / 1000f)
                : WetnessSettings.Dry);

        // Eased rather than linear. A linear ramp starts and stops abruptly, and the eye reads the
        // stop as a second, smaller event -- which is the thing this exists to remove.
        float probeFade = _baked.ProbeArrivedAt < 0
            ? 1f
            : Math.Clamp((_loop.TimeSource.Milliseconds - _baked.ProbeArrivedAt) / ProbeFadeMilliseconds, 0f, 1f);
        probeFade = probeFade * probeFade * (3f - (2f * probeFade));

        _device.SetIrradianceVolumeSettings(
            _baked.ProbeField is not null && GlobalIlluminationCVars.GlobalIllumination.Value
                ? new IrradianceVolumeSettings(
                    true,
                    GlobalIlluminationCVars.GlobalIlluminationIntensity.Value,
                    light.Ambient,
                    probeFade)
                : IrradianceVolumeSettings.Off);

        // Ambient occlusion needs the G-buffer's normals and depth, so it is offered only when
        // there is a G-buffer; without one the lighting has no ambient term of its own to shade.
        _device.SetAmbientOcclusion(
            FrameCVars.OffscreenScene.Value && LightingCVars.Deferred.Value
                ? new AmbientOcclusionSettings(
                    ShadowCVars.AmbientOcclusion.Value,
                    ShadowCVars.AmbientOcclusionRadius.Value,
                    ShadowCVars.AmbientOcclusionIntensity.Value,
                    ShadowCVars.ScreenSpaceGi.Value,
                    ShadowCVars.ScreenSpaceGiIntensity.Value)
                : AmbientOcclusionSettings.Off);

        // Reprojection compares two frames, and the jitter is precisely what differs between them,
        // so the matrix handed over is the one without it.
        _device.SetTemporalAntiAliasing(
            new TemporalAaSettings(
                wantsTemporal,
                ReconstructionCVars.TemporalFeedback.Value,
                jitterX,
                jitterY,
                ReconstructionCVars.TemporalSharpening.Value),
            unjitteredProjection,
            eye.Position,
            eye.Orientation.Transposed());

        _device.SetVolumetricFog(
            medium,
            new Vector3f((float)eye.Position.X, (float)eye.Position.Y, (float)eye.Position.Z),
            wantsFog ? state.FogPresets.VolumesFor(fogMission) : ReadOnlySpan<LocalFogVolume>.Empty);
    }
}
