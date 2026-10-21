using System.Globalization;
using System.Text;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>
/// The level's panes of glass, <c>GlassContainer</c> (glasscontainer.c): each by its key, run each tick so a broken pane leaves the
/// world (<c>GlassContainer_RunHandler</c>), written to a save game one line a pane, and put back as a save game says.
/// </summary>
public sealed class Igi2GlassContainer
{
    /// <summary>The call a pane is written as, <c>%s("%s", %d);</c>.</summary>
    public const string Call = "GlassContainer";

    private readonly List<Igi2Glass> _panes = [];
    private readonly Dictionary<string, Igi2Glass> _byKey = new(StringComparer.Ordinal);
    private readonly List<Igi2Glass> _gone = [];

    /// <summary>Gets the panes, in the order they were made.</summary>
    public IReadOnlyList<Igi2Glass> Panes => _panes;

    /// <summary>
    /// Makes a pane of every solid the collision world built at a glass attach point, in the order built, each drawing its threshold
    /// from the run stream and joining the explosions' list (<c>Glass_CreateHandler</c>, <c>GlassContainer_RegisterGlass</c>).
    /// </summary>
    /// <param name="world">The collision world.</param>
    /// <param name="models">Resolves a pane's model.</param>
    /// <param name="random">The level's run stream.</param>
    /// <param name="explosions">The explosions, or <see langword="null"/>.</param>
    /// <returns>The container.</returns>
    public static Igi2GlassContainer Build(
        Igi2CollisionWorld world, Func<string, Formats.World.MefModel?> models, IgiRandom random, Igi2ExplosionWorld? explosions)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(models);
        Igi2GlassContainer container = new();
        foreach (Igi2RigidObject solid in world.Objects)
        {
            if (solid.Owner is Igi2CollisionOwner { GlassKey: { } key } owner)
            {
                Igi2Glass glass = new(solid, key, models(owner.Model), random) { Explosions = explosions };
                explosions?.AddTarget(glass);
                container.Add(glass);
            }
        }

        return container;
    }

    /// <summary>Adds a pane; a second pane of the same key replaces the first in the key's lookup, as the hash table's insert does.</summary>
    /// <param name="glass">The pane.</param>
    public void Add(Igi2Glass glass)
    {
        ArgumentNullException.ThrowIfNull(glass);
        _panes.Add(glass);
        _byKey[glass.Key] = glass;
    }

    /// <summary>Finds a pane by its key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The pane, or <see langword="null"/>.</returns>
    public Igi2Glass? Find(string key) => _byKey.GetValueOrDefault(key);

    /// <summary>The glass tick of every pane, in order.</summary>
    public void Run()
    {
        foreach (Igi2Glass glass in _panes)
        {
            glass.Run();
        }

        NoteGone();
    }

    /// <summary>
    /// The lines a save game keeps of the panes, <c>GlassContainer_SerializeHandler</c>: each pane's key and whether it has left the
    /// world (<c>Glass_IsBroken</c>).
    /// </summary>
    /// <returns>The lines.</returns>
    public string SaveLines()
    {
        StringBuilder text = new();
        foreach (Igi2Glass glass in _panes)
        {
            text.Append(CultureInfo.InvariantCulture, $"{Call}(\"{glass.Key}\", {(glass.IsRemoved ? 1 : 0)});\r\n");
        }

        return text.ToString();
    }

    /// <summary>
    /// Puts the panes back as a save game kept them, <c>GlassContainer_DeSerializeDoneHandler</c>: a pane the save names is broken or
    /// whole as it says, and one it does not name is whole.
    /// </summary>
    /// <param name="saved">The save's lines, each pane's key and value.</param>
    public void Restore(IReadOnlyDictionary<string, int> saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        foreach (Igi2Glass glass in _panes)
        {
            glass.SetBroken(saved.TryGetValue(glass.Key, out int value) && value != 0);
        }

        NoteGone();
    }

    /// <summary>Gets a value indicating whether a pane drawn at a place, by the task that placed it, has left the world.</summary>
    /// <param name="levelId">The placing task's level id.</param>
    /// <param name="position">Where the pane is drawn, in engine units.</param>
    /// <returns><see langword="true"/> for a pane that is gone.</returns>
    public bool IsGone(int levelId, Vector3d position)
    {
        foreach (Igi2Glass glass in _gone)
        {
            if (glass.Solid.Owner is Igi2CollisionOwner owner && owner.LevelId == levelId
                && Math.Abs(glass.Solid.Position.X - position.X) < 1.0
                && Math.Abs(glass.Solid.Position.Y - position.Y) < 1.0
                && Math.Abs(glass.Solid.Position.Z - position.Z) < 1.0)
            {
                return true;
            }
        }

        return false;
    }

    // The panes that have left the world, which the drawing asks after every frame.
    private void NoteGone()
    {
        _gone.Clear();
        _gone.AddRange(_panes.Where(static g => g.IsRemoved));
    }
}
