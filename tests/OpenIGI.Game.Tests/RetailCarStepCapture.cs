using System.Globalization;
using System.IO.Compression;

namespace OpenIGI.Game.Tests;

/// <summary>A bit-exact record of retail car physics ticks, as the probe's <c>carstep</c> hook set writes it.</summary>
/// <remarks>
/// Each tick carries the car object's memory at <c>Car_Tick</c>'s entry (<c>0x42ECA0</c>), every force pushed
/// onto its rigid body (<c>0x4ECF50</c>) with the caller, and the ground-ray query's buffers as <c>0x42EF00</c>
/// reads them back. Nothing touches the body between two ticks except the car's channel packet, whose values
/// are themselves in the next entry dump -- so a tick's result is the next tick's entry.
/// </remarks>
internal sealed class RetailCarStepCapture
{
    /// <summary>The number of car bytes each entry dump holds.</summary>
    public const int CarBytes = 2400;

    public byte[] TypeMemory { get; private set; } = [];

    public byte[] RecordMemory { get; private set; } = [];

    public List<Tick> Ticks { get; } = [];

    public static RetailCarStepCapture Load(string fixture)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        using FileStream file = File.OpenRead(path);
        using GZipStream gzip = new(file, CompressionMode.Decompress);
        using StreamReader reader = new(gzip);

        RetailCarStepCapture capture = new();
        Dictionary<int, Tick> bySequence = [];
        List<byte> type = [];
        List<byte> record = [];
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 3 || words[0] != "[cs]")
            {
                continue;
            }

            int sequence = words[2].StartsWith("seq=", StringComparison.Ordinal) ? Int(words[2][4..]) : 0;
            Tick Get() => bySequence.TryGetValue(sequence, out Tick? tick) ? tick : bySequence[sequence] = new Tick(sequence);
            switch (words[1])
            {
                case "TYPEMEM":
                    type.AddRange(Hex(Value(words, "hex=")));
                    break;
                case "RECORD":
                    record.AddRange(Hex(Value(words, "hex=")));
                    break;
                case "ENTRY" when words.Length > 3 && words[3].StartsWith("autotick=", StringComparison.Ordinal):
                    Get().AutoTick = Int(words[3][9..]);
                    break;
                case "ENTRY":
                    Append(Get().Entry, Int(Value(words, "off=")), Hex(Value(words, "hex=")));
                    break;
                case "FORCE":
                {
                    string[] f = Value(words, "f=").Split(',');
                    string[] p = Value(words, "p=").Split(',');
                    Get().Forces.Add(new Force(
                        int.Parse(Value(words, "from=")[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        [Bits(f[0]), Bits(f[1]), Bits(f[2])],
                        [Bits(p[0]), Bits(p[1]), Bits(p[2])]));
                    break;
                }

                case "RAYS":
                    Get().RayCount = Int(Value(words, "count="));
                    break;
                case "RAYSTART":
                    Get().Starts.AddRange(Hex(Value(words, "hex=")));
                    break;
                case "RAYEND":
                    Get().Ends.AddRange(Hex(Value(words, "hex=")));
                    break;
                case "RAYOUT":
                    Get().Results.AddRange(Hex(Value(words, "hex=")));
                    break;
            }
        }

        capture.TypeMemory = [.. type];
        capture.RecordMemory = [.. record];
        capture.Ticks.AddRange(bySequence.Values.Where(static tick => tick.Sequence > 0).OrderBy(static tick => tick.Sequence));
        foreach (Tick tick in capture.Ticks)
        {
            tick.Seal();
        }

        return capture;
    }

    private static void Append(byte[] target, int offset, byte[] bytes) => bytes.CopyTo(target, offset);

    private static string Value(string[] words, string key) =>
        words.First(word => word.StartsWith(key, StringComparison.Ordinal))[key.Length..];

    private static int Int(string text) => int.Parse(text, CultureInfo.InvariantCulture);

    private static float Bits(string hex) =>
        BitConverter.Int32BitsToSingle(unchecked((int)uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));

    private static byte[] Hex(string text) => Convert.FromHexString(text);

    /// <summary>One retail car tick.</summary>
    internal sealed class Tick(int sequence)
    {
        public int Sequence { get; } = sequence;

        public int AutoTick { get; set; }

        public byte[] Entry { get; } = new byte[CarBytes];

        public List<Force> Forces { get; } = [];

        public int RayCount { get; set; }

        /// <summary>Gets the ray starts, 24 bytes (a double3) each.</summary>
        public byte[] RayStarts { get; private set; } = [];

        /// <summary>Gets the ray ends.</summary>
        public byte[] RayEnds { get; private set; } = [];

        /// <summary>Gets the ray results, 112 bytes each.</summary>
        public byte[] RayResults { get; private set; } = [];

        /// <summary>Gets how many leading bytes of each ray buffer the probe did not capture.</summary>
        /// <remarks>
        /// The first probe build read all three buffers 4 bytes late and dumped 96 spare result bytes, which is
        /// how such a capture is recognised. Those 4 bytes are the low half of the first start's (and end's) X
        /// and the first result's struck-object pointer, which the wheel rays never set.
        /// </remarks>
        public int UnknownRayPrefix { get; private set; }

        internal List<byte> Starts { get; } = [];

        internal List<byte> Ends { get; } = [];

        internal List<byte> Results { get; } = [];

        internal void Seal()
        {
            UnknownRayPrefix = Results.Count == 96 + (RayCount * 112) ? 4 : 0;
            RayStarts = [.. new byte[UnknownRayPrefix], .. Starts];
            RayEnds = [.. new byte[UnknownRayPrefix], .. Ends];
            RayResults = [.. new byte[UnknownRayPrefix], .. Results];
        }

        public float Float(int offset) => BitConverter.ToSingle(Entry, offset);

        public double Double(int offset) => BitConverter.ToDouble(Entry, offset);

        public int Int32(int offset) => BitConverter.ToInt32(Entry, offset);
    }

    /// <summary>One force pushed onto the body: the caller, the force and its application point, as floats.</summary>
    internal readonly record struct Force(int Caller, float[] Vector, float[] Point);
}
