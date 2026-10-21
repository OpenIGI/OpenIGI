using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Hosting;

/// <summary>What making a title's player asks of whatever shows and plays it.</summary>
public interface IPlayerSetupHost
{
    /// <summary>Gets the log.</summary>
    IOpenIgiLogger Logger { get; }

    /// <summary>Gets the game's loop, whose namespace, types and tree the player's tables and scripts load into.</summary>
    GameLoop Game { get; }

    /// <summary>Gets the files the tables are read from.</summary>
    VirtualFileSystem FileSystem { get; }

    /// <summary>Gets the resources the scripts are registered as.</summary>
    ResourceRegistry Resources { get; }

    /// <summary>Gets the mission's terrain, which the grenades and the view model trace.</summary>
    TerrainData? Terrain { get; }

    /// <summary>Gets the level's built scene, whose ladders the player climbs and whose vehicles' guns turn.</summary>
    WorldScene? Scene { get; }

    /// <summary>Reads, once a session, the animation sets the player's body and its first-person rig use.</summary>
    /// <returns>The body's set, or <see langword="null"/> when there is none.</returns>
    BoneAnimationSet? ReadSharedAnimations();

    /// <summary>Records that a stage of the load has finished, for the load's timings.</summary>
    /// <param name="what">What finished.</param>
    void EndStage(string what);

    /// <summary>Shows a gun a soldier dropped.</summary>
    /// <param name="loaded">The mission, whose packs hold the gun's model.</param>
    /// <param name="pickup">The dropped gun.</param>
    void WeaponDropped(LoadedMission loaded, GunPickup pickup);

    /// <summary>Hands the effects the game material the open ground is made of.</summary>
    /// <param name="row">The material's row, or <see langword="null"/> when the terrain declares none.</param>
    void TerrainRowRead(int? row);

}
