using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Weapons;

/// <summary>
/// The weapon roster as <c>weapons/weaponconfig.qvm</c> declares it and the runtime consumes it.
/// </summary>
/// <remarks>
/// <para>
/// The script is an ordinary task tree, so loading it is just running it against the task system, and
/// it describes all twenty-four weapons in the same field order as the live path. Mission startup
/// applies each record to the matching <see cref="WeaponTypeRegistry"/> entry before gameplay begins.
/// </para>
/// <para>
/// It is not dead weight, though. A <c>WeaponConfig</c> instance is 1800 bytes, which is exactly the
/// 32-byte task header plus the 1768-byte runtime record, and its extra fiftieth parameter — <c>Is
/// weapon selectable when empty</c> — lands at the very offset the live loader hardcodes to 1. So this
/// is the <i>same struct</i>, declared a field further on: an intended replacement for
/// <c>DefineWeaponType</c> that shipped without ever being wired up. That makes it a genuine
/// cross-check on the live reader and the authoritative source for shared runtime tuning, which is
/// what the parity tests verify.
/// </para>
/// </remarks>
public sealed class WeaponConfigRegistry
{
    private const string LogCategory = "Weapons";

    /// <summary>The script that carries the roster.</summary>
    public const string ScriptResource = "LOCAL:weapons/weaponconfig.qvm";

    /// <summary>The task type each weapon is declared as.</summary>
    public const string WeaponConfigType = "WeaponConfig";

    /// <summary>The type the weapons hang off.</summary>
    public const string WeaponConfigContainerType = "WeaponConfigContainer";

    private readonly List<WeaponConfigTask> _weapons = [];
    private readonly Dictionary<int, WeaponConfigTask> _byId = [];
    private readonly IOpenIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="WeaponConfigRegistry"/> class.</summary>
    /// <param name="logger">The logger for load diagnostics; defaults to a sink that discards them.</param>
    public WeaponConfigRegistry(IOpenIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Gets the weapons, in script order.</summary>
    public IReadOnlyList<WeaponConfigTask> Weapons => _weapons;

    /// <summary>Gets the weapons keyed by their numeric id.</summary>
    public IReadOnlyDictionary<int, WeaponConfigTask> ById => _byId;

    /// <summary>Registers the weapon task types.</summary>
    /// <param name="types">The registry to add to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="types"/> is <see langword="null"/>.</exception>
    public static void RegisterTypes(TaskTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(types);
        if (!types.TryGet(WeaponConfigType, out TaskType? weaponType) || weaponType.IsDeclarationOnly)
        {
            types.Register<WeaponConfigTask>(WeaponConfigType);
        }

        if (!types.TryGet(WeaponConfigContainerType, out TaskType? containerType) || containerType.IsDeclarationOnly)
        {
            types.Register<WeaponConfigContainerTask>(WeaponConfigContainerType);
        }
    }

    /// <summary>What <see cref="Load"/> returns when the title ships no script at this path.</summary>
    /// <remarks>
    /// Negative, so it cannot be confused with the nought a title that ships an empty table returns.
    /// </remarks>
    public const int NoScript = -1;

    /// <summary>Reads and runs the weapon configuration.</summary>
    /// <param name="resources">Where the script is read from.</param>
    /// <param name="natives">The script namespace; the enum constants must already be registered.</param>
    /// <param name="tree">The task tree the configuration is built into.</param>
    /// <returns>The number of weapons registered, or <see cref="NoScript"/> when there is no script.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="EngineFatalException">The script is malformed or faults.</exception>
    public int Load(ResourceRegistry resources, NativeRegistry natives, TaskTree tree)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(tree);

        _weapons.Clear();
        _byId.Clear();

        // Absent is not the same as broken. A title that keeps its weapon configuration in some other
        // form ships no script at this path at all -- I.G.I. 2 builds its weapons as tasks out of
        // WEAPONS/weapon.qvm rather than as a table here -- and the caller should be able to say so and
        // carry on, the way it already does for a missing material table. A script that IS here and does
        // not parse still throws below, because that is a broken installation rather than a different
        // one.
        if (!resources.TryLoad(ScriptResource, out ReadOnlyMemory<byte> bytes))
        {
            return NoScript;
        }

        QvmProgram program;
        try
        {
            program = QvmProgram.Read(bytes);
        }
        catch (QvmFormatException ex)
        {
            throw new EngineFatalException($"Couldn't load script: {ScriptResource}", ex);
        }

        QvmInterpreter interpreter = new(program, natives);
        QvmError error = interpreter.Run();
        if (error != QvmError.None)
        {
            throw new EngineFatalException(
                $"{ScriptResource} faulted with {error}: {interpreter.ErrorMessage}");
        }

        Collect(tree.Root);
        _logger.Information(LogCategory, $"Registered {_weapons.Count} weapon(s).");
        return _weapons.Count;
    }

    /// <summary>
    /// Publishes each weapon's <c>WEAPON_ID_*</c> symbol with its real numeric id.
    /// </summary>
    /// <param name="natives">The namespace later scripts resolve against.</param>
    /// <returns>The number of symbols published.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="natives"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Run this before anything that names a weapon — <c>humanplayer.qvm</c> above all. A symbol
    /// somebody else has already registered is left alone rather than overwritten, so a caller can
    /// still pin one by hand.
    /// </remarks>
    public int PublishScriptIds(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);

        int published = 0;
        foreach (WeaponConfigTask weapon in _weapons)
        {
            if (weapon.ScriptId.Length == 0 || natives.Contains(weapon.ScriptId))
            {
                continue;
            }

            natives.RegisterConstant(weapon.ScriptId, weapon.WeaponId);
            published++;
        }

        return published;
    }

    /// <summary>Looks a weapon up by its numeric id.</summary>
    /// <param name="weaponId">The id.</param>
    /// <param name="weapon">Receives the weapon when the id is known.</param>
    /// <returns><see langword="true"/> when the roster has that id.</returns>
    public bool TryGet(int weaponId, out WeaponConfigTask weapon) => _byId.TryGetValue(weaponId, out weapon!);

    /// <summary>Associates the loaded script-config records with the executable-style weapon records.</summary>
    public void AttachTo(WeaponTypeRegistry roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        roster.ApplyRetailConfig(this);
    }

    /// <summary>Reads a tagged Real32 child from this weapon's config tree.</summary>
    public static bool TryGetRealTag(WeaponConfigTask weapon, string tag, out float value)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        ArgumentNullException.ThrowIfNull(tag);
        foreach (GameTask child in Descendants(weapon))
        {
            if (child is not DeclaredTask declared
                || !string.Equals(child.Label, tag, StringComparison.Ordinal)
                || declared.Type.Parameters.Count == 0
                || declared.GetValue(declared.Type.Parameters.Count - 1) is not float candidate)
            {
                continue;
            }

            value = candidate;
            return true;
        }

        value = 0f;
        return false;
    }

    /// <summary>Reads a tagged String16 child from this weapon's config tree.</summary>
    public static bool TryGetStringTag(WeaponConfigTask weapon, string tag, out string value)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        ArgumentNullException.ThrowIfNull(tag);
        foreach (GameTask child in Descendants(weapon))
        {
            if (child is not DeclaredTask declared
                || !string.Equals(child.Label, tag, StringComparison.Ordinal)
                || declared.Type.Parameters.Count == 0
                || declared.GetValue(declared.Type.Parameters.Count - 1) is not string candidate)
            {
                continue;
            }

            value = candidate;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static IEnumerable<GameTask> Descendants(GameTask parent)
    {
        foreach (GameTask child in parent.Children)
        {
            yield return child;
            foreach (GameTask nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    private void Collect(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is WeaponConfigTask weapon)
            {
                _weapons.Add(weapon);

                // The original's lookup takes the first match, so a repeated id keeps the earlier one.
                _byId.TryAdd(weapon.WeaponId, weapon);
            }

            Collect(child);
        }
    }
}
