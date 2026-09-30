using OpenIGI.Game.Config;
using OpenIGI.Game.Menus;
using OpenIGI.Input;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests.Config;

/// <summary>
/// The configuration subsystem: the save file, the fifty-one <c>Config_*</c> functions, and the
/// performance-level table.
/// </summary>
public sealed class ConfigTests
{
    [RequiresGameDataFact]
    public void TheRetailSaveFileLoads()
    {
        GameConfig config = LoadRetail();

        // The retail game rewrites this file; names and bindings belong to the player.
        Assert.NotEmpty(config.Profiles);
        Assert.All(config.Profiles, profile => Assert.False(string.IsNullOrWhiteSpace(profile.Name)));
        Assert.InRange(config.ActiveIndex, 0, config.Profiles.Count - 1);
    }

    [Fact]
    public void AProfileStillOnTheOriginalControlsMovesToWasdWithTheMouseTheRightWayUp()
    {
        // What a new player got: the retail file's arrow keys and, in the one an installation ships, the
        // mouse inverted.
        GameConfig config = RetailShaped(invert: true);

        Assert.True(config.MigrateControls());

        PlayerProfile profile = config.Profiles[0];
        Assert.Equal("KEY_W", Key(profile, "MoveUp"));
        Assert.Equal("KEY_A", Key(profile, "MoveLeft"));
        Assert.Equal("KEY_SPACE", Key(profile, "Jump"));
        Assert.False(profile.InvertMouse);
        Assert.Equal(GameConfig.CurrentControlsVersion, config.ControlsVersion);
    }

    [Fact]
    public void AProfileItsPlayerReboundKeepsItsControlsAndItsInversion()
    {
        GameConfig config = RetailShaped(invert: true);
        PlayerProfile profile = config.Profiles[0];
        int jump = profile.Bindings.FindIndex(b => b.Action == "Jump");
        profile.Bindings[jump] = new("Jump", "INPUTPORT_DEVICE_KEYBOARD", "KEY_SPACE");

        Assert.False(config.MigrateControls());

        Assert.Equal("KEY_UP", Key(profile, "MoveUp"));
        Assert.True(profile.InvertMouse);
    }

    [Fact]
    public void TheControlsMoveOnceAndAPlayerWhoChoosesTheArrowKeysAgainKeepsThem()
    {
        GameConfig config = RetailShaped(invert: false);
        config.MigrateControls();

        PlayerProfile profile = config.Profiles[0];
        profile.Bindings.Clear();
        profile.Bindings.AddRange(PlayerProfile.RetailDefaultBindings);

        GameConfig reloaded = ConfigScript.Load(ConfigScriptWriter.Write(config));

        Assert.Equal(GameConfig.CurrentControlsVersion, reloaded.ControlsVersion);
        Assert.False(reloaded.MigrateControls());
        Assert.Equal("KEY_UP", Key(reloaded.Profiles[0], "MoveUp"));
    }

    [Fact]
    public void AFreshConfigurationIsAlreadyOnTheModernControls()
    {
        GameConfig config = GameConfig.CreateDefault();

        Assert.False(config.MigrateControls());
        Assert.Equal("KEY_W", Key(config.Profiles[0], "MoveUp"));
        Assert.False(config.Profiles[0].InvertMouse);
    }

    [RequiresGameDataFact]
    public void TheRetailSaveFilesUntouchedProfilesComeUpOnWasd()
    {
        GameConfig config = LoadRetail();

        Assert.Equal(0, config.ControlsVersion);
        config.MigrateControls();

        Assert.Contains(config.Profiles, profile => Key(profile, "MoveUp") == "KEY_W" && !profile.InvertMouse);
    }

    private static GameConfig RetailShaped(bool invert)
    {
        GameConfig config = ConfigScript.Load(ConfigScriptWriter.Write(new GameConfig()));
        config.Add(new PlayerProfile { Name = "Joe", InvertMouse = invert });
        config.Profiles[0].Bindings.AddRange(PlayerProfile.RetailDefaultBindings);
        config.ControlsVersion = 0;
        return config;
    }

    private static string Key(PlayerProfile profile, string action) =>
        profile.Bindings.First(b => string.Equals(b.Action, action, StringComparison.OrdinalIgnoreCase)).Key;

    [Fact]
    public void EveryProfileCarriesItsOwnSettingsAndBindings()
    {
        GameConfig input = GameConfig.CreateDefault();
        input.Active!.Name = "First";
        input.Active.Bindings[0] = new("MoveLeft", "INPUTPORT_DEVICE_KEYBOARD", "KEY_LEFT");
        input.Active.MouseSensitivity = 0.25f;
        Assert.True(input.CreateProfile("Second", 2));
        input.Active!.Bindings[0] = new("MoveLeft", "INPUTPORT_DEVICE_KEYBOARD", "KEY_A");
        input.Active.MouseSensitivity = 0.75f;
        input.Active.DeviceGuid = "custom-device";

        GameConfig config = ConfigScript.Load(ConfigScriptWriter.Write(input));

        Assert.Equal(2, config.Profiles.Count);
        Assert.Equal(1, config.ActiveIndex);
        Assert.Equal("First", config.Profiles[0].Name);
        Assert.Equal("Second", config.Profiles[1].Name);
        Assert.Equal("KEY_LEFT", config.Profiles[0].Bindings[0].Key);
        Assert.Equal("KEY_A", config.Profiles[1].Bindings[0].Key);
        Assert.Equal(0.25f, config.Profiles[0].MouseSensitivity);
        Assert.Equal(0.75f, config.Profiles[1].MouseSensitivity);
        Assert.Equal(PlayerProfile.DefaultDeviceGuid, config.Profiles[0].DeviceGuid);
        Assert.Equal("custom-device", config.Profiles[1].DeviceGuid);
        Assert.All(config.Profiles, profile => Assert.Equal(PlayerProfile.BindingCount, profile.Bindings.Count));
    }

    [Fact]
    public void ARemappedBindingSurvivesASaveReloadCycleAndDrivesGameplayResolution()
    {
        // Issue #230: the exact player scenario — remap in Controls, save, relaunch, and the new
        // key must drive the action while the old one no longer does.
        GameConfig config = GameConfig.CreateDefault();
        PlayerProfile profile = config.Active!;
        int moveIndex = profile.Bindings.FindIndex(
            binding => string.Equals(binding.Action, "MoveLeft", StringComparison.OrdinalIgnoreCase));
        Assert.InRange(moveIndex, 0, profile.Bindings.Count - 1);
        Assert.True(
            InputBindingKeyNames.TryParseKeyboardKey(profile.Bindings[moveIndex].Key, out KeyCode previous));
        Assert.NotEqual(KeyCode.F, previous); // F is unbound in the defaults, so no swap interferes.

        Assert.Equal(-1, profile.RebindKeyboard(moveIndex, KeyCode.F));

        GameConfig reloaded = ConfigScript.Load(ConfigScriptWriter.Write(config));
        PlayerProfile restored = reloaded.Profiles[config.ActiveIndex];

        Assert.Equal(InputBindingKeyNames.ToRetailKeyboardKey(KeyCode.F), restored.Bindings[moveIndex].Key);

        var fresh = new InputState();
        fresh.BeginFrame();
        fresh.Keyboard.SetKey(KeyCode.F, down: true);
        Assert.True(PlayerProfileInputBindings.IsDown(restored, fresh, "MoveLeft"));

        var stale = new InputState();
        stale.BeginFrame();
        stale.Keyboard.SetKey(previous, down: true);
        Assert.False(PlayerProfileInputBindings.IsDown(restored, stale, "MoveLeft"));
    }

    [Fact]
    public void AWheelBindingDrivesGameplayResolutionSurvivesReloadAndSwapsConflicts()
    {
        // Issue #232: wheel up/down are bindable controls with momentary (edge-only) semantics.
        GameConfig config = GameConfig.CreateDefault();
        PlayerProfile profile = config.Active!;
        int nextIndex = profile.Bindings.FindIndex(
            binding => string.Equals(binding.Action, "NextWeapon", StringComparison.OrdinalIgnoreCase));
        int prevIndex = profile.Bindings.FindIndex(
            binding => string.Equals(binding.Action, "PrevWeapon", StringComparison.OrdinalIgnoreCase));
        Assert.InRange(nextIndex, 0, profile.Bindings.Count - 1);
        Assert.InRange(prevIndex, 0, profile.Bindings.Count - 1);

        Assert.Equal(-1, profile.RebindMouseWheel(nextIndex, 1));
        Assert.Equal("MOUSE_WHEEL_UP", profile.Bindings[nextIndex].Key);
        Assert.Equal("Mouse Wheel Up", InputBindingKeyNames.DisplayName(profile.Bindings[nextIndex]));

        // Same-direction conflict swaps, like buttons: NextWeapon inherits PrevWeapon's old key.
        Assert.Equal(nextIndex, profile.RebindMouseWheel(prevIndex, 1));
        Assert.Equal("MOUSE_WHEEL_UP", profile.Bindings[prevIndex].Key);
        Assert.Equal("KEY_O", profile.Bindings[nextIndex].Key);

        GameConfig reloaded = ConfigScript.Load(ConfigScriptWriter.Write(config));
        PlayerProfile restored = reloaded.Profiles[config.ActiveIndex];
        Assert.Equal("MOUSE_WHEEL_UP", restored.Bindings[prevIndex].Key);

        var up = new InputState();
        up.BeginFrame();
        up.Mouse.AddWheel(1f);
        Assert.True(PlayerProfileInputBindings.IsDown(restored, up, "PrevWeapon"));
        Assert.True(PlayerProfileInputBindings.WasPressed(restored, up, "PrevWeapon"));
        Assert.False(PlayerProfileInputBindings.IsDown(restored, up, "NextWeapon"));

        var down = new InputState();
        down.BeginFrame();
        down.Mouse.AddWheel(-1f);
        Assert.False(PlayerProfileInputBindings.IsDown(restored, down, "PrevWeapon"));

        var still = new InputState();
        still.BeginFrame();
        Assert.False(PlayerProfileInputBindings.IsDown(restored, still, "PrevWeapon"));

        // Feather-touch noise below the floor fires nothing in either direction.
        var feather = new InputState();
        feather.BeginFrame();
        feather.Mouse.AddWheel(0.05f);
        Assert.False(PlayerProfileInputBindings.IsDown(restored, feather, "PrevWeapon"));
        feather.Mouse.AddWheel(-0.1f);
        Assert.False(PlayerProfileInputBindings.IsDown(restored, feather, "PrevWeapon"));
    }

    [RequiresGameDataFact]
    public void WhatIsWrittenReadsBackIdentically()
    {
        GameConfig original = LoadRetail();
        GameConfig round = ConfigScript.Load(ConfigScriptWriter.Write(original));

        Assert.Equal(original.Profiles.Count, round.Profiles.Count);
        Assert.Equal(original.ActiveIndex, round.ActiveIndex);
        Assert.Equal(original.ContentControlPassword, round.ContentControlPassword);

        for (int i = 0; i < original.Profiles.Count; i++)
        {
            PlayerProfile a = original.Profiles[i];
            PlayerProfile b = round.Profiles[i];

            Assert.Equal(a.Name, b.Name);
            Assert.Equal(a.Difficulty, b.Difficulty);
            Assert.Equal(a.ActiveMission, b.ActiveMission);
            Assert.Equal(a.MouseSensitivity, b.MouseSensitivity);
            Assert.Equal(a.MusicVolume, b.MusicVolume);
            Assert.Equal(a.DeviceGuid, b.DeviceGuid);
            Assert.Equal(a.DisplayMode, b.DisplayMode);
            Assert.Equal(a.Bindings, b.Bindings);
        }
    }

    [Fact]
    public void AWrittenFileIsAValidProgram()
    {
        GameConfig config = GameConfig.CreateDefault();
        config.Active!.Bindings.Add(new InputBinding("MoveLeft", "INPUTPORT_DEVICE_KEYBOARD", "KEY_LEFT"));

        byte[] bytes = ConfigScriptWriter.Write(config);

        // The reader enforces the header's chained section identities, so it round-tripping at all is
        // the check that the layout is right.
        Assert.True(OpenIGI.Scripting.Qvm.QvmProgram.IsQvm(bytes));
        GameConfig reloaded = ConfigScript.Load(bytes);

        Assert.Equal("Joe", reloaded.Profiles[0].Name);
        Assert.Contains(
            new InputBinding("MoveLeft", "INPUTPORT_DEVICE_KEYBOARD", "KEY_LEFT"),
            reloaded.Profiles[0].Bindings);
    }

    [Fact]
    public void AWrittenFileEndsWithTheTerminatingBreak()
    {
        GameConfig config = GameConfig.CreateDefault();

        byte[] bytes = ConfigScriptWriter.Write(config);
        QvmProgram program = QvmProgram.Read(bytes);

        // BRK is opcode 0 and the only thing that stops the interpreter: the original reads code[pc]
        // straight into its dispatch table with no bounds check (0x4BCFD0), so a script that just runs
        // out of instructions carries on into whatever follows the code section. A retail config.qvm
        // ends POP, BRK; ours used to stop at the POP.
        Assert.Equal((byte)QvmOpcode.Brk, program.Code.Span[^1]);
    }

    [Fact]
    public void AWrittenFileRunsToCompletionWithoutFaulting()
    {
        GameConfig config = GameConfig.CreateDefault();
        config.Active!.Bindings.Add(new InputBinding("MoveLeft", "INPUTPORT_DEVICE_KEYBOARD", "KEY_LEFT"));

        byte[] bytes = ConfigScriptWriter.Write(config);
        QvmProgram program = QvmProgram.Read(bytes);

        // Interpreted directly rather than through ConfigScript.Load, which reports a fault and carries
        // on -- by design, since a corrupt file should cost the settings rather than the session. That
        // tolerance is exactly what let a script with no terminator look fine for so long: everything
        // had already run by the time it fell off the end.
        NativeRegistry natives = new();
        foreach (string name in ConfigScript.NativeNames)
        {
            natives.RegisterFunction(name, _ => QvmValue.FromInt32(0));
        }

        foreach (string? symbol in program.SymbolNames)
        {
            if (symbol is not null && !natives.Contains(symbol))
            {
                natives.RegisterConstant(symbol, 0);
            }
        }

        QvmInterpreter interpreter = new(program, natives);

        Assert.Equal(QvmError.None, interpreter.Run());
        Assert.Equal(QvmError.None, interpreter.Error);
    }

    [Fact]
    public void ThePerformanceLevelsAreMatchedByNearestBitPatternNotByEquality()
    {
        // Level 2 is 13. Flipping one bit still reports level 2, which is what stops the slider
        // jumping when a switch is toggled.
        Assert.Equal(2, GraphicsPerformance.LevelForFlags(13));
        Assert.Equal(2, GraphicsPerformance.LevelForFlags(13 ^ 1));
        Assert.Equal(13, GraphicsPerformance.FlagsForLevel(2));
        Assert.Equal(7, GraphicsPerformance.LevelCount);
    }

    [Fact]
    public void ANewProfileInheritsTheActiveOneRatherThanTheDefaults()
    {
        GameConfig config = GameConfig.CreateDefault();
        config.Active!.MusicVolume = 0.25f;
        config.Active.Bindings.Add(new InputBinding("Fire", "INPUTPORT_DEVICE_MOUSE", "MOUSE_BUTTON_1"));

        Assert.True(config.CreateProfile("Alex", 3));

        Assert.Equal(2, config.Profiles.Count);
        Assert.Equal(1, config.ActiveIndex);
        Assert.Equal("Alex", config.Active!.Name);
        Assert.Equal(3, config.Active.Difficulty);
        Assert.Equal(0.25f, config.Active.MusicVolume);
        Assert.Equal(PlayerProfile.BindingCount + 1, config.Active.Bindings.Count);
        Assert.Contains(new InputBinding("Fire", "INPUTPORT_DEVICE_MOUSE", "MOUSE_BUTTON_1"), config.Active.Bindings);
    }

    [Fact]
    public void ADefaultProfileHasTheStandardBindingsAndAnEmptyConfigurationUsesThemToo()
    {
        GameConfig defaults = GameConfig.CreateDefault();
        PlayerProfile joe = defaults.Active!;

        Assert.Equal(PlayerProfile.BindingCount, joe.Bindings.Count);
        Assert.Equal(new InputBinding("MoveLeft", "INPUTPORT_DEVICE_KEYBOARD", "KEY_A"), joe.Bindings[0]);
        Assert.Contains(new InputBinding("MoveUp", "INPUTPORT_DEVICE_KEYBOARD", "KEY_W"), joe.Bindings);
        Assert.Contains(new InputBinding("MoveRight", "INPUTPORT_DEVICE_KEYBOARD", "KEY_D"), joe.Bindings);
        Assert.Contains(new InputBinding("MoveDown", "INPUTPORT_DEVICE_KEYBOARD", "KEY_S"), joe.Bindings);
        Assert.Contains(new InputBinding("Jump", "INPUTPORT_DEVICE_KEYBOARD", "KEY_SPACE"), joe.Bindings);
        Assert.Contains(new InputBinding("Activate", "INPUTPORT_DEVICE_KEYBOARD", "KEY_E"), joe.Bindings);
        Assert.Contains(new InputBinding("MapComputer", "INPUTPORT_DEVICE_KEYBOARD", "KEY_C"), joe.Bindings);
        Assert.Contains(new InputBinding("Binoculars", "INPUTPORT_DEVICE_KEYBOARD", "KEY_B"), joe.Bindings);
        Assert.Contains(new InputBinding("ToggleWalkRun", "INPUTPORT_DEVICE_KEYBOARD", "KEY_LEFT_SHIFT"), joe.Bindings);
        Assert.Contains(new InputBinding("Fire", "INPUTPORT_DEVICE_MOUSE", "MOUSE_BUTTON_1"), joe.Bindings);
        Assert.Contains(new InputBinding("WeaponCategory9", "INPUTPORT_DEVICE_KEYBOARD", "KEY_9"), joe.Bindings);
        Assert.Equal(
            PlayerProfile.BindingCount,
            joe.Bindings.Select(binding => (binding.Device, binding.Key)).Distinct().Count());

        GameConfig empty = new();
        Assert.True(empty.CreateProfile("Alex", 1));
        Assert.Equal(PlayerProfile.BindingCount, empty.Active!.Bindings.Count);
    }

    [Fact]
    public void Loading_a_legacy_profile_with_no_bindings_repairs_the_standard_defaults()
    {
        GameConfig legacy = new();
        legacy.Add(new PlayerProfile { Name = "Joe" });

        GameConfig loaded = ConfigScript.Load(ConfigScriptWriter.Write(legacy));

        Assert.Equal(PlayerProfile.BindingCount, loaded.Active!.Bindings.Count);
        Assert.Equal(new InputBinding("MoveLeft", "INPUTPORT_DEVICE_KEYBOARD", "KEY_A"), loaded.Active.Bindings[0]);
    }

    [Fact]
    public void Loading_an_incomplete_profile_restores_only_its_missing_actions()
    {
        GameConfig partial = new();
        PlayerProfile joe = new() { Name = "Joe" };
        joe.Bindings.Add(new InputBinding("MoveLeft", InputBindingKeyNames.KeyboardDevice, "KEY_F"));
        partial.Add(joe);

        GameConfig loaded = ConfigScript.Load(ConfigScriptWriter.Write(partial));

        Assert.Equal(PlayerProfile.BindingCount, loaded.Active!.Bindings.Count);
        Assert.Equal("KEY_F", loaded.Active.Bindings.Single(binding => binding.Action == "MoveLeft").Key);
        Assert.Contains(loaded.Active.Bindings, binding => binding.Action == "Binoculars");

        // The flashlight's own default is F, which this player gave to MoveLeft: it comes back without a key
        // rather than making one key drive two actions.
        InputBinding flashlight = loaded.Active.Bindings.Single(binding => binding.Action == "Flashlight");
        Assert.Equal(PlayerProfile.UnboundKey, flashlight.Key);
        Assert.Equal("Unbound", InputBindingKeyNames.DisplayName(flashlight));
    }

    [Fact]
    public void A_profile_saved_before_the_flashlight_gains_it_on_F()
    {
        GameConfig partial = new();
        PlayerProfile joe = new() { Name = "Joe" };
        joe.Bindings.AddRange(PlayerProfile.DefaultBindings.Where(binding => binding.Action != "Flashlight"));
        partial.Add(joe);

        GameConfig loaded = ConfigScript.Load(ConfigScriptWriter.Write(partial));

        Assert.Equal(PlayerProfile.BindingCount, loaded.Active!.Bindings.Count);
        Assert.Equal("KEY_F", loaded.Active.Bindings.Single(binding => binding.Action == "Flashlight").Key);
    }

    [Fact]
    public void An_unbound_action_is_never_pressed()
    {
        Assert.False(InputBindingKeyNames.TryParseKeyboardKey(PlayerProfile.UnboundKey, out _));
    }

    [Fact]
    public void Rebinding_a_conflicting_key_swaps_assignments_and_round_trips()
    {
        GameConfig config = GameConfig.CreateDefault();
        PlayerProfile profile = config.Active!;
        int moveLeft = profile.Bindings.FindIndex(binding => binding.Action == "MoveLeft");
        int fire = profile.Bindings.FindIndex(binding => binding.Action == "Fire");

        Assert.Equal(moveLeft, profile.RebindKeyboard(fire, KeyCode.A));
        Assert.Equal("KEY_A", profile.Bindings[fire].Key);
        Assert.Equal(InputBindingKeyNames.MouseDevice, profile.Bindings[moveLeft].Device);
        Assert.Equal("MOUSE_BUTTON_1", profile.Bindings[moveLeft].Key);

        GameConfig loaded = ConfigScript.Load(ConfigScriptWriter.Write(config));
        Assert.Equal("KEY_A", loaded.Active!.Bindings[fire].Key);
        Assert.Equal("MOUSE_BUTTON_1", loaded.Active.Bindings[moveLeft].Key);
    }

    [Fact]
    public void Resetting_bindings_restores_defaults_and_preserves_extension_actions()
    {
        PlayerProfile profile = PlayerProfile.CreateDefault();
        int moveUp = profile.Bindings.FindIndex(binding => binding.Action == "MoveUp");
        profile.RebindKeyboard(moveUp, KeyCode.F);
        profile.Bindings.Add(new InputBinding("ModAction", InputBindingKeyNames.KeyboardDevice, "KEY_F10"));

        profile.ResetBindingsToDefaults();

        Assert.Equal("KEY_W", profile.Bindings[moveUp].Key);
        Assert.Contains(profile.Bindings, binding => binding.Action == "ModAction" && binding.Key == "KEY_F10");
    }

    [Fact]
    public void Retail_binding_names_round_trip_through_the_port_key_names()
    {
        Assert.True(InputBindingKeyNames.TryParseKeyboardKey("KEY_RIGHT_CTRL", out KeyCode control));
        Assert.Equal(KeyCode.RightControl, control);
        Assert.True(InputBindingKeyNames.TryParseKeyboardKey("KEY_NUMPAD0", out KeyCode numpad));
        Assert.Equal(KeyCode.Numpad0, numpad);
        Assert.Equal("KEY_RIGHT_CTRL", InputBindingKeyNames.ToRetailKeyboardKey(KeyCode.RightControl));
        Assert.Equal("KEY_9", InputBindingKeyNames.ToRetailKeyboardKey(KeyCode.D9));
        Assert.True(InputBindingKeyNames.TryParseMouseButton("MOUSE_BUTTON_5", out MouseButton button));
        Assert.Equal(MouseButton.Extra2, button);
        Assert.Equal("MOUSE_BUTTON_5", InputBindingKeyNames.ToRetailMouseButton(MouseButton.Extra2));
        Assert.Equal(
            "Mouse Button 1",
            InputBindingKeyNames.DisplayName(new InputBinding("Fire", InputBindingKeyNames.MouseDevice, "MOUSE_BUTTON_1")));
    }

    [Fact]
    public void ProfileNamesAreTrimmedUniqueAndBounded()
    {
        GameConfig config = GameConfig.CreateDefault();

        Assert.False(config.CreateProfile("   ", 1));
        Assert.False(config.CreateProfile(string.Empty, 1));
        Assert.False(config.CreateProfile("joe", 1));

        Assert.True(config.CreateProfile("Alex  ", 1));
        Assert.Equal("Alex", config.Active!.Name);
    }

    [Fact]
    public void TheProfileListStopsAtSixteen()
    {
        GameConfig config = new();

        for (int i = 0; i < GameConfig.MaxProfiles; i++)
        {
            Assert.True(config.CreateProfile($"P{i}", 1));
        }

        Assert.False(config.CreateProfile("One too many", 1));
    }

    [Fact]
    public void AVolumeSurvivesTheRoundTripThroughTheNatives()
    {
        ConfigNatives natives = new(GameConfig.CreateDefault(), NullConfigHost.Instance);

        Assert.True(Invoke(natives, "Config_SoundOptionsSetMusicVolume", "0.75", out _));
        Assert.True(Invoke(natives, "Config_SoundOptionsGetMusicVolume", null, out QvmValue read));

        // The whole reason the argument arrives unevaluated: an integer read would make this zero.
        Assert.Equal(0.75f, read.ToSingle());
    }

    [Fact]
    public void ContentControlIsOffUntilAPasswordIsSet()
    {
        ConfigNatives natives = new(GameConfig.CreateDefault(), NullConfigHost.Instance);

        Assert.True(Invoke(natives, "Config_IsContentControlPasswordEnabled", null, out QvmValue off));
        Assert.Equal(0, off.ToInt32());

        Invoke(natives, "Config_SetContentControlPassword", "\"hunter2\"", out _);

        Assert.True(Invoke(natives, "Config_IsContentControlPasswordEnabled", null, out QvmValue on));
        Assert.Equal(1, on.ToInt32());

        Invoke(natives, "Config_VerifyContentControlPassword", "\"wrong\"", out QvmValue bad);
        Invoke(natives, "Config_VerifyContentControlPassword", "\"hunter2\"", out QvmValue good);

        Assert.Equal(0, bad.ToInt32());
        Assert.Equal(1, good.ToInt32());
    }

    [Fact]
    public void AListFillerWritesIntoTheControlRunningItsScript()
    {
        GameConfig config = GameConfig.CreateDefault();
        config.CreateProfile("Alex", 2);

        ListBoxTask list = new();
        ConfigNatives natives = new(config, NullConfigHost.Instance) { Target = list };

        Assert.True(Invoke(natives, "Config_FillPlayerProfileListBox", null, out _));

        Assert.Equal(2, list.Items.Count);
        Assert.Equal("Joe", list.Items[0].Label);
        Assert.Equal("Alex", list.Items[1].Label);
    }

    [Fact]
    public void AnUnknownNameIsNotClaimed()
    {
        ConfigNatives natives = new(GameConfig.CreateDefault(), NullConfigHost.Instance);

        Assert.False(Invoke(natives, "Game_SetMusicVolume", "1", out _));
    }

    private static bool Invoke(ConfigNatives natives, string name, string? argument, out QvmValue result)
    {
        ScriptExpressionEvaluator evaluator = new(new EmptyContext());
        List<ScriptExpression> arguments = argument is null
            ? []
            : [ScriptExpressionParser.Parse(argument, _ => null)];

        return natives.TryInvoke(name, new ScriptArguments(evaluator, arguments), out result);
    }

    private static GameConfig LoadRetail() =>
        ConfigScript.Load(File.ReadAllBytes(GameData.Resolve("config.qvm")));

    private sealed class EmptyContext : IScriptExpressionContext
    {
        public bool TryGetValue(string name, out QvmValue value)
        {
            value = QvmValue.Zero;
            return false;
        }

        public QvmValueKind GetValueKind(string name) => QvmValueKind.None;

        public bool TrySetValue(string name, QvmValue value) => false;

        public bool TryInvoke(string name, ScriptArguments arguments, out QvmValue result)
        {
            result = QvmValue.Zero;
            return false;
        }
    }
}
