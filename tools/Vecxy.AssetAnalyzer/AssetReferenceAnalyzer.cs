using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vecxy.AssetAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AssetReferenceAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Failure = new(
        "VECXY001", "Asset reference output failed", "Could not write asset references: {0}",
        "Vecxy.Assets", DiagnosticSeverity.Warning, true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Failure];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var locations = new ConcurrentDictionary<string, ConcurrentBag<ReferenceLocation>>();
            start.RegisterSyntaxNodeAction(node => Analyze(node, locations), Microsoft.CodeAnalysis.CSharp.SyntaxKind.SimpleMemberAccessExpression);
            start.RegisterCompilationEndAction(end => Write(end, locations));
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, ConcurrentDictionary<string, ConcurrentBag<ReferenceLocation>> references)
    {
        var access = (MemberAccessExpressionSyntax)context.Node;
        var symbol = context.SemanticModel.GetSymbolInfo(access).Symbol as IPropertySymbol;
        var attribute = symbol?.GetAttributes().FirstOrDefault(x => x.AttributeClass?.ToDisplayString() == "Vecxy.Assets.AssetReferenceAttribute");
        if (attribute is null || attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Value is not string id) return;
        var span = access.GetLocation().GetLineSpan();
        references.GetOrAdd(id, _ => new()).Add(new ReferenceLocation
        {
            File = span.Path.Replace('\\', '/'), Line = span.StartLinePosition.Line + 1
        });
    }

    private static void Write(CompilationAnalysisContext context, ConcurrentDictionary<string, ConcurrentBag<ReferenceLocation>> references)
    {
        try
        {
            context.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.MSBuildProjectDirectory", out var projectDirectory);
            if (string.IsNullOrWhiteSpace(projectDirectory)) return;
            var directory = Path.Combine(projectDirectory, "obj");
            Directory.CreateDirectory(directory);
            var data = references.ToDictionary(x => x.Key, x => x.Value.OrderBy(y => y.File).ThenBy(y => y.Line).ToArray());
            File.WriteAllText(Path.Combine(directory, "vecxy.asset.references.json"), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            context.ReportDiagnostic(Diagnostic.Create(Failure, Location.None, exception.Message));
        }
    }

    private sealed class ReferenceLocation { public string File { get; set; } = ""; public int Line { get; set; } }
}
