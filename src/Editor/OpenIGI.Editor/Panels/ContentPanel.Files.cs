using System.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Ilff;
using OpenIGI.Audio;
using OpenIGI.Core.Audio;
using OpenIGI.Engine.Audio;
using OpenIGI.Formats.Sound;
using OpenIGI.Formats.Textures;
using OpenIGI.UI.Editor.Widgets;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// Editing textures and sounds outside the editor: a texture is exported as a PNG and a sound as a WAV into the project's
/// <c>exports</c> folder, edited in any image or audio editor, and imported back in the game's own format (<c>.tex</c>, ILSF)
/// into the project, inside its archive when it lives in one. The installation is never written.
/// </summary>
internal sealed partial class ContentPanel
{
    /// <summary>Gets or sets where exported files go, or <see langword="null"/> while there is no project.</summary>
    public string? ExportsDirectory { get; set; }

    /// <summary>Gets or sets what writes a file into the project, given its game path (relative to the installation).</summary>
    public Action<string, byte[]>? WriteToProject { get; set; }

    /// <summary>Raised to say what an export or import did, and whether it failed.</summary>
    public event Action<string, bool>? Reported;

    // The editing items for a node's menu: export for any picture or sound, import once it has been exported.
    private IEnumerable<MenuItem> FileMenu(ContentNode node)
    {
        if (ExportsDirectory is null)
        {
            yield break;
        }

        if (IsSound(node))
        {
            yield return MenuItem.Separator;
            yield return MenuItem.Command("Export as WAV", () => ExportWav(node));
            if (WriteToProject is not null)
            {
                yield return MenuItem.Command("Import Edited WAV", () => ImportWav(node), File.Exists(ExportPath(node, ".wav")));
            }

            yield return MenuItem.Command("Open Exports Folder", OpenExports);
            yield break;
        }

        if (!ContentThumbnails.IsPicture(node) || node.Extension == ".mef")
        {
            yield break;
        }

        yield return MenuItem.Separator;
        yield return MenuItem.Command("Export as PNG", () => ExportPng(node));
        if (node.Extension == ".tex" && WriteToProject is not null)
        {
            yield return MenuItem.Command("Import Edited PNG", () => ImportPng(node), File.Exists(ExportPath(node)));
        }

        yield return MenuItem.Command("Open Exports Folder", OpenExports);
    }

    // Where a node's export goes: the game path below the exports folder, an archive's entries in a folder named after it.
    private string ExportPath(ContentNode node, string extension = ".png")
    {
        string relative = node.Path.StartsWith(LocalRoot, StringComparison.OrdinalIgnoreCase) ? node.Path[LocalRoot.Length..] : node.Path;
        if (node.Entry is not null)
        {
            relative = $"{relative}/{node.Name}";
        }

        string path = Path.Combine(ExportsDirectory!, relative.Replace('/', Path.DirectorySeparatorChar));

        // A sound is already named .wav in the game; its export is told apart by the folder alone.
        return string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase) ? path : Path.ChangeExtension(path, extension);
    }

    private void ExportPng(ContentNode node)
    {
        try
        {
            if (Bytes(node) is not { } bytes || !ImageSniffer.TryDecode(bytes.Span, out TextureImage? picture) || picture is null)
            {
                Reported?.Invoke($"{node.Name} could not be read as a picture.", true);
                return;
            }

            string path = ExportPath(node);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            PngWriter.Write(path, picture.GetLevel(0), picture.Width, picture.Height);
            Reported?.Invoke($"Exported {node.Name} to {path}. Edit it, then Import Edited PNG.", false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or TextureFormatException)
        {
            Reported?.Invoke($"Could not export {node.Name}: {error.Message}", true);
        }
    }

    // The edited PNG back as a .tex in the original's container version and secondary format, with a mip chain when the
    // original had one, written into the project, inside a rebuilt copy of its archive when it lives in one.
    private void ImportPng(ContentNode node)
    {
        try
        {
            if (Bytes(node) is not { } original || WriteToProject is not { } write)
            {
                return;
            }

            (Bgra32[] pixels, int width, int height) = PngReader.Read(ExportPath(node));
            TextureDescriptor header = TexFile.ReadHeader(original.Span, out _);
            uint version = TexFile.ReadVersion(original.Span);
            TextureImage image = TextureImage.FromPixels(width, height, pixels);
            if (ImageSniffer.TryDecode(original.Span, out TextureImage? before) && before is { MipLevels: > 1 })
            {
                image = image.WithMipChain();
            }

            Replace(node, TexWriter.WriteTexture(image, version, header.SecondaryFormat), write);
            Reported?.Invoke($"Imported {node.Name} ({width} × {height}) into the project. Reload the mission to see it in the world.", false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or TextureFormatException or IlffFormatException or InvalidDataException)
        {
            Reported?.Invoke($"Could not import {node.Name}: {error.Message}", true);
        }
    }

    private void ExportWav(ContentNode node)
    {
        try
        {
            if (Bytes(node) is not { } bytes || !IlsfFile.IsIlsf(bytes.Span))
            {
                Reported?.Invoke($"{node.Name} could not be read as a sound.", true);
                return;
            }

            IlsfFile sound = IlsfFile.Read(bytes);
            string path = ExportPath(node, ".wav");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WaveWriter.Write(path, sound.DecodePcm().Span, sound.SampleRate, sound.Channels, sound.BitsPerSample);
            Reported?.Invoke($"Exported {node.Name} to {path}. Edit it, then Import Edited WAV.", false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or IlsfFormatException)
        {
            Reported?.Invoke($"Could not export {node.Name}: {error.Message}", true);
        }
    }

    // The edited WAV back as an ILSF sound, PCM at whatever rate and channels it was saved at, kept streamed when the original
    // was streamed (music and ambience) and held in memory otherwise.
    private void ImportWav(ContentNode node)
    {
        try
        {
            if (Bytes(node) is not { } original || WriteToProject is not { } write)
            {
                return;
            }

            if (!WavPcm.TryRead(File.ReadAllBytes(ExportPath(node, ".wav")), out AudioFormat format, out byte[] pcm))
            {
                Reported?.Invoke($"The edited {node.Name} is not an 8- or 16-bit PCM WAV; save it as one and import again.", true);
                return;
            }

            bool streamed = IlsfFile.IsIlsf(original.Span) && IlsfFile.Read(original).IsStreamed;
            Replace(node, IlsfWriter.WritePcm(pcm, format.SampleRate, format.Channels, format.BitsPerSample, streamed), write);
            double seconds = pcm.Length / (double)(format.SampleRate * format.Channels * format.BitsPerSample / 8);
            Reported?.Invoke(
                FormattableString.Invariant($"Imported {node.Name} ({seconds:0.0} s, {format.SampleRate} Hz, {format.Channels} channel(s)) into the project. Reload the mission to hear it."),
                false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or IlsfFormatException or IlffFormatException or InvalidDataException or ArgumentException)
        {
            Reported?.Invoke($"Could not import {node.Name}: {error.Message}", true);
        }
    }

    // Writes a file's new bytes into the project, inside a rebuilt copy of its archive when it lives in one.
    private void Replace(ContentNode node, byte[] body, Action<string, byte[]> write)
    {
        string gamePath = node.Path.StartsWith(LocalRoot, StringComparison.OrdinalIgnoreCase) ? node.Path[LocalRoot.Length..] : node.Path;
        if (node.Entry is { } entry)
        {
            IresArchive archive = IresArchive.Read(_host.Files.ReadAllBytes(node.Path)).WithBodies(e =>
                e.Name == entry.Name ? body : (ReadOnlyMemory<byte>?)null);
            write(gamePath, IresWriter.Write([.. archive.Entries.Select(static e => new IresWriter.Entry(e.Name, e.Kind, e.Data))]));
        }
        else
        {
            write(gamePath, body);
        }

        _thumbnails.Release();
        _previewed = null;
    }

    private bool IsSound(ContentNode node) =>
        !node.IsFolder && node.Extension == ".wav" && Bytes(node) is { } bytes && IlsfFile.IsIlsf(bytes.Span);

    private void OpenExports()
    {
        if (ExportsDirectory is { } folder)
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
    }

    private ReadOnlyMemory<byte>? Bytes(ContentNode node) =>
        node.Entry is { } entry ? entry.Data : _host.TryReadFile(node.Path, out ReadOnlyMemory<byte> bytes) ? bytes : null;
}
