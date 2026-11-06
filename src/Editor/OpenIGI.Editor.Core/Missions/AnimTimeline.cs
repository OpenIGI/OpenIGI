using System.Globalization;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Editor.Core.Missions;

/// <summary>One value an <c>AnimTask</c> sets on its target: a channel, from a tick on.</summary>
public sealed class AnimKey
{
    /// <summary>Gets or sets the tick it is set at (30 a second).</summary>
    public int Tick { get; set; }

    /// <summary>Gets or sets the channel it sets, 0 to 31.</summary>
    public int Channel { get; set; }

    /// <summary>Gets or sets the value; <see cref="AnimTimeline.Hold"/> leaves the target's own value alone.</summary>
    public double Value { get; set; }

    /// <summary>Gets or sets whether the value is written as a real (<c>1.0</c>) rather than an integer.</summary>
    public bool IsReal { get; set; }

    /// <summary>Gets or sets the waits written just before it, as the file had them; written again while they still fill the gap.</summary>
    internal IReadOnlyList<(int Ticks, bool IsReal)> WaitsBefore { get; set; } = [];
}

/// <summary>
/// An <c>AnimData</c> parameter as a timeline: values set on channels at ticks, and how long it runs, instead of the flat
/// list of channel writes the script holds (<c>63, 3, -1, 4, -1, 32, 23.0, …</c>).
/// </summary>
/// <remarks>
/// <para>
/// The script's list is a count, then pairs of channel and value; writing channel <see cref="AnimDataProgram.WaitChannel"/>
/// waits that many ticks (<see cref="AnimDataProgram"/>). A key's tick is the sum of the waits before it, and the waits are
/// written back from the gaps between keys. A gap still as long as it was is written with the waits it was read with (retail
/// has a few written as integers, back to back, or of no time), so an unedited programme writes back exactly as read.
/// </para>
/// </remarks>
public sealed class AnimTimeline
{
    /// <summary>The value meaning "leave this channel as the target has it" (vehicles' hold sentinel).</summary>
    public const double Hold = -1.0;

    /// <summary>The ticks in a second (<c>GAME_FREQUENCY</c>).</summary>
    public const int TicksPerSecond = 30;

    /// <summary>The channels a key can set; the two above are the programme's own wait and counter.</summary>
    public const int ChannelCount = AnimDataProgram.WaitChannel;

    /// <summary>Gets the keys, in the order they run.</summary>
    public List<AnimKey> Keys { get; } = [];

    /// <summary>Gets or sets the tick the programme ends at; never before its last key.</summary>
    public int Length { get; set; }

    // The waits after the last key, as read.
    private IReadOnlyList<(int Ticks, bool IsReal)> _tailWaits = [];

    /// <summary>Reads a timeline from an <c>AnimData</c> parameter's arguments.</summary>
    /// <param name="arguments">The arguments: a count, then channel and value pairs.</param>
    /// <param name="timeline">The timeline, when it reads and writes back the same programme.</param>
    /// <returns>Whether it could be read without changing what the programme does.</returns>
    public static bool TryParse(IReadOnlyList<QscExpression> arguments, out AnimTimeline timeline)
    {
        timeline = new AnimTimeline();
        if (arguments.Count == 0 || !TryNumber(arguments[0], out double countValue, out _))
        {
            return false;
        }

        int count = (int)countValue;
        if (count < 0 || arguments.Count != 1 + (2 * count))
        {
            return false;
        }

        int tick = 0;
        List<(int Ticks, bool IsReal)> waits = [];
        for (int i = 0; i < count; i++)
        {
            if (!TryNumber(arguments[1 + (2 * i)], out double channelValue, out _)
                || !TryNumber(arguments[2 + (2 * i)], out double value, out bool isReal))
            {
                return false;
            }

            int channel = unchecked((sbyte)(int)channelValue);
            if (channel == AnimDataProgram.WaitChannel)
            {
                int wait = (int)value;
                if (wait < 0)
                {
                    return false;
                }

                tick += wait;
                waits.Add((wait, isReal));
                continue;
            }

            if (channel is < 0 or >= ChannelCount)
            {
                return false;
            }

            timeline.Keys.Add(new AnimKey { Tick = tick, Channel = channel, Value = value, IsReal = isReal, WaitsBefore = [.. waits] });
            waits.Clear();
        }

        timeline.Length = tick;
        timeline._tailWaits = [.. waits];
        return true;
    }

    /// <summary>Gets the tick the last key is set at, or nought.</summary>
    public int LastKeyTick => Keys.Count == 0 ? 0 : Keys.Max(static k => k.Tick);

    /// <summary>Gets the channels the keys set, in order.</summary>
    public IEnumerable<int> Channels => Keys.Select(static k => k.Channel).Distinct().Order();

    /// <summary>Writes the timeline back as an <c>AnimData</c> parameter's arguments.</summary>
    /// <returns>The count, then channel and value pairs, waits between keys at different ticks.</returns>
    public IReadOnlyList<QscExpression> ToArguments()
    {
        List<QscExpression> pairs = [];
        int tick = 0;
        foreach (AnimKey key in Keys.OrderBy(static k => k.Tick))
        {
            AddWaits(pairs, key.Tick - tick, key.WaitsBefore);
            tick = Math.Max(tick, key.Tick);

            pairs.Add(new QscIntLiteral(key.Channel));
            pairs.Add(key.IsReal || key.Value != Math.Floor(key.Value) ? ScriptValues.Real(key.Value) : ScriptValues.Integer((int)key.Value));
        }

        AddWaits(pairs, Math.Max(Length, LastKeyTick) - tick, _tailWaits);

        return [new QscIntLiteral(pairs.Count / 2), .. pairs];
    }

    /// <summary>Puts the keys in the order they run, keeping the order of keys at the same tick.</summary>
    public void Sort()
    {
        AnimKey[] sorted = [.. Keys.OrderBy(static k => k.Tick)];
        Keys.Clear();
        Keys.AddRange(sorted);
        Length = Math.Max(Length, LastKeyTick);
    }

    /// <summary>Gets whether a task type reads an <c>AnimTask</c>'s channels at all; only vehicles register a receiver.</summary>
    /// <param name="targetType">The target's task type.</param>
    /// <returns>Whether the programme drives it.</returns>
    public static bool Drives(string targetType) => targetType is "Train" or "Car" or "Heli" or "Plane";

    /// <summary>Gets what a channel means to a type of target, from its receiver (Train <c>0x42B040</c>, Car, Heli <c>0x431B70</c>, Plane).</summary>
    /// <param name="targetType">The target's task type.</param>
    /// <param name="channel">The channel.</param>
    /// <returns>The channel's name, or <c>Channel n</c> where the target reads nothing from it.</returns>
    public static string ChannelName(string targetType, int channel) => (targetType, channel) switch
    {
        ("Train", 0) => "Thrust",
        ("Car", 0) => "Steering",
        ("Car", 1) => "Thrust",
        ("Car", 3) => "Parking brake",
        ("Heli", 0) => "Cyclic X",
        ("Heli", 1) => "Cyclic Y",
        ("Heli", 2) => "Yaw",
        ("Heli", 3) => "Collective",
        ("Plane", 0) => "Control X",
        ("Plane", 1) => "Control Y",
        ("Plane", 2) => "Control Z",
        ("Plane", 3) => "Thrust",
        ("Plane", 5) => "Canopy",
        ("Plane", 6) => "Gear",
        _ => string.Create(CultureInfo.InvariantCulture, $"Channel {channel}"),
    };

    /// <summary>Gets the channels a type of target reads, first, for picking from.</summary>
    /// <param name="targetType">The target's task type.</param>
    /// <returns>The channels it reads, then every other.</returns>
    public static IEnumerable<int> ChannelOrder(string targetType)
    {
        int[] named = [.. Enumerable.Range(0, ChannelCount).Where(c => !ChannelName(targetType, c).StartsWith("Channel ", StringComparison.Ordinal))];
        return named.Concat(Enumerable.Range(0, ChannelCount).Except(named));
    }

    /// <summary>Gets a tick as seconds, for showing.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The seconds, as text.</returns>
    public static string Seconds(int tick) => (tick / (double)TicksPerSecond).ToString("0.##", CultureInfo.InvariantCulture) + " s";

    // A gap: the waits it was read with while they still add up to it, else one wait of its length (none for no gap).
    private static void AddWaits(List<QscExpression> pairs, int gap, IReadOnlyList<(int Ticks, bool IsReal)> read)
    {
        IEnumerable<(int Ticks, bool IsReal)> waits = read.Count > 0 && read.Sum(static w => w.Ticks) == gap
            ? read
            : gap > 0 ? [(gap, true)] : [];
        foreach ((int ticks, bool isReal) in waits)
        {
            pairs.Add(new QscIntLiteral(AnimDataProgram.WaitChannel));
            pairs.Add(isReal ? ScriptValues.Real(ticks) : ScriptValues.Integer(ticks));
        }
    }

    // A constant, and whether it was written as a real.
    private static bool TryNumber(QscExpression expression, out double value, out bool isReal)
    {
        isReal = (expression is QscUnary unary ? unary.Operand : expression) is QscRealLiteral;
        return ScriptValues.TryGetNumber(expression, out value);
    }
}
