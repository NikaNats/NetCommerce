#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace NetCommerce.Kernel.SourceGenerators;

[Generator]
public class StronglyTypedIdGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var provider = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s),
                transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx))
            .Where(static m => m is not null);

        context.RegisterSourceOutput(provider, Execute);
    }

    private static bool IsSyntaxTargetForGeneration(SyntaxNode node)
    {
        return node is RecordDeclarationSyntax record &&
               record.AttributeLists.Count > 0 &&
               record.AttributeLists
                   .SelectMany(al => al.Attributes)
                   .Any(a => a.Name.ToString() == "StronglyTypedId" || a.Name.ToString() == "StronglyTypedIdAttribute");
    }

    private static RecordDeclarationSyntax? GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
    {
        var recordDeclaration = (RecordDeclarationSyntax)context.Node;

        foreach (var attributeList in recordDeclaration.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                if (context.SemanticModel.GetSymbolInfo(attribute).Symbol is IMethodSymbol attributeSymbol &&
                    attributeSymbol.ContainingType.ToDisplayString() == "NetCommerce.Kernel.SourceGenerators.StronglyTypedIdAttribute")
                {
                    return recordDeclaration;
                }
            }
        }

        return null;
    }

    private static void Execute(SourceProductionContext context, RecordDeclarationSyntax? recordDeclaration)
    {
        if (recordDeclaration is null)
            return;

        var recordName = recordDeclaration.Identifier.Text;
        var namespaceName = GetNamespace(recordDeclaration);

        // For now, assume Guid-based IDs
        var source = GenerateStronglyTypedIdSource(namespaceName, recordName);
        context.AddSource($"{recordName}.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static string GetNamespace(RecordDeclarationSyntax recordDeclaration)
    {
        var namespaceDeclaration = recordDeclaration.Ancestors()
            .OfType<NamespaceDeclarationSyntax>()
            .FirstOrDefault();

        return namespaceDeclaration?.Name.ToString() ?? "global";
    }

    private static string GenerateStronglyTypedIdSource(string namespaceName, string recordName)
    {
        return $$"""
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

            namespace {{namespaceName}};

            public partial record struct {{recordName}} : IParsable<{{recordName}}>
            {
                public static {{recordName}} New() => new(Guid.NewGuid());
                public static {{recordName}} Empty => new(Guid.Empty);

                public override string ToString() => Value.ToString();

                // Reflection-Free EF Core Converter (AOT-Gold Standard)
                public class EfValueConverter : ValueConverter<{{recordName}}, Guid>
                {
                    public EfValueConverter()
                        : base(id => id.Value, guid => new {{recordName}}(guid))
                    { }
                }

                public static {{recordName}} Parse(string s, IFormatProvider? provider)
                {
                    return TryParse(s, provider, out var result)
                        ? result
                        : throw new FormatException($"Invalid {{recordName}} format.");
                }

                public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out {{recordName}} result)
                {
                    if (Guid.TryParse(s, out var guid))
                    {
                        result = new {{recordName}}(guid);
                        return true;
                    }
                    result = default;
                    return false;
                }
            }
            """;
    }
}
