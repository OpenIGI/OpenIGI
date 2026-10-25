using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Host.Lighting;
using OpenIGI.Host.Services;
using OpenIGI.Presentation;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    // The real-time lighting model's structures; made with the field cache.
    private GlobalIllumination _gi = null!;

    /// <summary>The host as the lighting model reads it.</summary>
    /// <param name="host">The host.</param>
    private sealed class GlobalIlluminationHost(GameHost host) : IGlobalIlluminationHost
    {
        public WorldScene? Scene => host._scene;

        public IWorldPresenter? World => host._world;

        public DirectionalLightSettings? Sun => host._timeOfDay.Sun;

        public DirectionalLightSettings? EffectiveSun => host._timeOfDay.EffectiveSun;

        public bool HasLevel => host._session.Mission is not null || host._menuBackdrop.Preview is not null;

        public int? CurrentMissionId => host._session.Mission?.Id ?? host._menuBackdrop.MissionId;

        public int FrameCount => host._loop.Clock.FrameCount;

        public DistanceFieldCache FieldCache => host._sdfCache;

        public DirectionalLightSettings CycleSun() => host._timeOfDay.CycleSun();

        public Vector3f SkyFillColour() => host._timeOfDay.SkyFillColour();

        public void UpdateBackdropProfile() => host.UpdateBackdropProfile();

        public void UpdateTerrainRaster() => host.UpdateTerrainRaster();

        public DistanceFieldBinding DescribeDistanceField(Vector3d eye) => host.DescribeDistanceField(eye);

        public IrradianceVolume? ProbeField => host._baked.ProbeField;
    }

    /// <summary>Describes the level's distance field for the backend, relative to the camera.</summary>
    /// <param name="eye">Where the camera is, which every box is measured from.</param>
    /// <returns>The clipmaps and their boxes, or nothing when there is no field to march.</returns>
    /// <remarks>
    /// CAMERA-RELATIVE, and that is not a convenience. These levels sit tens of kilometres from the world
    /// origin, where a float has metres of precision left; the march that reads them is a loop of steps a
    /// fraction of a voxel long, so it has to work in a space centred on the camera. Every other traced
    /// pass in the renderer already does.
    /// </remarks>
    private DistanceFieldBinding DescribeDistanceField(Vector3d eye)
    {
        if (_gi.DistanceField is not { } field || !field.Clipmaps.IsValid)
        {
            return DistanceFieldBinding.None;
        }

        GlobalDistanceFieldSettings settings = field.Settings;
        int levels = Math.Clamp(settings.Levels, 0, DistanceFieldBinding.MaxLevels);
        if (levels <= 0)
        {
            return DistanceFieldBinding.None;
        }

        Span<DistanceFieldLevel> described = stackalloc DistanceFieldLevel[DistanceFieldBinding.MaxLevels];
        for (int level = 0; level < levels; level++)
        {
            Vector3d corner = field.Origins[level];
            described[level] = new DistanceFieldLevel(
                new Vector3f(
                    (float)(corner.X - eye.X),
                    (float)(corner.Y - eye.Y),
                    (float)(corner.Z - eye.Z)),
                settings.VoxelSizeOf(level),
                field.RangeOfLevel(level));
        }

        return new DistanceFieldBinding(
            field.Clipmaps,
            levels,
            settings.Resolution,
            described[0],
            described[1],
            described[2],
            described[3]);
    }
}
