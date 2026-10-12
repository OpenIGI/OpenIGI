using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Pickups;

/// <summary>
/// A thing lying in the level for the player to collect by walking into it, I.G.I. 2's <c>GenericPickup</c> (genericpickup.c): a
/// document, a key card, a disk. The level's scripts follow it through <c>isPickedUp</c>.
/// </summary>
/// <remarks>
/// <para>
/// It is stood on and struck by nothing (<c>GenericPickup_ObjectCollisionEvaluateHandler</c>, <c>GetNextZEvaluateHandler</c>).
/// <c>GenericPickup_PickupHandler</c>: offered the player's view position, it is picked up when the view is within its reach
/// across the ground, and no more than a metre and a half plus its reach below it nor a metre plus its reach above; its reach is its
/// model's radius at its largest scale, at least a metre.
/// </para>
/// <para>
/// <c>GenericPickup_RunHandler</c>: picked up while still in the world, it sounds <c>pickup</c> and leaves the world, drawn no more.
/// </para>
/// </remarks>
public sealed partial class Igi2GenericPickupTask : Igi2Task, IIgi2PlacedTask, IIgi2Destructible
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "GenericPickup";

    /// <summary>The sound it makes as it is collected.</summary>
    public const string PickupSound = "pickup";

    /// <summary>The least reach it has, one metre.</summary>
    public const double MinimumReach = 4096.0;

    private byte _isPickedUp;

    /// <summary>Gets or sets where it lies.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets its turn, as three Euler angles.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets its model.</summary>
    [ScriptParam(ScriptParamType.String16, "Model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether the player has walked into it, <c>isPickedUp</c>.</summary>
    public bool IsPickedUp => _isPickedUp != 0;

    /// <summary>Gets a value indicating whether it is still in the world.</summary>
    public bool IsInWorld { get; private set; } = true;

    /// <inheritdoc/>
    /// <remarks>Collected, it leaves the world, and having no destroyed model it is drawn no more.</remarks>
    public bool IsDestroyed => !IsInWorld;

    /// <summary>Gets or sets the level's models, which its reach is taken from.</summary>
    public Func<string, MefModel?>? Models { get; set; }

    /// <summary>Gets or sets how it sounds, as the game's simple sounds play.</summary>
    public Action<string>? PlaySound { get; set; }

    /// <summary>Gets how far it reaches: its model's radius, at least a metre.</summary>
    public double Reach => Math.Max(Models?.Invoke(Model)?.NativeMarkerBound ?? 0f, MinimumReach);

    /// <summary>Offers it to the player, <c>GenericPickup_PickupHandler</c>.</summary>
    /// <param name="viewPosition">Where the player's view is.</param>
    public void Offer(Vector3d viewPosition)
    {
        double reach = Reach;
        double dx = Position.X - viewPosition.X;
        double dy = Position.Y - viewPosition.Y;
        double height = Position.Z - viewPosition.Z;
        if (Math.Sqrt((dy * dy) + (dx * dx)) < reach && -6144.0 - reach < height && height < reach + 4096.0)
        {
            _isPickedUp = 1;
        }
    }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (_isPickedUp != 0 && IsInWorld)
        {
            PlaySound?.Invoke(PickupSound);
            IsInWorld = false;
        }
    }

    /// <inheritdoc/>
    public bool TryGetValue(string name, out object? value)
    {
        value = name switch
        {
            "Position" => Position,
            "Orientation" => Orientation,
            "Model" => Model,
            _ => null,
        };
        return value is not null;
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "isPickedUp", () => _isPickedUp, value => _isPickedUp = (byte)value, sizeof(byte));
    }
}
