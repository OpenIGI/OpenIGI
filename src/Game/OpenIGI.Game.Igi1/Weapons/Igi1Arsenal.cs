using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Weapons;

/// <summary>Project I.G.I.'s weapon, ammunition and game-material tables, read together as a mission reads them.</summary>
/// <param name="Weapons">The weapon table, with its configuration attached when the title has one.</param>
/// <param name="Ammunition">The ammunition table.</param>
/// <param name="Materials">What every surface is made of.</param>
/// <param name="MaterialCount">How many materials the table read.</param>
/// <remarks>
/// The game's own mission reads them (<see cref="World.Igi1PlayerSetup"/>), and so does a guest session of the game playing
/// in another's level. WEAPON_ID_* and AMMO_ID_* are pattern-registered symbols that get their values as the tables are
/// read, so they are read before any script that names them runs.
/// </remarks>
public sealed record Igi1Arsenal(WeaponTypeRegistry Weapons, AmmoTypeRegistry Ammunition, GameMaterialRegistry Materials, int MaterialCount)
{
    /// <summary>Reads the tables, in the original's order.</summary>
    /// <param name="loop">The session's loop, whose natives the ids are registered on and whose tree holds the configuration.</param>
    /// <param name="fileSystem">Where the tables are read from.</param>
    /// <param name="resources">The session's resources.</param>
    /// <param name="logger">Where problems are reported.</param>
    /// <returns>The tables.</returns>
    /// <exception cref="IOException">A table could not be read.</exception>
    /// <exception cref="InvalidDataException">A table is malformed.</exception>
    public static Igi1Arsenal Load(GameLoop loop, VirtualFileSystem fileSystem, ResourceRegistry resources, IOpenIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(logger);

        WeaponTypeRegistry weapons = new(logger);
        AmmoTypeRegistry ammunition = new(logger);
        GameMaterialRegistry materials = new(logger);
        weapons.Register(loop.Natives);
        weapons.Load(fileSystem, resources, loop.Natives);
        WeaponConfigRegistry.RegisterTypes(loop.Types);
        WeaponConfigRegistry weaponConfig = new(logger);
        if (weaponConfig.Load(resources, loop.Natives, loop.Tasks) == WeaponConfigRegistry.NoScript)
        {
            logger.Warning("Weapon", $"No weapon configuration at {WeaponConfigRegistry.ScriptResource}; this title keeps it elsewhere.");
        }
        else
        {
            weaponConfig.AttachTo(weapons);
        }

        ammunition.Register(loop.Natives);
        ammunition.Load(fileSystem, resources, loop.Natives);

        materials.Register(loop.Natives);
        if (fileSystem.Exists(GameMaterialRegistry.ScriptResource))
        {
            resources.Register(GameMaterialRegistry.ScriptResource, fileSystem.ReadAllBytes(GameMaterialRegistry.ScriptResource));
        }
        else
        {
            logger.Warning("Material", $"No material table at {GameMaterialRegistry.ScriptResource}.");
        }

        int count = materials.Load(resources, loop.Natives);
        return new Igi1Arsenal(weapons, ammunition, materials, count);
    }
}
