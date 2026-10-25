using OpenIGI.Core.Numerics;
using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game.World;

/// <summary>
/// The fragments an explosion throws, <c>SimpointObjContainer</c>: they fall and slow, and turn together at a spin that dies
/// away; none of it touches anything. Both titles run it alike (Project I.G.I.'s <c>0x5035B0</c>; I.G.I. 2's
/// <c>SimpointObjContainer_RunHandler</c>, <c>igi2 0x5B6080</c>).
/// </summary>
/// <param name="model">The fragments' model.</param>
/// <param name="lifetime">The ticks each lives.</param>
/// <param name="gravityScale">What the gravity is scaled by: the explosion's full range over 4551.</param>
/// <param name="spin">The spin about each axis, radians a tick.</param>
public sealed class ExplosionFragments(string model, int lifetime, float gravityScale, Vector3f spin) : IDynamicTask
{
    /// <summary>What a fragment's velocity keeps each tick, <c>0x3F7AE148</c>.</summary>
    public const float AirResistance = 0.98f;

    /// <summary>The fragments' gravity before the explosion's scale, <c>0x4232674F</c>.</summary>
    public const float Gravity = 44.600887f;

    private readonly List<ExplosionFragment> _fragments = [];
    private Vector3f _spin = spin;

    /// <summary>Gets the fragments' model.</summary>
    public string Model { get; } = model;

    /// <summary>Gets the ticks each fragment lives.</summary>
    public int Lifetime { get; } = lifetime;

    /// <summary>Gets the fragments still flying.</summary>
    public IReadOnlyList<ExplosionFragment> Flying => _fragments;

    /// <summary>Gets or sets a value indicating whether the fragments have been thrown; an emptied container that has is gone.</summary>
    public bool IsStarted { get; set; }

    /// <summary>Adds a fragment, <c>SimpointObjContainer_Add</c> (<c>igi2 0x5B68E0</c>).</summary>
    /// <param name="fragment">The fragment.</param>
    public void Add(ExplosionFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        fragment.Lifetime = Lifetime;
        _fragments.Add(fragment);
    }

    /// <inheritdoc/>
    public bool Run()
    {
        if (_fragments.Count == 0)
        {
            return !IsStarted;
        }

        Fpu air = AirResistance;
        float gravity = (((Fpu)gravityScale * Gravity) * air).F;
        _spin = new Vector3f(((Fpu)_spin.X * 0.99f).F, ((Fpu)_spin.Y * 0.99f).F, ((Fpu)_spin.Z * 0.99f).F);
        float[] turn = EngineEuler.Rotation(_spin.X, _spin.Y, _spin.Z);
        foreach (ExplosionFragment f in _fragments)
        {
            Vector3f v = f.Velocity;
            float vx = (air * v.X).F, vy = (air * v.Y).F, vz = ((air * v.Z) - gravity).F;
            f.Velocity = new Vector3f(vx, vy, vz);
            f.Position = new Vector3d(((Fpu)vx + f.Position.X).Value, ((Fpu)vy + f.Position.Y).Value, ((Fpu)vz + f.Position.Z).Value);
            float[] o = f.Orientation;
            float[] turned = new float[9];
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    turned[(3 * r) + c] = ((((Fpu)turn[3 * r] * o[c]) + ((Fpu)turn[(3 * r) + 2] * o[6 + c])) + ((Fpu)turn[(3 * r) + 1] * o[3 + c])).F;
                }
            }

            turned.CopyTo(o, 0);
        }

        // SimpointObj_RunHandler (igi2 0x5B66E0): each counts its life down, and is gone once it has none.
        for (int i = _fragments.Count - 1; i >= 0; i--)
        {
            ExplosionFragment f = _fragments[i];
            if (f.Lifetime > 0)
            {
                f.Lifetime--;
            }
            else
            {
                _fragments.RemoveAt(i);
            }
        }

        return true;
    }
}
