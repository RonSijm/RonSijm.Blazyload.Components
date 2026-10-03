using System.Text;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RonSijm.Blazyload.Components.SourceGenerator;

namespace RonSijm.Blazyload.Components.SourceGenerator.Build;

public sealed class PrepareContractProject() : Microsoft.Build.Utilities.Task
{
    [Required]
    public ITaskItem[] Sources { get; set; } = [];

    public ITaskItem[] RazorSources { get; set; } = [];
    public ITaskItem[] PackageReferences { get; set; } = [];
    public ITaskItem[] ProjectReferences { get; set; } = [];

    [Required]
    public string OutputDirectory { get; set; } = null!;

    [Required]
    public string ProducerAssemblyName { get; set; } = null!;

    [Required]
    public string TargetFramework { get; set; } = null!;

    [Required]
    public string GeneratorAssembly { get; set; } = null!;

    public string ContractsNamespace { get; set; } = null!;
    public string ContractsClassName { get; set; } = null!;
    public string PackageId { get; set; } = null!;
    public string Version { get; set; } = "1.0.0";
    public string PackageOutputPath { get; set; } = null!;
    public string DefineConstants { get; set; } = "";

    [Output]
    public string ContractsProject { get; set; } = null!;

    [Output]
    public ITaskItem[] RemovedSources { get; set; } = [];

    [Output]
    public ITaskItem[] ReplacementSources { get; set; } = [];

    [Output]
    public ITaskItem[] WrittenFiles { get; set; } = [];

    public override bool Execute()
    {
        try
        {
            return Prepare();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.LogError($"BLZCON001: Cannot prepare generated contracts: {exception.Message}");
            return false;
        }
    }

    private bool Prepare()
    {
        var directory = Path.GetFullPath(OutputDirectory);
        var symbols = DefineConstants.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim());
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: symbols);
        var sourceFiles = Sources.Select(item => item.GetMetadata("FullPath")).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var files = sourceFiles.Select(path => new SourceFile(path, CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path).GetCompilationUnitRoot())).ToArray();
        var exports = new Dictionary<string, MemberDeclarationSyntax>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            foreach (var declaration in file.Root.DescendantNodes().OfType<MemberDeclarationSyntax>().Where(IsMarked))
            {
                if (declaration is not BaseTypeDeclarationSyntax && declaration is not DelegateDeclarationSyntax)
                {
                    Log.LogError($"BLZCON002: {file.Path}: [BlazyContract] can only mark model type declarations.");
                    continue;
                }

                if (declaration.Ancestors().Any(node => node is BaseTypeDeclarationSyntax))
                {
                    Log.LogError($"BLZCON003: {file.Path}: mark the enclosing model instead of a nested type.");
                    continue;
                }

                if (!GetModifiers(declaration).Any(token => token.IsKind(SyntaxKind.PublicKeyword)))
                {
                    Log.LogError($"BLZCON004: {file.Path}: exported model '{GetName(declaration)}' must be public.");
                    continue;
                }

                exports[GetFullName(declaration)] = declaration;
            }
        }

        if (Log.HasLoggedErrors)
        {
            return false;
        }

        Directory.CreateDirectory(directory);
        var modelSources = new List<string>();
        var removed = new List<ITaskItem>();
        var replacements = new List<ITaskItem>();
        var written = new List<ITaskItem>();
        for (var index = 0; index < files.Length; index++)
        {
            var file = files[index];
            if (!file.Root.DescendantNodes().OfType<MemberDeclarationSyntax>().Any(node => IsTopLevelType(node) && exports.ContainsKey(GetFullName(node))))
            {
                continue;
            }

            var modelRoot = (CompilationUnitSyntax)new ExportRewriter(exports, models: true).Visit(file.Root)!;
            modelRoot = modelRoot.WithAttributeLists(default);
            modelRoot = modelRoot.WithUsings(SyntaxFactory.List(modelRoot.Usings.Where(directive => directive.Name?.ToString() != "RonSijm.Blazyload.Components")));
            var modelPath = Path.Combine(directory, "models", $"{index}-{Path.GetFileName(file.Path)}");
            Write(modelPath, "#nullable enable\n" + modelRoot.NormalizeWhitespace().ToFullString(), written);
            modelSources.Add(modelPath);

            var producerRoot = new ExportRewriter(exports, models: false).Visit(file.Root)!;
            var replacementPath = Path.Combine(directory, "producer", $"{index}-{Path.GetFileName(file.Path)}");
            Write(replacementPath, "#nullable enable\n" + producerRoot.NormalizeWhitespace().ToFullString(), written);
            removed.Add(new TaskItem(Sources.First(item => string.Equals(item.GetMetadata("FullPath"), file.Path, StringComparison.OrdinalIgnoreCase))));
            replacements.Add(new TaskItem(replacementPath));
        }

        var forwarders = new StringBuilder("// <auto-generated />\n");
        foreach (var entry in exports.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var typeName = GetFullName(entry.Value, includeArity: false);
            if (entry.Value is TypeDeclarationSyntax { TypeParameterList: { } parameters })
            {
                typeName += "<" + new string(',', parameters.Parameters.Count - 1) + ">";
            }
            else if (entry.Value is DelegateDeclarationSyntax { TypeParameterList: { } delegateParameters })
            {
                typeName += "<" + new string(',', delegateParameters.Parameters.Count - 1) + ">";
            }

            forwarders.Append("[assembly: global::System.Runtime.CompilerServices.TypeForwardedTo(typeof(global::").Append(typeName).Append("))]\n");
        }

        var forwarderPath = Path.Combine(directory, "producer", "TypeForwarders.g.cs");
        Write(forwarderPath, forwarders.ToString(), written);
        replacements.Add(new TaskItem(forwarderPath));

        var contractAssembly = ProducerAssemblyName + ".Contracts";
        var project = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup",
                Property("TargetFramework", TargetFramework),
                Property("AssemblyName", contractAssembly),
                Property("RootNamespace", ContractsNamespace),
                Property("LangVersion", "latest"),
                Property("Nullable", "enable"),
                Property("ImplicitUsings", "enable"),
                Property("CopyLocalLockFileAssemblies", "true"),
                Property("DefineConstants", DefineConstants),
                Property("EnableDefaultCompileItems", "false"),
                Property("ImportDirectoryBuildProps", "false"),
                Property("ImportDirectoryBuildTargets", "false"),
                Property("BlazyContractsCompilation", "true"),
                Property("BlazyContractsNamespace", ContractsNamespace),
                Property("BlazyContractsClassName", ContractsClassName),
                Property("PackageId", PackageId),
                Property("Version", Version),
                Property("PackageOutputPath", PackageOutputPath),
                Property("Description", $"Generated component identifiers and shared models for {ProducerAssemblyName}."),
                Property("Authors", "Ron Sijm"),
                Property("GenerateDocumentationFile", "true"),
                Property("NoWarn", "$(NoWarn);1591")),
            new XElement("ItemGroup",
                modelSources.Select(path => new XElement("Compile", new XAttribute("Include", path))),
                sourceFiles.Concat(RazorSources.Select(item => item.GetMetadata("FullPath"))).Distinct(StringComparer.OrdinalIgnoreCase).Select(path => new XElement("AdditionalFiles", new XAttribute("Include", path))),
                new XElement("Analyzer", new XAttribute("Include", Path.GetFullPath(GeneratorAssembly))),
                new XElement("CompilerVisibleProperty", new XAttribute("Include", "BlazyContractsCompilation")),
                new XElement("CompilerVisibleProperty", new XAttribute("Include", "BlazyContractsNamespace")),
                new XElement("CompilerVisibleProperty", new XAttribute("Include", "BlazyContractsClassName")),
                PackageReferences.Select(item => new XElement("PackageReference", new XAttribute("Include", item.ItemSpec), new XAttribute("Version", item.GetMetadata("Version")))),
                ProjectReferences.Select(item => new XElement("ProjectReference", new XAttribute("Include", item.GetMetadata("FullPath"))))),
            new XElement("Target", new XAttribute("Name", "GetBlazyContractDependencies"), new XAttribute("DependsOnTargets", "ResolveReferences"), new XAttribute("Returns", "@(ReferenceCopyLocalPaths)")));
        ContractsProject = Path.Combine(directory, contractAssembly + ".csproj");
        Write(ContractsProject, project.ToString(), written);
        RemovedSources = removed.ToArray();
        ReplacementSources = replacements.ToArray();
        WrittenFiles = written.ToArray();
        return true;
    }

    private static XElement Property(string name, string value) => new(name, value);

    private static void Write(string path, string content, List<ITaskItem> written)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path) || File.ReadAllText(path) != content)
        {
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        written.Add(new TaskItem(path));
    }

    private static bool IsTopLevelType(MemberDeclarationSyntax declaration)
    {
        return (declaration is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax) && !declaration.Ancestors().Any(node => node is BaseTypeDeclarationSyntax);
    }

    private static SyntaxTokenList GetModifiers(MemberDeclarationSyntax declaration)
    {
        if (declaration is BaseTypeDeclarationSyntax type)
        {
            return type.Modifiers;
        }

        return ((DelegateDeclarationSyntax)declaration).Modifiers;
    }

    private static string GetName(MemberDeclarationSyntax declaration)
    {
        if (declaration is BaseTypeDeclarationSyntax type)
        {
            return type.Identifier.Text;
        }

        return ((DelegateDeclarationSyntax)declaration).Identifier.Text;
    }

    private static string GetFullName(MemberDeclarationSyntax declaration)
    {
        return GetFullName(declaration, includeArity: true);
    }

    private static string GetFullName(MemberDeclarationSyntax declaration, bool includeArity)
    {
        var namespaces = declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(node => node.Name.ToString());
        var name = GetName(declaration);
        var parameters = declaration switch
        {
            TypeDeclarationSyntax type => type.TypeParameterList,
            DelegateDeclarationSyntax type => type.TypeParameterList,
            _ => null
        };
        if (includeArity && parameters is not null)
        {
            name += "`" + parameters.Parameters.Count;
        }

        return string.Join(".", namespaces.Append(name));
    }

    private static bool IsMarked(MemberDeclarationSyntax declaration)
    {
        return declaration.AttributeLists.SelectMany(list => list.Attributes).Any(IsMarker);
    }

    private static bool IsMarker(AttributeSyntax attribute)
    {
        return ContractAttributeNames.Matches(attribute, "BlazyContract");
    }

    private sealed class SourceFile(string path, CompilationUnitSyntax root)
    {
        public string Path { get; } = path;
        public CompilationUnitSyntax Root { get; } = root;
    }

    private sealed class ExportRewriter(IReadOnlyDictionary<string, MemberDeclarationSyntax> exports, bool models) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node) => Rewrite(node);
        public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node) => Rewrite(node);
        public override SyntaxNode? VisitRecordDeclaration(RecordDeclarationSyntax node) => Rewrite(node);
        public override SyntaxNode? VisitEnumDeclaration(EnumDeclarationSyntax node) => Rewrite(node);
        public override SyntaxNode? VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) => Rewrite(node);
        public override SyntaxNode? VisitDelegateDeclaration(DelegateDeclarationSyntax node) => Rewrite(node);

        public override SyntaxNode? VisitAttribute(AttributeSyntax node)
        {
            if (models && IsMarker(node))
            {
                return null;
            }

            return base.VisitAttribute(node);
        }

        public override SyntaxNode? VisitUsingDirective(UsingDirectiveSyntax node)
        {
            var name = node.Name?.ToString().Replace("global::", "");
            if (models && (name == "RonSijm.Blazyload.Components" || name == "RonSijm.Blazyload.Components.BlazyContractAttribute" || name == "RonSijm.Blazyload.Components.BlazyContract"))
            {
                return null;
            }

            return base.VisitUsingDirective(node);
        }

        public override SyntaxNode? VisitAttributeList(AttributeListSyntax node)
        {
            var result = (AttributeListSyntax)base.VisitAttributeList(node)!;
            if (result.Attributes.Count == 0)
            {
                return null;
            }

            return result;
        }

        private SyntaxNode? Rewrite(MemberDeclarationSyntax node)
        {
            if (IsTopLevelType(node) && exports.ContainsKey(GetFullName(node)) != models)
            {
                return null;
            }

            return node switch
            {
                ClassDeclarationSyntax declaration => base.VisitClassDeclaration(declaration),
                StructDeclarationSyntax declaration => base.VisitStructDeclaration(declaration),
                RecordDeclarationSyntax declaration => base.VisitRecordDeclaration(declaration),
                EnumDeclarationSyntax declaration => base.VisitEnumDeclaration(declaration),
                InterfaceDeclarationSyntax declaration => base.VisitInterfaceDeclaration(declaration),
                DelegateDeclarationSyntax declaration => base.VisitDelegateDeclaration(declaration),
                _ => throw new InvalidOperationException("Unexpected exported declaration.")
            };
        }
    }
}
