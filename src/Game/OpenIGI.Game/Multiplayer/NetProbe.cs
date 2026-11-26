using System.Globalization;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Multiplayer;

/// <summary>What a client's game draws of one of the others: where its body stands and what it does, as the title has it.</summary>
/// <param name="Position">Where it stands, in world units.</param>
/// <param name="State">What it does: the title's state or clip, as its snapshots carry it in <see cref="ReplicatedHuman.State"/>.</param>
/// <param name="Alive">Whether it is drawn alive.</param>
public readonly record struct ProbeDrawn(Vector3d Position, int State, bool Alive);

/// <summary>
/// A debugging trace of a session, written as comma-separated lines for <c>tools/netprobe/compare.py</c> to set a client's
/// beside its server's. <b>Temporary: a tool for finding what makes the others jump, not part of the game.</b>
/// </summary>
/// <remarks>
/// <para>The server writes every human of every snapshot it takes:</para>
/// <para><c>S,tick,id,x,y,z,state,alive</c></para>
/// <para>A client writes, every frame, each human it shows: the moment it drew them at (a server tick, fractional), where the
/// snapshots put them then, and where its game drew them; and its own player:</para>
/// <para><c>C,seconds,drawnTick,id,sx,sy,sz,sstate,salive,dx,dy,dz,dstate,dalive</c></para>
/// <para><c>P,seconds,drawnTick,x,y,z,corrections,lastError,lastRunCommand</c></para>
/// <para>and either side a line of its own: <c>M,when,text</c>. Positions are world units, 4096 to the metre.</para>
/// </remarks>
public sealed class NetProbe : IDisposable
{
    private readonly StreamWriter _writer;

    private NetProbe(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        _writer = new StreamWriter(path, append: false) { AutoFlush = false };
    }

    /// <summary>Gets the client trace being written in this process, or <see langword="null"/>.</summary>
    public static NetProbe? Client { get; private set; }

    /// <summary>Gets the server trace being written in this process, or <see langword="null"/>.</summary>
    public static NetProbe? Server { get; private set; }

    /// <summary>Gets where the trace is written.</summary>
    public string Path { get; }

    /// <summary>Gets how many lines it has written.</summary>
    public long Lines { get; private set; }

    /// <summary>Runs a <c>trace</c> command for one side: <c>on</c>, <c>off</c>, <c>mark &lt;text&gt;</c> or nothing for its state.</summary>
    /// <param name="server">Whether it is the server's trace.</param>
    /// <param name="words">The words after <c>trace</c>.</param>
    /// <param name="directory">The folder a new trace is written in.</param>
    /// <param name="when">What a mark is stamped with: the server's tick, or the client's seconds.</param>
    /// <returns>What it did.</returns>
    public static string Command(bool server, IReadOnlyList<string> words, string directory, double when)
    {
        string side = server ? "server" : "client";
        NetProbe? current = server ? Server : Client;
        string verb = words.Count > 0 ? words[0].ToLowerInvariant() : string.Empty;
        switch (verb)
        {
            case "on":
                current?.Dispose();
                string path = System.IO.Path.Combine(directory, "netprobe", $"{side}-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
                NetProbe made = new(path);
                if (server)
                {
                    Server = made;
                }
                else
                {
                    Client = made;
                }

                return $"The {side} trace is written to {path}.";
            case "off":
                if (current is null)
                {
                    return $"No {side} trace is being written.";
                }

                string done = $"The {side} trace is closed: {current.Lines} lines in {current.Path}.";
                current.Dispose();
                if (server)
                {
                    Server = null;
                }
                else
                {
                    Client = null;
                }

                return done;
            case "mark" when words.Count > 1:
                if (current is null)
                {
                    return $"No {side} trace is being written.";
                }

                current.Mark(when, string.Join(' ', words.Skip(1)));
                return $"Marked the {side} trace.";
            default:
                return current is null ? $"No {side} trace is being written." : $"The {side} trace has {current.Lines} lines in {current.Path}.";
        }
    }

    /// <summary>Writes every human of a snapshot the server took.</summary>
    /// <param name="snapshot">The snapshot.</param>
    public void WriteSnapshot(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        foreach (ReplicatedHuman human in snapshot.Humans)
        {
            Line(string.Create(
                CultureInfo.InvariantCulture,
                $"S,{snapshot.Tick},{human.Id},{human.Position.X},{human.Position.Y},{human.Position.Z},{human.State},{Alive(human)}"));
        }
    }

    /// <summary>Writes one human a client showed this frame.</summary>
    /// <param name="seconds">The client's clock.</param>
    /// <param name="drawnTick">The server tick, fractional, the others were drawn at.</param>
    /// <param name="sampled">Where the snapshots put it at that moment.</param>
    /// <param name="drawn">Where the client's game drew it, or <see langword="null"/> when the title does not say.</param>
    public void WriteShown(double seconds, double drawnTick, in ReplicatedHuman sampled, ProbeDrawn? drawn)
    {
        string drawnPart = drawn is { } d
            ? string.Create(CultureInfo.InvariantCulture, $"{d.Position.X:F0},{d.Position.Y:F0},{d.Position.Z:F0},{d.State},{(d.Alive ? 1 : 0)}")
            : ",,,,";
        Line(string.Create(
            CultureInfo.InvariantCulture,
            $"C,{seconds:F4},{drawnTick:F3},{sampled.Id},{sampled.Position.X},{sampled.Position.Y},{sampled.Position.Z},{sampled.State},{Alive(sampled)},{drawnPart}"));
    }

    /// <summary>Writes the client's own player this frame.</summary>
    /// <param name="seconds">The client's clock.</param>
    /// <param name="drawnTick">The server tick the others were drawn at.</param>
    /// <param name="position">Where its player stands.</param>
    /// <param name="corrections">The corrections its prediction has had so far.</param>
    /// <param name="lastError">The size of the last error found, in world units.</param>
    /// <param name="lastRunCommand">The newest of its commands the server says it ran.</param>
    public void WriteOwn(double seconds, double drawnTick, Vector3d position, long corrections, double lastError, long lastRunCommand) =>
        Line(string.Create(
            CultureInfo.InvariantCulture,
            $"P,{seconds:F4},{drawnTick:F3},{position.X:F0},{position.Y:F0},{position.Z:F0},{corrections},{lastError:F0},{lastRunCommand}"));

    /// <summary>Writes a line of its own, to find a moment by.</summary>
    /// <param name="when">The server's tick, or the client's seconds.</param>
    /// <param name="text">The line.</param>
    public void Mark(double when, string text) =>
        Line(string.Create(CultureInfo.InvariantCulture, $"M,{when:F3},{text.Replace(',', ';')}"));

    /// <inheritdoc/>
    public void Dispose()
    {
        _writer.Flush();
        _writer.Dispose();
    }

    private static int Alive(in ReplicatedHuman human) => (human.Flags & ReplicatedHumanCondition.Alive) != 0 ? 1 : 0;

    private void Line(string line)
    {
        _writer.WriteLine(line);
        if (++Lines % 2000 == 0)
        {
            _writer.Flush();
        }
    }
}
