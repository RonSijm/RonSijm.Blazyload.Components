using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace RonSijm.Blazyload.Components.SourceGenerator.Build;

public sealed class PrepareContractsPackage() : Microsoft.Build.Utilities.Task
{
    [Required]
    public ITaskItem[] Assemblies { get; set; } = [];

    [Required]
    public string OutputDirectory { get; set; } = null!;

    [Required]
    public string PackageId { get; set; } = null!;

    public string Version { get; set; } = "1.0.0";
    public string Authors { get; set; } = "";
    public string PackageOutputPath { get; set; } = null!;

    [Output]
    public string ProjectPath { get; set; } = null!;

    [Output]
    public ITaskItem[] WrittenFiles { get; set; } = [];

    public override bool Execute()
    {
        try
        {
            if (Assemblies.Length == 0 || Assemblies.Any(item => !File.Exists(item.ItemSpec) || !File.Exists(item.GetMetadata("ContractsProject")) || string.IsNullOrEmpty(item.GetMetadata("TargetFramework"))))
            {
                Log.LogError("BLZCON007: Contracts packaging requires a built contracts assembly for every target framework.");
                return false;
            }

            var files = new XElement("ItemGroup");
            var dependencies = new XElement("ItemGroup");
            foreach (var assembly in Assemblies.DistinctBy(item => item.ItemSpec))
            {
                var framework = assembly.GetMetadata("TargetFramework");
                foreach (var path in new[] { assembly.ItemSpec, Path.ChangeExtension(assembly.ItemSpec, ".xml") }.Where(File.Exists))
                {
                    files.Add(new XElement("None", new XAttribute("Include", path), new XAttribute("Pack", "true"), new XAttribute("PackagePath", $"lib/{framework}")));
                }

                var contractProject = XElement.Load(assembly.GetMetadata("ContractsProject"));
                foreach (var reference in contractProject.Elements("ItemGroup").Elements().Where(item => item.Name == "PackageReference" || item.Name == "ProjectReference"))
                {
                    var dependency = new XElement(reference);
                    dependency.SetAttributeValue("Condition", $"'$(TargetFramework)' == '{framework}'");
                    dependencies.Add(dependency);
                }
            }

            var project = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup",
                    Property("TargetFrameworks", string.Join(";", Assemblies.Select(item => item.GetMetadata("TargetFramework")).Distinct())),
                    Property("PackageId", PackageId),
                    Property("Version", Version),
                    Property("Authors", Authors),
                    Property("Description", $"Generated component identifiers and shared models for {PackageId}."),
                    Property("PackageOutputPath", Path.GetFullPath(PackageOutputPath)),
                    Property("IncludeBuildOutput", "false"),
                    Property("EnableDefaultItems", "false"),
                    Property("ImportDirectoryBuildProps", "false"),
                    Property("ImportDirectoryBuildTargets", "false")),
                files, dependencies);
            Directory.CreateDirectory(OutputDirectory);
            ProjectPath = Path.GetFullPath(Path.Combine(OutputDirectory, PackageId + ".csproj"));
            var text = project.ToString();
            if (!File.Exists(ProjectPath) || File.ReadAllText(ProjectPath) != text)
            {
                File.WriteAllText(ProjectPath, text);
            }

            WrittenFiles = [new TaskItem(ProjectPath)];
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.LogError($"BLZCON008: Cannot prepare contracts package: {exception.Message}");
            return false;
        }
    }

    private static XElement Property(string name, string value) => new(name, value);
}
