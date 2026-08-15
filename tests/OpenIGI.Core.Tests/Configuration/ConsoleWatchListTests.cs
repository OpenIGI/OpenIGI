using OpenIGI.Core.Configuration;
using Xunit;

namespace OpenIGI.Core.Tests.Configuration;

/// <summary>
/// Covers the pinned-value list the watch overlay reads every frame.
/// </summary>
public sealed class ConsoleWatchListTests
{
    [Fact]
    public void Pinning_the_same_name_twice_pins_it_once()
    {
        var watch = new ConsoleWatchList();

        Assert.True(watch.Add("r_vsync"));
        Assert.False(watch.Add("R_VSYNC"));
        Assert.Single(watch.Names);
    }

    [Fact]
    public void Reading_resolves_a_variable_to_its_current_value()
    {
        var registry = new CVarRegistry();
        var vsync = CVar.Bool("r_vsync", true, registry: registry);
        var watch = new ConsoleWatchList();
        watch.Add("r_vsync");

        List<ConsoleWatchEntry> entries = [];
        watch.Read(registry, entries);
        Assert.Equal("1", Assert.Single(entries).Value);

        vsync.Value = false;
        watch.Read(registry, entries);
        Assert.Equal("0", Assert.Single(entries).Value);
    }

    /// <summary>
    /// Resolution happens per read rather than once at pin time, so a name pinned before its
    /// subsystem registered starts working the moment the variable appears.
    /// </summary>
    [Fact]
    public void A_name_pinned_before_it_exists_starts_resolving_once_it_does()
    {
        var registry = new CVarRegistry();
        var watch = new ConsoleWatchList();
        watch.Add("r_later");

        List<ConsoleWatchEntry> entries = [];
        watch.Read(registry, entries);
        Assert.False(Assert.Single(entries).IsResolved);

        CVar.Int("r_later", 7, registry: registry);

        watch.Read(registry, entries);
        Assert.True(Assert.Single(entries).IsResolved);
        Assert.Equal("7", entries[0].Value);
    }

    [Fact]
    public void A_provider_can_watch_something_that_is_not_a_variable()
    {
        var registry = new CVarRegistry();
        var watch = new ConsoleWatchList();
        int frame = 0;
        watch.AddProvider("fps", () => frame.ToString(System.Globalization.CultureInfo.InvariantCulture));
        watch.Add("fps");

        List<ConsoleWatchEntry> entries = [];
        frame = 60;
        watch.Read(registry, entries);

        Assert.Equal("60", Assert.Single(entries).Value);
    }

    [Fact]
    public void Unpinning_removes_it()
    {
        var watch = new ConsoleWatchList();
        watch.Add("r_vsync");

        Assert.True(watch.Remove("R_VSYNC"));
        Assert.Empty(watch.Names);
        Assert.False(watch.Remove("r_vsync"));
    }

    [Fact]
    public void The_pinned_set_round_trips_through_its_rendered_form()
    {
        var watch = new ConsoleWatchList();
        watch.Add("r_vsync");
        watch.Add("fps");

        var restored = new ConsoleWatchList();
        restored.Parse(watch.Render());

        Assert.Equal(watch.Names, restored.Names);
    }

    [Fact]
    public void Reading_reuses_the_callers_list_rather_than_allocating()
    {
        var registry = new CVarRegistry();
        CVar.Int("r_a", 1, registry: registry);
        var watch = new ConsoleWatchList();
        watch.Add("r_a");

        List<ConsoleWatchEntry> entries = [new("stale", "stale", false)];
        watch.Read(registry, entries);

        Assert.Equal("r_a", Assert.Single(entries).Name);
    }
}
