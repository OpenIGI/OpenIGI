using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Audio;
using OpenIGI.Game.World;
using OpenIGI.Host.Services;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    // What the level's lamps read of the host.
    private sealed class LevelLampsHost(GameHost host) : ILevelLampsHost
    {
        public GlobalIllumination Gi => host._gi;

        public Camera3D Eye => host._inspectorEye;

        public ObjectCollisionIndex? Solids => host._mission.Solids;

        public WorldRayCast? EffectWalls => host._mission.EffectWalls;

        public ShelterHeightField Shelter => host._shelterField;

        public bool SunPinned => host._options.SunDirection is not null;

        public TimeOfDayLighting TimeOfDay => host._timeOfDay;

        public SoundSystem? Sounds => host._audioOut.Sounds;

        public bool HasScene => host._scene is not null;
    }
}
