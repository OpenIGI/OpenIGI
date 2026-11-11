using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Ai;

public sealed partial class SoldierSimulation
{
    /// <summary>Binds every stationary gun its weapon, and hands every soldier the weapon it carries.</summary>
    /// <param name="mission">The packs the guns' models are read from, for where a mounted gun's muzzle is.</param>
    /// <param name="roster">The weapon table.</param>
    /// <returns>How many soldiers carry a weapon the table holds.</returns>
    /// <remarks>
    /// <para>
    /// The Human's held Weapon task exists independently of the third-person mesh. Combat reads that task directly
    /// (<c>0x48D8E0</c>), so a soldier is armed whether or not anything draws the gun in its hand.
    /// </para>
    /// <para>
    /// The human inventory scan (<c>0x44FBA0</c>) examines every carried Weapon task, not only slot 0: a grenade among them
    /// publishes <c>HumanAI+15869</c>, which is the patrol grenade chooser's gate. Slot 0, the first child that named a
    /// weapon, is the one held.
    /// </para>
    /// </remarks>
    public int Arm(LoadedMission mission, WeaponTypeRegistry roster)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(roster);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Interactions is { } interactions)
        {
            foreach (AiStationaryGun gun in interactions.Items.Select(static item => item.StationaryGun).OfType<AiStationaryGun>())
            {
                if (!roster.TryGetByScriptId(gun.WeaponId, out WeaponType weapon))
                {
                    gun.BindWeapon(null);
                    _logger.Warning(SoldierContext.LogCategory + ".Gunner", $"task={gun.TaskId} weapon={gun.WeaponId} is not in the weapon table.");
                    continue;
                }

                gun.BindWeapon(weapon);

                // 0x478BA0 fires a mounted gun from its own model's magic vertex 2 -- the origin when the model has no such
                // vertex -- because the stand passes it no eye to start from.
                if (mission.TryGetModel(weapon.GunModel, out MefModel? gunModel)
                    && gunModel is not null
                    && gunModel.TryGetMagicVertex(2, out Vector3f muzzle))
                {
                    gun.MuzzleVertex = muzzle;
                }
            }
        }

        int armed = 0;
        foreach (SoldierModelGroup group in Roster)
        {
            foreach (SoldierAgent agent in group.Agents)
            {
                IReadOnlyList<LoadoutWeapon> carried = PlayerLoadout.WeaponsOf(agent.View.Soldier);
                bool hasGrenade = carried.Any(item =>
                    roster.TryGetByScriptId(item.WeaponId, out WeaponType candidate) && candidate.Category == WeaponCategory.Grenade);
                if (Brains.TryGetValue(agent.View, out Igi1HumanAi? brain))
                {
                    brain.SetGrenadeAvailable(hasGrenade);
                }

                if (carried.Count == 0)
                {
                    continue;
                }

                string weaponId = carried[0].WeaponId;
                if (!roster.TryGetByScriptId(weaponId, out WeaponType weapon))
                {
                    _logger.Warning(SoldierContext.LogCategory, $"'{weaponId}' is not in the weapon table.");
                    continue;
                }

                agent.WeaponType = weapon;
                agent.CarriedWeaponType = weapon;
                agent.AmmoInClip = weapon.RoundsPerClip;
                armed++;
            }
        }

        return armed;
    }
}
