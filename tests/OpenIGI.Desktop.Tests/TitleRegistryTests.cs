using OpenIGI.Composition;
using OpenIGI.Core.Titles;
using OpenIGI.Host.Titles;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>The titles a build plays are packages in one registry, which a mod adds to or replaces in.</summary>
public sealed class TitleRegistryTests
{
    /// <summary>Both games are registered, in the order they are offered, each with all it brings.</summary>
    [Fact]
    public void The_build_registers_both_titles_in_order()
    {
        Assert.Equal([GameTitle.Igi1, GameTitle.Igi2], TitleCatalog.Titles.Packages.Select(static p => p.Title));
        Assert.Null(TitleCatalog.Titles.Get(GameTitle.Igi1).Gameplay);
        Assert.NotNull(TitleCatalog.Titles.Get(GameTitle.Igi2).Gameplay);
        Assert.True(TitleCatalog.Titles.TryGet(" IGI2 ", out TitlePackage? sequel));
        Assert.Same(TitleCatalog.For(GameTitle.Igi2), sequel.Runtime);
    }

    /// <summary>A title is registered once; replacing one keeps its place; an unknown one is refused.</summary>
    [Fact]
    public void A_title_is_registered_once_and_replaced_in_place()
    {
        TitlePackage first = TitleCatalog.Titles.Get(GameTitle.Igi1);
        TitlePackage second = TitleCatalog.Titles.Get(GameTitle.Igi2);
        TitleRegistry registry = new TitleRegistry().Register(first).Register(second);

        Assert.Throws<ArgumentException>(() => registry.Register(first));
        TitlePackage modded = second with { Gameplay = null };
        registry.Replace(modded);

        Assert.Same(modded, registry.Packages[1]);
        Assert.Throws<KeyNotFoundException>(() => new TitleRegistry().Replace(first));
        Assert.False(registry.TryGet("igi3", out _));
    }

    /// <summary>A package bound to an installation carries everything the host resolves a title to.</summary>
    [Fact]
    public void A_bound_package_is_the_title_at_its_installation()
    {
        TitlePackage package = TitleCatalog.Titles.Get(GameTitle.Igi2);
        TitleBinding binding = package.Bind("D:/games/igi2");

        Assert.Same(package.Runtime, binding.Runtime);
        Assert.Same(package.Presentation, binding.Presentation);
        Assert.Same(package.WorldBuilder, binding.WorldBuilder);
        Assert.Same(package.Gameplay, binding.Gameplay);
        Assert.Equal("D:/games/igi2", binding.DataRoot);
    }
}
