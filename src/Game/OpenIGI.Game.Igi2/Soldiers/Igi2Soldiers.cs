using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.ViewModel;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <summary>A level's soldiers: a body for every <c>HumanSoldier</c> it places.</summary>
public sealed class Igi2Soldiers
{
    private readonly List<Igi2HumanSoldier> _bodies;
    private readonly List<Igi2SoldierLook> _looks;
    private readonly Igi2BodyFrame _frame = new();
    private TitleWorldModel[] _drawn = [];
    private long _drawnTick = -1;

    private Igi2Soldiers(List<Igi2HumanSoldier> bodies, List<Igi2SoldierLook> looks)
    {
        _bodies = bodies;
        _looks = looks;
    }

    /// <summary>Gets the bodies, in the order the level makes them.</summary>
    public IReadOnlyList<Igi2HumanSoldier> Bodies => _bodies;

    /// <summary>The models the soldiers draw as a tick left them: each body posed, and the weapon in its hand.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The models, built once a tick.</returns>
    public IReadOnlyList<TitleWorldModel> Models(long tick)
    {
        if (_drawnTick != tick)
        {
            List<TitleWorldModel> models = [];
            for (int i = 0; i < _bodies.Count; i++)
            {
                Igi2HumanSoldier body = _bodies[i];
                if (body.Animation is Igi2HumanAnimation animation)
                {
                    models.AddRange(_frame.Build(body, animation, _looks[i].Model, _looks[i].BoneNames, body.Weapon.Inventory?.Current?.WeaponType.FixedModel, tick));
                }
            }

            _drawn = [.. models];
            _drawnTick = tick;
        }

        return _drawn;
    }

    /// <summary>
    /// Makes a body for each of a level's soldiers, as <c>Human_CreateHandler</c> (<c>igi2 0x4FDC90</c>),
    /// <c>HumanSoldier_CreateHandler</c> (<c>0x4E9E10</c>) and <c>Human_UpdateInternalDataHandler</c> (<c>0x4FF2F0</c>) make one.
    /// </summary>
    /// <param name="levelRoot">The level.</param>
    /// <param name="body">Builds a body's surroundings, weapon and animation for a soldier's task and rig.</param>
    /// <param name="models">Resolves a placed model name to its mesh.</param>
    /// <param name="random">The session's run stream, which every soldier draws from.</param>
    /// <returns>The soldiers.</returns>
    /// <remarks>
    /// Each stands where the level puts it, turned by its <c>Gamma</c> and upright; its weapon script arms it. Its task runs it
    /// from then on (<see cref="Igi2HumanSoldierTask.Body"/>).
    /// </remarks>
    public static Igi2Soldiers Spawn(
        GameTask levelRoot,
        Func<Igi2HumanSoldierTask, Igi2BoneHierarchy?, (IIgi2HumanSurroundings Surroundings, IIgi2PlayerWeaponControl Weapon, IIgi2HumanAnimation Animation)> body,
        Func<string, Formats.World.MefModel?> models,
        IgiRandom random)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(random);
        List<Igi2HumanSoldier> bodies = [];
        List<Igi2SoldierLook> looks = [];
        foreach (Igi2HumanSoldierTask task in Igi2TaskTree.Descendants(levelRoot).OfType<Igi2HumanSoldierTask>())
        {
            Formats.World.MefModel? model = models(task.Model);
            Igi2BoneHierarchy? rig = model?.BoneHierarchy;
            (IIgi2HumanSurroundings surroundings, IIgi2PlayerWeaponControl weapon, IIgi2HumanAnimation animation) = body(task, rig);
            Igi2HumanSoldier soldier = new(weapon, animation, surroundings)
            {
                Random = random,
                Position = task.Position,
                Gamma = task.Gamma,
                IdealGamma = task.Gamma,
                Team = task.Team,
            };
            soldier.Create();
            soldier.BoneHierarchy = task.BoneHierarchy;
            soldier.StandAnimation = task.StandAnimation;
            float[] facing = Igi2Pose.Rotation(0f, 0f, task.Gamma);
            soldier.Orientation = new Matrix3f(
                facing[0], facing[1], facing[2],
                facing[3], facing[4], facing[5],
                facing[6], facing[7], facing[8]);
            if (model is not null)
            {
                // BoneDynCubeObj_SetLODVirModel (igi2 0x567BE0): the root's bind translation and the mesh's radius.
                soldier.ModelRadius = model.NativeMarkerBound;
                if (rig is { BoneCount: > 0 })
                {
                    soldier.RootTranslation = rig.Translations[0];
                }
            }

            task.Body = soldier;
            bodies.Add(soldier);
            looks.Add(new Igi2SoldierLook(task.Model, model?.BoneNames ?? []));
        }

        return new Igi2Soldiers(bodies, looks);
    }

    /// <summary>What a soldier is drawn as: its model, and its bones' names.</summary>
    private readonly record struct Igi2SoldierLook(string Model, IReadOnlyList<string> BoneNames);
}

/// <summary>
/// A soldier's mind until the AI's is ported: it starts at once and sends the body an update with nothing on any channel each
/// tick, so the soldier stands where it was put.
/// </summary>
public sealed class Igi2StandingSoldierMind : IIgi2SoldierMind
{
    private readonly float[] _frame = new float[Igi2HumanChannels.Count];

    /// <inheritdoc/>
    public bool HasStarted { get; private set; }

    /// <inheritdoc/>
    public void Run(Igi2HumanSoldier soldier)
    {
        ArgumentNullException.ThrowIfNull(soldier);
        soldier.Update(_frame);
        HasStarted = true;
    }
}
