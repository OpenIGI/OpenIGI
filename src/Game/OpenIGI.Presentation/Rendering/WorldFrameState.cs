using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// One frame's camera-independent state for every placement, resolved once and shared by every pass
/// drawn from it: whether it draws, as what model, and where.
/// </summary>
/// <remarks>
/// The shadow cascades and the geometry pass each draw the whole scene, and none of what this holds
/// depends on the camera. It is rebuilt whenever the renderer begins a frame or is handed a new scene.
/// </remarks>
internal sealed class WorldFrameState
{
    private readonly WorldPlacementTransforms _transforms;
    private readonly WorldSubsetResolver _subsets;
    private readonly OcclusionSettle _occlusionSettle;

    /// <summary>One frame's per-placement state, shared by every pass drawn from it.</summary>
    /// <remarks>
    /// The shadow cascades and the geometry pass each draw the whole scene, and none of what this
    /// holds — pickup and gate visibility, door and elevator and cutscene transforms — depends on the
    /// camera. Resolving it once per frame instead of once per pass is what took the door registry's
    /// linear scan out of the profile's top ten.
    /// </remarks>
    private WorldFrameEntry[] _frame = [];
    private int[] _drawOrder = [];
    private WorldScene? _frameScene;
    private bool _framePrepared;
    private long _frameNumber;

    // Placements that have ever been drawn away from their authored transform: see PrepareFrame.
    private readonly HashSet<WorldPlacement> _movers = new(ReferenceEqualityComparer.Instance);

    // Where the static casters changed since a kept shadow last asked (TakeStaticCasterChanges): a placement that
    // began to move and so left the static set, appeared, went, or swapped its model. Past the cap the list is
    // dropped for a reset, which a kept shadow takes as everything having changed.
    private const int MaxStaticChanges = 4096;
    private readonly List<WorldCasterBounds> _staticChanges = [];

    // This frame's movers in draw order: all a movable-only pass has to walk, where the whole order is thousands.
    private readonly List<int> _movingOrder = [];
    private bool _staticReset = true;

    /// <summary>Initializes the frame state over the transforms and the resolver it consults.</summary>
    /// <param name="transforms">What each placement is displayed as, and where.</param>
    /// <param name="subsets">The model and subset caches.</param>
    /// <param name="occlusionSettle">The occlusion verdicts, forgotten when the scene changes.</param>
    internal WorldFrameState(
        WorldPlacementTransforms transforms,
        WorldSubsetResolver subsets,
        OcclusionSettle occlusionSettle)
    {
        _transforms = transforms;
        _subsets = subsets;
        _occlusionSettle = occlusionSettle;
    }

    /// <summary>Gets or sets the title's own view culling, asked once a frame for every placement.</summary>
    internal IPlacementVisibility? Visibility { get; set; }

    /// <summary>Gets the prepared frame's entry for every placement, in scene order.</summary>
    internal WorldFrameEntry[] Entries => _frame;

    /// <summary>Gets the placements in draw order.</summary>
    internal int[] DrawOrder => _drawOrder;

    /// <summary>Gets how many frames have been prepared.</summary>
    internal long FrameNumber => _frameNumber;

    /// <summary>Gets every placement that has ever been drawn away from its authored transform.</summary>
    internal IReadOnlyCollection<WorldPlacement> Movers => _movers;

    /// <summary>Marks the prepared state stale, so the next pass resolves it again.</summary>
    internal void Invalidate() => _framePrepared = false;

    /// <summary>Prepares the frame for a scene unless it already is.</summary>
    /// <param name="scene">The scene about to be drawn.</param>
    internal void EnsurePrepared(WorldScene scene)
    {
        if (!_framePrepared || !ReferenceEquals(_frameScene, scene))
        {
            PrepareFrame(scene);
        }
    }

    /// <summary>What the last prepared frame decided for every placement a task owns.</summary>
    /// <param name="owner">The task whose placements are described.</param>
    /// <returns>One line per placement.</returns>
    internal IEnumerable<string> DescribeForTesting(Engine.Tasks.GameTask owner)
    {
        if (_frameScene is not { } scene || _frame.Length != scene.Placements.Count)
        {
            yield break;
        }

        for (int i = 0; i < _frame.Length; i++)
        {
            WorldPlacement placement = scene.Placements[i];
            if (!ReferenceEquals(placement.Owner, owner))
            {
                continue;
            }

            WorldFrameEntry entry = _frame[i];
            Vector3d at = entry.Position * WorldScene.UnitsPerMetre;
            yield return entry.Skip
                ? $"{placement.Instance.Name} SKIPPED destroyed={entry.WasDestroyed} model={(entry.Model is null ? "null" : "set")}"
                : $"{placement.Instance.Name} drawn as {entry.Displayed?.Instance.Name} at ({at.X:0},{at.Y:0},{at.Z:0}) destroyed={entry.WasDestroyed}";
        }
    }

    /// <summary>Hands over where the static casters changed since the last call, and forgets them.</summary>
    /// <param name="changes">Receives the changes.</param>
    /// <returns><see langword="true"/> when everything is to be taken as changed.</returns>
    internal bool TakeStaticCasterChanges(List<WorldCasterBounds> changes)
    {
        bool reset = _staticReset;
        changes.AddRange(_staticChanges);
        _staticChanges.Clear();
        _staticReset = false;
        return reset;
    }

    /// <summary>Adds where every placement that moves stands this frame.</summary>
    /// <param name="casters">Receives a sphere for each.</param>
    internal void CollectMovableCasters(List<WorldCasterBounds> casters)
    {
        for (int i = 0; i < _frame.Length; i++)
        {
            ref WorldFrameEntry entry = ref _frame[i];
            if (!entry.Skip && entry.Moving && entry.Model is { } model)
            {
                casters.Add(BoundsOf(entry.Position, model));
            }
        }
    }

    // A placement that was drawn last frame and is not now.
    private void NoteGone(bool wasSkipped, ResolvedWorldModel? wasModel, Vector3d wasPosition)
    {
        if (!wasSkipped && wasModel is not null)
        {
            NoteStaticChange(wasPosition, wasModel);
        }
    }

    private void NoteStaticChange(Vector3d position, ResolvedWorldModel? model)
    {
        if (_staticReset || model is null)
        {
            return;
        }

        if (_staticChanges.Count >= MaxStaticChanges)
        {
            _staticChanges.Clear();
            _staticReset = true;
            return;
        }

        _staticChanges.Add(BoundsOf(position, model));
    }

    // Round the placement's origin, wide enough for its model however it is turned.
    private static WorldCasterBounds BoundsOf(Vector3d position, ResolvedWorldModel model)
    {
        BoundingSphere bounds = model.Geometry.Bounds;
        return new WorldCasterBounds(position, bounds.Centre.Length + bounds.Radius);
    }

    /// <summary>Resolves everything camera-independent about the frame, once, for every pass to share.</summary>
    private void PrepareFrame(WorldScene scene)
    {
        _frameNumber++;

        if (!ReferenceEquals(_frameScene, scene))
        {
            _frameScene = scene;
            _frame = new WorldFrameEntry[scene.Placements.Count];
            _movers.Clear();
            _staticChanges.Clear();
            _staticReset = true;
            _drawOrder = WorldRenderer.BuildDrawOrder(scene);

            // A new scene means new placement objects; entries keyed by the old ones would never be
            // asked for again and would keep the old scene's geometry alive — and their occlusion
            // queries are device objects, so they are handed back rather than dropped.
            _subsets.ForgetResolved();
            _transforms.Shattered.Clear();
            _occlusionSettle.Forget();
        }

        for (int i = 0; i < _frame.Length; i++)
        {
            ref WorldFrameEntry entry = ref _frame[i];
            WorldPlacement authored = scene.Placements[i];

            // What the placement was last frame, for the kept shadows: see _staticChanges.
            bool wasSkipped = entry.Skip;
            bool wasDestroyed = entry.WasDestroyed;
            int wasVariant = entry.Variant;
            ResolvedWorldModel? wasModel = entry.Model;
            Vector3d wasPosition = entry.Position;

            if (_transforms.Objects?.IsTaken(authored) == true
                || !ConditionalContainerGate.IsTaskActive(authored.Owner)
                || _transforms.Presence?.IsInWorld(authored) == false)
            {
                NoteGone(wasSkipped, wasModel, wasPosition);
                entry.Skip = true;
                continue;
            }

            // Blown up with nothing authored to stand in its place: there is no rubble model to
            // swap to, so the placement leaves the frame entirely rather than drawing intact.
            // A placement a bomb shattered has left as its pieces, whatever it would otherwise have been drawn as.
            if (((_transforms.IsBroken(authored) || _transforms.Objects?.IsLightGone(authored) == true)
                && authored.DestroyedReplacement is null)
                || (_transforms.Shattered.Count > 0 && _transforms.Shattered.Contains(authored)))
            {
                NoteGone(wasSkipped, wasModel, wasPosition);
                entry.Skip = true;
                continue;
            }

            entry.Skip = false;
            entry.EyeCulled = false;
            entry.IsRotor = authored.IsRotor;

            bool destroyed = _transforms.UsesDestroyedReplacement(authored);
            int variant = _transforms.VariantOf(authored);
            ResolvedWorldModel? model = _subsets.SelectModel(authored, ref entry, destroyed, variant);

            entry.Variant = variant;
            entry.WasDestroyed = destroyed;
            entry.Model = model;
            if (model is null)
            {
                NoteGone(wasSkipped, wasModel, wasPosition);
                entry.Skip = true;
                continue;
            }

            // The live transforms -- a camera's swivel, a fan's spin, a swung door, a turned gun stand --
            // never change the model, so they are applied over the displayed placement cached above.
            entry.Displayed = _transforms.ApplyLiveTransforms(authored, entry.Displayed!);

            // What nothing claims is drawn where it stands, as the original draws an object from its own matrix (0x49DF60):
            // asked once, and again only when a claim is made or let go.
            int epoch = PlacementClaims.Epoch;
            if (entry.ClaimEpoch != epoch || !ReferenceEquals(entry.ClaimFor, entry.Displayed))
            {
                entry.Unclaimed = !_transforms.IsClaimed(entry.Displayed!);
                entry.ClaimEpoch = epoch;
                entry.ClaimFor = entry.Displayed;
            }

            if (entry.Unclaimed)
            {
                entry.Position = entry.Displayed!.Position;
                entry.Orientation = entry.Displayed.Orientation;
                entry.Rotor = default;
                entry.HasRotor = false;
            }
            else
            {
                _transforms.ResolveDrawTransform(
                    entry.Displayed!,
                    out entry.Position,
                    out entry.Orientation,
                    out entry.Rotor,
                    out entry.HasRotor);
            }

            // The title's own culling decides what the EYE draws, not what stands in the world: a room the portal walk did not
            // reach is still a wall between the sun and the room the eye is in, so the light passes and the traced world keep it.
            // Dropping it from every pass let the sun through each wall the walk missed, and the room's lighting flipped with
            // every turn of the head that changed whether the walk saw out. Asked where the placement is drawn, so a lift's car
            // ridden away from where the level put it is weighed where it now stands.
            if (Visibility is { } visibility)
            {
                entry.EyeCulled = !visibility.IsVisible(authored, entry.Position);
                entry.Clip = entry.EyeCulled ? null : visibility.ClipOf(authored, entry.Position);
            }
            else
            {
                entry.EyeCulled = false;
                entry.Clip = null;
            }

            // Anything drawn away from where it was placed is a mover -- a vehicle, a door, a fan, a camera
            // head -- and stays one, so the frame it comes to rest on is paired against the one before.
            if ((entry.HasRotor || entry.Position != authored.Position || entry.Orientation != authored.Orientation)
                && _movers.Add(authored))
            {
                // It leaves the static set where the level placed it.
                NoteStaticChange(authored.Position, model);
            }

            entry.Moving = _movers.Contains(authored);

            if (wasSkipped)
            {
                NoteStaticChange(entry.Position, model);
            }
            else if (wasDestroyed != destroyed || wasVariant != variant)
            {
                NoteStaticChange(wasPosition, wasModel);
                NoteStaticChange(entry.Position, model);
            }
        }

        _movingOrder.Clear();
        foreach (int index in _drawOrder)
        {
            if (!_frame[index].Skip && _frame[index].Moving)
            {
                _movingOrder.Add(index);
            }
        }

        _framePrepared = true;
    }

    /// <summary>Gets this frame's movers, in draw order.</summary>
    internal ReadOnlySpan<int> MovingOrder => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_movingOrder);
}
