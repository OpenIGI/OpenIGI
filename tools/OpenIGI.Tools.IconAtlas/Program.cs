using System.Globalization;
using System.Text;
using System.Xml.Linq;
using OpenIGI.Tools;
using SkiaSharp;

// Bakes the touch controls' icons into the atlas the game draws them from.
//
//   dotnet run --project tools/OpenIGI.Tools.IconAtlas                     rewrite the committed atlas
//   dotnet run --project tools/OpenIGI.Tools.IconAtlas -- --sheet <dir> <png>
//                                                                          every SVG in a folder on one named sheet, to choose from
//
// The sources are SVGs beside a manifest (assets/touch-icons/manifest.txt: "Name=file" a line, in the atlas's order). Each is
// drawn white on nothing into a square tile, and only its coverage is kept: the game tints a tile as it draws it. The atlas is
// committed, so a build needs neither this tool nor an SVG reader, and the game never parses a path.

return ToolHost.Run("IconAtlas", args, Run);

static int Run(ToolContext ctx)
{
    const int Tile = 128;
    string[] arguments = [.. ctx.Arguments];
    if (arguments.Length == 3 && arguments[0] == "--sheet")
    {
        return Sheet(ctx, arguments[1], arguments[2]);
    }

    string root = Locate();
    string sources = Path.Combine(root, "assets", "touch-icons");
    string output = Path.Combine(root, "src", "Game", "OpenIGI.Presentation", "Hud", "Remastered", "Touch", "touch-icons.bin");

    List<(string Name, string File)> entries = [];
    foreach (string line in File.ReadAllLines(Path.Combine(sources, "manifest.txt")))
    {
        string text = line.Trim();
        if (text.Length == 0 || text[0] == '#')
        {
            continue;
        }

        int equals = text.IndexOf('=', StringComparison.Ordinal);
        if (equals <= 0)
        {
            ctx.Fail($"manifest.txt: '{text}' is not Name=file.");
            return ToolExitCode.Failure;
        }

        entries.Add((text[..equals].Trim(), text[(equals + 1)..].Trim()));
    }

    // "OITI", the tile's size, the count, each name, then each tile's coverage, a byte a pixel, row by row.
    using MemoryStream stream = new();
    using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
    {
        writer.Write("OITI"u8);
        writer.Write((ushort)Tile);
        writer.Write((ushort)entries.Count);
        foreach ((string name, _) in entries)
        {
            writer.Write(name);
        }

        foreach ((string name, string file) in entries)
        {
            string path = Path.Combine(sources, file + ".svg");
            if (!File.Exists(path))
            {
                ctx.Fail($"{name}: no {path}.");
                return ToolExitCode.Failure;
            }

            using SKBitmap tile = Render(path, Tile);
            for (int y = 0; y < Tile; y++)
            {
                for (int x = 0; x < Tile; x++)
                {
                    writer.Write(tile.GetPixel(x, y).Alpha);
                }
            }
        }
    }

    File.WriteAllBytes(output, stream.ToArray());
    ctx.Info($"{entries.Count} icon(s) at {Tile}x{Tile} -> {output} ({stream.Length} bytes)");
    return ToolExitCode.Ok;
}

// Every SVG of a folder on one sheet, each over its file's name: for choosing which icon a button gets.
static int Sheet(ToolContext ctx, string folder, string png)
{
    const int Cell = 150, Icon = 96, Columns = 9;
    string[] files = [.. Directory.GetFiles(folder, "*.svg").Order(StringComparer.Ordinal)];
    int rows = (files.Length + Columns - 1) / Columns;
    using SKBitmap sheet = new(Columns * Cell, rows * Cell);
    using SKCanvas canvas = new(sheet);
    canvas.Clear(new SKColor(24, 26, 30));
    using SKFont font = new(SKTypeface.Default, 13f);
    using SKPaint ink = new() { Color = SKColors.White, IsAntialias = true };
    for (int i = 0; i < files.Length; i++)
    {
        using SKBitmap tile = Render(files[i], Icon);
        float x = (i % Columns) * Cell, y = (i / Columns) * Cell;
        canvas.DrawBitmap(tile, x + ((Cell - Icon) * 0.5f), y + 10f);
        string name = Path.GetFileNameWithoutExtension(files[i]);
        canvas.DrawText(name, x + (Cell * 0.5f), y + Icon + 34f, SKTextAlign.Center, font, ink);
    }

    using SKData data = sheet.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(png, data.ToArray());
    ctx.Info($"{files.Length} icon(s) -> {png}");
    return ToolExitCode.Ok;
}

// One SVG drawn white into a square tile: its paths and rectangles, filled and stroked as they say, the view box fitted to
// the tile with a small margin so a round cap at the box's edge is not cut.
static SKBitmap Render(string path, int size)
{
    XDocument document = XDocument.Load(path);
    XElement svg = document.Root ?? throw new InvalidDataException($"{path} has no root.");
    float[] box = [.. (svg.Attribute("viewBox")?.Value ?? "0 0 24 24")
        .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
        .Select(static v => float.Parse(v, CultureInfo.InvariantCulture))];
    const float Margin = 0.04f;
    float scale = size * (1f - (Margin * 2f)) / MathF.Max(box[2], box[3]);

    SKBitmap bitmap = new(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
    using SKCanvas canvas = new(bitmap);
    canvas.Clear(SKColors.Transparent);
    canvas.Translate(
        (size * 0.5f) - ((box[0] + (box[2] * 0.5f)) * scale),
        (size * 0.5f) - ((box[1] + (box[3] * 0.5f)) * scale));
    canvas.Scale(scale);

    foreach (XElement element in svg.Descendants())
    {
        using SKPath? shape = element.Name.LocalName switch
        {
            "path" when element.Attribute("d") is { } d => SKPath.ParseSvgPathData(d.Value),
            "rect" => Rect(element),
            _ => null,
        };
        if (shape is null)
        {
            continue;
        }

        string fill = Inherited(element, "fill") ?? "#000000";
        string stroke = Inherited(element, "stroke") ?? "none";
        if (fill != "none")
        {
            shape.FillType = Inherited(element, "fill-rule") == "evenodd" ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
            using SKPaint paint = new() { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Fill };
            canvas.DrawPath(shape, paint);
        }

        if (stroke != "none")
        {
            using SKPaint paint = new()
            {
                Color = SKColors.White,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Number(Inherited(element, "stroke-width"), 1f),
                StrokeCap = Inherited(element, "stroke-linecap") switch { "round" => SKStrokeCap.Round, "square" => SKStrokeCap.Square, _ => SKStrokeCap.Butt },
                StrokeJoin = Inherited(element, "stroke-linejoin") switch { "round" => SKStrokeJoin.Round, "bevel" => SKStrokeJoin.Bevel, _ => SKStrokeJoin.Miter },
            };
            canvas.DrawPath(shape, paint);
        }
    }

    return bitmap;
}

static SKPath Rect(XElement element)
{
    float x = Number(element.Attribute("x")?.Value, 0f), y = Number(element.Attribute("y")?.Value, 0f);
    float width = Number(element.Attribute("width")?.Value, 0f), height = Number(element.Attribute("height")?.Value, 0f);
    float rx = Number(element.Attribute("rx")?.Value, 0f);
    float ry = Number(element.Attribute("ry")?.Value, rx);
    SKPath path = new();
    path.AddRoundRect(new SKRect(x, y, x + width, y + height), rx, ry);
    return path;
}

// A presentation attribute, the element's own or the nearest set above it.
static string? Inherited(XElement element, string name)
{
    for (XElement? at = element; at is not null; at = at.Parent)
    {
        if (at.Attribute(name) is { } attribute)
        {
            return attribute.Value;
        }
    }

    return null;
}

static float Number(string? text, float fallback) =>
    text is not null && float.TryParse(text.Replace("px", string.Empty, StringComparison.Ordinal), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
        ? value
        : fallback;

// The repository's root: the folder above this one that holds the solution.
static string Locate()
{
    for (DirectoryInfo? at = new(AppContext.BaseDirectory); at is not null; at = at.Parent)
    {
        if (File.Exists(Path.Combine(at.FullName, "OpenIGI.slnx")))
        {
            return at.FullName;
        }
    }

    throw new InvalidOperationException("No OpenIGI.slnx above the tool.");
}
