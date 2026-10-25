namespace OpenIGI.Engine.Titles;

/// <summary>A folder of files that stand in for a title's own at the same paths while it runs: a mod's data.</summary>
/// <param name="Id">A stable id for the folder, which the console's content commands name it by.</param>
/// <param name="Name">The name shown for it.</param>
/// <param name="Root">The folder, which mirrors the title's installation.</param>
public sealed record TitleContentFolder(string Id, string Name, string Root);
