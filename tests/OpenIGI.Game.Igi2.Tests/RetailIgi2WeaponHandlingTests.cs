using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The weapon in hand as the bodies show it: the upper-body clips, the weapon's own clips, the first-person model.</summary>
public sealed class RetailIgi2WeaponHandlingTests
{
    private static readonly Lazy<Mission1Setup> Mission1 = new(static () =>
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(level);
        Assert.True(PlayerSpawn.TryFindGameplay(level.Root, out PlayerSpawnPoint spawn));
        Igi2HumanPlayerTask player = (Igi2HumanPlayerTask)PlayerSpawn.FindGameplayTask(level.Root)!;
        Igi2BoneHierarchy? arms = Igi2Gameplay.FirstPersonRig(name => level.ModelFor(name), player);
        Igi2Session session = Igi2Session.Start();
        return new Mission1Setup(
            Igi2AnimationLibrary.Load(packs, resources),
            level.ModelFor(spawn.Model)!.BoneHierarchy!,
            arms!,
            session.Require<Igi2HumanWeapons>());
    });

    /// <summary>The upper body plays on one of its pair, swaps to the other for the next clip, and steps itself; clearing leaves none.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_upper_body_swaps_controllers_and_steps_itself()
    {
        Mission1Setup m = Mission1.Value;
        Igi2AnimationClip stand = m.Library.Find(m.Rig, "stand")!;
        Igi2AnimationClip tense = m.Library.Find(m.Rig, "tense")!;
        Igi2HumanAnimation animation = new(m.Library, m.Rig, Igi2PlayerStateAnimations.Table, static () => null);

        animation.SetUpperBody(tense, 0, 0f);
        Igi2AnimController first = animation.UpperBody!;
        animation.SetUpperBody(stand, 0, 0.2f);
        Igi2AnimController second = animation.UpperBody!;

        Assert.NotSame(first, second);
        Assert.True((first.Flags & Igi2AnimControllerBits.SuppressTriggers) != 0);
        Assert.Equal(960, animation.UpperBodyBlendTicks);
        animation.AutoStep();
        Assert.Equal(Igi2AnimController.TicksPerFrame, second.Time);
        Assert.Equal(Igi2AnimController.TicksPerFrame, first.Time);

        animation.ClearUpperBody(0.4f);
        Assert.Null(animation.UpperBody);
        Assert.False(animation.UpperBodyEnded);
    }

    /// <summary>An upper-body clip that is the main clip too does not trigger its events a second time.</summary>
    [RequiresTitleDataFact("igi2")]
    public void An_upper_body_clip_shared_with_the_main_one_is_quiet()
    {
        Mission1Setup m = Mission1.Value;
        Igi2HumanAnimation animation = new(m.Library, m.Rig, Igi2PlayerStateAnimations.Table, static () => null);
        animation.PlayState(0, 0, 0f);

        animation.PlayUpperBodyState(0, 0, 0f);

        Assert.True((animation.UpperBody!.Flags & Igi2AnimControllerBits.SuppressTriggers) != 0);
        Assert.Equal(0, animation.LastUpperBodyState);
    }

    /// <summary>
    /// The AK-47's first-person draw is its named clip, played to last its draw time; the put-away it does not name finds
    /// nothing; the body's put-away is the weapon table's <c>weaponraise</c> row.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_ak47_gives_its_draw_clips()
    {
        Mission1Setup m = Mission1.Value;
        Igi2WeaponTypeTask ak47 = m.Weapons.Tables.WeaponTypes.Single(static w => w.WeaponId == "WEAPON_ID_AK47");
        Igi2AnimationClip?[] body = Igi2WeaponTypeAnims.BuildTable(m.Library, m.Rig, ak47.WeaponAnimationName, (Igi2WeaponAnimFamily)ak47.WeaponType);

        Igi2WeaponAnim draw = Igi2WeaponTypeAnim.Get(ak47, Igi2WeaponAnimRequest.FirstActivate, m.Arms, m.Library, null, 0)!.Value;
        Assert.Equal(ak47.FirstPersonActivateAnim, draw.Clip.Name);
        Assert.Equal((new Fpu(draw.Clip.TotalTime) * 0.00020833334f / ak47.DeActivateTime).F, draw.TimeScale);
        Assert.Null(Igi2WeaponTypeAnim.Get(ak47, Igi2WeaponAnimRequest.FirstDeactivate, m.Arms, m.Library, null, 0));

        Igi2WeaponAnim putAway = Igi2WeaponTypeAnim.Get(ak47, Igi2WeaponAnimRequest.ThirdDeactivate, m.Rig, m.Library, body, 0)!.Value;
        Assert.Same(body[234], putAway.Clip);
        Assert.StartsWith("weaponraise", putAway.Clip.Name, StringComparison.Ordinal);
    }

    /// <summary>
    /// The first-person draw played backwards starts its clip's length before the end and steps back; a request the
    /// weapon has no clip for ends the model's clip and raises the action signal at once.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_first_person_model_plays_the_weapon_in_hand()
    {
        Mission1Setup m = Mission1.Value;
        Igi2PlayerWeapons weapons = new(m.Weapons.CreateInventory([]), m.Weapons.FireTaskTypes, m.Library);
        weapons.Inventory.EnableWeaponType("WEAPON_ID_AK47", addAmmo: true);
        (Igi2HumanPlayer player, _, _) = Igi2PlayerFakes.Player(weapon: weapons);
        player.View.ObjectRig = m.Arms;

        player.SetWeaponTypeAnim(Igi2WeaponAnimRequest.FirstActivate, 0, reverse: true);
        Igi2AnimController arms = player.View.ObjectAnimation;
        Igi2WeaponAnim draw = weapons.GetAnim(Igi2WeaponAnimRequest.FirstActivate, m.Arms, 0)!.Value;
        Assert.Equal(draw.Clip.TotalTime, arms.Time);
        Assert.Equal(Fpu.ToInt32(new Fpu(Igi2AnimController.TicksPerFrame) * -draw.TimeScale), arms.TimeStep);
        Assert.Equal(Igi2PlayerSignal.None, player.ActionSignal & Igi2PlayerSignal.AnimationEnded);

        player.UpdateViewBody(updateAnimation: false);
        Assert.Equal(draw.Clip.TotalTime, arms.Time);
        player.UpdateViewBody();
        Assert.Equal(draw.Clip.TotalTime + arms.TimeStep, arms.Time);

        player.SetWeaponTypeAnim(Igi2WeaponAnimRequest.FirstDeactivate, 0, reverse: false);
        Assert.True(arms.IsEnded);
        Assert.Equal(Igi2PlayerSignal.AnimationEnded, player.ActionSignal & Igi2PlayerSignal.AnimationEnded);
    }

    private sealed record Mission1Setup(Igi2AnimationLibrary Library, Igi2BoneHierarchy Rig, Igi2BoneHierarchy Arms, Igi2HumanWeapons Weapons);
}
