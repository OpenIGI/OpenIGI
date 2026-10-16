using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Flow;

namespace OpenIGI.Game.Igi2.Humans;

/// <content>A human as the areas count it.</content>
public abstract partial class Igi2Human : IIgi2AreaOccupant
{
    /// <inheritdoc/>
    /// <remarks>Only a soldier whose mind is a <c>HumanAI</c> has one.</remarks>
    public virtual bool IsAi => false;

    /// <inheritdoc/>
    /// <remarks>
    /// <c>BoneDynCubeObj_GetRootPosition</c> (<c>igi2 0x5680F0</c>): the root bone's translation turned by the body's frame and added
    /// to its position, each row summed second term first.
    /// </remarks>
    public Vector3d RootPosition
    {
        get
        {
            Matrix3f m = Orientation;
            Vector3f r = RootTranslation;
            Fpu x = (((Fpu)m.M12 * r.Y) + ((Fpu)m.M11 * r.X)) + ((Fpu)m.M13 * r.Z);
            Fpu y = (((Fpu)m.M22 * r.Y) + ((Fpu)m.M21 * r.X)) + ((Fpu)m.M23 * r.Z);
            Fpu z = (((Fpu)m.M32 * r.Y) + ((Fpu)m.M31 * r.X)) + ((Fpu)m.M33 * r.Z);
            Vector3d p = Position;
            return new Vector3d((x + p.X).Value, (y + p.Y).Value, (z + p.Z).Value);
        }
    }
}
