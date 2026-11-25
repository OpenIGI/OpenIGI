using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <summary>Makes another player in a level the way the level's own player is made, sharing what is the level's.</summary>
/// <remarks>
/// The steps are <see cref="Igi2TitlePlayer.Spawn"/>'s and <see cref="Igi2Gameplay.CreatePlayer"/>'s for the body
/// (<c>HumanPlayer_CreateHandler</c>), with the weapons made as each soldier's are. The level's things it meets are the level
/// player's: the collision world, the pickups, the grenades, the gunshot world and its impacts, and the AI that hears it.
/// </remarks>
internal static class Igi2NetworkHumanFactory
{
    public static Igi2NetworkHuman Make(
        Igi2TitlePlayer level,
        Igi2PlayerSpawnKit kit,
        Igi2LevelSurroundings shared,
        string model,
        Vector3d position,
        float gamma,
        int team)
    {
        Igi2LevelSurroundings surroundings = shared.ForRemotePlayer();
        (Igi2HumanPlayer human, Igi2PlayerWeapons? weapons) = Body(kit, surroundings, model, position, gamma, team);
        kit.World.AddBody(human.Collider!);
        human.Pickups = level.Human.Pickups;

        // The documents, key cards and disks are the level's, for every player to collect: without them the server never let a
        // client's player take one (the client hid it, the objective waiting on it never moved). And the level's clock.
        human.GenericPickups = level.Human.GenericPickups;
        human.LevelTick = level.Human.LevelTick;
        human.AnimTriggers = level.Human.AnimTriggers;
        human.WhizzbyClock = level.Human.WhizzbyClock;
        human.DeadBodySounds = level.Human.DeadBodySounds;
        human.WeaponCycle = kit.Tuning?.WeaponCycle ?? [];
        if (weapons is not null)
        {
            // Its casings fly from the gun in its body's hands, as a soldier's do.
            Igi2PlayerWeapons arms = weapons;
            weapons.Effects = new Igi2LevelWeaponEffects(kit.Sounds, () => human.View.Position, level.Ai, casing =>
            {
                if (level.Casings is { } casings && human.Model is { } dress && arms.Inventory.Current?.WeaponType.FixedModel is { } gun)
                {
                    Igi2TitlePlayer.EjectFrom(
                        casings, casing, ViewModel.Igi2BodyFrame.Build(human, human.PresentedPose(), dress.Name, gun, 0), gun, human.TotalSpeed);
                }
            });
            if (level.Weapons is { } own)
            {
                weapons.ImpactEffects = own.ImpactEffects;
                weapons.World = own.World;
                weapons.Grenades = own.Grenades;
            }
        }

        // A client on Project I.G.I.'s movement is moved here as there: on a body of its own, over the level's same world.
        if (level.Human.Igi1Feet is { } feet)
        {
            human.Igi1Feet = new Igi1FootMotion(feet.World, human);
        }

        human.UpdateViewBody(updateAnimation: false);
        return new Igi2NetworkHuman(human, weapons, kit.World);
    }

    /// <summary>
    /// Makes a race's ghost: a body made as another player's is, posed as a client's copy of one is
    /// (<see cref="Igi2NetworkHuman.PuppetTarget"/>), but part of nothing in the level -- not in its collision, heard by nobody (its
    /// sounds its own and silent), seen by no AI, picking up nothing. What it reads of the level, the ladders and timed actions it
    /// poses against, it only reads.
    /// </summary>
    /// <param name="level">The level's own player, whose clock and triggers it keeps time by.</param>
    /// <param name="kit">What a body is made of.</param>
    /// <param name="shared">The level's own player's surroundings, of which it shares only what it reads.</param>
    /// <param name="model">The model its body wears.</param>
    /// <param name="position">Where it first stands.</param>
    /// <param name="gamma">Which way it first faces.</param>
    /// <returns>The ghost.</returns>
    public static Igi2NetworkHuman MakeGhost(
        Igi2TitlePlayer level, Igi2PlayerSpawnKit kit, Igi2LevelSurroundings shared, string model, Vector3d position, float gamma)
    {
        Igi2Sounds silent = new(kit.Sounds.Definitions, audio: null);
        Igi2LevelSurroundings surroundings = shared.ForGhost(silent);
        (Igi2HumanPlayer human, Igi2PlayerWeapons? weapons) = Body(kit, surroundings, model, position, gamma, level.Human.Team);
        human.LevelTick = level.Human.LevelTick;
        human.AnimTriggers = level.Human.AnimTriggers;
        human.WeaponCycle = kit.Tuning?.WeaponCycle ?? [];
        if (weapons is not null)
        {
            // Its shots flash on its gun, heard by nobody.
            weapons.Effects = new Igi2LevelWeaponEffects(silent, () => human.View.Position);
        }

        human.UpdateViewBody(updateAnimation: false);
        return new Igi2NetworkHuman(human, weapons, kit.World);
    }

    // Another player's body, as the level's own is made: dressed, armed as a soldier is, created, and its collider made -- whether
    // the level's collision is to hold it is the caller's to say.
    private static (Igi2HumanPlayer Human, Igi2PlayerWeapons? Weapons) Body(
        Igi2PlayerSpawnKit kit, Igi2LevelSurroundings surroundings, string model, Vector3d position, float gamma, int team)
    {
        Formats.World.MefModel? body = kit.Looks(model);
        Igi2BoneHierarchy? rig = body?.BoneHierarchy;

        Igi2PlayerWeapons? weapons = null;
        IIgi2PlayerWeaponControl weapon = new Igi2NoWeaponControl();
        if (kit.Services.TryGet(out Igi2HumanWeapons? armoury))
        {
            Igi2Inventory inventory = armoury.CreateInventory(kit.Tuning?.AmmoLimits ?? []);
            weapons = new Igi2PlayerWeapons(inventory, armoury.FireTaskTypes, kit.Clips);
            weapon = weapons;
        }

        IIgi2PlayerWeaponControl held = weapon;
        Igi2HumanAnimation animation = new(kit.Clips, rig, Igi2PlayerStateAnimations.Table, () => held.BodyClips(rig));
        Igi2HumanPlayer human = new(weapon, animation, surroundings)
        {
            General = kit.General,
            Position = position,
            Gamma = gamma,
            IdealGamma = gamma,
            Team = team,

            // Nobody looks through its eyes: its weapon is the one on its body (Human_GetWeaponObjectHandler's body branch),
            // which is where its shots flash. Left in first person it had no view clip, so no weapon object, and no flash.
            IsThirdPerson = true,
        };
        surroundings.Owner = human;
        animation.TriggerTarget = human.BodyTriggers;

        float[] facing = EngineEuler.Rotation(0f, 0f, gamma);
        human.Orientation = new Matrix3f(
            facing[0], facing[1], facing[2],
            facing[3], facing[4], facing[5],
            facing[6], facing[7], facing[8]);
        human.Create(kit.Difficulty);
        human.MaintainCollisionZOffset();

        human.Model = Igi2BodyModel.From(model, body);
        human.Collider = new Igi2BoneCollider(human, human);
        if (body is not null)
        {
            human.ModelRadius = body.NativeMarkerBound;
            if (body.BoneHierarchy is { BoneCount: > 0 } bones)
            {
                human.RootTranslation = bones.Translations[0];
            }
        }

        return (human, weapons);
    }
}
