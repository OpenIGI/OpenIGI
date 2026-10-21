using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Lighting;
using OpenIGI.Rendering;
using System.Runtime.InteropServices;

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

    /// <summary>Gets or sets the live generic pickups whose consumed models no longer draw.</summary>
    internal GenericPickupRegistry? GenericPickups { get; set; }

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

    /// <summary>Resolves everything camera-independent about the frame, once, for every pass to share.</summary>
    private void PrepareFrame(WorldScene scene)
    {
        _frameNumber++;

        if (!ReferenceEquals(_frameScene, scene))
        {
            _frameScene = scene;
            _frame = new WorldFrameEntry[scene.Placements.Count];
            _movers.Clear();
            _drawOrder = WorldRenderer.BuildDrawOrder(scene);

            // A new scene means new placement objects; entries keyed by the old ones would never be
            // asked for again and would keep the old scene's geometry alive — and their occlusion
            // queries are device objects, so they are handed back rather than dropped.
            _subsets.ForgetResolved();
            _occlusionSettle.Forget();
        }

        for (int i = 0; i < _frame.Length; i++)
        {
            ref WorldFrameEntry entry = ref _frame[i];
            WorldPlacement authored = scene.Placements[i];

            if (GenericPickups?.IsCollected(authored.Owner) == true
                || !ConditionalContainerGate.IsTaskActive(authored.Owner))
            {
                entry.Skip = true;
                continue;
            }

            // Blown up with nothing authored to stand in its place: there is no rubble model to
            // swap to, so the placement leaves the frame entirely rather than drawing intact.
            if ((_transforms.Destructibles?.IsDestroyed(authored) == true || _transforms.Destruction?.IsDestroyed(authored) == true)
                && authored.DestroyedReplacement is null)
            {
                entry.Skip = true;
                continue;
            }

            entry.Skip = false;
            entry.IsRotor = authored.IsRotor;

            bool destroyed = _transforms.UsesDestroyedReplacement(authored);
            int variant = _transforms.VariantOf(authored);
            ResolvedWorldModel? model = _subsets.SelectModel(authored, ref entry, destroyed, variant);

            entry.Variant = variant;
            entry.WasDestroyed = destroyed;
            entry.Model = model;
            if (model is null)
            {
                entry.Skip = true;
                continue;
            }

            // The live transforms -- a camera's swivel, a fan's spin, a swung door, a turned gun stand --
            // never change the model, so they are applied over the displayed placement cached above.
            entry.Displayed = _transforms.ApplyLiveTransforms(authored, entry.Displayed!);

            _transforms.ResolveDrawTransform(
                entry.Displayed!,
                out entry.Position,
                out entry.Orientation,
                out entry.Rotor,
                out entry.HasRotor);

            // Anything drawn away from where it was placed is a mover -- a vehicle, a door, a fan, a camera
            // head -- and stays one, so the frame it comes to rest on is paired against the one before.
            if (entry.HasRotor || entry.Position != authored.Position || entry.Orientation != authored.Orientation)
            {
                _movers.Add(authored);
            }

            entry.Moving = _movers.Contains(authored);
        }

        _framePrepared = true;
    }
}
