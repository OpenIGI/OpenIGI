using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Host.Lighting;
using OpenIGI.Host.Services;
using OpenIGI.Presentation;

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

        public DirectionalLightSettings? Sun => host._sun;

        public DirectionalLightSettings? EffectiveSun => host._effectiveSun;

        public bool HasLevel => host._session.Mission is not null || host._menuBackdrop.Preview is not null;

        public int? CurrentMissionId => host._session.Mission?.Id ?? host._menuBackdrop.MissionId;

        public int FrameCount => host._loop.Clock.FrameCount;

        public DistanceFieldCache FieldCache => host._sdfCache;

        public DirectionalLightSettings CycleSun() => host.CycleSun();

        public Vector3f SkyFillColour() => host.SkyFillColour();

        public void UpdateBackdropProfile() => host.UpdateBackdropProfile();
    }
}
