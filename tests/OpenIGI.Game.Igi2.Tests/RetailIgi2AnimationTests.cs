using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Mission 1's clips, the weapon tables built from them, and the controllers that play them.</summary>
public sealed class RetailIgi2AnimationTests
{
    private static readonly Lazy<(Igi2AnimationLibrary Library, Igi2BoneHierarchy Rig)> Mission1 = new(static () =>
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(level);
        Assert.True(PlayerSpawn.TryFindGameplay(level.Root, out PlayerSpawnPoint spawn));
        return (Igi2AnimationLibrary.Load(packs, resources), level.ModelFor(spawn.Model)!.BoneHierarchy!);
    });

    /// <summary>The packs' <c>BANM</c> lists load every shipped clip, and the player's rig has its <c>stand</c>.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_packs_load_the_clips()
    {
        (Igi2AnimationLibrary library, Igi2BoneHierarchy rig) = Mission1.Value;

        Assert.True(library.ClipCount > 1200, $"only {library.ClipCount} clips loaded.");
        Igi2AnimationClip stand = library.Find(rig, "stand")!;
        Assert.Equal(13920, stand.TotalTime);
        Assert.True(stand.Loops);
        Assert.Null(library.Find(rig, "Stand"));
    }

    /// <summary>A weapon's table tries its own name, then its family, then the base name, then <c>stand_lowerbody</c>.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_weapon_table_falls_back_in_order()
    {
        (Igi2AnimationLibrary library, Igi2BoneHierarchy rig) = Mission1.Value;

        Igi2AnimationClip?[] table = Igi2WeaponTypeAnims.BuildTable(library, rig, "AK47", Igi2WeaponAnimFamily.Rifle);

        Assert.Equal("stand_ak47", table[0]?.Name);
        Assert.Equal("civilian_walk_rifle", table[3]?.Name);
        Assert.Equal("climb_ladder", table[136]?.Name);
        Assert.Equal(Igi2WeaponTypeAnims.LastResort, table[57]?.Name);
    }

    /// <summary>A clip set past its end wraps one tick beyond it and is not ended; a one-shot stops at its end.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_one_shot_clip_stops_at_its_end()
    {
        (Igi2AnimationLibrary library, Igi2BoneHierarchy rig) = Mission1.Value;
        Igi2AnimationClip tense = library.Find(rig, "tense")!;
        Igi2AnimController controller = new();

        controller.SetAnimationAndTime(tense, 5000);
        Assert.Equal(5000 % 3361, controller.Time);
        Assert.False(controller.IsEnded);
        Assert.Equal(Igi2AnimController.TicksPerFrame, controller.TimeStep);

        controller.Step(3360);
        Assert.True(controller.IsEnded);
        controller.Step(4000);
        Assert.Equal(3360, controller.Time);
        controller.Step(-5);
        Assert.Equal(0, controller.Time);
    }

    /// <summary>A looping clip wraps, and once it has, the ended bit stays until a clip is set again.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_looping_clip_wraps_and_stays_ended()
    {
        (Igi2AnimationLibrary library, Igi2BoneHierarchy rig) = Mission1.Value;
        Igi2AnimationClip stand = library.Find(rig, "stand")!;
        Igi2AnimController controller = new();

        controller.SetAnimationAndTime(stand, 13920);
        Assert.Equal(0, controller.Time);
        Assert.False(controller.IsEnded);

        controller.Step(14000);
        Assert.Equal(80, controller.Time);
        Assert.True(controller.IsEnded);
        controller.Step(160);
        Assert.True(controller.IsEnded);
    }

    /// <summary>A body holding nothing plays its rig's <c>stand</c> whatever the state, handing over between controllers.</summary>
    [RequiresTitleDataFact("igi2")]
    public void An_unarmed_body_plays_stand()
    {
        (Igi2AnimationLibrary library, Igi2BoneHierarchy rig) = Mission1.Value;
        Igi2HumanAnimation animation = new(library, rig, Igi2PlayerStateAnimations.Table, static () => null);

        animation.PlayState(Igi2PlayerState.RunForward, 0, 0.2f);
        Igi2AnimController first = animation.Main;
        animation.PlayState(Igi2PlayerState.Jump, 320, 0.2f);

        Assert.NotSame(first, animation.Main);
        Assert.Equal("stand", animation.Main.Clip?.Name);
        Assert.Equal(320, animation.MainTime);
        Assert.Equal(960, animation.BlendTicks);
        Assert.Equal(Igi2PlayerState.Jump, animation.LastState);
        animation.StepMain();
        Assert.Equal(480, animation.MainTime);
    }

    /// <summary>A held weapon picks the state's clip from its table; the lowering row plays the raise backwards from its end.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_held_weapon_picks_the_clip_and_lowering_plays_backwards()
    {
        (Igi2AnimationLibrary library, Igi2BoneHierarchy rig) = Mission1.Value;
        Igi2AnimationClip?[] table = Igi2WeaponTypeAnims.BuildTable(library, rig, "ak47", Igi2WeaponAnimFamily.Rifle);
        Igi2HumanAnimation animation = new(library, rig, Igi2PlayerStateAnimations.Table, () => table);

        animation.PlayState(Igi2PlayerState.RunForward, 0, 0f);
        Assert.Same(table[5], animation.Main.Clip);

        const int LowerWeaponState = 87;
        animation.PlayState(LowerWeaponState, 100, 0f);
        Igi2AnimationClip raise = table[Igi2WeaponTypeAnims.LowerWeapon]!;
        Assert.Same(raise, animation.Main.Clip);
        Assert.Equal((raise.TotalTime - 100) % (raise.TotalTime + 1), animation.MainTime);
        Assert.Equal(-Igi2AnimController.TicksPerFrame, animation.MainTimeStep);
    }
}
