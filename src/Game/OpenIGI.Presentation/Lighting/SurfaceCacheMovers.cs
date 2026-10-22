using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Lighting;

/// <summary>
/// The pages of placements that move: their records rewritten to where each placement stands now,
/// uploaded once a frame, and listed apart from the static lookup grid.
/// </summary>
internal sealed class SurfaceCacheMovers
{
    private readonly IGraphicsDevice _device;
    private readonly StorageBufferHandle _pages;
    private readonly Vector3d _origin;

    // What a page that moves needs to be written again: every page's record as uploaded, the cards they
    // project, and which instance each placement became. A mover's pages carry their placement's CURRENT
    // transform, as a rigid body's cards do: what was captured is in the model's own space, so moving the
    // record moves the surface with it and nothing is recaptured.
    private readonly float[] _pageRecords;
    private readonly MeshCard[] _cards;
    private readonly SurfaceCacheInstances _instances;
    private readonly Dictionary<WorldPlacement, int> _instanceOf = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<int> _movingInstances = [];
    private readonly List<int> _movingPages = [];

    // Each moving placement's run of _movingPages and its bounding sphere, as the lookup lists them; the
    // spheres follow the placements every frame one of them moves.
    private readonly List<SurfaceCacheLookup.MoverObject> _movers = [];
    private bool _moversMoved;
    private int _dirtyLow = int.MaxValue;
    private int _dirtyHigh = -1;
    private bool _moversChanged;

    /// <summary>Initializes the movers over the uploaded page records.</summary>
    /// <param name="device">The device the records are uploaded to.</param>
    /// <param name="pages">The page record buffer.</param>
    /// <param name="origin">The level origin the records are measured from.</param>
    /// <param name="pageRecords">Every page's record, as uploaded.</param>
    /// <param name="cards">The cards the pages project.</param>
    /// <param name="instances">The per-placement lighting pages.</param>
    internal SurfaceCacheMovers(
        IGraphicsDevice device,
        StorageBufferHandle pages,
        Vector3d origin,
        float[] pageRecords,
        MeshCard[] cards,
        SurfaceCacheInstances instances)
    {
        _device = device;
        _pages = pages;
        _origin = origin;
        _pageRecords = pageRecords;
        _cards = cards;
        _instances = instances;
        for (int i = 0; i < instances.Instances.Length; i++)
        {
            if (instances.Instances[i].Placement is { } placement)
            {
                _instanceOf[placement] = i;
            }
        }
    }

    /// <summary>Notes a lookup built afresh, which knows nothing of what has already moved.</summary>
    internal void LookupReplaced() => _moversChanged = _movingPages.Count > 0;

    /// <summary>Carries a placement's pages to where it stands this frame, if it has moved.</summary>
    /// <param name="placement">The placement, as the cache was built from it.</param>
    /// <param name="position">Where it is drawn this frame.</param>
    /// <param name="orientation">How it is turned this frame.</param>
    internal void MovePlacement(WorldPlacement placement, Vector3d position, Matrix3f orientation)
    {
        if (!_instanceOf.TryGetValue(placement, out int index))
        {
            return;
        }

        SurfaceCacheInstances.Placed placed = _instances.Instances[index];
        bool moving = _movingInstances.Contains(index);
        if (!moving && position == placed.Position && orientation == placed.Orientation)
        {
            return;
        }

        if (!moving)
        {
            _movingInstances.Add(index);
            _moversChanged = true;
        }

        int firstMoving = _movingPages.Count;
        for (int slot = 0; slot < placed.PageCount; slot++)
        {
            int page = placed.FirstPage + slot;
            if (!_instances.Layout.Pages[page].IsAllocated)
            {
                continue;
            }

            int at = page * SurfaceCachePageRecords.FloatsPerPage;
            MeshCard card = _cards[_instances.CardForPage[page]];
            SurfaceCachePageRecords.WritePlacement(_pageRecords, at, card, position, orientation, _origin);

            // The record's spare word marks a page that moves: the lookup grid skips it, the lighting
            // leaves it alone. See the shared card lookup.
            _pageRecords[at + 23] = 1f;

            if (!moving)
            {
                _movingPages.Add(page);
            }

            _dirtyLow = Math.Min(_dirtyLow, page);
            _dirtyHigh = Math.Max(_dirtyHigh, page);
        }

        if (!moving && _movingPages.Count > firstMoving)
        {
            _movers.Add(new SurfaceCacheLookup.MoverObject(firstMoving, _movingPages.Count - firstMoving, default, 0f));
        }

        _moversMoved = true;
    }

    /// <summary>Uploads the pages moved this frame, and the movers' list if it grew.</summary>
    /// <param name="lookup">The card lookup the movers are listed in, if there is one.</param>
    internal void CommitMoves(SurfaceCacheLookup? lookup)
    {
        if (_moversMoved || _moversChanged)
        {
            BoundMovers();
        }

        if (_moversChanged)
        {
            lookup?.SetMovers(_movingPages, _movers);
            _moversChanged = false;
        }
        else if (_moversMoved)
        {
            lookup?.UpdateMoverBounds(_movers);
        }

        _moversMoved = false;

        if (_dirtyHigh >= _dirtyLow)
        {
            int from = _dirtyLow * SurfaceCachePageRecords.FloatsPerPage;
            int count = (_dirtyHigh - _dirtyLow + 1) * SurfaceCachePageRecords.FloatsPerPage;
            _device.UpdateStorageBuffer<float>(_pages, from, _pageRecords.AsSpan(from, count));
            _dirtyLow = int.MaxValue;
            _dirtyHigh = -1;
        }
    }

    /// <summary>Encloses each mover's pages, where their records stand now, in one sphere for the lookup to test.</summary>
    private void BoundMovers()
    {
        for (int m = 0; m < _movers.Count; m++)
        {
            SurfaceCacheLookup.MoverObject mover = _movers[m];

            // The centre of the box around the pages' spheres, then the radius that reaches the far side of
            // every one of them from it.
            Vector3f low = new(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3f high = new(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < mover.PageCount; i++)
            {
                int at = (_movingPages[mover.FirstPage + i] * SurfaceCachePageRecords.FloatsPerPage) + 36;
                float r = _pageRecords[at + 3];
                low = new Vector3f(
                    MathF.Min(low.X, _pageRecords[at] - r),
                    MathF.Min(low.Y, _pageRecords[at + 1] - r),
                    MathF.Min(low.Z, _pageRecords[at + 2] - r));
                high = new Vector3f(
                    MathF.Max(high.X, _pageRecords[at] + r),
                    MathF.Max(high.Y, _pageRecords[at + 1] + r),
                    MathF.Max(high.Z, _pageRecords[at + 2] + r));
            }

            Vector3f centre = new((low.X + high.X) * 0.5f, (low.Y + high.Y) * 0.5f, (low.Z + high.Z) * 0.5f);
            float radius = 0f;
            for (int i = 0; i < mover.PageCount; i++)
            {
                int at = (_movingPages[mover.FirstPage + i] * SurfaceCachePageRecords.FloatsPerPage) + 36;
                float dx = _pageRecords[at] - centre.X;
                float dy = _pageRecords[at + 1] - centre.Y;
                float dz = _pageRecords[at + 2] - centre.Z;
                radius = MathF.Max(radius, MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) + _pageRecords[at + 3]);
            }

            _movers[m] = mover with { Centre = centre, Radius = radius };
        }
    }
}
