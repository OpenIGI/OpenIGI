namespace OpenIGI.Modding;

/// <summary>A mod's code: what it changes in a game, said once, before the game starts.</summary>
/// <remarks>
/// <para>
/// The loader makes the one public class of the mod's assembly that implements this, through its public parameterless
/// constructor, and calls <see cref="Configure"/> once for each game the mod applies to, before that game's content is
/// registered. Everything the mod changes it changes through the builder, so a game played without the mod is the game as it
/// ships.
/// </para>
/// <para>
/// <see cref="Configure"/> reads as the mod's whole wiring, top to bottom:
/// </para>
/// <code>
/// public sealed class MyMod : IOpenIgiMod
/// {
///     public void Configure(IModBuilder builder)
///     {
///         BoolCVar loud = builder.Settings.Bool("loud", false, "Doubles every footstep.");
///         builder.Scripts.Constant("MyMod_Answer", 42);
///         builder.LevelSystems&lt;MissionLevelBuild&gt;().InsertAfter(MissionSystemNames.Player, "MyMod.Arrive", Arrive);
///         builder.Events.On&lt;HumanKilled&gt;(killed =&gt; ...);
///         builder.OnStarted(session =&gt; ...);
///     }
/// }
/// </code>
/// </remarks>
public interface IOpenIgiMod
{
    /// <summary>Says what the mod changes in one game.</summary>
    /// <param name="builder">What the mod can change; <see cref="IModBuilder.Title"/> says which game it is.</param>
    void Configure(IModBuilder builder);
}
