using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Igi1;
using OpenIGI.TestSupport;
using Xunit;
using static OpenIGI.Desktop.Tests.MeshHitRays;
using static OpenIGI.Desktop.Tests.SoldierRendererProbe;
using static OpenIGI.Desktop.Tests.SoldierRendererScenario;
using Scenario = OpenIGI.Desktop.Tests.SoldierRendererScenario;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// What a mod hears of and does to the soldiers: the rounds that strike them by either path, a gun let go of, and a body of the
/// mod's own that fails.
/// </summary>
[Collection(SoldierRendererScenario.CollectionName)]
public sealed class SoldierModHookTests
{
    private static readonly WeaponType Rifle = new() { Category = WeaponCategory.Gun };

    [RequiresGameDataFact]
    public void A_round_the_player_fires_is_told_once_before_he_turns_or_falls()
    {
        using Scenario scenario = CreateScenario();
        MeshHitRay ray = Target(scenario);
        Matrix3f standing = ray.View.Orientation;
        List<(SoldierStrike Strike, bool Dead, Matrix3f Orientation)> told = [];
        scenario.Renderer.Simulation.Struck.Add(strike => told.Add((strike, strike.Soldier.IsDead, strike.Soldier.Orientation)));

        Shoot(scenario, ray);

        (SoldierStrike strike, bool dead, Matrix3f orientation) = Assert.Single(told);
        Assert.Same(ray.View, strike.Soldier);
        Assert.True(strike.Killed);
        Assert.False(dead);
        Assert.Equal(standing, orientation);
        Assert.True(ray.View.IsDead);
    }

    [RequiresGameDataFact]
    public void A_marched_round_is_told_once_before_his_death_is_taken()
    {
        using Scenario scenario = CreateScenario();
        MeshHitRay ray = Target(scenario);
        SoldierSimulation soldiers = scenario.Renderer.Simulation;
        List<(SoldierStrike Strike, bool Dead)> told = [];
        soldiers.Struck.Add(strike => told.Add((strike, strike.Soldier.IsDead)));

        soldiers.StrikeWithRound(ray.View, ray.MeshHit, ray.Origin, ray.End, Rifle, 1000f, shooter: null);

        (SoldierStrike strike, bool dead) = Assert.Single(told);
        Assert.True(strike.Killed);
        Assert.False(dead);
        for (int tick = 0; tick < 4 && !ray.View.IsDead; tick++)
        {
            scenario.Renderer.Tick(enemyAlive: false);
        }

        Assert.True(ray.View.IsDead);
        Assert.Single(told);
    }

    [RequiresGameDataFact]
    public void A_soldier_who_cannot_be_hurt_is_told_of_by_neither_path()
    {
        using Scenario scenario = CreateScenario();
        MeshHitRay ray = Target(scenario);
        SoldierSimulation soldiers = scenario.Renderer.Simulation;
        Igi1HumanAi brain = soldiers.BrainOf(ray.View) ?? throw new InvalidOperationException("The target has no brain.");
        brain.IsInvulnerable = true;
        int told = 0;
        soldiers.Struck.Add(_ => told++);

        Shoot(scenario, ray);
        soldiers.StrikeWithRound(ray.View, ray.MeshHit, ray.Origin, ray.End, Rifle, 1000f, shooter: null);

        Assert.Equal(0, told);
        Assert.False(ray.View.IsDead);
    }

    [RequiresGameDataFact]
    public void A_listener_that_throws_leaves_the_round_and_the_other_listeners_alone()
    {
        using Scenario scenario = CreateScenario();
        MeshHitRay ray = Target(scenario);
        SoldierSimulation soldiers = scenario.Renderer.Simulation;
        int told = 0;
        soldiers.Struck.Add(_ => throw new InvalidOperationException("broken"));
        soldiers.Struck.Add(_ => told++);

        Shoot(scenario, ray);

        Assert.Equal(1, told);
        Assert.True(ray.View.IsDead);
    }

    [RequiresGameDataFact]
    public void A_gun_let_go_of_drops_once_and_his_death_drops_no_other()
    {
        using Scenario scenario = CreateScenario();
        MeshHitRay ray = Target(scenario);
        SoldierSimulation soldiers = scenario.Renderer.Simulation;
        SoldierAgent agent = soldiers.AgentOf(ray.View) ?? throw new InvalidOperationException("The target has no agent.");
        agent.CarriedWeaponType = agent.WeaponType = Rifle;
        int drops = 0;
        soldiers.SetWeaponDropSink((_, _, _, _) => drops++);

        soldiers.DropGun(ray.View, ray.View.Position, new Vector3d(1.0, 0.0, 0.0));
        soldiers.DropGun(ray.View, ray.View.Position, new Vector3d(1.0, 0.0, 0.0));
        Assert.Equal(1, drops);

        Shoot(scenario, ray);
        Assert.True(ray.View.IsDead);
        Assert.Equal(1, drops);
    }

    [RequiresGameDataFact]
    public void A_body_that_throws_is_let_go_of_and_he_is_drawn_as_the_game_draws_him()
    {
        using Scenario scenario = CreateScenario();
        SoldierMember member = MemberFor(scenario.Renderer, Target(scenario).View);
        Style style = new(() => new Body { ThrowsOnTick = true });
        scenario.Renderer.BodyStyle = style;

        for (int tick = 0; tick < 3; tick++)
        {
            scenario.Renderer.Tick(enemyAlive: false);
        }

        Assert.Null(member.Styled);
        Assert.True(member.StyleFailed);
        Assert.Equal(1, style.Asked);
        Assert.True(style.Made.All(body => body.Disposed));
    }

    [RequiresGameDataFact]
    public void A_soldier_the_style_declines_is_not_asked_again_until_it_is_set_again()
    {
        using Scenario scenario = CreateScenario();
        Target(scenario);
        Style declining = new(() => null);
        scenario.Renderer.BodyStyle = declining;

        for (int tick = 0; tick < 3; tick++)
        {
            scenario.Renderer.Tick(enemyAlive: false);
        }

        Assert.Equal(1, declining.Asked);

        Style again = new(() => null);
        scenario.Renderer.BodyStyle = again;
        scenario.Renderer.Tick(enemyAlive: false);
        Assert.Equal(1, again.Asked);
    }

    // One enemy soldier, alone in the renderer, and a ray that meets him.
    private static MeshHitRay Target(Scenario scenario)
    {
        MeshHitRay ray = FindAnyMeshHitRay(
            scenario.Renderer,
            view => view.Soldier.Team != scenario.Renderer.Simulation.PlayerTeam && !CinematicActor.IsStaged(view.Soldier));
        KeepOnlyTarget(scenario.Renderer, MemberFor(scenario.Renderer, ray.View));
        return ray;
    }

    // One of the player's rounds along the ray, heavy enough to kill.
    private static void Shoot(Scenario scenario, MeshHitRay ray)
    {
        Vector3d origin = ray.Origin, end = ray.End;
        LevelBodies.Shoot(
            [scenario.Renderer],
            origin,
            end,
            scenario.Renderer.Simulation.PlayerTask,
            double.PositiveInfinity,
            alreadyHeard: false,
            struck => new Igi1WeaponHit(struck, origin, end, 1000f, WeaponCategory.Gun, Shooter: null, FromPlayer: true).ToShared(),
            out _);
    }

    private sealed class Style(Func<Body?> make) : ISoldierBodyStyle
    {
        public int Asked { get; private set; }

        public List<Body> Made { get; } = [];

        public ISoldierBody? Create(SoldierBodySoldier soldier)
        {
            Asked++;
            Body? body = make();
            if (body is not null)
            {
                Made.Add(body);
            }

            return body;
        }

        public void Frame(IReadOnlyList<ISoldierBody> bodies, float alpha, Camera3D? eye)
        {
        }
    }

    private sealed class Body : ISoldierBody
    {
        public bool ThrowsOnTick { get; init; }

        public bool Disposed { get; private set; }

        public bool HidesHeldWeapon => false;

        public void Tick()
        {
            if (ThrowsOnTick)
            {
                throw new InvalidOperationException("broken");
            }
        }

        public void Draw(in Camera3D camera, bool isEye, in Matrix4f modelView)
        {
        }

        public bool TryGetHeldWeapon(out Vector3f hand, out Matrix3f rotation)
        {
            (hand, rotation) = (default, default);
            return false;
        }

        public bool? TryMeetRound(Vector3d origin, Vector3d rayEnd, out HumanHit hit)
        {
            hit = default;
            return null;
        }

        public void Fell(SoldierFall fall)
        {
        }

        public void Dispose() => Disposed = true;
    }
}
