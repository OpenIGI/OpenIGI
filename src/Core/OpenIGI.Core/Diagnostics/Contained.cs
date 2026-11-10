namespace OpenIGI.Core.Diagnostics;

/// <summary>Calls code the engine does not own, such as a mod's, so that its failure is logged and the caller goes on.</summary>
/// <remarks>
/// The state forms take what the call needs as an argument, so a <see langword="static"/> lambda serves and nothing is
/// allocated per call; they are the ones for a per-tick or per-frame path.
/// </remarks>
public static class Contained
{
    /// <summary>Calls <paramref name="call"/>; if it throws, logs it as an error and returns <see langword="false"/>.</summary>
    /// <param name="logger">Where a failure is reported.</param>
    /// <param name="category">The log category.</param>
    /// <param name="what">What was called, as the log names it: "A Struck listener", for one.</param>
    /// <param name="call">The call.</param>
    /// <returns>Whether it returned without throwing.</returns>
    public static bool Run(IOpenIgiLogger logger, string category, string what, Action call)
    {
        ArgumentNullException.ThrowIfNull(call);
        return Run(logger, category, what, call, static c => c());
    }

    /// <summary>Calls <paramref name="call"/> with <paramref name="state"/>; if it throws, logs it as an error and returns <see langword="false"/>.</summary>
    /// <typeparam name="TState">What the call needs.</typeparam>
    /// <param name="logger">Where a failure is reported.</param>
    /// <param name="category">The log category.</param>
    /// <param name="what">What was called, as the log names it.</param>
    /// <param name="state">What the call needs.</param>
    /// <param name="call">The call.</param>
    /// <returns>Whether it returned without throwing.</returns>
    public static bool Run<TState>(IOpenIgiLogger logger, string category, string what, TState state, Action<TState> call)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(call);
        try
        {
            call(state);
            return true;
        }
#pragma warning disable CA1031 // Code the engine does not own fails on its own; the caller goes on.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.Error(category, $"{what} failed: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>Calls <paramref name="call"/> with <paramref name="state"/> for a result; if it throws, logs it as an error and returns <see langword="false"/>.</summary>
    /// <typeparam name="TState">What the call needs.</typeparam>
    /// <typeparam name="TResult">What it gives.</typeparam>
    /// <param name="logger">Where a failure is reported.</param>
    /// <param name="category">The log category.</param>
    /// <param name="what">What was called, as the log names it.</param>
    /// <param name="state">What the call needs.</param>
    /// <param name="call">The call.</param>
    /// <param name="result">What it gave, or the default when it threw.</param>
    /// <returns>Whether it returned without throwing.</returns>
    public static bool Run<TState, TResult>(
        IOpenIgiLogger logger, string category, string what, TState state, Func<TState, TResult> call, out TResult result)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(call);
        try
        {
            result = call(state);
            return true;
        }
#pragma warning disable CA1031 // Code the engine does not own fails on its own; the caller goes on.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            result = default!;
            logger.Error(category, $"{what} failed: {ex.Message}", ex);
            return false;
        }
    }
}
