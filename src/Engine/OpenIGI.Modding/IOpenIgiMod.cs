namespace OpenIGI.Modding;

/// <summary>A mod's code: what it changes in a game, said once, before the game starts.</summary>
/// <remarks>
/// <para>
/// <see cref="Configure"/> runs once for each game the mod applies to, before that game's content is registered; everything
/// the mod changes it changes through the builder, so a game played without the mod is the game as it ships.
/// </para>
/// <para>
/// A mod rarely writes this by hand. A <see langword="partial"/> class marked <see cref="OpenIgiModAttribute"/> has it
/// generated from the attributes on its members (<see cref="AddsLevelSystemAttribute{TBuild}"/>,
/// <see cref="OnGameEventAttribute"/>, <see cref="OnModLoadedAttribute"/>, <see cref="ScriptConstantAttribute"/>) and on the
/// assembly's task classes (<see cref="AddsTaskAttribute"/>, <see cref="ReplacesTaskAttribute"/>); anything else it adds goes
/// in a <c>partial void OnConfigure(IModBuilder builder)</c>.
/// </para>
/// </remarks>
public interface IOpenIgiMod
{
    /// <summary>Says what the mod changes in one game.</summary>
    /// <param name="builder">What the mod can change.</param>
    void Configure(IModBuilder builder);
}
