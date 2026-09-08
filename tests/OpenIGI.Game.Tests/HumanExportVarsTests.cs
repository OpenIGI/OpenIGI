using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Game.Player;
using OpenIGI.Scripting.Runtime;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// What every character exports, and the fact that it is exported <em>once</em>, by the class they
/// share.
/// </summary>
/// <remarks>
/// The original installs its ExportVars/UnexportVars pair on <c>Human</c> itself — <c>sub_45E450</c>
/// registers <c>sub_460850</c>/<c>sub_4608D0</c> — and neither <c>HumanPlayer</c> (<c>sub_40FE30</c>)
/// nor <c>HumanSoldier</c> (<c>sub_45CAB0</c>) registers an exporter of its own; both pass
/// <c>sub_460BE0()</c>, the <c>Human</c> type word, as their parent. <c>sub_460850</c> binds exactly
/// two names: <c>isDead</c> to the byte at <c>Human+3317</c> and <c>eTeam</c> to the int at
/// <c>Human+252</c>.
/// </remarks>
public sealed class HumanExportVarsTests
{
    private static GameLoop Loop()
    {
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        return loop;
    }

    private static T Declare<T>(GameLoop loop, string type, int id)
        where T : HumanTask
    {
        T task = (T)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(type));
        Assert.True(loop.Tasks.TryBindId(id, task));
        Assert.True(loop.Variables.Export(task));
        return task;
    }

    private static bool ReadBool(GameLoop loop, string symbol)
    {
        Assert.True(loop.Variables.TryRead(symbol, out QvmValue value), $"missing {symbol}");
        return value.ToInt32() != 0;
    }

    private static int ReadInt(GameLoop loop, string symbol)
    {
        Assert.True(loop.Variables.TryRead(symbol, out QvmValue value), $"missing {symbol}");
        return value.ToInt32();
    }

    /// <summary>The player and a soldier export the same two names, from the same one place.</summary>
    [Theory]
    [InlineData("HumanPlayer", 0)]
    [InlineData("HumanSoldier", 4023)]
    public void EveryCharacterExportsItsDeathLatchAndItsTeam(string type, int id)
    {
        GameLoop loop = Loop();
        HumanTask human = Declare<HumanTask>(loop, type, id);
        human.Team = 1;

        Assert.False(ReadBool(loop, $"{type}_{id}.isDead"));
        Assert.Equal(1, ReadInt(loop, $"{type}_{id}.eTeam"));

        human.IsDead = true;
        human.Team = 0;

        Assert.True(ReadBool(loop, $"{type}_{id}.isDead"));
        Assert.Equal(0, ReadInt(loop, $"{type}_{id}.eTeam"));
    }

    /// <summary>
    /// Both are bound writable, because the original binds the raw storage rather than a reader — a
    /// script assigning to either changes the character.
    /// </summary>
    [Fact]
    public void BothAreWritableFromAScript()
    {
        GameLoop loop = Loop();
        HumanSoldierTask soldier = Declare<HumanSoldierTask>(loop, "HumanSoldier", 406);

        Assert.True(loop.Variables.TryWrite("HumanSoldier_406.isDead", QvmValue.FromInt32(1)));
        Assert.True(soldier.IsDead);

        Assert.True(loop.Variables.TryWrite("HumanSoldier_406.eTeam", QvmValue.FromInt32(2)));
        Assert.Equal(2, soldier.Team);
    }

    /// <summary>
    /// Unexporting takes both names away again, which is what <c>sub_4608D0</c> does by name before a
    /// mission tree is torn down.
    /// </summary>
    [Fact]
    public void UnexportingTakesBothNamesAway()
    {
        GameLoop loop = Loop();
        HumanSoldierTask soldier = Declare<HumanSoldierTask>(loop, "HumanSoldier", 407);

        Assert.Equal(2, loop.Variables.GetBoundNames(soldier).Count);
        Assert.True(loop.Variables.Unexport(soldier));

        Assert.False(loop.Variables.TryRead("HumanSoldier_407.isDead", out _));
        Assert.False(loop.Variables.TryRead("HumanSoldier_407.eTeam", out _));
    }
}
