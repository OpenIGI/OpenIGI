using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>
/// An automatic gun's holder, I.G.I. 2's <c>AIStationaryGunHolder</c> (aistationarygunholder.c): a model that carries an
/// <see cref="Igi2AiStationaryGun"/> on its attach point and watches through a cone from the gun's muzzle while its on expression holds.
/// </summary>
/// <remarks>
/// <c>AIStationaryGunHolder_RunHandler</c>: its team and on expressions, where it has them, set its side and whether it watches. On, its
/// detection runs from the barrel's third magic vertex, the muzzle, along the barrel, through its cone alone; turned off, its detection
/// forgets everything. Then the gun runs.
/// </remarks>
public sealed partial class Igi2AiStationaryGunHolderTask : Igi2Task, IIgi2PlacedTask, IIgi2PartMover
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "AIStationaryGunHolder";

    private Igi2LevelExpression? _onExpression;
    private Igi2LevelExpression? _teamExpression;
    private byte _isOn;
    private int _team = 1;
    private Igi2AiDetection? _detection;
    private Igi2DetectionWorld? _detectionWorld;

    /// <summary>Gets or sets where it stands.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets its turn, as three Euler angles.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets its holder's model.</summary>
    [ScriptParam(ScriptParamType.String32, "Holder model")]
    public string HolderModel { get; set; } = string.Empty;

    /// <summary>Gets or sets its cone's half width up and down, in degrees.</summary>
    [ScriptParam(ScriptParamType.Degrees, "Viewcone Alpha")]
    public float ViewconeAlpha { get; set; } = 30f;

    /// <summary>Gets or sets its cone's half width across, in degrees.</summary>
    [ScriptParam(ScriptParamType.Degrees, "Viewcone Gamma")]
    public float ViewconeGamma { get; set; } = 30f;

    /// <summary>Gets or sets how far its cone reaches, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Viewcone Length")]
    public float ViewconeLength { get; set; } = 10f;

    /// <summary>Gets or sets the expression that says whether it watches.</summary>
    [ScriptParam(ScriptParamType.VarString, "On expression")]
    public string OnExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that says its side.</summary>
    [ScriptParam(ScriptParamType.VarString, "Team expression")]
    public string TeamExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets its barrel's model.</summary>
    [ScriptParam(ScriptParamType.String32, "Barrel model")]
    public string BarrelModel { get; set; } = "700_02_1";

    /// <summary>Gets or sets the sound it turns with.</summary>
    [ScriptParam(ScriptParamType.String32, "Rotationsound")]
    public string RotationSound { get; set; } = string.Empty;

    /// <summary>Gets or sets its lowest tilt, in degrees.</summary>
    [ScriptParam(ScriptParamType.Real32, "Min Alpha (deg)")]
    public float MinAlpha { get; set; } = -90f;

    /// <summary>Gets or sets its highest tilt, in degrees.</summary>
    [ScriptParam(ScriptParamType.Real32, "Max Alpha (deg)")]
    public float MaxAlpha { get; set; } = -90f;

    /// <summary>Gets or sets the most it turns either way, in degrees; past a full turn it turns freely.</summary>
    [ScriptParam(ScriptParamType.Real32, "Max Gamma (deg)")]
    public float MaxGamma { get; set; } = 300f;

    /// <summary>Gets or sets its ammunition, which its weapon never runs out of.</summary>
    [ScriptParam(ScriptParamType.Int32, "Ammo")]
    public int Ammo { get; set; } = 10000;

    /// <summary>Gets or sets how fast it tilts, in degrees a second.</summary>
    [ScriptParam(ScriptParamType.Real32, "Alpha speed (deg/sec)")]
    public float AlphaSpeed { get; set; } = 300f;

    /// <summary>Gets or sets how fast it turns, in degrees a second.</summary>
    [ScriptParam(ScriptParamType.Real32, "Gamma speed (deg/sec)")]
    public float GammaSpeed { get; set; } = 300f;

    /// <summary>Gets or sets how fast its barrel spins while it fires, in degrees a second.</summary>
    [ScriptParam(ScriptParamType.Real32, "Beta speed (deg/sec)")]
    public float BetaSpeed { get; set; } = 1000f;

    /// <summary>Gets or sets how likely its rounds are to strike, nought to one.</summary>
    [ScriptParam(ScriptParamType.Real32, "Accuracy (0..1)")]
    public float Accuracy { get; set; } = 0.3f;

    /// <summary>Gets or sets its weapon, a <c>WEAPON_ID_*</c> constant's name.</summary>
    [ScriptParam(ScriptParamType.String32, "WeaponID")]
    public string WeaponId { get; set; } = "WEAPON_ID_M2HB";

    /// <summary>Gets a value indicating whether it watches, <c>isOn</c>.</summary>
    public bool IsOn => _isOn != 0;

    /// <summary>Gets its side, as its team expression last said.</summary>
    public int Team => _team;

    /// <summary>Gets its detection, once prepared.</summary>
    public Igi2AiDetection? Detection => _detection;

    /// <summary>Gets the view its detection looks through.</summary>
    public Igi2DetectionView View { get; } = new();

    /// <summary>Gets the gun it carries, once prepared; <see langword="null"/> when its model has no attach point for one.</summary>
    public Igi2AiStationaryGun? Gun { get; private set; }

    /// <summary>Gets the parameters it gives its gun, <c>AIStationaryGunHolder_GetGunParametersHandler</c>.</summary>
    public Igi2AiGunParameters GunParameters => new(
        BarrelModel, RotationSound, MinAlpha, MaxAlpha, MaxGamma, AlphaSpeed, GammaSpeed, BetaSpeed, Accuracy, WeaponId);

    /// <summary>
    /// Arms it and readies its detection, as the level loads: <c>AIStationaryGunHolder_UpdateInternalDataHandler</c>, and its gun's
    /// creation on its model's attach point.
    /// </summary>
    /// <param name="weapons">The session's weapons.</param>
    /// <param name="models">The level's models.</param>
    /// <param name="magic">The magic object types its attach point is looked up in.</param>
    /// <param name="detection">The detectables it watches, or <see langword="null"/> for none.</param>
    /// <param name="sounds">The level's sounds, or <see langword="null"/> for none.</param>
    /// <param name="random">The stream its gun's rounds draw from, or <see langword="null"/> for a fresh one.</param>
    public void Prepare(
        Igi2HumanWeapons weapons,
        Func<string, MefModel?> models,
        Igi2MagicObjTypes? magic,
        Igi2DetectionWorld? detection,
        Igi2Sounds? sounds = null,
        IgiRandom? random = null)
    {
        ArgumentNullException.ThrowIfNull(weapons);
        ArgumentNullException.ThrowIfNull(models);
        _detectionWorld = detection;
        _detection = detection is null ? null : NewDetection(detection);
        View.ConeAlpha = ViewconeAlpha * Igi2AiStationaryGun.DegreesToRadians;
        View.ConeGamma = ViewconeGamma * Igi2AiStationaryGun.DegreesToRadians;
        View.ConeLength = ViewconeLength * 4096.0;
        View.IsSecondaryViewUsed = false;

        Gun = null;
        if (magic is not null && models(HolderModel) is { } model)
        {
            Igi2Pose holder = Igi2Pose.FromAngles(Position, Orientation);
            foreach (MefAttachment attachment in model.Attachments)
            {
                if (magic.Find(attachment.Name) is { TaskType: Igi2AiStationaryGun.TypeName } type)
                {
                    Gun = new Igi2AiStationaryGun(GunParameters, type.Model, weapons, models) { Sounds = sounds, Random = random ?? new(), Holder = this };
                    Gun.Mount(holder.Attach(attachment));
                    break;
                }
            }
        }
    }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        bool wasOn = _isOn != 0;
        _teamExpression ??= new Igi2LevelExpression(TeamExpression, Name, level.Natives);
        if (!_teamExpression.IsEmpty)
        {
            _team = (int)Igi2LevelExpression.Truncate(level.Evaluate(_teamExpression));
        }

        _onExpression ??= new Igi2LevelExpression(OnExpression, Name, level.Natives);
        if (!_onExpression.IsEmpty)
        {
            _isOn = (byte)Igi2LevelExpression.Truncate(level.Evaluate(_onExpression));
        }

        if (_isOn != 0)
        {
            if (_detection is { } detection && Gun is { } gun)
            {
                View.Position = gun.Muzzle;
                View.Orientation = [.. gun.BarrelFrame];
                detection.Run(View, null, _team);
            }
        }
        else if (wasOn && _detectionWorld is { } world)
        {
            _detection?.Clear();
            _detection = NewDetection(world);
        }

        level.RunChildren(this);
        Gun?.Run(_detection, level.LevelFlow?.Tick ?? Gun.Ticks + 1);
    }

    /// <inheritdoc/>
    /// <remarks>The gun's head, its holder's attach point, turned with the gun; its barrel, tilted and turned on the head.</remarks>
    public bool TryMovePart(WorldPlacement placement, out Vector3d positionMetres, out Matrix3f orientation)
    {
        if (Gun is { } gun)
        {
            return gun.TryMovePart(placement, out positionMetres, out orientation);
        }

        ArgumentNullException.ThrowIfNull(placement);
        (positionMetres, orientation) = (default, placement.Orientation);
        return false;
    }

    // AIDetection_InitDetection(.., 1, 4, Human_bAIDetectionQTaskMask, 4, 8): its own side one, enemies alive, humans, a cone every
    // fourth tick and a look every eighth.
    private static Igi2AiDetection NewDetection(Igi2DetectionWorld world) =>
        new(world, 1, Igi2AiDetection.LiveEnemies, Igi2DetectableKind.Human, 4, 8);
}
