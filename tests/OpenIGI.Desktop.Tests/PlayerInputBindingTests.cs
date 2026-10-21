using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Input;
using Xunit;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Desktop.Tests;

public sealed class PlayerInputBindingTests
{
    [Fact]
    public void Every_runtime_action_has_a_persisted_profile_name()
    {
        string[] configured = [.. PlayerProfile.DefaultBindings.Select(binding => binding.Action).Order()];
        string[] sampled = [.. PlayerInputBindings.All.Select(binding => PlayerInputBindings.ConfigName(binding.Action)).Order()];

        Assert.Equal(configured, sampled);
    }

    [Fact]
    public void Sampler_uses_every_profile_binding_including_mouse_buttons()
    {
        PlayerProfile profile = PlayerProfile.CreateDefault();
        InputState input = new();
        input.BeginFrame();
        input.Keyboard.SetKey(KeyCode.A, down: true);
        input.Keyboard.SetKey(KeyCode.D, down: true);
        input.Keyboard.SetKey(KeyCode.W, down: true);
        input.Keyboard.SetKey(KeyCode.S, down: true);
        input.Keyboard.SetKey(KeyCode.Backspace, down: true);
        input.Keyboard.SetKey(KeyCode.PageUp, down: true);
        input.Keyboard.SetKey(KeyCode.Space, down: true);
        input.Keyboard.SetKey(KeyCode.B, down: true);
        input.Keyboard.SetKey(KeyCode.LeftShift, down: true);
        input.Keyboard.SetKey(KeyCode.O, down: true);
        input.Keyboard.SetKey(KeyCode.D9, down: true);
        input.Mouse.SetButton(MouseButton.Left, down: true);
        input.Mouse.SetButton(MouseButton.Right, down: true);

        PlayerInputFrame frame = PlayerCamera.SampleFrame(input, profile);

        Assert.True(frame.IsSet(PlayerInputSlot.MoveLeft));
        Assert.True(frame.IsSet(PlayerInputSlot.MoveRight));
        Assert.True(frame.IsSet(PlayerInputSlot.MoveUp));
        Assert.True(frame.IsSet(PlayerInputSlot.MoveDown));
        Assert.True(frame.IsSet(PlayerInputSlot.Fire));
        Assert.True(frame.IsSet(PlayerInputSlot.AlternateFire));
        Assert.True(frame.IsSet(PlayerInputSlot.ZoomIn));
        Assert.True(frame.IsSet(PlayerInputSlot.Jump));
        Assert.True(frame.IsSet(PlayerInputSlot.Binoculars));
        Assert.True(frame.IsSet(PlayerInputSlot.ToggleWalkRun));
        Assert.True(frame.IsSet(PlayerInputSlot.NextWeapon));
        Assert.True(frame.IsSet(PlayerInputSlot.PreviousWeapon));
        Assert.True(frame.IsSet(PlayerInputSlot.WeaponCategory9));
    }

    [Fact]
    public void Sampler_observes_a_rebind_immediately_and_stops_using_the_old_key()
    {
        PlayerProfile profile = PlayerProfile.CreateDefault();
        int moveLeft = profile.Bindings.FindIndex(binding => binding.Action == "MoveLeft");
        profile.RebindKeyboard(moveLeft, KeyCode.F);

        InputState input = new();
        input.BeginFrame();
        input.Keyboard.SetKey(KeyCode.A, down: true);
        Assert.False(PlayerCamera.SampleFrame(input, profile).IsSet(PlayerInputSlot.MoveLeft));

        input.Keyboard.SetKey(KeyCode.F, down: true);
        Assert.True(PlayerCamera.SampleFrame(input, profile).IsSet(PlayerInputSlot.MoveLeft));
    }
}
