using System.Security.Cryptography;
using System.Text;
using OpenIGI.Core.Configuration;

namespace OpenIGI.Rendering.Vulkan.Shaders;

/// <summary>
/// Remembers what a GLSL source compiled to, so the same text is never put through shaderc twice.
/// </summary>
/// <remarks>
/// <para>
/// Compiling the backend's shaders is the single largest thing the render thread does that produces no
/// picture. Measured over a menu session: 816 ms of <c>shaderc</c> before the first frame, and another
/// 205 ms every time a level's surface cache builds its compute programs — which the menu now does once
/// per row of the mission list. None of it is work: the source is a constant in the assembly, the
/// compiler is deterministic, and the answer is the same on every run of every session.
/// </para>
/// <para>
/// So it is written down. The key is a hash of the source together with everything that could make the
/// same source compile differently — the stage, the options this compiler sets, and the shaderc build
/// itself — and the value is the SPIR-V, in a file named by the key. A key that misses compiles and
/// writes; a key that hits reads a few kilobytes.
/// </para>
/// <para>
/// Nothing here can fail in a way that matters. Every read is checked (a truncated or corrupt file is a
/// miss, not a crash), every write is a rename over a temporary so a half-written file is never named,
/// and every I/O error falls through to compiling — which is exactly what the backend did before this
/// existed. The cache is a speed-up with no semantics of its own.
/// </para>
/// </remarks>
internal sealed class ShaderBinaryCache
{
    // Bumped whenever ShaderCompiler changes what it asks for. The options are part of the answer, so a
    // cache written under the old ones has to be missed rather than trusted -- and the files are named
    // by a hash that includes this, so an old entry is simply never asked for again.
    private const int OptionsVersion = 1;

    // SPIR-V's own first word. A file that does not start with it is not SPIR-V, whatever its name says.
    private const uint SpirvMagic = 0x07230203;

    // A build's shaders are a few dozen files, and editing one leaves its predecessor behind for good --
    // harmless on a player's machine, where nothing changes, and unbounded on a developer's. Trimmed back
    // to Keep whenever the directory passes Sweep, oldest first.
    private const int SweepAt = 512;
    private const int Keep = 256;

    private readonly string? _directory;

    /// <summary>Initialises a new instance of the <see cref="ShaderBinaryCache"/> class.</summary>
    /// <param name="directory">
    /// Where to keep the compiled blobs, or <see langword="null"/> for the per-user cache directory.
    /// </param>
    public ShaderBinaryCache(string? directory = null)
    {
        try
        {
            _directory = directory ?? Path.Combine(SettingsPaths.GetUserDirectory(), "cache", "spirv");
            Directory.CreateDirectory(_directory);

            // On a worker, because it is housekeeping and the render thread is about to compile: a sweep
            // that stalled start-up would cost more than the entries it reclaims.
            string sweeping = _directory;
            _ = Task.Run(() => Trim(sweeping));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A read-only or missing cache directory disables the cache and nothing else.
            _directory = null;
        }
    }

    /// <summary>Works out the file name a source's SPIR-V is kept under.</summary>
    /// <param name="source">The GLSL source.</param>
    /// <param name="stage">The stage it is compiled for.</param>
    /// <returns>The key, which is a hex hash and safe as a file name.</returns>
    /// <remarks>
    /// The stage and the options version go into the hash rather than beside it, because the same text
    /// compiled as a different stage is a different program and must not collide with it.
    /// </remarks>
    public static string KeyFor(string source, ShaderStage stage)
    {
        byte[] preamble = Encoding.UTF8.GetBytes($"v{OptionsVersion}:{(int)stage}:");
        byte[] body = Encoding.UTF8.GetBytes(source);

        byte[] material = new byte[preamble.Length + body.Length];
        preamble.CopyTo(material, 0);
        body.CopyTo(material, preamble.Length);

        return Convert.ToHexStringLower(SHA256.HashData(material));
    }

    /// <summary>Reads a compiled blob back, when one was written under this key.</summary>
    /// <param name="key">The key from <see cref="KeyFor"/>.</param>
    /// <returns>The SPIR-V, or <see langword="null"/> when there is none to be had.</returns>
    public byte[]? TryRead(string key)
    {
        if (_directory is null)
        {
            return null;
        }

        try
        {
            byte[] spirv = File.ReadAllBytes(Path.Combine(_directory, key + ".spv"));
            return IsSpirv(spirv) ? spirv : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Writes a compiled blob down under its key.</summary>
    /// <param name="key">The key from <see cref="KeyFor"/>.</param>
    /// <param name="spirv">What the source compiled to.</param>
    /// <remarks>
    /// Through a temporary and a rename, so the name is only ever attached to a whole file. Two processes
    /// compiling the same shader at once therefore race to produce identical bytes, which is harmless;
    /// on Windows the loser's rename fails and is swallowed, leaving the winner's file in place.
    /// </remarks>
    public void Write(string key, byte[] spirv)
    {
        if (_directory is null || !IsSpirv(spirv))
        {
            return;
        }

        string path = Path.Combine(_directory, key + ".spv");
        string temporary = path + "." + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".tmp";

        try
        {
            File.WriteAllBytes(temporary, spirv);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // Nothing left to do about it; the cache is best-effort by design.
            }
        }
    }

    /// <summary>Drops the oldest entries when the directory has grown past its cap.</summary>
    /// <param name="directory">The cache directory.</param>
    /// <remarks>
    /// By last-write time, which for a cache entry is when it was compiled: the ones that go are the ones
    /// belonging to shader sources that have since been edited. Losing a live entry costs one recompile.
    /// </remarks>
    private static void Trim(string directory)
    {
        try
        {
            FileInfo[] entries = new DirectoryInfo(directory).GetFiles("*.spv");
            if (entries.Length <= SweepAt)
            {
                return;
            }

            Array.Sort(entries, (left, right) => left.LastWriteTimeUtc.CompareTo(right.LastWriteTimeUtc));
            for (int i = 0; i < entries.Length - Keep; i++)
            {
                entries[i].Delete();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Housekeeping: a directory that will not be swept is not a problem worth reporting.
        }
    }

    private static bool IsSpirv(byte[] blob) =>
        blob.Length >= 4
        && blob.Length % 4 == 0
        && BitConverter.ToUInt32(blob, 0) == SpirvMagic;
}
