using System.Globalization;
using System.Text;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The authored patrol paths of every retail level, read out of the object trees.
/// </summary>
/// <remarks>
/// A patrol path is a <c>PatrolPath</c> task whose child <c>PatrolPathCommand</c> tasks are its steps, so
/// these are data-driven checks of what the game actually ships rather than of a sample. See
/// the original.
/// </remarks>
public sealed class RetailPatrolPathTests
{
    private const int Levels = 14;

    /// <summary>Every level declares the three AI types with exactly the parameters the port transcribes.</summary>
    /// <remarks>
    /// Parameter lists are per-script (M5), so this has to hold for all fourteen independently — and
    /// registering the classes at all would throw on the first level that disagreed.
    /// </remarks>
    [RequiresGameDataFact]
    public void EveryLevelDeclaresTheAiTypesTheSameWay()
    {
        for (int id = 1; id <= Levels; id++)
        {
            Fixture fixture = Fixture.Load(id);

            TaskType path = fixture.Types.Get(AiTaskTypes.PatrolPath);
            Assert.True(path.IsDeclared, $"level {id} never declared PatrolPath");
            Assert.Empty(path.Parameters);

            TaskType command = fixture.Types.Get(AiTaskTypes.PatrolPathCommand);
            Assert.Collection(
                command.Parameters,
                p => AssertParameter(p, AiPatrolCommand.KindParameter, ScriptParamType.Int32),
                p => AssertParameter(p, AiPatrolCommand.OperandParameter, ScriptParamType.Int32));

            TaskType ai = fixture.Types.Get(AiTaskTypes.HumanAi);
            Assert.Collection(
                ai.Parameters,
                p => AssertParameter(p, "AI Type", ScriptParamType.String32),
                p => AssertParameter(p, "Graph ID", ScriptParamType.Int32));
        }
    }

    /// <summary>Every <c>HumanAI</c> in the game names one of the twenty-nine registered types.</summary>
    /// <remarks>
    /// This is what binds an AI to one of the nine archetype scripts. A name the engine does not register
    /// would resolve to nothing and the soldier would run no behaviour at all, so a single failure here
    /// would be a real gap rather than a cosmetic one.
    /// </remarks>
    [RequiresGameDataFact]
    public void EveryAiNamesARegisteredType()
    {
        int brains = 0;
        HashSet<AiType> seen = [];

        for (int id = 1; id <= Levels; id++)
        {
            foreach (HumanAiTask ai in Fixture.Load(id).Brains)
            {
                brains++;
                AiType? type = ai.ResolvedType;
                Assert.True(
                    type.HasValue,
                    $"level {id} has a HumanAI naming the unregistered type '{ai.AiTypeName}'");
                seen.Add(type!.Value);
                Assert.NotNull(ai.Archetype);
            }
        }

        Assert.True(brains > 200, $"expected the game's soldiers, saw {brains}");
        Assert.NotEmpty(seen);
    }

    /// <summary>The paths a human walker could not execute exist, and only in the levels that have vehicles.</summary>
    /// <remarks>
    /// <para>
    /// <c>PATROLPATHCOMMAND_SETSPEED</c> has no arm in the human walker <c>0x48E920</c>, whose
    /// <c>default:</c> returns without advancing the cursor — so a human patrol reaching one stops forever
    /// (§7.5). <b>I expected the data to avoid it and it does not</b>: 114 <c>SETSPEED</c> commands ship.
    /// </para>
    /// <para>
    /// What keeps them harmless is not that they are absent but <em>whose</em> paths they are. Fifteen of the
    /// 627 paths carry one, spread over six levels — 6, 7, 8, 9, 10 and 14 — and five of those six
    /// instantiate <c>CarAI</c>. <b>Level 9 is the exception</b>: it ships one such path and has no
    /// <c>CarAI</c> anywhere, so that path is an orphan no task in the level can walk. Which means the wedge
    /// is unreachable because of how the levels are authored, not because the engine prevents it.
    /// </para>
    /// </remarks>
    [RequiresGameDataFact]
    public void ThePathsAHumanCouldNotWalkAreConfinedToSixLevels()
    {
        // Levels 6, 7, 8, 10 and 14 instantiate CarAI; level 9's is an orphan with no vehicle to walk it.
        int[] expected = [6, 7, 8, 9, 10, 14];
        List<int> found = [];
        int vehiclePaths = 0;

        for (int id = 1; id <= Levels; id++)
        {
            foreach (AiPatrolPath path in Fixture.Load(id).Paths.Paths)
            {
                if (path.IsWalkableByAHuman)
                {
                    continue;
                }

                vehiclePaths++;
                if (!found.Contains(id))
                {
                    found.Add(id);
                }
            }
        }

        Assert.Equal(expected, found);
        Assert.Equal(15, vehiclePaths);
    }

    /// <summary>Writes a census of every patrol path in the game, and pins what it found.</summary>
    /// <remarks>
    /// The figures quoted in <c>ai.md</c> §7.8 come from this test. It is written as a corpus sweep rather
    /// than a sample because M5 and M6 both had bugs that only a full sweep caught.
    /// </remarks>
    [RequiresGameDataFact]
    public void TheCensusOfEveryPatrolPathHolds()
    {
        var kinds = new Dictionary<AiPatrolCommandKind, int>();
        int paths = 0;
        int commands = 0;
        int withEnd = 0;
        int endFirst = 0;
        int movers = 0;
        var perLevel = new StringBuilder();

        for (int id = 1; id <= Levels; id++)
        {
            AiPatrolPathRegistry registry = Fixture.Load(id).Paths;
            int levelMovers = 0;
            int levelSetSpeed = registry.Paths.Count(p => !p.IsWalkableByAHuman);

            foreach (AiPatrolPath path in registry.Paths)
            {
                paths++;
                commands += path.Commands.Count;

                int end = path.EndIndex;
                if (end >= 0)
                {
                    withEnd++;
                    if (end == 0)
                    {
                        endFirst++;
                    }
                }

                bool moves = false;
                foreach (AiPatrolCommand command in path.Commands)
                {
                    kinds[command.Kind] = kinds.GetValueOrDefault(command.Kind) + 1;
                    moves |= command.Kind.IsMovement();
                }

                if (moves)
                {
                    movers++;
                    levelMovers++;
                }
            }

            perLevel.AppendLine(CultureInfo.InvariantCulture, $"level {id}: {registry.Count} paths, {levelMovers} move, {levelSetSpeed} vehicle-only");
        }

        string census = BuildReport(perLevel, paths, commands, withEnd, endFirst, movers, kinds);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "openigi-patrol-path-census.txt"), census);

        Assert.True(paths > 0, "no patrol paths were found at all");
        Assert.Equal(commands, kinds.Values.Sum());

        // The measured corpus. These are the figures ai.md 7.8 quotes.
        Assert.Equal(627, paths);
        Assert.Equal(3877, commands);
        Assert.Equal(596, movers);

        // Only a third of paths carry an END, and only six put it first -- so the "loops once" behaviour
        // 7.3 reads applies to a minority, and the majority wrap forever off the first-command fallback.
        Assert.Equal(206, withEnd);
        Assert.Equal(6, endFirst);

        // No path may contain a kind outside the registered family.
        foreach (AiPatrolCommandKind kind in kinds.Keys)
        {
            Assert.True(Enum.IsDefined(kind), $"a path carries the unregistered command kind {(int)kind}");
        }

        Assert.False(kinds.ContainsKey(AiPatrolCommandKind.None));
    }

    private static string BuildReport(
        StringBuilder perLevel,
        int paths,
        int commands,
        int withEnd,
        int endFirst,
        int movers,
        Dictionary<AiPatrolCommandKind, int> kinds)
    {
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"paths                 {paths}");
        report.AppendLine(CultureInfo.InvariantCulture, $"commands              {commands}");
        report.AppendLine(CultureInfo.InvariantCulture, $"paths with an END     {withEnd}");
        report.AppendLine(CultureInfo.InvariantCulture, $"END is command 0      {endFirst}");
        report.AppendLine(CultureInfo.InvariantCulture, $"paths that move       {movers}");
        report.AppendLine();

        foreach ((AiPatrolCommandKind kind, int count) in kinds.OrderBy(p => (int)p.Key))
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"{(int)kind,3} {kind,-12} {count}");
        }

        report.AppendLine();
        report.Append(perLevel);
        return report.ToString();
    }

    private static void AssertParameter(TaskParameter parameter, string name, ScriptParamType type)
    {
        Assert.Equal(name, parameter.Name);
        Assert.Equal(type, parameter.Type);
    }

    private sealed record Fixture(TaskTypeRegistry Types, AiPatrolPathRegistry Paths, IReadOnlyList<HumanAiTask> Brains)
    {
        public static Fixture Load(int missionId)
        {
            VirtualFileSystem fileSystem = new();
            fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
            ResourceRegistry resources = new(fileSystem);

            GameLoop loop = new(new ManualTimeSource());
            PlayerTaskTypes.RegisterAll(loop.Types);
            AiTaskTypes.RegisterAll(loop.Types);

            LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));

            MissionRegistry registry = new();
            registry.Register(loop.Natives);
            registry.Scan(fileSystem, resources, loop.Natives);

            LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
            MissionLoader loader = new(registry, fileSystem, resources, levelLoader);
            loader.Load(missionId);

            List<HumanAiTask> brains = [];
            CollectBrains(levelRoot, brains);
            return new Fixture(loop.Types, AiPatrolPathRegistry.Build(levelRoot), brains);
        }

        private static void CollectBrains(GameTask task, List<HumanAiTask> into)
        {
            if (task is HumanAiTask brain)
            {
                into.Add(brain);
            }

            foreach (GameTask child in task.Children)
            {
                CollectBrains(child, into);
            }
        }
    }
}
