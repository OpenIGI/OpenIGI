using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace OpenIGI.Generators;

/// <summary>
/// Emits a mod's <c>IOpenIgiMod.Configure</c> from attributes, so a mod says what it changes where it changes it and never
/// registers anything by hand.
/// </summary>
/// <remarks>
/// <para>
/// The class marked <c>[OpenIgiMod]</c> must be <see langword="partial"/>. The generated <c>Configure</c> adds, in this order:
/// the assembly's task classes marked <c>[AddsTask]</c> and <c>[ReplacesTask]</c> (by class name), the class's
/// <c>[ScriptConstant]</c> fields, its <c>[AddsLevelSystem]</c>, <c>[OnGameEvent]</c> and <c>[OnModLoaded]</c> methods (in
/// declaration order), and then calls the class's optional <c>partial void OnConfigure(IModBuilder builder)</c>. It also names
/// the class to the loader with <c>[assembly: OpenIgiMod(typeof(...))]</c>.
/// </para>
/// <para>
/// A member's <c>Title</c> limits it to one game: it is added only while that game is being configured.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class ModGenerator : IIncrementalGenerator
{
    private const string ModAttribute = "OpenIGI.Modding.OpenIgiModAttribute";
    private const string AddsTaskAttribute = "OpenIGI.Modding.AddsTaskAttribute";
    private const string ReplacesTaskAttribute = "OpenIGI.Modding.ReplacesTaskAttribute";
    private const string LevelSystemAttribute = "OpenIGI.Modding.AddsLevelSystemAttribute`1";
    private const string GameEventAttribute = "OpenIGI.Modding.OnGameEventAttribute";
    private const string LoadedAttribute = "OpenIGI.Modding.OnModLoadedAttribute";
    private const string ConstantAttribute = "OpenIGI.Modding.ScriptConstantAttribute";
    private const string GameTaskType = "OpenIGI.Engine.Tasks.GameTask";
    private const string GameEventInterface = "OpenIGI.Engine.Events.IGameEvent";
    private const string ModContextInterface = "OpenIGI.Modding.IModContext";
    private const string Builder = "global::OpenIGI.Modding.IModBuilder";

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    private static readonly DiagnosticDescriptor NotPartial = Rule(
        "OIGI101", "Mod class must be partial", "'{0}' is marked [OpenIgiMod], so it must be declared partial for its Configure to be generated");

    private static readonly DiagnosticDescriptor HandWrittenConfigure = Rule(
        "OIGI102", "Mod class must not write Configure", "'{0}' writes Configure itself; its Configure is generated -- put additions in 'partial void OnConfigure(IModBuilder builder)'");

    private static readonly DiagnosticDescriptor BadLevelSystem = Rule(
        "OIGI103", "Level system method has the wrong shape", "'{0}' must be a method returning void that takes one '{1}'");

    private static readonly DiagnosticDescriptor BadPlacement = Rule(
        "OIGI104", "Level system must be placed once", "'{0}' must name exactly one of Before, After and Replaces");

    private static readonly DiagnosticDescriptor BadListener = Rule(
        "OIGI105", "Game event method has the wrong shape", "'{0}' must be a method returning void that takes one game event struct");

    private static readonly DiagnosticDescriptor BadLoaded = Rule(
        "OIGI106", "Loaded method has the wrong shape", "'{0}' must be a method returning void that takes one IModContext");

    private static readonly DiagnosticDescriptor BadTask = Rule(
        "OIGI107", "Task class cannot be made", "'{0}' must derive from GameTask, not be abstract, and have a public parameterless constructor");

    private static readonly DiagnosticDescriptor BadConstant = Rule(
        "OIGI108", "Script constant must be an integer constant", "'{0}' must be a const int");

    private static readonly DiagnosticDescriptor TwoMods = Rule(
        "OIGI109", "One mod class per assembly", "'{0}' is a second [OpenIgiMod] class; an assembly is one mod");

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<ModClass?> mods = context.SyntaxProvider
            .ForAttributeWithMetadataName(ModAttribute, static (node, _) => node is ClassDeclarationSyntax, static (ctx, _) => DescribeMod(ctx))
            .Where(static mod => mod is not null);

        IncrementalValuesProvider<TaskClass?> added = context.SyntaxProvider
            .ForAttributeWithMetadataName(AddsTaskAttribute, static (node, _) => node is ClassDeclarationSyntax, static (ctx, _) => DescribeTask(ctx, replaces: false))
            .Where(static task => task is not null);

        IncrementalValuesProvider<TaskClass?> replaced = context.SyntaxProvider
            .ForAttributeWithMetadataName(ReplacesTaskAttribute, static (node, _) => node is ClassDeclarationSyntax, static (ctx, _) => DescribeTask(ctx, replaces: true))
            .Where(static task => task is not null);

        var all = mods.Collect().Combine(added.Collect()).Combine(replaced.Collect());
        context.RegisterSourceOutput(all, static (ctx, input) => Emit(ctx, input.Left.Left, input.Left.Right, input.Right));
    }

    private static DiagnosticDescriptor Rule(string id, string title, string message) =>
        new(id, title, message, "OpenIGI", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static ModClass? DescribeMod(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol type)
        {
            return null;
        }

        ModClass mod = new(
            type.ToDisplayString(TypeFormat),
            type.Name,
            type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
            IsPartial(type),
            type.Locations.FirstOrDefault() ?? Location.None);

        if (type.GetMembers("Configure").OfType<IMethodSymbol>().Any(static m => m.DeclaringSyntaxReferences.Length > 0 && m.Parameters.Length == 1))
        {
            mod.Problems.Add(Diagnostic.Create(HandWrittenConfigure, mod.Location, type.Name));
        }

        foreach (ISymbol member in type.GetMembers())
        {
            foreach (AttributeData attribute in member.GetAttributes())
            {
                Describe(mod, member, attribute);
            }
        }

        return mod;
    }

    private static void Describe(ModClass mod, ISymbol member, AttributeData attribute)
    {
        INamedTypeSymbol? attributeClass = attribute.AttributeClass;
        if (attributeClass is null)
        {
            return;
        }

        string name = attributeClass.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted).WithGenericsOptions(SymbolDisplayGenericsOptions.None));
        string metadata = attributeClass.IsGenericType ? name + "`" + attributeClass.Arity : name;
        Location location = member.Locations.FirstOrDefault() ?? Location.None;
        string? title = Named(attribute, "Title");

        switch (metadata)
        {
            case LevelSystemAttribute:
            {
                ITypeSymbol build = attributeClass.TypeArguments[0];
                if (member is not IMethodSymbol method || !method.ReturnsVoid || method.Parameters.Length != 1
                    || !SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, build))
                {
                    mod.Problems.Add(Diagnostic.Create(BadLevelSystem, location, member.Name, build.Name));
                    return;
                }

                string? before = Named(attribute, "Before");
                string? after = Named(attribute, "After");
                string? replaces = Named(attribute, "Replaces");
                int placements = (before is null ? 0 : 1) + (after is null ? 0 : 1) + (replaces is null ? 0 : 1);
                if (placements != 1)
                {
                    mod.Problems.Add(Diagnostic.Create(BadPlacement, location, member.Name));
                    return;
                }

                string verb = before is not null ? "InsertBefore" : after is not null ? "InsertAfter" : "Replace";
                string anchor = before ?? after ?? replaces!;
                string systemName = Named(attribute, "Name") ?? mod.Name + "." + method.Name;
                string buildType = build.ToDisplayString(TypeFormat);
                mod.Steps.Add(new Step(
                    title,
                    $"builder.EditLevelSystems<{buildType}>(systems => systems.{verb}({Literal(anchor)}, " +
                    $"new global::OpenIGI.Game.Level.LevelSystem<{buildType}>({Literal(systemName)}, {method.Name})));"));
                return;
            }

            case GameEventAttribute:
            {
                if (member is not IMethodSymbol method || !method.ReturnsVoid || method.Parameters.Length != 1
                    || method.Parameters[0].Type is not INamedTypeSymbol { IsValueType: true } gameEvent
                    || !gameEvent.AllInterfaces.Any(static i => i.ToDisplayString() == GameEventInterface))
                {
                    mod.Problems.Add(Diagnostic.Create(BadListener, location, member.Name));
                    return;
                }

                string eventType = gameEvent.ToDisplayString(TypeFormat);
                mod.Steps.Add(new Step(title, $"builder.Listen<{eventType}>((in {eventType} gameEvent) => {method.Name}(gameEvent));"));
                return;
            }

            case LoadedAttribute:
            {
                if (member is not IMethodSymbol method || !method.ReturnsVoid || method.Parameters.Length != 1
                    || method.Parameters[0].Type.ToDisplayString() != ModContextInterface)
                {
                    mod.Problems.Add(Diagnostic.Create(BadLoaded, location, member.Name));
                    return;
                }

                mod.Steps.Add(new Step(title, $"builder.OnLoaded({method.Name});"));
                return;
            }

            case ConstantAttribute:
            {
                if (member is not IFieldSymbol { IsConst: true, Type.SpecialType: SpecialType.System_Int32 } field
                    || attribute.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Value is not string constant)
                {
                    mod.Problems.Add(Diagnostic.Create(BadConstant, location, member.Name));
                    return;
                }

                mod.Constants.Add(new Step(null, $"builder.AddConstant({Literal(constant)}, {field.Name});"));
                return;
            }
        }
    }

    private static TaskClass? DescribeTask(GeneratorAttributeSyntaxContext context, bool replaces)
    {
        if (context.TargetSymbol is not INamedTypeSymbol type)
        {
            return null;
        }

        bool makeable = !type.IsAbstract
            && DerivesFrom(type, GameTaskType)
            && type.InstanceConstructors.Any(static c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);
        TaskClass task = new(type.ToDisplayString(TypeFormat), type.Name, makeable, type.Locations.FirstOrDefault() ?? Location.None);
        foreach (AttributeData attribute in context.Attributes)
        {
            if (attribute.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Value is not string typeName)
            {
                continue;
            }

            string factory = $"static () => new {task.QualifiedName}()";
            string call;
            if (!replaces)
            {
                call = $"builder.AddTask({Literal(typeName)}, {factory});";
            }
            else if (attribute.NamedArguments.FirstOrDefault(static a => a.Key == "Adapter").Value.Value is INamedTypeSymbol adapter)
            {
                call = $"builder.ReplaceTask({Literal(typeName)}, {factory}, new {adapter.ToDisplayString(TypeFormat)}());";
            }
            else
            {
                call = $"builder.ReplaceTask({Literal(typeName)}, {factory});";
            }

            task.Steps.Add(new Step(Named(attribute, "Title"), call));
        }

        return task;
    }

    private static void Emit(
        SourceProductionContext context,
        ImmutableArray<ModClass?> mods,
        ImmutableArray<TaskClass?> added,
        ImmutableArray<TaskClass?> replaced)
    {
        List<TaskClass> tasks = [.. added.Concat(replaced).Where(static t => t is not null).Select(static t => t!)];
        bool tasksFailed = false;
        foreach (TaskClass task in tasks.Where(static t => !t.Makeable))
        {
            context.ReportDiagnostic(Diagnostic.Create(BadTask, task.Location, task.Name));
            tasksFailed = true;
        }

        List<ModClass> found = [.. mods.Where(static m => m is not null).Select(static m => m!)];
        if (found.Count == 0)
        {
            return;
        }

        foreach (ModClass extra in found.Skip(1))
        {
            context.ReportDiagnostic(Diagnostic.Create(TwoMods, extra.Location, extra.Name));
        }

        ModClass mod = found[0];
        if (!mod.IsPartial)
        {
            context.ReportDiagnostic(Diagnostic.Create(NotPartial, mod.Location, mod.Name));
            return;
        }

        foreach (Diagnostic problem in mod.Problems)
        {
            context.ReportDiagnostic(problem);
        }

        if (mod.Problems.Count > 0 || tasksFailed || found.Count > 1)
        {
            return;
        }

        // Sorted so the emitted file is byte-identical between builds.
        tasks.Sort(static (a, b) => string.CompareOrdinal(a.QualifiedName, b.QualifiedName));
        StringBuilder source = new();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.AppendLine($"[assembly: global::OpenIGI.Modding.OpenIgiMod(typeof({mod.QualifiedName}))]");
        source.AppendLine();
        if (mod.Namespace is not null)
        {
            source.AppendLine($"namespace {mod.Namespace};");
            source.AppendLine();
        }

        source.AppendLine("[global::System.CodeDom.Compiler.GeneratedCode(\"OpenIGI.Generators\", \"1.0\")]");
        source.AppendLine($"partial class {mod.Name} : global::OpenIGI.Modding.IOpenIgiMod");
        source.AppendLine("{");
        source.AppendLine("    /// <inheritdoc/>");
        source.AppendLine($"    public void Configure({Builder} builder)");
        source.AppendLine("    {");
        source.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(builder);");
        foreach (Step step in tasks.SelectMany(static t => t.Steps).Concat(mod.Constants).Concat(mod.Steps))
        {
            if (step.Title is null)
            {
                source.AppendLine($"        {step.Call}");
            }
            else
            {
                source.AppendLine($"        if (global::System.String.Equals(builder.Title.Id, {Literal(step.Title)}, global::System.StringComparison.OrdinalIgnoreCase))");
                source.AppendLine("        {");
                source.AppendLine($"            {step.Call}");
                source.AppendLine("        }");
            }
        }

        source.AppendLine();
        source.AppendLine("        OnConfigure(builder);");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    /// <summary>Adds what the attributes do not say, after everything they do.</summary>");
        source.AppendLine("    /// <param name=\"builder\">What the mod can change.</param>");
        source.AppendLine($"    partial void OnConfigure({Builder} builder);");
        source.AppendLine("}");

        context.AddSource($"{mod.Name}.Mod.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
    }

    private static string? Named(AttributeData attribute, string name)
    {
        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is string value)
            {
                return value;
            }
        }

        return null;
    }

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    private static bool IsPartial(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences.Any(static r => r.GetSyntax() is ClassDeclarationSyntax c && c.Modifiers.Any(SyntaxKind.PartialKeyword));

    private static bool DerivesFrom(INamedTypeSymbol type, string baseName)
    {
        for (INamedTypeSymbol? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == baseName)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>One line of the generated <c>Configure</c>, and the game it is limited to.</summary>
    /// <remarks>A plain class rather than a record: the analyzer target is netstandard2.0, which has no <c>init</c>.</remarks>
    private sealed class Step(string? title, string call)
    {
        public string? Title { get; } = title;

        public string Call { get; } = call;
    }

    /// <summary>The class marked <c>[OpenIgiMod]</c>, as the generator needs to see it.</summary>
    private sealed class ModClass(string qualifiedName, string name, string? ns, bool isPartial, Location location)
    {
        public string QualifiedName { get; } = qualifiedName;

        public string Name { get; } = name;

        public string? Namespace { get; } = ns;

        public bool IsPartial { get; } = isPartial;

        public Location Location { get; } = location;

        public List<Step> Constants { get; } = [];

        public List<Step> Steps { get; } = [];

        public List<Diagnostic> Problems { get; } = [];
    }

    /// <summary>A task class marked <c>[AddsTask]</c> or <c>[ReplacesTask]</c>.</summary>
    private sealed class TaskClass(string qualifiedName, string name, bool makeable, Location location)
    {
        public string QualifiedName { get; } = qualifiedName;

        public string Name { get; } = name;

        public bool Makeable { get; } = makeable;

        public Location Location { get; } = location;

        public List<Step> Steps { get; } = [];
    }
}
