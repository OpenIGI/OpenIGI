using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Systems;

/// <summary>
/// The world Project I.G.I.'s soldiers march their rounds through in another game's level: the level's own collision, and the
/// soldiers themselves as the game tests its own.
/// </summary>
/// <remarks>
/// <para>
/// A round is cast through the level's collision (<see cref="IHostCollisionWorld.TraceLines"/>), which holds what the level
/// places, its humans and its ground; the soldiers stand in it too, as the level's own rounds meet them, but a soldier's round
/// meets the soldiers bone by bone as the game's own does (<see cref="SoldierSimulation.TryCastRoundAtSoldiers"/>), so they are
/// left out of the level's cast. Whatever is nearer is struck.
/// </para>
/// <para>
/// What the level holds takes the round through its collider's handler (<see cref="IWorldCollider.Handler"/>), as a hit both
/// games know (<see cref="WeaponHit"/>), its damage the round's times <see cref="DamageScale"/>.
/// </para>
/// <para>
/// The game's own player standing in the level (<see cref="Player"/>) is met as the game's own missions meet theirs
/// (<c>GuardRounds</c>): its body in the pose it stands in, and only through the evaluate's two rolls of the shooter's hit
/// probability (<c>0x45F53D</c>, <c>0x4D76DA</c>); a round that fails them flies past it. The level's own stand-in for it is
/// left out of the level's cast, so a round does not meet the player twice.
/// </para>
/// </remarks>
/// <param name="world">The level's collision.</param>
/// <param name="soldiers">The soldiers, whose bodies the rounds meet bone by bone.</param>
/// <param name="arsenal">The game's tables: what a surface is made of, and what a weapon fires.</param>
/// <param name="materialOf">The game's material row for a row of the level's, or <see langword="null"/> where it has none.</param>
/// <param name="isSoldier">Whether a collider's owner is one of the soldiers, which the level's cast leaves out.</param>
internal sealed class Igi1HostRounds(
    IHostCollisionWorld world,
    SoldierSimulation soldiers,
    Igi1Arsenal arsenal,
    Func<int, int?> materialOf,
    Func<object, bool> isSoldier) : IGunShotWorld
{
    private readonly WorldLineTrace _trace = new(1);

    /// <summary>Gets or sets what a round's damage is multiplied by as it strikes what the level holds.</summary>
    public float DamageScale { get; set; } = 1f;

    /// <summary>Gets or sets the game's own player in the level, as a round meets it; <see langword="null"/> while it has none.</summary>
    public Func<Igi1HostedPlayerTarget?>? Player { get; set; }

    /// <summary>Gets or sets the stream the hit probability is rolled from: the level's, as the game's own missions roll it.</summary>
    public IgiRandom Rolls { get; set; } = new();

    /// <summary>Gets or sets what plays a round's sound, by its definition, where it sounds; <see langword="null"/> for silence.</summary>
    public Action<string, Vector3d>? Sound { get; set; }

    /// <summary>Gets or sets what shows a round's impact, once its contact is resolved; <see langword="null"/> for none.</summary>
    public Action<GunShotRound, GunShotRecord>? Impact { get; set; }

    /// <inheritdoc/>
    public bool Cast(GunShotRound round, Vector3d start, Vector3d limit, out GunShotContact contact)
    {
        contact = default;
        bool found = false;
        double best = double.PositiveInfinity;
        SoldierView? shooter = round.Shooter as SoldierView;
        object? shooterTask = shooter?.Soldier;
        Igi1HostedPlayerTarget? player = Player?.Invoke();
        object? playerTask = player?.Task;

        _trace.Starts[0] = start;
        _trace.Ends[0] = limit;
        _trace.Includes = owner => !ReferenceEquals(owner, shooterTask) && !isSoldier(owner) && !ReferenceEquals(owner, playerTask);
        world.TraceLines(_trace);
        WorldIntersection met = _trace.Intersections[0];
        if (met.IsHit)
        {
            bool exit = met.EnterMaterial is null && met.LeaveMaterial is not null;
            GameMaterial? row = Material(met.EnterMaterial ?? met.LeaveMaterial);
            bool human = met.HitObject?.Owner is IAiHuman;
            contact = new GunShotContact(
                met.Position,
                met.Normal,
                met.Distance,
                exit ? null : row,
                exit ? row : null,
                HasObject: met.HitObject is not null,
                StruckHuman: human,
                new LevelContact(met.HitObject, met.Position, met.Normal));
            best = met.Distance;
            found = true;
        }

        if (soldiers.TryCastRoundAtSoldiers(start, limit, shooter, shooter?.Soldier.Team ?? 1, out SoldierView? struck, out HumanHit body)
            && struck is not null
            && body.Distance < best)
        {
            GameMaterial? skin = Material(body.Material >= 0 ? body.Material : null)
                ?? (arsenal.Materials.TryGetByName(BulletImpactSounds.FleshMaterialName, out GameMaterial flesh) ? flesh : null);
            contact = new GunShotContact(
                body.Position,
                body.Normal,
                body.Distance,
                body.IsExit ? null : skin,
                body.IsExit ? skin : null,
                HasObject: true,
                StruckHuman: true,
                new SoldierContact(struck, body));
            best = body.Distance;
            found = true;
        }

        // The player only from another side, and only through the evaluate's two rolls, each its own draw.
        if (player is { } target
            && (shooter?.Soldier.Team ?? 1) != soldiers.PlayerTeam
            && CastPlayer(target, start, limit) is { } met2
            && met2.Distance < best
            && Rolls.NextDouble() <= round.Probability
            && Rolls.NextDouble() <= round.Probability)
        {
            GameMaterial? skin = Material(met2.Material >= 0 ? met2.Material : null)
                ?? (arsenal.Materials.TryGetByName(BulletImpactSounds.FleshMaterialName, out GameMaterial flesh) ? flesh : null);
            contact = new GunShotContact(
                met2.Position,
                met2.Normal,
                met2.Distance,
                met2.IsExit ? null : skin,
                met2.IsExit ? skin : null,
                HasObject: true,
                StruckHuman: true,
                new PlayerContact(target, met2));
            found = true;
        }

        return found;
    }

    /// <inheritdoc/>
    public void Strike(GunShotRound round, in GunShotContact contact, Vector3f travel)
    {
        Vector3d end = new(round.Origin.X + travel.X, round.Origin.Y + travel.Y, round.Origin.Z + travel.Z);
        switch (contact.Payload)
        {
            case SoldierContact soldier:
                soldiers.StrikeWithRound(
                    soldier.View, soldier.Hit, round.Origin, end, round.Weapon, round.DamageFactor, round.Shooter as SoldierView);
                break;
            case PlayerContact player:
                // The game's own round on the game's own player: its hit as the game's own missions deliver it, unscaled.
                player.Target.Task.WeaponHit(new Igi1WeaponHit(
                    player.Hit, round.Origin, end, round.DamageFactor, round.Weapon.Category, round.Shooter as SoldierView, FromPlayer: false).ToShared());
                break;
            case LevelContact { Collider.Handler: IWeaponHitTarget target } level:
                Igi1HostStrikes.Deliver(
                    target,
                    level.Position,
                    level.Normal,
                    round.DamageFactor * DamageScale,
                    travel,
                    (round.Shooter as SoldierView)?.Soldier,
                    round.Weapon.Category);
                break;
        }
    }

    /// <inheritdoc/>
    public void PlaySound(string definition, Vector3d position) => Sound?.Invoke(definition, position);

    /// <inheritdoc/>
    public void Effect(GunShotRound round, in GunShotRecord record) => Impact?.Invoke(round, record);

    /// <inheritdoc/>
    public AmmoType? AmmunitionOf(WeaponType weapon) =>
        arsenal.Ammunition.TryGetByCalibre(weapon.CalibreId, out AmmoType found) ? found : null;

    private GameMaterial? Material(int? levelRow) =>
        levelRow is { } row && materialOf(row) is { } own && arsenal.Materials.TryGet(own, out GameMaterial material) ? material : null;

    // Where a round's line first meets the player: its posed body, as the soldiers cast the game's own player's; where no body
    // is loaded for it -- nothing draws it -- the round body its stand-in is (HumanHitTest), facing the way it faces.
    private HumanHit? CastPlayer(Igi1HostedPlayerTarget target, Vector3d start, Vector3d limit)
    {
        if (soldiers.TryCastPlayerBody(start, limit, target.Feet, target.Yaw, out IReadOnlyList<HumanBodyCrossing> crossings))
        {
            return crossings.Count > 0
                ? crossings[0].Hit with { Material = crossings[0].Material, IsExit = !crossings[0].Entering }
                : null;
        }

        Vector3f facing = new(-SimMathF.Sin(target.Yaw), SimMathF.Cos(target.Yaw), 0f);
        return Igi1HumanLineCollider.TryMeet(start, limit, target.Feet, facing, out HumanHit hit, out bool leaving)
            ? hit with { IsExit = leaving }
            : null;
    }

    // What a round met of the game's own player.
    private sealed record PlayerContact(Igi1HostedPlayerTarget Target, HumanHit Hit);

    // What a round met of the level's own.
    private sealed record LevelContact(IWorldCollider? Collider, Vector3d Position, Vector3f Normal);

    // A soldier a round met, bone by bone.
    private sealed record SoldierContact(SoldierView View, HumanHit Hit);
}

/// <summary>The game's own player standing in another game's level, as a round of the game's meets it.</summary>
/// <param name="Task">The level's player task the player stands as, which takes the hit.</param>
/// <param name="Feet">Where its feet are, in world units.</param>
/// <param name="Yaw">Which way it faces.</param>
internal sealed record Igi1HostedPlayerTarget(HumanTask Task, Vector3d Feet, float Yaw);
