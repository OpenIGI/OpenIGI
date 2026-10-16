using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Computer;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Player;

/// <content>
/// The open map computer: its screens run after the player as the game screen's task, on the raw keys and the mouse, and while
/// the map is shown the player looks through its camera.
/// </content>
public sealed partial class Igi2TitlePlayer
{
    private TitleRawKeys _rawKeys;
    private Igi2ComputerMapWorld? _mapWorld;

    /// <summary>Gets the open computer's screens, or <see langword="null"/> while the level has no computer.</summary>
    public Igi2ComputerDisplay? ComputerDisplay { get; private set; }

    /// <summary>Gets a value indicating whether the computer's screens cover the view.</summary>
    public bool IsComputerShown => ComputerDisplay is { IsOpen: true };

    /// <summary>Gets a value indicating whether the player looks through the map's camera.</summary>
    public bool IsMapShown => ComputerDisplay is { IsMapShown: true, Map: not null };

    /// <summary>Gets or sets the level's humans, which the map can follow.</summary>
    internal IReadOnlyList<Igi2Human> MapHumans { get; set; } = [];

    /// <summary>Gets or sets the level's tasks by id, which the objectives' map buttons look at.</summary>
    internal Func<int, GameTask?>? TaskById { get; set; }

    /// <summary>Gets or sets the mission's number, which picks the broken casing for mission 17.</summary>
    public int MissionId { get; set; }

    // The game screen's ComputerMenu task, after the player: it opens as the player's computer state opens the computer, runs,
    // and closes with it.
    private void RunComputerDisplay()
    {
        if (Computer is not { } computer)
        {
            return;
        }

        Igi2ComputerDisplay display = ComputerDisplay ??= new Igi2ComputerDisplay(computer)
        {
            Strings = key => Strings?.Get(key) ?? key,
            PlaySound = name => Sounds.New(name, EyePosition, () => EyePosition),
        };
        display.IsBroken = MissionId == 17;
        if (computer.IsOpen && !display.IsOpen)
        {
            display.Open(() => Human.Position);
        }
        else if (!computer.IsOpen && display.IsOpen)
        {
            display.Close();
        }

        if (!display.IsOpen)
        {
            return;
        }

        TitleRawKeys raw = _rawKeys;
        _mapWorld ??= new Igi2ComputerMapWorld(this);
        display.Run(
            new Igi2ComputerInput(
                Human.Channels[Igi2HumanChannel.LookX],
                Human.Channels[Igi2HumanChannel.LookY],
                Human.InvertMouse,
                (raw & TitleRawKeys.MouseLeft) != 0,
                (raw & TitleRawKeys.MouseRight) != 0,
                (raw & TitleRawKeys.WheelUp) != 0,
                (raw & TitleRawKeys.WheelDown) != 0,
                (raw & TitleRawKeys.Up) != 0,
                (raw & TitleRawKeys.Down) != 0,
                (raw & TitleRawKeys.Left) != 0,
                (raw & TitleRawKeys.Right) != 0,
                (raw & TitleRawKeys.Enter) != 0,
                (raw & TitleRawKeys.Home) != 0,
                (raw & TitleRawKeys.Control) != 0),
            _mapWorld,
            Human.Random);
    }

    // What the map asks of the level, from the player's collision world, the level's humans and its tasks.
    private sealed class Igi2ComputerMapWorld(Igi2TitlePlayer player) : IIgi2ComputerMapWorld
    {
        private readonly Igi2LineTrace _line = new(1);

        public IEnumerable<(Vector3d Position, IIgi2CameraTarget? Followable)> Humans
        {
            get
            {
                foreach (Igi2Human human in player.MapHumans)
                {
                    // Human_ComputerMapFollowQTaskEventHandler: only a friend, team one, is followed.
                    yield return (human.Position, human.Team == 1 ? new Target(() => human.Position) : null);
                }
            }
        }

        public Vector3d? GroundBelow(Vector3d from, double distance)
        {
            if (player.CollisionWorld is not { } world)
            {
                return null;
            }

            _line.Starts[0] = from;
            world.NextZ(_line, [distance], Igi2CollisionWorld.TerrainGroup);
            return _line.Intersections[0].IsHit ? _line.Intersections[0].Position : null;
        }

        public (Vector3d Position, bool IsHit, IIgi2CameraTarget? Followable) Trace(Vector3d start, Vector3d finish)
        {
            if (player.CollisionWorld is not { } world)
            {
                return (finish, false, null);
            }

            _line.Starts[0] = start;
            _line.Ends[0] = finish;
            world.TraceLines(_line, 0xFFFFFFFFu);
            Igi2Intersection hit = _line.Intersections[0];
            IIgi2CameraTarget? followable = hit.HitObject?.Owner switch
            {
                Igi2Human { Team: 1 } human => new Target(() => human.Position),
                Vehicles.Igi2CarTask car => new Target(() => car.Position),
                _ => null,
            };
            return (hit.Position, hit.IsHit, followable);
        }

        public IIgi2CameraTarget? Task(int id) => player.TaskById?.Invoke(id) switch
        {
            IIgi2CameraTarget target => target,
            Igi2HumanPlayerTask => new Target(() => player.Human.Position),
            Ai.Igi2HumanSoldierTask { Body: { } soldier } => new Target(() => soldier.Position),
            GameTask task when task.TryGetScriptValue("Position", out object? value) && value is Vector3d position
                => new Target(() => position),
            _ => null,
        };
    }

    // Where something stands, as the map looks at it.
    private sealed class Target(Func<Vector3d> position) : IIgi2CameraTarget
    {
        private static readonly float[] Identity = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];

        public Vector3d CameraTargetPosition => position();

        public IReadOnlyList<float> CameraTargetOrientation => Identity;
    }
}
