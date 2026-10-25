using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World.Particles;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Project I.G.I.'s Smoke tasks, on the plume emitter both games share.</summary>
[Collection(RetailMissionWorld.Name)]
public sealed class SmokePlumeTests
{
    [RequiresGameDataFact]
    public void MissionOnesPlumeSmokesAParticleATickAtItsInitialFactor()
    {
        Igi1SmokePlumes plumes = Igi1SmokePlumes.Build(RetailMissionData.Get(1).Root, variables: null);
        Igi1SmokePlume plume = plumes.Plumes.First(p => p.MaxParticles == 300);
        Assert.Equal(30.0, plume.GenerateFactor);
        Assert.Equal(3, plume.Sprite);

        Particle2DModule module = new();
        plumes.Bind(module);
        plumes.Tick();
        Particle2DSystem system = Assert.IsType<Particle2DSystem>(plume.System);
        Assert.Contains(system, module.Systems);
        Assert.False(system.IsSelfDestruct);

        // A factor of thirty is one particle a tick (0x437DB0 throws one for every thirty added up past thirty).
        for (int tick = 0; tick < 10; tick++)
        {
            plumes.Tick();
        }

        Assert.InRange(system.Count, 9, 11);
    }

    [RequiresGameDataFact]
    public void APlumeWhoseModuleWasClearedMakesItsSystemAgain()
    {
        Igi1SmokePlumes plumes = Igi1SmokePlumes.Build(RetailMissionData.Get(1).Root, variables: null);
        Particle2DModule module = new();
        plumes.Bind(module);
        plumes.Tick();
        Particle2DSystem first = plumes.Plumes[0].System!;

        module.Clear();
        plumes.Tick();

        Assert.NotSame(first, plumes.Plumes[0].System);
        Assert.Contains(plumes.Plumes[0].System!, module.Systems);
    }
}
