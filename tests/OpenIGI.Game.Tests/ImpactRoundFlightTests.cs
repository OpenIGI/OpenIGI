using OpenIGI.Core.Numerics;
using OpenIGI.Game.Weapons;
using Xunit;
using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game.Tests;

/// <summary>The impact round's shared tick against each game's own: the M203's disassembly and I.G.I. 2's transcription it replaced.</summary>
public sealed class ImpactRoundFlightTests
{
    [Fact]
    public void A_tick_of_flight_is_each_game_s_own()
    {
        Random random = new(4);
        for (int c = 0; c < 20000; c++)
        {
            Vector3f speed = new(Value(random, 700f), Value(random, 700f), Value(random, 700f));
            Vector3d at = new(Value(random, 4e6f), Value(random, 4e6f), Value(random, 2e5f));

            Vector3f one = speed;
            Vector3d oneAt = at;
            ImpactRoundFlight.Step<X87>(ref one, ref oneAt);
            Assert.Equal(Igi1(speed, at), (one, oneAt));

            Vector3f two = speed;
            Vector3d twoAt = at;
            ImpactRoundFlight.Step<Fpu>(ref two, ref twoAt);
            Assert.Equal(Igi2(speed, at), (two, twoAt));
        }
    }

    private static float Value(Random random, float scale) => (float)((random.NextDouble() - 0.5) * 2.0 * scale);

    // 0x476340 as disassembled: fld/fmul/fst keeps x's product in the register for the fadd; y and z go through the stack.
    private static (Vector3f, Vector3d) Igi1(Vector3f v, Vector3d p)
    {
        X87 x = (X87)v.X * 0.995f;
        float y = ((X87)v.Y * 0.995f).F;
        float z = (((X87)v.Z - 35.68071f) * 0.995f).F;
        return (new Vector3f(x.F, y, z), new Vector3d((x + p.X).Value, ((X87)y + p.Y).Value, ((X87)z + p.Z).Value));
    }

    // Igi2ImpactGrenade.Run (0x4CC2F0) before the merge.
    private static (Vector3f, Vector3d) Igi2(Vector3f speed, Vector3d from)
    {
        const double Damping = 0.9950000047683716;
        const double Gravity = 35.68070983886719;
        float sx = ((Fpu)speed.X * Damping).F;
        float sy = ((Fpu)speed.Y * Damping).F;
        float sz = (((Fpu)speed.Z - Gravity) * Damping).F;
        return (new Vector3f(sx, sy, sz), new Vector3d(((Fpu)sx + from.X).Value, ((Fpu)sy + from.Y).Value, ((Fpu)sz + from.Z).Value));
    }
}
