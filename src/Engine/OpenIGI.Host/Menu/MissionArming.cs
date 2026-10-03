using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Igi1;
using System.Diagnostics;

namespace OpenIGI.Host.Menu;

/// <summary>
/// Makes the menu's backdrop ready to be played, a couple of milliseconds at a time, while the player is
/// still looking at the menu.
/// </summary>
/// <remarks>
/// <para>
/// A mission's level is built on a worker thread long before Play is pressed
/// (<see cref="MissionPreviewLoader"/>), but the graphics work cannot leave the render thread: a mesh
/// upload and a texture upload belong to the context that made them. That work is not large in total —
/// it is large all at once, and a frame is what pays for it.
/// </para>
/// <para>
/// So it is done in pieces, under a time budget, on frames where the only thing happening is a menu.
/// Every piece is one placement, one soldier, one weapon model: small enough that a single piece never
/// overruns the budget on its own, which is the property the budget depends on, because a piece cannot be
/// interrupted once it has started.
/// </para>
/// <para>
/// The largest single item by far is <em>world residency</em>. The backdrop's camera orbits one corner of
/// the level, and the renderer resolves a model the first time it draws it — so most of the level is
/// still unresolved when Play moves the camera to the player's spawn, and that one frame uploads the
/// rest. Measured on mission 1 at about 260 ms. Walking every placement here turns that into nothing.
/// </para>
/// </remarks>
public sealed class MissionArming
{
    private readonly WorldScene _scene;
    private readonly IWorldPresenter _world;
    private readonly IOpenIgiLogger _logger;
    private readonly Stopwatch _spent = new();

    private int _placement;
    private double _totalMilliseconds;
    private bool _reported;

    /// <summary>Initializes a new instance of the <see cref="MissionArming"/> class over a built backdrop.</summary>
    /// <param name="missionId">The mission being armed, for the log.</param>
    /// <param name="scene">The backdrop's scene, whose placements are made resident.</param>
    /// <param name="world">The renderer they are made resident in.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public MissionArming(int missionId, WorldScene scene, IWorldPresenter world, IOpenIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(logger);

        MissionId = missionId;
        _scene = scene;
        _world = world;
        _logger = logger;
    }

    /// <summary>Gets the mission this is arming.</summary>
    public int MissionId { get; }

    /// <summary>Gets a value indicating whether there is nothing left to do.</summary>
    public bool IsArmed => _placement >= _scene.Placements.Count;

    /// <summary>Gets how far through the work this is, 0 to 1.</summary>
    public float Progress =>
        _scene.Placements.Count == 0 ? 1f : Math.Clamp((float)_placement / _scene.Placements.Count, 0f, 1f);

    /// <summary>Gets how many placements are still to be made resident.</summary>
    public int Remaining => Math.Max(0, _scene.Placements.Count - _placement);

    /// <summary>Does as much of the remaining work as fits in a budget. Render thread only.</summary>
    /// <param name="budgetMilliseconds">How long this frame is willing to spend.</param>
    /// <remarks>
    /// The budget is checked between pieces, never inside one, so a frame can overrun by at most the cost
    /// of the piece that was running when the budget expired. That is the reason the pieces are small.
    /// </remarks>
    public void Step(double budgetMilliseconds)
    {
        if (IsArmed)
        {
            Report();
            return;
        }

        long startedAt = Stopwatch.GetTimestamp();
        double spent;
        do
        {
            if (!_world.MakeResident(_scene, _placement))
            {
                break;
            }

            _placement++;
            spent = (Stopwatch.GetTimestamp() - startedAt) * 1000.0 / Stopwatch.Frequency;
        }
        while (!IsArmed && spent < budgetMilliseconds);

        _totalMilliseconds += (Stopwatch.GetTimestamp() - startedAt) * 1000.0 / Stopwatch.Frequency;

        if (IsArmed)
        {
            Report();
        }
    }

    private void Report()
    {
        if (_reported)
        {
            return;
        }

        _reported = true;
        _logger.Information(
            "Preview",
            FormattableString.Invariant(
                $"Mission {MissionId}'s backdrop is armed: {_scene.Placements.Count} placement(s) made resident over {_totalMilliseconds:F0} ms of idle menu frames."));
    }
}
