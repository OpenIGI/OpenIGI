using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Computer;
using OpenIGI.Game.Igi1.Computer;
using Xunit;

namespace OpenIGI.Game.Igi1.Tests.Computer;

public sealed class Igi1ComputerTests
{
    private const float Width = 640f;
    private const float Height = 480f;

    [Fact]
    public void ThePointerStartsInTheMiddleAndMovesByHalfTheDisplayForAWholeChannel()
    {
        Igi1Computer computer = Open(out _);

        Assert.Equal(320f, computer.PointerX);
        Assert.Equal(240f, computer.PointerY);

        computer.Run(new Igi1ComputerInput(0.25f, 0.125f, false, 0), paused: false);

        Assert.Equal(400f, computer.PointerX);
        Assert.Equal(270f, computer.PointerY);
    }

    [Fact]
    public void ThePointerUndoesTheProfilesInversion()
    {
        Igi1Computer computer = Open(out _);

        // The channel arrives inverted; the menu turns it back.
        computer.Run(new Igi1ComputerInput(0f, -0.125f, true, 0), paused: false);

        Assert.Equal(270f, computer.PointerY);
    }

    [Fact]
    public void ThePointerIsHeldInsideTheDisplaysMargins()
    {
        Igi1Computer computer = Open(out _);

        computer.Run(new Igi1ComputerInput(-5f, -5f, false, 0), paused: false);
        // The margin's constant (0x1.8cccccp-5) is a hair under 31/640, so the edge is too.
        Assert.Equal(31f, computer.PointerX, 3);
        Assert.Equal(26f, computer.PointerY, 3);

        computer.Run(new Igi1ComputerInput(5f, 5f, false, 0), paused: false);
        Assert.Equal(611f, computer.PointerX, 3);
        Assert.Equal(434f, computer.PointerY, 3);
    }

    [Fact]
    public void NothingRunsWhileThePauseHoldsTheGame()
    {
        Igi1Computer computer = Open(out _);

        computer.Run(new Igi1ComputerInput(0.25f, 0f, false, 0), paused: true);

        Assert.Equal(320f, computer.PointerX);
    }

    [Fact]
    public void TheMapOpensAtItsWidestZoomOverThePlayer()
    {
        Igi1Computer computer = Open(out FakeWorld world);

        ComputerMap map = Assert.IsType<ComputerMap>(computer.Screen);
        Assert.Equal(0, computer.Zoom);
        Assert.Equal((float)Math.Tan(ComputerMap.ZoomAngles[0]), map.FieldOfView);

        // Over the player, hung 225 metres above the point.
        Assert.Equal(world.PlayerAt.Z + ComputerMap.Back, computer.MapPosition.Z, 3);
        Assert.Equal(world.PlayerAt.X, computer.MapPosition.X, 3);
    }

    [Fact]
    public void WithoutTheMapTheComputerOpensOnItsObjectives()
    {
        FakeWorld world = new();
        Igi1Computer computer = new(world) { MapEnabled = false };

        computer.Open(world.Player, Width, Height);

        Assert.IsType<ComputerObjectivesPage>(computer.Screen);
        Assert.False(computer.ShowsDevice);
    }

    [Fact]
    public void TheBootBoxBringsTheMapUpLineByLine()
    {
        Igi1Computer computer = Open(out _);
        ComputerMap map = (ComputerMap)computer.Screen!;
        Assert.Equal(ComputerMap.Layers.None, map.Shown);

        // INITIALIZING is held nine ticks: it ends on the tenth, when everything but the vehicles and cameras comes up.
        Tick(computer, 9);
        Assert.Equal(ComputerMap.Layers.None, map.Shown);
        Tick(computer, 1);
        Assert.Equal((ComputerMap.Layers)0x15, map.Shown);
        Assert.Equal(1, map.Startup.Step);

        // CONNECTING 4.5 ends on its fifth, DOWNLOADING 9 on its tenth, with every layer.
        Tick(computer, 5);
        Assert.Equal(2, map.Startup.Step);
        Tick(computer, 10);
        Assert.Equal((ComputerMap.Layers)0x1F, map.Shown);
        Assert.False(map.Startup.IsRunning);
        Assert.True(map.Startup.IsDone);
    }

    [Fact]
    public void PlusZoomsOnThePressAloneAndRestartsTheBootBox()
    {
        Igi1Computer computer = Open(out FakeWorld world);
        ComputerMap map = (ComputerMap)computer.Screen!;
        Tick(computer, 30);
        PointAt(computer, 7);

        world.Sounds.Clear();
        Press(computer, Igi1Computer.MouseLeft);
        Assert.Equal(1, computer.Zoom);
        Assert.Equal(["mapcomp_01", "mapcomp_05", "mapcomp_03"], world.Sounds);
        Assert.Equal(ComputerMap.Layers.None, map.Shown);
        Assert.True(map.Startup.IsRunning);
        Assert.Equal(0, map.Startup.Step);

        // Held, it does not zoom again.
        Press(computer, Igi1Computer.MouseLeft);
        Assert.Equal(1, computer.Zoom);
    }

    [Fact]
    public void AZoomFlaresTheInterferenceFromTheGamesStream()
    {
        Igi1Computer computer = Open(out FakeWorld world);
        ComputerMap map = (ComputerMap)computer.Screen!;
        PointAt(computer, 7);

        IgiRandom expected = IgiRandom.Seeded();
        Press(computer, Igi1Computer.MouseLeft);

        // The flare's two draws, then the tick's fade.
        float level = (float)((expected.NextDouble() * 0.05000000074505806) + 0.949999988079071);
        expected.NextBounded(75);
        Assert.Equal((float)(level - 0.03333333507180214), map.Noise, 5);
    }

    [Fact]
    public void TheWheelZoomsOnceANotch()
    {
        Igi1Computer computer = Open(out _);
        computer.Run(new Igi1ComputerInput(0f, 0f, false, Igi1Computer.WheelUp), paused: false);
        Assert.Equal(1, computer.Zoom);

        computer.Run(new Igi1ComputerInput(0f, 0f, false, Igi1Computer.WheelUp), paused: false);
        Assert.Equal(1, computer.Zoom);

        computer.Run(new Igi1ComputerInput(0f, 0f, false, 0), paused: false);
        computer.Run(new Igi1ComputerInput(0f, 0f, false, Igi1Computer.WheelDown), paused: false);
        Assert.Equal(0, computer.Zoom);
    }

    [Fact]
    public void ASecondClickWithinThirteenTicksZoomsIn()
    {
        Igi1Computer computer = Open(out _);
        Press(computer, Igi1Computer.MouseLeft);
        Release(computer);
        Tick(computer, 10);
        Press(computer, Igi1Computer.MouseLeft);

        Assert.Equal(1, computer.Zoom);
    }

    [Fact]
    public void ASecondClickAfterTheWindowIsAnotherFirstClick()
    {
        Igi1Computer computer = Open(out _);
        Press(computer, Igi1Computer.MouseLeft);
        Release(computer);
        Tick(computer, 13);
        Press(computer, Igi1Computer.MouseLeft);

        Assert.Equal(0, computer.Zoom);
    }

    [Fact]
    public void AnArrowPansWhileHeldAndScalesWithTheFieldOfView()
    {
        Igi1Computer computer = Open(out _);
        PointAt(computer, 3);
        Vector3d before = computer.MapPosition;

        Press(computer, Igi1Computer.MouseLeft);
        double step = Math.Pow(Math.Tan(ComputerMap.ZoomAngles[0]), 0.7f) * ComputerMap.Pan;
        Assert.Equal(before.Y + step, computer.MapPosition.Y, 1);
        Press(computer, Igi1Computer.MouseLeft);
        Assert.Equal(before.Y + (2 * step), computer.MapPosition.Y, 1);
    }

    [Fact]
    public void ATabSwapsTheScreenAtTheStartOfTheNextTick()
    {
        Igi1Computer computer = Open(out _);
        PointAt(computer, 1);
        Press(computer, Igi1Computer.MouseLeft);
        Assert.IsType<ComputerMap>(computer.Screen);

        Release(computer);
        Assert.IsType<ComputerObjectivesPage>(computer.Screen);
    }

    [Fact]
    public void TheSelectionLagsThePointerByATick()
    {
        Igi1Computer computer = Open(out _);
        ComputerItem plus = computer.Panel.Items.Single(item => item.Id == 7);
        MoveTo(computer, plus.X + 1f, plus.Y + 1f);
        Assert.False(plus.IsSelected);
        Assert.True(plus.IsPicked);

        computer.Run(default, paused: false);
        Assert.True(plus.IsSelected);
    }

    [Fact]
    public void ATextLinkDrawsItsColourOneFrameLate()
    {
        ComputerTextItem link = new(1, 0f, 0f, "font3.fnt", "LOG", (24f, 12f), 1, null);
        link.IsSelected = true;

        Assert.Equal(ComputerColors.Items[1], link.Draw());
        Assert.Equal((16f * 0.5f + 64f, 96f * 0.5f + 64f, 16f * 0.5f + 64f), link.Draw());
    }

    private static Igi1Computer Open(out FakeWorld world)
    {
        world = new FakeWorld();
        Igi1Computer computer = new(world);
        computer.Open(world.Player, Width, Height);
        return computer;
    }

    private static void Tick(Igi1Computer computer, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            computer.Run(default, paused: false);
        }
    }

    private static void Press(Igi1Computer computer, int buttons) =>
        computer.Run(new Igi1ComputerInput(0f, 0f, false, buttons), paused: false);

    private static void Release(Igi1Computer computer) => computer.Run(default, paused: false);

    // Puts the pointer on an item and lets the selection catch up.
    private static void PointAt(Igi1Computer computer, int id)
    {
        ComputerItem item = computer.Panel.Items.Single(candidate => candidate.Id == id);
        MoveTo(computer, item.X + 1f, item.Y + 1f);
        computer.Run(default, paused: false);
    }

    private static void MoveTo(Igi1Computer computer, float x, float y) =>
        computer.Run(new Igi1ComputerInput((x - computer.PointerX) / (Width * 0.5f), (y - computer.PointerY) / (Height * 0.5f), false, 0), paused: false);

    private sealed class FakeWorld : IIgi1ComputerWorld, IComputerFonts
    {
        public GameTask Player { get; } = new TestTask();

        public Vector3d PlayerAt { get; } = new(40960d, 81920d, 4096d);

        public List<string> Sounds { get; } = [];

        public IgiRandom Random { get; } = IgiRandom.Seeded();

        public IComputerFonts? Fonts => this;

        public int StringWidth(string font, string text) => text.Length * 8;

        public int LineHeight(string font) => 12;

        public (int Width, int Height) SpriteSize(string sprite) => (24, 24);

        public void PlaySound(string name) => Sounds.Add(name);

        public string? FindText(string resource) => resource;

        public bool TryDrop(Vector3d from, double depth, out Vector3d ground)
        {
            ground = default;
            return false;
        }

        public bool TryLine(Vector3d start, Vector3d finish, out Vector3d hit, out GameTask? struck)
        {
            hit = default;
            struck = null;
            return false;
        }

        public bool CanSee(Vector3d from, Vector3d point, GameTask target) => true;

        public IReadOnlyList<ComputerObject> Humans() => [];

        public IReadOnlyList<ComputerObject> Vehicles() => [];

        public IReadOnlyList<ComputerObject> Mines() => [];

        public IReadOnlyList<ComputerObject> Lenses() => [];

        public IReadOnlyList<ComputerObject> GunHolders() => [];

        public IReadOnlyList<ComputerHilight> Hilights() => [];

        public void FindObjects(int id, ICollection<ComputerObject> found)
        {
        }

        public bool TryLocate(GameTask task, out Vector3d at)
        {
            at = PlayerAt;
            return ReferenceEquals(task, Player);
        }

        public bool CanFollow(GameTask task) => false;

        public void SetOffMine(GameTask mine)
        {
        }

        public ComputerViewCone? ViewCone(GameTask lens) => null;

        public GameTask? TaskById(int id) => null;
    }

    private sealed class TestTask : GameTask
    {
    }
}
