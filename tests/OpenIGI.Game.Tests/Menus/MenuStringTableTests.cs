using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.Menus;

namespace OpenIGI.Game.Tests.Menus;

public sealed class MenuStringTableTests
{
    [RequiresGameDataFact]
    public void KeysFromTheMenuScriptResolveAgainstTheRetailLanguageArchive()
    {
        MenuStringTable strings = LoadEnglish();

        // These are Text Resource values straight out of mainmenu.qvm's string table. The key is an
        // identifier and the value is the shipped label, which is why "Single Player" reads
        // "Play Project IGI" on screen — the two are not the same string, and a table that echoed its
        // keys back would look correct while being wrong.
        Assert.Equal("Play Project IGI", strings.Get("Single Player"));
        Assert.Equal("Configuration", strings.Get("Configuration"));
        Assert.Equal("Quit", strings.Get("Quit"));
        Assert.Equal("Main Menu", strings.Get("Main Menu"));
        Assert.Equal("Credits", strings.Get("Credits"));
        Assert.Equal("Select Player", strings.Get("Select Player"));
    }

    [RequiresGameDataFact]
    public void AMissingKeyRendersAsTheOriginalsVisiblePlaceholder()
    {
        MenuStringTable strings = LoadEnglish();

        // "RM:" is what a translator saw in place of an untranslated entry, and it is what the
        // original draws — not a blank.
        Assert.Equal("RM: No Such Entry", strings.Get("No Such Entry"));
        Assert.False(strings.Contains("No Such Entry"));
        Assert.Equal(1, strings.MissingCount);

        // The miss is cached, so asking twice does not grow the report.
        Assert.Equal("RM: No Such Entry", strings.Get("No Such Entry"));
        Assert.Equal(1, strings.MissingCount);
    }

    [RequiresGameDataFact]
    public void AnEmptyKeyYieldsAnEmptyStringRatherThanAPlaceholder()
    {
        MenuStringTable strings = LoadEnglish();

        Assert.Equal(string.Empty, strings.Get(string.Empty));
        Assert.Equal(0, strings.MissingCount);
    }

    [RequiresGameDataFact]
    public void TheResourceNameIsTheOneTheArchiveRegisters()
    {
        MenuStringTable strings = LoadEnglish();

        Assert.Equal("LOCAL:english/Single Player", strings.GetResourceName("Single Player"));
    }

    [RequiresGameDataFact]
    public void EveryTextResourceTheMainMenuNamesResolves()
    {
        // The check that matters: if a key the script uses does not resolve, that entry draws as
        // "RM: ..." in game.
        MenuStringTable strings = LoadEnglish();

        string[] keys =
        [
            "Main Menu", "Single Player", "Multi Player", "Configuration", "Quit",
            "Read Me", "Credits", "Select Player",
        ];

        List<string> unresolved = [.. keys.Where(k => !strings.Contains(k))];
        Assert.Empty(unresolved);
    }

    private static MenuStringTable LoadEnglish()
    {
        VirtualFileSystem fileSystem = new(NullLogger.Instance);
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));

        ResourceRegistry resources = new(fileSystem, NullLogger.Instance);
        resources.Register(
            MenuStringTable.GetArchiveName(),
            File.ReadAllBytes(GameData.Resolve("language/english/menusystem.res")));

        return new MenuStringTable(resources);
    }
}
