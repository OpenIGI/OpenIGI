using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using OpenIGI.Rendering.WebGpu.Native;
using Xunit;

namespace OpenIGI.Rendering.WebGpu.Native.Tests;

/// <summary>
/// What the committed bindings must be true of, checked against the header they came from rather than
/// against the generator that wrote them.
/// </summary>
/// <remarks>
/// A generator that silently drops a declaration produces bindings that compile, pass a build, and are
/// missing a function nobody notices until the day it is called. Counting what the header declares and
/// what the assembly exposes is the check that catches that, and it is independent of the generator
/// because it reads the header itself.
/// </remarks>
public sealed partial class BindingSurfaceTests
{
    [Fact]
    public void The_bindings_name_the_release_they_were_generated_from()
    {
        string version = File.ReadAllText(Path.Combine(NativeDirectory, "VERSION")).Trim();

        Assert.Equal(WebGpu.Version, version);
    }

    [Fact]
    public void Every_exported_function_has_a_binding()
    {
        // The header's own count, taken from the text rather than from the model the generator built, so
        // a declaration the parser failed to recognise is a failure here rather than a silent omission.
        HashSet<string> declared = [];
        foreach (string header in new[] { "webgpu.h", "wgpu.h" })
        {
            foreach (Match match in ExportedPattern().Matches(File.ReadAllText(Path.Combine(NativeDirectory, header))))
            {
                declared.Add(match.Groups["name"].Value);
            }
        }

        HashSet<string> bound = [];
        foreach (MethodInfo method in typeof(WebGpu).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.GetCustomAttribute<LibraryImportAttribute>() is not null)
            {
                bound.Add(method.Name);
            }
        }

        Assert.NotEmpty(declared);
        Assert.Empty(declared.Except(bound, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_binding_names_the_one_library()
    {
        foreach (MethodInfo method in typeof(WebGpu).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            LibraryImportAttribute? import = method.GetCustomAttribute<LibraryImportAttribute>();
            if (import is null)
            {
                continue;
            }

            Assert.Equal(WebGpu.LibraryName, import.LibraryName);
            Assert.Equal(method.Name, import.EntryPoint);
        }
    }

    [Fact]
    public void Nothing_in_the_binding_holds_a_managed_reference()
    {
        // The whole point of the layer is that a descriptor is filled in place and its address passed. A
        // struct carrying a reference cannot be pinned that way, and the failure is a runtime exception
        // at the first call rather than anything the compiler says.
        MethodInfo probe = typeof(RuntimeHelpers)
            .GetMethod(nameof(RuntimeHelpers.IsReferenceOrContainsReferences))!;

        List<string> managed = [];
        foreach (Type type in typeof(WebGpu).Assembly.GetTypes())
        {
            if (!type.IsValueType || type.IsEnum || type.IsGenericType || type.ContainsGenericParameters)
            {
                continue;
            }

            if ((bool)probe.MakeGenericMethod(type).Invoke(null, null)!)
            {
                managed.Add(type.Name);
            }
        }

        Assert.Empty(managed);
    }

    [Fact]
    public void A_handle_is_one_machine_word()
    {
        Assert.Equal(nint.Size, Marshal.SizeOf<WGPUDevice>());
        Assert.Equal(nint.Size, Marshal.SizeOf<WGPUBuffer>());
        Assert.False(WGPUDevice.Null.IsValid);
        Assert.True(new WGPUDevice(1).IsValid);
    }

    /// <summary>The directory holding the pinned headers.</summary>
    private static string NativeDirectory
    {
        get
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenIGI.slnx")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            return Path.Combine(directory.FullName, "src", "Rendering", "OpenIGI.Rendering.WebGpu.Native", "native");
        }
    }

    [GeneratedRegex(@"(WGPU_EXPORT [\w \*]+ |^ {4}[\w \*]+ )(?<name>wgpu\w+)\(", RegexOptions.Multiline)]
    private static partial Regex ExportedPattern();
}
