using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
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

    /// <summary>
    /// Mission one's dust hangs off a train's car and rides along with it (<c>Smoke_UpdateLocal</c>, <c>0x438240</c>;
    /// <c>Smoke_Move</c>, <c>0x437AD0</c>): its offset is taken in the car's frame when the car first answers and carried
    /// from then on.
    /// </summary>
    [RequiresGameDataFact]
    public void A_plume_hung_off_a_train_rides_along_with_it()
    {
        Igi1SmokePlume plume = Igi1SmokePlumes.Build(RetailMissionData.Get(1).Root, variables: null).Plumes
            .First(p => p.Task.Parent?.Type.Name == "Train");
        Vector3d authored = plume.Position;
        Vector3d carAt = new(authored.X - 1000, authored.Y + 2000, authored.Z - 300);
        Matrix3f carTurn = Matrix3f.RotationZ(0.5f);
        GameTask car = plume.Task.Parent!;

        plume.Follow(task => ReferenceEquals(task, car) ? (carAt, carTurn) : null);
        Assert.Equal(authored.X, plume.Position.X, 3);
        Assert.Equal(authored.Y, plume.Position.Y, 3);

        // The car moves on and turns half about: the plume keeps its place on it, its offset turned with it.
        Vector3d moved = new(carAt.X + 5000, carAt.Y, carAt.Z);
        Matrix3f turned = Matrix3f.RotationZ(0.5f + MathF.PI);
        plume.Follow(task => ReferenceEquals(task, car) ? (moved, turned) : null);
        Assert.Equal(moved.X - (authored.X - carAt.X), plume.Position.X, 1);
        Assert.Equal(moved.Y - (authored.Y - carAt.Y), plume.Position.Y, 1);
        Assert.Equal(authored.Z, plume.Position.Z, 3);
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
