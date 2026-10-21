using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Ai.Runtime;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>I.G.I. 2 started as a guest inside another game's session: its own loop, its own installation, its tables loaded.</summary>
public sealed class Igi2GuestTitleTests
{
    [RequiresTitleDataFact("igi2")]
    public void A_guest_reads_its_tables_from_its_own_installation()
    {
        GuestTitle guest = GuestTitle.Start(new Igi2Runtime(), GameDataSource.Igi2.RequireRoot());

        Assert.NotEmpty(guest.Session.Services.Require<Igi2WeaponTables>().WeaponTypes);
        Assert.NotNull(guest.Session.Services.Require<Igi2GameMaterials>()[0]);
        Assert.NotEmpty(guest.Session.Services.Require<Igi2HumanAiConfig>().BaseRows);
        Igi2AiWorld ai = guest.Session.Services.Require<Igi2AiWorld>();
        Assert.NotNull(ai.Default);
        Assert.NotNull(ai.SquadDefault);
    }

    [RequiresTitleDataFact("igi2")]
    public void A_guests_types_and_natives_stay_its_own()
    {
        // The host keeps its own registries: a guest declares I.G.I. 2's HumanSoldier into its loop alone.
        GameLoop host = new();
        GuestTitle guest = GuestTitle.Start(new Igi2Runtime(), GameDataSource.Igi2.RequireRoot());

        Assert.True(guest.Loop.Types.TryGet(Igi2HumanSoldierTask.TypeName, out _));
        Assert.False(host.Types.TryGet(Igi2HumanSoldierTask.TypeName, out _));
        Assert.NotSame(host.Natives, guest.Loop.Natives);
    }
}
