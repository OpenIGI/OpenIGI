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

    private sealed class FakeSaves : IIgi2ComputerSaves
    {
        public List<string> Calls { get; } = [];

        public bool[] Used { get; } = new bool[5];

        public float BatteryLevel => 1f;

        public bool IsSaveAllowed => true;

        public int QuickLoadSlot => 0;

        public (bool IsUsed, string Info, string When) Slot(int slot) => (Used[slot], "Mission Time: 0:01:00", "(today)");

        public void BeginSave(int slot) => Calls.Add($"begin {slot}");

        public void FinishSave(int slot) => Calls.Add($"finish {slot}");

        public void Load(int slot) => Calls.Add($"load {slot}");

        public void FreezeFire() => Calls.Add("freeze");

        public void QuitComputer() => Calls.Add("quit");
    }

    [Fact]
    public void AnUploadGoesToTheFirstFreeSlotAndSavesWhenItsBarIsFull()
    {
        Igi2Computer computer = new() { StartupMode = Igi2ComputerMenu.Save };
        computer.Open();
        FakeSaves saves = new();
        saves.Used[0] = true;
        Igi2ComputerDisplay display = new(computer) { Saves = saves };
        display.Open(() => default);
        Tick(computer, display, new Igi2ComputerInput(0f, 0f));
        Tick(computer, display, new Igi2ComputerInput(0f, 0f));
        Assert.Equal(Igi2ComputerSaveState.Main, display.SaveState);
        Assert.Equal(5, display.Items.Active?.Id);

        Tick(computer, display, new Igi2ComputerInput(0f, 0f, Enter: true));
        Tick(computer, display, new Igi2ComputerInput(0f, 0f));
        Assert.Equal(Igi2ComputerSaveState.Uploading, display.SaveState);
        Assert.Equal(1, display.ChosenSlot);

        for (int i = 0; i < 101 && display.SaveState == Igi2ComputerSaveState.Uploading; i++)
        {
            Tick(computer, display, new Igi2ComputerInput(0f, 0f));
        }

        Assert.Equal(Igi2ComputerSaveState.Complete, display.SaveState);
        Assert.Equal(["begin 1"], saves.Calls);
        for (int i = 0; i < Igi2ComputerDisplay.CompleteTicks + 1; i++)
        {
            Tick(computer, display, new Igi2ComputerInput(0f, 0f));
        }

        Assert.Equal(["begin 1", "finish 1", "quit"], saves.Calls);
    }

    [Fact]
    public void TheLogShowsTheNewestLineFirstAndItsDownArrowScrollsOlder()
    {
        Igi2Computer computer = new() { StartupMode = Igi2ComputerMenu.Log };
        computer.Open();
        Igi2ComputerDisplay display = new(computer) { Log = [.. Enumerable.Range(0, 20).Select(static i => $"line{i}")] };
        display.Open(() => default);
        List<(int X, int Y)> rules = [];
        Assert.StartsWith("line19", display.LayoutLog(rules)[0].Text, StringComparison.Ordinal);
        Assert.Equal(Igi2ComputerDisplay.LogLines, rules.Count);

        Igi2ComputerMenuItem down = display.Items.Find(Igi2ComputerDisplay.LogDownId)!;
        int x = (int)down.X + 4, y = (int)down.Y + 4;
        Tick(computer, display, new Igi2ComputerInput(0f, 0f));
        display.HoldPointer(x, y);
        Tick(computer, display, new Igi2ComputerInput(0f, 0f, MouseLeft: true));
        Tick(computer, display, new Igi2ComputerInput(0f, 0f));
        Assert.Equal(1, display.LogScroll);
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
