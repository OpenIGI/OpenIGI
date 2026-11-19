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
    [InlineData(KeyCode.Delete, Igi2HumanChannel.LowerStance)]
    [InlineData(KeyCode.Insert, Igi2HumanChannel.RaiseStance)]
    [InlineData(KeyCode.Z, Igi2HumanChannel.ToggleStandLie)]
    [InlineData(KeyCode.LeftControl, Igi2HumanChannel.Crouch)]
    [InlineData(KeyCode.RightControl, Igi2HumanChannel.Crouch)]
    [InlineData(KeyCode.LeftShift, Igi2HumanChannel.ToggleWalkRun)]
    [InlineData(KeyCode.CapsLock, Igi2HumanChannel.ToggleWalkRun)]
    [InlineData(KeyCode.N, Igi2HumanChannel.Thermal)]
    [InlineData(KeyCode.PageUp, Igi2HumanChannel.Thermal)]
    [InlineData(KeyCode.I, Igi2HumanChannel.Inventory)]
    [InlineData(KeyCode.NumpadDivide, Igi2HumanChannel.Inventory)]
    [InlineData(KeyCode.Numpad1, Igi2HumanChannel.WeaponCategory1 + 2)]
    [InlineData(KeyCode.Numpad3, Igi2HumanChannel.WeaponCategory1)]
    [InlineData(KeyCode.Numpad9, Igi2HumanChannel.WeaponCategory9)]
    public void Profile_keys_reach_the_title_player(KeyCode key, Igi2HumanChannel channel)
    {
        PlayerProfile profile = PlayerProfile.CreateDefault();
        TitleInputBindings.RestoreMissing(profile, new Igi2Gameplay().InputBindings);
        InputState input = new();
        input.BeginFrame();
        input.Keyboard.SetKey(key, true);
        ulong pressed = TitleInputBindings.Sample(profile, input, new Igi2Gameplay().InputBindings, pressed: true);
        Assert.NotEqual(0UL, pressed);
        input.BeginFrame();
        input.Keyboard.SetKey(key, false);
        ulong held = TitleInputBindings.Sample(profile, input, new Igi2Gameplay().InputBindings, pressed: false);
        Assert.Equal(0UL, held);
        Igi2TitlePlayer player = Igi2TitlePlayer.Spawn(new Igi2CollisionWorld([], null),
            Igi2HumanPlayerGeneral.Unset with { MaxDamageScale = 1f }, default, 0f, Game.Ai.AiDifficulty.Normal);
        player.Tick(new TitlePlayerInput(0f, 0f, default, default, 0.4f, false, TitleHeld: held, TitlePressed: pressed));
        Assert.Equal(1f, player.Human.Channels[channel]);
        player.Tick(new TitlePlayerInput(0f, 0f, default, default, 0.4f, false));
        Assert.Equal(0f, player.Human.Channels[channel]);
    }

    /// <summary>The shared number keys select the categories the original's config.qvm puts on those keys: 1 and 3 swapped.</summary>
    [Theory]
    [InlineData(PlayerAction.WeaponCategory1, Igi2HumanChannel.WeaponCategory1 + 2)]
    [InlineData(PlayerAction.WeaponCategory2, Igi2HumanChannel.WeaponCategory1 + 1)]
    [InlineData(PlayerAction.WeaponCategory3, Igi2HumanChannel.WeaponCategory1)]
    [InlineData(PlayerAction.WeaponCategory4, Igi2HumanChannel.WeaponCategory1 + 3)]
    [InlineData(PlayerAction.WeaponCategory7, Igi2HumanChannel.WeaponCategory1 + 6)]
    [InlineData(PlayerAction.WeaponCategory9, Igi2HumanChannel.WeaponCategory9)]
    public void Number_keys_reach_the_retail_weapon_categories(PlayerAction key, Igi2HumanChannel channel)
    {
        Igi2TitlePlayer player = Igi2TitlePlayer.Spawn(new Igi2CollisionWorld([], null),
            Igi2HumanPlayerGeneral.Unset with { MaxDamageScale = 1f }, default, 0f, Game.Ai.AiDifficulty.Normal);
        player.Tick(new TitlePlayerInput(0f, 0f, key, key, 0.4f, false));
        Assert.Equal(1f, player.Human.Channels[channel]);
        player.Tick(new TitlePlayerInput(0f, 0f, key, default, 0.4f, false));
        Assert.Equal(0f, player.Human.Channels[channel]);
    }

    /// <summary>Saved rebindings and unbound controls survive startup; occupied defaults never acquire a second action.</summary>
    [Fact]
    public void Profile_assignments_survive_repair()
    {
        PlayerProfile profile = PlayerProfile.CreateDefault();
        profile.Bindings.Add(new("Title_igi2_LowerStance", InputBindingKeyNames.KeyboardDevice, "KEY_J"));
        profile.Bindings.Add(new("Title_igi2_ToggleStandLie", InputBindingKeyNames.KeyboardDevice, PlayerProfile.UnboundKey));
        // Insert, the raise's own default, given to an action I.G.I. 2 reads: the raise comes back without a key.
        int binoculars = profile.Bindings.FindIndex(binding => binding.Action == "Binoculars");
        profile.Bindings[binoculars] = profile.Bindings[binoculars] with { Key = "KEY_INSERT" };
        TitleInputBindings.RestoreMissing(profile, new Igi2Gameplay().InputBindings);
        TitleInputBindings.RestoreMissing(profile, new Igi2Gameplay().InputBindings);
        Assert.Equal("KEY_J", Assert.Single(profile.Bindings, b => b.Action == "Title_igi2_LowerStance").Key);
        Assert.Equal(PlayerProfile.UnboundKey, Assert.Single(profile.Bindings, b => b.Action == "Title_igi2_RaiseStance").Key);
        Assert.Equal(PlayerProfile.UnboundKey, Assert.Single(profile.Bindings, b => b.Action == "Title_igi2_ToggleStandLie").Key);
        InputState input = new();
        input.BeginFrame();
        input.Keyboard.SetKey(KeyCode.Z, true);
        Assert.Equal(0UL, TitleInputBindings.Sample(profile, input, new Igi2Gameplay().InputBindings, pressed: true));
        input.Keyboard.SetKey(KeyCode.J, true);
        Assert.Equal((ulong)Igi2PlayerAction.LowerStance,
            TitleInputBindings.Sample(profile, input, new Igi2Gameplay().InputBindings, pressed: true));
    }

    /// <summary>Delete/Insert lower and raise stance, movement crawls, and held Ctrl crouch releases.</summary>
    [Fact]
    public void Keyboard_stances_crawl_and_stand_again()
    {
        PlayerProfile profile = PlayerProfile.CreateDefault();
        TitleInputBindings.RestoreMissing(profile, new Igi2Gameplay().InputBindings);
        InputState input = new();
        Igi2HumanPlayer human = Igi2PlayerFakes.OnFloor().Player;
        Igi2PlayerInputReader reader = new();
        float[] frame = new float[Igi2HumanChannels.Count];
        Step(KeyCode.Delete);
        Assert.Equal(Igi2HumanPose.Crouching, human.Pose);
        Assert.False(human.CrouchIsHeld);
        Step(KeyCode.Delete);
        Assert.Equal(Igi2HumanPose.Crouching, human.Pose);
        Step(KeyCode.None);
        Step(KeyCode.Delete);
        Assert.Equal(Igi2HumanPose.LyingFlat, human.Pose);
        Step(KeyCode.W);
        Assert.Equal(Igi2PlayerState.FlatCrawl, human.Locomotion.CurrentState);
        Step(KeyCode.None);
        Step(KeyCode.Insert);
        Assert.Equal(Igi2HumanPose.Crouching, human.Pose);
        Step(KeyCode.None);
        Step(KeyCode.Insert);
        Assert.Equal(Igi2HumanPose.Standing, human.Pose);
        Step(KeyCode.LeftControl);
        Assert.Equal(Igi2HumanPose.Crouching, human.Pose);
        Assert.True(human.CrouchIsHeld);
        Step(KeyCode.None);
        Assert.Equal(Igi2HumanPose.Standing, human.Pose);

        void Step(KeyCode key)
        {
            input.BeginFrame();
            foreach (KeyCode candidate in new[] { KeyCode.Insert, KeyCode.Delete, KeyCode.W, KeyCode.LeftControl })
            {
                input.Keyboard.SetKey(candidate, candidate == key);
            }
            Igi2PlayerAction held = (Igi2PlayerAction)TitleInputBindings.Sample(profile, input, new Igi2Gameplay().InputBindings, pressed: false);
            if (PlayerProfileInputBindings.IsDown(profile, input, "MoveUp")) { held |= Igi2PlayerAction.MoveForwards; }
            reader.Read(new(0f, 0f, held, 0), frame);
            human.Channels.Accept(frame);
            human.Signal |= Igi2PlayerSignal.Grounded;
            human.Locomotion.Tick(human);
        }
    }

    /// <summary>A touch screen offers I.G.I. 2's goggles, its prone stance and its own crouch, each as the bit the title reads.</summary>
    [Fact]
    public void The_title_offers_its_own_touch_buttons()
    {
        IReadOnlyList<TitleInputBinding> bindings = new Igi2Gameplay().InputBindings;
        ulong Of(TitleTouchControl control) => bindings.Where(b => b.Touch == control).Aggregate(0UL, static (m, b) => m | b.Mask);

        Assert.Equal((ulong)Igi2PlayerAction.Thermal, Of(TitleTouchControl.Thermal));
        Assert.Equal((ulong)Igi2PlayerAction.ToggleStandLie, Of(TitleTouchControl.Prone));
        Assert.Equal((ulong)Igi2PlayerAction.Crouch, Of(TitleTouchControl.Crouch));
        Assert.Equal(1, bindings.Count(b => b.Touch == TitleTouchControl.Thermal));
    }

    /// <summary>Title controls are persisted separately, and resetting or changing them preserves IGI1's assignments.</summary>
    [Fact]
    public void Retail_stance_controls_are_scoped_to_igi2()
    {
        PlayerProfile profile = PlayerProfile.CreateDefault();
        TitleInputBindings.RestoreMissing(profile, new Igi2Gameplay().InputBindings);
        Assert.Equal(PlayerAction.Crouch | PlayerAction.ToggleWalkRun, TitleInputBindings.ReplacedActions(new Igi2Gameplay().InputBindings));
        Assert.Null(TitleInputBindings.Label("Crouch", new Igi2Gameplay().InputBindings));
        Assert.Equal("Crouch", TitleInputBindings.Label("Title_igi2_Crouch", new Igi2Gameplay().InputBindings));
        Assert.Null(TitleInputBindings.Label("Title_igi2_Crouch", []));
        Assert.Equal("Crouch", TitleInputBindings.Label("Crouch", []));
        InputBinding original = profile.Bindings.Single(b => b.Action == "Crouch");
        int titleCrouch = profile.Bindings.FindIndex(b => b.Action == "Title_igi2_Crouch");
        profile.Bindings[titleCrouch] = profile.Bindings[titleCrouch] with { Key = "KEY_J" };
        TitleInputBindings.Reset(profile, new Igi2Gameplay().InputBindings);
        Assert.Equal("KEY_LEFT_CTRL", profile.Bindings[titleCrouch].Key);
        Assert.Equal(original, profile.Bindings.Single(b => b.Action == "Crouch"));
        Assert.Equal("KEY_Z", profile.Bindings.Single(b => b.Action == "Title_igi2_ToggleStandLie").Key);
        GameConfig config = GameConfig.CreateDefault();
        config.Active!.Bindings.AddRange(profile.Bindings.Where(b => b.Action.StartsWith("Title_", StringComparison.Ordinal)));
        GameConfig loaded = ConfigScript.Load(ConfigScriptWriter.Write(config));
        Assert.Equal(config.Active.Bindings, loaded.Active!.Bindings);
    }
}
