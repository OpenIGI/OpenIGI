using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Saves;

namespace OpenIGI.Game.Igi2.Paths;

/// <content>
/// Its save state, <c>SplinePathGuideQTask_ptSerializeParameterList</c>: the path it follows, the object it moves, the segment it is on, its flags and how far
/// along the segment it is.
/// </content>
public sealed partial class Igi2SplinePathGuideTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Int(PathId);
        writer.Int(ModelId);
        writer.Int(_segment);
        writer.Int(_flags);
        writer.Real(_t);
    }

    /// <inheritdoc/>
    /// <remarks>Its place along the path follows from the segment and how far along it.</remarks>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        PathId = reader.Int();
        ModelId = reader.Int();
        _segment = reader.Int();
        _flags = reader.Int();
        _t = reader.Real();
        _position = ((Fpu)(double)_segment + _t).Value;
    }
}
