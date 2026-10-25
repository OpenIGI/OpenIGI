using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player;

/// <summary>A title's player whose binoculars the host presents in the remastered style: open, what they read, whom they picked out.</summary>
/// <remarks>
/// Project I.G.I.'s own player is presented through its viewpoint; a title's player that carries binoculars of its own says so
/// through this, so the lens, the interface and the highlight follow whichever player is looking.
/// </remarks>
public interface ITitlePlayerOptics
{
    /// <summary>Gets a value indicating whether the player is looking through the binoculars.</summary>
    bool IsBinocularsOpen { get; }

    /// <summary>Gets where the player is looking, for the compass, in radians.</summary>
    float BinocularHeadingRadians { get; }

    /// <summary>Gets how many times the binoculars magnify the unzoomed view.</summary>
    float BinocularMagnification { get; }

    /// <summary>Gets the range to what the centre of the view rests on, in metres, or null when nothing is.</summary>
    float? BinocularRangeMetres { get; }

    /// <summary>Gets whom the binoculars have picked out, each as it is drawn.</summary>
    IReadOnlyList<TitleBinocularContact> BinocularContacts { get; }
}

/// <summary>One person a title's binoculars picked out.</summary>
/// <param name="Body">The object the person's body is drawn for, which the highlight is keyed by.</param>
/// <param name="Centre">The middle of the person, in the title's world units.</param>
/// <param name="HalfHeight">Half the person's height, in world units: the box spans it either way.</param>
/// <param name="Marker">Which class of contact the person is.</param>
/// <param name="Id">The slot the binoculars hold them in, for what animates per contact.</param>
/// <param name="Alpha">How strongly they are marked: one while seen, fading once lost.</param>
/// <param name="PulseScale">How far the mark's entry pulse is from settled: above one while freshly seen.</param>
public readonly record struct TitleBinocularContact(
    object Body,
    Vector3d Centre,
    double HalfHeight,
    BinocularMarkerColor Marker,
    int Id,
    float Alpha,
    float PulseScale);
