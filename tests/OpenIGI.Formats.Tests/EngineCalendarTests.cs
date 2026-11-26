using OpenIGI.Formats.World;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>The engine's file stamp, ordered as <c>QCalendar_Compare</c> orders it.</summary>
public sealed class EngineCalendarTests
{
    /// <summary>Stamps read back field by field, in the order the struct declares them.</summary>
    [Fact]
    public void A_stamp_reads_its_seven_fields_in_order()
    {
        byte[] bytes = new byte[EngineCalendar.Size];
        int[] fields = [2002, 11, 4, 17, 30, 59, 250];
        for (int i = 0; i < fields.Length; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), fields[i]);
        }

        Assert.Equal(new EngineCalendar(2002, 11, 4, 17, 30, 59, 250), EngineCalendar.Read(bytes));
    }

    /// <summary>An earlier moment orders first, down to the millisecond.</summary>
    [Fact]
    public void An_earlier_moment_orders_first()
    {
        EngineCalendar exported = new(2002, 11, 4, 17, 30, 59, 250);

        Assert.True(exported < exported with { Milliseconds = 251 });
        Assert.True(exported > exported with { Day = 3 });
        Assert.Equal(0, exported.CompareTo(exported));
    }

    /// <summary>A field wider than its printed width orders as its text does, not as its number does.</summary>
    /// <remarks>The original compares the printed strings, so "100" sorts before "99" in a two-digit field's place.</remarks>
    [Fact]
    public void A_field_past_its_width_orders_as_printed()
    {
        EngineCalendar wide = new(2002, 11, 4, 100, 0, 0, 0);
        EngineCalendar narrow = new(2002, 11, 4, 99, 0, 0, 0);

        Assert.True(wide < narrow);
    }
}
