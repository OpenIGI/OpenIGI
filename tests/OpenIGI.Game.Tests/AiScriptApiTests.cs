using OpenIGI.Game.Ai;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Covers the AI vocabulary transcribed from the two registrars. <see cref="RetailAiScriptTests"/> checks it
/// against the 884 shipped AI scripts.
/// </summary>
public sealed class AiScriptApiTests
{
    /// <summary>
    /// Fifty-five natives, split eighteen actions to thirty-seven functions, and no name registered twice.
    /// </summary>
    [Fact]
    public void TheApiIsEighteenActionsAndThirtySevenFunctions()
    {
        Assert.Equal(55, AiScriptApi.Natives.Count);
        Assert.Equal(18, AiScriptApi.Natives.Count(n => n.IsAction));
        Assert.Equal(37, AiScriptApi.Natives.Count(n => !n.IsAction));
        Assert.Equal(55, AiScriptApi.Natives.Select(n => n.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(55, AiScriptApi.Natives.Select(n => n.Address).Distinct().Count());
    }

    /// <summary>
    /// Every action is a command and every getter returns something — the split the three registrar wrappers
    /// encode. Only the four distance-and-random natives are real-valued.
    /// </summary>
    [Fact]
    public void TheReturnTagsSplitCommandsFromGetters()
    {
        foreach (AiNative native in AiScriptApi.Natives)
        {
            if (native.IsAction)
            {
                Assert.Equal(AiNativeReturn.Void, native.Return);
            }
        }

        string[] real = [.. AiScriptApi.Natives.Where(n => n.Return == AiNativeReturn.Real).Select(n => n.Name)];
        Assert.Equal(
            [
                "AIFunction_GetScriptRealValue",
                "AIFunction_GetRandomValue",
                "AIFunction_GetEventDistance",
                "AIFunction_GetAlarmTriggerDistance",
            ],
            real);

        // Every Get* is a getter and every Set* a command, with no exceptions.
        foreach (AiNative native in AiScriptApi.Natives)
        {
            if (native.Name.StartsWith("AIFunction_Get", StringComparison.Ordinal))
            {
                Assert.NotEqual(AiNativeReturn.Void, native.Return);
            }
            else if (native.Name.StartsWith("AIFunction_Set", StringComparison.Ordinal))
            {
                Assert.Equal(AiNativeReturn.Void, native.Return);
            }
        }
    }

    /// <summary>The event numbering is the registrar's, which is the reverse of the string-table order.</summary>
    [Fact]
    public void TheEventNumbersAreTheRegistrarsNotTheStringTables()
    {
        Assert.Equal(0, (int)AiEvent.Create);
        Assert.Equal(7, (int)AiEvent.Combat);
        Assert.Equal(16, (int)AiEvent.Gunshot);
        Assert.Equal(23, (int)AiEvent.FriendlyDetection);
        Assert.Equal(24, Enum.GetValues<AiEvent>().Length);

        // Contiguous from zero, which is what makes the registrar's run of literals a plain enum.
        Assert.Equal(Enumerable.Range(0, 24), Enum.GetValues<AiEvent>().Select(e => (int)e).Order());
    }

    /// <summary>Twenty-nine soldier archetypes, contiguous from zero, with RPG first.</summary>
    [Fact]
    public void ThereAreTwentyNineSoldierArchetypes()
    {
        Assert.Equal(29, Enum.GetValues<AiType>().Length);
        Assert.Equal(0, (int)AiType.Rpg);
        Assert.Equal(28, (int)AiType.SpetnazGuardSpas);
        Assert.Equal(Enumerable.Range(0, 29), Enum.GetValues<AiType>().Select(e => (int)e).Order());
    }

    /// <summary>
    /// The twenty-nine types collapse onto nine scripts, and <b>the faction prefix changes nothing</b>: every
    /// patrol variant runs <c>patrol</c> and every guard variant runs <c>guard</c>, whichever faction it names.
    /// </summary>
    [Fact]
    public void TheFactionPrefixDoesNotChangeTheScript()
    {
        AiType[] patrols =
        [
            AiType.PatrolUzi, AiType.PatrolAk, AiType.PatrolSpas, AiType.PatrolPistol,
            AiType.SecurityPatrolUzi, AiType.SecurityPatrolSpas,
            AiType.MafiaPatrolUzi, AiType.MafiaPatrolAk, AiType.MafiaPatrolSpas,
            AiType.SpetnazPatrolUzi, AiType.SpetnazPatrolAk, AiType.SpetnazPatrolSpas,
        ];
        AiType[] guards =
        [
            AiType.GuardUzi, AiType.GuardAk, AiType.GuardSpas, AiType.GuardPistol,
            AiType.MafiaGuardUzi, AiType.MafiaGuardAk, AiType.MafiaGuardSpas,
            AiType.SpetnazGuardUzi, AiType.SpetnazGuardAk, AiType.SpetnazGuardSpas,
        ];

        foreach (AiType type in patrols)
        {
            Assert.Equal(AiArchetype.Patrol, AiScriptApi.ArchetypeOf(type));
        }

        foreach (AiType type in guards)
        {
            Assert.Equal(AiArchetype.Guard, AiScriptApi.ArchetypeOf(type));
        }

        Assert.Equal(29, patrols.Length + guards.Length + 7);
    }

    /// <summary>
    /// The first seven types map to themselves, which is the argument that pins the script order. If script 0
    /// were <c>guard</c> — as its string address suggests — then <c>AITYPE_RPG</c> would run <c>guard.qsc</c>.
    /// </summary>
    [Fact]
    public void TheIdentityPrefixPinsTheScriptOrder()
    {
        Assert.Equal(AiArchetype.Rpg, AiScriptApi.ArchetypeOf(AiType.Rpg));
        Assert.Equal(AiArchetype.Gunner, AiScriptApi.ArchetypeOf(AiType.Gunner));
        Assert.Equal(AiArchetype.Sniper, AiScriptApi.ArchetypeOf(AiType.Sniper));
        Assert.Equal(AiArchetype.Anya, AiScriptApi.ArchetypeOf(AiType.Anya));
        Assert.Equal(AiArchetype.Ekk, AiScriptApi.ArchetypeOf(AiType.Ekk));
        Assert.Equal(AiArchetype.Priboi, AiScriptApi.ArchetypeOf(AiType.Priboi));
        Assert.Equal(AiArchetype.Civilian, AiScriptApi.ArchetypeOf(AiType.Civilian));

        Assert.Equal("LOCAL:common/ai/rpg.qsc", AiScriptApi.ScriptPath(AiArchetype.Rpg));
        Assert.Equal("LOCAL:common/ai/guard.qsc", AiScriptApi.ScriptPath(AiArchetype.Guard));
    }

    /// <summary>An unregistered type is refused rather than indexing past the map.</summary>
    [Fact]
    public void AnUnregisteredTypeIsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AiScriptApi.ArchetypeOf((AiType)29));

    /// <summary>
    /// A silenced shot carries a thirtieth as far as an unsilenced one — one metre against thirty. Both are
    /// registered as bare unit counts, and M9 read the same two numbers out of the weapon module.
    /// </summary>
    [Fact]
    public void TheGunshotRangesAreThirtyMetresAndOne()
    {
        Assert.Equal(30 * 4096, AiScriptApi.GunshotRange);
        Assert.Equal(1 * 4096, AiScriptApi.SilencedGunshotRange);
        Assert.Equal(30, AiScriptApi.GunshotRange / AiScriptApi.SilencedGunshotRange);
    }

    /// <summary>Every constant the AI module registers resolves, and the names are the engine's spelling.</summary>
    [Fact]
    public void EveryConstantResolvesUnderTheEnginesName()
    {
        // 24 events + 29 types + 2 flags + 2 alarm-access + 2 ranges.
        Assert.Equal(59, AiScriptApi.Constants.Count);

        Assert.True(AiScriptApi.TryGetConstant("AIEVENT_ALERT_RESPONSE", out int alertResponse));
        Assert.Equal(6, alertResponse);

        Assert.True(AiScriptApi.TryGetConstant("AIEVENT_GROUNDIMPACT", out int groundImpact));
        Assert.Equal(11, groundImpact);

        Assert.True(AiScriptApi.TryGetConstant("AITYPE_SECURITY_PATROL_SPAS", out int security));
        Assert.Equal(16, security);

        Assert.True(AiScriptApi.TryGetConstant("AIALARMACCESS_AFTERCOMBAT", out int afterCombat));
        Assert.Equal(1, afterCombat);

        Assert.False(AiScriptApi.TryGetConstant("AIEVENT_NOT_A_THING", out _));
    }

    /// <summary>The generated names round-trip back to their values.</summary>
    [Fact]
    public void TheGeneratedNamesRoundTrip()
    {
        foreach (AiEvent value in Enum.GetValues<AiEvent>())
        {
            Assert.True(AiScriptApi.TryGetConstant(AiScriptApi.EventName(value), out int n));
            Assert.Equal((int)value, n);
        }

        foreach (AiType value in Enum.GetValues<AiType>())
        {
            Assert.True(AiScriptApi.TryGetConstant(AiScriptApi.TypeName(value), out int n));
            Assert.Equal((int)value, n);
        }
    }
}
