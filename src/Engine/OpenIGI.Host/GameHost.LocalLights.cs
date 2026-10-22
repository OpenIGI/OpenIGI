using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Host.Lighting;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Lighting;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    // Reused each frame to hand the view-space lamp lights to the device without allocating, and without a
    // stackalloc inside the frame loop. Sized to the shader's cap; only the first N entries are ever sent.
    private readonly DeferredLocalLight[] _localLightScratch = new DeferredLocalLight[DeferredLocalLight.Maximum];

    // The same lamps in the storm's frame -- metres from the eye, world axes -- for the drops falling
    // through their light; how many of them this frame has.
    private readonly StormLamp[] _stormLamps = new StormLamp[DeferredLocalLight.Maximum];
    private int _stormLampCount;

    // Which lamps hold shadow maps and the surface cache's coarse lamp maps, and drawing them.
    private readonly LampShadowMaps _lampMaps;

    // The player's flashlight, and what its lens is kept out of.
    private readonly PlayerFlashlight _flashlight;
    private Func<DeathCameraVantage.Blocked?>? _flashlightWalls;

    // Where a new level's lamps are ordered from for their coarse maps, nearest first.
    private Vector3d LampOrderEye => _camera?.Position ?? default;

    // Where everyone who casts in a lamp's map stands, in metres: the player and the living soldiers.
    private void LampMapPeople(List<Vector3d> people)
    {
        if (HostedPlayer is { } player)
        {
            people.Add(WorldScene.ToMetres(player.Body.Position));
        }

        if (_soldiers is { } soldiers)
        {
            foreach (HumanOccupant occupant in soldiers.LiveOccupants())
            {
                if (!occupant.IsDead)
                {
                    people.Add(WorldScene.ToMetres(occupant.Position));
                }
            }
        }
    }

    // The distance field's per-level sizes and ranges, reused frame to frame for LampFieldFor.
    private readonly float[] _lampFieldVoxels = new float[4];
    private readonly float[] _lampFieldRanges = new float[4];

    /// <summary>The level's distance field as a lamp-shadow renderer reads it, or null while there is none.</summary>
    private LampOcclusionField? LampFieldFor(GlobalDistanceField? field)
    {
        if (field is null || !field.Clipmaps.IsValid)
        {
            return null;
        }

        GlobalDistanceFieldSettings settings = field.Settings;
        int levels = Math.Clamp(settings.Levels, 0, Math.Min(4, field.Origins.Count));
        if (levels <= 0)
        {
            return null;
        }

        for (int level = 0; level < 4; level++)
        {
            int from = Math.Min(level, levels - 1);
            _lampFieldVoxels[level] = settings.VoxelSizeOf(from);
            _lampFieldRanges[level] = field.RangeOfLevel(from);
        }

        return new LampOcclusionField(
            field.Clipmaps,
            levels,
            settings.Resolution,
            field.Origins[0],
            field.Origins[Math.Min(1, levels - 1)],
            field.Origins[Math.Min(2, levels - 1)],
            field.Origins[Math.Min(3, levels - 1)],
            _lampFieldVoxels,
            _lampFieldRanges);
    }

    /// <summary>Submits the level's lamps and the player's flashlight as point lights for the deferred pass.</summary>
    private void SubmitLocalLights(
        FrameLoopState state, bool captured, Camera3D eye, Matrix4f unjitteredProjection, bool flashlightShown, Camera3D flashlightBody,
        MapSceneView? mapScene, SceneTargetDescription sceneTarget)
    {
        // The level's lamp fixtures, submitted as real point lights for the deferred pass that
        // closes just below -- the same timing as the screen-space fill. Transformed into this
        // frame's view space here, in double precision: the level sits at large world coordinates,
        // so the camera-relative subtract is done before the cast to float. Only under a shading
        // model and only when the scale asks for them; a windowless interior the gather cannot
        // reach is exactly what these light. The shader also guards on the model and the count.
        float localLightScale = LocalLightCVars.LocalLightScale.Value;
        float localLightRange = LocalLightCVars.LocalLightRange.Value;
        _stormLampCount = 0;
        if (captured
            && localLightScale > 0f
            && _gi.EffectiveLighting != LightingModel.Baked
            && _gi.LightFixtures.Count > 0)
        {
            Matrix3f worldToView = eye.Orientation.Transposed();

            // WHICH lamps, before how they light. A level carries well over a hundred fixtures and
            // the shader's arrays hold thirty-two, so the set has to be chosen every frame rather
            // than taken off the top of a list -- the first thirty-two are the level's brightest,
            // which is a fact about the whole level and says nothing about where the player is
            // standing. Nearest to the camera, measured against each fixture's own reach so that a
            // long tube is not passed over for a desk lamp a metre closer.
            int localCount = _lamps.SelectNearest(eye.Position, localLightRange);

            for (int i = 0; i < localCount; i++)
            {
                LightFixture fixture = _gi.LightFixtures[_lamps.NearestOrder[i]];
                Vector3f cameraRelative = new(
                    (float)(fixture.Position.X - eye.Position.X),
                    (float)(fixture.Position.Y - eye.Position.Y),
                    (float)(fixture.Position.Z - eye.Position.Z));

                // Clamp the fixture's relative intensity before scaling. Intensity is area x
                // luminance, so a long fluorescent tube (area ~9) would outpower a desk lamp
                // (area ~0.5) sixteen to one and blow the room out around it -- but a bigger lamp
                // is not sixteen times brighter, only larger. The clamp flattens that to something
                // like real fixtures; the scale cvar then sets the overall level.
                //
                // Then by how much of that the lamp is actually giving out, which is one for
                // everything nobody has switched. The emission on the fixture's own geometry is
                // scaled by the same number in the world renderer, so a lamp fading out fades as
                // one thing rather than as a panel and a pool of light going at their own rates.
                int lampIndex = _lamps.NearestOrder[i];
                float output = _gi.LampSwitchboard?.LevelOf(lampIndex) ?? 1f;

                // Clamp the fixture's relative intensity before scaling. Intensity is area x
                // luminance, so a long fluorescent tube (area ~9) would outpower a desk lamp
                // (area ~0.5) sixteen to one and blow the room out around it -- but a bigger lamp
                // is not sixteen times brighter, only larger. The clamp flattens that to something
                // like real fixtures; the scale cvar then sets the overall level.
                float intensity = MathF.Min(fixture.Intensity, 1.5f) * localLightScale * output;

                // A cooling filament is a colder black body and therefore a redder one, which is
                // the last thing anybody sees of a light going out. White whenever the lamp is
                // not on its way out.
                Vector3f colour = fixture.Colour;

                if (_gi.LampSwitchboard?.TintOf(lampIndex) is { } tint && tint != Vector3f.One)
                {
                    colour = new Vector3f(colour.X * tint.X, colour.Y * tint.Y, colour.Z * tint.Z);
                }

                // The way the fixture faces, rotated into the same space its position is in. A
                // rotation and not a transform: this is a direction, so the camera's translation
                // must not touch it. Zero stays zero, which the shader reads as a point light.
                Vector3f facing = fixture.IsDirectional
                    ? worldToView.Transform(fixture.Direction)
                    : default;

                _stormLamps[i] = new StormLamp(
                    cameraRelative,
                    fixture.Radius * localLightRange,
                    colour * intensity,
                    fixture.IsDirectional ? fixture.Direction : default);

                _localLightScratch[i] = new DeferredLocalLight(
                    worldToView.Transform(cameraRelative),
                    fixture.Radius * localLightRange,
                    colour,
                    intensity,
                    facing,

                    // The fixture's own size, which is what gives its shadow a penumbra. Floored
                    // rather than taken raw: a fixture the extractor measured as a few
                    // centimetres across would cast an edge as hard as a point light's, and no
                    // real fitting is that small.
                    MathF.Max(fixture.Extent, 0.05f),

                    // The rectangle the fitting emits from, where its shadow rays are aimed.
                    fixture.HasRect ? worldToView.Transform(fixture.Axis) * fixture.HalfLength : default,
                    fixture.HasRect ? fixture.HalfWidth : 0f);
            }

            // Both backends, through the seam: a windowless interior needs its lamps whichever
            // renderer is drawing it, and the ultra preset -- the one that discards the shipped
            // bakes entirely and so has NOTHING else lighting an interior -- is the Vulkan path.
            _device.SetLocalLights(_localLightScratch.AsSpan(0, localCount));
            _lampMaps.Assign(localCount, unjitteredProjection, _localLightScratch, _lamps.NearestOrder, _gi.LightFixtures);
            _lampMaps.AssignCoarse(localCount, _lamps.NearestOrder);
            _stormLampCount = localCount;

            // And the distance field those lamps' shadows are marched through wherever the renderer
            // cannot trace them -- WebGPU always, Vulkan with the RT switch off. See
            // LampOcclusionField.
            _device.SetLampOcclusionField(LampFieldFor(_gi.DistanceField));
        }

        // The player's flashlight, outside the lamps' gate: it lights under every lighting model the
        // deferred path has, the GI's warm-up included, and needs no fixture in the level to exist.
        _flashlight.Submit(
            captured && flashlightShown && !_mission.IsPlayerDead,
            _menuFlow.MenuMode && _menuFlow.PauseMenu,
            flashlightBody,
            eye,
            Viewpoint?.IsThirdPersonEngaged == true,
            _loop.TimeSource.Milliseconds,
            _flashlightWalls ??= ThirdPersonWalls);

        if (mapScene is not null)
        {
            _device.SetViewport(new Viewport(
                0,
                0,
                captured ? sceneTarget.Width : _window.Width,
                captured ? sceneTarget.Height : _window.Height));
        }

        if (!captured)
        {
            _device.EndStageTiming();
            _device.BeginStageTiming(RenderStage.Forward);
        }
    }
}
