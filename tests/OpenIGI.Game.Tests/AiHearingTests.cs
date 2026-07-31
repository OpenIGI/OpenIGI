using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// A soldier hearing a gunshot — the delivery of <see cref="AiEvent.Gunshot"/>, gated by the two ranges the
/// AI module registers: 30 m normally, 1 m silenced.
/// </summary>
public sealed class AiHearingTests
{
    private const double Metre = 4096.0;

    /// <summary>A shot ten metres off is heard and queues the event.</summary>
    [RequiresGameDataFact]
    public void ASoldierHearsAShotWithinThirtyMetres()
    {
        AiSoldier soldier = Soldier(out Vector3d position);

        Assert.True(soldier.HearGunshot(Away(position, 10 * Metre), silenced: false));
        Assert.True(soldier.Events.PendingCount(AiEvent.Gunshot) > 0);
    }

    /// <summary>A shot forty metres off is out of earshot and queues nothing.</summary>
    [RequiresGameDataFact]
    public void ASoldierDoesNotHearAShotBeyondThirtyMetres()
    {
        AiSoldier soldier = Soldier(out Vector3d position);

        Assert.False(soldier.HearGunshot(Away(position, 40 * Metre), silenced: false));
        Assert.Equal(0, soldier.Events.PendingCount(AiEvent.Gunshot));
    }

    /// <summary>
    /// A silenced weapon carries a thirtieth as far: a shot two metres away is heard unsilenced and missed
    /// silenced, and a shot inside a metre is heard even silenced.
    /// </summary>
    [RequiresGameDataFact]
    public void ASilencedShotIsHeardOnlyWithinAMetre()
    {
        Assert.True(Soldier(out Vector3d a).HearGunshot(Away(a, 2 * Metre), silenced: false));
        Assert.False(Soldier(out Vector3d b).HearGunshot(Away(b, 2 * Metre), silenced: true));
        Assert.True(Soldier(out Vector3d c).HearGunshot(Away(c, 0.8 * Metre), silenced: true));
    }

    /// <summary>A dead soldier hears nothing.</summary>
    [RequiresGameDataFact]
    public void ADeadSoldierHearsNothing()
    {
        AiSoldier soldier = Soldier(out Vector3d position);
        soldier.Kill();

        Assert.False(soldier.HearGunshot(Away(position, 1 * Metre), silenced: false));
    }

    private static Vector3d Away(Vector3d from, double units) => new(from.X + units, from.Y, from.Z);

    private static AiSoldier Soldier(out Vector3d position)
    {
        string path = Path.Combine(
            GameData.RequireRoot(), "missions", "location0", "level1", "graphs", "graph4019.dat");
        GraphFile graph = GraphFile.Read(File.ReadAllBytes(path));
        position = graph.Nodes[0].Position;
        return new AiSoldier(graph, position, facing: 0f, phase: 0, seed: 1);
    }
}
