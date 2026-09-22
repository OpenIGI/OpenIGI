using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;
using OpenIGI.TestSupport.Rhi;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>
/// That a compute module's layout is the one the module declares.
/// </summary>
/// <remarks>
/// The failure worth guarding against is not a layout that names a binding the module does not have —
/// that is refused — but one that merely orders them differently, which binds the wrong texture to the
/// wrong slot and shades something plausible. Reading the layout from the same place the shader was
/// compiled from is what removes the chance.
/// </remarks>
public sealed class ComputeProgramTests(ITestOutputHelper output)
{
    [Fact]
    public void The_layout_is_every_binding_the_module_declares_bar_the_push_block()
    {
        using RecordingRhiDevice device = new();
        using ComputeProgram program = new(device, ShaderModuleId.UpscaleRejectionDilate);

        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.UpscaleRejectionDilate);

        int declared = reflection.Bindings.Count(binding =>
            reflection.PushBlock is not { } push
            || binding.Group != push.Group
            || binding.Binding != push.Binding);

        int laidOut = device.Resources
            .OfType<RecordedBindGroupLayout>()
            .Sum(layout => layout.Entries.Count);

        output.WriteLine($"{declared} declared, {laidOut} laid out");

        Assert.Equal(declared, laidOut);
        Assert.True(declared > 0);
    }

    [Fact]
    public void Each_entry_sits_at_the_index_the_module_gave_it()
    {
        using RecordingRhiDevice device = new();
        using ComputeProgram program = new(device, ShaderModuleId.UpscaleRejectionDilate);

        foreach (ShaderBinding binding in program.Reflection.Bindings)
        {
            if (program.Reflection.PushBlock is { } push
                && binding.Group == push.Group
                && binding.Binding == push.Binding)
            {
                continue;
            }

            (int group, int slot) = program.Slot(binding.Name);

            Assert.Equal(binding.Group, group);
            Assert.Equal(binding.Binding, slot);
        }
    }

    [Fact]
    public void A_storage_binding_carries_the_format_the_module_authored()
    {
        // A storage format is what a bind group layout is validated against; the wrong one binds nothing.
        using RecordingRhiDevice device = new();
        using ComputeProgram program = new(device, ShaderModuleId.UpscaleRejectionDilate);

        List<RhiBindGroupLayoutEntry> storage =
        [
            .. device.Resources
                .OfType<RecordedBindGroupLayout>()
                .SelectMany(layout => layout.Entries)
                .Where(entry => entry.Type == RhiBindingType.StorageTexture),
        ];

        Assert.NotEmpty(storage);
        Assert.All(storage, entry => Assert.NotEqual(RhiTextureFormat.Undefined, entry.StorageFormat));
    }

    [Fact]
    public void A_binding_the_module_does_not_have_is_refused_by_name()
    {
        using RecordingRhiDevice device = new();
        using ComputeProgram program = new(device, ShaderModuleId.UpscaleRejectionDilate);

        ArgumentException refused = Assert.Throws<ArgumentException>(() => program.Slot("uNotAThing"));

        // The message lists what it does bind, because the usual cause is a renamed uniform.
        Assert.Contains("uNotAThing", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dispatch_covers_a_region_that_does_not_divide_evenly()
    {
        using RecordingRhiDevice device = new();
        using ComputeProgram program = new(device, ShaderModuleId.UpscaleRejectionDilate);

        (int x, int y, int _) = program.WorkgroupSize;

        Assert.True(x > 1 || y > 1, "This module dispatches a tile, so its workgroup is not one invocation.");

        (int groupsX, int groupsY, int groupsZ) = program.GroupsFor(x + 1, y + 1);

        Assert.Equal(2, groupsX);
        Assert.Equal(2, groupsY);
        Assert.Equal(1, groupsZ);
    }

    [Fact]
    public void A_region_of_nothing_still_dispatches_at_least_one_workgroup()
    {
        using RecordingRhiDevice device = new();
        using ComputeProgram program = new(device, ShaderModuleId.UpscaleRejectionDilate);

        Assert.Equal((1, 1, 1), program.GroupsFor(0, 0, 0));
    }

    [Fact]
    public void Everything_it_made_goes_when_it_does()
    {
        RecordingRhiDevice device = new();
        ComputeProgram program = new(device, ShaderModuleId.UpscaleRejectionDilate);

        List<RecordedResource> made =
        [
            .. device.Resources.Where(resource =>
                resource is RecordedBindGroupLayout or RecordedPipelineLayout
                    or RecordedComputePipeline or RecordedShaderModule),
        ];

        Assert.NotEmpty(made);

        program.Dispose();

        Assert.All(made, resource => Assert.True(resource.IsDisposed));

        device.Dispose();
    }
}
