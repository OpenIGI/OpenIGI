using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using static OpenIGI.Desktop.Tests.MeshHitRays;
using static OpenIGI.Desktop.Tests.SoldierRendererProbe;
using static OpenIGI.Desktop.Tests.SoldierRendererScenario;
using Scenario = OpenIGI.Desktop.Tests.SoldierRendererScenario;

namespace OpenIGI.Desktop.Tests;

/// <summary>A soldier a mod has listed in <see cref="SoldierSimulation.SilentDeaths"/> dies as any other, without the cry.</summary>
[Collection(SoldierRendererScenario.CollectionName)]
public sealed class SoldierSilentDeathTests
{
    [RequiresGameDataFact]
    public void A_soldier_listed_silent_dies_without_his_death_cry()
    {
        using Scenario scenario = CreateScenario();
        (SoldierView view, int cries) = ShootDead(scenario, silent: true);

        Assert.True(view.IsDead);
        Assert.Equal(0, cries);
    }

    [RequiresGameDataFact]
    public void A_soldier_not_listed_cries_once_as_he_dies()
    {
        using Scenario scenario = CreateScenario();
        (SoldierView view, int cries) = ShootDead(scenario, silent: false);

        Assert.True(view.IsDead);
        Assert.Equal(1, cries);
    }

    [RequiresGameDataFact]
    public void A_new_level_forgets_who_dies_silently()
    {
        using Scenario scenario = CreateScenario();
        SoldierSimulation soldiers = scenario.Renderer.Simulation;
        soldiers.SilentDeaths.Add(FindAnyMeshHitRay(scenario.Renderer, _ => true).View);

        soldiers.Animate(new Dictionary<int, Formats.Ai.GraphFile>());

        Assert.Empty(soldiers.SilentDeaths);
    }

    private static (SoldierView View, int Cries) ShootDead(Scenario scenario, bool silent)
    {
        MeshHitRay ray = FindAnyMeshHitRay(
            scenario.Renderer,
            view => view.Soldier.Team != scenario.Renderer.Simulation.PlayerTeam && !CinematicActor.IsStaged(view.Soldier));
        KeepOnlyTarget(scenario.Renderer, MemberFor(scenario.Renderer, ray.View));
        SoldierSimulation soldiers = scenario.Renderer.Simulation;
        if (silent)
        {
            soldiers.SilentDeaths.Add(ray.View);
        }

        int cries = 0;
        soldiers.SetDeathSound(_ => cries++);
        for (int shots = 0; !ray.View.IsDead && shots < 64; shots++)
        {
            ray = FindAnyMeshHitRay(scenario.Renderer, view => ReferenceEquals(view, ray.View));
            Vector3d origin = ray.Origin, end = ray.End;
            LevelBodies.Shoot(
                [scenario.Renderer],
                origin,
                end,
                soldiers.PlayerTask,
                double.PositiveInfinity,
                alreadyHeard: false,
                struck => new Igi1WeaponHit(struck, origin, end, 1000f, WeaponCategory.Gun, Shooter: null, FromPlayer: true).ToShared(),
                out _);
        }

        return (ray.View, cries);
    }
}
