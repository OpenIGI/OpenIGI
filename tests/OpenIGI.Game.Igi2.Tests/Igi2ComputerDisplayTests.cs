using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Computer;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

public sealed class Igi2ComputerDisplayTests
{
    private static (Igi2Computer Computer, Igi2ComputerDisplay Display) Open(Igi2ComputerMenu screen)
    {
        Igi2Computer computer = new() { StartupMode = screen };
        computer.Open();
        Igi2ComputerDisplay display = new(computer);
        display.Open(() => default);
        return (computer, display);
    }

    private static void Tick(Igi2Computer computer, Igi2ComputerDisplay display, Igi2ComputerInput input)
    {
        computer.Run();
        display.Run(input, null, null);
    }

    [Fact]
    public void TheMenuListsTheOtherScreensFromTheBottomUpAndLightsTheTop()
    {
        (_, Igi2ComputerDisplay display) = Open(Igi2ComputerMenu.Map);

        Assert.Equal([4, 2, 3], display.Items.Items.Select(static i => i.Id));
        Assert.True(display.Items.Items[0].Y > display.Items.Items[2].Y);
        Assert.Equal(2, display.Items.Find(3)!.LinkDown);
        Assert.Equal(4, display.Items.Find(3)!.LinkUp);
        Assert.Equal(3, display.Items.Find(4)!.LinkDown);
    }

    [Fact]
    public void TheDownArrowMovesTheLightAndEnterTurnsToItsScreen()
    {
        (Igi2Computer computer, Igi2ComputerDisplay display) = Open(Igi2ComputerMenu.Map);
        Tick(computer, display, new Igi2ComputerInput(0f, 0f));
        Assert.Equal(3, display.Items.Active?.Id);

        Tick(computer, display, new Igi2ComputerInput(0f, 0f, Down: true));
        Tick(computer, display, new Igi2ComputerInput(0f, 0f));
        Assert.Equal(2, display.Items.Active?.Id);

        Tick(computer, display, new Igi2ComputerInput(0f, 0f, Enter: true));
        Tick(computer, display, new Igi2ComputerInput(0f, 0f, Enter: true));
        Tick(computer, display, new Igi2ComputerInput(0f, 0f));
        Assert.Equal(Igi2ComputerMenu.Objectives, computer.ActiveMenu);
        Assert.Equal(Igi2ComputerMenu.Objectives, display.Screen);
    }

    [Fact]
    public void ThePointerMovesByTwiceTheLookChannelsAndStaysInsideTheGlass()
    {
        (Igi2Computer computer, Igi2ComputerDisplay display) = Open(Igi2ComputerMenu.Log);
        Tick(computer, display, new Igi2ComputerInput(0.1f, 0f));
        Assert.Equal(320f + (0.2f * 320f), display.PointerX, 3);

        Tick(computer, display, new Igi2ComputerInput(-5f, -5f));
        Assert.Equal(80f, display.PointerX);
        Assert.Equal(40f, display.PointerY, 3);
    }

    [Fact]
    public void TheMapZoomsOnAWheelTurnAndDrawsItsNoiseFromTheRunStream()
    {
        (Igi2Computer computer, Igi2ComputerDisplay display) = Open(Igi2ComputerMenu.Map);
        IgiRandom random = new(7u);
        IgiRandom expected = new(7u);
        Tick(computer, display, new Igi2ComputerInput(0f, 0f));
        display.Run(new Igi2ComputerInput(0f, 0f, WheelUp: true), null, random);

        Assert.Equal(1, display.Map!.ZoomLevel);
        float noise = (float)((expected.NextDouble() * 0.05000000074505806) + 0.949999988079071);
        uint timer = (expected.NextRaw() % 75u) + 15u;
        Assert.Equal((int)timer - 1, display.Map.NoiseTimer);
        Assert.Equal(noise - 0.033333335f, display.Map.Noise, 5);
    }
}
