using System.Globalization;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Input;

namespace OpenIGI.Host;

/// <summary>
/// Debugging tools for a co-op session: a trace of what this game draws against what its server had (<see cref="NetProbe"/>),
/// the player turned on a soldier and its trigger held, the server's own <c>dbg</c> commands, and a script of all of it on a
/// clock. <b>Temporary: tools for finding what makes the others jump, not part of the game.</b>
/// </summary>
public sealed partial class GameHost
{
    private const string ProbeCategory = "NetProbe";

    // The input the tools steer the player with while they shoot or walk, in place of the window's.
    private readonly InputState _probeInput = new();

    // The console the script's lines are run on.
    private ConsoleSystem? _probeConsole;

    // The soldier being shot and until when, and until when the player walks; on the probe's clock, in seconds.
    private string? _probeTarget;
    private double _probeShootUntil;
    private double _probeWalkUntil;

    // A script's lines and the second each runs at, from when the session is in its level; and when that was.
    private readonly List<(double At, string Line)> _probeScript = [];
    private double? _probeScriptStart;

    private double ProbeSeconds => _loop.TimeSource.Milliseconds / 1000.0;

    private static string ProbeDirectory => Path.GetDirectoryName(SettingsPaths.GetSettingsPath()) ?? SettingsPaths.PortableDirectory;

    private void RegisterNetProbe(ConsoleSystem console)
    {
        _probeConsole = console;
        console.Commands.Add(
            "dbg_trace",
            "Debugging: writes what this game draws of the others every frame, for tools/netprobe/compare.py: on, off, mark <text>.",
            context => context.Print(NetProbe.Command(server: TickObserver is Multiplayer.HostedSession, context.Arguments, ProbeDirectory, ProbeWhen())),
            "on|off|mark <text>");

        console.Commands.Add(
            "dbg_guards",
            "Debugging: lists the soldiers this game shows, nearest first: entity, where it draws them, alive, distance.",
            context =>
            {
                foreach (string line in ShownSoldiers().Select(static s => s.Line))
                {
                    context.Print(line);
                }
            });

        console.Commands.Add(
            "dbg_aim",
            "Debugging: turns the player on a soldier's chest: its entity, or nearest.",
            context => context.Print(ProbeAim(context[0]) ? $"Aimed at {context[0]}." : $"Could not aim at '{context[0]}'."),
            "<entity|nearest>");

        console.Commands.Add(
            "dbg_shoot",
            "Debugging: keeps the player turned on a soldier and its trigger held for some seconds (2 by default).",
            context =>
            {
                _probeTarget = context.Count > 0 ? context[0] : "nearest";
                _probeShootUntil = ProbeSeconds + ProbeNumber(context[1], 2);
                ProbeMark($"shoot {_probeTarget} {ProbeNumber(context[1], 2):0.#}s");
            },
            "<entity|nearest> [seconds]");

        console.Commands.Add(
            "dbg_walk",
            "Debugging: walks the player forward for some seconds (2 by default).",
            context =>
            {
                _probeWalkUntil = ProbeSeconds + ProbeNumber(context[0], 2);
                ProbeMark($"walk {ProbeNumber(context[0], 2):0.#}s");
            },
            "[seconds]");

        console.Commands.Add(
            "dbg",
            "Debugging: runs one of the server's dbg commands (guards, tp, hurt, kill, trace) -- here when hosting, else through rcon. @me is this player.",
            context => ProbeServer(context.Rest(0)),
            "<command>");

        console.Commands.Add(
            "dbg_run",
            "Debugging: runs a script of '<seconds> <command>' lines from when the session is in its level.",
            context => context.Print(LoadProbeScript(context.Rest(0))),
            "<file>");
    }

    // Once a frame, before the player reads its input: the script's lines that are due, and the player turned and its trigger
    // held while it shoots.
    private void UpdateNetProbe()
    {
        double now = ProbeSeconds;
        if (_probeScript.Count > 0 && ProbeInLevel())
        {
            _probeScriptStart ??= now;
            while (_probeScript.Count > 0 && now - _probeScriptStart.Value >= _probeScript[0].At)
            {
                string line = _probeScript[0].Line;
                _probeScript.RemoveAt(0);
                _logger.Information(ProbeCategory, $"script {now - _probeScriptStart.Value:F1}s: {line}");
                _probeConsole?.Execute(line);
            }
        }

        bool shooting = _probeTarget is not null && now < _probeShootUntil;
        bool walking = now < _probeWalkUntil;
        if (shooting && !ProbeAim(_probeTarget!))
        {
            shooting = false;
        }

        if (!shooting)
        {
            _probeTarget = null;
        }

        if (shooting || walking)
        {
            _probeInput.Mouse.SetButton(MouseButton.Left, shooting);
            _probeInput.Keyboard.SetKey(KeyCode.W, walking);
            ScriptedViewInput = _probeInput;
        }
        else if (ReferenceEquals(ScriptedViewInput, _probeInput))
        {
            _probeInput.Mouse.SetButton(MouseButton.Left, false);
            _probeInput.Keyboard.SetKey(KeyCode.W, false);
            ScriptedViewInput = null;
        }
    }

    // Whether the session is in its level: a joined client playing the server's, or a host whose level is up.
    private bool ProbeInLevel() => TickObserver switch
    {
        Multiplayer.SessionClient client => client.LocalEntity is not null && !client.HoldsLevel && client.World is not null,
        Multiplayer.HostedSession hosted => hosted.Session?.LastSnapshot is not null,
        _ => false,
    };

    // A server dbg command: run here when this game hosts, else sent through the remote console.
    private void ProbeServer(string command)
    {
        if (TickObserver is Multiplayer.HostedSession hosted)
        {
            string[] words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            IEnumerable<string> lines = words.Length > 0 && words[0].Equals("trace", StringComparison.OrdinalIgnoreCase)
                ? [NetProbe.Command(server: true, words[1..], ProbeDirectory, hosted.Session?.LastSnapshot?.Tick ?? 0)]
                : hosted.Session?.Debug([.. words.Select(w => w.Replace("@me", "host", StringComparison.OrdinalIgnoreCase))]) ?? ["No match is being played."];
            foreach (string line in lines)
            {
                _logger.Information(ProbeCategory, line);
            }

            return;
        }

        if (TickObserver is not Multiplayer.SessionClient client || !client.SendRcon(_rconPassword, "dbg " + command.Replace("@me", client.PlayerName, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.Warning(ProbeCategory, "Not in a session: dbg needs a server.");
        }
    }

    // Turns the player on a soldier it shows, by entity or the nearest living one.
    private bool ProbeAim(string target)
    {
        if (TickObserver is not Multiplayer.SessionClient { World: { } world })
        {
            return false;
        }

        int? id = string.Equals(target, "nearest", StringComparison.OrdinalIgnoreCase) || target.Length == 0
            ? ShownSoldiers().FirstOrDefault(static s => s.Alive).Id
            : int.TryParse(target, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
        return id is { } chosen && world.AimAt(chosen);
    }

    // The soldiers this client shows, nearest its player first.
    private IEnumerable<(int? Id, bool Alive, string Line)> ShownSoldiers()
    {
        if (TickObserver is not Multiplayer.SessionClient { World: { } world, Session.Latest: { } latest })
        {
            return [];
        }

        Vector3d own = world.LocalPosition;
        return latest.Humans
            .Where(static h => ReplicatedEntity.IsSoldier(h.Id))
            .Select(h => (h.Id, Drawn: world.Drawn(h.Id)))
            .Where(static s => s.Drawn is not null)
            .Select(s => (s.Id, s.Drawn!.Value, Distance: (s.Drawn!.Value.Position - own).Length / WorldUnits.PerMetre))
            .OrderBy(static s => s.Distance)
            .Select(static s => ((int?)s.Id, s.Value.Alive, FormattableString.Invariant(
                $"soldier {s.Id}: {(s.Value.Alive ? "alive" : "dead")} drawn at ({s.Value.Position.X / WorldUnits.PerMetre:0.0}, {s.Value.Position.Y / WorldUnits.PerMetre:0.0}, {s.Value.Position.Z / WorldUnits.PerMetre:0.0}) m, state {s.Value.State}, {s.Distance:0.0} m away")));
    }

    private string LoadProbeScript(string file)
    {
        string path = Path.IsPathRooted(file) ? file : Path.Combine(ProbeDirectory, file);
        if (!File.Exists(path))
        {
            return $"No script at {path}.";
        }

        _probeScript.Clear();
        _probeScriptStart = null;
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            int space = line.IndexOf(' ', StringComparison.Ordinal);
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith('#') || space < 0
                || !double.TryParse(line[..space], NumberStyles.Float, CultureInfo.InvariantCulture, out double at))
            {
                continue;
            }

            _probeScript.Add((at, line[(space + 1)..].Trim()));
        }

        _probeScript.Sort(static (a, b) => a.At.CompareTo(b.At));
        return $"{_probeScript.Count} lines from {path}, run from when the session is in its level.";
    }

    // A mark in this game's trace, stamped as its lines are.
    private void ProbeMark(string text) => NetProbe.Client?.Mark(ProbeWhen(), text);

    // Marks are stamped with server ticks on both sides, so the two traces line up: a host's present, a client's drawn moment.
    private double ProbeWhen() => TickObserver switch
    {
        Multiplayer.HostedSession hosted => hosted.Session?.LastSnapshot?.Tick ?? 0,
        Multiplayer.SessionClient client => client.Session?.DrawnTick ?? 0,
        _ => 0,
    };

    private static double ProbeNumber(string text, double otherwise) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : otherwise;
}
