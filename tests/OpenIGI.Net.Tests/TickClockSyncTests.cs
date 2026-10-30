using Xunit;

namespace OpenIGI.Net.Tests;

/// <summary>A client's commands steered by how far ahead the server says they arrive.</summary>
public sealed class TickClockSyncTests
{
    [Fact]
    public void A_lead_the_server_reports_too_long_pulls_the_stamps_back_to_the_buffer()
    {
        // A round trip overstated by frame time: 200 ms reckoned, so the stamps run 6 ticks further ahead than needed.
        TickClockSync clock = new(30);
        clock.AddSample(serverTick: 100, now: 10.0, roundTripTime: 0.2);
        long before = clock.CommandTick(10.0, 0.2);

        // The server keeps seeing the commands arrive that far ahead until the steering has taken it up.
        for (int report = 0; report < 100; report++)
        {
            long lead = clock.CommandTick(10.0, 0.2) - 103;
            clock.ReportCommandLead(lead);
        }

        long after = clock.CommandTick(10.0, 0.2);
        Assert.True(after < before, $"the stamps stayed at {after} from {before}");
        Assert.InRange(after - 103, clock.CommandBufferTicks - 1, clock.CommandBufferTicks + 1);
    }
}
