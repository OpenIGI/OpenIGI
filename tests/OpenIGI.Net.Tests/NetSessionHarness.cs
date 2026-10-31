namespace OpenIGI.Net.Tests;

/// <summary>A server and clients on one loopback network, each behind a simulated line, stepped on a manual clock.</summary>
internal sealed class NetSessionHarness : IDisposable
{
    public const ulong ProtocolId = 0x4F50454E49474931UL;
    public const double Step = 1.0 / 60.0;

    private readonly LoopbackNetwork _network = new();
    private readonly List<ClientEnd> _clients = [];

    public NetSessionHarness(NetServerOptions? options = null, int serverPort = 26001)
    {
        options ??= new NetServerOptions();
        options.ProtocolId = options.ProtocolId == 0 ? ProtocolId : options.ProtocolId;
        ServerLine = new SimulatedLinkTransport(_network.Open(serverPort), () => Now, seed: 1);
        Server = new NetServer(ServerLine, options);
    }

    public double Now { get; private set; }

    public NetServer Server { get; }

    public SimulatedLinkTransport ServerLine { get; }

    public List<NetEvent> ServerEvents { get; } = [];

    public IReadOnlyList<ClientEnd> Clients => _clients;

    public ClientEnd AddClient(ulong protocolId = ProtocolId, int seed = 2)
    {
        SimulatedLinkTransport line = new(_network.Open(), () => Now, seed);
        ClientEnd end = new(new NetClient(line, protocolId), line);
        _clients.Add(end);
        return end;
    }

    /// <summary>Makes every line as bad as asked, each figure one-way: half at the sender, half at the receiver.</summary>
    public void SetLine(double latency, double jitter, double loss, double duplicate)
    {
        foreach (SimulatedLinkTransport line in _clients.Select(c => c.Line).Append(ServerLine))
        {
            foreach (SimulatedLinkSettings settings in new[] { line.Send, line.Receive })
            {
                settings.Latency = latency / 2;
                settings.Jitter = jitter / 2;
                settings.Loss = loss / 2;
                settings.Duplicate = duplicate / 2;
            }
        }
    }

    public void Run(double seconds)
    {
        int steps = (int)Math.Ceiling(seconds / Step);
        for (int i = 0; i < steps; i++)
        {
            Tick();
        }
    }

    public bool RunUntil(Func<bool> condition, double timeoutSeconds = 10)
    {
        double end = Now + timeoutSeconds;
        while (Now < end)
        {
            Tick();
            if (condition())
            {
                return true;
            }
        }

        return false;
    }

    public void Tick()
    {
        Now += Step;
        Server.Update(Now);
        while (Server.TryReceive(out NetEvent e))
        {
            ServerEvents.Add(e);
        }

        foreach (ClientEnd client in _clients)
        {
            client.Client.Update(Now);
            while (client.Client.TryReceive(out NetEvent e))
            {
                client.Events.Add(e);
            }

            client.Client.Flush(Now);
        }

        Server.Flush(Now);
    }

    public void Dispose()
    {
        foreach (ClientEnd client in _clients)
        {
            client.Client.Dispose();
        }

        Server.Dispose();
    }

    internal sealed record ClientEnd(NetClient Client, SimulatedLinkTransport Line)
    {
        public List<NetEvent> Events { get; } = [];

        public IEnumerable<byte[]> Messages(NetChannel channel) =>
            Events.Where(e => e.Kind == NetEventKind.Message && e.Message.Channel == channel).Select(e => e.Message.Data);
    }
}
