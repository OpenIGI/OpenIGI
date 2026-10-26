using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using Xunit;
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

    // 0x470D70's fallbacks, which are also what the retail weapon config authors: 70, 90 and 5 degrees.
    private static readonly float Default = BinocularsView.HalfAngleTangent(70f);
    private static readonly float Widest = BinocularsView.HalfAngleTangent(90f);
    private static readonly float Narrowest = BinocularsView.HalfAngleTangent(5f);

    [Fact]
    public void TheAnglesAreTheConfigsHalfAngleTangents()
    {
        // The task's own arguments halve with integer division (5 -> 2 degrees); the config's reader does not.
        Assert.Equal(MathF.Tan(35f * MathF.PI / 180f), Default, 5);
        Assert.Equal(1f, Widest, 5);
        Assert.Equal(MathF.Tan(2.5f * MathF.PI / 180f), Narrowest, 5);
        Assert.NotEqual(WeaponTaskParameters.HalfAngleTangent(5), Narrowest);
    }

    [Fact]
    public void RaisingTheBinocularsSnapsTheViewToTheDefault()
    {
        // 0x470B83 writes the view's tangent outright on the first use, and takes no zoom step that tick.
        BinocularsView view = new();
        WeaponType binoculars = Binoculars;
        SightZoomFrame opened = view.Tick(binoculars, togglePressed: true, zoomInHeld: true, zoomOutHeld: false,
            HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);

        Assert.True(view.IsOpen);
        Assert.True(view.OpenedThisTick);
        Assert.Equal(Default, opened.TanX);
        Assert.Equal(HumanView.DefaultViewModelTanX, opened.ViewModelTanX);
        Assert.True(view.ZoomChangedThisTick);
        Assert.False(view.ZoomSteppingThisTick);
    }

    [Fact]
    public void AZoomStepsUntilItHasPassedItsLimit()
    {
        BinocularsView view = new();
        WeaponType binoculars = Binoculars;
        SightZoomFrame frame = view.Tick(binoculars, true, false, false, HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);

        frame = view.Tick(binoculars, false, true, false, frame.TanX, frame.ViewModelTanX);
        Assert.Equal(Default * BinocularsView.ZoomInFactor, frame.TanX);
        Assert.True(view.ZoomSteppingThisTick);

        // Let go, and the magnification holds.
        float held = frame.TanX;
        frame = view.Tick(binoculars, false, false, false, frame.TanX, frame.ViewModelTanX);
        Assert.Equal(held, frame.TanX);
        Assert.False(view.ZoomSteppingThisTick);

        // A step is taken while the tangent is still above the minimum, unclamped, so the last lands under it.
        for (int i = 0; i < 200 && frame.TanX > Narrowest; i++)
        {
            frame = view.Tick(binoculars, false, true, false, frame.TanX, frame.ViewModelTanX);
        }

        Assert.True(frame.TanX <= Narrowest);
        Assert.True(frame.TanX > Narrowest * BinocularsView.ZoomInFactor);
        float floor = frame.TanX;
        frame = view.Tick(binoculars, false, true, false, frame.TanX, frame.ViewModelTanX);
        Assert.Equal(floor, frame.TanX);
        Assert.False(view.ZoomSteppingThisTick);

        for (int i = 0; i < 400 && frame.TanX < Widest; i++)
        {
            frame = view.Tick(binoculars, false, false, true, frame.TanX, frame.ViewModelTanX);
        }

        Assert.True(frame.TanX >= Widest);
        Assert.True(frame.TanX < Widest * BinocularsView.ZoomOutFactor);
    }

    [Fact]
    public void AlternateFireZoomsInThenLatchesThenSnapsBack()
    {
        BinocularsView view = new();
        WeaponType binoculars = Binoculars;
        SightZoomFrame frame = view.Tick(binoculars, true, false, false, HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);

        // Held: it zooms in, a step a tick.
        frame = view.Tick(binoculars, false, false, false, frame.TanX, frame.ViewModelTanX, alternateHeld: true, alternateHeldLastTick: false);
        frame = view.Tick(binoculars, false, false, false, frame.TanX, frame.ViewModelTanX, alternateHeld: true, alternateHeldLastTick: true);
        float zoomed = Default * BinocularsView.ZoomInFactor * BinocularsView.ZoomInFactor;
        Assert.Equal(zoomed, frame.TanX);

        // Let go: it latches and holds.
        frame = view.Tick(binoculars, false, false, false, frame.TanX, frame.ViewModelTanX, alternateHeld: false, alternateHeldLastTick: true);
        Assert.Equal(zoomed, frame.TanX);

        // Pressed again while latched: back to the default at once, silently.
        frame = view.Tick(binoculars, false, false, false, frame.TanX, frame.ViewModelTanX, alternateHeld: true, alternateHeldLastTick: false);
        Assert.Equal(Default, frame.TanX);
        Assert.False(view.ZoomSteppingThisTick);

        // Still held, still latched: nothing.
        frame = view.Tick(binoculars, false, false, false, frame.TanX, frame.ViewModelTanX, alternateHeld: true, alternateHeldLastTick: true);
        Assert.Equal(Default, frame.TanX);

        // Let go frees the latch, and the next hold zooms in again.
        frame = view.Tick(binoculars, false, false, false, frame.TanX, frame.ViewModelTanX, alternateHeld: false, alternateHeldLastTick: true);
        frame = view.Tick(binoculars, false, false, false, frame.TanX, frame.ViewModelTanX, alternateHeld: true, alternateHeldLastTick: false);
        Assert.Equal(Default * BinocularsView.ZoomInFactor, frame.TanX);
        Assert.True(view.ZoomSteppingThisTick);
    }

    [Fact]
    public void PuttingTheBinocularsAwaySnapsTheViewHome()
    {
        BinocularsView view = new();
        WeaponType binoculars = Binoculars;
        SightZoomFrame frame = view.Tick(binoculars, true, false, false, HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);
        frame = view.Tick(binoculars, false, true, false, frame.TanX, frame.ViewModelTanX);

        frame = view.Tick(binoculars, true, false, false, frame.TanX, frame.ViewModelTanX);

        Assert.False(view.IsOpen);
        Assert.True(view.ClosedThisTick);
        Assert.False(view.HasOpticsState);
        Assert.Equal(HumanView.DefaultTanX, frame.TanX);
        Assert.False(view.ZoomSteppingThisTick);
    }

    [Fact]
    public void BinocularZoomInFractionTracksTheTangentBetweenDefaultAndMinimum()
    {
        // The remastered detector's range scales with this.
        BinocularsView view = new();
        WeaponType binoculars = Binoculars;
        view.Tick(binoculars, togglePressed: true, false, false, HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);

        Assert.Equal(0f, view.ZoomInFraction(Default), 3);
        Assert.Equal(1f, view.ZoomInFraction(Narrowest), 3);
        Assert.Equal(0.5f, view.ZoomInFraction((Default + Narrowest) / 2f), 3);
        Assert.Equal(0f, view.ZoomInFraction(Widest), 3);
    }

    [Fact]
    public void BinocularZoomInFractionIsZeroWithoutABoundBinocular()
    {
        BinocularsView view = new();
        WeaponType binoculars = Binoculars;
        Assert.Equal(0f, view.ZoomInFraction(0.3f), 3);
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
