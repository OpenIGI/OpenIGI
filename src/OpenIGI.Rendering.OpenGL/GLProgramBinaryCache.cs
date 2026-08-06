using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenIGI.Core.Configuration;
using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>Persists linked GPU programs for the current driver, avoiding repeat GLSL compilation.</summary>
/// <remarks>
/// Program binaries are deliberately private to the exact GL driver that created them. The cache key
/// therefore includes its vendor, renderer and version as well as all source text. A failed restore is
/// never fatal: the entry is ignored and the normal GLSL compile/link path recreates it.
/// </remarks>
internal sealed class GLProgramBinaryCache
{
    private const int HeaderSize = 16;
    private const int MaximumBinaryBytes = 32 * 1024 * 1024;
    private static ReadOnlySpan<byte> Magic => "OIGISHP1"u8;

    private readonly GL _gl;
    private readonly string _directory = string.Empty;

    /// <summary>Initializes a cache scoped to the active GL driver.</summary>
    public GLProgramBinaryCache(GL gl, in GLFeatureLevel features)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _gl = gl;

        if (!features.SupportsProgramBinary)
        {
            return;
        }

        string identity = string.Join(
            "\n",
            gl.GetStringS(StringName.Vendor) ?? "unknown",
            gl.GetStringS(StringName.Renderer) ?? "unknown",
            gl.GetStringS(StringName.Version) ?? "unknown");
        _directory = Path.Combine(
            SettingsPaths.GetUserDirectory(),
            "cache",
            "shaders",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))));
        IsEnabled = true;
    }

    /// <summary>Gets whether this context can restore and retain program binaries.</summary>
    public bool IsEnabled { get; }

    /// <summary>Builds the stable name for one program's source.</summary>
    public string? CreateKey(string kind, params string[] sources)
    {
        if (!IsEnabled)
        {
            return null;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(sources);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, kind);
        foreach (string source in sources)
        {
            Append(hash, source);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>Requests a binary when linking a program will create one.</summary>
    public void MakeBinaryRetrievable(uint program)
    {
        if (IsEnabled)
        {
            _gl.ProgramParameter(program, ProgramParameterPName.BinaryRetrievableHint, 1);
        }
    }

    /// <summary>Restores a program into a new GL object when a matching binary exists.</summary>
    public bool TryLoad(string key, out uint program)
    {
        program = 0;
        if (!IsEnabled)
        {
            return false;
        }

        try
        {
            string path = PathFor(key);
            if (!File.Exists(path))
            {
                return false;
            }

            byte[] stored = File.ReadAllBytes(path);
            if (!IsWellFormed(stored, out GLEnum format, out ReadOnlySpan<byte> binary))
            {
                return false;
            }

            uint candidate = _gl.CreateProgram();
            _gl.ProgramBinary(candidate, format, binary);
            _gl.GetProgram(candidate, ProgramPropertyARB.LinkStatus, out int linked);
            if (linked == 0)
            {
                _gl.DeleteProgram(candidate);
                return false;
            }

            program = candidate;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Saves a successfully linked program without allowing cache I/O to affect rendering.</summary>
    public unsafe void Store(string? key, uint program)
    {
        if (!IsEnabled || key is null)
        {
            return;
        }

        try
        {
            _gl.GetProgram(program, ProgramPropertyARB.ProgramBinaryLength, out int length);
            if (length <= 0 || length > MaximumBinaryBytes)
            {
                return;
            }

            byte[] binary = new byte[length];
            uint written = 0;
            GLEnum format = default;
            fixed (byte* destination = binary)
            {
                _gl.GetProgramBinary(program, (uint)binary.Length, &written, &format, destination);
            }
            if (written == 0 || written > binary.Length)
            {
                return;
            }

            byte[] stored = new byte[HeaderSize + written];
            Magic.CopyTo(stored);
            BinaryPrimitives.WriteUInt32LittleEndian(stored.AsSpan(8), (uint)format);
            BinaryPrimitives.WriteUInt32LittleEndian(stored.AsSpan(12), written);
            binary.AsSpan(0, (int)written).CopyTo(stored.AsSpan(HeaderSize));

            Directory.CreateDirectory(_directory);
            string path = PathFor(key);
            string temporary = path + ".tmp";
            File.WriteAllBytes(temporary, stored);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // A driver's own cache is still useful, and a user-cache failure must never stop a launch.
        }
    }

    private static void Append(IncrementalHash hash, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static bool IsWellFormed(byte[] stored, out GLEnum format, out ReadOnlySpan<byte> binary)
    {
        format = default;
        binary = default;
        if (stored.Length < HeaderSize
            || !stored.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            return false;
        }

        uint length = BinaryPrimitives.ReadUInt32LittleEndian(stored.AsSpan(12));
        if (length == 0 || length > MaximumBinaryBytes || length != stored.Length - HeaderSize)
        {
            return false;
        }

        format = (GLEnum)BinaryPrimitives.ReadUInt32LittleEndian(stored.AsSpan(8));
        binary = stored.AsSpan(HeaderSize);
        return true;
    }

    private string PathFor(string key) => Path.Combine(_directory, key + ".bin");
}
