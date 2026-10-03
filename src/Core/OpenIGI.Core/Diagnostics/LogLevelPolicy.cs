using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace OpenIGI.Core.Diagnostics;

/// <summary>Decides the lowest severity logged for each category.</summary>
/// <remarks>
/// <para>
/// A default level applies to every category; an override applies to a category and everything under it,
/// so <c>AI</c> covers <c>AI.Gunner</c> and <c>AI.Alarm</c>, and the longest matching override wins.
/// Categories are compared without regard to case.
/// </para>
/// <para>
/// Lookups are lock-free: the overrides are an immutable snapshot, and each category's answer is cached
/// until the next change. Changes may come from any thread.
/// </para>
/// </remarks>
public sealed class LogLevelPolicy
{
    /// <summary>The variable a policy is read from by <see cref="FromEnvironment"/>.</summary>
    /// <remarks>Either a level (<c>debug</c>) or a list (<c>warning,Vfs=debug,AI.Gunner=trace</c>).</remarks>
    public const string EnvironmentVariable = "OPENIGI_LOG";

    /// <summary>The older variable that named only a <see cref="LogVerbosity"/>; still honoured.</summary>
    public const string LegacyEnvironmentVariable = "OPENIGI_LOG_VERBOSITY";

    private readonly object _gate = new();
    private volatile ImmutableDictionary<string, LogLevel> _overrides =
        ImmutableDictionary.Create<string, LogLevel>(StringComparer.OrdinalIgnoreCase);
    private volatile ConcurrentDictionary<string, LogLevel> _resolved = NewCache();
    private volatile LogLevel _default;

    /// <summary>Initialises a new instance of the <see cref="LogLevelPolicy"/> class.</summary>
    /// <param name="defaultLevel">The level for every category without an override.</param>
    public LogLevelPolicy(LogLevel defaultLevel = LogLevel.Information) => _default = defaultLevel;

    /// <summary>Gets or sets the level for every category without an override.</summary>
    public LogLevel Default
    {
        get => _default;
        set
        {
            lock (_gate)
            {
                if (_default == value)
                {
                    return;
                }

                _default = value;
                _resolved = NewCache();
            }
        }
    }

    /// <summary>Gets the overrides, keyed by category prefix.</summary>
    public IReadOnlyDictionary<string, LogLevel> Overrides => _overrides;

    /// <summary>Gets a value indicating whether the environment named the default level, which settings then leave alone.</summary>
    public bool IsPinned { get; private set; }

    /// <summary>Reads <see cref="EnvironmentVariable"/>, else <see cref="LegacyEnvironmentVariable"/>, over a fallback.</summary>
    /// <param name="fallback">The default level when neither variable is set or understood.</param>
    /// <returns>The policy; <see cref="IsPinned"/> when a variable named the default level.</returns>
    public static LogLevelPolicy FromEnvironment(LogLevel fallback)
    {
        string? spec = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(spec))
        {
            spec = Environment.GetEnvironmentVariable(LegacyEnvironmentVariable);
        }

        LogLevelPolicy policy = new(fallback);
        if (string.IsNullOrWhiteSpace(spec))
        {
            return policy;
        }

        // Only a default named in the variable pins it; category overrides alone leave the default to settings.
        _ = policy.TryApply(spec);
        policy.IsPinned = spec
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(static entry => !entry.Contains('=', StringComparison.Ordinal) && TryParseLevel(entry, out _));
        return policy;
    }

    /// <summary>Parses a level name: a <see cref="LogLevel"/>, a <see cref="LogVerbosity"/>, or a short form.</summary>
    /// <param name="text">The name, such as <c>debug</c>, <c>warn</c>, <c>minimal</c> or <c>off</c>.</param>
    /// <param name="level">The level, when recognised.</param>
    /// <returns>Whether the name was recognised.</returns>
    public static bool TryParseLevel(string? text, out LogLevel level)
    {
        switch (text?.Trim().ToUpperInvariant())
        {
            case "TRACE" or "TRC" or "ALL":
                level = LogLevel.Trace;
                return true;
            case "DEBUG" or "DBG":
                level = LogLevel.Debug;
                return true;
            case "INFORMATION" or "INFO" or "INF" or "VERBOSE":
                level = LogLevel.Information;
                return true;
            case "WARNING" or "WARN" or "WRN" or "MINIMAL":
                level = LogLevel.Warning;
                return true;
            case "ERROR" or "ERR":
                level = LogLevel.Error;
                return true;
            case "FATAL" or "FTL":
                level = LogLevel.Fatal;
                return true;
            case "NONE" or "OFF":
                level = LogLevel.None;
                return true;
            default:
                level = default;
                return false;
        }
    }

    /// <summary>Maps the player-facing verbosity onto a level.</summary>
    /// <param name="verbosity">The verbosity.</param>
    /// <returns>The lowest level that verbosity logs.</returns>
    public static LogLevel LevelFor(LogVerbosity verbosity) => verbosity switch
    {
        LogVerbosity.Off => LogLevel.None,
        LogVerbosity.Minimal => LogLevel.Warning,
        _ => LogLevel.Information,
    };

    /// <summary>Applies a specification: a bare level sets the default, <c>Category=level</c> sets an override.</summary>
    /// <param name="spec">Comma- or semicolon-separated entries, for example <c>warning,Vfs=debug</c>.</param>
    /// <returns>Whether every entry was understood; the ones that were are applied either way.</returns>
    public bool TryApply(string spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        bool ok = true;
        foreach (string entry in spec.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int equals = entry.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
            {
                if (TryParseLevel(entry, out LogLevel level))
                {
                    Default = level;
                }
                else
                {
                    ok = false;
                }

                continue;
            }

            string category = entry[..equals].Trim();
            if (category.Length > 0 && TryParseLevel(entry[(equals + 1)..], out LogLevel overrideLevel))
            {
                SetOverride(category, overrideLevel);
            }
            else
            {
                ok = false;
            }
        }

        return ok;
    }

    /// <summary>Sets or clears the level for a category and everything under it.</summary>
    /// <param name="category">The category prefix, for example <c>AI</c>.</param>
    /// <param name="level">The level, or <see langword="null"/> to fall back to the default again.</param>
    public void SetOverride(string category, LogLevel? level)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        lock (_gate)
        {
            _overrides = level is { } value ? _overrides.SetItem(category, value) : _overrides.Remove(category);
            _resolved = NewCache();
        }
    }

    /// <summary>Gets the lowest severity logged for a category.</summary>
    /// <param name="category">The category.</param>
    /// <returns>The level.</returns>
    public LogLevel LevelFor(string category)
    {
        ConcurrentDictionary<string, LogLevel> resolved = _resolved;
        if (resolved.TryGetValue(category, out LogLevel level))
        {
            return level;
        }

        level = Resolve(category);
        resolved.TryAdd(category, level);
        return level;
    }

    private static ConcurrentDictionary<string, LogLevel> NewCache() => new(StringComparer.OrdinalIgnoreCase);

    private static bool Covers(string prefix, string category) =>
        category.Length == prefix.Length
            ? category.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            : category.Length > prefix.Length
              && category[prefix.Length] == '.'
              && category.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private LogLevel Resolve(string category)
    {
        ImmutableDictionary<string, LogLevel> overrides = _overrides;
        if (overrides.IsEmpty)
        {
            return _default;
        }

        string? best = null;
        foreach (string prefix in overrides.Keys)
        {
            if ((best is null || prefix.Length > best.Length) && Covers(prefix, category))
            {
                best = prefix;
            }
        }

        return best is null ? _default : overrides[best];
    }
}
