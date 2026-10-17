using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// The player's tuning data — everything <c>humanplayer/humanplayer.qsc</c> publishes.
/// </summary>
/// <remarks>
/// <para>
/// <c>HumanPlayer_LoadParameters</c> (<c>0x4137A0</c>) loads the script and runs it; the four natives it
/// calls are registered by <c>HumanPlayer</c>'s own registrar (<c>0x40FE30</c>). A missing script is
/// <b>fatal</b> in the original — it prints and then spins in an empty <c>while(1)</c> — so this class
/// throws rather than carrying on with defaults.
/// </para>
/// </remarks>
public sealed class HumanPlayerConfig
{
    private const string LogCategory = "Player";

    /// <summary>The script that carries the data.</summary>
    public const string ScriptResource = "LOCAL:humanplayer/humanplayer.qvm";

    /// <summary>The native that publishes the general parameters.</summary>
    public const string DefineGeneralNative = "DefineHumanPlayerGeneral";

    /// <summary>The native that publishes the weapon-switch order.</summary>
    public const string DefineWeaponCycleNative = "DefineHumanPlayerWeaponCycle";

    /// <summary>The native that publishes the weapon categories.</summary>
    public const string DefineWeaponCategoryNative = "DefineHumanPlayerWeaponCategory";

    /// <summary>The native that publishes the per-ammo carry limits.</summary>
    public const string DefineAmmoLimitNative = "DefineHumanPlayerAmmoLimit";

    /// <summary>The prefix of the pattern-registered weapon-id symbols.</summary>
    public const string WeaponIdPrefix = "WEAPON_ID_";

    /// <summary>The prefix of the pattern-registered ammo-id symbols.</summary>
    public const string AmmoIdPrefix = "AMMO_ID_";

    private readonly List<int> _weaponCycle = [];
    private readonly List<HumanPlayerWeaponCategory> _categories = [];
    private readonly Dictionary<int, int> _ammoLimits = [];
    private readonly Dictionary<string, int> _placeholderIds = new(StringComparer.Ordinal);
    private readonly IOpenIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="HumanPlayerConfig"/> class.</summary>
    /// <param name="logger">The logger for load diagnostics; defaults to a sink that discards them.</param>
    public HumanPlayerConfig(IOpenIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Gets the general parameters, once the script has run.</summary>
    public HumanPlayerParameters? Parameters { get; private set; }

    /// <summary>Gets the weapon ids in switch order.</summary>
    public IReadOnlyList<int> WeaponCycle => _weaponCycle;

    /// <summary>Gets the weapon categories, in script order.</summary>
    public IReadOnlyList<HumanPlayerWeaponCategory> Categories => _categories;

    /// <summary>Gets the carry limit for each ammo id that declares one.</summary>
    public IReadOnlyDictionary<int, int> AmmoLimits => _ammoLimits;

    /// <summary>
    /// Gets the ids this loader had to invent for pattern-registered symbols, keyed by symbol name.
    /// </summary>
    /// <remarks>
    /// Empty once the weapon system supplies the real ones. See <see cref="Register"/> for why they
    /// exist at all.
    /// </remarks>
    public IReadOnlyDictionary<string, int> PlaceholderIds => _placeholderIds;

    /// <summary>Registers the four natives.</summary>
    /// <param name="natives">The script namespace the player script resolves against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="natives"/> is <see langword="null"/>.</exception>
    public void Register(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);
        natives.RegisterFunction(DefineGeneralNative, DefineGeneral);
        natives.RegisterFunction(DefineWeaponCycleNative, DefineWeaponCycle);
        natives.RegisterFunction(DefineWeaponCategoryNative, DefineWeaponCategory);
        natives.RegisterFunction(DefineAmmoLimitNative, DefineAmmoLimit);
    }

    /// <summary>Reads and runs the player script.</summary>
    /// <param name="resources">Where the script is read from.</param>
    /// <param name="natives">The script namespace, with <see cref="Register"/> already applied.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="EngineFatalException">The script is missing, malformed, or faults.</exception>
    public void Load(ResourceRegistry resources, NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);

        _weaponCycle.Clear();
        _categories.Clear();
        _ammoLimits.Clear();
        _placeholderIds.Clear();
        Parameters = null;

        if (!resources.TryLoad(ScriptResource, out ReadOnlyMemory<byte> bytes))
        {
            throw new EngineFatalException(
                $"HumanPlayer_LoadParameters: Couldn't load script \"{ScriptResource}\"");
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

        DeclareWeaponAndAmmoIds(program, natives);

        QvmInterpreter interpreter = new(program, natives);
        QvmError error = interpreter.Run();
        if (error != QvmError.None)
        {
            throw new EngineFatalException(
                $"{ScriptResource} faulted with {error}: {interpreter.ErrorMessage}");
        }

        _logger.Information(
            LogCategory,
            $"Player parameters loaded: {_weaponCycle.Count} weapon(s) in the cycle, "
                + $"{_categories.Count} categor(ies), {_ammoLimits.Count} ammo limit(s).");
    }

    /// <summary>
    /// Declares the <c>WEAPON_ID_*</c> and <c>AMMO_ID_*</c> symbols the script resolves against.
    /// </summary>
    /// <remarks>
    /// <b>Divergence, deliberate and temporary.</b> The original does not register these names at all:
    /// <c>0x47CF20</c> registers the <em>wildcard</em> <c>"AMMO_ID_.*"</c> with the script symbol system
    /// and the weapon roster supplies the values as it is read, so the numbers depend on the order
    /// <c>weaponconfig.qsc</c> and the per-weapon scripts load. None of that exists yet, so any symbol
    /// this script names that is not already registered gets an id allocated in symbol-table order and
    /// recorded in <see cref="PlaceholderIds"/>. The ids are therefore <b>not</b> the original's; only
    /// their identity across the four natives is meaningful until M9 lands the weapon system. Nothing
    /// here overwrites an id a caller has already registered.
    /// </remarks>
    private void DeclareWeaponAndAmmoIds(QvmProgram program, NativeRegistry natives)
    {
        int next = 1;
        foreach (string? symbol in program.SymbolNames)
        {
            if (symbol is null
                || !(symbol.StartsWith(WeaponIdPrefix, StringComparison.Ordinal)
                    || symbol.StartsWith(AmmoIdPrefix, StringComparison.Ordinal))
                || natives.Contains(symbol))
            {
                continue;
            }

            natives.RegisterConstant(symbol, next);
            _placeholderIds[symbol] = next;
            next++;
        }

        if (_placeholderIds.Count > 0)
        {
            _logger.Warning(
                LogCategory,
                $"{_placeholderIds.Count} weapon/ammo id(s) had no registered value and were numbered "
                    + "by symbol order; the weapon system owns the real ones.");
        }
    }

    private QvmValue DefineGeneral(QvmCallContext call)
    {
        float[] arguments = new float[HumanPlayerParameters.ArgumentCount];
        for (int i = 0; i < arguments.Length; i++)
        {
            arguments[i] = i < call.ArgumentCount ? call.GetSingle(i) : 0f;
        }

        Parameters = new HumanPlayerParameters(arguments);
        return QvmValue.FromInt32(1);
    }

    private QvmValue DefineWeaponCycle(QvmCallContext call)
    {
        _weaponCycle.Clear();
        for (int i = 0; i < call.ArgumentCount; i++)
        {
            _weaponCycle.Add(call.GetInt32(i));
        }

        return QvmValue.FromInt32(1);
    }

    private QvmValue DefineWeaponCategory(QvmCallContext call)
    {
        // The original walks a flat "count, ids..." stream and fills nine fixed records; the loop is
        // bounded by the table, not by the argument list, so a short stream leaves later records at
        // whatever they held and a long one is simply not read.
        _categories.Clear();
        int at = 0;
        while (_categories.Count < HumanPlayerWeaponCategory.CategoryCount && at < call.ArgumentCount)
        {
            int count = call.GetInt32(at++);
            List<int> ids = [];
            for (int i = 0; i < count && at < call.ArgumentCount; i++)
            {
                ids.Add(call.GetInt32(at++));
            }

            _categories.Add(new HumanPlayerWeaponCategory(ids));
        }

        return QvmValue.FromInt32(1);
    }

    private QvmValue DefineAmmoLimit(QvmCallContext call)
    {
        _ammoLimits.Clear();
        for (int i = 0; i + 1 < call.ArgumentCount; i += 2)
        {
            _ammoLimits[call.GetInt32(i)] = call.GetInt32(i + 1);
        }

        return QvmValue.FromInt32(1);
    }
}
