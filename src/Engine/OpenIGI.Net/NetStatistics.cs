namespace OpenIGI.Net;

/// <summary>How a connection's line is doing: what the net graph draws.</summary>
public sealed class NetStatistics
{
    private const double Smoothing = 0.1;

    private double _rateWindowStart = double.NaN;
    private long _windowBytesSent;
    private long _windowBytesReceived;

    /// <summary>Gets the smoothed round-trip time in seconds, or zero before the first sample.</summary>
    public double RoundTripTime { get; private set; }

    /// <summary>Gets how much the round trip varies, in seconds.</summary>
    public double RoundTripJitter { get; private set; }

    /// <summary>Gets the smoothed fraction of sent packets that were never acknowledged.</summary>
    public double PacketLoss { get; private set; }

    /// <summary>Gets the bytes sent over the last whole second.</summary>
    public double BytesSentPerSecond { get; private set; }

    /// <summary>Gets the bytes received over the last whole second.</summary>
    public double BytesReceivedPerSecond { get; private set; }

    /// <summary>Gets every packet sent.</summary>
    public long PacketsSent { get; private set; }

    /// <summary>Gets every packet received.</summary>
    public long PacketsReceived { get; private set; }

    /// <summary>Gets every byte sent.</summary>
    public long BytesSent { get; private set; }

    /// <summary>Gets every byte received.</summary>
    public long BytesReceived { get; private set; }

    /// <summary>Gets the reliable messages sent more than once.</summary>
    public long ReliableResends { get; private set; }

    internal void AddRoundTripSample(double seconds)
    {
        if (RoundTripTime == 0)
        {
            RoundTripTime = seconds;
            RoundTripJitter = seconds / 2;
            return;
        }

        RoundTripJitter += Smoothing * (Math.Abs(seconds - RoundTripTime) - RoundTripJitter);
        RoundTripTime += Smoothing * (seconds - RoundTripTime);
    }

    internal void AddLossSample(bool lost) => PacketLoss += Smoothing * ((lost ? 1.0 : 0.0) - PacketLoss);

    internal void CountSent(int bytes, double now)
    {
        PacketsSent++;
        BytesSent += bytes;
        _windowBytesSent += bytes;
        RollWindow(now);
    }

    internal void CountReceived(int bytes, double now)
    {
        PacketsReceived++;
        BytesReceived += bytes;
        _windowBytesReceived += bytes;
        RollWindow(now);
    }

    internal void CountResend() => ReliableResends++;

    private void RollWindow(double now)
    {
        if (double.IsNaN(_rateWindowStart))
        {
            _rateWindowStart = now;
        }

        double elapsed = now - _rateWindowStart;
        if (elapsed >= 1.0)
        {
            BytesSentPerSecond = _windowBytesSent / elapsed;
            BytesReceivedPerSecond = _windowBytesReceived / elapsed;
            _windowBytesSent = 0;
            _windowBytesReceived = 0;
            _rateWindowStart = now;
        }
    }
}
