using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// What a footstep sounds like, and how far it is heard — the rules of <c>0x463920</c>.
/// </summary>
/// <remarks>
/// <para>
/// Animation event <b>1010</b> is by far the commonest event in the retail animation data — 117 of
/// 245 — and it is deliberately absent from the <c>animtrigger</c> table, because a table entry names
/// one fixed sound and a footstep depends on what is underfoot. This is where that choice is made
/// instead.
/// </para>
/// </remarks>
public static class FootstepSounds
{
    /// <summary>The animation event a footstep is.</summary>
    public const int EventId = 1010;

    /// <summary>The material a step on bare terrain lands on.</summary>
    /// <remarks>
    /// <para>
    /// The ground query <c>0x462390</c> does not read a material out of the ray hit. It <b>asks the task
    /// that was hit</b> — <c>dword_A9A6E0[hitTask.Type]</c> dispatches a probe message, and the answer
    /// lands at offset 76 of the cached hit block, which is <c>Human+1772</c>, the field the footstep
    /// handler reads. So the material is a property of the thing underfoot, not of the geometry.
    /// </para>
    /// <para>
    /// <b>The terrain answers too, and it is not always <c>Ground</c>.</b> It has no task, so the leaf
    /// query answers for itself: <c>0x4CB050</c> ends by calling <c>0x4CB810</c>, which takes the low
    /// byte of the terrain material's <i>first</i> field — argument 1 of <c>CreateTerrainMaterial</c>,
    /// long mistaken for a flag word — and uses it to select the same per-level material array a mesh
    /// face selects with its own index. So the ground under your feet outdoors names a game material
    /// exactly as a crate does.
    /// </para>
    /// <para>
    /// Across the campaign that field takes two values: <b>1</b> (<c>Ground</c>) in levels 1–6, 9, 10,
    /// 13 and 14, and <b>20</b> (<c>Snow</c>) in the four snow missions, 7, 8, 11 and 12. Every terrain
    /// material within a level agrees, which is why <see cref="TerrainGameMaterialIndex"/> can answer
    /// for the whole map; the name below stays only as the fallback for a level that declares nothing.
    /// </para>
    /// <para>
    /// <b>What is not modelled:</b> the per-point half. The original reaches its terrain material
    /// through a message-13 broadcast at the hit position, so in principle two regions of one map could
    /// answer differently. No retail level does — every material in a level carries the same value — so
    /// picking any of them gives the retail answer.
    /// </para>
    /// </remarks>
    public const string TerrainMaterialName = "Ground";

    /// <summary>The game material index the terrain of a level declares, or null when it declares none.</summary>
    /// <param name="terrainMaterials">The level's terrain materials, keyed by id.</param>
    /// <returns>The row to look up in the game material table.</returns>
    /// <remarks>
    /// Every terrain material in a retail level declares the same index, so the first is the answer. A
    /// level that somehow disagreed would need the original's per-point message-13 lookup; taking the
    /// lowest id keeps that case deterministic rather than dependent on dictionary order.
    /// </remarks>
    public static int? TerrainGameMaterialIndex(IReadOnlyDictionary<int, TerrainMaterial> terrainMaterials)
    {
        ArgumentNullException.ThrowIfNull(terrainMaterials);

        int? lowest = null;
        int index = 0;

        foreach (KeyValuePair<int, TerrainMaterial> pair in terrainMaterials)
        {
            if (lowest is null || pair.Key < lowest)
            {
                lowest = pair.Key;
                index = pair.Value.GameMaterialIndex;
            }
        }

        return lowest is null ? null : index;
    }

    /// <summary>How far above the ground the material probe starts, in engine units.</summary>
    public const double ProbeAboveInUnits = 204.8;

    /// <summary>How far below its origin the material probe reaches, in engine units.</summary>
    public const double ProbeDepthInUnits = 2048.0;

    /// <summary>Engine units in a metre, the factor a material's hearing radius is authored against.</summary>
    public const double UnitsPerMetre = 4096.0;

    /// <summary>
    /// The order the six walk sounds of a material are used in — a fixed twelve-step cycle, not a draw.
    /// </summary>
    /// <remarks>
    /// Transcribed from the twelve dwords at <c>0x53F318</c>, which the handler walks with a per-human
    /// cursor advanced <c>(i + 1) % 12</c>. It is <b>not</b> a permutation: the first six use each
    /// sound once, the second six are an irregular tail in which <c>3</c> appears twice and <c>0</c> not
    /// at all. Authored by hand, so it is copied rather than generated.
    /// </remarks>
    public static ReadOnlySpan<int> Cycle => [0, 1, 2, 3, 4, 5, 3, 1, 4, 2, 3, 5];

    /// <summary>Takes the next walk sound for a material and advances the walker's cursor.</summary>
    /// <param name="material">The material under the feet.</param>
    /// <param name="cursor">The walker's own position in <see cref="Cycle"/>; advanced by one.</param>
    /// <returns>The sound definition's name, or an empty string when the material names none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="material"/> is <see langword="null"/>.</exception>
    public static string Next(GameMaterial material, ref int cursor) => Next(material, ref cursor, out _);

    /// <summary>Takes the next walk sound, and reports which of the six it was.</summary>
    /// <param name="material">The material under the feet.</param>
    /// <param name="cursor">The walker's own position in <see cref="Cycle"/>; advanced by one.</param>
    /// <param name="slot">Receives the sound's index, 0 to 5.</param>
    /// <returns>The sound definition's name, or an empty string when the material names none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="material"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The slot is worth having on its own: the rain layer is named from the same index rather than
    /// drawn again, so the two sounds of one step always belong to each other.
    /// </remarks>
    public static string Next(GameMaterial material, ref int cursor, out int slot)
    {
        ArgumentNullException.ThrowIfNull(material);

        slot = Cycle[((cursor % Cycle.Length) + Cycle.Length) % Cycle.Length];
        cursor = (cursor + 1) % Cycle.Length;

        return slot < material.WalkSounds.Count ? material.WalkSounds[slot] : string.Empty;
    }

    /// <summary>The wet layer a step lays over its own sound while the level's weather is on.</summary>
    /// <param name="slot">The cycle slot the step took, 0 to 5.</param>
    /// <returns>The definition name, <c>walk_rain_1</c> through <c>walk_rain_6</c>.</returns>
    /// <remarks>
    /// <para>
    /// <c>0x463920</c> builds it with <c>sprintf(buffer, "walk_rain_%1d", slot + 1)</c> and plays it
    /// <b>as well as</b> the material's own sound, at the same position and the same pitch. It is a
    /// second voice layered on the first, not a replacement — walking on metal in the rain is the metal
    /// step and the wet step together.
    /// </para>
    /// <para>
    /// The gate is <c>0x416FB0</c>, which returns a single global byte. That is the level's weather
    /// flag, not a shelter test, so standing under a roof does not stop it.
    /// </para>
    /// </remarks>
    public static string RainSoundName(int slot) =>
        $"walk_rain_{Math.Clamp(slot, 0, 5) + 1}";

    /// <summary>How much of its volume a crouched step keeps.</summary>
    /// <remarks>
    /// <para>
    /// <c>0.6666667</c>. The handler tests <c>Human+829</c> — the crouch byte — and, when it is set,
    /// sends the started voice a two-float message, <c>-1.0</c> and <c>0.6666667</c>
    /// (<c>0x4E6C60</c>). The second is the volume the voice keeps; the <c>-1.0</c> reads as "leave the
    /// other alone", which is how the same message is used elsewhere, though that half is inference
    /// rather than something traced to a handler.
    /// </para>
    /// <para>
    /// The same crouch test also skips the AI hearing event entirely, so crouching is both quieter to
    /// the ear and completely silent to a guard.
    /// </para>
    /// </remarks>
    public const float CrouchedVolumeScale = 0.6666667f;

    /// <summary>Reports whether a material names anything to walk on.</summary>
    /// <param name="material">The material a probe struck.</param>
    /// <returns><see langword="false"/> when every one of its six walk sounds is empty.</returns>
    /// <remarks>
    /// <para>
    /// Six rows of the retail table name no walk sound at all: <c>Air</c>, <c>Water</c>, <c>Flesh</c>,
    /// <c>BloodyFlesh</c>, <c>HumanCollision</c>, <c>MetalLadder</c> and <c>MetalFence</c>. None of them
    /// is a floor, and the original never asks them: <c>0x462390</c> takes the material from the
    /// <b>ground task</b> it is already standing on, through a message dispatch, rather than from
    /// whatever a fresh downward ray happens to strike first.
    /// </para>
    /// <para>
    /// This port has no ground task to ask, so it casts that ray — and near the player it lands on
    /// human-collision volumes, which named nothing and left every step on placed geometry silent.
    /// Treating a row that names no walk as "not the floor" and going on to the ground beneath is what
    /// keeps the port's answer the same as the original's.
    /// </para>
    /// </remarks>
    public static bool NamesAWalk(GameMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);

        foreach (string sound in material.WalkSounds)
        {
            if (!string.IsNullOrEmpty(sound))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The pitch a footstep plays at.</summary>
    /// <param name="random">The engine's generator.</param>
    /// <returns>A ratio between 0.8 and 1.3.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The original computes <c>rand() * 0.5 + 1.0 - 0.2</c>, which is the same thing written the way
    /// the compiler folded it. Two steps on the same surface are never quite the same sound, and this
    /// is the whole of why.
    /// </remarks>
    public static float Pitch(IgiRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return (float)((random.NextDouble() * 0.5) + 0.8);
    }

    /// <summary>How far away a step on this material is heard by the AI, in engine units.</summary>
    /// <param name="material">The material under the feet.</param>
    /// <returns>The radius in engine units.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="material"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The handler posts its hearing event with the material's own argument-5 number times 4096, so the
    /// ground decides how far a footstep carries as well as what it sounds like — gravel gives a
    /// sneaking player away where carpet does not.
    /// </remarks>
    public static double HearingRadiusInUnits(GameMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);
        return material.Value5 * UnitsPerMetre;
    }

    /// <summary>The sound the ground makes when a body lands on it.</summary>
    /// <param name="material">The material under the feet.</param>
    /// <returns>The definition name, or empty when the material names none.</returns>
    /// <remarks>
    /// <para>
    /// Both airborne states play this on contact and they play it identically: the falling per-tick
    /// (<c>0x40BE40</c>) and the jump's (<c>0x40BBD0</c>) each read the material pointer at
    /// <c>Human+1772</c>, add <b>276</b>, and hand that straight to the sound bank. That lands on the
    /// field straight after the six 32-byte walk names, which the same handler reaches at
    /// <c>+84 + 32·slot</c>. The reader <c>0x407F50</c> confirms the order from the other end — it
    /// stores the name, three reals, a flag, argument 5, then <b>six walk names, the land name, the
    /// rain name</b>, six bullet names and two spare — though at its own offsets, which run 32 lower
    /// than the pointer these two states hold.
    /// </para>
    /// <para>
    /// <b>It is not the fall-damage sound.</b> <c>player_fall_1..3</c> come from <c>0x408460</c> and
    /// only when the drop actually hurt; this one plays on <i>every</i> landing, hurt or not, and it is
    /// what a harmless hop off a crate sounds like. It takes no pitch variation and no volume change.
    /// </para>
    /// <para>
    /// The shipped bank holds <c>land_ground_1</c> through <c>land_ground_5</c> and nothing else of the
    /// kind, so every row names one of those five however hard the surface is — <c>material.qvm</c>
    /// gives <c>Air</c> the first and <c>Wood</c> the fifth. A row naming none lands in silence.
    /// </para>
    /// </remarks>
    public static string LandSound(GameMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);
        return material.LandSound;
    }

    /// <summary>Where to start and end the downward probe that finds the ground material.</summary>
    /// <param name="groundPoint">The point the feet are standing on, in engine units.</param>
    /// <returns>The probe's start and end, in engine units.</returns>
    /// <remarks>
    /// A separate cast from the one that found the ground height: the original probes from 0.05 m above
    /// the ground point down to 0.5 m below it, which is short enough not to punch through a floor into
    /// whatever is under it.
    /// </remarks>
    public static (Vector3d From, Vector3d To) Probe(Vector3d groundPoint) => (
        new Vector3d(groundPoint.X, groundPoint.Y, groundPoint.Z + ProbeAboveInUnits),
        new Vector3d(groundPoint.X, groundPoint.Y, groundPoint.Z - ProbeDepthInUnits));
}
