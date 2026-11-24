using OpenIGI.Core.Numerics;
using OpenIGI.Game.Computer;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Replays;

/// <summary>
/// An <see cref="InputTape"/> as bytes: each tick its flags and, where it took input, only the fields that changed since the last
/// input -- the mouse most ticks, a key now and then -- with every float kept to the bit. Read back strictly: a count, a length
/// or a field out of range is a broken tape, never a guess.
/// </summary>
public static class InputTapeCodec
{
    /// <summary>The longest text a command or a mark carries, in characters.</summary>
    public const int MaxText = 256;

    private const byte HasInput = 0x1;
    private const byte Breaking = 0x2;

    // Which fields of a tick's input are written, one bit each, in this order.
    [Flags]
    private enum Field : uint
    {
        MouseX = 1 << 0,
        MouseY = 1 << 1,
        Held = 1 << 2,
        Pressed = 1 << 3,
        Sensitivity = 1 << 4,
        Switches = 1 << 5,
        TitleHeld = 1 << 6,
        TitlePressed = 1 << 7,
        Raw = 1 << 8,
        ViewBlend = 1 << 9,
        Aim = 1 << 10,
        WheelPick = 1 << 11,
        Computer = 1 << 12,
        All = (1 << 13) - 1,
    }

    /// <summary>Writes a tape.</summary>
    /// <param name="writer">Where it goes.</param>
    /// <param name="tape">The tape.</param>
    public static void Write(BinaryWriter writer, InputTape tape)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(tape);
        writer.Write7BitEncodedInt(tape.Length);
        TitlePlayerInput? last = null;
        for (int tick = 0; tick < tape.Length; tick++)
        {
            TitlePlayerInput? input = tape.InputAt(tick);
            byte flags = (byte)((input is null ? 0 : HasInput) | (tape.BreaksAt(tick) ? Breaking : 0));
            writer.Write(flags);
            if (input is { } given)
            {
                WriteInput(writer, given, last);
                last = given;
            }
        }

        writer.Write7BitEncodedInt(tape.Commands.Count);
        foreach ((int tick, string command) in tape.Commands)
        {
            writer.Write7BitEncodedInt(tick);
            ReplayBinary.WriteText(writer, command, MaxText);
        }

        writer.Write7BitEncodedInt(tape.Marks.Count);
        foreach ((int tick, string text) in tape.Marks)
        {
            writer.Write7BitEncodedInt(tick);
            ReplayBinary.WriteText(writer, text, MaxText);
        }

        writer.Write7BitEncodedInt(tape.Digests.Count);
        foreach ((int tick, ulong digest) in tape.Digests)
        {
            writer.Write7BitEncodedInt(tick);
            writer.Write(digest);
        }
    }

    /// <summary>Reads a tape.</summary>
    /// <param name="reader">Where it comes from.</param>
    /// <returns>The tape.</returns>
    /// <exception cref="InvalidDataException">The bytes are not a tape, or one past the limits.</exception>
    public static InputTape Read(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        InputTape tape = new();
        int length = Count(reader, InputTape.MaxTicks, "ticks");
        TitlePlayerInput last = default;
        for (int tick = 0; tick < length; tick++)
        {
            byte flags = reader.ReadByte();
            if ((flags & ~(HasInput | Breaking)) != 0)
            {
                throw new InvalidDataException($"Tick {tick} carries unknown flags.");
            }

            if ((flags & HasInput) != 0)
            {
                last = ReadInput(reader, last);
                tape.SetInput(tick, last);
            }

            tape.SetBreak(tick, (flags & Breaking) != 0);
            tape.Ran(tick);
        }

        int commands = Count(reader, InputTape.MaxEntries, "commands");
        for (int i = 0; i < commands; i++)
        {
            tape.Command(Tick(reader, length), Text(reader));
        }

        int marks = Count(reader, InputTape.MaxEntries, "marks");
        for (int i = 0; i < marks; i++)
        {
            tape.Mark(Tick(reader, length), Text(reader));
        }

        int digests = Count(reader, InputTape.MaxEntries, "fingerprints");
        int previous = -1;
        for (int i = 0; i < digests; i++)
        {
            int tick = Tick(reader, length);
            if (tick <= previous)
            {
                throw new InvalidDataException("The fingerprints are out of order.");
            }

            previous = tick;
            tape.SetDigest(tick, reader.ReadUInt64());
        }

        return tape;
    }

    private static void WriteInput(BinaryWriter writer, in TitlePlayerInput input, TitlePlayerInput? last)
    {
        Field changed = last is not { } before ? Field.All : Changes(input, before);
        writer.Write7BitEncodedInt((int)changed);
        if ((changed & Field.MouseX) != 0) { writer.Write(input.MouseX); }
        if ((changed & Field.MouseY) != 0) { writer.Write(input.MouseY); }
        if ((changed & Field.Held) != 0) { writer.Write((ulong)input.Held); }
        if ((changed & Field.Pressed) != 0) { writer.Write((ulong)input.Pressed); }
        if ((changed & Field.Sensitivity) != 0) { writer.Write(input.MouseSensitivity); }
        if ((changed & Field.Switches) != 0) { writer.Write(Switches(input)); }
        if ((changed & Field.TitleHeld) != 0) { writer.Write(input.TitleHeld); }
        if ((changed & Field.TitlePressed) != 0) { writer.Write(input.TitlePressed); }
        if ((changed & Field.Raw) != 0) { writer.Write((int)input.Raw); }
        if ((changed & Field.ViewBlend) != 0) { WriteOptional(writer, input.ViewBlend); }
        if ((changed & Field.Aim) != 0) { WriteAim(writer, input.Aim); }
        if ((changed & Field.WheelPick) != 0) { WriteOptional(writer, input.WheelPick); }
        if ((changed & Field.Computer) != 0) { WriteComputer(writer, input.Computer); }
    }

    private static TitlePlayerInput ReadInput(BinaryReader reader, in TitlePlayerInput last)
    {
        uint mask = (uint)reader.Read7BitEncodedInt();
        if ((mask & ~(uint)Field.All) != 0)
        {
            throw new InvalidDataException("A tick names fields this tape has not got.");
        }

        Field changed = (Field)mask;
        float mouseX = (changed & Field.MouseX) != 0 ? reader.ReadSingle() : last.MouseX;
        float mouseY = (changed & Field.MouseY) != 0 ? reader.ReadSingle() : last.MouseY;
        PlayerAction held = (changed & Field.Held) != 0 ? (PlayerAction)reader.ReadUInt64() : last.Held;
        PlayerAction pressed = (changed & Field.Pressed) != 0 ? (PlayerAction)reader.ReadUInt64() : last.Pressed;
        float sensitivity = (changed & Field.Sensitivity) != 0 ? reader.ReadSingle() : last.MouseSensitivity;
        byte switches = (changed & Field.Switches) != 0 ? reader.ReadByte() : Switches(last);
        ulong titleHeld = (changed & Field.TitleHeld) != 0 ? reader.ReadUInt64() : last.TitleHeld;
        ulong titlePressed = (changed & Field.TitlePressed) != 0 ? reader.ReadUInt64() : last.TitlePressed;
        TitleRawKeys raw = (changed & Field.Raw) != 0 ? (TitleRawKeys)reader.ReadInt32() : last.Raw;
        float? viewBlend = (changed & Field.ViewBlend) != 0 ? ReadOptionalSingle(reader) : last.ViewBlend;
        TitleAim? aim = (changed & Field.Aim) != 0 ? ReadAim(reader) : last.Aim;
        int? wheelPick = (changed & Field.WheelPick) != 0 ? ReadOptionalInt(reader) : last.WheelPick;
        TitleComputerInput computer = (changed & Field.Computer) != 0 ? ReadComputer(reader) : last.Computer;
        if ((switches & ~0x7) != 0)
        {
            throw new InvalidDataException("A tick's switches are not a tape's.");
        }

        return new TitlePlayerInput(
            mouseX,
            mouseY,
            held,
            pressed,
            sensitivity,
            (switches & 0x1) != 0,
            (switches & 0x2) != 0,
            (switches & 0x4) != 0,
            titleHeld,
            titlePressed,
            raw,
            viewBlend,
            aim,
            wheelPick,
            computer);
    }

    // The fields that differ, a float by its bits so that one NaN is never taken for another.
    private static Field Changes(in TitlePlayerInput now, in TitlePlayerInput before)
    {
        Field changed = 0;
        if (Bits(now.MouseX) != Bits(before.MouseX)) { changed |= Field.MouseX; }
        if (Bits(now.MouseY) != Bits(before.MouseY)) { changed |= Field.MouseY; }
        if (now.Held != before.Held) { changed |= Field.Held; }
        if (now.Pressed != before.Pressed) { changed |= Field.Pressed; }
        if (Bits(now.MouseSensitivity) != Bits(before.MouseSensitivity)) { changed |= Field.Sensitivity; }
        if (Switches(now) != Switches(before)) { changed |= Field.Switches; }
        if (now.TitleHeld != before.TitleHeld) { changed |= Field.TitleHeld; }
        if (now.TitlePressed != before.TitlePressed) { changed |= Field.TitlePressed; }
        if (now.Raw != before.Raw) { changed |= Field.Raw; }
        if (OptionalBits(now.ViewBlend) != OptionalBits(before.ViewBlend)) { changed |= Field.ViewBlend; }
        if (!SameAim(now.Aim, before.Aim)) { changed |= Field.Aim; }
        if (now.WheelPick != before.WheelPick) { changed |= Field.WheelPick; }
        if (!SameComputer(now.Computer, before.Computer)) { changed |= Field.Computer; }
        return changed;
    }

    private static byte Switches(in TitlePlayerInput input) =>
        (byte)((input.InvertMouse ? 0x1 : 0) | (input.BreakCutscene ? 0x2 : 0) | (input.UnfilteredMouse ? 0x4 : 0));

    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);

    private static long OptionalBits(float? value) => value is { } v ? Bits(v) : long.MinValue;

    private static bool SameAim(TitleAim? a, TitleAim? b) =>
        (a, b) switch
        {
            (null, null) => true,
            ({ } x, { } y) => BitConverter.DoubleToInt64Bits(x.EyeOffset.X) == BitConverter.DoubleToInt64Bits(y.EyeOffset.X)
                && BitConverter.DoubleToInt64Bits(x.EyeOffset.Y) == BitConverter.DoubleToInt64Bits(y.EyeOffset.Y)
                && BitConverter.DoubleToInt64Bits(x.EyeOffset.Z) == BitConverter.DoubleToInt64Bits(y.EyeOffset.Z)
                && Bits(x.Direction.X) == Bits(y.Direction.X)
                && Bits(x.Direction.Y) == Bits(y.Direction.Y)
                && Bits(x.Direction.Z) == Bits(y.Direction.Z),
            _ => false,
        };

    private static bool SameComputer(in TitleComputerInput a, in TitleComputerInput b) =>
        a.Open == b.Open
        && Bits(a.Pointer.CountsX) == Bits(b.Pointer.CountsX)
        && Bits(a.Pointer.CountsY) == Bits(b.Pointer.CountsY)
        && a.Pointer.Buttons == b.Pointer.Buttons
        && a.Pointer.MapKey == b.Pointer.MapKey
        && a.Pointer.PageKey == b.Pointer.PageKey
        && a.Held == b.Held
        && a.DisplayWidth == b.DisplayWidth
        && a.DisplayHeight == b.DisplayHeight;

    private static void WriteOptional(BinaryWriter writer, float? value)
    {
        writer.Write(value.HasValue);
        if (value is { } v)
        {
            writer.Write(v);
        }
    }

    private static void WriteOptional(BinaryWriter writer, int? value)
    {
        writer.Write(value.HasValue);
        if (value is { } v)
        {
            writer.Write(v);
        }
    }

    private static float? ReadOptionalSingle(BinaryReader reader) => reader.ReadBoolean() ? reader.ReadSingle() : null;

    private static int? ReadOptionalInt(BinaryReader reader) => reader.ReadBoolean() ? reader.ReadInt32() : null;

    private static void WriteAim(BinaryWriter writer, TitleAim? aim)
    {
        writer.Write(aim.HasValue);
        if (aim is { } a)
        {
            writer.Write(a.EyeOffset.X);
            writer.Write(a.EyeOffset.Y);
            writer.Write(a.EyeOffset.Z);
            writer.Write(a.Direction.X);
            writer.Write(a.Direction.Y);
            writer.Write(a.Direction.Z);
        }
    }

    private static TitleAim? ReadAim(BinaryReader reader)
    {
        if (!reader.ReadBoolean())
        {
            return null;
        }

        Vector3d offset = new(reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble());
        Vector3f direction = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        return new TitleAim(offset, direction);
    }

    private static void WriteComputer(BinaryWriter writer, in TitleComputerInput computer)
    {
        writer.Write(computer.Open);
        writer.Write(computer.Pointer.CountsX);
        writer.Write(computer.Pointer.CountsY);
        writer.Write(computer.Pointer.Buttons);
        writer.Write(computer.Pointer.MapKey);
        writer.Write((int)computer.Pointer.PageKey);
        writer.Write((ulong)computer.Held);
        writer.Write(computer.DisplayWidth);
        writer.Write(computer.DisplayHeight);
    }

    private static TitleComputerInput ReadComputer(BinaryReader reader)
    {
        bool open = reader.ReadBoolean();
        float countsX = reader.ReadSingle();
        float countsY = reader.ReadSingle();
        int buttons = reader.ReadInt32();
        bool mapKey = reader.ReadBoolean();
        int page = reader.ReadInt32();
        if (!Enum.IsDefined((ComputerPageKind)page))
        {
            throw new InvalidDataException("A tick names a computer page there is not.");
        }

        PlayerAction held = (PlayerAction)reader.ReadUInt64();
        int width = reader.ReadInt32();
        int height = reader.ReadInt32();
        if (width < 0 || height < 0 || width > 65536 || height > 65536)
        {
            throw new InvalidDataException("A tick's computer display is not a display.");
        }

        return new TitleComputerInput(open, new ComputerPointerInput(countsX, countsY, buttons, mapKey) { PageKey = (ComputerPageKind)page }, held, width, height);
    }

    private static int Count(BinaryReader reader, int limit, string what)
    {
        int count = reader.Read7BitEncodedInt();
        return count < 0 || count > limit ? throw new InvalidDataException($"The tape has {count} {what}, past its limit of {limit}.") : count;
    }

    private static int Tick(BinaryReader reader, int length)
    {
        int tick = reader.Read7BitEncodedInt();
        return tick < 0 || tick > length ? throw new InvalidDataException($"A tick {tick} lies outside the tape's {length}.") : tick;
    }

    private static string Text(BinaryReader reader) => ReplayBinary.ReadText(reader, MaxText);
}
