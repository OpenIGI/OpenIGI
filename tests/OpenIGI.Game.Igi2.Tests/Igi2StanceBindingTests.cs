using OpenIGI.Game.Config;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Player;
using OpenIGI.Input;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Rebindable desktop controls reach the player's retail stance channels.</summary>
public sealed class Igi2StanceBindingTests
{
    /// <summary>The missing stance actions reach the player, including a tap released before its next tick.</summary>
    [Theory]
    [InlineData(KeyCode.Z, Igi2HumanChannel.LowerStance)]
    [InlineData(KeyCode.Delete, Igi2HumanChannel.LowerStance)]
    [InlineData(KeyCode.Q, Igi2HumanChannel.RaiseStance)]
    [InlineData(KeyCode.Insert, Igi2HumanChannel.RaiseStance)]
    [InlineData(KeyCode.LeftShift, Igi2HumanChannel.Crouch)]
    [InlineData(KeyCode.RightControl, Igi2HumanChannel.Crouch)]
    [InlineData(KeyCode.CapsLock, Igi2HumanChannel.ToggleWalkRun)]
    [InlineData(KeyCode.End, Igi2HumanChannel.ToggleWalkRun)]
    public void Profile_keys_reach_the_title_player(KeyCode key, Igi2HumanChannel channel)
    {
        PlayerProfile profile = PlayerProfile.CreateDefault();
        TitleInputBindings.RestoreMissing(profile, Igi2Gameplay.Instance.InputBindings);
        InputState input = new();
        input.BeginFrame();
        input.Keyboard.SetKey(key, true);
        ulong pressed = TitleInputBindings.Sample(profile, input, Igi2Gameplay.Instance.InputBindings, pressed: true);
        Assert.NotEqual(0UL, pressed);
        input.BeginFrame();
        input.Keyboard.SetKey(key, false);
        ulong held = TitleInputBindings.Sample(profile, input, Igi2Gameplay.Instance.InputBindings, pressed: false);
        Assert.Equal(0UL, held);
        Igi2TitlePlayer player = Igi2TitlePlayer.Spawn(new Igi2CollisionWorld([], null),
            Igi2HumanPlayerGeneral.Unset with { MaxDamageScale = 1f }, default, 0f, Game.Ai.AiDifficulty.Normal);
        player.Tick(new TitlePlayerInput(0f, 0f, default, default, 0.4f, false, TitleHeld: held, TitlePressed: pressed));
        Assert.Equal(1f, player.Human.Channels[channel]);
        player.Tick(new TitlePlayerInput(0f, 0f, default, default, 0.4f, false));
        Assert.Equal(0f, player.Human.Channels[channel]);
    }

    /// <summary>Saved rebindings and unbound controls survive startup; occupied defaults never acquire a second action.</summary>
    [Fact]
    public void Profile_assignments_survive_repair()
    {
        PlayerProfile profile = PlayerProfile.CreateDefault();
        profile.Bindings.Add(new("Title_igi2_LowerStance", InputBindingKeyNames.KeyboardDevice, "KEY_J"));
        profile.Bindings.Add(new("Title_igi2_ToggleStandLie", InputBindingKeyNames.KeyboardDevice, PlayerProfile.UnboundKey));
        profile.Bindings.Add(new("CustomAction", InputBindingKeyNames.KeyboardDevice, "KEY_Q"));
        TitleInputBindings.RestoreMissing(profile, Igi2Gameplay.Instance.InputBindings);
        TitleInputBindings.RestoreMissing(profile, Igi2Gameplay.Instance.InputBindings);
        Assert.Equal("KEY_J", Assert.Single(profile.Bindings, b => b.Action == "Title_igi2_LowerStance").Key);
        Assert.Equal(PlayerProfile.UnboundKey, Assert.Single(profile.Bindings, b => b.Action == "Title_igi2_RaiseStance").Key);
        Assert.Equal(PlayerProfile.UnboundKey, Assert.Single(profile.Bindings, b => b.Action == "Title_igi2_ToggleStandLie").Key);
        InputState input = new();
        input.BeginFrame();
        input.Keyboard.SetKey(KeyCode.Z, true);
        Assert.Equal(0UL, TitleInputBindings.Sample(profile, input, Igi2Gameplay.Instance.InputBindings, pressed: true));
        input.Keyboard.SetKey(KeyCode.J, true);
        Assert.Equal((ulong)Igi2PlayerAction.LowerStance,
            TitleInputBindings.Sample(profile, input, Igi2Gameplay.Instance.InputBindings, pressed: true));
    }

    /// <summary>Retail Z/Q lower and raise stance, movement crawls, and held Shift crouch releases.</summary>
    [Fact]
    public void Keyboard_stances_crawl_and_stand_again()
    {
        PlayerProfile profile = PlayerProfile.CreateDefault();
        TitleInputBindings.RestoreMissing(profile, Igi2Gameplay.Instance.InputBindings);
        InputState input = new();
        Igi2HumanPlayer human = Igi2PlayerFakes.OnFloor().Player;
        Igi2PlayerInputReader reader = new();
        float[] frame = new float[Igi2HumanChannels.Count];
        Step(KeyCode.Z);
        Assert.Equal(Igi2HumanPose.Crouching, human.Pose);
        Assert.False(human.CrouchIsHeld);
        Step(KeyCode.Z);
        Assert.Equal(Igi2HumanPose.Crouching, human.Pose);
        Step(KeyCode.None);
        Step(KeyCode.Z);
        Assert.Equal(Igi2HumanPose.LyingFlat, human.Pose);
        Step(KeyCode.W);
        Assert.Equal(Igi2PlayerState.FlatCrawl, human.Locomotion.CurrentState);
        Step(KeyCode.None);
        Step(KeyCode.Q);
        Assert.Equal(Igi2HumanPose.Crouching, human.Pose);
        Step(KeyCode.None);
        Step(KeyCode.Q);
        Assert.Equal(Igi2HumanPose.Standing, human.Pose);
        Step(KeyCode.LeftShift);
        Assert.Equal(Igi2HumanPose.Crouching, human.Pose);
        Assert.True(human.CrouchIsHeld);
        Step(KeyCode.None);
        Assert.Equal(Igi2HumanPose.Standing, human.Pose);

        void Step(KeyCode key)
        {
            input.BeginFrame();
            foreach (KeyCode candidate in new[] { KeyCode.Q, KeyCode.Z, KeyCode.W, KeyCode.LeftShift })
            {
                input.Keyboard.SetKey(candidate, candidate == key);
            }
            Igi2PlayerAction held = (Igi2PlayerAction)TitleInputBindings.Sample(profile, input, Igi2Gameplay.Instance.InputBindings, pressed: false);
            if (PlayerProfileInputBindings.IsDown(profile, input, "MoveUp")) { held |= Igi2PlayerAction.MoveForwards; }
            reader.Read(new(0f, 0f, held, 0), frame);
            human.Channels.Accept(frame);
            human.Signal |= Igi2PlayerSignal.Grounded;
            human.Locomotion.Tick(human);
        }
    }

    /// <summary>Title controls are persisted separately, and resetting or changing them preserves IGI1's assignments.</summary>
    [Fact]
    public void Retail_stance_controls_are_scoped_to_igi2()
    {
        PlayerProfile profile = PlayerProfile.CreateDefault();
        TitleInputBindings.RestoreMissing(profile, Igi2Gameplay.Instance.InputBindings);
        Assert.Equal(PlayerAction.Crouch | PlayerAction.ToggleWalkRun, TitleInputBindings.ReplacedActions(Igi2Gameplay.Instance.InputBindings));
        Assert.Null(TitleInputBindings.Label("Crouch", Igi2Gameplay.Instance.InputBindings));
        Assert.Equal("Crouch", TitleInputBindings.Label("Title_igi2_Crouch", Igi2Gameplay.Instance.InputBindings));
        Assert.Null(TitleInputBindings.Label("Title_igi2_Crouch", []));
        Assert.Equal("Crouch", TitleInputBindings.Label("Crouch", []));
        InputBinding original = profile.Bindings.Single(b => b.Action == "Crouch");
        int titleCrouch = profile.Bindings.FindIndex(b => b.Action == "Title_igi2_Crouch");
        profile.Bindings[titleCrouch] = profile.Bindings[titleCrouch] with { Key = "KEY_J" };
        TitleInputBindings.Reset(profile, Igi2Gameplay.Instance.InputBindings);
        Assert.Equal("KEY_LEFT_SHIFT", profile.Bindings[titleCrouch].Key);
        Assert.Equal(original, profile.Bindings.Single(b => b.Action == "Crouch"));
        Assert.Equal(PlayerProfile.UnboundKey, profile.Bindings.Single(b => b.Action == "Title_igi2_ToggleStandLie").Key);
        GameConfig config = GameConfig.CreateDefault();
        config.Active!.Bindings.AddRange(profile.Bindings.Where(b => b.Action.StartsWith("Title_", StringComparison.Ordinal)));
        GameConfig loaded = ConfigScript.Load(ConfigScriptWriter.Write(config));
        Assert.Equal(config.Active.Bindings, loaded.Active!.Bindings);
    }
}
