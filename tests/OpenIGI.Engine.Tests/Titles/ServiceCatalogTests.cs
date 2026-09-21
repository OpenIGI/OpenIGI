using OpenIGI.Engine.Titles;
using Xunit;

namespace OpenIGI.Engine.Tests.Titles;

/// <summary>The typed bag content modules publish their tables into.</summary>
public sealed class ServiceCatalogTests
{
    /// <summary>What a module provides, a later caller finds.</summary>
    [Fact]
    public void A_published_table_is_found_by_its_type()
    {
        ServiceCatalog catalog = new();
        Table table = new();

        ((IServiceCollector)catalog).Provide(table);

        Assert.True(((IServiceCatalog)catalog).TryGet(out Table? found));
        Assert.Same(table, found);
        Assert.Same(table, ((IServiceCatalog)catalog).Require<Table>());
    }

    /// <summary>An absent table is reported, not invented.</summary>
    [Fact]
    public void An_unpublished_table_is_absent()
    {
        ServiceCatalog catalog = new();

        Assert.False(((IServiceCatalog)catalog).TryGet(out Table? found));
        Assert.Null(found);

        InvalidOperationException failure =
            Assert.Throws<InvalidOperationException>(((IServiceCatalog)catalog).Require<Table>);
        Assert.Contains(nameof(Table), failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two modules cannot both own the same table.
    /// </summary>
    /// <remarks>
    /// Refused rather than last-one-wins. Ownership of a table is a design decision, and a silent
    /// overwrite would make which module won depend on the order the title happens to list them.
    /// </remarks>
    [Fact]
    public void Two_modules_cannot_publish_the_same_type()
    {
        ServiceCatalog catalog = new();
        ((IServiceCollector)catalog).Provide(new Table());

        Assert.Throws<InvalidOperationException>(() => ((IServiceCollector)catalog).Provide(new Table()));
    }

    /// <summary>Publishing nothing is a mistake worth catching at the call site.</summary>
    [Fact]
    public void A_null_table_is_refused()
    {
        ServiceCatalog catalog = new();
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollector)catalog).Provide<Table>(null!));
    }

    private sealed class Table;
}
