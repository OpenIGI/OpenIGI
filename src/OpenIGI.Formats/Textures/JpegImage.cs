using System.Buffers.Binary;
using OpenIGI.Core.Graphics;

namespace OpenIGI.Formats.Textures;

/// <summary>
/// A JPEG image, as I.G.I. 2 ships its terrain textures.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here reproduces game behaviour.</b> I.G.I. 2 does not decode JPEG itself:
/// <c>Direct3DTexture_DownloadFromFile</c> (<c>igi2ed 0x662FE0</c>) hands the raw bytes to
/// <c>D3DXCreateTextureFromFileInMemoryEx</c>, and the shipped binary carries a statically linked
/// libjpeg inside D3DX to do the work (<c>?jpeg_read_header@D3DX@@</c>,
/// <c>?jpeg_start_decompress@D3DX@@</c>). So the specification is the format's own, and correctness is
/// measured against what any conforming decoder produces rather than against the original.
/// </para>
/// <para>
/// <b>Progressive is not optional here.</b> Of the 83 files the game ships, <b>68 are progressive</b>
/// and 15 baseline — so a baseline-only reader would decode fewer than a fifth of the ground's
/// textures. Both are implemented, along with restart intervals, which 15 of the files use.
/// </para>
/// <para>
/// The corpus is 8-bit, three-component, and entirely 4:4:4 — no chroma subsampling anywhere. General
/// sampling factors are still handled, with nearest-neighbour upsampling, because a reader that assumed
/// the corpus's shape would fail on the first file that did not share it and would do so by producing a
/// sheared image rather than an error.
/// </para>
/// </remarks>
public static class JpegImage
{
    /// <summary>Gets a value indicating whether the bytes begin with a JPEG marker.</summary>
    /// <param name="data">The file bytes.</param>
    /// <returns><see langword="true"/> when they start with <c>FF D8</c>.</returns>
    public static bool IsJpeg(ReadOnlySpan<byte> data) =>
        data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8;

    /// <summary>Decodes a JPEG into opaque <see cref="Bgra32"/> pixels.</summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>The image, with one mip level.</returns>
    /// <exception cref="TextureFormatException">The bytes are not a JPEG this reader handles.</exception>
    public static TextureImage Read(ReadOnlySpan<byte> data)
    {
        Decoder decoder = new(data);
        return decoder.Decode();
    }

    /// <summary>The order a block's 64 coefficients are stored in.</summary>
    internal static ReadOnlySpan<byte> ZigZag =>
    [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
    ];

    private sealed class Component
    {
        public int Id;
        public int H = 1;
        public int V = 1;
        public int QuantTable;
        public int DcTable;
        public int AcTable;
        public int Predictor;
        public int BlocksPerLine;
        public int BlocksPerColumn;
        public short[] Coefficients = [];
        public byte[] Pixels = [];
    }

    private sealed class HuffmanTable
    {
        private readonly byte[] _values;
        private readonly int[] _minCode = new int[17];
        private readonly int[] _maxCode = new int[17];
        private readonly int[] _valuePointer = new int[17];

        public HuffmanTable(ReadOnlySpan<byte> counts, byte[] values)
        {
            _values = values;

            int code = 0;
            int k = 0;
            for (int length = 1; length <= 16; length++)
            {
                _valuePointer[length] = k;
                _minCode[length] = code;
                code += counts[length - 1];
                k += counts[length - 1];
                _maxCode[length] = counts[length - 1] == 0 ? -1 : code - 1;
                code <<= 1;
            }
        }

        public int Decode(BitReader bits)
        {
            int code = 0;
            for (int length = 1; length <= 16; length++)
            {
                code = (code << 1) | bits.ReadBit();
                if (_maxCode[length] >= 0 && code <= _maxCode[length])
                {
                    int index = _valuePointer[length] + code - _minCode[length];
                    if (index < 0 || index >= _values.Length)
                    {
                        throw new TextureFormatException("A JPEG Huffman code resolved outside its table.");
                    }

                    return _values[index];
                }
            }

            throw new TextureFormatException("A JPEG Huffman code is longer than sixteen bits.");
        }
    }

    /// <summary>Reads the entropy-coded bit stream, honouring byte stuffing and restart markers.</summary>
    private sealed class BitReader(ReadOnlyMemory<byte> data, int offset)
    {
        private readonly ReadOnlyMemory<byte> _data = data;
        private int _position = offset;
        private int _bits;
        private int _count;

        public int Position => _position;

        public int ReadBit()
        {
            if (_count == 0)
            {
                ReadOnlySpan<byte> span = _data.Span;
                if (_position >= span.Length)
                {
                    // Running off the end mid-block is what a truncated file does; feeding zeroes lets
                    // the rest of the image survive, which is what every decoder does here.
                    return 0;
                }

                int value = span[_position++];
                if (value == 0xFF)
                {
                    // A stuffed zero is data; anything else is a marker and this scan is over. Step back
                    // onto the FF when it is a marker, so a following Restart finds *this* one rather
                    // than hunting past it and losing a whole interval's worth of blocks.
                    int next = _position < span.Length ? span[_position] : 0xD9;
                    if (next == 0)
                    {
                        _position++;
                    }
                    else
                    {
                        _position--;
                        return 0;
                    }
                }

                _bits = value;
                _count = 8;
            }

            _count--;
            return (_bits >> _count) & 1;
        }

        public int ReadBits(int length)
        {
            int value = 0;
            for (int i = 0; i < length; i++)
            {
                value = (value << 1) | ReadBit();
            }

            return value;
        }

        /// <summary>Drops any part-used byte and steps over a restart marker.</summary>
        public void Restart()
        {
            _count = 0;
            ReadOnlySpan<byte> span = _data.Span;
            while (_position + 1 < span.Length)
            {
                if (span[_position] == 0xFF && span[_position + 1] >= 0xD0 && span[_position + 1] <= 0xD7)
                {
                    _position += 2;
                    return;
                }

                _position++;
            }
        }
    }

    private ref struct Decoder(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private readonly ushort[][] _quant = new ushort[4][];
        private readonly HuffmanTable?[] _dc = new HuffmanTable?[4];
        private readonly HuffmanTable?[] _ac = new HuffmanTable?[4];
        private Component[] _components = [];
        private int _width;
        private int _height;
        private int _maxH = 1;
        private int _maxV = 1;
        private int _mcusPerLine;
        private int _mcusPerColumn;
        private int _restartInterval;
        private bool _progressive;
        private int _eobRun;

        public TextureImage Decode()
        {
            if (!IsJpeg(_data))
            {
                throw new TextureFormatException("A JPEG must begin with FF D8.");
            }

            int offset = 2;
            while (offset + 1 < _data.Length)
            {
                if (_data[offset] != 0xFF)
                {
                    offset++;
                    continue;
                }

                int marker = _data[offset + 1];
                offset += 2;

                if (marker is 0xD8 or 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                {
                    continue;
                }

                if (marker == 0xD9)
                {
                    break;
                }

                if (offset + 2 > _data.Length)
                {
                    break;
                }

                int length = BinaryPrimitives.ReadUInt16BigEndian(_data[offset..]);
                if (length < 2 || offset + length > _data.Length)
                {
                    throw new TextureFormatException($"A JPEG segment at {offset} declares {length} bytes it does not have.");
                }

                ReadOnlySpan<byte> segment = _data.Slice(offset + 2, length - 2);
                switch (marker)
                {
                    case 0xDB:
                        ReadQuantisationTables(segment);
                        break;
                    case 0xC4:
                        ReadHuffmanTables(segment);
                        break;
                    case 0xDD:
                        _restartInterval = BinaryPrimitives.ReadUInt16BigEndian(segment);
                        break;
                    case 0xC0:
                    case 0xC1:
                        ReadFrame(segment, progressive: false);
                        break;
                    case 0xC2:
                        ReadFrame(segment, progressive: true);
                        break;
                    case 0xC3:
                    case 0xC5:
                    case 0xC6:
                    case 0xC7:
                    case 0xC9:
                    case 0xCA:
                    case 0xCB:
                    case 0xCD:
                    case 0xCE:
                    case 0xCF:
                        throw new TextureFormatException(
                            $"A JPEG of type FF{marker:X2} — lossless, hierarchical or arithmetic — is not one this reader handles.");
                    case 0xDA:
                        offset = ReadScan(segment, offset + length);
                        continue;
                    default:
                        break;
                }

                offset += length;
            }

            return Finish();
        }

        private void ReadQuantisationTables(ReadOnlySpan<byte> segment)
        {
            int i = 0;
            while (i < segment.Length)
            {
                int precision = segment[i] >> 4;
                int id = segment[i] & 15;
                i++;

                if (id >= 4)
                {
                    throw new TextureFormatException($"A JPEG names quantisation table {id}.");
                }

                ushort[] table = new ushort[64];
                for (int k = 0; k < 64; k++)
                {
                    table[ZigZag[k]] = precision == 0
                        ? segment[i + k]
                        : BinaryPrimitives.ReadUInt16BigEndian(segment[(i + (k * 2))..]);
                }

                i += precision == 0 ? 64 : 128;
                _quant[id] = table;
            }
        }

        private void ReadHuffmanTables(ReadOnlySpan<byte> segment)
        {
            int i = 0;
            while (i < segment.Length)
            {
                int kind = segment[i] >> 4;
                int id = segment[i] & 15;
                i++;

                if (id >= 4)
                {
                    throw new TextureFormatException($"A JPEG names Huffman table {id}.");
                }

                ReadOnlySpan<byte> counts = segment.Slice(i, 16);
                i += 16;

                int total = 0;
                foreach (byte count in counts)
                {
                    total += count;
                }

                byte[] values = segment.Slice(i, total).ToArray();
                i += total;

                HuffmanTable table = new(counts, values);
                if (kind == 0)
                {
                    _dc[id] = table;
                }
                else
                {
                    _ac[id] = table;
                }
            }
        }

        private void ReadFrame(ReadOnlySpan<byte> segment, bool progressive)
        {
            if (segment[0] != 8)
            {
                throw new TextureFormatException($"A JPEG of {segment[0]}-bit samples is not one this reader handles.");
            }

            _progressive = progressive;
            _height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
            _width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);

            if (_width <= 0 || _height <= 0)
            {
                throw new TextureFormatException($"A JPEG is {_width}x{_height}.");
            }

            int count = segment[5];
            _components = new Component[count];
            for (int c = 0; c < count; c++)
            {
                ReadOnlySpan<byte> entry = segment.Slice(6 + (c * 3), 3);
                _components[c] = new Component
                {
                    Id = entry[0],
                    H = entry[1] >> 4,
                    V = entry[1] & 15,
                    QuantTable = entry[2],
                };

                _maxH = Math.Max(_maxH, _components[c].H);
                _maxV = Math.Max(_maxV, _components[c].V);
            }

            _mcusPerLine = (_width + (8 * _maxH) - 1) / (8 * _maxH);
            _mcusPerColumn = (_height + (8 * _maxV) - 1) / (8 * _maxV);

            foreach (Component component in _components)
            {
                component.BlocksPerLine = _mcusPerLine * component.H;
                component.BlocksPerColumn = _mcusPerColumn * component.V;
                component.Coefficients = new short[component.BlocksPerLine * component.BlocksPerColumn * 64];
            }
        }

        private int ReadScan(ReadOnlySpan<byte> segment, int dataStart)
        {
            if (_components.Length == 0)
            {
                throw new TextureFormatException("A JPEG scan arrived before its frame.");
            }

            int count = segment[0];
            Component[] scan = new Component[count];
            for (int i = 0; i < count; i++)
            {
                int id = segment[1 + (i * 2)];
                int tables = segment[2 + (i * 2)];
                Component component = Array.Find(_components, c => c.Id == id)
                    ?? throw new TextureFormatException($"A JPEG scan names component {id}, which its frame does not.");

                component.DcTable = tables >> 4;
                component.AcTable = tables & 15;
                scan[i] = component;
            }

            int spectralStart = segment[1 + (count * 2)];
            int spectralEnd = segment[2 + (count * 2)];
            int approximation = segment[3 + (count * 2)];
            int high = approximation >> 4;
            int low = approximation & 15;

            BitReader bits = new(_data.ToArray(), dataStart);
            _eobRun = 0;
            foreach (Component component in scan)
            {
                component.Predictor = 0;
            }

            DecodeScan(bits, scan, spectralStart, spectralEnd, high, low);

            // Resume marker scanning from wherever the entropy data ended.
            int position = Math.Max(bits.Position, dataStart);
            while (position + 1 < _data.Length)
            {
                if (_data[position] == 0xFF && _data[position + 1] != 0 && (_data[position + 1] < 0xD0 || _data[position + 1] > 0xD7))
                {
                    return position;
                }

                position++;
            }

            return _data.Length;
        }

        private void DecodeScan(BitReader bits, Component[] scan, int spectralStart, int spectralEnd, int high, int low)
        {
            bool single = scan.Length == 1;
            int mcus = single
                ? ((scan[0].BlocksPerLine + 0) * scan[0].BlocksPerColumn)
                : _mcusPerLine * _mcusPerColumn;

            int sinceRestart = 0;
            for (int mcu = 0; mcu < mcus; mcu++)
            {
                if (_restartInterval > 0 && sinceRestart == _restartInterval)
                {
                    bits.Restart();
                    sinceRestart = 0;
                    _eobRun = 0;
                    foreach (Component component in scan)
                    {
                        component.Predictor = 0;
                    }
                }

                sinceRestart++;

                if (single)
                {
                    Component component = scan[0];
                    int row = mcu / component.BlocksPerLine;
                    int column = mcu % component.BlocksPerLine;
                    DecodeBlock(bits, component, (row * component.BlocksPerLine) + column, spectralStart, spectralEnd, high, low);
                }
                else
                {
                    int mcuRow = mcu / _mcusPerLine;
                    int mcuColumn = mcu % _mcusPerLine;
                    foreach (Component component in scan)
                    {
                        for (int v = 0; v < component.V; v++)
                        {
                            for (int h = 0; h < component.H; h++)
                            {
                                int row = (mcuRow * component.V) + v;
                                int column = (mcuColumn * component.H) + h;
                                DecodeBlock(bits, component, (row * component.BlocksPerLine) + column, spectralStart, spectralEnd, high, low);
                            }
                        }
                    }
                }
            }
        }

        private void DecodeBlock(BitReader bits, Component component, int block, int spectralStart, int spectralEnd, int high, int low)
        {
            if (block < 0 || (block * 64) + 64 > component.Coefficients.Length)
            {
                return;
            }

            Span<short> coefficients = component.Coefficients.AsSpan(block * 64, 64);

            if (!_progressive)
            {
                DecodeBaselineBlock(bits, component, coefficients);
                return;
            }

            if (spectralStart == 0)
            {
                if (high == 0)
                {
                    DecodeDcFirst(bits, component, coefficients, low);
                }
                else
                {
                    coefficients[0] |= (short)(bits.ReadBit() << low);
                }

                return;
            }

            if (high == 0)
            {
                DecodeAcFirst(bits, component, coefficients, spectralStart, spectralEnd, low);
            }
            else
            {
                DecodeAcRefine(bits, component, coefficients, spectralStart, spectralEnd, low);
            }
        }

        private void DecodeBaselineBlock(BitReader bits, Component component, Span<short> coefficients)
        {
            HuffmanTable dc = _dc[component.DcTable] ?? throw new TextureFormatException("A JPEG scan names an absent DC table.");
            HuffmanTable ac = _ac[component.AcTable] ?? throw new TextureFormatException("A JPEG scan names an absent AC table.");

            int size = dc.Decode(bits);
            int diff = size == 0 ? 0 : Extend(bits.ReadBits(size), size);
            component.Predictor += diff;
            coefficients[0] = (short)component.Predictor;

            int k = 1;
            while (k < 64)
            {
                int symbol = ac.Decode(bits);
                int run = symbol >> 4;
                int magnitude = symbol & 15;

                if (magnitude == 0)
                {
                    if (run != 15)
                    {
                        break;
                    }

                    k += 16;
                    continue;
                }

                k += run;
                if (k >= 64)
                {
                    break;
                }

                coefficients[ZigZag[k]] = (short)Extend(bits.ReadBits(magnitude), magnitude);
                k++;
            }
        }

        private void DecodeDcFirst(BitReader bits, Component component, Span<short> coefficients, int low)
        {
            HuffmanTable dc = _dc[component.DcTable] ?? throw new TextureFormatException("A JPEG scan names an absent DC table.");

            int size = dc.Decode(bits);
            int diff = size == 0 ? 0 : Extend(bits.ReadBits(size), size);
            component.Predictor += diff;
            coefficients[0] = (short)(component.Predictor << low);
        }

        private void DecodeAcFirst(BitReader bits, Component component, Span<short> coefficients, int start, int end, int low)
        {
            if (_eobRun > 0)
            {
                _eobRun--;
                return;
            }

            HuffmanTable ac = _ac[component.AcTable] ?? throw new TextureFormatException("A JPEG scan names an absent AC table.");

            int k = start;
            while (k <= end)
            {
                int symbol = ac.Decode(bits);
                int run = symbol >> 4;
                int magnitude = symbol & 15;

                if (magnitude == 0)
                {
                    if (run != 15)
                    {
                        _eobRun = (1 << run) - 1;
                        if (run > 0)
                        {
                            _eobRun += bits.ReadBits(run);
                        }

                        break;
                    }

                    k += 16;
                    continue;
                }

                k += run;
                if (k > end || k >= 64)
                {
                    break;
                }

                coefficients[ZigZag[k]] = (short)(Extend(bits.ReadBits(magnitude), magnitude) << low);
                k++;
            }
        }

        private void DecodeAcRefine(BitReader bits, Component component, Span<short> coefficients, int start, int end, int low)
        {
            HuffmanTable ac = _ac[component.AcTable] ?? throw new TextureFormatException("A JPEG scan names an absent AC table.");

            int plus = 1 << low;
            int minus = -1 << low;
            int k = start;

            if (_eobRun <= 0)
            {
                while (k <= end)
                {
                    int symbol = ac.Decode(bits);
                    int run = symbol >> 4;
                    int magnitude = symbol & 15;
                    int value = 0;

                    if (magnitude == 0)
                    {
                        if (run != 15)
                        {
                            // No minus one here, unlike the first-pass form: this block is counted by
                            // the decrement at the end of the refinement below. Applying both leaves
                            // every end-of-block run one short, which desynchronises the scan a band or
                            // two in and then fails as an over-long Huffman code somewhere unrelated.
                            _eobRun = 1 << run;
                            if (run > 0)
                            {
                                _eobRun += bits.ReadBits(run);
                            }

                            break;
                        }
                    }
                    else
                    {
                        value = bits.ReadBit() != 0 ? plus : minus;
                    }

                    while (k <= end)
                    {
                        int index = ZigZag[k];
                        if (coefficients[index] != 0)
                        {
                            // An already-non-zero coefficient takes a correction bit rather than a run.
                            if (bits.ReadBit() != 0 && (coefficients[index] & plus) == 0)
                            {
                                coefficients[index] += (short)(coefficients[index] >= 0 ? plus : minus);
                            }
                        }
                        else
                        {
                            if (run == 0)
                            {
                                if (value != 0)
                                {
                                    coefficients[index] = (short)value;
                                }

                                k++;
                                break;
                            }

                            run--;
                        }

                        k++;
                    }
                }
            }

            if (_eobRun > 0)
            {
                // The rest of the band is a run of end-of-block: only correction bits remain.
                while (k <= end)
                {
                    int index = ZigZag[k];
                    if (coefficients[index] != 0 && bits.ReadBit() != 0 && (coefficients[index] & plus) == 0)
                    {
                        coefficients[index] += (short)(coefficients[index] >= 0 ? plus : minus);
                    }

                    k++;
                }

                _eobRun--;
            }
        }

        private TextureImage Finish()
        {
            if (_components.Length == 0)
            {
                throw new TextureFormatException("A JPEG carried no frame.");
            }

            Span<float> block = stackalloc float[64];
            Span<float> scratch = stackalloc float[64];

            foreach (Component component in _components)
            {
                ushort[] quant = _quant[component.QuantTable]
                    ?? throw new TextureFormatException($"A JPEG component names quantisation table {component.QuantTable}, which is absent.");

                int width = component.BlocksPerLine * 8;
                component.Pixels = new byte[width * component.BlocksPerColumn * 8];

                for (int by = 0; by < component.BlocksPerColumn; by++)
                {
                    for (int bx = 0; bx < component.BlocksPerLine; bx++)
                    {
                        int offset = ((by * component.BlocksPerLine) + bx) * 64;
                        for (int i = 0; i < 64; i++)
                        {
                            block[i] = component.Coefficients[offset + i] * quant[i];
                        }

                        InverseTransform(block, scratch);

                        for (int y = 0; y < 8; y++)
                        {
                            for (int x = 0; x < 8; x++)
                            {
                                int value = (int)MathF.Round(block[(y * 8) + x]) + 128;
                                component.Pixels[(((by * 8) + y) * width) + (bx * 8) + x] =
                                    (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
                            }
                        }
                    }
                }
            }

            return ToPixels();
        }

        private readonly TextureImage ToPixels()
        {
            Bgra32[] pixels = new Bgra32[_width * _height];
            Component y = _components[0];
            Component? cb = _components.Length > 1 ? _components[1] : null;
            Component? cr = _components.Length > 2 ? _components[2] : null;

            int yStride = y.BlocksPerLine * 8;
            int cbStride = cb is null ? 0 : cb.BlocksPerLine * 8;
            int crStride = cr is null ? 0 : cr.BlocksPerLine * 8;

            for (int row = 0; row < _height; row++)
            {
                for (int column = 0; column < _width; column++)
                {
                    byte luma = Sample(y.Pixels, yStride, column, row, y.H, y.V);
                    if (cb is null || cr is null)
                    {
                        pixels[(row * _width) + column] = new Bgra32(luma, luma, luma, 255);
                        continue;
                    }

                    // Nearest neighbour, which the corpus never exercises: every shipped file is 4:4:4.
                    float blue = Sample(cb.Pixels, cbStride, column, row, cb.H, cb.V) - 128f;
                    float red = Sample(cr.Pixels, crStride, column, row, cr.H, cr.V) - 128f;

                    pixels[(row * _width) + column] = new Bgra32(
                        Clamp(luma + (1.772f * blue)),
                        Clamp(luma - (0.344136f * blue) - (0.714136f * red)),
                        Clamp(luma + (1.402f * red)),
                        255);
                }
            }

            return new TextureImage(_width, _height, 1, pixels, [0]);
        }

        private readonly byte Sample(byte[] plane, int stride, int x, int y, int h, int v)
        {
            if (stride == 0 || plane.Length == 0)
            {
                return 128;
            }

            int sx = h == _maxH ? x : x * h / _maxH;
            int sy = v == _maxV ? y : y * v / _maxV;
            int index = (sy * stride) + sx;
            return index >= 0 && index < plane.Length ? plane[index] : (byte)128;
        }

        private static byte Clamp(float value) =>
            (byte)(value < 0f ? 0 : value > 255f ? 255 : (int)MathF.Round(value));

        private static int Extend(int value, int size) =>
            value < (1 << (size - 1)) ? value - (1 << size) + 1 : value;

        /// <summary>A separable two-dimensional inverse discrete cosine transform, in place.</summary>
        /// <remarks>
        /// The plain formulation rather than a fast approximation. It is not the bottleneck — a level's
        /// ground is a handful of textures — and the accurate one keeps the result inside a level or two
        /// of what any other decoder produces, which is what makes comparing against one meaningful.
        /// </remarks>
        private static void InverseTransform(Span<float> block, Span<float> temp)
        {

            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    float sum = 0f;
                    for (int u = 0; u < 8; u++)
                    {
                        sum += Cu(u) * block[(y * 8) + u] * Cosine[(x * 8) + u];
                    }

                    temp[(y * 8) + x] = sum;
                }
            }

            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    float sum = 0f;
                    for (int v = 0; v < 8; v++)
                    {
                        sum += Cu(v) * temp[(v * 8) + x] * Cosine[(y * 8) + v];
                    }

                    block[(y * 8) + x] = sum * 0.25f;
                }
            }
        }

        private static float Cu(int u) => u == 0 ? 0.70710678f : 1f;
    }

    private static readonly float[] Cosine = BuildCosine();

    private static float[] BuildCosine()
    {
        float[] table = new float[64];
        for (int x = 0; x < 8; x++)
        {
            for (int u = 0; u < 8; u++)
            {
                table[(x * 8) + u] = MathF.Cos((2 * x + 1) * u * MathF.PI / 16f);
            }
        }

        return table;
    }
}
