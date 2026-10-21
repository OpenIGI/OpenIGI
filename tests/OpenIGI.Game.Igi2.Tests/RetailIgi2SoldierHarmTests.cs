using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Weapons;
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
        WeaponCollisionQuery own = new(friend, null, 1f);
        Assert.False(body.EvaluateWeaponCollision(own));
        Assert.False(body.EvaluateWeaponCollision(new WeaponCollisionQuery(body, null, 1f)));
        Assert.Null(Shoot(m.Collision!, body, height: 4096.0, includes: o => o is not IWeaponCollisionTarget t || t.EvaluateWeaponCollision(own)));
        friend.Team = body.Team + 1;
        Assert.True(body.EvaluateWeaponCollision(new WeaponCollisionQuery(friend, null, 1f)));
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
        Igi2BodyModel model = body.Model!;
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

    /// <summary>
    /// A dead soldier's bones are its death clip alone: the upper body cleared, the aim callbacks off, the root held at nought. The
    /// bones' 24-bit composition (<c>BoneDynCubeObj_UpdateHierarchy</c>, <c>igi2 0x564B50</c>) lands within a hundredth of a unit of
    /// the same clip sampled and composed in double precision, and the root bone stands at the root.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_dead_soldier_is_posed_by_its_death_clip_alone()
    {
        (RetailIgi2Mission1 m, _) = Spawn();
        Igi2HumanSoldier body = Soldier(m, Target);
        Stand(body);
        body.WeaponHit(Hit(body, Shoot(m.Collision!, body, height: 4096.0, includes: null)!, 100f));
        Run(body, 20);

        Igi2HumanAnimation animation = Assert.IsType<Igi2HumanAnimation>(body.Animation);
        Assert.Null(animation.UpperBody);
        Formats.Animation.BonePose exact = body.CurrentPose()!;
        Formats.Animation.Igi2AnimationClip clip = animation.Main.Clip!;
        Formats.Animation.Igi2BoneHierarchy rig = animation.Rig!;
        Quaternion32[] local = new Quaternion32[rig.BoneCount];
        Formats.Animation.Igi2ClipSampler.Sample(clip, animation.Main.Time, squad: false, local);
        Formats.Animation.BonePose reference = Formats.Animation.BonePose.Evaluate(rig.CreateSkeleton(), local, default);

        Assert.Equal(default, body.RootTranslation);
        Assert.Equal(default, exact.Positions[0]);
        for (int bone = 0; bone < rig.BoneCount; bone++)
        {
            Vector3f a = exact.Positions[bone], b = reference.Positions[bone];
            Assert.True(Math.Abs(a.X - b.X) < 0.01f && Math.Abs(a.Y - b.Y) < 0.01f && Math.Abs(a.Z - b.Z) < 0.01f, $"bone {bone}: {a} against {b}");
            Matrix3f r = exact.Rotations[bone], s = reference.Rotations[bone];
            Assert.True(Math.Abs(r.M12 - s.M12) < 1e-4f && Math.Abs(r.M23 - s.M23) < 1e-4f && Math.Abs(r.M31 - s.M31) < 1e-4f, $"bone {bone} turns otherwise");
        }
    }

    /// <summary>
    /// A standing soldier's idle arms play on the <c>lower body</c> branch, its spine, arms and head (<c>HumanAnim_Init</c>,
    /// <c>igi2 0x58F930</c>); its legs stay on the main clip, within a hundredth of a unit of that clip sampled in double precision.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_standing_soldier_keeps_its_legs_on_the_main_clip()
    {
        (RetailIgi2Mission1 m, _) = Spawn();
        Igi2HumanSoldier body = Soldier(m, Target);
        Stand(body);
        Igi2HumanAnimation animation = Assert.IsType<Igi2HumanAnimation>(body.Animation);
        Assert.NotNull(animation.UpperBody);
        Formats.Animation.Igi2BoneHierarchy rig = animation.Rig!;
        bool[] upper = Igi2BoneBranches.Mark(rig, body.Model!.LowerBodyBone, include: true);
        Formats.Animation.BonePose exact = body.CurrentPose()!;
        Quaternion32[] local = new Quaternion32[rig.BoneCount];
        Vector3f root = Formats.Animation.Igi2ClipSampler.Sample(animation.Main.Clip!, animation.Main.Time, squad: false, local);
        Formats.Animation.BonePose reference = Formats.Animation.BonePose.Evaluate(rig.CreateSkeleton(), local, root);

        int legs = 0;
        for (int bone = 0; bone < rig.BoneCount; bone++)
        {
            if (upper[bone])
            {
                continue;
            }

            legs++;
            Vector3f a = exact.Positions[bone], b = reference.Positions[bone];
            Assert.True(Math.Abs(a.X - b.X) < 0.01f && Math.Abs(a.Y - b.Y) < 0.01f && Math.Abs(a.Z - b.Z) < 0.01f, $"bone {bone}: {a} against {b}");
        }

        Assert.Equal(11, legs);
        Assert.Equal(root, body.RootTranslation);
    }

    /// <summary>
    /// A dying soldier drops the weapon in its hand (<c>HumanSoldier_CreateGunPickups</c>, <c>igi2 0x4E8360</c>): a Makarov with nine
    /// tenths of a clip at normal difficulty, thrown short from its hand along its heading, which falls, lands with a sound and comes to
    /// rest 122.88 above the ground on its side (<c>GunPickup_RunHandler</c>, <c>0x46A760</c>).
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_dying_soldier_drops_its_weapon_which_falls_and_lands()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(m.Level);
        Igi2AnimationLibrary clips = Igi2AnimationLibrary.Load(packs, resources);
        Igi2HumanWeapons armoury = m.Services!.Require<Igi2HumanWeapons>();
        Pickups.Igi2PickupList pickups = Pickups.Igi2PickupList.Build(m.Tree[0], armoury.Tables, m.Level.ModelFor, m.Collision!, m.Materials);
        List<string> sounds = [];
        pickups.Collision = m.Collision;
        pickups.Random = new IgiRandom();
        pickups.PlaySound = (name, _) => sounds.Add(name);
        int placed = pickups.Items.Count;
        Igi2Gameplay.SpawnSoldiers(
            m.Tree[0], m.Services!, m.Collision!, clips, m.Level.ModelFor, m.Services!.Require<Igi2PlayerTuning>(), m.Sounds, new IgiRandom(),
            Game.Ai.AiDifficulty.Normal, pickups);
        Igi2HumanSoldier body = Soldier(m, Target);
        Stand(body);
        Igi2WeaponTypeTask makarov = body.Weapon.Inventory!.Current!.WeaponType;

        body.WeaponHit(Hit(body, Shoot(m.Collision!, body, height: 4096.0, includes: null)!, 100f));
        body.RunDelayed();

        Pickups.Igi2GunPickup drop = Assert.Single(pickups.Items.Skip(placed));
        Assert.Same(makarov, drop.WeaponType);
        int clip = makarov.AmmoInfo(0, armoury.Tables).RoundsPerClip;
        Assert.Equal(Math.Max(1, (int)(clip * BitConverter.Int32BitsToSingle(0x3F666666))), drop.AmmoInClip);
        Assert.True(drop.IsMoving);
        Assert.InRange((drop.Position - body.Position).Length, 0.0, 12288.0);

        for (int tick = 0; tick < 120 && drop.IsMoving; tick++)
        {
            pickups.Run();
        }

        Assert.False(drop.IsMoving);
        Assert.True(drop.IsLanded);
        Assert.Single(sounds);
        Assert.StartsWith("weapondrop_0", sounds[0], StringComparison.Ordinal);
        Igi2LineTrace ground = new(1);
        ground.Starts[0] = drop.Position;
        ground.Ends[0] = new Vector3d(drop.Position.X, drop.Position.Y, drop.Position.Z - 8192.0);
        m.Collision!.TraceLines(ground, Igi2CollisionWorld.TerrainGroup);
        Assert.True(ground.Intersections[0].IsHit);
        Assert.Equal(122.88, drop.Position.Z - ground.Intersections[0].Position.Z, 0.05);
    }

    /// <summary>
    /// A blast reaches a soldier's eyes and lands on its chest (<c>HumanSoldier_ExplosionHitHandler</c>, <c>igi2 0x4EB8E0</c>): a weak
    /// one hurts and flinches it and shakes its view, a strong one kills it, how it died nought; one set off by its own side, or one
    /// too far away, does nothing.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_blast_lands_on_the_chest_and_spares_the_soldiers_own_side()
    {
        (RetailIgi2Mission1 m, Igi2Soldiers soldiers) = Spawn();
        Igi2HumanSoldier body = Soldier(m, Target);
        Stand(body);
        Igi2ExplosionWorld world = new(new IgiRandom(), m.Collision!, m.Materials, new World.Igi2DynamicTasks());
        Vector3d near = new(body.Position.X + 4096.0, body.Position.Y, body.Position.Z + 4096.0);
        Igi2ExplosionHit Blast(Vector3d at, float damage, IIgi2WeaponOwner? owner, int team) =>
            new(world, at, team, damage, 2048f, 8192f, Igi2ExplosionWorld.ShakeTime, null, owner, Igi2ExplosionFalloff.Linear);

        Igi2HumanSoldier friend = soldiers.Bodies.First(b => !ReferenceEquals(b, body) && b.Team == body.Team);
        body.ExplosionHit(Blast(near, 100f, friend, body.Team));
        Assert.Equal(0f, body.Damage.Damage);

        body.ExplosionHit(Blast(new Vector3d(body.Position.X + 409600.0, body.Position.Y, body.Position.Z), 100f, null, -1));
        Assert.Equal(0f, body.Damage.Damage);

        body.ExplosionHit(Blast(near, 0.01f, null, -1));
        Assert.Equal(Igi2DamagePart.Chest, body.Damage.LastPart);
        Assert.True(body.Damage.PartDamage(Igi2DamagePart.Chest) > 0f);
        Assert.True((body.Signal & Igi2SoldierSignal.Hit) != 0);
        Assert.False(body.IsDead);
        Assert.True(body.View.ShakeRange > 0f);

        body.ExplosionHit(Blast(near, 100f, null, -1));
        Assert.True(body.IsDead);
        Assert.Equal(0, body.DeathByType);
        Assert.Equal(100f, body.DeathDamage);
        Assert.Equal((float)(body.Position.X - near.X), body.DeathDirection.X, 1f);
    }

    /// <summary>
    /// Another game's round and blast reach the soldier through the engine's shared events: the round lands on the zone of the
    /// bone it names at that zone's multiplier, a knife's blow unmultiplied, and the blast lands on the chest with no world to
    /// shield it.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Another_games_round_and_blast_hurt_the_soldier()
    {
        (RetailIgi2Mission1 m, _) = Spawn();
        Igi2HumanSoldier body = Soldier(m, Target);
        Stand(body);
        Igi2Intersection hit = Shoot(m.Collision!, body, height: 4096.0, includes: null)!;
        object foreign = new();
        Vector3f shot = new(-40960f, 0f, 0f);

        ((IWeaponHitTarget)body).WeaponHit(new WeaponHit(hit.Position, hit.Normal, 0.01f, shot, null, foreign, WeaponCategory.Gun, hit.Sector));
        Igi2DamagePart part = body.DamagePartOf(Hit(body, hit, 0.01f));
        float expected = ((Fpu)0.01f * body.Damage.Multiplier(part)).F;
        Assert.Equal(part, body.Damage.LastPart);
        Assert.Equal(expected, body.Damage.PartDamage(part));

        ((IWeaponHitTarget)body).WeaponHit(new WeaponHit(hit.Position, hit.Normal, 0.01f, shot, null, foreign, WeaponCategory.Knife, hit.Sector));
        Assert.Equal(((Fpu)expected + 0.01f).F, body.Damage.PartDamage(part));

        float before = body.Damage.PartDamage(Igi2DamagePart.Chest);
        Vector3d near = new(body.Position.X + 4096.0, body.Position.Y, body.Position.Z + 4096.0);
        ((IExplosionHitTarget)body).ExplosionHit(new ExplosionHit(near, -1, 0.01f, 2048f, 8192f, null, foreign));
        Assert.Equal(Igi2DamagePart.Chest, body.Damage.LastPart);
        Assert.True(body.Damage.PartDamage(Igi2DamagePart.Chest) > before);
        Assert.False(body.IsDead);

        ((IWeaponHitTarget)body).WeaponHit(new WeaponHit(hit.Position, hit.Normal, 1000f, shot, null, foreign, WeaponCategory.Pistol, hit.Sector));
        Assert.True(body.IsDead);
        Assert.Equal(3, body.DeathByType);
    }

    /// <summary>
    /// Another game's shot finds the soldiers among the level's bodies: the line meets a soldier's own bone collider on the bone
    /// this game's world finds, its task takes the hit, and the dead stop no more rounds.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Another_games_shot_finds_the_soldier_among_the_levels_bodies()
    {
        (RetailIgi2Mission1 m, _) = Spawn();
        Igi2HumanSoldier body = Soldier(m, Target);
        Stand(body);
        Igi2Intersection expected = Shoot(m.Collision!, body, height: 4096.0, includes: null)!;
        Igi2ShootableSoldiers soldiers = new(m.Tree.OfType<Ai.Igi2HumanSoldierTask>());
        LevelBodies bodies = new();
        bodies.Add(soldiers);

        Vector3d p = body.Position;
        Vector3d from = new(p.X + 20480.0, p.Y, p.Z + 4096.0);
        Vector3d to = new(p.X - 20480.0, p.Y, p.Z + 4096.0);
        static WeaponHit Round(Game.Player.HumanHit met) =>
            new(met.Position, default, 0.01f, new Vector3f(-40960f, 0f, 0f), null, new object(), WeaponCategory.Gun, met.Bone);

        Assert.True(bodies.Shoot(from, to, null, Round, out Game.Player.HumanHit hit));
        Assert.Equal(expected.Sector, hit.Bone);
        Assert.Equal(expected.Position.X, hit.Position.X, 1.0);
        Assert.Equal(body.DamagePartOf(Hit(body, expected, 0.01f)), body.Damage.LastPart);
        Assert.True(body.Damage.Damage > 0f);

        Assert.True(bodies.Shoot(from, to, null, met => Round(met) with { Damage = 1000f }, out _));
        Assert.True(body.IsDead);
        Assert.DoesNotContain(soldiers.Bodies(), b => b.Task is Ai.Igi2HumanSoldierTask { Body: var held } && ReferenceEquals(held, body));
    }

    /// <summary>
    /// A blow from in front lands on the chest and one from behind on the back (<c>HumanSoldier_CloseCombatDamageHandler</c>,
    /// <c>igi2 0x4EBCD0</c>), each at its zone's multiplier; enough of them kill, how it died 4.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_blow_lands_on_the_side_the_attacker_struck_from()
    {
        (RetailIgi2Mission1 m, Igi2Soldiers soldiers) = Spawn();
        Igi2HumanSoldier body = Soldier(m, Target);
        Stand(body);
        Igi2HumanSoldier attacker = soldiers.Bodies.First(b => !ReferenceEquals(b, body));
        Matrix3f o = body.Orientation;
        Vector3d ahead = new(body.Position.X + (o.M12 * 4096f), body.Position.Y + (o.M22 * 4096f), body.Position.Z);
        Vector3d behind = new(body.Position.X - (o.M12 * 4096f), body.Position.Y - (o.M22 * 4096f), body.Position.Z);

        attacker.Position = ahead;
        body.CloseCombatDamage(0.01f, attacker);
        Assert.Equal(Igi2DamagePart.Chest, body.Damage.LastPart);
        Assert.Equal(((Fpu)body.Damage.Multiplier(Igi2DamagePart.Chest) * 0.01f).F, body.Damage.PartDamage(Igi2DamagePart.Chest));
        Assert.True((body.Signal & Igi2SoldierSignal.Hit) != 0);

        attacker.Position = behind;
        body.CloseCombatDamage(0.01f, attacker);
        Assert.Equal(Igi2DamagePart.Back, body.Damage.LastPart);

        body.CloseCombatDamage(100f, attacker);
        Assert.True(body.IsDead);
        Assert.Equal(4, body.DeathByType);
    }

    /// <summary>
    /// A line of sight through a standing soldier's chest is stopped by its posed mesh (<c>BoneDynCubeObj_LineOfSightHandler</c>,
    /// <c>igi2 0x566B90</c>) when the query asks for humans, and passes when it does not, or when it runs well clear of it.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_soldier_blocks_a_line_of_sight_that_asks_for_humans()
    {
        (RetailIgi2Mission1 m, _) = Spawn();
        Igi2HumanSoldier body = Soldier(m, Target);
        Stand(body);
        Vector3d p = body.Position;
        Igi2LineOfSight Line(double offset) =>
            new(new Vector3d(p.X - 8192.0, p.Y + offset, p.Z + 4096.0), new Vector3d(p.X + 8192.0, p.Y + offset, p.Z + 4096.0), m.Materials);

        Igi2LineOfSight through = Line(0.0);
        Assert.False(m.Collision!.LineOfSight(through, Igi2CollisionWorld.HumanGroup));
        Assert.True(through.IsHit);

        Igi2LineOfSight unasked = Line(0.0);
        Assert.True(m.Collision.LineOfSight(unasked, Igi2CollisionWorld.SplineGroup));

        Igi2LineOfSight wide = Line(40960.0);
        Assert.True(m.Collision.LineOfSight(wide, Igi2CollisionWorld.HumanGroup));
        Assert.Equal(1f, wide.Transparency);
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

    /// <summary>
    /// A soldier told to throw plays the near throw at a target 8192 away (<c>HumanSoldier_InitStateStandThrowGrenade</c>,
    /// <c>igi2 0x4E6470</c>); when the clip lets go, its mind's grenade leaves the right hand with a fuse of its flight and fifteen
    /// ticks more, once only (<c>RunStateStandThrowGrenade</c>, <c>0x4E6540</c>); and the clip's end stands it again.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_soldier_throws_its_grenade_from_the_hand()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(m.Level);
        Igi2AnimationLibrary clips = Igi2AnimationLibrary.Load(packs, resources);
        Igi2Grenades grenades = new(
            m.Services!.Require<OpenIGI.Game.Physics.PhysicsObjectTypes>(), m.Level.ModelFor, new Physics.Igi2PhysicsWorld(m.Collision!, m.Materials), new World.Igi2DynamicTasks());
        Igi2Gameplay.SpawnSoldiers(
            m.Tree[0], m.Services!, m.Collision!, clips, m.Level.ModelFor, m.Services!.Require<Igi2PlayerTuning>(), m.Sounds, new IgiRandom(),
            Game.Ai.AiDifficulty.Normal, grenades: grenades);
        Igi2HumanSoldier body = Soldier(m, Target);
        Stand(body);
        ThrowingMind mind = new() { Target = new Vector3d(body.Position.X + 8192.0, body.Position.Y, body.Position.Z) };
        body.Mind = mind;
        body.RunDelayed();

        mind.Frame[(int)Igi2SoldierChannel.ThrowGrenade] = 1f;
        body.RunDelayed();
        mind.Frame[(int)Igi2SoldierChannel.ThrowGrenade] = 0f;
        Assert.Equal(Igi2SoldierState.StandThrowGrenade, body.Locomotion.CurrentState);
        Assert.False(body.IsThrowGrenadeTriggered);

        body.Signal |= Igi2SoldierSignal.GrenadeReleased;
        body.RunDelayed();
        Igi2Grenade grenade = Assert.Single(grenades.Flying);
        Assert.Equal(ThrowingMind.FlyTime + Igi2HumanSoldier.GrenadeFuseMargin, grenade.Throw.ExplodeTime);
        Assert.InRange((grenade.Throw.Position - body.Position).Length, 1.0, 8192.0);
        Assert.Same(body, grenade.Throw.Thrower);
        Assert.True(body.IsThrowGrenadeTriggered);
        Assert.Contains(Igi2HumanSoldier.GrenadeShout, mind.Said);

        body.Signal |= Igi2SoldierSignal.GrenadeReleased;
        body.RunDelayed();
        Assert.Single(grenades.Flying);

        for (int tick = 0; tick < 300 && body.Locomotion.CurrentState == Igi2SoldierState.StandThrowGrenade; tick++)
        {
            body.RunDelayed();
        }

        Assert.Equal(Igi2SoldierState.Stand, body.Locomotion.CurrentState);
    }

    /// <summary>A mind that sends a fixed frame, carries a hand grenade and aims it straight at its target.</summary>
    private sealed class ThrowingMind : IIgi2SoldierMind
    {
        public const int FlyTime = 20;

        public float[] Frame { get; } = new float[Igi2HumanChannels.Count];

        public List<string> Said { get; } = [];

        public Vector3d Target { get; init; }

        public bool HasStarted => true;

        public string? GrenadeWeaponId => "WEAPON_ID_GRENADE";

        public Vector3d? GrenadeTargetPosition => Target;

        public void Run(Igi2HumanSoldier soldier) => soldier.Update(Frame);

        public void Say(Igi2HumanSoldier soldier, string sound) => Said.Add(sound);

        public bool TryAimGrenade(Igi2HumanSoldier soldier, Vector3d hand, out Vector3f speed, out float[] orientation, out int flyTime)
        {
            speed = new Vector3f(409.6f, 0f, 204.8f);
            orientation = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];
            flyTime = FlyTime;
            return true;
        }
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
