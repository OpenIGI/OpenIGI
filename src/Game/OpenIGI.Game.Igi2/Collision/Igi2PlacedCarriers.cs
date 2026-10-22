using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Collision;

/// <summary>
/// The I.G.I. 2 solids of another game's placed models, kept where that game has them: a train, a helicopter, a lift or a door
/// that moves carries its solid along, and what rides it.
/// </summary>
/// <remarks>
/// <para>
/// The host moves its placed models' collision as they move (Project I.G.I.'s <see cref="ObjectCollisionIndex"/>);
/// <see cref="Follow"/> copies each placement's pose as it stands onto its solid, once a tick, so I.G.I. 2's rounds, sight and
/// feet meet the model where it is.
/// </para>
/// <para>
/// A model that carries a graph is the graph's parent (<see cref="IAttachParent"/>): its nodes are held in the model's frame and
/// ride its solid, as a graph under one of I.G.I. 2's own movers rides it (<c>0x4F9770</c> holds a node so in Project I.G.I.).
/// </para>
/// </remarks>
public sealed class Igi2PlacedCarriers
{
    private readonly IReadOnlyDictionary<WorldPlacement, Igi2RigidObject> _solids;
    private readonly Func<WorldPlacement, (bool Found, Vector3d PositionMetres, Matrix3f Orientation)>? _pose;
    private readonly float[] _orientation = new float[9];

    /// <summary>Initializes a new instance of the <see cref="Igi2PlacedCarriers"/> class.</summary>
    /// <param name="solids">Each placement's solid, as <see cref="Igi2PlacedCollision.Build"/> made them.</param>
    /// <param name="pose">Where a placement's collision stands now, or <see langword="null"/> for a level whose models stay put.</param>
    public Igi2PlacedCarriers(
        IReadOnlyDictionary<WorldPlacement, Igi2RigidObject> solids,
        Func<WorldPlacement, (bool Found, Vector3d PositionMetres, Matrix3f Orientation)>? pose = null)
    {
        _solids = solids ?? throw new ArgumentNullException(nameof(solids));
        _pose = pose;
    }

    /// <summary>Gets a model's solid as a parent its riders are held relative to, or <see langword="null"/> when it has none.</summary>
    /// <param name="model">The model's task.</param>
    /// <returns>The parent.</returns>
    public IAttachParent? Carrier(GameTask model)
    {
        ArgumentNullException.ThrowIfNull(model);
        foreach ((WorldPlacement placement, Igi2RigidObject solid) in _solids)
        {
            if (ReferenceEquals(placement.Owner, model) && !placement.IsAttached)
            {
                return solid;
            }
        }

        return null;
    }

    /// <summary>Moves each solid to where the host has its placement now.</summary>
    public void Follow()
    {
        if (_pose is null)
        {
            return;
        }

        foreach ((WorldPlacement placement, Igi2RigidObject solid) in _solids)
        {
            (bool found, Vector3d metres, Matrix3f orientation) = _pose(placement);
            if (!found)
            {
                continue;
            }

            solid.Position = metres * WorldScene.UnitsPerMetre;
            orientation.CopyTo(_orientation);
            _orientation.CopyTo(solid.Orientation, 0);
        }
    }

}
