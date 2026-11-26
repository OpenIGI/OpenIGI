using OpenIGI.Frontend.Screens;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Input;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;
using Xunit;

namespace OpenIGI.Frontend.Tests;

/// <summary>Covers the settings' controls tab: the binding rows, capturing a key, button or wheel, and resetting them.</summary>
[Collection(ShellTestHarness.CollectionName)]
public sealed class ControlsTabShellTests : ShellTestHarness
{
    [Fact]
    public void Controls_tab_lists_the_full_retail_binding_set_for_a_fresh_profile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, _) = BuildShell(dir);
            Pump(shell, 0.1f);
            Find<Button>(shell.Stack.Top!, button => button.Text == "Settings")!.Activate();
            Pump(shell, 0.6f);

            SettingsScreen screen = Assert.IsType<SettingsScreen>(shell.Stack.Top);
            TabStrip tabs = screen.Root!.Descendants().OfType<TabStrip>().First();
            tabs.SelectedIndex = 3; // Controls
            Pump(shell, 0.1f);

            Assert.Equal(PlayerProfile.BindingCount, screen.Root.Descendants().OfType<KeyBindingRow>().Count());
            KeyBindingRow moveLeft = screen.Root.Descendants().OfType<KeyBindingRow>().Single(row => row.Action == "Move left");
            KeyBindingRow fire = screen.Root.Descendants().OfType<KeyBindingRow>().Single(row => row.Action == "Fire");
            Assert.Equal(KeyCode.A, moveLeft.BoundKey);
            Assert.Equal("A", moveLeft.BoundKeyName);
            Assert.Equal("Mouse Button 1", fire.BoundKeyName);
            Assert.NotNull(Find<Button>(screen, button => button.Text == "RESET BINDINGS"));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    /// <summary>The keys only a multiplayer session answers to have a panel of their own, and no other panel has them.</summary>
    [Fact]
    public void Controls_tab_keeps_the_multiplayer_only_keys_in_their_own_panel()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, _) = BuildShell(dir);
            Pump(shell, 0.1f);
            Find<Button>(shell.Stack.Top!, button => button.Text == "Settings")!.Activate();
            Pump(shell, 0.6f);

            SettingsScreen screen = Assert.IsType<SettingsScreen>(shell.Stack.Top);
            TabStrip tabs = screen.Root!.Descendants().OfType<TabStrip>().First();
            tabs.SelectedIndex = 3; // Controls
            Pump(shell, 0.1f);

            OpenIGI.UI.Kit.Widgets.GlassPanel multiplayer = screen.Root.Descendants().OfType<OpenIGI.UI.Kit.Widgets.GlassPanel>()
                .Single(panel => panel.Descendants().OfType<OpenIGI.UI.Kit.Widgets.GroupHeader>().Any(header => header.Title == "MULTIPLAYER"));
            string[] actions = [.. multiplayer.Descendants().OfType<KeyBindingRow>().Select(row => row.Action)];

            Assert.Contains("Scoreboard", actions);
            Assert.Contains("Buy menu", actions);
            Assert.DoesNotContain("Drop weapon", actions); // I.G.I. 2 drops in single player too, so it sits with the weapon keys
            Assert.DoesNotContain("Fire", actions);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Co-op voice has a panel of its own, with the key held to talk; and a match's keys kept as settings -- the team menu's --
    /// sit with the other multiplayer keys and write the key's bind name when rebound.
    /// </summary>
    [Fact]
    public void Controls_tab_offers_voice_and_the_match_keys_kept_as_settings()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var cvars = new OpenIGI.Core.Configuration.CVarRegistry();
            OpenIGI.Core.Configuration.CVar.Int("coop_voice", 1, 0, 2, OpenIGI.Core.Configuration.CVarOptions.Archive, "Voice.", cvars);
            OpenIGI.Core.Configuration.CVar.Int("coop_voice_volume", 100, 0, 200, OpenIGI.Core.Configuration.CVarOptions.Archive, "Volume.", cvars);
            OpenIGI.Core.Configuration.CVar.Text("coop_voice_key", "k", OpenIGI.Core.Configuration.CVarOptions.Archive, "Talk key.", cvars);
            OpenIGI.Core.Configuration.StringCVar team = OpenIGI.Core.Configuration.CVar.Text(
                "mp_team_key", "m", OpenIGI.Core.Configuration.CVarOptions.Archive, "Team key.", cvars);

            (Shell shell, _) = BuildShell(dir, cvars: cvars);
            Pump(shell, 0.1f);
            Find<Button>(shell.Stack.Top!, button => button.Text == "Settings")!.Activate();
            Pump(shell, 0.6f);

            SettingsScreen screen = Assert.IsType<SettingsScreen>(shell.Stack.Top);
            TabStrip tabs = screen.Root!.Descendants().OfType<TabStrip>().First();
            tabs.SelectedIndex = 3; // Controls
            Pump(shell, 0.1f);

            OpenIGI.UI.Kit.Widgets.GlassPanel Panel(string title) => screen.Root.Descendants().OfType<OpenIGI.UI.Kit.Widgets.GlassPanel>()
                .Single(panel => panel.Descendants().OfType<OpenIGI.UI.Kit.Widgets.GroupHeader>().Any(header => header.Title == title));

            OpenIGI.UI.Kit.Widgets.GlassPanel voice = Panel("VOICE");
            Assert.Contains(voice.Descendants().OfType<Dropdown>(), row => row.Label == "Voice Chat");
            KeyBindingRow talk = voice.Descendants().OfType<KeyBindingRow>().Single();
            Assert.Equal("Push to talk", talk.Action);
            Assert.Equal(KeyCode.K, talk.BoundKey);

            KeyBindingRow teamRow = Panel("MULTIPLAYER").Descendants().OfType<KeyBindingRow>().Single(row => row.Action == "Team menu");
            teamRow.Activate();
            Assert.True(teamRow.TryCaptureKey(KeyCode.N));
            Assert.Equal("n", team.Value);
            Assert.Equal(KeyCode.N, teamRow.BoundKey);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Controls_tab_swaps_conflicting_assignments_and_refreshes_both_rows()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, _) = BuildShell(dir);
            Pump(shell, 0.1f);
            Find<Button>(shell.Stack.Top!, button => button.Text == "Settings")!.Activate();
            Pump(shell, 0.6f);

            SettingsScreen screen = Assert.IsType<SettingsScreen>(shell.Stack.Top);
            TabStrip tabs = screen.Root!.Descendants().OfType<TabStrip>().First();
            tabs.SelectedIndex = 3;
            Pump(shell, 0.1f);

            KeyBindingRow moveLeft = screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "Move left");
            KeyBindingRow fire = screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "Fire");
            // The right button is the sight's (alternate fire) in a fresh profile.
            KeyBindingRow sight = screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "Alternate fire");

            fire.Activate();
            Assert.True(fire.TryCaptureKey(KeyCode.A));

            Assert.Equal(KeyCode.A, fire.BoundKey);
            Assert.Equal("A", fire.BoundKeyName);
            Assert.Equal(KeyCode.None, moveLeft.BoundKey);
            Assert.Equal("Mouse Button 1", moveLeft.BoundKeyName);

            moveLeft.Activate();
            Assert.True(moveLeft.TryCaptureMouseButton(MouseButton.Right));

            Assert.Equal("Mouse Button 2", moveLeft.BoundKeyName);
            Assert.Equal("Mouse Button 1", sight.BoundKeyName);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    /// <summary>Title controls hide superseded rows and swap/reset only the controls active in that title.</summary>
    [Fact]
    public void Controls_tab_scopes_title_rebindings_and_reset()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            TitleInputBinding[] controls =
            [
                new(1, new("Title_test_Crouch", InputBindingKeyNames.KeyboardDevice, "KEY_LEFT_SHIFT"), "Crouch", PlayerAction.Crouch),
                new(2, new("Title_test_Walk", InputBindingKeyNames.KeyboardDevice, "KEY_CAPITAL"), "Walk/run", PlayerAction.ToggleWalkRun),
            ];
            (Shell shell, FakeHost host) = BuildShell(dir, controls: controls);
            Pump(shell, 0.1f);
            Find<Button>(shell.Stack.Top!, button => button.Text == "Settings")!.Activate();
            Pump(shell, 0.6f);
            SettingsScreen screen = Assert.IsType<SettingsScreen>(shell.Stack.Top);
            screen.Root!.Descendants().OfType<TabStrip>().First().SelectedIndex = 3;
            Pump(shell, 0.1f);
            KeyBindingRow crouch = screen.Root.Descendants().OfType<KeyBindingRow>().Single(r => r.Action == "Crouch");
            KeyBindingRow walk = screen.Root.Descendants().OfType<KeyBindingRow>().Single(r => r.Action == "Walk/run");
            Assert.DoesNotContain(screen.Root.Descendants().OfType<KeyBindingRow>(), r => r.Action == "Walk / run");
            Assert.Equal(KeyCode.LeftShift, crouch.BoundKey);
            crouch.Activate();
            Assert.True(crouch.TryCaptureKey(KeyCode.RightControl));
            Assert.Equal("KEY_LEFT_CTRL", host.Profile!.Bindings.Single(b => b.Action == "Crouch").Key);
            Assert.Equal("KEY_LEFT_SHIFT", host.Profile.Bindings.Single(b => b.Action == "ToggleWalkRun").Key);
            walk.Activate();
            Assert.True(walk.TryCaptureKey(KeyCode.RightControl));
            Assert.Equal(KeyCode.CapsLock, crouch.BoundKey);
            Assert.Equal(KeyCode.RightControl, walk.BoundKey);
            Find<Button>(screen, b => b.Text == "RESET BINDINGS")!.Activate();
            Assert.Equal(KeyCode.LeftShift, crouch.BoundKey);
            Assert.Equal(KeyCode.CapsLock, walk.BoundKey);
            Assert.Equal("KEY_LEFT_CTRL", host.Profile.Bindings.Single(b => b.Action == "Crouch").Key);
        }
        finally
        {
            if (Directory.Exists(dir)) { Directory.Delete(dir, recursive: true); }
        }
    }

    [Fact]
    public void Controls_tab_waits_for_a_fresh_press_after_activation()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, _) = BuildShell(dir);
            Pump(shell, 0.1f);
            Find<Button>(shell.Stack.Top!, button => button.Text == "Settings")!.Activate();
            Pump(shell, 0.6f);

            SettingsScreen screen = Assert.IsType<SettingsScreen>(shell.Stack.Top);
            TabStrip tabs = screen.Root!.Descendants().OfType<TabStrip>().First();
            tabs.SelectedIndex = 3;
            Pump(shell, 0.1f);

            KeyBindingRow fire = screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "Fire");
            UiVec2 centre = fire.Bounds.Center;
            InputState input = new();

            // A stale press is already queued when the pointer activates the row.
            input.Keyboard.SetKey(KeyCode.F, down: true);
            input.Keyboard.SetKey(KeyCode.F, down: false);
            input.BeginFrame();
            input.Mouse.SetPosition(centre.X, centre.Y);
            input.Mouse.SetButton(MouseButton.Left, down: true);
            shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);
            Assert.True(fire.IsListening);

            // The first listening update establishes the capture boundary and discards F.
            input.BeginFrame();
            input.Mouse.SetButton(MouseButton.Left, down: false);
            shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);
            Assert.True(fire.IsListening);
            Assert.Equal("Mouse Button 1", fire.BoundKeyName);

            // Only a press made after the prompt is armed can become the binding.
            input.BeginFrame();
            input.Keyboard.SetKey(KeyCode.G, down: true);
            shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);

            Assert.False(fire.IsListening);
            Assert.Equal(KeyCode.G, fire.BoundKey);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Controls_tab_reset_button_restores_the_modern_default_layout()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, _) = BuildShell(dir);
            Pump(shell, 0.1f);
            Find<Button>(shell.Stack.Top!, button => button.Text == "Settings")!.Activate();
            Pump(shell, 0.6f);

            SettingsScreen screen = Assert.IsType<SettingsScreen>(shell.Stack.Top);
            TabStrip tabs = screen.Root!.Descendants().OfType<TabStrip>().First();
            tabs.SelectedIndex = 3;
            Pump(shell, 0.1f);

            KeyBindingRow moveUp = screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "Move forward");
            moveUp.Activate();
            Assert.True(moveUp.TryCaptureKey(KeyCode.F));
            Assert.Equal(KeyCode.F, moveUp.BoundKey);

            Find<Button>(screen, button => button.Text == "RESET BINDINGS")!.Activate();

            Assert.Equal(KeyCode.W, moveUp.BoundKey);
            Assert.Equal(KeyCode.A, screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "Move left").BoundKey);
            Assert.Equal(KeyCode.D, screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "Move right").BoundKey);
            Assert.Equal(KeyCode.S, screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "Move back").BoundKey);
            Assert.Equal(KeyCode.Space, screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "Jump").BoundKey);
            Assert.Equal(KeyCode.C, screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "Map computer").BoundKey);
            Assert.Equal(KeyCode.F, screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "Activate").BoundKey);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Controls_tab_binds_a_wheel_scroll_after_activation()
    {
        // Issue #232: scrolling while a row listens must bind the wheel direction like a button.
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, _) = BuildShell(dir);
            Pump(shell, 0.1f);
            Find<Button>(shell.Stack.Top!, button => button.Text == "Settings")!.Activate();
            Pump(shell, 0.6f);

            SettingsScreen screen = Assert.IsType<SettingsScreen>(shell.Stack.Top);
            TabStrip tabs = screen.Root!.Descendants().OfType<TabStrip>().First();
            tabs.SelectedIndex = 3;
            Pump(shell, 0.1f);

            KeyBindingRow fire = screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "Fire");
            UiVec2 centre = fire.Bounds.Center;
            InputState input = new();

            input.BeginFrame();
            input.Mouse.SetPosition(centre.X, centre.Y);
            input.Mouse.SetButton(MouseButton.Left, down: true);
            shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);
            Assert.True(fire.IsListening);

            // The first listening update arms the boundary (quiet here, so the wheel gate opens).
            input.BeginFrame();
            input.Mouse.SetButton(MouseButton.Left, down: false);
            shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);
            Assert.True(fire.IsListening);
            Assert.Equal("Mouse Button 1", fire.BoundKeyName);

            // A scroll after arming binds wheel-down and ends listening.
            input.BeginFrame();
            input.Mouse.AddWheel(-1f);
            shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);

            Assert.False(fire.IsListening);
            Assert.Equal(KeyCode.None, fire.BoundKey);
            Assert.Equal("Mouse Wheel Down", fire.BoundKeyName);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Controls_tab_ignores_wheel_inertia_until_a_quiet_frame()
    {
        // Review follow-up: clicking a row mid-scroll (trackpad momentum) must not bind the
        // leftover motion — only a scroll that starts after a quiet frame does.
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, _) = BuildShell(dir);
            Pump(shell, 0.1f);
            Find<Button>(shell.Stack.Top!, button => button.Text == "Settings")!.Activate();
            Pump(shell, 0.6f);

            SettingsScreen screen = Assert.IsType<SettingsScreen>(shell.Stack.Top);
            TabStrip tabs = screen.Root!.Descendants().OfType<TabStrip>().First();
            tabs.SelectedIndex = 3;
            Pump(shell, 0.1f);

            KeyBindingRow fire = screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "Fire");
            UiVec2 centre = fire.Bounds.Center;
            InputState input = new();

            input.BeginFrame();
            input.Mouse.SetPosition(centre.X, centre.Y);
            input.Mouse.SetButton(MouseButton.Left, down: true);
            shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);
            Assert.True(fire.IsListening);

            // Arming frame carries leftover scroll momentum.
            input.BeginFrame();
            input.Mouse.SetButton(MouseButton.Left, down: false);
            input.Mouse.AddWheel(1f);
            shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);
            Assert.True(fire.IsListening);

            // Momentum continues on the next frame: still must not bind.
            input.BeginFrame();
            input.Mouse.AddWheel(1f);
            shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);
            Assert.True(fire.IsListening);
            Assert.Equal("Mouse Button 1", fire.BoundKeyName);

            // A quiet frame opens the gate; the next scroll binds.
            input.BeginFrame();
            shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);
            Assert.True(fire.IsListening);

            input.BeginFrame();
            input.Mouse.AddWheel(1f);
            shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);
            Assert.False(fire.IsListening);
            Assert.Equal("Mouse Wheel Up", fire.BoundKeyName);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
