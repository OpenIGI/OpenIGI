using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>What an object that explodes is authored with, the parameters of <c>ExplodeObject</c> its kind inherits.</summary>
public interface IIgi2ExplosiveParameters
{
    /// <summary>Gets where it stands.</summary>
    Vector3d Position { get; }

    /// <summary>Gets which way it faces, as level data's three angles.</summary>
    Vector3f Orientation { get; }

    /// <summary>Gets the model it wears whole.</summary>
    string Model { get; }

    /// <summary>Gets the model it wears once exploded.</summary>
    string DestroyedModel { get; }

    /// <summary>Gets the damage it takes to explode; less than nothing never does.</summary>
    float DamageScale { get; }

    /// <summary>Gets how far its blast does full damage, in metres.</summary>
    float ExplosionRadius { get; }

    /// <summary>Gets how far past that its blast's damage falls away, in metres.</summary>
    float ExplosionFalloffRadius { get; }

    /// <summary>Gets the damage at its blast's heart.</summary>
    float ExplosionDamageScale { get; }

    /// <summary>Gets the seconds a blast that destroyed it takes to set it off.</summary>
    float ExplosionDelay { get; }

    /// <summary>Gets how many fragments its blast throws.</summary>
    int ExplosionFragments { get; }

    /// <summary>Gets how many fireballs its blast makes; none, and it explodes without a blast.</summary>
    int ExplosionFireballs { get; }

    /// <summary>Gets the script that explodes it.</summary>
    string ExplosionScript { get; }

    /// <summary>Gets the sound its blast makes.</summary>
    string ExplosionSound { get; }
}

/// <summary>A task of a kind that is an <c>ExplodeObject</c>: it blows up with its <see cref="Explosive"/>.</summary>
public interface IIgi2ExplodingTask : IIgi2Destructible, IIgi2ExplosionTarget
{
    /// <summary>Gets what it does when it explodes.</summary>
    Igi2Explosive Explosive { get; }
}

/// <summary>
/// The exploding half of <c>ExplodeObject</c>, which every kind of it shares: its damage, its script, its countdown, and the
/// swap to its destroyed model with the blast that goes with it.
/// </summary>
/// <remarks>
/// <para>
/// Each tick, while whole (<c>ExplodeObject_RunHandler</c>, <c>igi2 0x49C850</c>), its script says whether it explodes; a hit
/// that did it enough damage set a countdown, which is run down, and at nought, or when the script says, it explodes
/// (<c>ExplodeObject_Explode</c>, <c>0x49C370</c>): it wears its destroyed model (<c>ExplodeObject_SetExploded</c>,
/// <c>0x49D010</c>), and if it has fireballs a blast goes off at that model's first magic vertex.
/// </para>
/// <para>
/// A bullet adds its damage and, enough of it, explodes it the next tick (<c>ExplodeObject_WeaponHitHandler</c>, <c>0x49CCE0</c>);
/// a blast adds what reaches it and, enough, sets it off after its delay (<c>ExplodeObject_ExplosionHitHandler</c>,
/// <c>0x49CD80</c>). An object whose damage scale is less than nothing takes no damage.
/// </para>
/// </remarks>
public sealed class Igi2Explosive
{
    /// <summary>The model its blast's fragments are, <c>1009_01_1</c>.</summary>
    public const string FragmentModel = "1009_01_1";

    /// <summary>The size of its blast's burn mark.</summary>
    public const float BurnRange = 6144f;

    /// <summary>The AI event its blast raises.</summary>
    public const int AiEvent = 20;

    /// <summary>The flags of that event.</summary>
    public const int AiEventFlags = 0x402;

    /// <summary>How far that event carries.</summary>
    public const float AiEventRange = 307200f;

    private readonly IIgi2ExplosiveParameters _parameters;
    private readonly GameTask _owner;
    private float[] _frame = Igi2Pose.Rotation(0f, 0f, 0f);
    private byte _isExploded;
    private byte _isExplode;
    private float _damage;
    private float _maxDamage;
    private float _explosionDamage;
    private int _delay = -1;
    private Igi2LevelExpression? _expression;
    private IReadOnlyList<Igi2RigidObject> _solids = [];

    /// <summary>Initializes a new instance of the <see cref="Igi2Explosive"/> class.</summary>
    /// <param name="parameters">The owner's parameters.</param>
    /// <param name="owner">The task it explodes, whose name its script is compiled under.</param>
    public Igi2Explosive(IIgi2ExplosiveParameters parameters, GameTask owner)
    {
        _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <summary>Gets a value indicating whether it has exploded, <c>isExploded</c>.</summary>
    public bool IsExploded => _isExploded != 0;

    /// <summary>Gets the damage it has taken, <c>vDamage</c>.</summary>
    public float Damage => _damage;

    /// <summary>Gets the ticks before it explodes, <c>nDelay</c>; less than nought while nothing has set it off.</summary>
    public int Delay => _delay;

    /// <summary>Gets its frame, row by row, as its parameters turn it.</summary>
    public IReadOnlyList<float> Frame => _frame;

    /// <summary>Gets the model it wears now.</summary>
    public string Model { get; private set; } = string.Empty;

    /// <summary>Gets or sets the level's models.</summary>
    public Func<string, MefModel?>? Models { get; set; }

    /// <summary>Gets or sets the explosions its blast goes off in, or <see langword="null"/> for none.</summary>
    public Igi2ExplosionWorld? Explosions { get; set; }

    /// <summary>Takes the solids the collision world built of it; its own answers the weapon hits sent to it.</summary>
    /// <param name="solids">Its solids, as <see cref="Igi2CollisionWorld.SolidsOf"/> gives them.</param>
    public void Bind(IReadOnlyList<Igi2RigidObject> solids)
    {
        _solids = solids ?? throw new ArgumentNullException(nameof(solids));
        if (solids.Count > 0 && solids[0].Owner is Igi2CollisionOwner { Depth: 0 })
        {
            solids[0].Handler = _owner;
        }
    }

    /// <summary>
    /// Readies it whole, as <c>ExplodeObject_UpdateInternalDataHandler</c> (<c>igi2 0x49CA00</c>) leaves it: its model and frame
    /// its parameters', no damage, nothing counting down, and its two damages taken from its scales.
    /// </summary>
    public void Reset()
    {
        Model = _parameters.Model;
        Vector3f angles = _parameters.Orientation;
        _frame = Igi2Pose.Rotation(angles.X, angles.Y, angles.Z);
        _isExplode = 0;
        _isExploded = 0;
        _damage = 0f;
        _delay = -1;
        _maxDamage = _parameters.DamageScale;
        _explosionDamage = _parameters.ExplosionDamageScale;
    }

    /// <summary>Its tick, after its owner's children have run.</summary>
    /// <param name="level">The level's tick.</param>
    public void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (_isExploded != 0)
        {
            return;
        }

        _expression ??= new Igi2LevelExpression(_parameters.ExplosionScript, _owner.Name, level.Natives);
        if (!_expression.IsEmpty)
        {
            _isExplode = (byte)Igi2LevelExpression.Truncate(level.Evaluate(_expression));
        }

        if (_isExplode == 0 && _delay != 0)
        {
            if (_delay > 0)
            {
                _delay--;
            }

            return;
        }

        Explode();
        if (Models?.Invoke(_parameters.DestroyedModel) is not null && _delay > 0)
        {
            _delay--;
        }
    }

    /// <summary>A bullet's hit: its damage added, and enough of it explodes the object the next tick.</summary>
    /// <param name="damage">The damage the bullet still does.</param>
    public void WeaponHit(float damage)
    {
        // Only a damage scale below nought refuses; one that is not a number takes damage like any other.
        if (_isExploded != 0 || _maxDamage < 0f)
        {
            return;
        }

        // The shooter is told it hit something (Human_eHitByYouQTaskEvent), which only counts towards the score.
        _damage = ((Fpu)damage + _damage).F;
        if (_damage >= _maxDamage)
        {
            _delay = 0;
        }
    }

    /// <summary>A blast's hit: what reaches it added, and enough of it sets it off after its delay.</summary>
    /// <param name="hit">The blast.</param>
    public void ExplosionHit(in Igi2ExplosionHit hit)
    {
        // Only an object in the world's tree of objects (ptDynCubeObjTreeItem) is hit, which here is one with a solid.
        if (_isExploded != 0 || _solids.Count == 0 || _maxDamage < 0f)
        {
            return;
        }

        // Its size is its model's radius times its largest scale, its solid's bounding radius.
        float radius = _solids[0].BoundingRadius;
        _damage = (hit.Resolve(_parameters.Position, radius) + _damage).F;
        if (_damage >= _maxDamage)
        {
            _delay = Fpu.ToInt32((Fpu)_parameters.ExplosionDelay * 30.0);
        }
    }

    /// <summary>Exports <c>isExploded</c>, a writable byte (<c>ExplodeObject_RegisterScriptSymbolsHandler</c>, <c>igi2 0x49CE80</c>).</summary>
    /// <param name="variables">The level's variables.</param>
    public void ExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(_owner, "isExploded", () => _isExploded, value => _isExploded = (byte)value, sizeof(byte));
    }

    // ExplodeObject_Explode (igi2 0x49C370): exploded first, so the blast goes off at the destroyed model's vertex.
    private void Explode()
    {
        SetExploded();
        int fireballs = _parameters.ExplosionFireballs;
        if (fireballs <= 0 || Explosions is not { } explosions)
        {
            return;
        }

        Vector3d at = Igi2MagicVertex.Position(Models?.Invoke(Model), 0, _frame, _parameters.Position);
        explosions.New(
            at,
            1,
            0,
            _explosionDamage,
            ((Fpu)_parameters.ExplosionRadius * 4096.0).F,
            ((Fpu)_parameters.ExplosionFalloffRadius * 4096.0).F,
            BurnRange,
            _parameters.ExplosionFragments,
            fireballs,
            null,
            FragmentModel,
            _parameters.ExplosionSound,
            AiEvent,
            AiEventFlags,
            AiEventRange,
            null,
            null,
            Igi2ExplosionFalloff.Linear);
    }

    /// <summary>
    /// Wears its destroyed model, if that model exists, and is exploded from then on, without a blast
    /// (<c>ExplodeObject_SetExploded</c>, <c>igi2 0x49D010</c>).
    /// </summary>
    public void SetExploded()
    {
        if (Models?.Invoke(_parameters.DestroyedModel) is { } destroyed)
        {
            if (!string.Equals(Model, _parameters.DestroyedModel, StringComparison.Ordinal)
                && _solids.Count > 0
                && Igi2CollisionWorld.Meshes(destroyed) is { } meshes)
            {
                _solids[0].ChangeModel(meshes, destroyed.NativeMarkerBound);
            }

            Model = _parameters.DestroyedModel;
        }

        _isExploded = 1;
    }

    /// <summary>
    /// Writes its save state, <c>ExplodeObject_ptSerializeParameterList</c>: a model object's, then whether it has exploded and is
    /// exploding, its damage and its delay.
    /// </summary>
    /// <param name="writer">The writer.</param>
    public void WriteState(Saves.Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Saves.Igi2ModelObjState.Write(writer, _parameters.Position, _frame);
        writer.Bool(_isExploded != 0);
        writer.Bool(_isExplode != 0);
        writer.Real(_damage);
        writer.Int(_delay);
    }

    /// <summary>Reads its save state back; one that had exploded wears its destroyed model again (<c>ExplodeObject_DeSerializeHandler</c>).</summary>
    /// <param name="reader">The reader.</param>
    public void ReadState(Saves.Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Saves.Igi2ModelObjState.Read(reader);
        bool exploded = reader.Bool();
        _isExplode = reader.Bool() ? (byte)1 : (byte)0;
        _damage = reader.Real();
        _delay = reader.Int();
        if (exploded)
        {
            SetExploded();
        }
        else
        {
            _isExploded = 0;
        }
    }
}
