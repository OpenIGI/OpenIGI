using OpenIGI.Core.Graphics;
using OpenIGI.Formats;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Scripting.Qsc;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>The content browser's preview of the selected file: a picture, a model to turn, a sound to play, or text.</summary>
internal sealed partial class ContentPanel
{
    private void ReleasePreview()
    {
        _sound.Release();
        if (_image is { } image)
        {
            _images.DestroyImage(image);
            _image = null;
        }
    }

    private void DrawPreview(UiPainter painter)
    {
        UiRect right = PreviewArea;
        EditorDraw.Label(painter, Theme, _facts, new UiRect(right.X + Theme.Px(8f), _open.Bounds.Bottom + Theme.Px(4f), right.Width - Theme.Px(16f), Theme.RowHeight), Theme.TextMuted);
        if (_image is { } image)
        {
            DrawImage(painter, image, UiRect.FromEdges(right.X + Theme.Px(8f), _open.Bounds.Bottom + Theme.RowHeight + Theme.Px(8f), right.Right - Theme.Px(8f), right.Bottom - Theme.Px(8f)));
        }
        else if (_previewed is null)
        {
            const string hint = "Select a file to preview it.";
            float width = Theme.Font.MeasureWidth(hint, Theme.TextSize);
            EditorDraw.Label(painter, Theme, hint, new UiRect(right.Center.X - (width * 0.5f), right.Center.Y, width + 1f, Theme.RowHeight), Theme.TextDisabled);
        }
    }

    private void Preview(ContentNode? node)
    {
        if (node is not null)
        {
            Remember(node);
        }

        _previewed = node;
        ReleasePreview();
        _text.Visibility = Visibility.Collapsed;
        _model.Visibility = Visibility.Collapsed;
        _model.Show(null);
        _sound.Visibility = Visibility.Collapsed;
        _sound.Show(ReadOnlyMemory<byte>.Empty);
        _open.IsEnabled = node is { Entry: null, IsFolder: false } && (node.Extension == ".qvm" || ScriptPanel.IsTextFile(node.Name));
        _formName = node is null ? null : FormFor(node);
        _openForm.Text = _formName is { } form ? $"Open in {form}" : "Open in Editor";
        _openForm.Visibility = _formName is null ? Visibility.Collapsed : Visibility.Visible;
        _facts = string.Empty;
        if (node is null || node.IsFolder)
        {
            return;
        }

        ReadOnlyMemory<byte> bytes;
        if (node.Entry is { } entry)
        {
            bytes = entry.Data;
        }
        else if (!_host.TryReadFile(node.Path, out bytes))
        {
            _facts = "The file could not be read.";
            return;
        }

        _facts = $"{node.Name}  •  {Size(bytes.Length)}";

        // Each kind of file is tried in turn, and one that cannot read it does not stop the next: a script failing as a picture
        // was once all an AI script showed. What nothing reads opens as bytes.
        string? failure = null;
        foreach (Func<ContentNode, ReadOnlyMemory<byte>, bool> show in (Func<ContentNode, ReadOnlyMemory<byte>, bool>[])[ShowScript, ShowPicture, ShowSound, ShowModel, ShowAnimation, ShowPlainText])
        {
            try
            {
                if (show(node, bytes))
                {
                    return;
                }
            }
            catch (Exception error) when (error is TextureFormatException or BitmapFormatException or Scripting.Qvm.QvmFormatException or QscDecompileException or PackFormatException or IlffFormatException or Formats.Sound.IlsfFormatException or InvalidDataException or ArgumentException)
            {
                failure ??= error.Message;
            }
        }

        _facts += failure is null ? "  •  shown as bytes" : $"  •  shown as bytes ({failure})";
        ShowText(HexDump(bytes.Span));
    }

    private bool ShowScript(ContentNode node, ReadOnlyMemory<byte> bytes)
    {
        if (node.Extension != ".qvm" || !Scripting.Qvm.QvmProgram.IsQvm(bytes.Span))
        {
            return false;
        }

        // A config is shown by what its values are, a line each, rather than as one call too long to read.
        if (Core.Configs.ConfigDocument.TryRead(bytes) is { } config)
        {
            ShowText(ConfigSummary(config));
            _facts += $"  •  {config.Title.ToLowerInvariant()} config";
            return true;
        }

        ShowText(QscPrinter.Print(QscDecompiler.Decompile(bytes)));
        _facts += "  •  script";
        return true;
    }

    // A config's values named and grouped as its editor shows them, aligned in two columns.
    private static string ConfigSummary(Core.Configs.ConfigDocument config)
    {
        System.Text.StringBuilder text = new();
        for (int c = 0; c < config.Calls.Count; c++)
        {
            Core.Configs.ConfigCall call = config.Calls[c];
            string? section = null;
            for (int i = 0; i < call.Values.Count; i++)
            {
                Core.Configs.ConfigField field = Core.Configs.ConfigSchemas.FieldAt(call.Schema, i);
                if (field.Section != section)
                {
                    section = field.Section;
                    text.Append(text.Length > 0 ? "\n" : string.Empty).Append("// ").Append(section).Append('\n');
                }

                text.Append("  ").Append(field.Name.PadRight(26)).Append(config.Text(c, i)).Append('\n');
            }
        }

        return text.ToString();
    }

    private bool ShowPicture(ContentNode node, ReadOnlyMemory<byte> bytes)
    {
        if (!ContentThumbnails.IsPicture(node) || node.Extension == ".mef" || !ContentPictures.TryDecode(node.Extension, bytes, out TextureImage? picture, out string kind) || picture is null)
        {
            return false;
        }

        if (kind.Length > 0)
        {
            _facts += "  •  " + kind;
        }

        Bgra32[] pixels = [.. picture.GetLevel(0)];
        _image = _images.CreateImage(picture.Width, picture.Height, pixels);
        _imageSize = (picture.Width, picture.Height, picture.MipLevels);
        _facts += string.Create(System.Globalization.CultureInfo.InvariantCulture, $"  •  {picture.Width} × {picture.Height}, {picture.MipLevels} mip level(s)");
        return true;
    }

    private bool ShowSound(ContentNode node, ReadOnlyMemory<byte> bytes)
    {
        if (!_sound.Show(bytes))
        {
            return false;
        }

        _sound.Visibility = Visibility.Visible;
        _facts += "  •  " + _sound.Facts;
        return true;
    }

    private bool ShowModel(ContentNode node, ReadOnlyMemory<byte> bytes)
    {
        if (node.Extension != ".mef")
        {
            return false;
        }

        MefModel model = MefModel.Read(bytes, AssetFormatProfile.Permissive);
        _model.Show(model, Path.GetFileNameWithoutExtension(node.Name));
        _model.Visibility = Visibility.Visible;
        _facts += string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"  •  {model.Vertices.Length} vertices, {model.TriangleCount} triangles{Detail(node.Name, model)}  •  {_model.Facts}  •  drag to turn");
        return true;
    }

    // A character's lower levels of detail are its name with the last digit counted on (003_01_2 … _5), the meshes the game
    // swaps in as it gets further away (0x4CFF70): coarse by design, so the preview says what it is showing.
    private static string Detail(string name, MefModel model)
    {
        string stem = Path.GetFileNameWithoutExtension(name);
        return model.Format == MefMeshFormat.Skinned && stem.Length > 2 && stem[^2] == '_' && stem[^1] is >= '2' and <= '9'
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"  •  level of detail {stem[^1]}, for far away (the full one is {stem[..^1]}1)")
            : string.Empty;
    }

    // An animation file played on a body of the level its skeleton fits: a Project I.G.I. set, or an IGI 2 clip.
    private bool ShowAnimation(ContentNode node, ReadOnlyMemory<byte> bytes)
    {
        if (node.Extension != ".iff" || _host.PreviewAnimation(bytes) is not { } preview)
        {
            return false;
        }

        _model.Show(preview.Body, preview.BodyName, preview.Animator, preview.OpenClip);
        _model.Visibility = Visibility.Visible;
        _facts += $"  \u2022  {preview.Facts}  \u2022  pick a clip below";
        return true;
    }

    private bool ShowPlainText(ContentNode node, ReadOnlyMemory<byte> bytes)
    {
        if (node.Extension is not (".txt" or ".qsc" or ".ini" or ".cfg") && !LooksLikeText(bytes.Span))
        {
            return false;
        }

        ShowText(System.Text.Encoding.Latin1.GetString(bytes.Span));
        return true;
    }

    // The first bytes of a file as offset, sixteen hex bytes and their printable characters a line.
    private static string HexDump(ReadOnlySpan<byte> bytes)
    {
        const int Shown = 16 * 1024;
        System.Text.StringBuilder text = new();
        int length = Math.Min(bytes.Length, Shown);
        for (int line = 0; line < length; line += 16)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{line:X8}  ");
            for (int i = 0; i < 16; i++)
            {
                text.Append(line + i < length ? bytes[line + i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture) + " " : "   ");
            }

            text.Append(' ');
            for (int i = 0; i < 16 && line + i < length; i++)
            {
                byte b = bytes[line + i];
                text.Append(b is >= 32 and < 127 ? (char)b : '.');
            }

            text.Append('\n');
        }

        if (bytes.Length > Shown)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"… {bytes.Length - Shown} more byte(s)\n");
        }

        return text.ToString();
    }

    private void ShowText(string text)
    {
        _text.Text = text;
        _text.Visibility = Visibility.Visible;
    }

    private static bool LooksLikeText(ReadOnlySpan<byte> bytes)
    {
        int sample = Math.Min(bytes.Length, 512);
        for (int i = 0; i < sample; i++)
        {
            byte b = bytes[i];
            if (b < 9 || (b > 13 && b < 32))
            {
                return false;
            }
        }

        return sample > 0;
    }

    private static string Size(int bytes) => bytes switch
    {
        < 1024 => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{bytes} B"),
        < 1024 * 1024 => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB"),
        _ => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.#} MB"),
    };

    private void DrawImage(UiPainter painter, UiImage image, UiRect area)
    {
        (int width, int height, _) = _imageSize;
        float scale = MathF.Min(1f * MathF.Max(1f, Theme.Scale), MathF.Min(area.Width / width, area.Height / height));
        float w = MathF.Round(width * scale);
        float h = MathF.Round(height * scale);
        UiRect shown = new(MathF.Round(area.Center.X - (w * 0.5f)), MathF.Round(area.Y), w, h);

        // A checkerboard behind, so transparency reads as transparency.
        float cell = Theme.Px(8f);
        painter.PushClip(shown);
        for (float y = shown.Y; y < shown.Bottom; y += cell)
        {
            for (float x = shown.X; x < shown.Right; x += cell)
            {
                bool dark = ((int)((x - shown.X) / cell) + (int)((y - shown.Y) / cell)) % 2 == 0;
                painter.FillRect(new UiRect(x, y, cell, cell), dark ? Bgra32.FromRgb(60, 62, 66) : Bgra32.FromRgb(90, 92, 96));
            }
        }

        painter.PopClip();
        painter.DrawSprite(image, shown, new UiRect(0f, 0f, 1f, 1f), Bgra32.White);
        painter.StrokeRect(shown, Theme.Hairline, Theme.Border);
    }
}
