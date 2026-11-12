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

        _trace.Starts[0] = start;
        _trace.Ends[0] = limit;
        _trace.Includes = owner => !ReferenceEquals(owner, shooterTask) && !isSoldier(owner);
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
            case LevelContact { Collider.Handler: IWeaponHitTarget target } level:
                float length = MathF.Sqrt((travel.X * travel.X) + (travel.Y * travel.Y) + (travel.Z * travel.Z));
                Vector3f along = length > 0f ? new Vector3f(travel.X / length, travel.Y / length, travel.Z / length) : default;
                target.WeaponHit(new WeaponHit(
                    level.Position,
                    level.Normal,
                    round.DamageFactor * DamageScale,
                    along,
                    (round.Shooter as SoldierView)?.Soldier,
                    Context: null,
                    round.Weapon.Category));
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

    // What a round met of the level's own.
    private sealed record LevelContact(IWorldCollider? Collider, Vector3d Position, Vector3f Normal);

    // A soldier a round met, bone by bone.
    private sealed record SoldierContact(SoldierView View, HumanHit Hit);
}
