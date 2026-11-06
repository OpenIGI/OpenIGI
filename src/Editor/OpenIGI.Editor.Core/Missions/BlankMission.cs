using System.Globalization;
using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Editor.Core.Missions;

/// <summary>What the ground of a blank mission is.</summary>
public enum BlankGround
{
    /// <summary>The level's ground, as it shipped.</summary>
    Keep,

    /// <summary>Flat, at the player start's height.</summary>
    Flat,

    /// <summary>Rolling ground generated from a seed: the same seed always gives the same ground.</summary>
    Seeded,
}

/// <summary>
/// A blank mission on a level of Project I.G.I.: its ground, sky and light kept, the player with their kit, and the least a
/// mission needs to start and end, nothing else.
/// </summary>
/// <remarks>
/// <para>
/// What is kept is the level as a place: <c>EnvironmentalEffects</c> (sky, sun, light), the <c>TerrainModifiers</c> of
/// <c>Static</c> (height maps, texturing, ground lightmaps) and every declaration. Buildings, decoration, soldiers, cutscenes,
/// triggers and sounds go. The player keeps their task id, model and kit, standing where the level starts them.
/// </para>
/// <para>
/// To end properly it gets an exit: an area 50 m ahead of the player (along their heading), an objective that names it on the
/// map computer with the game's own line "Proceed to the extraction point." (<c>M2_OBJ3</c>), and <c>LevelFlow</c> completing
/// when the player stands in it, failing when they die. As the shipped levels end, each is announced first: "Mission
/// Complete!" (<c>MISSION_COMPLETE</c>) or "Mission Failed!" (<c>MISSION_FAILED</c>, with the fail sound), and the level
/// ends a second after the line has shown. Nothing in it is new to the original game, so a blank mission stays retail
/// compatible.
/// </para>
/// </remarks>
public static class BlankMission
{
    /// <summary>The objective line it uses: the original's own, so no text has to be added.</summary>
    public const string ObjectiveText = "M2_OBJ3";

    /// <summary>How far ahead of the player the exit is, in metres.</summary>
    public const double ExitDistanceMetres = 50;

    /// <summary>Engine units in a metre.</summary>
    public const double UnitsPerMetre = 4096;

    private static readonly string[] Needed = ["LevelFlow", "HumanPlayer", "AreaActivate", "DefineComputerObjective"];

    /// <summary>Makes the blank mission's object script from its level's.</summary>
    /// <param name="baseObjects">The level's <c>objects.qvm</c>.</param>
    /// <returns>The blank mission's document.</returns>
    /// <exception cref="ArgumentException">The level lacks what a blank mission keeps (a player, LevelFlow, the area and objective types).</exception>
    public static MissionDocument Make(ReadOnlyMemory<byte> baseObjects)
    {
        MissionDocument document = MissionDocument.Read(baseObjects);
        if (Needed.FirstOrDefault(t => !document.Declarations.ContainsKey(t)) is { } missing)
        {
            throw new ArgumentException($"This level does not declare {missing}, which a blank mission needs.", nameof(baseObjects));
        }

        MissionTask player = document.EnumerateTasks().FirstOrDefault(static t => t.TypeName == "HumanPlayer")
            ?? throw new ArgumentException("This level has no player to keep.", nameof(baseObjects));
        MissionTask flow = document.RootTasks.FirstOrDefault(static t => t.TypeName == "LevelFlow")
            ?? throw new ArgumentException("This level has no LevelFlow to end it.", nameof(baseObjects));

        // The player stands apart first, so clearing their old container does not take them.
        document.Remove(player);

        // The ground's own tasks are kept wherever a level put them: those outside TerrainModifiers go into it.
        MissionTask? modifiers = document.EnumerateTasks().FirstOrDefault(static t => t.Label == "TerrainModifiers");
        MissionTask[] stray = [.. document.EnumerateTasks().Where(t => IsGround(t) && !Within(t, modifiers))];
        foreach (MissionTask task in stray)
        {
            document.Remove(task);
        }

        if (modifiers is null && stray.Length > 0)
        {
            modifiers = new MissionTask("Container", "TerrainModifiers");
            document.Insert(modifiers, new TaskLocation(null, 0));
        }

        foreach (MissionTask task in stray)
        {
            document.Insert(task, new TaskLocation(modifiers, modifiers!.Children.Count));
        }

        // Everything else goes, but the way down to the ground's tasks: a container holding them keeps them, and loses the rest.
        void Prune(MissionTask container)
        {
            foreach (MissionTask child in container.Children.ToList())
            {
                if (ReferenceEquals(child, modifiers))
                {
                    continue;
                }

                if (modifiers is not null && Within(modifiers, child))
                {
                    Prune(child);
                }
                else
                {
                    document.Remove(child);
                }
            }
        }

        foreach (MissionTask root in document.RootTasks.ToList())
        {
            if (root.TypeName != "LevelFlow" && root.Label != "EnvironmentalEffects" && !ReferenceEquals(root, modifiers))
            {
                Prune(root);
            }
        }

        // The first emptied container holds the mission: the player, the exit and the objective.
        MissionTask holder = document.RootTasks.FirstOrDefault(static t => t is { TypeName: "Container" or "Dynamic" } && t.Label != "EnvironmentalEffects" && t.Children.Count == 0)
            ?? AddContainer(document);
        document.Insert(player, new TaskLocation(holder, holder.Children.Count));

        HashSet<int> used = [.. document.EnumerateTasks().Select(static t => t.Id)];
        int NextId()
        {
            int id = 100;
            while (used.Contains(id))
            {
                id++;
            }

            used.Add(id);
            return id;
        }

        MissionTask exit = New(document, "AreaActivate", "Exit", NextId());
        double[] start = Numbers(player, "Position") ?? [0, 0, 0];
        double heading = Numbers(player, "Gamma") is [double gamma, ..] ? gamma : 0;
        double ahead = ExitDistanceMetres * UnitsPerMetre;
        // Gamma 0 faces +Y and turns towards -X: the original walks a player of Gamma g along (-sin g, cos g).
        Set(document, exit, "Position", start[0] - (Math.Sin(heading) * ahead), start[1] + (Math.Cos(heading) * ahead), start[2]);
        Set(document, exit, "Dimensions", 8 * UnitsPerMetre, 8 * UnitsPerMetre, 6 * UnitsPerMetre);
        Text(document, exit, "Criteria", "CRITERIA_HUMAN0");
        document.Insert(exit, new TaskLocation(holder, holder.Children.Count));

        string reached = $"AreaActivate_{exit.Id.ToString(CultureInfo.InvariantCulture)}.nActive";
        MissionTask objective = New(document, "DefineComputerObjective", "Objectives", NextId());
        Text(document, objective, "Objective 1 Text Resource", ObjectiveText);
        Set(document, objective, "Objective 1 Link To Position", exit.Id);
        Text(document, objective, "Objective 1 Complete Expression", reached);
        foreach (MissionParameter link in objective.Parameters.Where(static p => p.Name.EndsWith("Link To Position", StringComparison.Ordinal) && !p.Name.StartsWith("Objective 1 ", StringComparison.Ordinal)))
        {
            Set(document, objective, link.Name, -1);
        }

        document.Insert(objective, new TaskLocation(holder, holder.Children.Count));

        string dead = $"HumanPlayer_{player.Id.ToString(CultureInfo.InvariantCulture)}.isDead";
        if (!document.Declarations.ContainsKey("StatusMessage"))
        {
            Text(document, flow, "Complete", reached);
            Text(document, flow, "Failed", dead);
            return document;
        }

        // Announced, then ended once the line has shown: level 3 waits on nTicksSinceFinishedDisplay the same way.
        double[] at = Numbers(exit, "Position") ?? start;
        MissionTask complete = Message(document, "Mission Complete", NextId(), at, reached, CompleteText, "message");
        MissionTask failed = Message(document, "Mission Failed", NextId(), at, dead, FailedText, "fail");
        document.Insert(complete, new TaskLocation(holder, holder.Children.Count));
        document.Insert(failed, new TaskLocation(holder, holder.Children.Count));
        Text(document, flow, "Complete", Shown(complete));
        Text(document, flow, "Failed", Shown(failed));
        return document;
    }

    /// <summary>The line it shows when the mission is complete: the original's own.</summary>
    public const string CompleteText = "MISSION_COMPLETE";

    /// <summary>The line it shows when the mission is failed: the original's own.</summary>
    public const string FailedText = "MISSION_FAILED";

    // A status message as the shipped levels write one: sent once, not a cutscene line, two seconds on screen.
    private static MissionTask Message(MissionDocument document, string label, int id, double[] at, string send, string text, string sound)
    {
        MissionTask message = New(document, "StatusMessage", label, id);
        Set(document, message, "Position", at[0], at[1], at[2]);
        Text(document, message, "Send", send);
        Text(document, message, "Text", text);
        Text(document, message, "Sound", sound);
        Set(document, message, "Is send once", 1);
        Set(document, message, "Cutscene message", 0);
        Set(document, message, "Duration", 2);
        return message;
    }

    private static string Shown(MissionTask message) =>
        $"StatusMessage_{message.Id.ToString(CultureInfo.InvariantCulture)}.nTicksSinceFinishedDisplay > GAME_FREQUENCY";

    /// <summary>The level files a blank mission takes: all but its per-soldier AI scripts, which name tasks it no longer has.</summary>
    /// <param name="relativePath">A file's path inside the level's folder.</param>
    /// <returns>Whether the blank mission keeps it.</returns>
    public static bool Keeps(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        string path = relativePath.Replace('\\', '/');
        return !(path.StartsWith("ai/", StringComparison.OrdinalIgnoreCase) && !path.EndsWith("/settings.qvm", StringComparison.OrdinalIgnoreCase))
            && !path.StartsWith("graphs/", StringComparison.OrdinalIgnoreCase);
    }

    // The tasks that make the ground what it is: its height maps, texturing and lightmaps.
    private static bool IsGround(MissionTask task) => task.TypeName is "HeightMap" or "TerrainLightMap" or "TextureModifier";

    private static bool Within(MissionTask task, MissionTask? container)
    {
        for (MissionTask? at = task.Parent; at is not null; at = at.Parent)
        {
            if (ReferenceEquals(at, container))
            {
                return true;
            }
        }

        return false;
    }

    private static MissionTask AddContainer(MissionDocument document)
    {
        MissionTask container = new("Container", "Mission");
        document.Insert(container, new TaskLocation(null, document.RootTasks.Count()));
        return container;
    }

    private static MissionTask New(MissionDocument document, string type, string label, int id)
    {
        MissionTask task = new(type, label, id);
        document.ApplyDeclaration(task);
        return task;
    }

    private static double[]? Numbers(MissionTask task, string parameter) =>
        task.FindParameter(parameter) is { } found && found.TryGetNumbers(out double[] values) ? values : null;

    private static void Set(MissionDocument document, MissionTask task, string parameter, params double[] values)
    {
        if (task.FindParameter(parameter) is { } found)
        {
            document.SetArguments(task, parameter, found.WithNumbers(values));
        }
    }

    private static void Text(MissionDocument document, MissionTask task, string parameter, string text)
    {
        if (task.FindParameter(parameter) is not null)
        {
            document.SetArguments(task, parameter, [new QscStringLiteral(text)]);
        }
    }
}
