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

        // The first profile is always the default "Joe", and GOStart names which one is in play.
        Assert.NotEmpty(config.Profiles);
        Assert.Equal("Joe", config.Profiles[0].Name);
        Assert.InRange(config.ActiveIndex, 0, config.Profiles.Count - 1);
    }

    [RequiresGameDataFact]
    public void EveryProfileCarriesItsOwnSettingsAndBindings()
    {
        GameConfig config = LoadRetail();

        foreach (PlayerProfile profile in config.Profiles)
        {
            // Twenty-seven bindings, stored by name so they survive a renumbering.
            Assert.Equal(PlayerProfile.BindingCount, profile.Bindings.Count);
            Assert.Contains(profile.Bindings, b => b.Action == "MoveLeft" && b.Key == "KEY_LEFT");
            Assert.Contains(profile.Bindings, b => b.Device == "INPUTPORT_DEVICE_MOUSE");
            Assert.Equal(PlayerProfile.DefaultDeviceGuid, profile.DeviceGuid);
        }
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
        Assert.Equal(new InputBinding("MoveLeft", "INPUTPORT_DEVICE_KEYBOARD", "KEY_LEFT"), reloaded.Profiles[0].Bindings[0]);
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
    public void ADefaultProfileHasTheRetailBindingsAndAnEmptyConfigurationUsesThemToo()
    {
        GameConfig defaults = GameConfig.CreateDefault();
        PlayerProfile joe = defaults.Active!;

        Assert.Equal(PlayerProfile.BindingCount, joe.Bindings.Count);
        Assert.Equal(new InputBinding("MoveLeft", "INPUTPORT_DEVICE_KEYBOARD", "KEY_LEFT"), joe.Bindings[0]);
        Assert.Contains(new InputBinding("Fire", "INPUTPORT_DEVICE_MOUSE", "MOUSE_BUTTON_1"), joe.Bindings);
        Assert.Contains(new InputBinding("WeaponCategory9", "INPUTPORT_DEVICE_KEYBOARD", "KEY_9"), joe.Bindings);

        GameConfig empty = new();
        Assert.True(empty.CreateProfile("Alex", 1));
        Assert.Equal(PlayerProfile.BindingCount, empty.Active!.Bindings.Count);
    }

    [Fact]
    public void Loading_a_legacy_profile_with_no_bindings_repairs_the_retail_defaults()
    {
        GameConfig legacy = new();
        legacy.Add(new PlayerProfile { Name = "Joe" });

        GameConfig loaded = ConfigScript.Load(ConfigScriptWriter.Write(legacy));

        Assert.Equal(PlayerProfile.BindingCount, loaded.Active!.Bindings.Count);
        Assert.Equal(new InputBinding("MoveLeft", "INPUTPORT_DEVICE_KEYBOARD", "KEY_LEFT"), loaded.Active.Bindings[0]);
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
