using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Desktop.Tests.Completion;

/// <summary>The areas a mission is finished in with company: a vehicle driven there, or a companion escorted there.</summary>
internal sealed partial class MissionSolver
{
    // An area that must hold more than one human holds the player and a companion who follows it there. The solver does not
    // walk the companion over: it clears the way, every hostile soldier in the world first, and lets it follow.
    private Goal? Escort(ScriptExpression comparison)
    {
        if (comparison.Left is not { Kind: ScriptExpressionKind.Identifier } word
            || !word.Text.StartsWith("AreaActivate_", StringComparison.Ordinal)
            || !word.Text.EndsWith(".nActive", StringComparison.Ordinal)
            || comparison.Right is not { } count
            || new ScriptExpressionEvaluator(_context).EvaluateReal(count) < 2)
        {
            return null;
        }

        int team = _mission.Soldiers?.PlayerTeam ?? 0;
        foreach (SoldierView view in _mission.Soldiers?.SoldierViews ?? [])
        {
            if (!view.IsDead && view.Soldier.Team != team && ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                return new Goal(GoalKind.Kill, view.Soldier, $"{view.Soldier.Name} (clearing the way for {word.Text})");
            }
        }

        // A companion's own AI script decides where it goes, off counters the level advances as the two of them get there
        // (mission 10's Priboi: EditVariable_171, _174, _176, _173, _179). Each counter is worked on in the order the script
        // reads it; one that can only be waited for is done, or not yet due.
        if (!_escorting)
        {
            _escorting = true;
            try
            {
                foreach (SoldierView view in _mission.Soldiers?.SoldierViews ?? [])
                {
                    if (view.IsDead || view.Soldier.Team != team)
                    {
                        continue;
                    }

                    // A companion on the move is walking the leg it was just given: stay with it until it stops, as a player
                    // escorting it would, rather than set off the next step and leave it behind.
                    if (Moving(view))
                    {
                        return Goal.Wait($"{view.Soldier.Name} walking its leg");
                    }

                    if (_loop.Clock.TickCount % 1000 == 0)
                    {
                        Note($"companion {view.Soldier.Name} at {Fmt(view.Position)}, counters {string.Join(' ', CompanionCounters(view.Soldier).Select(c => $"{c[..c.IndexOf('.', StringComparison.Ordinal)]}={Value(c)}"))}");
                    }

                    foreach (string counter in CompanionCounters(view.Soldier))
                    {
                        if (NeedWord(counter, view.Soldier) is { Kind: not GoalKind.Wait } goal)
                        {
                            return goal;
                        }
                    }
                }
            }
            finally
            {
                _escorting = false;
            }
        }

        return null;
    }

    // Whether a companion has moved more than a metre in the last two seconds.
    private bool Moving(SoldierView view)
    {
        int now = _loop.Clock.TickCount;
        if (!_tracks.TryGetValue(view.Soldier, out Queue<(int Tick, Vector3d At)>? track))
        {
            _tracks[view.Soldier] = track = new();
        }

        if (track.Count == 0 || track.Last().Tick != now)
        {
            track.Enqueue((now, view.Position));
        }

        while (track.Count > 0 && track.Peek().Tick < now - 60)
        {
            track.Dequeue();
        }

        return track.Count > 1 && (track.Peek().At - view.Position).Length > 4096.0;
    }

    // The counters a companion's AI script reads, from missions/location0/level<N>/ai/<HumanAI id>.qvm.
    private IReadOnlyList<string> CompanionCounters(GameTask soldier)
    {
        if (_counters.TryGetValue(soldier, out IReadOnlyList<string>? known))
        {
            return known;
        }

        List<string> counters = [];
        if (Descendants(soldier).FirstOrDefault(t => t.Type.Name == "HumanAI") is { Id: >= 0 } ai
            && Environment.GetEnvironmentVariable("IGI_GAME_PATH") is { Length: > 0 } root)
        {
            string path = Path.Combine(root, "missions", "location0", $"level{_missionId}", "ai", $"{ai.Id}.qvm");
            if (File.Exists(path))
            {
                foreach (string? symbol in OpenIGI.Scripting.Qvm.QvmProgram.Read(File.ReadAllBytes(path)).SymbolNames)
                {
                    if (symbol is not null && symbol.StartsWith("EditVariable_", StringComparison.Ordinal) && symbol.EndsWith(".nValue", StringComparison.Ordinal))
                    {
                        counters.Add(symbol);
                    }
                }
            }
        }

        _counters[soldier] = counters;
        return counters;
    }

    // An area that counts vehicles is reached by a car the level drives (CarAI on its route): start the container of the first
    // car that is not driving yet, else wait for one to arrive.
    private Goal NeedVehicle(GameTask area, string word)
    {
        foreach (GameTask driver in Descendants(_tree.Root))
        {
            Goal? start = null;
            if (driver.Type.Name == "CarAI" && InactiveContainer(driver) is { } stopped)
            {
                start = NeedStart(stopped, area);
            }
            else if (driver is GameTask { Type.Name: "AnimTask" } anim
                && anim.TryGetScriptValue("TargetID", out object? target) && target is int id
                && _tree.FromId(id) is { Type.Name: "Car" or "Heli" })
            {
                start = InactiveContainer(anim) is { } closed ? NeedStart(closed, area) : NeedOn(anim);
            }

            // Whatever the player has to stand in to set a vehicle going, it stands in before the vehicle's destination.
            switch (start)
            {
                case { Kind: GoalKind.Stand } board:
                    return board with { Kind = GoalKind.Ride };
                case { Kind: not GoalKind.Wait }:
                    return start;
            }
        }

        return Goal.Wait($"{word} (a car to arrive)");
    }
}
