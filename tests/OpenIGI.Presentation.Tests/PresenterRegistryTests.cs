using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Presentation.Tests;

public sealed class PresenterRegistryTests
{
    [Fact]
    public void A_presenter_is_found_by_what_it_is_made_from_and_what_it_is_used_through()
    {
        Marker small = new("small");
        PresenterRegistry registry = new PresenterRegistry()
            .Add<int, Marker>(static (in int size) => new Marker($"made from {size}"))
            .Add<string, Marker>((in string _) => small);

        Assert.Equal("made from 3", registry.Create<int, Marker>(3)?.Name);
        Assert.Same(small, registry.Create<string, Marker>("anything"));
        Assert.True(registry.Has<int, Marker>());
        Assert.False(registry.Has<long, Marker>());
        Assert.Null(registry.Create<long, Marker>(3L));
    }

    [Fact]
    public void Adding_a_pair_twice_is_refused_and_replacing_it_is_not()
    {
        PresenterRegistry registry = new PresenterRegistry().Add<int, Marker>(static (in int _) => new Marker("first"));

        Assert.Throws<InvalidOperationException>(() => registry.Add<int, Marker>(static (in int _) => new Marker("second")));
        registry.Replace<int, Marker>(static (in int _) => new Marker("mod"));
        Assert.Equal("mod", registry.Create<int, Marker>(0)?.Name);
    }

    [Fact]
    public void A_task_is_drawn_by_its_own_type_s_presenter_or_else_its_nearest_base_s()
    {
        PresenterRegistry registry = new PresenterRegistry()
            .ForTask<BaseTask>(static (task, _) => new Drawn("base"))
            .ForTask<OwnTask>(static (task, _) => new Drawn("own"));
        TaskPresenterRequest request = new(new RecordingGraphicsDevice(), new ResourceRegistry(new VirtualFileSystem()), new OwnTask(), NullLogger.Instance);

        Assert.Equal(2, registry.TaskTypeCount);
        Assert.Equal("own", Assert.IsType<Drawn>(registry.CreateForTask(new OwnTask(), request)).Name);
        Assert.Equal("base", Assert.IsType<Drawn>(registry.CreateForTask(new InheritingTask(), request)).Name);
        Assert.Null(registry.CreateForTask(new UnrelatedTask(), request));
    }

    [Fact]
    public void Each_level_presenter_is_made_once_in_the_order_registered_and_one_with_nothing_to_draw_is_left_out()
    {
        PresenterRegistry registry = new PresenterRegistry()
            .ForLevel(static _ => new Drawn("first"))
            .ForLevel(static _ => null)
            .ForLevel(static _ => new Drawn("last"));
        TaskPresenterRequest request = new(new RecordingGraphicsDevice(), new ResourceRegistry(new VirtualFileSystem()), new OwnTask(), NullLogger.Instance);

        Assert.Equal(3, registry.LevelPresenterCount);
        Assert.Equal(["first", "last"], registry.CreateForLevel(request).Select(static p => ((Drawn)p).Name));
    }

    private sealed record Marker(string Name);

    private sealed class Drawn(string name) : ITaskPresenter
    {
        public string Name { get; } = name;

        public void Draw(in Camera3D camera, in Matrix4f projection, float blend)
        {
        }

        public void Dispose()
        {
        }
    }

    private class BaseTask : GameTask;

    private sealed class InheritingTask : BaseTask;

    private sealed class OwnTask : BaseTask;

    private sealed class UnrelatedTask : GameTask;
}
