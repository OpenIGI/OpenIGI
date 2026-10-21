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
/// Emits the service binding for task types, so a level hands its tasks their tables and worlds without a system finding each
/// task and setting it by hand, and without runtime reflection.
/// </summary>
/// <remarks>
/// For each class with <c>[LevelService]</c> properties this generates an override of <c>BindLevelServices</c> that calls the
/// base's and then sets each property the level's services hold a value of its type for. Properties are bound in declaration
/// order; a property the level has no service for keeps its value.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class LevelServiceGenerator : IIncrementalGenerator
{
    private const string AttributeName = "OpenIGI.Engine.Tasks.LevelServiceAttribute";
    private const string BaseTypeName = "OpenIGI.Engine.Tasks.GameTask";

    private static readonly SymbolDisplayFormat ServiceTypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly DiagnosticDescriptor NotATask = new(
        "OIGI008",
        "[LevelService] requires a GameTask",
        "'{0}' has [LevelService] properties but does not derive from GameTask, so no level can bind them",
        "OpenIGI",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NotPartial = new(
        "OIGI009",
        "Task type with level services must be partial",
        "'{0}' has [LevelService] properties, so it must be declared partial for the binding to be generated",
        "OpenIGI",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NotBindable = new(
        "OIGI010",
        "Level service must be a settable reference-type property",
        "'{0}' cannot be bound: a [LevelService] property must have a setter and a reference type",
        "OpenIGI",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<Candidate?> candidates = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeName,
                static (node, _) => node is PropertyDeclarationSyntax,
                static (ctx, _) => Describe(ctx))
            .Where(static candidate => candidate is not null);

        context.RegisterSourceOutput(candidates.Collect(), static (ctx, all) => Emit(ctx, all));
    }

    private static Candidate? Describe(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not IPropertySymbol property || property.ContainingType is not { } owner)
        {
            return null;
        }

        bool bindable = property.SetMethod is not null && property.Type.IsReferenceType && !property.IsStatic;
        string serviceType = property.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(ServiceTypeFormat);
        SyntaxReference? syntax = property.DeclaringSyntaxReferences.FirstOrDefault();
        return new Candidate(
            owner,
            property.Name,
            serviceType,
            bindable,
            syntax?.SyntaxTree.FilePath ?? string.Empty,
            syntax?.Span.Start ?? 0,
            property.Locations.FirstOrDefault() ?? Location.None);
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

        bool failed = false;
        foreach (Candidate member in members.Where(static m => !m.Bindable))
        {
            context.ReportDiagnostic(Diagnostic.Create(NotBindable, member.Location, member.MemberName));
            failed = true;
        }

        if (failed)
        {
            return;
        }

        members.Sort(static (a, b) =>
        {
            int byFile = string.CompareOrdinal(a.FilePath, b.FilePath);
            return byFile != 0 ? byFile : a.Position.CompareTo(b.Position);
        });

        var body = new StringBuilder();
        for (int i = 0; i < members.Count; i++)
        {
            Candidate member = members[i];
            body.Append("        if (services.TryGet<").Append(member.ServiceType).Append(">(out ")
                .Append(member.ServiceType).Append("? service").Append(i).AppendLine("))");
            body.AppendLine("        {");
            body.Append("            this.").Append(member.MemberName).Append(" = service").Append(i).AppendLine(";");
            body.AppendLine("        }");
            if (i < members.Count - 1)
            {
                body.AppendLine();
            }
        }

        context.AddSource($"{FileNameFor(owner)}.LevelServices.g.cs", SourceText.From(Render(owner, body), Encoding.UTF8));
    }

    private static string Render(INamedTypeSymbol owner, StringBuilder body)
    {
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/> Emitted by OpenIGI.Generators.LevelServiceGenerator.");
        source.AppendLine("#nullable enable");
        source.AppendLine();

        if (owner.ContainingNamespace is { IsGlobalNamespace: false })
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

        source.AppendLine("    /// <inheritdoc/>");
        source.AppendLine("    protected override void BindLevelServices(global::OpenIGI.Engine.Titles.IServiceCatalog services)");
        source.AppendLine("    {");
        source.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(services);");
        source.AppendLine("        base.BindLevelServices(services);");
        source.Append(body);
        source.AppendLine("    }");

        for (int i = 0; i < nesting.Count; i++)
        {
            indent.Length -= 4;
            source.Append(indent).AppendLine("}");
        }

        return source.ToString();
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

    // A plain class rather than a record: netstandard2.0 has no IsExternalInit.
    private sealed class Candidate
    {
        internal Candidate(
            INamedTypeSymbol owner,
            string memberName,
            string serviceType,
            bool bindable,
            string filePath,
            int position,
            Location location)
        {
            Owner = owner;
            MemberName = memberName;
            ServiceType = serviceType;
            Bindable = bindable;
            FilePath = filePath;
            Position = position;
            Location = location;
        }

        internal INamedTypeSymbol Owner { get; }

        internal string MemberName { get; }

        internal string ServiceType { get; }

        internal bool Bindable { get; }

        internal string FilePath { get; }

        internal int Position { get; }

        internal Location Location { get; }
    }
}
