using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>What Project I.G.I.'s player moves, throws and climbs through: whichever level it stands in, the game's or another's.</summary>
/// <param name="Solids">The placed objects it walks into and stands on.</param>
/// <param name="GrenadeWalls">The cast a thrown grenade meets, which takes in the ground, as the body's ground query does not.</param>
/// <param name="GrenadeTrace">The same as a world trace, which says what was met.</param>
/// <param name="TerrainTrace">A ray through the ground for the view model's forward sweep; <see langword="null"/> for objects alone.</param>
/// <param name="Ladders">The ladders it climbs.</param>
public sealed record Igi1PlayerWorld(
    ObjectCollisionIndex Solids,
    WorldRayCast GrenadeWalls,
    WorldTraceCast GrenadeTrace,
    WorldTraceCast? TerrainTrace,
    IReadOnlyList<LadderPlacement> Ladders)
{
    /// <summary>The world of one of the game's own levels: its placed objects, and its terrain where it has one.</summary>
    /// <param name="solids">The placed objects.</param>
    /// <param name="terrain">The terrain, or <see langword="null"/>.</param>
    /// <param name="ladders">The ladders.</param>
    /// <returns>The world.</returns>
    public static Igi1PlayerWorld Of(ObjectCollisionIndex solids, TerrainData? terrain, IReadOnlyList<LadderPlacement> ladders)
    {
        ArgumentNullException.ThrowIfNull(solids);
        // The grenade's own cast INCLUDES the terrain: the body meets the ground through its ground query, but a grenade has
        // no such query and would fall through the world.
        WorldTraceCast trace = terrain is { } ground ? WorldTrace.Nearest(solids.Tracer(), TerrainRayTrace.Caster(ground)) : solids.Tracer();
        return new Igi1PlayerWorld(
            solids,
            terrain is not null ? WorldTrace.AsRayCast(trace) : solids.Caster(),
            trace,
            terrain is { } swept ? TerrainRayTrace.Caster(swept) : null,
            ladders ?? []);
    }
}

/// <summary>
/// Makes Project I.G.I.'s player's rig, whichever level it stands in: its body's animations and walls, what its grenades and view
/// model meet, its ladders, <c>humanplayer.qvm</c>'s tuning and what it carries.
/// </summary>
/// <remarks>
/// The game's own mission (<see cref="World.Igi1PlayerSetup"/>) and another game's level both bring the player up through it, in
/// the original's order; what the level then wires to it -- its pickups, its interactions, its harm -- is the level's.
/// </remarks>
public static class Igi1PlayerRig
{
    /// <summary>Makes the rig: its body's animations, its walls, and what its grenades, view model and climbing meet.</summary>
    /// <param name="world">What it moves through.</param>
    /// <param name="bodyAnimations">The body's animation set (skeleton 0, <c>000.iff</c>), or <see langword="null"/> for none.</param>
    /// <param name="logger">Where it reports.</param>
    /// <returns>The rig, not yet stood in the level.</returns>
    public static PlayerCamera Build(Igi1PlayerWorld world, BoneAnimationSet? bodyAnimations, IOpenIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(logger);
        PlayerCamera rig = new(logger);

        // Walking is the animation's own root motion, so without the body's set the player can stand and fall but not walk.
        if (bodyAnimations is not null)
        {
            rig.UseAnimations(bodyAnimations);
        }

        // The walls. Terrain alone gives the player ground to stand on but nothing to stop it walking through crates and
        // buildings; the solidity is in each model's own HSMC mesh.
        rig.UseWalls(world.Solids);
        logger.Information("Player", $"{world.Solids.Count} placed object(s) are solid, {world.Solids.TriangleCount} collision triangle(s).");
        rig.GrenadeWalls = world.GrenadeWalls;
        rig.GrenadeTrace = world.GrenadeTrace;

        // The view model's forward sweep, which wants a ray through the terrain rather than the height under a point; without
        // one the pullback meets placed objects only.
        rig.TerrainTrace = world.TerrainTrace;

        // The ladders the level attaches to its buildings, so the player can climb them.
        rig.UseLadders(world.Ladders);
        logger.Information("Player", $"{world.Ladders.Count} ladder(s) to climb.");
        return rig;
    }

    /// <summary>Runs <c>humanplayer.qvm</c> over the rig: its tuning, the weapon it starts with, its ammunition limits and categories.</summary>
    /// <param name="rig">The rig.</param>
    /// <param name="loop">The session's loop, whose natives the script resolves against.</param>
    /// <param name="resources">Where the script is read from.</param>
    /// <param name="logger">Where it reports.</param>
    /// <returns>The script's tuning, or <see langword="null"/> where it published none and the retail defaults stand.</returns>
    /// <exception cref="IOException">The script could not be read.</exception>
    /// <exception cref="InvalidDataException">The script is malformed.</exception>
    /// <remarks>
    /// The player's tuning is not in the executable: <c>humanplayer.qvm</c> is the whole of it. It also says which weapon starts in
    /// hand: <c>0x40F760</c>'s cycle index begins at -1, so the first update lands on entry 0 of
    /// <c>DefineHumanPlayerWeaponCycle</c> and steps on until it finds one carried. <c>0x410370</c> caps the new player's
    /// ammunition at <c>DefineHumanPlayerAmmoLimit</c>'s figures before anything is put in its pockets, and <c>0x411000</c> reads
    /// the nine categories the number keys select.
    /// </remarks>
    public static HumanPlayerParameters? Tune(PlayerCamera rig, GameLoop loop, ResourceRegistry resources, IOpenIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(logger);
        HumanPlayerConfig config = new();
        config.Register(loop.Natives);
        config.Load(resources, loop.Natives);
        if (config.PlaceholderIds.Count > 0)
        {
            logger.Warning(
                "Player",
                $"humanplayer.qvm left {config.PlaceholderIds.Count} id(s) unresolved; the weapon cycle may name weapons that do not exist.");
        }

        if (config.Parameters is { } tuning)
        {
            rig.UseParameters(tuning);
        }
        else
        {
            logger.Warning("Player", "humanplayer.qvm published no parameters; using the retail defaults.");
        }

        rig.UseWeaponCycle(config.WeaponCycle);
        foreach ((int ammoId, int limit) in config.AmmoLimits)
        {
            rig.Weapons?.Ammunition.SetLimit(ammoId, limit);
        }

        rig.Weapons?.UseCategories(config.Categories);
        return config.Parameters;
    }

    /// <summary>
    /// The player's rounds as the level's humans take them: offered to every source of bodies the level has, the nearest that
    /// agrees struck, as the player's own (<see cref="LevelBodies"/>).
    /// </summary>
    /// <param name="rig">The rig whose rounds they are, whose tally they are credited to.</param>
    /// <param name="sources">The level's sources of bodies, read as each round is cast.</param>
    /// <param name="shooter">The player's task, which each body weighs in its evaluate.</param>
    /// <returns>The shot.</returns>
    public static HumanShot Rounds(PlayerCamera rig, IReadOnlyList<IShootableBodies> sources, Func<Engine.Tasks.GameTask?> shooter)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(shooter);
        return (Core.Numerics.Vector3d origin, Core.Numerics.Vector3d end, float damageFactor, WeaponCategory category, double stopDistance, bool alreadyHeard, out HumanHit hit) =>
            LevelBodies.Shoot(
                sources,
                origin,
                end,
                shooter(),
                stopDistance,
                alreadyHeard,
                struck => new Ai.Igi1WeaponHit(struck, origin, end, damageFactor, category, Shooter: null, FromPlayer: true, rig.Shots).ToShared(),
                out hit);
    }

    /// <summary>Hands the player everything a loadout declares, with the ammunition each weapon draws from.</summary>
    /// <param name="rig">The rig.</param>
    /// <param name="loadout">What the player carries.</param>
    /// <param name="roster">The weapon table.</param>
    /// <param name="ammunition">The ammunition table.</param>
    /// <param name="logger">Where a weapon the table does not hold is reported.</param>
    /// <returns>Whether the player carries anything, and so needs a hand to hold it in.</returns>
    /// <remarks>
    /// Which weapon ends up in hand is not decided here: the weapon cycle's index starts at -1, and the first tick lands on entry
    /// 0 of <c>DefineHumanPlayerWeaponCycle</c> and steps on until it finds one that is carried.
    /// </remarks>
    public static bool Carry(PlayerCamera rig, PlayerLoadout loadout, WeaponTypeRegistry roster, AmmoTypeRegistry ammunition, IOpenIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(loadout);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(ammunition);
        ArgumentNullException.ThrowIfNull(logger);
        if (rig.View is null || loadout.Weapons.Count == 0)
        {
            return false;
        }

        foreach (LoadoutWeapon declared in loadout.Weapons)
        {
            if (!roster.TryGetByScriptId(declared.WeaponId, out WeaponType weapon))
            {
                logger.Warning("Weapon", $"{declared.WeaponId} is not in the roster; it is not carried.");
                continue;
            }

            // A weapon finds its ammunition by naming a Calibre ID, and -1 -- the knife's -- names none at all, which is exactly
            // why a knife never runs out.
            rig.Carry(weapon, ammunition.TryGetByCalibre(weapon.CalibreId, out AmmoType ammo) ? ammo : null);
        }

        foreach (LoadoutAmmo declared in loadout.Ammunition)
        {
            AmmoType? pool = ammunition.Types.FirstOrDefault(a => string.Equals(a.ScriptId, declared.AmmoId, StringComparison.Ordinal));
            if (pool is not null)
            {
                rig.Weapons?.Ammunition.Add(pool.AmmoId, declared.Count);
                if (string.Equals(declared.AmmoId, "AMMO_ID_M203", StringComparison.Ordinal))
                {
                    rig.Weapons?.SetM203Ammunition(pool);
                }
            }
        }

        return true;
    }
}
