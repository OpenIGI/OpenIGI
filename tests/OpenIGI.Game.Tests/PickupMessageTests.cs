using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Menus;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>The messages the original posts itself as the player takes a weapon or ammunition (<c>0x46D3A7</c>, <c>0x46D990</c>).</summary>
public sealed class PickupMessageTests
{
    [RequiresGameDataFact]
    public void TheAmmunitionMessageIsInTheLanguagesMessages()
    {
        VirtualFileSystem fileSystem = new(NullLogger.Instance);
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem, NullLogger.Instance);
        resources.Register(MenuStringTable.GetArchiveName(), File.ReadAllBytes(GameData.Resolve("language/english/menusystem.res")));
        resources.Register("LOCAL:language/english/messages.res", File.ReadAllBytes(GameData.Resolve("language/english/messages.res")));
        MissionStringResolver text = new(resources, new MenuStringTable(resources));

        string message = text.Get("PICKEDUPAMMO");

        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.DoesNotContain("RM:", message, StringComparison.Ordinal);
    }
}
