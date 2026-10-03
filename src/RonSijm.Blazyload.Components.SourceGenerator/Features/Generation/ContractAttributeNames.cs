using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RonSijm.Blazyload.Components.SourceGenerator;

internal static class ContractAttributeNames
{
    public static bool Matches(AttributeSyntax attribute, string expected)
    {
        var name = attribute.Name.ToString().Replace("global::", "");
        var usings = attribute.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().SelectMany(node => node.Usings)
            .Concat(attribute.Ancestors().OfType<CompilationUnitSyntax>().SelectMany(node => node.Usings));
        var alias = usings.FirstOrDefault(directive => directive.Alias?.Name.Identifier.ValueText == name);
        if (alias is not null)
        {
            name = alias.Name!.ToString().Replace("global::", "");
        }

        return name == expected || name == expected + "Attribute" || name == "RonSijm.Blazyload.Components." + expected || name == "RonSijm.Blazyload.Components." + expected + "Attribute";
    }
}
