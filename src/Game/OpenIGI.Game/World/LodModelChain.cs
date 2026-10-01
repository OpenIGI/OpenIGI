using System.Collections.ObjectModel;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// A model name resolved to its chain of detail levels — the original's "LOD virtual model".
/// </summary>
/// <remarks>
/// <para>
/// <c>0x4CED50</c> builds one of these by loading the requested name and then <b>incrementing the last
/// character of the name</b> and loading again, stopping at the first name that does not resolve or once
/// it has five. So <c>435_01_1</c> pulls in <c>435_01_2</c> through <c>435_01_5</c>, and a name ending in
/// something other than a digit still works because the step is character arithmetic, not a parse.
/// </para>
/// <para>
/// This matters well beyond drawing: every level is baked one lightmap per level of detail, so the length
/// of this chain is what decides how many <c>.olm</c> entries an object contributes.
/// </para>
/// </remarks>
public sealed class LodModelChain
{
    /// <summary>The most detail levels the original will chain, from <c>0x4CED50</c>'s loop bound.</summary>
    public const int MaxLevels = 5;

    private readonly MefModel[] _models;
    private readonly string[] _names;

    private LodModelChain(string name, string[] names, MefModel[] models)
    {
        Name = name;
        _names = names;
        _models = models;
    }

    /// <summary>Gets the name the chain was resolved from, which is level 0's name.</summary>
    public string Name { get; }

    /// <summary>Gets the number of detail levels, between one and <see cref="MaxLevels"/>.</summary>
    public int Count => _models.Length;

    /// <summary>Gets the models, most detailed first.</summary>
    public ReadOnlySpan<MefModel> Models => _models;

    /// <summary>Gets the model names, most detailed first.</summary>
    public IReadOnlyList<string> Names => new ReadOnlyCollection<string>(_names);

    /// <summary>Gets the most detailed model, the one a nearby camera draws.</summary>
    public MefModel Highest => _models[0];

    /// <summary>Resolves a name to its chain of detail levels.</summary>
    /// <param name="name">The model name, as a placement or a magic-object definition spells it.</param>
    /// <param name="resolve">Looks one model name up, returning <see langword="false"/> when it has none.</param>
    /// <param name="chain">Receives the chain when the first name resolves.</param>
    /// <returns><see langword="false"/> when not even the requested name resolves.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static bool TryResolve(string name, ModelResolver resolve, out LodModelChain? chain)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(resolve);

        chain = null;
        if (name.Length == 0)
        {
            return false;
        }

        List<MefModel> models = [];
        List<string> names = [];
        char[] current = name.ToCharArray();

        for (int level = 0; level < MaxLevels; level++)
        {
            string candidate = new(current);
            if (!resolve(candidate, out MefModel? model) || model is null)
            {
                break;
            }

            models.Add(model);
            names.Add(candidate);

            // The original steps the name by adding one to its final character, in place.
            current[^1]++;
        }

        if (models.Count == 0)
        {
            return false;
        }

        chain = new LodModelChain(name, [.. names], [.. models]);
        return true;
    }

    /// <summary>Gets one detail level.</summary>
    /// <param name="level">The level, zero being the most detailed.</param>
    /// <returns>The model.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is not in the chain.</exception>
    public MefModel this[int level]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(level);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(level, _models.Length);
            return _models[level];
        }
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Name} ({Count} LOD)";
}

/// <summary>Looks one model name up in whatever packs are open.</summary>
/// <param name="name">The model name.</param>
/// <param name="model">Receives the model when the name resolves.</param>
/// <returns><see langword="true"/> when the name resolves.</returns>
public delegate bool ModelResolver(string name, out MefModel? model);
