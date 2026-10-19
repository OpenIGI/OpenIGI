using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace OpenIGI.Generators;

/// <summary>
/// Emits the parameter-binding code for task types, so level data is parsed into task fields without any
/// runtime reflection.
/// </summary>
/// <remarks>
/// <para>
/// For each class with <c>[ScriptParam]</c> members this generates an override of
/// <c>BindScriptParameters</c> that reads the arguments in declaration order, and an override of
/// <c>ScriptParameters</c> describing them. That mirrors the original's per-type property table
/// (<c>0x4F1950</c> / <c>0x4F1A80</c>) with the table resolved at compile time.
/// </para>
/// <para>
/// Declaration order is the wire order, so the emitted reads happen in the order the members appear in
/// source. Splitting one task's parameters across several files makes that order ill-defined and is
/// reported as <c>OIGI005</c>.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class ScriptParamGenerator : IIncrementalGenerator
{
    private const string AttributeName = "OpenIGI.Engine.Tasks.ScriptParamAttribute";
    private const string BaseTypeName = "OpenIGI.Engine.Tasks.GameTask";
    private const string DerivedAttributeName = "OpenIGI.Engine.Tasks.DerivedScriptParametersAttribute";

    private static readonly DiagnosticDescriptor NotATask = new(
        "OIGI001",
        "[ScriptParam] requires a GameTask",
        "'{0}' has [ScriptParam] members but does not derive from GameTask, so nothing can bind them",
        "OpenIGI",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NotPartial = new(
        "OIGI002",
        "Task type must be partial",
        "'{0}' has [ScriptParam] members, so it must be declared partial for the binder to be generated",
        "OpenIGI",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnknownParamType = new(
        "OIGI003",
        "Unknown script parameter type",
        "'{0}' declares script parameter type '{1}', which has no reader",
        "OpenIGI",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor TypeMismatch = new(
        "OIGI004",
        "Script parameter type does not match the member type",
        "Script parameter '{0}' is declared as {1}, which parses into '{2}', but the member is '{3}'",
        "OpenIGI",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor SplitAcrossFiles = new(
        "OIGI005",
        "Script parameters split across files",
        "'{0}' declares [ScriptParam] members in more than one file, so their wire order is ambiguous",
        "OpenIGI",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NotAssignable = new(
        "OIGI006",
        "Script parameter must be assignable",
        "Script parameter '{0}' is read-only, so the binder cannot write the parsed value into it",
        "OpenIGI",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<Candidate?> candidates = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeName,
                static (node, _) => node is VariableDeclaratorSyntax or PropertyDeclarationSyntax or FieldDeclarationSyntax,
                static (ctx, _) => Describe(ctx))
            .Where(static candidate => candidate is not null);

        context.RegisterSourceOutput(candidates.Collect(), static (ctx, all) => Emit(ctx, all));
    }

    private static Candidate? Describe(GeneratorAttributeSyntaxContext context)
    {
        ISymbol member = context.TargetSymbol;
        if (member.ContainingType is not { } owner)
        {
            return null;
        }

        AttributeData attribute = context.Attributes[0];
        if (attribute.ConstructorArguments.Length == 0)
        {
            return null;
        }

        TypedConstant typeArgument = attribute.ConstructorArguments[0];
        string? paramTypeName = GetEnumMemberName(typeArgument);
        if (paramTypeName is null)
        {
            return null;
        }

        string? declaredName = attribute.ConstructorArguments.Length > 1
            ? attribute.ConstructorArguments[1].Value as string
            : null;

        (string memberType, bool assignable) = member switch
        {
            IFieldSymbol field => (field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), !field.IsReadOnly && !field.IsConst),
            IPropertySymbol property => (property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), property.SetMethod is not null),
            _ => (string.Empty, false),
        };

        if (memberType.Length == 0)
        {
            return null;
        }

        SyntaxReference? syntax = member.DeclaringSyntaxReferences.FirstOrDefault();

        return new Candidate(
            owner,
            member.Name,
            declaredName ?? member.Name,
            paramTypeName,
            memberType,
            assignable,
            syntax?.SyntaxTree.FilePath ?? string.Empty,
            syntax?.Span.Start ?? 0,
            member.Locations.FirstOrDefault() ?? Location.None);
    }

    private static string? GetEnumMemberName(TypedConstant constant)
    {
        if (constant.Type is not INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType || constant.Value is null)
        {
            return null;
        }

        foreach (ISymbol member in enumType.GetMembers())
        {
            if (member is IFieldSymbol { HasConstantValue: true } field && Equals(field.ConstantValue, constant.Value))
            {
                return field.Name;
            }
        }

        return null;
    }

    private static void Emit(SourceProductionContext context, ImmutableArray<Candidate?> candidates)
    {
        IEnumerable<IGrouping<INamedTypeSymbol, Candidate>> groups = candidates
            .Where(static c => c is not null)
            .Select(static c => c!)
            .GroupBy(static c => c.Owner, SymbolEqualityComparer.Default as IEqualityComparer<INamedTypeSymbol>);

        foreach (IGrouping<INamedTypeSymbol, Candidate> group in groups)
        {
            EmitType(context, group.Key, [.. group]);
        }
    }

    private static void EmitType(SourceProductionContext context, INamedTypeSymbol owner, List<Candidate> members)
    {
        if (!DerivesFromGameTask(owner))
        {
            context.ReportDiagnostic(Diagnostic.Create(NotATask, members[0].Location, owner.Name));
            return;
        }

        if (!IsPartial(owner))
        {
            context.ReportDiagnostic(Diagnostic.Create(NotPartial, members[0].Location, owner.Name));
            return;
        }

        var files = new HashSet<string>(members.Select(static m => m.FilePath), StringComparer.Ordinal);
        if (files.Count > 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(SplitAcrossFiles, members[0].Location, owner.Name));
            return;
        }

        members.Sort(static (a, b) => a.Position.CompareTo(b.Position));

        var body = new StringBuilder();
        var descriptors = new StringBuilder();
        var lookup = new StringBuilder();
        var setter = new StringBuilder();
        var looked = new HashSet<string>(StringComparer.Ordinal);
        bool failed = false;

        foreach (Candidate member in members)
        {
            if (!ScriptParamTypeMap.TryGet(member.ParamTypeName, out ScriptParamTypeMap.Entry entry))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnknownParamType, member.Location, member.MemberName, member.ParamTypeName));
                failed = true;
                continue;
            }

            descriptors.Append("            new global::OpenIGI.Engine.Tasks.TaskParameter(")
                .Append(Literal(member.DeclaredName))
                .Append(", global::OpenIGI.Engine.Tasks.ScriptParamType.")
                .Append(member.ParamTypeName)
                .AppendLine("),");

            if (entry.Method is null)
            {
                // QTaskNote: editor metadata, no parse callback, no argument consumed.
                continue;
            }

            if (entry.ClrType is null)
            {
                // A parser with no CLR value still has to consume its authored arguments.
                body.Append("        reader.").Append(entry.Method).AppendLine("();");
                continue;
            }

            if (!member.Assignable)
            {
                context.ReportDiagnostic(Diagnostic.Create(NotAssignable, member.Location, member.MemberName));
                failed = true;
                continue;
            }

            if (!TypesMatch(entry.ClrType, member.MemberType))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    TypeMismatch, member.Location, member.MemberName, member.ParamTypeName, entry.ClrType, member.MemberType));
                failed = true;
                continue;
            }

            body.Append("        this.").Append(member.MemberName)
                .Append(" = reader.").Append(entry.Method).AppendLine("();");
            if (looked.Add(member.DeclaredName))
            {
                lookup.Append("            case ").Append(Literal(member.DeclaredName)).Append(": value = this.")
                    .Append(member.MemberName).AppendLine("; return true;");

                // A value of another type -- another game's declaration of the name -- is refused, not converted.
                string typed = "typed" + looked.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                setter.Append("            case ").Append(Literal(member.DeclaredName)).Append(": if (value is ")
                    .Append(member.MemberType.TrimEnd('?')).Append(' ').Append(typed).Append(") { this.").Append(member.MemberName)
                    .Append(" = ").Append(typed).AppendLine("; return true; } return false;");
            }
        }

        if (failed)
        {
            return;
        }

        bool derived = owner.GetAttributes()
            .Any(static a => string.Equals(a.AttributeClass?.ToDisplayString(), DerivedAttributeName, StringComparison.Ordinal));
        context.AddSource(
            $"{FileNameFor(owner)}.ScriptParams.g.cs",
            SourceText.From(Render(owner, body, descriptors, lookup, setter, derived), Encoding.UTF8));
    }

    private static string Render(INamedTypeSymbol owner, StringBuilder body, StringBuilder descriptors, StringBuilder lookup, StringBuilder setter, bool derived)
    {
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/> Emitted by OpenIGI.Generators.ScriptParamGenerator.");
        source.AppendLine("#nullable enable");
        source.AppendLine();

        bool hasNamespace = owner.ContainingNamespace is { IsGlobalNamespace: false };
        if (hasNamespace)
        {
            source.Append("namespace ").Append(owner.ContainingNamespace.ToDisplayString()).AppendLine(";");
            source.AppendLine();
        }

        var nesting = new Stack<INamedTypeSymbol>();
        for (INamedTypeSymbol? type = owner; type is not null; type = type.ContainingType)
        {
            nesting.Push(type);
        }

        var indent = new StringBuilder();
        foreach (INamedTypeSymbol type in nesting)
        {
            source.Append(indent).Append("partial class ").AppendLine(type.Name);
            source.Append(indent).AppendLine("{");
            indent.Append("    ");
        }

        source.AppendLine("    private static readonly global::OpenIGI.Engine.Tasks.TaskParameter[] GeneratedScriptParameters =");
        source.AppendLine("    [");
        source.Append(descriptors);
        source.AppendLine("    ];");
        source.AppendLine();
        if (derived)
        {
            // ParameterList_Derive: the base type's parameters first, then this type's own.
            source.AppendLine("    private global::OpenIGI.Engine.Tasks.TaskParameter[]? _derivedScriptParameters;");
            source.AppendLine();
            source.AppendLine("    /// <inheritdoc/>");
            source.AppendLine("    protected override global::System.Collections.Generic.IReadOnlyList<global::OpenIGI.Engine.Tasks.TaskParameter> ScriptParameters");
            source.AppendLine("        => _derivedScriptParameters ??= [.. base.ScriptParameters, .. GeneratedScriptParameters];");
        }
        else
        {
            source.AppendLine("    /// <inheritdoc/>");
            source.AppendLine("    protected override global::System.Collections.Generic.IReadOnlyList<global::OpenIGI.Engine.Tasks.TaskParameter> ScriptParameters");
            source.AppendLine("        => GeneratedScriptParameters;");
        }

        source.AppendLine();
        source.AppendLine("    /// <inheritdoc/>");
        source.AppendLine("    protected override void BindScriptParameters(global::OpenIGI.Engine.Tasks.ScriptArgumentReader reader)");
        source.AppendLine("    {");
        source.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(reader);");
        if (derived)
        {
            source.AppendLine("        base.BindScriptParameters(reader);");
        }

        source.Append(body);
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    /// <inheritdoc/>");
        source.AppendLine("    public override bool TryGetScriptValue(string name, out object? value)");
        source.AppendLine("    {");
        source.AppendLine("        switch (name)");
        source.AppendLine("        {");
        source.Append(lookup);
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine("        return base.TryGetScriptValue(name, out value);");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    /// <inheritdoc/>");
        source.AppendLine("    public override bool TrySetScriptValue(string name, object? value)");
        source.AppendLine("    {");
        source.AppendLine("        switch (name)");
        source.AppendLine("        {");
        source.Append(setter);
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine("        return base.TrySetScriptValue(name, value);");
        source.AppendLine("    }");

        for (int i = 0; i < nesting.Count; i++)
        {
            indent.Length -= 4;
            source.Append(indent).AppendLine("}");
        }

        return source.ToString();
    }

    private static bool TypesMatch(string expected, string actual)
    {
        // The member type comes back fully qualified; the map spells primitives with their keywords.
        string normalized = actual.StartsWith("global::System.", StringComparison.Ordinal)
            ? actual switch
            {
                "global::System.Int32" => "int",
                "global::System.Int16" => "short",
                "global::System.Boolean" => "bool",
                "global::System.Single" => "float",
                "global::System.Double" => "double",
                "global::System.String" => "string",
                "global::System.Single[]" => "float[]",
                _ => actual,
            }
            : actual;

        return string.Equals(expected, normalized, StringComparison.Ordinal);
    }

    private static bool DerivesFromGameTask(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.ToDisplayString(), BaseTypeName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPartial(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            bool partial = current.DeclaringSyntaxReferences
                .Select(static reference => reference.GetSyntax())
                .OfType<TypeDeclarationSyntax>()
                .Any(static declaration => declaration.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)));

            if (!partial)
            {
                return false;
            }
        }

        return true;
    }

    private static string FileNameFor(INamedTypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty)
            .Replace('<', '_')
            .Replace('>', '_')
            .Replace(',', '_')
            .Replace(' ', '_');

    private static string Literal(string value) => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    // A plain class rather than a record: netstandard2.0 has no IsExternalInit, so init accessors
    // (and therefore positional records) do not compile here.
    private sealed class Candidate
    {
        internal Candidate(
            INamedTypeSymbol owner,
            string memberName,
            string declaredName,
            string paramTypeName,
            string memberType,
            bool assignable,
            string filePath,
            int position,
            Location location)
        {
            Owner = owner;
            MemberName = memberName;
            DeclaredName = declaredName;
            ParamTypeName = paramTypeName;
            MemberType = memberType;
            Assignable = assignable;
            FilePath = filePath;
            Position = position;
            Location = location;
        }

        internal INamedTypeSymbol Owner { get; }

        internal string MemberName { get; }

        internal string DeclaredName { get; }

        internal string ParamTypeName { get; }

        internal string MemberType { get; }

        internal bool Assignable { get; }

        internal string FilePath { get; }

        internal int Position { get; }

        internal Location Location { get; }
    }
}
