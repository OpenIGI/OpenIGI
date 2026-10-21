using OpenIGI.Core.Configuration;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Titles;
using OpenIGI.Frontend.Saves;
using OpenIGI.Frontend.Screens;
using OpenIGI.Frontend.Settings;
using OpenIGI.Frontend;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Input;
using OpenIGI.Core.Configuration.Presets;
using OpenIGI.Rendering;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Screens;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using OpenIGI.UI;
using System.Linq;
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

            Assert.Equal(PlayerProfile.BindingCount, screen.Root!.Descendants().OfType<KeyBindingRow>().Count());
            KeyBindingRow moveLeft = screen.Root.Descendants().OfType<KeyBindingRow>().Single(row => row.Action == "MoveLeft");
            KeyBindingRow fire = screen.Root.Descendants().OfType<KeyBindingRow>().Single(row => row.Action == "Fire");
            Assert.Equal(KeyCode.A, moveLeft.BoundKey);
            Assert.Equal("A", moveLeft.BoundKeyName);
            Assert.Equal("Mouse Button 1", fire.BoundKeyName);
            Assert.NotNull(Find<Button>(screen, button => button.Text == "RESET BINDINGS TO DEFAULTS"));
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

            KeyBindingRow moveLeft = screen.Root!.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "MoveLeft");
            KeyBindingRow fire = screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "Fire");
            KeyBindingRow nextWeapon = screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "NextWeapon");

            fire.Activate();
            Assert.True(fire.TryCaptureKey(KeyCode.A));

            Assert.Equal(KeyCode.A, fire.BoundKey);
            Assert.Equal("A", fire.BoundKeyName);
            Assert.Equal(KeyCode.None, moveLeft.BoundKey);
            Assert.Equal("Mouse Button 1", moveLeft.BoundKeyName);

            moveLeft.Activate();
            Assert.True(moveLeft.TryCaptureMouseButton(MouseButton.Right));

            Assert.Equal("Mouse Button 2", moveLeft.BoundKeyName);
            Assert.Equal("Mouse Button 1", nextWeapon.BoundKeyName);
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
            KeyBindingRow crouch = screen.Root!.Descendants().OfType<KeyBindingRow>().Single(r => r.Action == "Crouch");
            KeyBindingRow walk = screen.Root.Descendants().OfType<KeyBindingRow>().Single(r => r.Action == "Walk/run");
            Assert.DoesNotContain(screen.Root.Descendants().OfType<KeyBindingRow>(), r => r.Action == "ToggleWalkRun");
            Assert.Equal(KeyCode.LeftShift, crouch.BoundKey);
            crouch.Activate();
            Assert.True(crouch.TryCaptureKey(KeyCode.RightControl));
            Assert.Equal("KEY_RIGHT_CTRL", host.Profile!.Bindings.Single(b => b.Action == "Crouch").Key);
            Assert.Equal("KEY_LEFT_SHIFT", host.Profile.Bindings.Single(b => b.Action == "ToggleWalkRun").Key);
            walk.Activate();
            Assert.True(walk.TryCaptureKey(KeyCode.RightControl));
            Assert.Equal(KeyCode.CapsLock, crouch.BoundKey);
            Assert.Equal(KeyCode.RightControl, walk.BoundKey);
            Find<Button>(screen, b => b.Text == "RESET BINDINGS TO DEFAULTS")!.Activate();
            Assert.Equal(KeyCode.LeftShift, crouch.BoundKey);
            Assert.Equal(KeyCode.CapsLock, walk.BoundKey);
            Assert.Equal("KEY_RIGHT_CTRL", host.Profile.Bindings.Single(b => b.Action == "Crouch").Key);
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

            KeyBindingRow fire = screen.Root!.Descendants().OfType<KeyBindingRow>()
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

            KeyBindingRow moveUp = screen.Root!.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "MoveUp");
            moveUp.Activate();
            Assert.True(moveUp.TryCaptureKey(KeyCode.F));
            Assert.Equal(KeyCode.F, moveUp.BoundKey);

            Find<Button>(screen, button => button.Text == "RESET BINDINGS TO DEFAULTS")!.Activate();

            Assert.Equal(KeyCode.W, moveUp.BoundKey);
            Assert.Equal(KeyCode.A, screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "MoveLeft").BoundKey);
            Assert.Equal(KeyCode.D, screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "MoveRight").BoundKey);
            Assert.Equal(KeyCode.S, screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "MoveDown").BoundKey);
            Assert.Equal(KeyCode.Space, screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "Jump").BoundKey);
            Assert.Equal(KeyCode.C, screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "MapComputer").BoundKey);
            Assert.Equal(KeyCode.E, screen.Root.Descendants().OfType<KeyBindingRow>()
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

            KeyBindingRow fire = screen.Root!.Descendants().OfType<KeyBindingRow>()
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

            KeyBindingRow fire = screen.Root!.Descendants().OfType<KeyBindingRow>()
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
