using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;
using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// The player struck by a bullet: its posed body in the collision world, the gate a bullet must pass
/// (<c>Human_WeaponCollisionEvaluateHandler</c>, <c>igi2 0x4FF4E0</c>) and the damage it takes (<c>HumanPlayer_WeaponHitHandler</c>,
/// <c>0x4BA0A0</c>; <c>HumanPlayer_HandleDamage</c>, <c>0x4B9AC0</c>).
/// </summary>
public sealed class RetailIgi2PlayerShotTests
{
    /// <summary>
    /// A bullet is let through to the player when the run stream's draw is no more than the weapon's hit probability, never when it
    /// is the player's own; the player hears it go by unless it fired it, and within fifteen level ticks only half the time.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_bullet_reaches_the_player_by_its_hit_probability()
    {
        (RetailIgi2Mission1 m, Igi2TitlePlayer player) = Player();
        Igi2HumanPlayer human = player.Human;
        human.LevelTick = static () => 100;

        Assert.False(human.EvaluateWeaponCollision(new Igi2WeaponCollisionQuery(null, null, -1f)));
        Assert.Equal(100, human.WhizzbyClock.LastTick);
        Assert.True(human.EvaluateWeaponCollision(new Igi2WeaponCollisionQuery(null, null, 1f)));
        Assert.False(human.EvaluateWeaponCollision(new Igi2WeaponCollisionQuery(human, null, 1f)));

        human.WhizzbyClock.LastTick = 0;
        human.EvaluateWeaponCollision(new Igi2WeaponCollisionQuery(human, null, 1f));
        Assert.Equal(0, human.WhizzbyClock.LastTick);
        Assert.NotNull(m.Collision);
    }

    /// <summary>
    /// A shot through the player's chest strikes its posed body; the hit adds the zone's share of its damage, flinches the player,
    /// shakes its view, flashes the screen brighter on the side it came from, and pushes the body along the shot.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_shot_through_the_chest_hurts_flashes_and_pushes_the_player()
    {
        (RetailIgi2Mission1 m, Igi2TitlePlayer player) = Player();
        Igi2HumanPlayer human = player.Human;
        Vector3d p = human.Position;
        Igi2LineTrace trace = new(1) { Includes = static o => o is not IIgi2WeaponCollisionTarget t || t.EvaluateWeaponCollision(new Igi2WeaponCollisionQuery(null, null, 1f)) };
        trace.Starts[0] = new Vector3d(p.X + 20480.0, p.Y, p.Z + 5120.0);
        trace.Ends[0] = new Vector3d(p.X - 20480.0, p.Y, p.Z + 5120.0);
        m.Collision!.TraceLines(trace, Igi2CollisionWorld.HumanGroup);
        Igi2Intersection hit = trace.Intersections[0];
        Assert.True(hit.IsHit);
        Assert.Same(human, hit.HitObject is Igi2BoneCollider collider ? collider.Owner : null);

        Igi2WeaponTypeTask gun = m.Services!.Require<Igi2HumanWeapons>().Tables.WeaponTypes.First(w => w.WeaponType == 0);
        float before = human.Damage.Damage;
        Vector3f shot = new(-40960f, 0f, 0f);
        human.WeaponHit(new Igi2WeaponHit(hit, 0.05f, shot, null, gun, null));

        Assert.True(human.Damage.Damage > before);
        Assert.NotEqual(Igi2DamagePart.None, human.Damage.LastPart);
        Assert.Equal(1f, human.View.ShakeFactor);
        Assert.InRange(human.View.ShakeRange, 204.8f * 0.25f, 204.8f);
        Assert.Contains(human.DamageFlash, static f => f > 0f);
        Assert.True(human.ImpactSpeed.X < 0f);
        Assert.Equal(0f, human.ImpactSpeed.Z);
    }

    /// <summary>
    /// Each pose after the clips step eases the view's shoulder pitch a fifth of the way toward the view's pitch while the player
    /// stands (<c>HumanPlayer_LowerBackCallback</c>, <c>igi2 0x4B93F0</c>), and tilts the spine by it; between steps the pose is kept.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_spine_follows_the_views_pitch_a_fifth_at_a_time()
    {
        (_, Igi2TitlePlayer player) = Player();
        Igi2HumanPlayer human = player.Human;
        human.View.IdealPitch = 0.4f;
        player.Tick(RetailIgi2Mission1.Idle);
        float settled = human.View.ShouldersPitch;
        float pitch = human.View.Pitch;
        Assert.NotEqual(0f, pitch);
        human.BodyPose();
        Assert.Equal(((((Fpu)pitch - settled) * 0.2f) + settled).F, human.View.ShouldersPitch);

        float kept = human.View.ShouldersPitch;
        human.BodyPose();
        Assert.Equal(kept, human.View.ShouldersPitch);
    }

    private static (RetailIgi2Mission1 Mission, Igi2TitlePlayer Player) Player()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        Assert.True(PlayerSpawn.TryFindGameplay(m.Level.Root, out PlayerSpawnPoint point));
        Igi2TitlePlayer player = RetailIgi2Mission1.Stand(m.Collision!, m.Level, null, point.Position, point.Gamma);
        player.Human.Model = Igi2BodyModel.From(point.Model, m.Level.ModelFor(point.Model));
        player.Human.Collider = new Igi2BoneCollider(player.Human, player.Human);
        m.Collision!.AddBody(player.Human.Collider);
        return (m, player);
    }
}
