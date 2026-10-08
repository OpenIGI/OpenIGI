using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.ViewModel;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>What the player's view holds: the skinned arms posed by the view's clip, and the held weapon on the right hand.</summary>
public sealed class RetailIgi2FirstPersonTests
{
    private static readonly Lazy<Mission1Setup> Mission1 = new(static () =>
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(level);
        Igi2HumanPlayerTask task = (Igi2HumanPlayerTask)PlayerSpawn.FindGameplayTask(level.Root)!;
        return new Mission1Setup(level, task, Igi2AnimationLibrary.Load(packs, resources), Igi2Session.Start());
    });

    /// <summary>
    /// Mission 1 names <c>arms1_1</c> as the first-person model; with the MP5SD in hand the view draws the skinned arms
    /// hanging in front of and below the eyes and the MP5SD's fixed model, <c>102_01_1</c>, on the right hand.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_view_draws_the_arms_and_the_held_weapon()
    {
        (Igi2HumanPlayer player, Igi2PlayerWeapons weapons) = ArmedPlayer();
        Settle(player);

        TitleFirstPersonView view = Frame(player, weapons, 1);

        Assert.Equal(["arms1_1", "102_01_1"], view.Parts.Select(static p => p.Model));
        Assert.NotNull(view.Parts[0].Pose);
        Assert.Null(view.Parts[1].Pose);
        Assert.Equal(player.View.ObjectPosition, view.Origin);
        Assert.Equal(player.View.ObjectFieldOfView, view.ObjectFieldOfView);

        Vector3f arms = SkinnedMean(Mission1.Value.Level.ModelFor("arms1_1")!, view.Parts[0]);
        Assert.InRange(arms.Y, 500f, 2500f);
        Assert.InRange(arms.Z, -1500f, 0f);

        Vector3f gun = view.Parts[1].Position;
        Assert.InRange(gun.X, 0f, 1500f);
        Assert.InRange(gun.Y, 300f, 2500f);
        Assert.InRange(gun.Z, -1500f, 0f);
    }

    /// <summary>
    /// The MP5SD's walk clip hangs the gun on the right hand so that it is held level with the view: the record's
    /// matrix, read transposed as <c>ModelObj_CreateAttachObject</c> (<c>igi2 0x56D0B0</c>) reads it, undoes the
    /// hand's own turn. Read untransposed it tilted the gun nine degrees up and over.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_mp5sd_is_held_level_with_the_view()
    {
        (Igi2HumanPlayer player, Igi2PlayerWeapons weapons) = ArmedPlayer();
        Settle(player);

        Matrix3f gun = Frame(player, weapons, 1).Parts[1].Orientation;

        Assert.Equal("walk_mp5_1st", player.View.ObjectAnimation.Clip!.Name);
        AssertNear(new Vector3f(1f, 0f, 0f), gun.Column1);
        AssertNear(new Vector3f(0f, 1f, 0f), gun.Column2);
        AssertNear(new Vector3f(0f, 0f, 1f), gun.Column3);
    }

    /// <summary>After a change to the Glock, the hand holds the Glock's fixed model, <c>139_01_1</c>.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_hand_follows_the_weapon_changed_to()
    {
        (Igi2HumanPlayer player, Igi2PlayerWeapons weapons) = ArmedPlayer();
        Settle(player);
        Igi2PlayerFakes.Feed(player, Igi2PlayerAction.NextWeapon);
        player.Update();
        for (int tick = 0; tick < 600 && player.ActionState != Igi2PlayerActionState.Idle; tick++)
        {
            Igi2PlayerFakes.Feed(player, Igi2PlayerAction.None);
            player.Update();
        }

        Assert.Equal("139_01_1", Frame(player, weapons, 2).Parts[^1].Model);
    }

    /// <summary>A zoomed view draws nothing, as <c>HumanView_EnterHandler</c> declines then.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_zoomed_view_draws_nothing()
    {
        (Igi2HumanPlayer player, Igi2PlayerWeapons weapons) = ArmedPlayer();
        Settle(player);
        player.View.IsZoomMode = true;

        Assert.Null(new Igi2FirstPersonFrame().Build(player, weapons.Inventory.Current?.WeaponType.FixedModel, 3));
    }

    private static TitleFirstPersonView Frame(Igi2HumanPlayer player, Igi2PlayerWeapons weapons, long tick) =>
        new Igi2FirstPersonFrame().Build(player, weapons.Inventory.Current?.WeaponType.FixedModel, tick)!;

    private static (Igi2HumanPlayer Player, Igi2PlayerWeapons Weapons) ArmedPlayer()
    {
        Mission1Setup m = Mission1.Value;
        Igi2PlayerTuning tuning = m.Session.Require<Igi2PlayerTuning>();
        Igi2PlayerWeapons weapons = Igi2Gameplay.Arm(m.Session.Session.Services, m.Level.Root, tuning, m.Library)!;
        (Igi2HumanPlayer player, _, _) = Igi2PlayerFakes.OnFloor(weapons);
        string model = Igi2Gameplay.FirstPersonModel(m.Level.ModelFor, m.Task)!;
        player.View.ObjectModel = model;
        player.View.ObjectRig = m.Level.ModelFor(model)!.BoneHierarchy;
        player.WeaponCycle = tuning.WeaponCycle;
        return (player, weapons);
    }

    private static void Settle(Igi2HumanPlayer player)
    {
        for (int i = 0; i < 3; i++)
        {
            Igi2PlayerFakes.Feed(player, Igi2PlayerAction.None);
            player.Update();
        }
    }

    private static Vector3f SkinnedMean(MefModel model, TitleViewModelPart part)
    {
        Vertex3D[] skinned = [.. MefMeshBuilder.Build(model, 1f)!.Vertices];
        int count = MefSkinner.Skin(model, part.Pose!, 1f, skinned);
        double x = 0, y = 0, z = 0;
        for (int i = 0; i < count; i++)
        {
            x += skinned[i].Position.X;
            y += skinned[i].Position.Y;
            z += skinned[i].Position.Z;
        }

        return new Vector3f((float)(x / count), (float)(y / count), (float)(z / count));
    }

    private static void AssertNear(Vector3f expected, Vector3f actual, float tolerance = 1e-4f)
    {
        Assert.InRange(actual.X, expected.X - tolerance, expected.X + tolerance);
        Assert.InRange(actual.Y, expected.Y - tolerance, expected.Y + tolerance);
        Assert.InRange(actual.Z, expected.Z - tolerance, expected.Z + tolerance);
    }

    private sealed record Mission1Setup(Igi2Level Level, Igi2HumanPlayerTask Task, Igi2AnimationLibrary Library, Igi2Session Session);
}
