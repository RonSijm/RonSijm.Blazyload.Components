using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using RonSijm.Blazyload.Components.Build;
using RonSijm.Demo.BobsBurgers.Components;
using RonSijm.Demo.WonderWharf.Components;
using NSubstitute;

namespace RonSijm.Blazyload.Components.Tests;

public sealed class BuildManifestTests
{
    [Fact]
    public void AggregatesIndependentDomainAssemblies()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"BlazyComponents-{Guid.NewGuid():N}");
        try
        {
            var task = CreateTask(directory, typeof(EventCalendar).Assembly.Location, typeof(RestaurantDashboard).Assembly.Location);
            Assert.True(task.Execute());
            using var document = JsonDocument.Parse(File.ReadAllText(task.ManifestPath));
            var entries = document.RootElement.GetProperty("components");
            Assert.Equal(4, entries.EnumerateObject().Count());
            var linkerDescriptor = File.ReadAllText(task.LinkerDescriptorPath);
            foreach (var (name, type) in new[]
            {
                ("wonderwharf.events", typeof(EventCalendar)),
                ("wonderwharf.rides", typeof(RideSchedule)),
                ("bobsburgers.dashboard", typeof(RestaurantDashboard)),
                ("bobsburgers.burger-of-the-day", typeof(BurgerOfTheDay))
            })
            {
                var entry = entries.GetProperty(name);
                Assert.Equal(type.FullName, entry.GetProperty("type").GetString());
                Assert.Equal(type.Assembly.GetName().Name, entry.GetProperty("assembly").GetString());
                Assert.Contains($"fullname=\"{type.FullName}\"", linkerDescriptor);
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void DuplicateComponentNamesFailBuild()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"BlazyComponents-{Guid.NewGuid():N}");
        var engine = Substitute.For<IBuildEngine>();
        var task = CreateTask(directory, typeof(BuildDuplicateOne).Assembly.Location);
        task.BuildEngine = engine;
        Assert.False(task.Execute());
        engine.Received().LogErrorEvent(Arg.Is<BuildErrorEventArgs>(error => error.Message!.Contains("build.duplicate") && error.Message.Contains(nameof(BuildDuplicateOne)) && error.Message.Contains(nameof(BuildDuplicateTwo))));
        Assert.False(File.Exists(task.ManifestPath));
    }

    private static GenerateBlazyComponentManifest CreateTask(string directory, params string[] assemblies)
    {
        return new GenerateBlazyComponentManifest
        {
            BuildEngine = Substitute.For<IBuildEngine>(),
            Assemblies = assemblies.Select(path => new TaskItem(path)).ToArray(),
            ManifestPath = Path.Combine(directory, "blazy-components.json"),
            LinkerDescriptorPath = Path.Combine(directory, "linker.xml")
        };
    }
}

[BlazyComponentAttribute("build.duplicate")]
public sealed class BuildDuplicateOne : Microsoft.AspNetCore.Components.ComponentBase;

[BlazyComponentAttribute("build.duplicate")]
public sealed class BuildDuplicateTwo : Microsoft.AspNetCore.Components.ComponentBase;
