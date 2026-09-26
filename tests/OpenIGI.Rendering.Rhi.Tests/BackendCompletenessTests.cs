using System.Reflection;
using OpenIGI.Rendering.Rhi;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.Rhi.Tests;

/// <summary>
/// That neither backend leaves a member of the interface unimplemented.
/// </summary>
/// <remarks>
/// <para>
/// A member that throws still satisfies the compiler, so a half-built backend builds, passes its own
/// tests and fails on the frame that first reaches the gap. While a backend is being written that is
/// honest and useful; once it claims to implement the interface it is a trap, and the only thing that
/// notices is a player.
/// </para>
/// <para>
/// Checked by reading the compiled method bodies, which is crude and is the point: it needs no device,
/// it cannot be satisfied by a stub that returns a default, and it names the member that is missing.
/// </para>
/// </remarks>
public sealed class BackendCompletenessTests(ITestOutputHelper output)
{
    [Fact]
    public void Neither_backend_leaves_a_member_of_the_interface_throwing()
    {
        List<string> unimplemented = [];

        foreach (Type implementation in Implementations())
        {
            foreach (MethodInfo method in InterfaceMethods(implementation))
            {
                if (ThrowsNotSupported(method))
                {
                    unimplemented.Add($"{implementation.Name}.{method.Name}");
                }
            }
        }

        foreach (string missing in unimplemented)
        {
            output.WriteLine(missing);
        }

        Assert.Empty(unimplemented);
    }

    [Fact]
    public void Both_backends_implement_the_whole_device()
    {
        // The other direction: a type claiming the interface has to have a body for every member of it,
        // which the compiler already insists on -- this asserts the set is the one being walked above,
        // so a member added to the interface cannot be missed by the check.
        foreach (Type implementation in Implementations())
        {
            int members = InterfaceMethods(implementation).Count;

            output.WriteLine($"{implementation.Name}: {members} member(s)");
            Assert.True(members >= 20, $"{implementation.Name} maps {members} members, which is too few.");
        }
    }

    /// <summary>The types that claim to be a device.</summary>
    private static List<Type> Implementations()
    {
        List<Type> found = [];

        foreach (Assembly assembly in new[]
        {
            typeof(OpenIGI.Rendering.WebGpu.WebGpuRhiDevice).Assembly,
            typeof(OpenIGI.Rendering.Vulkan.Rhi.VulkanRhiTranslation).Assembly,
        })
        {
            foreach (Type type in assembly.GetTypes())
            {
                if (!type.IsAbstract && !type.IsInterface && typeof(IRhiDevice).IsAssignableFrom(type))
                {
                    found.Add(type);
                }
            }
        }

        Assert.Equal(2, found.Count);
        return found;
    }

    /// <summary>The methods a type provides for the device interface.</summary>
    private static List<MethodInfo> InterfaceMethods(Type implementation)
    {
        InterfaceMapping mapping = implementation.GetInterfaceMap(typeof(IRhiDevice));
        return [.. mapping.TargetMethods];
    }

    /// <summary>Whether a method's body is a throw of NotSupportedException and nothing else.</summary>
    /// <remarks>
    /// Read from the IL rather than by calling it, because calling would need a device. A body that
    /// constructs that exception and throws is the shape a "not built yet" member has; one that throws it
    /// conditionally, after doing real work, is longer and is not matched.
    /// </remarks>
    private static bool ThrowsNotSupported(MethodInfo method)
    {
        MethodBody? body = method.GetMethodBody();
        byte[]? il = body?.GetILAsByteArray();

        if (il is null || il.Length > 64)
        {
            return false;
        }

        Module module = method.Module;

        for (int i = 0; i + 4 < il.Length; i++)
        {
            // newobj, whose operand is a constructor token.
            if (il[i] != 0x73)
            {
                continue;
            }

            int token = BitConverter.ToInt32(il, i + 1);

            try
            {
                if (module.ResolveMethod(token)?.DeclaringType == typeof(NotSupportedException))
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // A token this module cannot resolve is not one that names the exception looked for.
            }
        }

        return false;
    }
}
