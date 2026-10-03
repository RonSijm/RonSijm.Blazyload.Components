using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System.Xml.Linq;
using NSubstitute;
using RonSijm.Blazyload.Components.SourceGenerator;
using RonSijm.Blazyload.Components.SourceGenerator.Build;

namespace RonSijm.Blazyload.Components.Tests;

public sealed class ContractGenerationTests
{
    [Fact]
    public void GeneratesIdentifiersFromCSharpAndRazorWithoutScanningCodeSnippets()
    {
        var result = Generate(
            new SourceInput("Widget.cs", """
                using Contract = RonSijm.Blazyload.Components.BlazyComponentAttribute;
                [Contract("wharf.burger-of-the-day")] public class Widget {}
                [Other.BlazyComponent("ignored.fake")] public class OtherWidget {}
                """),
            new SourceInput("Calendar.razor", """""
                @using RonSijm.Blazyload.Components
                @attribute [BlazyComponent("wharf.events")]
                @* @attribute [BlazyComponent("ignored.comment")] *@
                @code {
                    private const string Snippet = """
                        @attribute [BlazyComponent("wharf.events")]
                        """;
                }
                @functions {
                    private string Example() { return "@attribute [BlazyComponent(\"ignored.string\")]"; }
                }
                """""));
        Assert.Empty(result.Diagnostics);
        var source = result.GeneratedTrees.Single(tree => tree.FilePath.EndsWith("WonderWharfComponents.g.cs")).ToString();
        Assert.Contains("public const string BurgerOfTheDay = \"wharf.burger-of-the-day\";", source);
        Assert.Contains("public const string Events = \"wharf.events\";", source);
        Assert.DoesNotContain("ignored.", source);
    }

    [Theory]
    [InlineData("[BlazyComponent(\"wharf.events\")] class A {} [BlazyComponent(\"other.events\")] class B {}", "BLZCON102")]
    [InlineData("[BlazyComponent(\"wharf.events\")] class A {} [BlazyComponent(\"wharf.events\")] class B {}", "BLZCON102")]
    [InlineData("[BlazyComponent(nameof(A))] class A {}", "BLZCON101")]
    [InlineData("[BlazyComponent(\"\")] class A {}", "BLZCON101")]
    public void InvalidIdentifiersReportBuildErrors(string source, string diagnostic)
    {
        var result = Generate(new SourceInput("Widget.cs", source));
        Assert.Contains(result.Diagnostics, item => item.Id == diagnostic && item.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UnreadableInputReportsAnError()
    {
        var result = Generate(new SourceInput("Missing.cs", null));
        Assert.Contains(result.Diagnostics, item => item.Id == "BLZCON101" && item.GetMessage().Contains("Missing.cs"));
    }

    [Fact]
    public void ProducerCompilationOnlyReceivesTheMarker()
    {
        var compilation = CSharpCompilation.Create("Producer");
        var driver = CSharpGeneratorDriver.Create(new ComponentContractGenerator());
        var result = driver.RunGenerators(compilation, TestContext.Current.CancellationToken).GetRunResult();
        Assert.Single(result.GeneratedTrees);
        Assert.Contains("internal sealed class BlazyContractAttribute", result.GeneratedTrees[0].ToString());
    }

    [Fact]
    public void ExtractsMixedPartialGenericNestedAndAliasedModelsWithoutChangingOriginalFiles()
    {
        WithDirectory(directory =>
        {
            var original = """
                using Shared = RonSijm.Blazyload.Components.BlazyContractAttribute;
                namespace Demo.Models;
                [Shared] public partial record Model<T>(T Value) { public sealed class Nested {} }
                public class Model { }
                public class Implementation { public Model<string>? Value { get; set; } }
                """;
            var first = Path.Combine(directory, "First.cs");
            var second = Path.Combine(directory, "Second.cs");
            File.WriteAllText(first, original);
            File.WriteAllText(second, "namespace Demo.Models; public partial record Model<T> { public string Label => \"shared\"; }");
            var task = CreateTask(directory, first, second);
            Assert.True(task.Execute());
            Assert.Equal(original, File.ReadAllText(first));
            Assert.Equal(2, task.RemovedSources.Length);
            var producer = string.Join("\n", task.ReplacementSources.Select(item => File.ReadAllText(item.ItemSpec)));
            Assert.Contains("public class Model", producer);
            Assert.Contains("public class Implementation", producer);
            Assert.DoesNotContain("partial record Model", producer);
            Assert.Contains("TypeForwardedTo(typeof(global::Demo.Models.Model<>))", producer);
            var models = string.Join("\n", Directory.GetFiles(Path.Combine(task.OutputDirectory, "models")).Select(File.ReadAllText));
            Assert.Contains("namespace Demo.Models;", models);
            Assert.Contains("sealed class Nested", models);
            Assert.Contains("Label => \"shared\"", models);
            Assert.DoesNotContain("[Shared]", models);
            Assert.DoesNotContain("using Shared", models);
            Assert.DoesNotContain("Implementation", models);
        });
    }

    [Theory]
    [InlineData("[BlazyContract] internal record Model;", "must be public")]
    [InlineData("public class Container { [BlazyContract] public class Nested {} }", "enclosing model")]
    public void InvalidModelExportsFailExplicitly(string source, string message)
    {
        WithDirectory(directory =>
        {
            var path = Path.Combine(directory, "Model.cs");
            File.WriteAllText(path, source);
            var task = CreateTask(directory, path);
            Assert.False(task.Execute());
            task.BuildEngine.Received().LogErrorEvent(Arg.Is<BuildErrorEventArgs>(error => error.Message!.Contains(message)));
        });
    }

    [Fact]
    public void ContractPackagePreservesFrameworkSpecificDependencies()
    {
        WithDirectory(directory =>
        {
            var assemblies = new List<TaskItem>();
            foreach (var framework in new[] { "net8.0", "net10.0" })
            {
                var dll = Path.Combine(directory, framework + ".dll");
                var project = Path.Combine(directory, framework + ".csproj");
                File.WriteAllBytes(dll, []);
                File.WriteAllText(project, $"""<Project><ItemGroup><PackageReference Include="Shared.Models" Version="{framework.Substring(3, 1)}.0.0" /></ItemGroup></Project>""");
                var item = new TaskItem(dll);
                item.SetMetadata("TargetFramework", framework);
                item.SetMetadata("ContractsProject", project);
                assemblies.Add(item);
            }

            var task = new PrepareContractsPackage
            {
                BuildEngine = Substitute.For<IBuildEngine>(),
                Assemblies = assemblies.ToArray(),
                OutputDirectory = Path.Combine(directory, "pack"),
                PackageId = "Demo.Contracts",
                PackageOutputPath = directory
            };
            Assert.True(task.Execute());
            var generated = XElement.Load(task.ProjectPath);
            Assert.Equal("net8.0;net10.0", generated.Element("PropertyGroup")!.Element("TargetFrameworks")!.Value);
            var dependencies = generated.Descendants("PackageReference").ToArray();
            Assert.Equal(2, dependencies.Length);
            Assert.Equal("'$(TargetFramework)' == 'net8.0'", dependencies[0].Attribute("Condition")!.Value);
            Assert.Equal("'$(TargetFramework)' == 'net10.0'", dependencies[1].Attribute("Condition")!.Value);
            Assert.Equal(2, generated.Descendants("None").Count());
        });
    }

    private static GeneratorDriverRunResult Generate(params AdditionalText[] files)
    {
        var compilation = CSharpCompilation.Create("Contracts");
        var driver = CSharpGeneratorDriver.Create([new ComponentContractGenerator().AsSourceGenerator()], files, optionsProvider: new ContractOptions());
        return driver.RunGenerators(compilation, TestContext.Current.CancellationToken).GetRunResult();
    }

    private static PrepareContractProject CreateTask(string directory, params string[] files)
    {
        return new PrepareContractProject
        {
            BuildEngine = Substitute.For<IBuildEngine>(),
            Sources = files.Select(path => new TaskItem(path)).ToArray(),
            OutputDirectory = Path.Combine(directory, "output"),
            ProducerAssemblyName = "Demo.WonderWharf",
            TargetFramework = "net10.0",
            GeneratorAssembly = typeof(ComponentContractGenerator).Assembly.Location,
            ContractsNamespace = "Demo.WonderWharf.Contracts",
            ContractsClassName = "WonderWharfComponents",
            PackageId = "Demo.WonderWharf.Contracts",
            PackageOutputPath = directory
        };
    }

    private static void WithDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"BlazyContracts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            action(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class SourceInput(string path, string? text) : AdditionalText
    {
        public override string Path => path;
        public override SourceText? GetText(CancellationToken cancellationToken = default)
        {
            var result = text is null ? null : SourceText.From(text);
            return result;
        }
    }

    private sealed class ContractOptions() : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options();
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        private sealed class Options() : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value)
            {
                value = key switch
                {
                    "build_property.BlazyContractsCompilation" => "true",
                    "build_property.BlazyContractsNamespace" => "Demo.Contracts",
                    "build_property.BlazyContractsClassName" => "WonderWharfComponents",
                    _ => ""
                };
                return value.Length > 0;
            }
        }
    }
}
