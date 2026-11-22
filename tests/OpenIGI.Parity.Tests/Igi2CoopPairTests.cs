using OpenIGI.Core.Numerics;
using OpenIGI.Game.Flow;
using OpenIGI.Game.Igi2.Multiplayer;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Igi2.ViewModel;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Parity.Tests;

/// <summary>I.G.I. 2 missions played by two real games together: what one player does, the other's game shows.</summary>
[Collection(ParityGroup.Name)]
public sealed class Igi2CoopPairTests
{
    [RequiresTitleDataFact("igi2")]
    public void The_joiner_is_welcomed_its_level_let_go_and_each_sees_the_other_walk()
    {
        using Igi2CoopPair pair = Joined();
        Assert.False(pair.Client.HoldsLevel, "the joiner's level is still held behind its loading card");

        Igi2NetworkHuman partner = Assert.Single(pair.Host!.NetworkHumans);
        Vector3d from = partner.Human.Position;
        Igi2CoopPair.Hold(pair.JoinerInput, KeyCode.W, true);
        pair.RunUntil(() => false, 300);
        Igi2CoopPair.Hold(pair.JoinerInput, KeyCode.W, false);
        double walked = Vector3d.Distance(from, partner.Human.Position);
        Assert.True(walked > 2048, $"the joiner's walk moved its player on the host {walked} units");

        // The host's player stands in the joiner's level where it stands in the host's.
        Igi2NetworkHuman host = Assert.Single(pair.Joiner!.NetworkHumans);
        double apart = Vector3d.Distance(host.Human.Position, pair.Host.Human.Position);
        Assert.True(apart < 1024, $"the host's player is {apart} units from where the joiner shows it");
    }

    /// <summary>
    /// The joiner's copy of the host walks as the host walks: its body's pose moves from tick to tick. Its clip's blends once
    /// never stepped, and a partner that set off slid over the ground in the pose it stood in.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_joiners_copy_of_the_walking_host_moves_its_limbs()
    {
        using Igi2CoopPair pair = Joined();
        Assert.True(pair.RunUntil(() => pair.Joiner!.NetworkHumans.Count == 1, 300) >= 0, "the joiner never drew the host");
        Igi2NetworkHuman host = Assert.Single(pair.Joiner!.NetworkHumans);
        Igi2CoopPair.Hold(pair.HostInput, KeyCode.W, true);
        pair.RunUntil(() => false, 60);

        HashSet<string> poses = [];
        for (int i = 0; i < 30; i++)
        {
            pair.RunUntil(() => false, 1);
            if (host.Human.BodyPose() is { } pose)
            {
                System.Text.StringBuilder key = new();
                foreach (Matrix3f r in pose.Pose.Rotations)
                {
                    key.Append(System.Globalization.CultureInfo.InvariantCulture, $"{r.M11:F3}{r.M12:F3}{r.M21:F3};");
                }

                poses.Add(key.ToString());
            }
        }

        Igi2CoopPair.Hold(pair.HostInput, KeyCode.W, false);
        Assert.True(poses.Count > 10, $"the copy of the walking host took {poses.Count} distinct leg poses in 30 ticks");
    }

    /// <summary>
    /// The host's copy of a walking joiner moves its legs, as the joiner's copy of the host does: the gait's clip poses the
    /// legs, and the weapon's upper-body clip only the bones from the lower back up.
    /// </summary>
    [RequiresTitleDataTheory("igi2")]
    [InlineData("", "")]
    [InlineData("priboi", "priboi")]
    public void Each_sides_copy_of_the_other_walking_moves_its_legs(string hostLook, string joinerLook)
    {
        using Igi2CoopPair pair = hostLook.Length == 0 ? Joined() : new(hostLook, joinerLook);
        Assert.True(
            pair.RunUntil(() => pair.Joined && pair.Joiner!.NetworkHumans.Count == 1 && pair.Host!.NetworkHumans.Count == 1, 4000) >= 0,
            "the two never drew each other");
        Igi2CoopPair.Hold(pair.JoinerInput, KeyCode.W, true);
        Igi2CoopPair.Hold(pair.HostInput, KeyCode.W, true);
        pair.RunUntil(() => false, 60);
        HashSet<string> onHost = [], onJoiner = [];
        for (int i = 0; i < 30; i++)
        {
            pair.RunUntil(() => false, 1);
            onHost.Add(PoseKey(Assert.Single(pair.Host!.NetworkHumans)));
            onJoiner.Add(PoseKey(Assert.Single(pair.Joiner!.NetworkHumans)));
        }

        Igi2CoopPair.Hold(pair.JoinerInput, KeyCode.W, false);
        Igi2CoopPair.Hold(pair.HostInput, KeyCode.W, false);
        Assert.True(onHost.Count > 10, $"the host's copy of the walking joiner took {onHost.Count} distinct leg poses in 30 ticks");
        Assert.True(onJoiner.Count > 10, $"the joiner's copy of the walking host took {onJoiner.Count} distinct leg poses in 30 ticks");
    }

    /// <summary>
    /// After the mission's intro has played on both games (as a real session starts, not skipped on the command line), each
    /// side's copy of the other walks with moving legs.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void After_the_intro_each_sides_copy_of_the_other_walking_moves_its_legs()
    {
        using Igi2CoopPair pair = new(withIntro: true);
        Assert.True(
            pair.RunUntil(() => pair.Joined && pair.Joiner!.NetworkHumans.Count == 1 && pair.Host!.NetworkHumans.Count == 1, 4000) >= 0,
            "the two never drew each other");
        pair.RunUntil(() => false, 200);
        Igi2CoopPair.Hold(pair.HostInput, KeyCode.Escape, true);
        pair.RunUntil(() => false, 3);
        Igi2CoopPair.Hold(pair.HostInput, KeyCode.Escape, false);
        Assert.True(pair.RunUntil(() => pair.Host!.Level?.CutsceneView is null && pair.Joiner!.Level?.CutsceneView is null, 6000) >= 0, "the intro never ended");
        pair.RunUntil(() => false, 60);
        Igi2CoopPair.Hold(pair.JoinerInput, KeyCode.W, true);
        Igi2CoopPair.Hold(pair.HostInput, KeyCode.W, true);
        pair.RunUntil(() => false, 60);
        HashSet<string> onHost = [], onJoiner = [];
        for (int i = 0; i < 30; i++)
        {
            pair.RunUntil(() => false, 1);
            onHost.Add(PoseKey(Assert.Single(pair.Host!.NetworkHumans)));
            onJoiner.Add(PoseKey(Assert.Single(pair.Joiner!.NetworkHumans)));
        }

        Igi2CoopPair.Hold(pair.JoinerInput, KeyCode.W, false);
        Igi2CoopPair.Hold(pair.HostInput, KeyCode.W, false);
        Igi2NetworkHuman h = Assert.Single(pair.Host!.NetworkHumans), j = Assert.Single(pair.Joiner!.NetworkHumans);
        Assert.True(onHost.Count > 10 && onJoiner.Count > 10, $"host's copy {onHost.Count} poses (state {h.Human.Locomotion.CurrentState}), joiner's copy {onJoiner.Count} poses (state {j.Human.Locomotion.CurrentState})");
    }

    /// <summary>
    /// A player who looks up and then turns on the spot keeps its spine on its aim in the other's game: the spine follows the
    /// upper body's state (held up or firing), as the original's lower back reads it, not the state the body moves in.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_joiners_copy_of_a_host_looking_up_keeps_its_spine_up_while_the_host_turns()
    {
        using Igi2CoopPair pair = Joined();
        Assert.True(pair.RunUntil(() => pair.Joiner!.NetworkHumans.Count == 1, 4000) >= 0, "the joiner never drew the host");
        for (int i = 0; i < 40; i++)
        {
            pair.HostInput.Mouse.AddMotion(0f, -12f);
            pair.RunUntil(() => false, 1);
        }

        pair.RunUntil(() => false, 60);
        Igi2NetworkHuman copy = Assert.Single(pair.Joiner!.NetworkHumans);
        float pitch = copy.Human.View.Pitch;
        Assert.True(Math.Abs(pitch) > 0.2f, $"the host never looked away from level: its copy's pitch is {pitch}");
        Assert.True(
            Math.Abs(copy.Human.View.ShouldersPitch - pitch) < 0.02f,
            $"the copy standing still bends {copy.Human.View.ShouldersPitch} for a look of {pitch}");
        bool turned = false;
        for (int i = 0; i < 60; i++)
        {
            pair.HostInput.Mouse.AddMotion(6f, 0f);
            pair.RunUntil(() => false, 1);
            _ = copy.Human.BodyPose();
            turned |= copy.Human.Locomotion.CurrentState == Igi2PlayerState.StandTurn;
            Assert.True(
                Math.Abs(copy.Human.View.ShouldersPitch - copy.Human.View.Pitch) < 0.05f,
                $"turning, the copy bends {copy.Human.View.ShouldersPitch} for a look of {copy.Human.View.Pitch} (tick {i})");
        }

        Assert.True(turned, "the host's copy never turned on the spot");
    }

    /// <summary>
    /// A partner at a timed action -- a gate's lock picked, a code pad hacked -- is shown at it: the action names its own clip,
    /// which the copy reads from the action it stands at. It once played nothing, and the partner stood there with its gun up.
    /// </summary>
    [RequiresTitleDataTheory("igi2")]
    [InlineData("pick_lock")]
    [InlineData("tba_hack_code_pad")]
    public void The_joiners_copy_of_a_host_at_a_timed_action_plays_the_actions_clip(string clip)
    {
        using Igi2CoopPair pair = Joined();
        Assert.True(pair.RunUntil(() => pair.Joiner!.NetworkHumans.Count == 1, 4000) >= 0, "the joiner never drew the host");
        pair.RunUntil(() => false, 30);
        Game.Igi2.Interaction.Igi2GenericTbaTask action = pair.Host!.LevelTasks().OfType<Game.Igi2.Interaction.Igi2GenericTbaTask>()
            .First(t => string.Equals(t.ActivateAnimation, clip, StringComparison.Ordinal));

        // The host is put at the action and into its work, as its activate key would put it.
        const System.Reflection.BindingFlags Members =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        Igi2HumanPlayer host = pair.Host.Human;
        Igi2HumanPlayer copy = Assert.Single(pair.Joiner!.NetworkHumans).Human;
        bool shown = false;
        for (int i = 0; i < 90 && !shown; i++)
        {
            if (host.Locomotion.CurrentState != Igi2PlayerState.ActivateGenericTba)
            {
                host.Position = action.Centre;
                typeof(Igi2HumanPlayer).GetProperty(nameof(Igi2HumanPlayer.ActivatingTarget), Members)!.SetValue(host, action);
                typeof(Igi2HumanPlayer).GetProperty(nameof(Igi2HumanPlayer.IsActivating), Members)!.SetValue(host, true);
                typeof(Igi2HumanPlayer).GetMethod("Go", Members)!.Invoke(host, [Igi2PlayerState.ActivateGenericTba]);
            }

            pair.RunUntil(() => false, 1);
            shown = copy.Locomotion.CurrentState == Igi2PlayerState.ActivateGenericTba
                && string.Equals(((OpenIGI.Game.Igi2.Humans.Igi2HumanAnimation)copy.Animation).Main.Clip?.Name, clip, StringComparison.Ordinal);
        }

        Assert.True(
            shown,
            $"the copy is in state {copy.Locomotion.CurrentState} playing {((OpenIGI.Game.Igi2.Humans.Igi2HumanAnimation)copy.Animation).Main.Clip?.Name}, not {clip}");
    }

    // A body's legs as its pose has them: what a gait moves and an upper-body clip must leave alone.
    private static string PoseKey(Igi2NetworkHuman other)
    {
        System.Text.StringBuilder key = new();
        if (other.Human is { Model: { } model } human && human.BodyPose() is { } pose)
        {
            for (int bone = 0; bone < pose.Pose.BoneCount && bone < model.BoneNames.Count; bone++)
            {
                if (model.BoneNames[bone].Contains("leg", StringComparison.Ordinal))
                {
                    Matrix3f r = pose.Pose.Rotations[bone];
                    key.Append(System.Globalization.CultureInfo.InvariantCulture, $"{r.M11:F3}{r.M12:F3}{r.M21:F3};");
                }
            }
        }

        return key.ToString();
    }

    /// <summary>
    /// A joiner running in a curve and firing as it goes is where its server has it: the server's copy runs its commands from
    /// the same look, settings and seed, so the prediction is never put right. Its turn once carried the ground speed round on
    /// the client alone (steady turns), and every turn at a run slid the server's copy aside.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_joiner_running_in_a_curve_and_firing_is_never_corrected()
    {
        using Igi2CoopPair pair = Joined();
        pair.RunUntil(() => false, 60);
        long before = pair.Client.Session!.Reconciler.Corrections;

        Igi2CoopPair.Hold(pair.JoinerInput, KeyCode.W, true);
        for (int i = 0; i < 240; i++)
        {
            pair.JoinerInput.Mouse.AddMotion(i % 80 < 40 ? 6f : -6f, 0f);
            pair.JoinerInput.Mouse.SetButton(MouseButton.Left, i % 30 < 10);
            pair.RunUntil(() => false, 1);
        }

        Igi2CoopPair.Hold(pair.JoinerInput, KeyCode.W, false);
        pair.JoinerInput.Mouse.SetButton(MouseButton.Left, false);
        long corrections = pair.Client.Session.Reconciler.Corrections - before;
        Assert.True(corrections <= 1, $"{corrections} corrections in 8 s of running, turning and firing; last error {pair.Client.Session.Reconciler.LastError:F0}");
    }

    [RequiresTitleDataFact("igi2")]
    public void Every_soldier_is_sent_and_the_joiner_poses_its_copies_where_the_hosts_stand()
    {
        using Igi2CoopPair pair = Joined();
        pair.RunUntil(() => false, 120);
        Snapshot? latest = pair.Client.Session!.Latest;
        Assert.NotNull(latest);
        IReadOnlyList<Igi2HumanSoldier> host = pair.Host!.Soldiers!.Bodies;
        IReadOnlyList<Igi2HumanSoldier> joiner = pair.Joiner!.Soldiers!.Bodies;
        Assert.Equal(host.Count, joiner.Count);
        int sent = latest.Humans.Count(static h => ReplicatedEntity.IsSoldier(h.Id));
        Assert.Equal(host.Count(static s => s.IsActive), sent);

        // No mind of the joiner's runs a soldier: every one is the host's puppet, standing where the snapshots it draws put it --
        // the host's a moment ago, as every other player is drawn.
        ClientSession session = pair.Client.Session!;
        Assert.NotNull(session.DrawnTick);
        for (int i = 0; i < joiner.Count; i++)
        {
            Assert.True(joiner[i].Puppet is not null, $"soldier {i} runs its own mind on the joiner");
            Assert.True(host[i].Puppet is null, $"soldier {i} is a puppet on the host");
            if (host[i].IsActive && !host[i].IsDead && session.Remote.Sample(session.DrawnTick.Value, Igi2ReplicatedWorld.SoldierEntity(i), out ReplicatedHuman seen))
            {
                double apart = Vector3d.Distance(seen.Position.ToVector(), joiner[i].Position);
                Assert.True(
                    apart < 1024,
                    $"soldier {i} stands {apart} units from where the snapshots put it (active here {joiner[i].IsActive}, {Vector3d.Distance(host[i].Position, joiner[i].Position)} from the host's)");
            }
        }
    }

    [RequiresTitleDataFact("igi2")]
    public void A_soldier_the_host_kills_falls_on_the_joiner()
    {
        using Igi2CoopPair pair = Joined();
        int index = IndexOfLiving(pair.Host!.Soldiers!.Bodies);
        Assert.True(index >= 0, "the mission has no living soldier");
        pair.Host.Soldiers.Bodies[index].KillOutright();
        Igi2HumanSoldier copy = pair.Joiner!.Soldiers!.Bodies[index];
        Assert.True(pair.RunUntil(() => copy.IsDead, 300) >= 0, "the joiner's copy of the soldier never fell");
    }

    /// <summary>
    /// The joiner's round marks its hit there and then, and the copy it struck stays as the server poses it: its flinch and its
    /// fall are the server's word alone, as Source's are. Felled at once on the joiner's screen by a round heavy enough to kill
    /// there, a guard the server's round met elsewhere lay while the server's moved on and got up somewhere else.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_guard_the_joiners_round_strikes_is_marked_at_once_and_posed_by_the_server()
    {
        using Igi2CoopPair pair = Joined();
        pair.Ping(0.1);
        int index = IndexOfLiving(pair.Host!.Soldiers!.Bodies);
        Assert.True(index >= 0, "the mission has no living soldier");
        pair.RunUntil(() => false, 60);
        Igi2HumanSoldier copy = pair.Joiner!.Soldiers!.Bodies[index];
        Igi2HumanPlayer own = pair.Joiner.Human;
        (int hits, int kills) = (own.Shots.Hits, own.Shots.Kills);
        copy.WeaponHit(new OpenIGI.Game.Igi2.Weapons.Igi2WeaponHit(
            new OpenIGI.Game.Physics.WorldIntersection { Position = copy.Position }, 1_000_000f, new Vector3f(40960f, 0f, 0f),
            own, pair.Joiner.Weapons!.Inventory.Current!.WeaponType, null));

        Assert.False(copy.IsDead, "the joiner's copy of the guard fell to the joiner's own round");
        Assert.Equal(hits + 1, own.Shots.Hits);
        Assert.Equal(kills, own.Shots.Kills);
        pair.RunUntil(() => false, 30);
        Assert.False(copy.IsDead);
        Assert.Equal(copy.Puppet!.Value.State, copy.Locomotion.CurrentState);
    }

    /// <summary>
    /// The guns dead guards drop lie on the joiner where they lie on the host, the same guns and no others: each machine once
    /// dropped its own -- the joiner's copies the guns they were made with, by its own stream -- and a gun the host took could
    /// still be taken on the joiner.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_guns_dead_guards_drop_lie_on_the_joiner_as_on_the_host()
    {
        using Igi2CoopPair pair = Joined();
        IReadOnlyList<Igi2HumanSoldier> bodies = pair.Host!.Soldiers!.Bodies;
        int killed = 0;
        for (int i = 0; i < bodies.Count && killed < 3; i++)
        {
            if (!bodies[i].IsDead && bodies[i].IsActive && bodies[i].Weapon.Inventory?.Current is not null)
            {
                bodies[i].KillOutright();
                killed++;
            }
        }

        Assert.True(killed > 0, "the mission has no armed living soldier");
        pair.RunUntil(() => false, 240);

        static List<(string Weapon, Vector3d At)> Dropped(Igi2TitlePlayer level) =>
            [.. level.Human.Pickups!.Items.Where(static g => g.Task is null && !g.IsDelete && !g.IsMoving).Select(static g => (g.WeaponType.WeaponId, g.Position))];

        List<(string Weapon, Vector3d At)> host = Dropped(pair.Host), joiner = Dropped(pair.Joiner!);
        Assert.NotEmpty(host);
        Assert.Equal(host.Count, joiner.Count);
        foreach ((string weapon, Vector3d at) in host)
        {
            Assert.Contains(joiner, j => j.Weapon == weapon && Vector3d.Distance(j.At, at) < 64);
        }
    }

    /// <summary>
    /// A mine laid on the host lies on the joiner, still, and goes when the host's does; one the joiner's own game would lay is
    /// not laid there. Mines once travelled with nothing: a partner saw none, and the layer's own lay live and went off alone.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_mine_laid_on_the_host_lies_still_on_the_joiner_and_goes_with_it()
    {
        using Igi2CoopPair pair = Joined();
        OpenIGI.Game.Igi2.Weapons.Igi2WeaponTables tables = pair.Host!.Weapons!.Inventory.Tables;
        Vector3d at = pair.Host.Human.Position + new Vector3d(4096, 0, 0);
        float[] frame = OpenIGI.Game.EngineEuler.Rotation(0f, 0f, 0f);
        OpenIGI.Game.Igi2.Weapons.Igi2Mine.Saved saved = new(at, frame, false, false, 0f, -1, 0, 0, 0, 0f, 0f, false, false);
        OpenIGI.Game.Igi2.Weapons.Igi2Mine laid = pair.Host.Mines!.Recreate(saved, tables)!;
        Assert.NotNull(laid);

        Assert.True(pair.RunUntil(() => pair.Joiner!.Mines!.Laid.Count == 1, 120) >= 0, "the host's mine never lay on the joiner");
        OpenIGI.Game.Igi2.Weapons.Igi2Mine copy = pair.Joiner!.Mines!.Laid[0];
        Assert.True(copy.IsReplica);
        Assert.True(Vector3d.Distance(copy.Position, at) < 64);

        // The joiner's own laying lays nothing of its own.
        pair.Joiner.Mines.Recreate(saved with { Position = at + new Vector3d(0, 4096, 0) }, pair.Joiner.Weapons!.Inventory.Tables);
        Assert.Single(pair.Joiner.Mines.Laid);

        laid.Delete();
        pair.Host.Mines.Remove(laid);
        Assert.True(pair.RunUntil(() => pair.Joiner.Mines.Laid.Count == 0, 120) >= 0, "the joiner's copy outlived the host's mine");
    }

    /// <summary>
    /// An object that explodes is the server's to blow up: on the joiner its own rounds do not set it off -- it went up twice on
    /// the shooter's screen, its own and the server's -- and the server's word that it went up puts on what is left of it.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_joiners_exploding_objects_go_up_on_the_servers_word_alone()
    {
        using Igi2CoopPair pair = Joined();
        OpenIGI.Game.Igi2.Interaction.Igi2ExplodeObjectTask target = pair.Joiner!.LevelTasks()
            .OfType<OpenIGI.Game.Igi2.Interaction.Igi2ExplodeObjectTask>()
            .First(static o => !o.Explosive.IsExploded && o.DestroyedModel.Length > 0 && o.Explosive.Models?.Invoke(o.DestroyedModel) is not null);

        target.Explosive.WeaponHit(1_000_000f);
        pair.RunUntil(() => false, 10);
        Assert.False(target.Explosive.IsExploded, $"{target.Name} went up on the joiner's own round");

        Assert.True(pair.Joiner.Level!.Natives.TryResolve($"{target.Name}.isExploded", out QvmSymbol? isExploded));
        string whole = target.Explosive.Model;
        isExploded!.Variable!.TryWrite(QvmValue.FromInt32(1), out _);
        target.Explosive.Run(pair.Joiner.Level);
        Assert.True(target.Explosive.IsExploded);
        Assert.True(target.Explosive.Model == target.DestroyedModel && target.Explosive.Model != whole, $"{target.Name} kept its whole model");
    }

    /// <summary>
    /// The joiner's copy of a firing host fires: its upper body plays the shot as the host's does, the gun still hung in its
    /// hands, and the gun flashes. The copy's own action states once ran on empty controls, and it never fired, reloaded or drew
    /// on the joiner's screen.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_joiners_copy_of_a_firing_host_fires()
    {
        using Igi2CoopPair pair = Joined();
        Assert.True(pair.RunUntil(() => pair.Joiner!.NetworkHumans.Count == 1, 300) >= 0, "the joiner never drew the host");
        Igi2NetworkHuman copy = Assert.Single(pair.Joiner!.NetworkHumans);
        pair.RunUntil(() => false, 30);

        bool fired = false;
        bool flashed = false;
        pair.HostInput.Mouse.SetButton(MouseButton.Left, true);
        for (int i = 0; i < 60 && !(fired && flashed); i++)
        {
            pair.RunUntil(() => false, 1);
            fired |= copy.Human.CurrentUpperBody is 6 or 7;
            flashed |= pair.Joiner.WorldModels.Any(static m => m.MuzzleFlashes is { Count: > 0 });
        }

        pair.HostInput.Mouse.SetButton(MouseButton.Left, false);
        Assert.True(fired, $"the copy's upper body stayed at {copy.Human.CurrentUpperBody} while the host fired");

        // Its gun flashes in its body's hands: nobody looks through a copy's eyes, so its weapon object is the body's.
        Assert.True(flashed, "the copy's gun never flashed while the host fired");

        string? gun = copy.Weapons?.Inventory.Current?.WeaponType.FixedModel;
        IReadOnlyList<TitleWorldModel> drawn = Igi2BodyFrame.Build(copy.Human, copy.Human.BodyPose(), copy.Human.Model!.Name, gun, 0);
        Assert.Contains(drawn, part => string.Equals(part.Model, gun, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A partner at work -- manning a gun, hacking a terminal, picking a lock, searching a cabinet -- is shown at it: those
    /// states' entries needed what is worked, which a copy has none of, and the copy stood there with its gun up.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_joiners_copy_of_a_working_host_plays_the_work()
    {
        using Igi2CoopPair pair = Joined();
        Assert.True(pair.RunUntil(() => pair.Joiner!.NetworkHumans.Count == 1, 300) >= 0, "the joiner never drew the host");
        Igi2HumanPlayer copy = Assert.Single(pair.Joiner!.NetworkHumans).Human;
        (int State, int Clip)[] work =
        [
            (Igi2PlayerState.ActivateStationaryGun, Igi2HumanPlayer.StationaryGunClip),
            (Igi2PlayerState.ActivateTerminal, Igi2HumanPlayer.HackAnimation),
            (Igi2PlayerState.ActivateDoor, Igi2HumanPlayer.PickLockAnimation),
            (Igi2PlayerState.ActivateCabinet, Igi2HumanPlayer.SearchLoopClip),
        ];
        foreach ((int state, int clip) in work)
        {
            copy.PoseAsPuppet(Igi2PlayerState.Stand, false, 0, 0, 0, copy.Position, default, copy.Gamma, 0f);
            copy.PoseAsPuppet(state, false, 0, 0, 0, copy.Position, default, copy.Gamma, 0f);
            Assert.Equal(clip, ((OpenIGI.Game.Igi2.Humans.Igi2HumanAnimation)copy.Animation).LastState);
        }
    }

    /// <summary>
    /// A host hit while standing flinches on the joiner's screen with the server's clip: the copy is never hit itself, and its
    /// flinch's entry once read its zone table at no zone and stopped the joiner's game.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_joiners_copy_of_a_hit_host_flinches_with_the_servers_clip()
    {
        using Igi2CoopPair pair = Joined();
        Assert.True(pair.RunUntil(() => pair.Joiner!.NetworkHumans.Count == 1, 300) >= 0, "the joiner never drew the host");
        Igi2HumanPlayer copy = Assert.Single(pair.Joiner!.NetworkHumans).Human;
        Assert.Equal(OpenIGI.Game.Igi2.Humans.Igi2DamagePart.None, copy.Damage.LastPart);
        foreach (int clip in new[] { 58, 61, 62, 63, 64, 0 })
        {
            copy.PoseAsPuppet(Igi2PlayerState.Stand, false, 0, 0, 0, copy.Position, default, copy.Gamma, 0f);
            copy.Damage.LastPart = OpenIGI.Game.Igi2.Humans.Igi2DamagePart.None;
            copy.PoseAsPuppet(Igi2PlayerState.Hit, false, 0, 0, 0, copy.Position, default, copy.Gamma, 0f, clip);
            Assert.Equal(clip == 0 ? 64 : clip, ((OpenIGI.Game.Igi2.Humans.Igi2HumanAnimation)copy.Animation).LastState);
        }
    }

    [RequiresTitleDataFact("igi2")]
    public void The_joiners_copy_of_the_host_is_no_target_of_its_soldiers_and_the_hosts_partner_is()
    {
        using Igi2CoopPair pair = Joined();
        pair.RunUntil(() => pair.Joiner!.NetworkHumans.Count == 1, 300);
        Igi2NetworkHuman hostShown = Assert.Single(pair.Joiner!.NetworkHumans);
        Assert.DoesNotContain(hostShown.Human, Players(pair.Joiner));

        Igi2NetworkHuman partner = Assert.Single(pair.Host!.NetworkHumans);
        Assert.Contains(partner.Human, Players(pair.Host));
    }

    [RequiresTitleDataFact("igi2")]
    public void The_hosts_partner_carries_the_missions_loadout()
    {
        using Igi2CoopPair pair = Joined();
        pair.RunUntil(() => false, 30);
        string[] own = Carried(pair.Host!.Weapons!.Inventory);
        Assert.NotEmpty(own);
        Igi2NetworkHuman partner = Assert.Single(pair.Host.NetworkHumans);
        Assert.Equal(own, Carried(partner.Weapons!.Inventory));
        Assert.Equal(own, Carried(pair.Joiner!.Weapons!.Inventory));
    }

    /// <summary>
    /// The mission's state on the joiner is the host's: every conditional container, area, cutscene and alarm holds what the
    /// host's does once both have stood a moment. The joiner once worked each out from its own world -- its own player alone in
    /// its areas, its own cameras, its own scripts -- and a trigger a partner set off fired on the host only.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_joiners_mission_state_is_the_hosts()
    {
        using Igi2CoopPair pair = Joined();
        Igi2CoopPair.Hold(pair.JoinerInput, KeyCode.W, true);
        pair.RunUntil(() => false, 240);
        Igi2CoopPair.Hold(pair.JoinerInput, KeyCode.W, false);
        pair.RunUntil(() => false, 45);

        NativeRegistry hostNatives = pair.Host!.Level!.Natives;
        NativeRegistry joinerNatives = pair.Joiner!.Level!.Natives;
        string[] fields = [".isRun", ".nActive", ".isAlarm", ".isTrigger"];
        List<string> differ = [];
        int compared = 0;
        foreach (string name in hostNatives.Names.Where(n => fields.Any(f => n.EndsWith(f, StringComparison.Ordinal))).Order(StringComparer.Ordinal))
        {
            if (!hostNatives.TryResolve(name, out QvmSymbol? host) || host.Variable is not { } hostVariable
                || !joinerNatives.TryResolve(name, out QvmSymbol? joiner) || joiner.Variable is not { } joinerVariable)
            {
                continue;
            }

            compared++;
            int there = hostVariable.Read().ToInt32(), here = joinerVariable.Read().ToInt32();
            if (there != here)
            {
                differ.Add($"{name}: host {there}, joiner {here}");
            }
        }

        Assert.True(compared > 10, $"only {compared} variables compared");
        Assert.True(differ.Count == 0, string.Join("; ", differ));
    }

    /// <summary>
    /// The joiner's containers and areas take the values their server's mirror writes, and act on them, rather than working
    /// them out afresh from the joiner's own world: a container told it no longer runs takes its children out, and an area keeps
    /// the count it was given.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_joiners_containers_and_areas_follow_the_values_the_server_writes()
    {
        using Igi2CoopPair pair = Joined();
        pair.RunUntil(() => false, 30);
        Igi2TitlePlayer joiner = pair.Joiner!;
        ConditionalContainerTask container = joiner.LevelTasks().OfType<ConditionalContainerTask>()
            .First(static c => c.IsRun && c.IsActive && c.Condition.Length > 0);
        AreaActivateTask area = joiner.LevelTasks().OfType<AreaActivateTask>().First();

        NativeRegistry natives = joiner.Level!.Natives;
        Assert.True(natives.TryResolve($"{container.Name}.isRun", out QvmSymbol? isRun));
        Assert.True(natives.TryResolve($"{area.Name}.nActive", out QvmSymbol? active));
        isRun!.Variable!.TryWrite(QvmValue.FromInt32(0), out _);
        pair.RunUntil(() => false, 1);
        Assert.False(container.IsActive, $"{container.Name} kept its children in though its server said it no longer runs");

        // The area's own run, straight after the write: a tick of the pair may already bring the server's next word.
        active!.Variable!.TryWrite(QvmValue.FromInt32(7), out _);
        area.Run(joiner.Level);
        Assert.Equal(7, area.Active);
    }

    [RequiresTitleDataFact("igi2")]
    public void A_level_variable_the_host_changes_is_changed_on_the_joiner()
    {
        using Igi2CoopPair pair = Joined();
        NativeRegistry hostNatives = pair.Host!.Level!.Natives;
        NativeRegistry joinerNatives = pair.Joiner!.Level!.Natives;
        // A level variable the host's own tasks leave as a script writes it: one its task works out afresh each tick (an alarm's
        // isActive) is no test of what is sent.
        string? name = null;
        QvmSymbol? hostVariable = null;
        int value = 0;
        foreach (string candidate in hostNatives.Names.Where(static n => n.Contains('.', StringComparison.Ordinal)
            && !n.StartsWith("Human", StringComparison.Ordinal)).Order(StringComparer.Ordinal).Take(400))
        {
            if (!hostNatives.TryResolve(candidate, out QvmSymbol? symbol) || symbol.Variable is not { ValueType: QvmValueKind.Integer } variable)
            {
                continue;
            }

            int was = variable.Read().ToInt32();
            int wanted = was == 0 ? 1 : 0;
            variable.TryWrite(QvmValue.FromInt32(wanted), out _);
            pair.RunUntil(() => false, 12);
            if (variable.Read().ToInt32() == wanted)
            {
                (name, hostVariable, value) = (candidate, symbol, wanted);
                break;
            }

            variable.TryWrite(QvmValue.FromInt32(was), out _);
        }

        Assert.NotNull(name);
        Assert.True(joinerNatives.TryResolve(name, out QvmSymbol? joinerVariable));
        Assert.True(
            pair.RunUntil(() => joinerVariable.Read().ToInt32() == value, 120) >= 0,
            $"{name} is {joinerVariable.Read().ToInt32()} on the joiner, {value} on the host");
    }

    [RequiresTitleDataFact("igi2")]
    public void A_fallen_partner_lies_down_on_its_own_game_and_the_host_standing_by_brings_it_back()
    {
        using Igi2CoopPair pair = Joined();
        Igi2NetworkHuman partner = Assert.Single(pair.Host!.NetworkHumans);
        string? carried = partner.Weapons?.Inventory.Current?.WeaponType.WeaponId;
        Assert.NotNull(carried);
        partner.Human.Damage.Damage = partner.Human.Damage.MaxDamage + 1f;
        partner.Human.UpdateDie();
        Assert.True(pair.RunUntil(() => pair.Joiner!.Human.IsDead, 300) >= 0, "the joiner's own player never fell");

        // Lying long enough for the death to settle, where it once emptied the inventory.
        pair.RunUntil(() => partner.Human.IsDeathAnimationDone && pair.Joiner!.Human.IsDeathAnimationDone, 300);

        // The host walks to it: put beside it, and held there as long as a revival takes.
        pair.Host.Human.Position = partner.Human.Position + new Vector3d(2048, 0, 0);
        Assert.True(
            pair.RunUntil(() => !partner.Human.IsDead && !pair.Joiner!.Human.IsDead, CoopSquad.ReviveTicks * 4 + 300) >= 0,
            $"not revived: on the host {partner.Human.IsDead}, on the joiner {pair.Joiner!.Human.IsDead}");

        // Revived with what it carried, on the server and on its own game: a revived partner once stood up empty-handed.
        Assert.Equal(carried, partner.Weapons?.Inventory.Current?.WeaponType.WeaponId);
        Assert.Equal(carried, pair.Joiner!.Weapons?.Inventory.Current?.WeaponType.WeaponId);
    }

    [RequiresTitleDataFact("igi2")]
    public void A_door_the_host_opens_is_told_open_on_the_joiner()
    {
        using Igi2CoopPair pair = Joined();
        Game.Igi2.Interaction.Igi2DoorTask[] hostDoors = [.. pair.Host!.LevelTasks().OfType<Game.Igi2.Interaction.Igi2DoorTask>()];
        Game.Igi2.Interaction.Igi2DoorTask[] joinerDoors = [.. pair.Joiner!.LevelTasks().OfType<Game.Igi2.Interaction.Igi2DoorTask>()];
        int index = Array.FindIndex(hostDoors, static d => !d.IsCommandedOpen);
        Assert.True(index >= 0, $"no shut door among {hostDoors.Length}");
        hostDoors[index].Command(open: true, swingsBack: false);
        Assert.True(pair.RunUntil(() => joinerDoors[index].IsCommandedOpen, 120) >= 0, $"door {index} never opened on the joiner");
    }

    [RequiresTitleDataFact("igi2")]
    public void A_weapon_taken_on_the_host_is_gone_on_the_joiner()
    {
        using Igi2CoopPair pair = Joined();
        Game.Igi2.Pickups.Igi2GunPickupTask[] host = [.. pair.Host!.LevelTasks().OfType<Game.Igi2.Pickups.Igi2GunPickupTask>()];
        Game.Igi2.Pickups.Igi2GunPickupTask[] joiner = [.. pair.Joiner!.LevelTasks().OfType<Game.Igi2.Pickups.Igi2GunPickupTask>()];
        int index = Array.FindIndex(host, static g => g.Pickup is { IsDelete: false });
        Assert.True(index >= 0, $"no weapon lying among {host.Length}");
        host[index].Pickup!.TakenElsewhere();
        Assert.True(pair.RunUntil(() => joiner[index].Pickup is not { IsDelete: false }, 120) >= 0, $"weapon {index} still lies on the joiner");
    }

    [RequiresTitleDataFact("igi2")]
    public void The_joiner_follows_the_host_into_the_mission_it_goes_on_to()
    {
        using Igi2CoopPair pair = Joined();
        object hostLevel = pair.Host!, joinerLevel = pair.Joiner!;
        pair.RunUntil(() => false, 60);
        const int next = 21;
        typeof(Host.GameHost).GetMethod("StartMission", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(pair.HostGame, [next]);
        // Every level the joiner plays on the way, by the mission it is: one, the host's next, loaded once.
        List<int> joinerMissions = [];
        object? lastJoiner = null;
        Assert.True(
            pair.RunUntil(
                () =>
                {
                    if (pair.Joiner is { } now && !ReferenceEquals(now, joinerLevel) && !ReferenceEquals(now, lastJoiner))
                    {
                        lastJoiner = now;
                        joinerMissions.Add(now.MissionId);
                    }

                    return pair.Host is { } h && !ReferenceEquals(h, hostLevel) && pair.Joiner is { } j && !ReferenceEquals(j, joinerLevel)
                        && h.NetworkHumans.Count == 1 && !pair.Client.HoldsLevel && j.TickCount > 150;
                },
                6000) >= 0,
            $"the pair never came into the next level: host on {pair.Host?.MissionId}, joiner on {pair.Joiner?.MissionId}, held {pair.Client.HoldsLevel}");
        Assert.Equal(next, pair.Host!.MissionId);
        Assert.Equal([next], joinerMissions);
    }

    [RequiresTitleDataFact("igi2")]
    public void An_explosion_on_the_host_goes_off_on_the_joiner_once_and_harms_nobody_there()
    {
        using Igi2CoopPair pair = Joined();
        pair.RunUntil(() => false, 30);
        Game.Igi2.Weapons.Igi2ExplosionWorld host = pair.Host!.Explosions!;
        Game.Igi2.Weapons.Igi2ExplosionWorld joiner = pair.Joiner!.Explosions!;
        int replays = 0;
        joiner.Started = _ => replays++;
        int before = Blasts(joiner);
        Vector3d at = pair.Host.Human.Position + new Vector3d(40960, 0, 0);
        host.Start(new Game.Igi2.Weapons.Igi2ExplosionRequest(
            at, 1, 1, 1f, 4096f, 4096f, 1024f, 4, 2, null, string.Empty, string.Empty, -1, 0, 0f, new Core.Numerics.Vector3f(0, 0, 1),
            1.5707964f, 272f, 1360f, null, null, default));
        Assert.True(pair.RunUntil(() => Blasts(joiner) > before, 120) >= 0, "the explosion never went off on the joiner");
        pair.RunUntil(() => false, 60);
        Assert.Equal(0, replays);

        static int Blasts(Game.Igi2.Weapons.Igi2ExplosionWorld world) => world.FragmentGroups.Count + world.Dynamic.Tasks.OfType<Game.Igi2.Weapons.Igi2Explosion>().Count();
    }

    [RequiresTitleDataFact("igi2")]
    public void A_round_the_host_fires_is_fired_again_from_its_copy_on_the_joiner_and_harms_nobody_there()
    {
        using Igi2CoopPair pair = Joined();
        pair.RunUntil(() => pair.Joiner!.NetworkHumans.Count == 1, 300);
        Game.Igi2.Weapons.Igi2WeaponFiring host = pair.Host!.Weapons!.Firing;
        Game.Igi2.Weapons.Igi2WeaponTypeTask weapon = host.Inventory.Current!.WeaponType;
        Game.Igi2.Weapons.Igi2GunTask gun = Assert.IsType<Game.Igi2.Weapons.Igi2GunTask>(weapon.FireType(0));

        // Straight at the joiner's own player, from the host's eyes.
        Vector3d eye = pair.Host.Human.View.Position;
        Vector3d target = pair.Joiner!.Human.Position + new Vector3d(0, 0, 4096);
        Vector3d along = target - eye;
        float yaw = (float)Math.Atan2(-along.X, along.Y);
        float pitch = (float)Math.Atan2(along.Z, Math.Sqrt((along.X * along.X) + (along.Y * along.Y)));
        float[] orientation = Game.EngineEuler.Rotation(pitch, 0f, yaw);
        Game.Igi2.Weapons.Igi2WeaponFiring copy = Assert.Single(pair.Joiner.NetworkHumans).Weapons!.Firing;
        Assert.True(copy.IsReplica);
        int before = copy.GunShot.BulletsFired;
        float health = pair.Joiner.Human.Damage.Damage;
        host.Launched!(new Game.Igi2.Weapons.Igi2LaunchNote(
            new Game.Igi2.Weapons.Igi2ProjectileLaunch(gun.ProjectileTaskType, eye, orientation, default, gun.MuzzleVelocityMps, gun.EffectiveRangeMeters, 0, weapon, null),
            0));
        Assert.True(pair.RunUntil(() => copy.GunShot.BulletsFired > before, 120) >= 0, "the host's round was never fired again on the joiner");
        pair.RunUntil(() => false, 10);
        Assert.Equal(health, pair.Joiner.Human.Damage.Damage);
    }

    [RequiresTitleDataFact("igi2")]
    public void A_lift_travelling_on_the_host_travels_with_it_on_the_joiner()
    {
        using Igi2CoopPair pair = JoinedOn(LiftMission);
        Game.Igi2.Interaction.Igi2LiftTask[] host = [.. pair.Host!.LevelTasks().OfType<Game.Igi2.Interaction.Igi2LiftTask>()];
        Game.Igi2.Interaction.Igi2LiftTask[] joiner = [.. pair.Joiner!.LevelTasks().OfType<Game.Igi2.Interaction.Igi2LiftTask>()];
        Assert.Equal(host.Length, joiner.Length);
        int index = Array.FindIndex(host, static l => l.Children.OfType<Game.Igi2.Interaction.Igi2FloorTask>().Count() >= 2);
        Assert.True(index >= 0, $"the mission has no lift of two floors among {host.Length}");

        // The host's lift set off from its first floor to its second.
        Game.Igi2.Interaction.Igi2FloorTask[] floors = [.. host[index].Children.OfType<Game.Igi2.Interaction.Igi2FloorTask>()];
        host[index].Follow(new Game.Igi2.Interaction.Igi2LiftLeg(0, 1, floors[0].Position, 40f), 0);
        pair.RunUntil(() => false, 20);
        Game.Igi2.Interaction.Igi2LiftLeg there = host[index].Leg();
        Game.Igi2.Interaction.Igi2LiftLeg here = joiner[index].Leg();
        Assert.Equal(there.Target, here.Target);
        double apart = Vector3d.Distance(there.Position, here.Position);
        Assert.True(apart < 4096, $"the joiner's car is {apart} units from the host's");
    }

    [RequiresTitleDataFact("igi2")]
    public void The_joiners_cars_stand_where_the_hosts_do_and_one_blown_up_there_is_a_wreck_here()
    {
        using Igi2CoopPair pair = JoinedOn(CarMission);
        pair.RunUntil(() => false, 60);
        Game.Igi2.Vehicles.Igi2CarTask[] host = [.. pair.Host!.LevelTasks().OfType<Game.Igi2.Vehicles.Igi2CarTask>()];
        Game.Igi2.Vehicles.Igi2CarTask[] joiner = [.. pair.Joiner!.LevelTasks().OfType<Game.Igi2.Vehicles.Igi2CarTask>()];
        Assert.Equal(host.Length, joiner.Length);
        Assert.True(host.Length > 0, "the mission has no cars");
        for (int i = 0; i < host.Length; i++)
        {
            Assert.True(joiner[i].IsPuppet, $"car {i} drives itself on the joiner");
            Assert.False(host[i].IsPuppet, $"car {i} is a puppet on the host");
            double apart = Vector3d.Distance(host[i].CurrentPosition, joiner[i].CurrentPosition);
            Assert.True(apart < 4096, $"car {i} stands {apart} units from the host's");
        }

        int index = Array.FindIndex(host, static c => !c.IsExploded && c.CarType is not null);
        Assert.True(index >= 0, "no car that can blow up");
        host[index].HitZoneDestroyed();
        Assert.True(pair.RunUntil(() => joiner[index].IsExploded, 300) >= 0, $"car {index} blew up on the host and not on the joiner");
    }

    [RequiresTitleDataFact("igi2")]
    public void The_joiners_trains_run_where_the_hosts_do()
    {
        using Igi2CoopPair pair = JoinedOn(TrainMission);
        pair.RunUntil(() => false, 90);
        Game.Igi2.Vehicles.Igi2TrainTask[] host = [.. pair.Host!.LevelTasks().OfType<Game.Igi2.Vehicles.Igi2TrainTask>()];
        Game.Igi2.Vehicles.Igi2TrainTask[] joiner = [.. pair.Joiner!.LevelTasks().OfType<Game.Igi2.Vehicles.Igi2TrainTask>()];
        Assert.Equal(host.Length, joiner.Length);
        Assert.True(host.Length > 0, "the mission has no trains");
        for (int i = 0; i < host.Length; i++)
        {
            Assert.True(joiner[i].IsPuppet, $"train {i} runs itself on the joiner");
            double apart = Vector3d.Distance(host[i].CurrentPosition, joiner[i].CurrentPosition);
            Assert.True(apart < 16384, $"train {i} runs {apart} units from the host's");
        }
    }

    [RequiresTitleDataFact("igi2")]
    public void The_joiners_turrets_point_where_the_hosts_do()
    {
        using Igi2CoopPair pair = JoinedOn(TurretMission);
        pair.RunUntil(() => false, 60);
        Game.Igi2.Weapons.Igi2AiStationaryGunHolderTask[] host = [.. pair.Host!.LevelTasks().OfType<Game.Igi2.Weapons.Igi2AiStationaryGunHolderTask>()];
        Game.Igi2.Weapons.Igi2AiStationaryGunHolderTask[] joiner = [.. pair.Joiner!.LevelTasks().OfType<Game.Igi2.Weapons.Igi2AiStationaryGunHolderTask>()];
        Assert.True(host.Length > 0 && host.Length == joiner.Length, $"turrets: host {host.Length}, joiner {joiner.Length}");
        for (int i = 0; i < host.Length; i++)
        {
            if (host[i].Gun is not { } theirs || joiner[i].Gun is not { } mine)
            {
                continue;
            }

            Assert.True(mine.IsPuppet, $"turret {i} aims itself on the joiner");
            Assert.True(mine.Firing.IsReplica, $"turret {i}'s rounds harm on the joiner");
            Assert.InRange(Math.Abs(mine.Turn - theirs.Turn), 0f, 0.2f);
        }
    }

    [RequiresTitleDataFact("igi2")]
    public void Each_side_names_the_other_over_its_head_and_marks_a_fallen_partner_with_its_revival()
    {
        using Igi2CoopPair pair = Joined();
        pair.RunUntil(() => pair.Joiner!.NameTags().Count == 1, 300);
        Assert.Contains(pair.Host!.NameTags(), static t => t.Name == "Anya");
        Assert.Single(pair.Joiner!.NameTags());

        // The joiner falls: the host marks where it lies, and the joiner's own panel shows its revival under way.
        Igi2NetworkHuman partner = Assert.Single(pair.Host.NetworkHumans);
        partner.Human.Damage.Damage = partner.Human.Damage.MaxDamage + 1f;
        partner.Human.UpdateDie();
        Assert.True(pair.RunUntil(() => pair.Host.ReviveMarkers().Count == 1 && pair.Client.OwnRevive is not null, 300) >= 0,
            $"marks {pair.Host.ReviveMarkers().Count}, own revive {pair.Client.OwnRevive}");
        Assert.Empty(pair.Host.NameTags());
    }

    private static int IndexOfLiving(IReadOnlyList<Igi2HumanSoldier> soldiers)
    {
        for (int i = 0; i < soldiers.Count; i++)
        {
            if (soldiers[i] is { IsActive: true, IsDead: false })
            {
                return i;
            }
        }

        return -1;
    }

    private static List<object> Players(Game.Igi2.Player.Igi2TitlePlayer level)
    {
        List<object> players = [];
        if (level.Ai is { } ai)
        {
            foreach (object player in ai.Players)
            {
                players.Add(player);
            }
        }

        return players;
    }

    private static string[] Carried(Game.Igi2.Weapons.Igi2Inventory inventory) =>
        [.. inventory.Weapons.Where(static w => w.IsActive).Select(static w => w.WeaponType.WeaponId).Order(StringComparer.Ordinal)];

    // A mission with a lift of two floors or more.
    private const int LiftMission = Igi2CoopPair.FirstMission;

    // Missions with cars and a mounted gun, with trains, and with a turret.
    private const int CarMission = 15;
    private const int TrainMission = 12;
    private const int TurretMission = 17;

    /// <summary>
    /// A host alone in a campaign mission played together who falls fails the mission, as the level's own "player dead" flow
    /// fails it (HumanPlayer_0.isDead), and is not told to wait for a teammate who is not there.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_host_alone_who_falls_fails_the_mission_and_waits_for_nobody()
    {
        using Igi2CoopPair pair = new([], [], joinLater: true);
        Assert.True(pair.RunUntil(() => pair.Host is not null, 4000) >= 0, "the host's level never came up");
        pair.RunUntil(() => false, 60);

        pair.Host!.Human.Damage.Damage = pair.Host.Human.Damage.MaxDamage + 1f;
        pair.Host.Human.UpdateDie();
        pair.RunUntil(() => false, 30);
        Assert.Null(pair.Session.OwnRevive);
        Assert.True(
            pair.RunUntil(() => pair.Host.Outcome == OpenIGI.Game.Player.TitleMissionOutcome.Failed, 3000) >= 0,
            "the mission never failed on its only player's death");
    }

    /// <summary>A host down while its partner stands waits to be revived, and the mission goes on.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_host_down_while_its_partner_stands_waits_for_it_and_the_mission_goes_on()
    {
        using Igi2CoopPair pair = Joined();
        pair.Host!.Human.Damage.Damage = pair.Host.Human.Damage.MaxDamage + 1f;
        pair.Host.Human.UpdateDie();
        pair.RunUntil(() => false, 30);
        Assert.NotNull(pair.Session.OwnRevive);
        Assert.Equal(-1, pair.RunUntil(() => pair.Host.Outcome == OpenIGI.Game.Player.TitleMissionOutcome.Failed, 900));
    }

    /// <summary>
    /// Each player wears the look it chose: the joiner's body on the host is its look's, borrowed from the multiplayer game's pack
    /// as the first mission does not ship it, and the joiner draws the host in the host's.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Each_player_wears_its_look_on_the_others_game_a_body_the_level_lacks_borrowed()
    {
        using Igi2CoopPair pair = new("chn3", "igiagent2");
        Assert.True(
            pair.RunUntil(() => pair.Joined && pair.Host!.NetworkHumans.Count == 1 && pair.Joiner!.NetworkHumans.Count == 1, 4000) >= 0,
            "the joiner was never welcomed, or never drew the host");
        pair.RunUntil(() => false, 30);

        Assert.Equal("igiagent2_1", Assert.Single(pair.Host!.NetworkHumans).Human.Model?.Name);
        Assert.Equal("chn3_1", Assert.Single(pair.Joiner!.NetworkHumans).Human.Model?.Name);
        Assert.NotEmpty(Assert.Single(pair.Host.NetworkHumans).Human.Model!.BoneNames);
    }

    /// <summary>The joiner's copy of the host holds the weapon the host holds, drawn in its hand and posed with it.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_joiners_copy_of_the_host_holds_the_hosts_weapon()
    {
        using Igi2CoopPair pair = Joined();
        Assert.True(pair.RunUntil(() => pair.Joiner!.NetworkHumans.Count == 1, 300) >= 0, "the joiner never drew the host");
        OpenIGI.Game.Igi2.Weapons.Igi2WeaponTypeTask? held = pair.Host!.Weapons?.Inventory.Current?.WeaponType;
        Assert.NotNull(held);

        Igi2NetworkHuman copy = Assert.Single(pair.Joiner!.NetworkHumans);
        Assert.True(
            pair.RunUntil(() => copy.Weapons?.Inventory.Current?.WeaponType.WeaponId == held.WeaponId, 120) >= 0,
            $"the copy holds {copy.Weapons?.Inventory.Current?.WeaponType.WeaponId ?? "nothing"}, the host {held.WeaponId}");
    }

    /// <summary>
    /// The joiner draws the host's gun in its copy's hands, the copy standing still the whole time: held is not enough, the
    /// clip the copy plays must hang it. A copy made unarmed and given its gun while it stood kept the unarmed clip -- the host
    /// stood empty-handed on the joiner's game while the host saw the joiner's gun.
    /// </summary>
    [RequiresTitleDataTheory("igi2")]
    [InlineData("", "")]
    [InlineData("chn3", "igiagent2")]
    public void The_joiner_draws_the_hosts_gun_in_its_copys_hands(string hostLook, string joinerLook)
    {
        using Igi2CoopPair pair = hostLook.Length == 0 ? Joined() : new(hostLook, joinerLook);
        Assert.True(
            pair.RunUntil(() => pair.Joined && pair.Joiner!.NetworkHumans.Count == 1, 4000) >= 0,
            "the joiner never drew the host");
        pair.RunUntil(() => false, 120);

        Igi2NetworkHuman copy = Assert.Single(pair.Joiner!.NetworkHumans);
        string? gun = copy.Weapons?.Inventory.Current?.WeaponType.FixedModel;
        Assert.NotNull(gun);
        IReadOnlyList<TitleWorldModel> drawn = Igi2BodyFrame.Build(copy.Human, copy.Human.BodyPose(), copy.Human.Model!.Name, gun, 0);
        Assert.Contains(drawn, part => string.Equals(part.Model, gun, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A joiner fallen while the host stands is not its own level's "player dead" (HumanPlayer_0.isDead), which shows "Mission
    /// failed": its level weighs the host's copy beside its own player, as the server's weighs both.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_joiner_down_while_the_host_stands_is_not_its_levels_player_dead()
    {
        using Igi2CoopPair pair = Joined();
        Assert.True(pair.RunUntil(() => pair.Joiner!.NetworkHumans.Count == 1, 300) >= 0, "the joiner never drew the host");
        Assert.True(pair.Joiner!.Level!.Natives.TryResolve("HumanPlayer_0.isDead", out QvmSymbol? isDead));

        Igi2NetworkHuman partner = Assert.Single(pair.Host!.NetworkHumans);
        partner.Human.Damage.Damage = partner.Human.Damage.MaxDamage + 1f;
        partner.Human.UpdateDie();
        Assert.True(pair.RunUntil(() => pair.Joiner.Human.IsDead, 300) >= 0, "the joiner's own player never fell");
        pair.RunUntil(() => false, 30);
        Assert.Equal(0, isDead.Variable!.Read().ToInt32());
    }

    /// <summary>
    /// A fallen partner leaves its pool of blood where it lies, as a guard does: the ground's marks were the player's and the
    /// guards' alone, and a partner who fell left none on any screen.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_fallen_partner_leaves_its_pool_of_blood()
    {
        using Igi2CoopPair pair = Joined();
        Igi2HumanPlayer partner = Assert.Single(pair.Host!.NetworkHumans).Human;

        // The host stands well away, or it revives the partner before its body has come to rest.
        pair.Host.Human.Position = partner.Position + new Vector3d(CoopSquad.ReviveReachUnits * 4, 0, 0);
        partner.Damage.Damage = partner.Damage.MaxDamage + 1f;
        partner.UpdateDie();
        Assert.True(
            pair.RunUntil(() => pair.Host.GroundDecals.Any(d => d.Texture.EndsWith(OpenIGI.Game.World.HumanShadow.PoolTexture, StringComparison.Ordinal)
                && Math.Abs(d.Position.X - partner.Position.X) + Math.Abs(d.Position.Y - partner.Position.Y) < 4096), 400) >= 0,
            $"the fallen partner left no pool of blood: dead {partner.IsDead}, clip done {partner.IsDeathAnimationDone}, settled {partner.IsDeathPositionStabilized}, puddle {partner.IsBloodPuddle}, mark on {partner.Shadow.IsEnabled} valid {partner.Shadow.IsPositionValid} size {partner.Shadow.Size}, state {partner.Locomotion.CurrentState}");
    }

    /// <summary>The host falls on the joiner's screen as it fell on its own: the same death clip, not one the joiner drew.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_joiners_copy_of_a_fallen_host_dies_the_hosts_death()
    {
        using Igi2CoopPair pair = Joined();
        Assert.True(pair.RunUntil(() => pair.Joiner!.NetworkHumans.Count == 1, 300) >= 0, "the joiner never drew the host");
        Igi2HumanPlayer copy = Assert.Single(pair.Joiner!.NetworkHumans).Human;
        pair.Joiner.Human.Position = pair.Host!.Human.Position + new Vector3d(CoopSquad.ReviveReachUnits * 4, 0, 0);
        pair.Host.Human.Damage.Damage = pair.Host.Human.Damage.MaxDamage + 1f;
        pair.Host.Human.UpdateDie();
        Assert.True(pair.RunUntil(() => copy.IsDead, 300) >= 0, "the joiner's copy of the host never fell");
        pair.RunUntil(() => false, 30);

        Assert.Equal(pair.Host.Human.Animation.LastState, copy.Animation.LastState);
    }

    /// <summary>
    /// A guard the joiner kills is in the joiner's kill feed, under the joiner and its weapon: I.G.I. 2 told the session of no
    /// hit, so its players had no hit marks, no mark of where a hit came from, and no guard in the feed.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_guard_the_joiner_kills_is_in_its_kill_feed()
    {
        using Igi2CoopPair pair = Joined();
        Igi2HumanPlayer partner = Assert.Single(pair.Host!.NetworkHumans).Human;
        Igi2HumanSoldier guard = pair.Host.Soldiers!.Bodies[IndexOfLiving(pair.Host.Soldiers.Bodies)];
        OpenIGI.Game.Igi2.Weapons.Igi2WeaponTypeTask gun = partner.Weapon.Inventory!.Current!.WeaponType;
        guard.WeaponHit(new OpenIGI.Game.Igi2.Weapons.Igi2WeaponHit(
            new OpenIGI.Game.Physics.WorldIntersection { Position = guard.Position }, 1_000_000f, new Vector3f(40960f, 0f, 0f), partner, gun, null));
        Assert.True(guard.IsDead || guard.Damage.IsFatal, "the round did not kill the guard");

        Assert.True(
            pair.RunUntil(() => pair.Client.KillFeed.Any(k => k.Note.KillerEntity == pair.Client.LocalEntity), 120) >= 0,
            "the joiner's feed never listed its kill");
        Assert.Contains(pair.Client.KillFeed, k => k.Note.KillerEntity == pair.Client.LocalEntity && k.Note.Weapon == gun.WeaponId);
    }

    /// <summary>
    /// The joiner's copy of the host takes no harm from the joiner's own rounds, friendly fire or not: its harm is the server's,
    /// and it once bled, cried out and flinched on the joiner's screen where the server spared it.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_joiners_copy_of_the_host_takes_no_harm_from_the_joiners_rounds()
    {
        using Igi2CoopPair pair = Joined();
        Assert.True(pair.RunUntil(() => pair.Joiner!.NetworkHumans.Count == 1, 300) >= 0, "the joiner never drew the host");
        Igi2HumanPlayer copy = Assert.Single(pair.Joiner!.NetworkHumans).Human;
        OpenIGI.Game.Igi2.Weapons.Igi2WeaponTypeTask gun = pair.Joiner.Weapons!.Inventory.Current!.WeaponType;
        OpenIGI.Game.Igi2.Weapons.Igi2WeaponHit hit = new(
            new OpenIGI.Game.Physics.WorldIntersection { Position = copy.Position }, 0.05f, new Vector3f(-40960f, 0f, 0f), pair.Joiner.Human, gun, null);

        CoopCVars.FriendlyFire.Value = true;
        try
        {
            copy.WeaponHit(hit);
        }
        finally
        {
            CoopCVars.FriendlyFire.Value = false;
        }

        Assert.Equal(0f, copy.Damage.Damage);
    }

    /// <summary>
    /// Without friendly fire (coop_friendlyfire, off unless the host turns it on, as Project I.G.I.'s co-op has it) the host's
    /// rounds do its partner no harm, a soldier's still do; with it on, the host's do too.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_teammates_round_does_no_harm_without_friendly_fire()
    {
        using Igi2CoopPair pair = Joined();
        Igi2NetworkHuman partner = Assert.Single(pair.Host!.NetworkHumans);
        OpenIGI.Game.Igi2.Weapons.Igi2WeaponTypeTask gun = pair.Host.Weapons!.Inventory.Current!.WeaponType;
        OpenIGI.Game.Igi2.Weapons.Igi2WeaponHit From(OpenIGI.Game.Igi2.Weapons.IIgi2WeaponOwner? shooter) =>
            new(new OpenIGI.Game.Physics.WorldIntersection { Position = partner.Human.Position }, 0.05f, new OpenIGI.Core.Numerics.Vector3f(-40960f, 0f, 0f), shooter, gun, null);

        float before = partner.Human.Damage.Damage;
        partner.Human.WeaponHit(From(pair.Host.Human));
        Assert.Equal(before, partner.Human.Damage.Damage);
        pair.Host.Human.WeaponHit(From(partner.Human));
        Assert.Equal(0f, pair.Host.Human.Damage.Damage);

        partner.Human.WeaponHit(From(null));
        Assert.True(partner.Human.Damage.Damage > before, "a soldier's round did the partner no harm");

        float soldierHurt = partner.Human.Damage.Damage;
        CoopCVars.FriendlyFire.Value = true;
        try
        {
            partner.Human.WeaponHit(From(pair.Host.Human));
            Assert.True(partner.Human.Damage.Damage > soldierHurt, "with friendly fire on the host's round did no harm");
        }
        finally
        {
            CoopCVars.FriendlyFire.Value = false;
        }
    }

    /// <summary>
    /// The host and its partner finish the first mission -- the lift down, both in the mine shaft -- and the host goes on to the
    /// second with the room still open and the joiner following it there.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_host_finishing_the_first_mission_goes_on_to_the_second_and_the_joiner_follows()
    {
        using Igi2CoopPair pair = Joined();
        Igi2TitlePlayer first = pair.Host!;
        Igi2HumanPlayer partner = Assert.Single(first.NetworkHumans).Human;
        pair.RunUntil(() => false, 120);
        OpenIGI.Game.Igi2.Flow.Igi2AreaActivateTask shaft = first.LevelTasks().OfType<OpenIGI.Game.Igi2.Flow.Igi2AreaActivateTask>().Single(static a => a.Id == 162);
        OpenIGI.Game.Flow.EditVariableTask lift = first.LevelTasks().OfType<OpenIGI.Game.Flow.EditVariableTask>().Single(static v => v.Id == 271);
        lift.Value = 2;
        bool completed = false;
        int frames = pair.RunUntil(
            () =>
            {
                if (ReferenceEquals(pair.Host, first) && first.Outcome != TitleMissionOutcome.Completed && !completed)
                {
                    first.Human.Position = shaft.Position;
                    partner.Position = shaft.Position;
                }

                completed |= first.Outcome == TitleMissionOutcome.Completed;
                return pair.Client.Ended is not null || (completed && pair.Client.Session?.Map == "12" && !ReferenceEquals(pair.Host, first));
            },
            6000);

        Assert.True(completed, "the first mission never completed");
        Assert.True(
            frames >= 0 && pair.Client.Ended is null,
            $"after the first mission the joiner's session ended '{pair.Client.Ended}', its map '{pair.Client.Session?.Map}', the host's level {(ReferenceEquals(pair.Host, first) ? "the first still" : "a new one")}");
        Assert.True(pair.RunUntil(() => pair.Joined && pair.Host!.NetworkHumans.Count == 1 && !pair.Client.HoldsLevel, 4000) >= 0, "the joiner never came into the second mission");
    }

    /// <summary>
    /// A dedicated server whose mission fails -- its only player down -- begins the mission again, and the player follows it
    /// there. It once opened the failed page, which nobody at a server can click, and stopped answering: no one could join it
    /// again until it was restarted by hand.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_dedicated_server_whose_mission_fails_begins_it_again_and_its_player_follows()
    {
        using Igi2CoopPair pair = new(dedicated: true, Igi2CoopPair.FirstMission, 20000);
        Assert.True(
            pair.RunUntil(() => pair.Joined && pair.Host!.NetworkHumans.Count == 1 && !pair.Client.HoldsLevel, 4000) >= 0,
            "the joiner was never welcomed");
        pair.RunUntil(() => false, 60);
        Igi2TitlePlayer first = pair.Host!;
        Igi2TitlePlayer joinerFirst = pair.Joiner!;
        byte? epoch = pair.Client.Session?.Epoch;
        Igi2HumanPlayer partner = Assert.Single(first.NetworkHumans).Human;
        partner.Damage.Damage = partner.Damage.MaxDamage + 1f;
        partner.UpdateDie();
        Assert.True(pair.RunUntil(() => first.Outcome == OpenIGI.Game.Player.TitleMissionOutcome.Failed, 3000) >= 0, "the mission never failed");
        Assert.True(
            pair.RunUntil(() => !ReferenceEquals(pair.Host, first), 1500) >= 0,
            $"the server never began the mission again: failed page {pair.HostGame.ShowsMissionFailed}, outcome {first.Outcome}, level {first.Level?.GetHashCode()}");
        Assert.False(pair.HostGame.ShowsMissionFailed, "the server opened the failed page");
        Assert.True(
            pair.RunUntil(() => pair.Client.Ended is null && pair.Joined && pair.Host!.NetworkHumans.Count == 1 && !pair.Client.HoldsLevel, 4000) >= 0,
            $"the player never came into the mission again: ended '{pair.Client.Ended ?? "no"}', joined {pair.Joined}, host's players {pair.Host?.NetworkHumans.Count}, held {pair.Client.HoldsLevel}, map {pair.Client.Session?.Map}, joiner level new {!ReferenceEquals(pair.Joiner, joinerFirst)}, joiner failed page {pair.JoinerGame.ShowsMissionFailed}, epoch {pair.Client.Session?.Epoch} (was {epoch}), behind {pair.Client.Session?.Behind}");
    }

    internal static Igi2CoopPair Joined(params string[] hostArgs) => JoinedOn(Igi2CoopPair.FirstMission, hostArgs);

    internal static Igi2CoopPair JoinedOn(int mission, params string[] hostArgs)
    {
        Igi2CoopPair pair = new(mission, hostArgs: hostArgs);
        Assert.True(
            pair.RunUntil(() => pair.Joined && pair.Host!.NetworkHumans.Count == 1 && !pair.Client.HoldsLevel, 4000) >= 0,
            $"the joiner was never welcomed: entity {pair.Client.LocalEntity?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}, joiner level {(pair.Joiner is null ? "none" : "up")}, held {pair.Client.HoldsLevel}");
        return pair;
    }
}
