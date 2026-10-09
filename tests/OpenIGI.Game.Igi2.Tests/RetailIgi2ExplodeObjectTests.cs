using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// Mission 1's barrels, its five <c>ExplodeObject</c>s (<c>ExplodeObject_RunHandler</c>, <c>igi2 0x49C850</c>): a bullet's damage
/// explodes one the next tick, a blast's sets one off after its delay, and an exploded one wears its destroyed model and goes
/// off at that model's first magic vertex.
/// </summary>
public sealed class RetailIgi2ExplodeObjectTests
{
    /// <summary>The barrel by the outer fence, <c>ExplodeObject_3830</c>, whose destroyed model is a model of its own.</summary>
    private const int Barrel = 3830;

    /// <summary>A barrel whose destroyed model is its whole one, <c>ExplodeObject_3668</c>.</summary>
    private const int SameModel = 3668;

    /// <summary>All five are typed, with their authored parameters, and each is placed with its destroyed model beside its own.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_barrels_are_read_with_their_destroyed_models()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2ExplodeObjectTask[] barrels = [.. m.Tree.OfType<Igi2ExplodeObjectTask>()];
        Assert.Equal([3405, 3406, 3665, 3668, 3830], barrels.Select(static b => b.Id).Order());

        Igi2ExplodeObjectTask barrel = barrels.Single(static b => b.Id == Barrel);
        Assert.Equal(("333_02_1", "342_02_1"), (barrel.Model, barrel.DestroyedModel));
        Assert.Equal((1.5f, 2f, 1.5f, 1f, 0.2f), (barrel.DamageScale, barrel.ExplosionRadius, barrel.ExplosionFalloffRadius, barrel.ExplosionDamageScale, barrel.ExplosionDelay));
        Assert.Equal((3, 3, "explo_03_l"), (barrel.ExplosionFragments, barrel.ExplosionFireballs, barrel.ExplosionSound));
        Assert.False(barrel.Explosive.IsExploded);
        Assert.Equal(-1, barrel.Explosive.Delay);

        Igi2Placement placed = m.Level.Scene.Placements.Single(static p => p.LevelId == Barrel);
        Assert.Equal(("333_02_1", "342_02_1"), (placed.Model, placed.DestroyedModel));
    }

    /// <summary>
    /// Bullets add their damage; once it reaches the damage scale the barrel explodes the next tick, wears its destroyed model,
    /// sets <c>isExploded</c>, sends a blast into the level's tasks and takes no more damage.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Enough_bullets_explode_a_barrel_the_next_tick()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        (Igi2ExplodeObjectTask barrel, Igi2DynamicTasks dynamic) = Arm(m, Barrel);
        Igi2RigidObject solid = m.Collision!.SolidsOf(Igi2ExplodeObjectTask.TypeName, Barrel)[0];
        Assert.Same(barrel, solid.Handler);

        barrel.Explosive.WeaponHit(1f);
        barrel.Run(m.Run);
        Assert.Equal((1f, -1, false), (barrel.Explosive.Damage, barrel.Explosive.Delay, barrel.Explosive.IsExploded));

        barrel.Explosive.WeaponHit(1f);
        Assert.Equal((2f, 0), (barrel.Explosive.Damage, barrel.Explosive.Delay));
        Assert.Empty(dynamic.Tasks);
        barrel.Run(m.Run);
        Assert.True(barrel.Explosive.IsExploded);
        Assert.Equal("342_02_1", barrel.Explosive.Model);
        Assert.Single(dynamic.Tasks.OfType<Igi2Explosion>());
        Assert.Equal(1.0, m.Run.Evaluate(new Igi2LevelExpression($"ExplodeObject_{Barrel}.isExploded", "test", m.Run.Natives)));

        barrel.Explosive.WeaponHit(5f);
        Assert.Equal(2f, barrel.Explosive.Damage);
        barrel.Run(m.Run);
        Assert.Single(dynamic.Tasks.OfType<Igi2Explosion>());

        Igi2PlacementDestruction destruction = new([barrel]);
        Assert.True(destruction.IsDestroyed(Barrel));
        Assert.False(destruction.IsDestroyed(SameModel));
    }

    /// <summary>
    /// A blast that does a barrel its damage scale sets it off after its delay, <c>(int)(0.2 * 30)</c> = six ticks counted down
    /// one a tick, and it explodes on the tick the count reaches nought.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_blast_sets_a_barrel_off_after_its_delay()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        (Igi2ExplodeObjectTask barrel, Igi2DynamicTasks dynamic) = Arm(m, SameModel);
        Igi2ExplosionWorld blasts = barrel.Explosive.Explosions!;

        Igi2ExplosionHit hit = new(blasts, barrel.Position, -1, 2f, 8192f, 6144f, 90, null, null, Igi2ExplosionFalloff.Linear);
        barrel.ExplosionHit(hit);
        Assert.Equal(6, barrel.Explosive.Delay);
        Assert.True(barrel.Explosive.Damage >= barrel.DamageScale);

        for (int tick = 0; tick < 6; tick++)
        {
            Assert.False(barrel.Explosive.IsExploded);
            barrel.Run(m.Run);
        }

        Assert.Equal(0, barrel.Explosive.Delay);
        Assert.False(barrel.Explosive.IsExploded);
        barrel.Run(m.Run);
        Assert.True(barrel.Explosive.IsExploded);
        Assert.Equal("333_02_1", barrel.Explosive.Model);
        Assert.Single(dynamic.Tasks.OfType<Igi2Explosion>());
    }

    private static (Igi2ExplodeObjectTask Barrel, Igi2DynamicTasks Dynamic) Arm(RetailIgi2Mission1 m, int id)
    {
        Igi2ExplodeObjectTask barrel = m.Tree.OfType<Igi2ExplodeObjectTask>().Single(b => b.Id == id);
        Igi2DynamicTasks dynamic = new();
        Igi2ExplosionWorld blasts = new(new IgiRandom(0x1234567u), m.Collision!, m.Materials, dynamic);
        barrel.Explosive.Models = m.Level.ModelFor;
        barrel.Explosive.Explosions = blasts;
        barrel.Explosive.Bind(m.Collision!.SolidsOf(Igi2ExplodeObjectTask.TypeName, id));
        blasts.AddTarget(barrel);
        return (barrel, dynamic);
    }
}
