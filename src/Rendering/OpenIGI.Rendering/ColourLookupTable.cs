using System.Globalization;
using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Rendering;

/// <summary>
/// An authored colour grade, as a cube of output colours indexed by input colour.
/// </summary>
/// <remarks>
/// <para>
/// The grading knobs — <c>r_contrast</c>, <c>r_saturation</c> — do this job procedurally, one named
/// operation at a time. A lookup table is the same job with the answer written down instead: whatever
/// the grade does to a colour, it does the same thing to that colour every time, so the whole of it can
/// be sampled onto a grid once and read back with three coordinates. That buys two things the knobs
/// cannot give. Any grade at all fits, including ones with no name and no formula, and the cost is one
/// texture fetch however complicated it was to author.
/// </para>
/// <para>
/// <b>Why 32 and not 64.</b> The table is trilinearly interpolated, so what it must resolve is not the
/// colour but the <em>curvature</em> of the grade between samples, and a grade is smooth almost
/// everywhere. 32³ is 32,768 entries and the industry's default for exactly this reason; 64³ is eight
/// times the memory to describe the same curve. A grade with a hard break in it — a key, a hue
/// isolation — is the case that wants more, and this reads whatever size the file declares.
/// </para>
/// <para>
/// <b>The <c>.cube</c> format</b>, because it is text. Every colour tool exports it, and it can be read
/// and corrected in a diff — which the alternative, a PNG strip of the cube's slices, cannot be. That
/// matters more than it sounds: a grade is content, and content that can only be inspected by opening
/// it in an image editor is content nobody reviews. Lines are <c>R G B</c> triples in 0–1, red varying
/// fastest, then green, then blue.
/// </para>
/// </remarks>
public sealed class ColourLookupTable
{
    private const string LogCategory = "Grade";

    /// <summary>The largest cube edge that will be read.</summary>
    /// <remarks>
    /// 64³ is a quarter of a million entries and already more than a smooth grade needs; past that a
    /// file is far likelier to be malformed than to be a grade somebody meant.
    /// </remarks>
    public const int MaxSize = 64;

    /// <summary>The smallest cube edge that describes anything.</summary>
    /// <remarks>Below two there is nothing to interpolate between and the table is a single colour.</remarks>
    public const int MinSize = 2;

    private readonly float[] _entries;

    private ColourLookupTable(string name, int size, float[] entries)
    {
        Name = name;
        Size = size;
        _entries = entries;
    }

    /// <summary>Gets what the table is called — its file name, for logging and for <c>r_lut</c>.</summary>
    public string Name { get; }

    /// <summary>Gets the cube's edge length in samples.</summary>
    public int Size { get; }

    /// <summary>Gets the table as RGB triples, red varying fastest.</summary>
    /// <remarks>Length is <see cref="Size"/> cubed times three.</remarks>
    public ReadOnlySpan<float> Entries => _entries;

    /// <summary>Builds the table that changes nothing.</summary>
    /// <param name="size">The cube's edge length.</param>
    /// <returns>A table whose output colour is its input colour.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is outside the supported range.</exception>
    /// <remarks>
    /// Worth having as more than a curiosity: sampling it is the test that the sampler's coordinate
    /// mapping is right. A LUT read with the naive <c>colour</c> instead of the half-texel inset
    /// <c>colour × (N−1)/N + 0.5/N</c> still looks plausible on a real grade and is visibly wrong on
    /// this one, because here any error is the whole of the difference.
    /// </remarks>
    public static ColourLookupTable Identity(int size = 32)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, MinSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, MaxSize);

        float[] entries = new float[size * size * size * 3];
        float step = 1f / (size - 1);
        int at = 0;

        for (int b = 0; b < size; b++)
        {
            for (int g = 0; g < size; g++)
            {
                for (int r = 0; r < size; r++)
                {
                    entries[at++] = r * step;
                    entries[at++] = g * step;
                    entries[at++] = b * step;
                }
            }
        }

        return new ColourLookupTable("identity", size, entries);
    }

    /// <summary>Reads a <c>.cube</c> file.</summary>
    /// <param name="path">The file.</param>
    /// <param name="logger">Where to report a file that is not a usable table.</param>
    /// <returns>The table, or <see langword="null"/> when the file is missing or malformed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Never throws for bad content. A grade is a look rather than a mechanism: a level that cannot
    /// find its grade should render ungraded, not fail to render.
    /// </remarks>
    public static ColourLookupTable? Load(string path, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        IOpenIgiLogger log = logger ?? NullLogger.Instance;

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return Parse(Path.GetFileNameWithoutExtension(path), File.ReadAllLines(path), log);
        }
        catch (IOException ex)
        {
            log.Warning(LogCategory, $"Could not read the colour table '{path}': {ex.Message}");
            return null;
        }
    }

    /// <summary>Parses the lines of a <c>.cube</c> file.</summary>
    /// <param name="name">What to call the resulting table.</param>
    /// <param name="lines">The file's lines.</param>
    /// <param name="logger">Where to report a malformed table.</param>
    /// <returns>The table, or <see langword="null"/> when the lines are not a usable one.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static ColourLookupTable? Parse(string name, IReadOnlyList<string> lines, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(lines);

        IOpenIgiLogger log = logger ?? NullLogger.Instance;

        int size = -1;
        float[]? entries = null;
        int at = 0;

        foreach (string raw in lines)
        {
            // A comment or a blank. The format's comment marker is '#', and files exported by tools
            // routinely open with several lines of them.
            ReadOnlySpan<char> line = raw.AsSpan().Trim();
            if (line.IsEmpty || line[0] == '#')
            {
                continue;
            }

            if (line.StartsWith("LUT_3D_SIZE", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadSize(line, out size))
                {
                    log.Warning(LogCategory, $"The colour table '{name}' declares an unusable LUT_3D_SIZE.");
                    return null;
                }

                entries = new float[size * size * size * 3];
                continue;
            }

            // A 1D table is a different thing with the same extension, and silently reading it as a
            // cube would produce a grade nobody authored.
            if (line.StartsWith("LUT_1D_SIZE", StringComparison.OrdinalIgnoreCase))
            {
                log.Warning(LogCategory, $"The colour table '{name}' is a 1D LUT; only 3D tables are read.");
                return null;
            }

            // TITLE, DOMAIN_MIN, DOMAIN_MAX and anything else the format allows. The domain is assumed
            // to be 0-1, which is what every grade authored for display-referred footage uses.
            if (!char.IsAsciiDigit(line[0]) && line[0] != '-' && line[0] != '+' && line[0] != '.')
            {
                continue;
            }

            if (entries is null)
            {
                log.Warning(LogCategory, $"The colour table '{name}' has entries before its LUT_3D_SIZE.");
                return null;
            }

            if (at + 3 > entries.Length)
            {
                log.Warning(
                    LogCategory,
                    $"The colour table '{name}' holds more entries than its declared size of {size}.");
                return null;
            }

            if (!TryReadTriple(line, entries.AsSpan(at, 3)))
            {
                log.Warning(LogCategory, $"The colour table '{name}' has a line that is not three numbers.");
                return null;
            }

            at += 3;
        }

        if (entries is null)
        {
            log.Warning(LogCategory, $"The colour table '{name}' never declared a LUT_3D_SIZE.");
            return null;
        }

        // Short is as wrong as long, and worse: a table missing its last plane reads as a grade that
        // goes black in the highlights, which looks like a rendering fault rather than a bad file.
        if (at != entries.Length)
        {
            log.Warning(
                LogCategory,
                $"The colour table '{name}' declared {size}³ = {entries.Length / 3} entries and held {at / 3}.");
            return null;
        }

        log.Information(LogCategory, $"Colour table '{name}' loaded, {size}³.");
        return new ColourLookupTable(name, size, entries);
    }

    private static bool TryReadSize(ReadOnlySpan<char> line, out int size)
    {
        size = -1;

        int space = line.IndexOf(' ');
        if (space < 0
            || !int.TryParse(line[(space + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out size))
        {
            return false;
        }

        return size is >= MinSize and <= MaxSize;
    }

    private static bool TryReadTriple(ReadOnlySpan<char> line, Span<float> triple)
    {
        int written = 0;

        foreach (Range part in line.Split(' '))
        {
            ReadOnlySpan<char> token = line[part].Trim();
            if (token.IsEmpty)
            {
                continue;
            }

            if (written == 3
                || !float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                return false;
            }

            // Clamped rather than rejected: a grade exported with a hair of overshoot at the top end is
            // a normal thing for a tool to write, and the target is an 8-bit display either way.
            triple[written++] = Math.Clamp(value, 0f, 1f);
        }

        return written == 3;
    }
}
