using OpenIGI.Core.Graphics;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Widgets;

/// <summary>One pin of a node on a <see cref="NodeCanvas"/>.</summary>
/// <param name="Id">Its identity within its node.</param>
/// <param name="Label">What it is called.</param>
/// <param name="Colour">Its type's colour.</param>
/// <param name="IsExec">Whether it carries control flow (drawn as an arrow) rather than a value (drawn as a dot).</param>
public sealed record CanvasPin(string Id, string Label, Bgra32 Colour, bool IsExec = false)
{
    /// <summary>Gets a value indicating whether a wire is plugged in, which fills the pin.</summary>
    public bool IsConnected { get; init; }

    /// <summary>Gets the value typed in place, shown beside an unwired input, or <see langword="null"/>.</summary>
    public string? Value { get; init; }

    /// <summary>Gets a value indicating whether the value is true or false, drawn as a tick box that a click flips.</summary>
    public bool IsToggle { get; init; }

    /// <summary>Gets a value indicating whether the value reads as true, for a toggle.</summary>
    public bool IsOn { get; init; }

    /// <summary>Gets the tooltip: the pin's type and what it means.</summary>
    public string? Tooltip { get; init; }
}

/// <summary>One node of a <see cref="NodeCanvas"/>.</summary>
/// <param name="Id">Its identity.</param>
/// <param name="Title">Its title.</param>
/// <param name="Position">Its top-left corner, in canvas units.</param>
/// <param name="Header">Its title bar's colour, by category.</param>
/// <param name="Inputs">Its input pins, down its left side.</param>
/// <param name="Outputs">Its output pins, down its right side.</param>
public sealed record CanvasNode(string Id, string Title, UiVec2 Position, Bgra32 Header, IReadOnlyList<CanvasPin> Inputs, IReadOnlyList<CanvasPin> Outputs)
{
    /// <summary>Gets a second, quieter line under the title, such as the task's type.</summary>
    public string? Subtitle { get; init; }

    /// <summary>Gets the problem to badge the node with, or <see langword="null"/>.</summary>
    public string? Problem { get; init; }

    /// <summary>Gets a value indicating whether the problem is an error rather than a warning.</summary>
    public bool IsError { get; init; }

    /// <summary>Gets a value indicating whether the node is folded to its title.</summary>
    public bool Collapsed { get; init; }

    /// <summary>Gets a value indicating whether the node starts a chain (an event), drawn with a rounded, accented header.</summary>
    public bool IsEvent { get; init; }

    /// <summary>Gets the settings shown under the pins, as a node editor shows a node's defaults on it.</summary>
    public IReadOnlyList<CanvasDetail> Details { get; init; } = [];

    /// <summary>Gets the tooltip: what the node does.</summary>
    public string? Tooltip { get; init; }
}

/// <summary>A wire from an output pin to an input pin.</summary>
/// <param name="FromNode">The node it leaves.</param>
/// <param name="FromPin">The output it leaves by.</param>
/// <param name="ToNode">The node it enters.</param>
/// <param name="ToPin">The input it enters by.</param>
/// <param name="Colour">Its colour: the type it carries.</param>
public sealed record CanvasWire(string FromNode, string FromPin, string ToNode, string ToPin, Bgra32 Colour)
{
    /// <summary>Gets the reroute points the wire runs through, in canvas units, in order from its output.</summary>
    public IReadOnlyList<UiVec2> Via { get; init; } = [];

    /// <summary>Gets a value indicating whether it carries control flow, drawn thicker.</summary>
    public bool IsExec { get; init; }
}

/// <summary>A box on the canvas behind its nodes: a comment, or a frame that holds a container's children.</summary>
/// <param name="Id">Its identity.</param>
/// <param name="Title">Its title.</param>
/// <param name="Bounds">Where it is, in canvas units.</param>
/// <param name="Colour">Its tint.</param>
public sealed record CanvasFrame(string Id, string Title, UiRect Bounds, Bgra32 Colour)
{
    /// <summary>Gets a value indicating whether it is a comment (moves what it covers) rather than a frame (the nodes are its children).</summary>
    public bool IsComment { get; init; }
}

/// <summary>What a <see cref="NodeCanvas"/> draws: built by whoever owns the graph, read again whenever it changes.</summary>
public interface INodeCanvasSource
{
    /// <summary>Gets the nodes, drawn in order.</summary>
    IReadOnlyList<CanvasNode> Nodes { get; }

    /// <summary>Gets the wires.</summary>
    IReadOnlyList<CanvasWire> Wires { get; }

    /// <summary>Gets the frames and comments, drawn behind the nodes.</summary>
    IReadOnlyList<CanvasFrame> Frames { get; }
}

/// <summary>One setting shown on a node, under its pins.</summary>
/// <param name="Label">The setting's name.</param>
/// <param name="Value">Its value as it reads.</param>
/// <param name="Tooltip">The whole value, for hover.</param>
public sealed record CanvasDetail(string Label, string Value, string Tooltip);
