using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering.D3D12.Rhi;
using OpenIGI.Rendering.Pipeline;
using OpenIGI.Rendering.Vulkan.Rhi;
using OpenIGI.Rendering.WebGpu;
using Xunit;

namespace OpenIGI.Rendering.Rhi.Tests;

/// <summary>A backend opened offscreen for a test: its device, and the texture its frames land in.</summary>
public sealed class RhiBackendHost : IDisposable
{
    private readonly IDisposable _owner;

    /// <summary>Initialises a new instance of the <see cref="RhiBackendHost"/> class.</summary>
    /// <param name="name">The backend's name.</param>
    /// <param name="device">The device.</param>
    /// <param name="target">Where a frame drawn offscreen lands.</param>
    /// <param name="owner">What owns the device, disposed with the host.</param>
    public RhiBackendHost(string name, IRhiDevice device, Func<IRhiTexture> target, IDisposable owner)
    {
        Name = name;
        Device = device;
        _target = target;
        _owner = owner;
    }

    private readonly Func<IRhiTexture> _target;

    /// <summary>Gets the backend's name, as <see cref="RhiBackends"/> lists it.</summary>
    public string Name { get; }

    /// <summary>Gets the device.</summary>
    public IRhiDevice Device { get; }

    /// <summary>Gets the texture the frame drawn last landed in.</summary>
    public IRhiTexture Target => _target();

    /// <inheritdoc/>
    public void Dispose() => _owner.Dispose();
}

/// <summary>
/// Every backend an agreement test can draw on, opened the one way each is opened offscreen.
/// </summary>
/// <remarks>
/// <para>
/// An agreement test draws the same thing through two backends and compares the pictures. Vulkan is the
/// reference -- it is the backend every other one has been brought into line with -- and each of the others
/// is compared with it, so a backend added here is compared everywhere at once, by a row rather than by a
/// copy of every test.
/// </para>
/// <para>
/// A machine without the reference, or without the backend being compared (a CI runner has no GPU, and
/// wgpu-native has no Linux build there), skips that comparison with the reason, the way the single-backend
/// suites skip, instead of failing on something that says nothing about the code.
/// </para>
/// </remarks>
public static class RhiBackends
{
    /// <summary>The backend every other one is compared with.</summary>
    public const string Reference = "Vulkan";

    private static readonly (string Name, Func<int, int, RhiTextureFormat, RhiBackendHost> Open)[] All =
    [
        ("Vulkan", OpenVulkan),
        ("WebGPU", OpenWebGpu),
        ("Direct3D 12", OpenD3D12),
    ];

    private static readonly Dictionary<string, Lazy<string?>> Probes = All.ToDictionary(
        backend => backend.Name,
        backend => new Lazy<string?>(() => Probe(backend.Open, backend.Name), isThreadSafe: true),
        StringComparer.Ordinal);

    /// <summary>How many code values a backend's picture may stray from the reference's in any channel.</summary>
    /// <param name="name">The backend.</param>
    /// <returns>One where the backend's shaders reach the GPU through the same compiler as the reference's; two where they do not.</returns>
    /// <remarks>
    /// WebGPU's WGSL becomes SPIR-V and goes through the driver's own SPIR-V compiler, as Vulkan's does, and
    /// the two agree to the rounding of the last store. Direct3D 12's shaders are optimised by DXC first,
    /// which contracts and reorders floating-point arithmetic differently: measured on an RTX 3080, the
    /// deferred frame then differs by one or two levels over about five per cent of its channels, scattered
    /// evenly -- sky included -- with no edge or pattern, which is rounding and not a rule followed differently.
    /// </remarks>
    public static int ColourTolerance(string name) => name == "Direct3D 12" ? 2 : 1;

    /// <summary>Gets the backends compared with the reference on this machine, as theory data.</summary>
    /// <remarks>Only those that open here: a theory row cannot be skipped on its own.</remarks>
    public static TheoryData<string> ComparedWithReference
    {
        get
        {
            TheoryData<string> data = [];
            foreach ((string name, _) in All)
            {
                if (name != Reference && Unavailable(name) is null)
                {
                    data.Add(name);
                }
            }

            return data;
        }
    }

    /// <summary>Gets why no comparison can run here, or <see langword="null"/> when one can.</summary>
    public static string? NothingToCompare
    {
        get
        {
            if (Unavailable(Reference) is { } reference)
            {
                return reference;
            }

            List<string> reasons = [];
            foreach ((string name, _) in All)
            {
                if (name == Reference)
                {
                    continue;
                }

                if (Unavailable(name) is not { } reason)
                {
                    return null;
                }

                reasons.Add(reason);
            }

            return string.Join("; ", reasons);
        }
    }

    /// <summary>Gets why a backend cannot be opened here, or <see langword="null"/> when it can.</summary>
    /// <param name="name">The backend.</param>
    /// <returns>The reason, or null.</returns>
    public static string? Unavailable(string name) =>
        Probes.TryGetValue(name, out Lazy<string?>? probe) ? probe.Value : $"No backend is called {name}.";

    /// <summary>Opens a backend offscreen.</summary>
    /// <param name="name">The backend.</param>
    /// <param name="width">The frame's width.</param>
    /// <param name="height">Its height.</param>
    /// <param name="format">What the frame is drawn in.</param>
    /// <returns>The backend.</returns>
    public static RhiBackendHost Open(string name, int width, int height, RhiTextureFormat format)
    {
        foreach ((string backend, Func<int, int, RhiTextureFormat, RhiBackendHost> open) in All)
        {
            if (backend == name)
            {
                return open(width, height, format);
            }
        }

        throw new ArgumentException($"No backend is called {name}.", nameof(name));
    }

    /// <summary>Opens a backend offscreen at the format a frame is presented in.</summary>
    /// <param name="name">The backend.</param>
    /// <param name="size">The frame's width and height.</param>
    /// <returns>The backend.</returns>
    public static RhiBackendHost Open(string name, int size) =>
        Open(name, size, size, GraphicsDevice.DefaultPresentFormat);

    private static RhiBackendHost OpenVulkan(int width, int height, RhiTextureFormat format)
    {
        VulkanRhi vulkan = VulkanRhi.CreateOffscreen(width, height, format);
        return new RhiBackendHost("Vulkan", vulkan.Device, () => vulkan.OffscreenTarget(0), vulkan);
    }

    private static RhiBackendHost OpenD3D12(int width, int height, RhiTextureFormat format)
    {
        D3D12Rhi d3d12 = D3D12Rhi.CreateOffscreen(width, height, format, DebugLog.FromEnvironment(), D3D12Rhi.ValidationFromEnvironment);
        return new RhiBackendHost("Direct3D 12", d3d12.Device, () => d3d12.OffscreenTarget(0), d3d12);
    }

    private static RhiBackendHost OpenWebGpu(int width, int height, RhiTextureFormat format)
    {
        WebGpuRhiDevice device = WebGpuRhiDevice.Create();
        try
        {
            device.AttachOffscreen(width, height, format);
        }
        catch
        {
            device.Dispose();
            throw;
        }

        return new RhiBackendHost("WebGPU", device, () => device.OffscreenTarget(0), device);
    }

    private static string? Probe(Func<int, int, RhiTextureFormat, RhiBackendHost> open, string name)
    {
        try
        {
            using RhiBackendHost host = open(4, 4, GraphicsDevice.DefaultPresentFormat);
            return null;
        }
        catch (Exception error)
        {
            return $"No {name} device here: {error.Message}";
        }
    }
}

/// <summary>A theory comparing each backend with the reference, run only where there is something to compare.</summary>
/// <remarks>Pair it with <c>[MemberData(nameof(RhiBackends.ComparedWithReference), MemberType = typeof(RhiBackends))]</c>.</remarks>
public sealed class AgreementTheoryAttribute : TheoryAttribute
{
    /// <summary>Initialises a new instance of the <see cref="AgreementTheoryAttribute"/> class.</summary>
    public AgreementTheoryAttribute() => Skip = RhiBackends.NothingToCompare;
}

/// <summary>A fact about the reference backend alone, run only where it opens.</summary>
/// <remarks>For what only the reference can do yet; a comparison wants <see cref="AgreementTheoryAttribute"/>.</remarks>
public sealed class ReferenceFactAttribute : FactAttribute
{
    /// <summary>Initialises a new instance of the <see cref="ReferenceFactAttribute"/> class.</summary>
    public ReferenceFactAttribute() => Skip = RhiBackends.Unavailable(RhiBackends.Reference);
}

/// <summary>Where a backend's own warnings go in a test run: the file <c>OPENIGI_D3D12_LOG</c> names, or nowhere.</summary>
/// <remarks>The debug layer reports through the device's logger, which a test otherwise leaves null.</remarks>
internal sealed class DebugLog(string path) : IOpenIgiLogger
{
    private static readonly Lock Gate = new();

    /// <summary>The logger the environment asks for, or null.</summary>
    /// <returns>The logger.</returns>
    public static IOpenIgiLogger? FromEnvironment() =>
        Environment.GetEnvironmentVariable("OPENIGI_D3D12_LOG") is { Length: > 0 } path ? new DebugLog(path) : null;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel level, string category) => level >= LogLevel.Warning && level < LogLevel.None;

    /// <inheritdoc/>
    public void Log(in LogRecord record)
    {
        if (!IsEnabled(record.Level, record.Category))
        {
            return;
        }

        using StringWriter line = new(System.Globalization.CultureInfo.InvariantCulture);
        LogFormatter.WriteConsoleLine(line, record);
        lock (Gate)
        {
            File.AppendAllText(path, line.ToString());
        }
    }
}
