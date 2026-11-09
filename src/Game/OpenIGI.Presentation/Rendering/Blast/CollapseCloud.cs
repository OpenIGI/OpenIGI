using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation.Rendering.Blast;

/// <summary>
/// The dust a building throws up as it comes down: a dome off its footprint that bursts out as the slabs land, rolls out
/// along the ground past the walls and hangs for the rest of the round.
/// </summary>
/// <param name="groundMetres">The footprint's middle on the ground, world metres.</param>
/// <param name="footprintMetres">How far the footprint reaches from its middle, metres.</param>
/// <param name="heightMetres">How tall the building stood, metres.</param>
/// <param name="seed">The seed its billows are placed by.</param>
/// <param name="delayTicks">How many ticks after it is made the dust starts: the slabs have to fall first.</param>
public sealed class CollapseCloud(Vector3d groundMetres, float footprintMetres, float heightMetres, uint seed, int delayTicks)
{
    /// <summary>Gets the footprint's middle on the ground, world metres.</summary>
    public Vector3d GroundMetres { get; } = groundMetres;

    /// <summary>Gets the seed its billows are placed by.</summary>
    public uint Seed { get; } = seed;

    /// <summary>Gets the ticks it has run, counting from before it starts.</summary>
    public int Ticks { get; private set; } = -Math.Max(delayTicks, 0);

    /// <summary>Steps it one game tick.</summary>
    public void Tick() => Ticks++;

    /// <summary>Its age at this frame, in seconds; negative before it starts.</summary>
    /// <param name="blend">How far the frame is between the last tick and the next.</param>
    /// <returns>The age.</returns>
    public float Age(float blend) => (Ticks + Math.Clamp(blend, 0f, 1f)) / BombBlast.TickRate;

    /// <summary>How far it has rolled out from the footprint's middle, metres: past the walls fast, then slowly.</summary>
    /// <param name="age">Its age, in seconds.</param>
    /// <returns>The reach.</returns>
    public float RadiusMetres(float age) =>
        footprintMetres * (0.55f + (1.05f * (1f - MathF.Exp(-MathF.Max(age, 0f) / 0.9f))) + (0.04f * MathF.Max(age, 0f)));

    /// <summary>How high it stands, metres: up past the roofline, then settling.</summary>
    /// <param name="age">Its age, in seconds.</param>
    /// <returns>The height.</returns>
    public float HeightMetres(float age) =>
        MathF.Max(heightMetres, 3f) * (0.45f + (0.85f * BombBlast.Smooth(0f, 2.5f, age)) - (0.2f * BombBlast.Smooth(4f, 14f, age)));

    /// <summary>How thick it is: in at once as the slabs land, thinning to a haze by the round's end.</summary>
    /// <param name="age">Its age, in seconds.</param>
    /// <returns>One at its thickest.</returns>
    public static float Density(float age) =>
        age <= 0f ? 0f : BombBlast.Smooth(0f, 0.5f, age) * (1f - (0.75f * BombBlast.Smooth(3f, 14f, age)));
}
