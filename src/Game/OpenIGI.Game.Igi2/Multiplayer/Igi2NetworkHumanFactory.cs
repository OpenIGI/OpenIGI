using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Animation;
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
        Formats.World.MefModel? body = kit.Models(model);
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
        kit.World.AddBody(human.Collider);
        human.Pickups = level.Human.Pickups;
        human.AnimTriggers = level.Human.AnimTriggers;
        human.WhizzbyClock = level.Human.WhizzbyClock;
        human.DeadBodySounds = level.Human.DeadBodySounds;
        human.WeaponCycle = kit.Tuning?.WeaponCycle ?? [];
        if (body is not null)
        {
            human.ModelRadius = body.NativeMarkerBound;
            if (body.BoneHierarchy is { BoneCount: > 0 } bones)
            {
                human.RootTranslation = bones.Translations[0];
            }
        }

        if (weapons is not null)
        {
            weapons.Effects = new Igi2LevelWeaponEffects(kit.Sounds, () => human.View.Position, level.Ai);
            if (level.Weapons is { } own)
            {
                weapons.ImpactEffects = own.ImpactEffects;
                weapons.World = own.World;
                weapons.Grenades = own.Grenades;
            }
        }

        human.UpdateViewBody(updateAnimation: false);
        return new Igi2NetworkHuman(human, weapons, kit.World);
    }
}
