using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Mission 1's soldiers struck, hurt and killed, as <c>HumanSoldier_WeaponHitHandler</c> and the soldier's harm states take it.</summary>
public sealed class RetailIgi2SoldierHarmTests
{
    /// <summary>The soldier the tests shoot, <c>HumanSoldier_400</c>.</summary>
    private const int Target = 400;

    /// <summary>
    /// A line through a standing soldier strikes its collision mesh where its bones hold it, the bone recorded; a line over its head
    /// misses, and a soldier's own side's bullets pass through it (<c>Human_WeaponCollisionEvaluateHandler</c>, <c>igi2 0x4FF4E0</c>).
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_bullet_strikes_the_soldier_where_its_bones_hold_its_mesh()
    {
        (RetailIgi2Mission1 m, Igi2Soldiers soldiers) = Spawn();
        Igi2HumanSoldier body = Soldier(m, Target);
        Stand(body);

        Igi2Intersection? hit = Shoot(m.Collision!, body, height: 4096.0, includes: null);
        Assert.NotNull(hit);
        Igi2BoneCollider collider = Assert.IsType<Igi2BoneCollider>(hit!.HitObject);
        Assert.Same(body, collider.Owner);
        Assert.InRange(hit.Sector, 0, body.Animation.Rig!.BoneCount - 1);
        Assert.NotNull(hit.EnterMaterial);
        Assert.InRange(Math.Abs(hit.Position.Y - body.Position.Y), 0.0, 4096.0);

        Assert.Null(Shoot(m.Collision!, body, height: 40960.0, includes: null));

        Igi2HumanSoldier friend = soldiers.Bodies.First(b => !ReferenceEquals(b, body) && b.Team == body.Team);
        Igi2WeaponCollisionQuery own = new(friend, null, 1f);
        Assert.False(body.EvaluateWeaponCollision(own));
        Assert.False(body.EvaluateWeaponCollision(new Igi2WeaponCollisionQuery(body, null, 1f)));
        Assert.Null(Shoot(m.Collision!, body, height: 4096.0, includes: o => o is not IIgi2WeaponCollisionTarget t || t.EvaluateWeaponCollision(own)));
        friend.Team = body.Team + 1;
        Assert.True(body.EvaluateWeaponCollision(new Igi2WeaponCollisionQuery(friend, null, 1f)));
    }

    /// <summary>
    /// A soldier's limit is its type's hit points at the difficulty (<c>HumanAIConfig_SetAISettingsFromConfig</c>, <c>igi2 0x41ED66</c>);
    /// a light hit takes the struck zone's multiplier, counts toward a flinch and raises the hit bit, and the soldier flinches and
    /// stands again. The idle arms see the hit bit the same tick and clear the flinch's upper-body clip
    /// (<c>HumanSoldier_IdleUpperBodySignalHandler</c>, <c>igi2 0x4E3D26</c>), so the flinch ends with the idle arms' clip.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_light_hit_hurts_by_the_zone_and_the_soldier_flinches()
    {
        (RetailIgi2Mission1 m, _) = Spawn();
        Igi2HumanSoldier body = Soldier(m, Target);
        Igi2HumanSoldierTask task = m.Tree.OfType<Igi2HumanSoldierTask>().Single(static t => t.Id == Target);
        Igi2AiTweaks tweaks = m.Services!.Require<Igi2HumanAiConfig>().Tweaks(task.Ai!.AiType, (int)Game.Ai.AiDifficulty.Normal)!;
        Assert.Equal(tweaks.HitPoints, body.Damage.MaxDamage);
        Stand(body);

        Igi2Intersection hit = Shoot(m.Collision!, body, height: 4096.0, includes: null)!;
        Igi2WeaponHit shot = Hit(body, hit, 0.01f);
        Igi2DamagePart part = body.DamagePartOf(shot);
        body.WeaponHit(shot);

        float expected = ((Fpu)0.01f * body.Damage.Multiplier(part)).F;
        Assert.Equal(part, body.Damage.LastPart);
        Assert.Equal(expected, body.Damage.PartDamage(part));
        Assert.Equal(expected, body.Damage.Damage);
        Assert.Equal(expected, body.RecoilMeter);
        Assert.True((body.Signal & Igi2SoldierSignal.Hit) != 0);
        Assert.False(body.IsDead);

        List<int> states = Run(body, 600);
        Assert.Equal([Igi2SoldierState.Stand, Igi2SoldierState.Hit, Igi2SoldierState.Stand], states);
    }

    /// <summary>
    /// A hit past the limit kills: the dead bit and how it died are kept, the soldier enters its standing death, plays the next of
    /// the shared death clips, and lies dying once the clip has played.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_heavy_hit_kills_and_the_soldier_dies()
    {
        (RetailIgi2Mission1 m, _) = Spawn();
        Igi2HumanSoldier body = Soldier(m, Target);
        Stand(body);

        Igi2Intersection hit = Shoot(m.Collision!, body, height: 4096.0, includes: null)!;
        Igi2WeaponHit shot = Hit(body, hit, 100f);
        body.WeaponHit(shot);

        Assert.True(body.IsDead);
        Assert.True((body.Signal & Igi2SoldierSignal.Dead) != 0);
        Assert.Equal(100f, body.DeathDamage);
        Assert.Equal(shot.ShotVector, body.DeathDirection);
        Assert.Equal(3, body.DeathByType);

        List<int> states = Run(body, 150);
        Assert.Equal([Igi2SoldierState.Stand, Igi2SoldierState.Die], states);
        Assert.Equal(1, body.DeathCycle.Next);
        Assert.Equal(Igi2HumanSoldier.Dying, body.LifeState);
        Assert.True(body.IsDeathAnimationDone);
        Assert.Equal(Igi2HumanPose.LyingFlat, body.Pose);

        // A dead soldier takes nothing more.
        float taken = body.Damage.Damage;
        body.WeaponHit(shot);
        Assert.Equal(taken, body.Damage.Damage);
    }

    /// <summary>
    /// A body hit is to the chest when the shot came from in front and to the back when from behind; any hit from in front within
    /// 614.4 of the groin's mark is to the groin (<c>HumanDamage_GetDamagePartFromWeaponHit</c>, <c>igi2 0x469980</c>). The soldier's
    /// model has no magic vertices (its <c>XTVM</c> chunk is empty), so the mark is the soldier's own position.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_zone_turns_on_where_the_shot_came_from()
    {
        (RetailIgi2Mission1 m, _) = Spawn();
        Igi2HumanSoldier body = Soldier(m, Target);
        Stand(body);
        Igi2SoldierModel model = body.Model!;
        Assert.Null(model.Groin);
        int torso = Enumerable.Range(0, model.DamageParts.Count).First(b => model.DamageParts[b] == Igi2DamagePart.UpperBody);
        Matrix3f o = body.Orientation;
        Vector3f ahead = new(o.M12 * 4096f, o.M22 * 4096f, o.M32 * 4096f);
        Vector3d high = new(body.Position.X, body.Position.Y, body.Position.Z + 40960.0);

        Assert.Equal(Igi2DamagePart.Chest, body.DamagePartOf(At(body, torso, high, -ahead)));
        Assert.Equal(Igi2DamagePart.Back, body.DamagePartOf(At(body, torso, high, ahead)));

        Vector3d groin = model.GroinPosition(body.CurrentPose(), o, body.Position);
        Assert.Equal(body.Position, groin);
        Assert.Equal(Igi2DamagePart.Groin, body.DamagePartOf(At(body, torso, groin, -ahead)));
        Assert.Equal(Igi2DamagePart.Back, body.DamagePartOf(At(body, torso, groin, ahead)));
    }

    private static Igi2WeaponHit At(Igi2HumanSoldier body, int bone, Vector3d position, Vector3f shot) =>
        new(new Igi2Intersection { Sector = bone, Position = position, IsHit = true }, 0.01f, shot, null, body.Weapon.Inventory!.Current!.WeaponType, null);

    private static Igi2HumanSoldier Soldier(RetailIgi2Mission1 m, int id) =>
        m.Tree.OfType<Igi2HumanSoldierTask>().Single(t => t.Id == id).Body!;

    private static void Stand(Igi2HumanSoldier body)
    {
        for (int tick = 0; tick < 30; tick++)
        {
            body.RunDelayed();
        }
    }

    private static List<int> Run(Igi2HumanSoldier body, int ticks)
    {
        List<int> states = [body.Locomotion.CurrentState];
        for (int tick = 0; tick < ticks; tick++)
        {
            body.RunDelayed();
            if (states[^1] != body.Locomotion.CurrentState)
            {
                states.Add(body.Locomotion.CurrentState);
            }
        }

        return states;
    }

    /// <summary>A horizontal line along X through the soldier's upright axis, a height above where it stands.</summary>
    private static Igi2Intersection? Shoot(Igi2CollisionWorld world, Igi2HumanSoldier body, double height, Func<object, bool>? includes)
    {
        Igi2LineTrace trace = new(1) { Includes = includes };
        Vector3d p = body.Position;
        trace.Starts[0] = new Vector3d(p.X + 20480.0, p.Y, p.Z + height);
        trace.Ends[0] = new Vector3d(p.X - 20480.0, p.Y, p.Z + height);
        world.TraceLines(trace, Igi2CollisionWorld.HumanGroup);
        Igi2Intersection hit = trace.Intersections[0];
        return hit.IsHit ? hit : null;
    }

    private static Igi2WeaponHit Hit(Igi2HumanSoldier body, Igi2Intersection hit, float damage)
    {
        Igi2WeaponTypeTask gun = body.Weapon.Inventory!.Current!.WeaponType;
        return new Igi2WeaponHit(hit, damage, new Vector3f(-40960f, 0f, 0f), null, gun, null);
    }

    private static (RetailIgi2Mission1 Mission, Igi2Soldiers Soldiers) Spawn()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(m.Level);
        Igi2AnimationLibrary clips = Igi2AnimationLibrary.Load(packs, resources);
        Igi2Soldiers soldiers = Igi2Gameplay.SpawnSoldiers(
            m.Tree[0], m.Services!, m.Collision!, clips, m.Level.ModelFor, m.Services!.Require<Igi2PlayerTuning>(), m.Sounds, new IgiRandom(), Game.Ai.AiDifficulty.Normal);
        return (m, soldiers);
    }
}
