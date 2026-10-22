using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>The flash event delivered to every human, <c>flashbang.c FlashBang_New</c>.</summary>
/// <param name="Position">Where the flash went off.</param>
/// <param name="Range">The player range delivered to every human.</param>
/// <param name="Team">The thrower's team, or minus one.</param>
/// <param name="Sight">The level's line-of-sight service.</param>
public readonly record struct Igi2FlashBangHit(Vector3d Position, float Range, int Team, Igi2DetectionWorld? Sight);

/// <summary>The one-tick puff and sound of <c>flashbang.c FlashBang_RunHandler</c>.</summary>
/// <param name="world">The effects and run stream.</param>
/// <param name="position">Where it went off.</param>
public sealed class Igi2FlashBang(Igi2ExplosionWorld world, Vector3d position) : IIgi2DynamicTask
{
    /// <summary>Gets its three-particle puff, <c>flashbang.c FlashBang_CreateHandler</c>.</summary>
    public Igi2Particle2DSystem? System { get; private set; }

    /// <summary>Creates the particle child after the flash task has joined the dynamic list.</summary>
    internal void Prepare()
    {
        System = world.Particles?.New("smoke1.spr", false, 0, default, new Vector3f(127f, 127f, 127f),
            2457.6f, 2457.6f, 819200f, 0x1000008, 0, 0.014583333f, 0f, 20, null);
    }

    /// <inheritdoc/>
    public bool Run()
    {
        // flashbang.c FlashBang_RunHandler: three low-word draws per particle, speed then intensity then spin.
        for (int i = 0; i < 3; i++)
        {
            Fpu speed = ((Fpu)(double)(ushort)world.Random.NextRaw() * 0.0000152587890625) + 1.0;
            float z = (speed * 1.5 * 4096000.0 * 9.259259059035685e-06).F;
            float intensity = ((((Fpu)(double)(ushort)world.Random.NextRaw() * 0.0000152587890625) + 1.0) * 0.25).F;
            Fpu spin = (Fpu)(double)(ushort)world.Random.NextRaw() * 0.0000152587890625;
            float rotation = (((((spin - 0.5) + spin) - 0.5) * 0.04363323375582695) + 0.02036217600107193).F;
            System?.AddParticle(position, new Vector3f(0f, 0f, z), intensity, rotation, 0f, 1f, 90, 30);
        }

        world.Sound("explo_flash", position);
        return false;
    }
}

/// <content>Flash grenade creation and human event dispatch.</content>
public sealed partial class Igi2ExplosionWorld
{
    /// <summary>Gets or sets the line-of-sight service flashbangs use.</summary>
    public Igi2DetectionWorld? FlashSight { get; set; }

    /// <inheritdoc/>
    public void FlashBang(Vector3d position, WorldIntersection ground, int team)
    {
        // flashbang.c FlashBang_New: every human receives the event, even outside the range or on the thrower's team.
        Igi2FlashBang flash = new(this, position);
        Dynamic.Add(flash);
        flash.Prepare();
        Igi2FlashBangHit hit = new(position, 122880f, team, FlashSight);
        foreach (Igi2Human human in Humans)
        {
            human.FlashBang(hit);
        }

        if (ground.HitObject is Igi2RigidObject or Igi2SplineSegment)
        {
            Effects.BurnMark(ground, 1024f);
        }
    }
}
