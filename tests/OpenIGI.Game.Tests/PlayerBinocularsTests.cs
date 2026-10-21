using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using Xunit;
using OpenIGI.Game.Igi1.Player;
using static OpenIGI.Game.Tests.PlayerWeaponRecords;
using static OpenIGI.Game.Tests.PlayerWeaponsDriver;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The binoculars through the weapon machine: the binding that selects them, the actions they ignore,
/// and the authored view and zoom.
/// </summary>
public sealed class PlayerBinocularsTests
{
    [Fact]
    public void BinocularBindingSelectsTheCarriedBinoculars()
    {
        PlayerWeapons weapons = new([30, 20]);
        weapons.Carry(Knife);
        weapons.Carry(Binoculars);
        Equip(weapons);

        PlayerInputState input = new();
        PlayerInputFrame binocularPress = default;
        binocularPress[(int)PlayerInputSlot.Binoculars] = 1f;
        input.Accept(binocularPress);
        weapons.Tick(input, clipEnded: true, onGround: true);

        Assert.Equal(HumanWeaponState.ChangingWeapon, weapons.State);
        for (int i = 0; i < 10 && weapons.State != HumanWeaponState.Ready; i++)
        {
            input.Accept(default);
            weapons.Tick(input, clipEnded: true, onGround: true);
        }

        Assert.Equal("WEAPON_ID_BINOCULARS", weapons.InHand?.Weapon.ScriptId);
        Assert.True(weapons.ConsumeBinocularToggle());
    }

    [Fact]
    public void BinocularsIgnoreFireReloadAndPeekActions()
    {
        PlayerWeapons weapons = new([30]);
        weapons.Carry(Binoculars);
        Equip(weapons);

        PlayerInputState input = new();
        PlayerInputFrame actions = default;
        actions[(int)PlayerInputSlot.Fire] = 1f;
        actions[(int)PlayerInputSlot.Reload] = 1f;
        actions[(int)PlayerInputSlot.Peek] = 1f;
        input.Accept(actions);

        weapons.Tick(input, clipEnded: true, onGround: true);

        Assert.Equal(HumanWeaponState.Ready, weapons.State);
        Assert.False(weapons.FiredThisTick);
        Assert.Null(weapons.RequestedAction);
    }

    [Fact]
    public void BinocularViewUsesAuthoredDefaultAndClampsZoomToAuthoredBounds()
    {
        BinocularsView view = new();
        WeaponType binoculars = Binoculars;
        SightZoomFrame opened = view.Tick(
            binoculars,
            togglePressed: true,
            zoomInHeld: false,
            zoomOutHeld: false,
            tanX: HumanView.DefaultTanX,
            viewModelTanX: HumanView.DefaultViewModelTanX);

        Assert.True(view.IsOpen);
        Assert.True(view.OpenedThisTick);
        Assert.False(view.ClosedThisTick);
        Assert.True(view.ZoomTargetChangedThisTick);
        // Opening the view is silent: the motor cue only sounds while a zoom key is actually moving the
        // magnification, so no zoom step is reported on the open tick.
        Assert.False(view.ZoomSteppingThisTick);
        Assert.Equal(HumanView.DefaultTanX * BinocularsView.ZoomInFactor, opened.TanX, 5);
        Assert.Equal(HumanView.DefaultViewModelTanX, opened.ViewModelTanX);

        // Retail is level-triggered: the handler is invoked every simulation tick while the
        // binoculars are open, multiplying by 0.95999998 on each ZoomIn tick and by 1.04 on each
        // ZoomOut tick. It does NOT use an edge or a cooldown — holding the key steps every tick
        // until the respective authored minimum/maximum tangent is reached, where it stays.
        SightZoomFrame narrowed = view.Tick(binoculars, false, true, false, opened.TanX, opened.ViewModelTanX);
        Assert.Equal(opened.TanX * BinocularsView.ZoomInFactor, narrowed.TanX, 5);
        Assert.True(view.ZoomChangedThisTick);
        Assert.True(view.ZoomTargetChangedThisTick);
        // A held zoom key that moved the magnification is a zoom step -- this is when the cue plays.
        Assert.True(view.ZoomSteppingThisTick);
        narrowed = view.Tick(binoculars, false, false, false, narrowed.TanX, narrowed.ViewModelTanX);
        // Releasing the key holds the current magnification instead of continuing toward the last
        // retail target. Only holding a zoom input multiplies on the next tick.
        Assert.Equal(opened.TanX * BinocularsView.ZoomInFactor, narrowed.TanX, 5);
        Assert.Equal(HumanView.DefaultViewModelTanX, narrowed.ViewModelTanX);
        Assert.False(view.ZoomChangedThisTick);
        Assert.False(view.ZoomTargetChangedThisTick);
        // Releasing the key ends the zoom step, so the cue stops even though the view stays open.
        Assert.False(view.ZoomSteppingThisTick);
        for (int i = 0; i < 100; i++)
        {
            narrowed = view.Tick(binoculars, false, true, false, narrowed.TanX, narrowed.ViewModelTanX);
            Assert.False(float.IsNaN(narrowed.TanX));
        }

        Assert.Equal(0.1f, narrowed.TanX, 3);
        // Held at the authored minimum the magnification no longer moves, so the zoom step -- and its
        // cue -- ends even while the key is still down.
        Assert.False(view.ZoomSteppingThisTick);

        SightZoomFrame closing = view.Tick(
            binoculars,
            togglePressed: true,
            zoomInHeld: false,
            zoomOutHeld: false,
            tanX: narrowed.TanX,
            viewModelTanX: narrowed.ViewModelTanX);
        Assert.False(view.IsOpen);
        Assert.False(view.OpenedThisTick);
        Assert.True(view.ClosedThisTick);
        Assert.True(view.ZoomTargetChangedThisTick);
        // Closing is silent too -- only an active zoom key drives the cue.
        Assert.False(view.ZoomSteppingThisTick);

        // Closing starts the authored return transition; it does not snap the world camera back in one
        // tick. The loop cue must also stop on close — see bin_zoom_1 one-shot at mission teardown
        // or death (Ghidra FUN_004B5860 strip + FUN_00470570 0x3c23d70a syringe vs firearm report).
        Assert.Equal(0.1f * BinocularsView.ZoomOutFactor, closing.TanX, 5);
        Assert.Equal(HumanView.DefaultViewModelTanX, closing.ViewModelTanX);

        SightZoomFrame restored = closing;
        for (int i = 0; i < 100 && view.HasOpticsState; i++)
        {
            restored = view.Tick(
                binoculars,
                togglePressed: false,
                zoomInHeld: false,
                zoomOutHeld: false,
                tanX: restored.TanX,
                viewModelTanX: restored.ViewModelTanX);
        }

        Assert.Equal(HumanView.DefaultTanX, restored.TanX, 5);
        Assert.False(view.HasOpticsState);
    }

    [Fact]
    public void BinocularZoomInFractionTracksTheTangentBetweenDefaultAndMinimum()
    {
        // The remastered detector's range scales with this. Binoculars author default 0.5, min 0.1.
        BinocularsView view = new();
        view.Tick(Binoculars, togglePressed: true, false, false, HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);

        Assert.Equal(0f, view.ZoomInFraction(0.5f), 3);   // the authored default -- base range
        Assert.Equal(1f, view.ZoomInFraction(0.1f), 3);   // the tightest zoom -- full range
        Assert.Equal(0.5f, view.ZoomInFraction(0.3f), 3); // halfway in
        Assert.Equal(0f, view.ZoomInFraction(1.0f), 3);   // zoomed out past the default -- clamped
        Assert.Equal(0f, view.ZoomInFraction(2.0f), 3);   // beyond the widest -- clamped
    }

    [Fact]
    public void BinocularZoomInFractionIsZeroWithoutABoundBinocular()
    {
        BinocularsView view = new();
        Assert.Equal(0f, view.ZoomInFraction(0.3f), 3);
    }

    [Fact]
    public void BinocularViewUsesTheRetailMultiplicativeZoomFactors()
    {
        BinocularsView view = new();
        WeaponType binoculars = Binoculars;

        SightZoomFrame opened = view.Tick(
            binoculars,
            togglePressed: true,
            zoomInHeld: false,
            zoomOutHeld: false,
            tanX: HumanView.DefaultTanX,
            viewModelTanX: HumanView.DefaultViewModelTanX);
        Assert.Equal(HumanView.DefaultTanX * BinocularsView.ZoomInFactor, opened.TanX, precision: 5);
        Assert.True(view.ZoomChangedThisTick);
        // No progress or filter check — retail input is exact; r2 shows immediate multiply & clamp.

        // Exactly one zoom factor per held tick at 0.95999998 / 1.04 (IGI.EXE 0x5399D0 constants).
        SightZoomFrame belowThreshold = view.Tick(
            binoculars,
            togglePressed: false,
            zoomInHeld: true,
            zoomOutHeld: false,
            tanX: 0.79f,
            viewModelTanX: HumanView.DefaultViewModelTanX);
        Assert.Equal(0.79f * BinocularsView.ZoomInFactor, belowThreshold.TanX, precision: 5);

        SightZoomFrame wider = view.Tick(
            binoculars,
            togglePressed: false,
            zoomInHeld: false,
            zoomOutHeld: true,
            tanX: belowThreshold.TanX,
            viewModelTanX: HumanView.DefaultViewModelTanX);
        Assert.Equal(belowThreshold.TanX * BinocularsView.ZoomOutFactor, wider.TanX, precision: 5);
        Assert.True(wider.TanX > belowThreshold.TanX);
    }

    [Fact]
    public void OpticalLatchesCanBeResetWhenFirstPersonControlEnds()
    {
        BinocularsView binoculars = new();
        binoculars.Tick(Binoculars, true, false, false, HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);
        Assert.True(binoculars.IsOpen);
        binoculars.Reset();
        Assert.False(binoculars.IsOpen);

        Mp5SightZoom sight = new();
        WeaponType mp5 = new()
        {
            WeaponId = 7,
            ScriptId = "WEAPON_ID_MP5SD",
            Category = WeaponCategory.Gun,
            Weapon = new WeaponTaskBinding("TASKTYPE_GUNMP5SD", 0, [0.1f], string.Empty, 0),
        };
        sight.Tick(mp5, true, HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);
        Assert.True(sight.IsActive);
        sight.Reset();
        Assert.False(sight.IsActive);
        SightZoomFrame sightFrame = sight.Tick(mp5, false, 0.2f, 0.2f);
        Assert.Equal(HumanView.DefaultTanX, sightFrame.TanX);
        Assert.Equal(HumanView.DefaultViewModelTanX, sightFrame.ViewModelTanX);

        DragunovScope scope = new();
        WeaponType dragunov = new()
        {
            WeaponId = 32,
            ScriptId = "WEAPON_ID_DRAGUNOV",
            Category = WeaponCategory.Gun,
            Weapon = new WeaponTaskBinding("TASKTYPE_GUNDRAGUNOV", 0, [0.1f], string.Empty, 0),
        };
        scope.Tick(dragunov, true, false, false, false, HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);
        Assert.True(scope.IsScoped);
        scope.Reset();
        Assert.False(scope.IsScoped);
    }
}
